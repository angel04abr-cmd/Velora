// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Velora.Updates;

namespace Velora.Tests;

public class UpdateTests
{
    private const string Owner = "yo", Repo = "velora";
    private const string Base = "https://github.com/yo/velora/releases/download/";

    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Status, byte[] Body)> Routes { get; } = new();
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (!Routes.TryGetValue(url, out var r)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(r.Status) { Content = new ByteArrayContent(r.Body) });
        }
    }

    private static string ReleaseJson(string tag, bool draft = false, bool pre = false, bool withMsi = true, bool withHash = true, string notes = "Notas de la versión")
    {
        string v = tag.TrimStart('v');
        var assets = new List<object>();
        if (withMsi) assets.Add(new { name = $"Velora-{v}-x64.msi", browser_download_url = $"{Base}{tag}/Velora-{v}-x64.msi", size = 1000 });
        if (withHash) assets.Add(new { name = $"Velora-{v}-x64.msi.sha256", browser_download_url = $"{Base}{tag}/Velora-{v}-x64.msi.sha256", size = 90 });
        assets.Add(new { name = $"Velora-{v}-win-x64.zip", browser_download_url = $"{Base}{tag}/Velora-{v}-win-x64.zip", size = 5000 });
        return JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease = pre, body = notes, html_url = $"https://github.com/yo/velora/releases/tag/{tag}", assets });
    }

    private static (UpdateClient Client, FakeServer Server) Make(string? releaseJson)
    {
        var server = new FakeServer();
        if (releaseJson != null) server.Routes["https://api.github.com/repos/yo/velora/releases/latest"] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(releaseJson));
        return (new UpdateClient(new HttpClient(server), Owner, Repo), server);
    }

    [Fact]
    public async Task Detecta_una_version_mas_nueva_con_su_instalador_y_su_huella()
    {
        var (client, _) = Make(ReleaseJson("v1.2.0"));
        var info = await client.CheckAsync(new Version(1, 1, 0));
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 2, 0), info.Version);
        Assert.Equal("Velora-1.2.0-x64.msi", info.MsiName);
        Assert.Equal(Base + "v1.2.0/Velora-1.2.0-x64.msi", info.MsiUrl);
        Assert.EndsWith(".msi.sha256", info.HashUrl);
        Assert.Equal("Notas de la versión", info.Notes);
    }

    [Theory]
    [InlineData("v1.1.0")] // la misma
    [InlineData("v1.0.9")] // más antigua: nunca se baja de versión
    public async Task No_hay_actualizacion_si_la_publicada_no_es_mayor(string tag)
    {
        var (client, _) = Make(ReleaseJson(tag));
        Assert.Null(await client.CheckAsync(new Version(1, 1, 0)));
    }

    [Fact]
    public async Task Ignora_borradores_y_versiones_de_prueba()
    {
        Assert.Null(await Make(ReleaseJson("v2.0.0", draft: true)).Client.CheckAsync(new Version(1, 0, 0)));
        Assert.Null(await Make(ReleaseJson("v2.0.0", pre: true)).Client.CheckAsync(new Version(1, 0, 0)));
    }

    [Fact]
    public async Task Sin_instalador_en_la_version_no_hay_actualizacion()
    {
        Assert.Null(await Make(ReleaseJson("v2.0.0", withMsi: false)).Client.CheckAsync(new Version(1, 0, 0)));
    }

    [Fact]
    public async Task Sin_ninguna_version_publicada_no_hay_error()
    {
        var (client, _) = Make(null); // el servidor responde 404
        Assert.Null(await client.CheckAsync(new Version(1, 0, 0)));
    }

    private static async Task<(UpdateClient Client, UpdateInfo Info, string Dir)> PrepareDownload(byte[] msi, string hashText, bool withHash = true)
    {
        var (client, server) = Make(ReleaseJson("v1.3.0", withHash: withHash));
        server.Routes[$"{Base}v1.3.0/Velora-1.3.0-x64.msi"] = (HttpStatusCode.OK, msi);
        server.Routes[$"{Base}v1.3.0/Velora-1.3.0-x64.msi.sha256"] = (HttpStatusCode.OK, Encoding.ASCII.GetBytes(hashText));
        var info = (await client.CheckAsync(new Version(1, 0, 0)))!;
        string dir = Path.Combine(Path.GetTempPath(), "velora-tests-upd-" + Guid.NewGuid().ToString("N"));
        return (client, info, dir);
    }

    [Fact]
    public async Task Descarga_y_comprueba_la_huella()
    {
        byte[] msi = new byte[250_000];
        new Random(1).NextBytes(msi);
        string hash = Convert.ToHexString(SHA256.HashData(msi)).ToLowerInvariant();
        var (client, info, dir) = await PrepareDownload(msi, hash + "  Velora-1.3.0-x64.msi\n");
        var progress = new List<double>();
        string path = await client.DownloadAsync(info, dir, new Progress<double>(progress.Add));
        Assert.True(File.Exists(path));
        Assert.Equal(msi, await File.ReadAllBytesAsync(path));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Si_la_huella_no_coincide_no_se_instala_y_se_borra_la_descarga()
    {
        byte[] msi = [1, 2, 3, 4, 5, 6, 7, 8];
        var (client, info, dir) = await PrepareDownload(msi, new string('a', 64));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(info, dir));
        Assert.Contains("no coincide", ex.Message);
        Assert.False(File.Exists(Path.Combine(dir, info.MsiName)));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Sin_archivo_de_huella_se_rechaza_la_descarga()
    {
        var (client, info, dir) = await PrepareDownload([1, 2, 3], new string('a', 64), withHash: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(info, dir));
    }

    [Fact]
    public async Task Una_huella_con_formato_incorrecto_se_rechaza()
    {
        var (client, info, dir) = await PrepareDownload([1, 2, 3], "esto no es una huella");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(info, dir));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    [Theory]
    [InlineData("https://evil.example.com/Velora-1.3.0-x64.msi")]
    [InlineData("https://github.com/otro/repo/releases/download/v1.3.0/Velora-1.3.0-x64.msi")]
    [InlineData("http://github.com/yo/velora/releases/download/v1.3.0/Velora-1.3.0-x64.msi")]
    public async Task Solo_se_descarga_desde_las_releases_oficiales(string url)
    {
        var (client, _) = Make(null);
        var info = new UpdateInfo(new Version(1, 3, 0), "v1.3.0", "", "", "Velora-1.3.0-x64.msi", url, 10, Base + "v1.3.0/Velora-1.3.0-x64.msi.sha256");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(info, Path.Combine(Path.GetTempPath(), "velora-tests-nunca")));
    }

    [Fact]
    public async Task El_nombre_del_instalador_no_puede_salirse_de_la_carpeta()
    {
        var (client, _) = Make(null);
        var info = new UpdateInfo(new Version(1, 3, 0), "v1.3.0", "", "", @"..\..\malo.msi", Base + "v1.3.0/x.msi", 10, Base + "v1.3.0/x.msi.sha256");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DownloadAsync(info, Path.Combine(Path.GetTempPath(), "velora-tests-nunca")));
    }

    [Fact]
    public void El_script_espera_instala_y_reabre_con_el_documento()
    {
        string s = UpdateClient.BuildApplyScript(@"C:\Temp\Velora-1.3.0-x64.msi", @"C:\Program Files\Velora\Velora.exe", 4242, "1.3.0", @"C:\Docs\100%.pdf");
        Assert.Contains("tasklist /FI \"PID eq 4242\"", s);
        Assert.Contains(@"msiexec /i ""C:\Temp\Velora-1.3.0-x64.msi"" /passive /norestart", s);
        Assert.Contains(@"start """" ""C:\Program Files\Velora\Velora.exe"" --actualizado 1.3.0 ""C:\Docs\100%%.pdf""", s); // el % se duplica en un .cmd
        Assert.True(s.IndexOf("msiexec", StringComparison.Ordinal) > s.IndexOf("tasklist", StringComparison.Ordinal));
        // si la instalación falla o se cancela, se reabre sin el aviso de «actualizado»
        Assert.Contains("if %errorlevel%==0 goto bien", s);
        Assert.Contains(@"start """" ""C:\Program Files\Velora\Velora.exe"" ""C:\Docs\100%%.pdf""", s);
    }
}
