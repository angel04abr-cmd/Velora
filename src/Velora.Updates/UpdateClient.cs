// SPDX-License-Identifier: GPL-3.0-or-later
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Velora.Updates;

/// <summary>Una versión más nueva publicada en GitHub.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string MsiName, string MsiUrl, long MsiSize, string? HashUrl);

/// <summary>
/// Actualizaciones desde las «Releases» de GitHub. Se consulta la última versión publicada; si es mayor que la instalada se descarga
/// el instalador (.msi), se comprueba su huella SHA-256 con la que acompaña a la versión y se deja listo para ejecutarlo.
/// No depende de WPF para poder probarse con un servidor falso.
/// </summary>
public sealed class UpdateClient
{
    private static readonly Regex MsiName = new(@"^Velora-(?<v>\d+\.\d+\.\d+)-x64\.msi$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;

    public UpdateClient(HttpClient http, string owner, string repo)
    {
        _http = http;
        Owner = owner;
        Repo = repo;
    }

    public string Owner { get; }
    public string Repo { get; }
    public string RepositoryUrl => $"https://github.com/{Owner}/{Repo}";
    private string DownloadPrefix => $"https://github.com/{Owner}/{Repo}/releases/download/";

    /// <summary>Devuelve la versión nueva, o null si ya se tiene la última (o no hay nada publicado).</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) return null;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= current) return null;

        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepositoryUrl : RepositoryUrl;
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? "", Size: a.GetProperty("size").GetInt64()))
            .ToList();

        var msi = assets.FirstOrDefault(a => MsiName.Match(a.Name) is { Success: true } m && m.Groups["v"].Value == version.ToString());
        if (msi.Name == null) return null; // la versión no trae su instalador
        var hash = assets.FirstOrDefault(a => a.Name.Equals(msi.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        return new UpdateInfo(version, tag, notes, page, msi.Name, msi.Url, msi.Size, hash.Name == null ? null : hash.Url);
    }

    private void RequireOfficialUrl(string url)
    {
        if (!url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La dirección de descarga no es la oficial de Velora.");
    }

    /// <summary>Descarga el instalador a <paramref name="workDir"/>, comprueba su huella SHA-256 y devuelve su ruta. Si la huella no coincide, lanza una excepción y borra el archivo.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string workDir, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        RequireOfficialUrl(info.MsiUrl);
        if (info.HashUrl == null) throw new InvalidOperationException("La versión no trae su huella SHA-256: no se puede comprobar la descarga.");
        RequireOfficialUrl(info.HashUrl);
        if (!MsiName.IsMatch(info.MsiName)) throw new InvalidOperationException("El nombre del instalador no es válido.");

        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, info.MsiName);

        string expected;
        using (var hr = await _http.GetAsync(info.HashUrl, ct))
        {
            hr.EnsureSuccessStatusCode();
            string text = (await hr.Content.ReadAsStringAsync(ct)).Trim();
            expected = text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!Regex.IsMatch(expected, "^[0-9a-fA-F]{64}$")) throw new InvalidOperationException("El archivo de huella SHA-256 no es válido.");
        }

        using (var response = await _http.GetAsync(info.MsiUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.MsiSize;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(path)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException("La huella SHA-256 de la descarga no coincide: el archivo está dañado o fue alterado. No se instala.");
        }

        return path;
    }

    /// <summary>
    /// Script (.cmd) que espera a que Velora se cierre, instala el MSI (Windows pedirá permiso de administrador) y vuelve a abrir Velora,
    /// con el mismo documento y, si la instalación terminó bien, con el aviso de que se actualizó.
    /// </summary>
    public static string BuildApplyScript(string msiPath, string exePath, int waitForPid, string version, string? openFile)
    {
        static string Q(string s) => s.Replace("%", "%%");
        string file = string.IsNullOrEmpty(openFile) ? "" : $" \"{Q(openFile)}\"";
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append("chcp 65001 >nul\r\n");
        sb.Append("for /l %%i in (1,1,60) do (\r\n");
        sb.Append($"  tasklist /FI \"PID eq {waitForPid}\" 2>nul | find \"{waitForPid}\" >nul || goto instalar\r\n");
        sb.Append("  ping -n 2 127.0.0.1 >nul\r\n");
        sb.Append(")\r\n");
        sb.Append(":instalar\r\n");
        sb.Append($"msiexec /i \"{Q(msiPath)}\" /passive /norestart\r\n");
        sb.Append("if %errorlevel%==0 goto bien\r\n");
        sb.Append("if %errorlevel%==3010 goto bien\r\n");
        sb.Append($"start \"\" \"{Q(exePath)}\"{file}\r\n");
        sb.Append("goto fin\r\n");
        sb.Append(":bien\r\n");
        sb.Append($"start \"\" \"{Q(exePath)}\" --actualizado {version}{file}\r\n");
        sb.Append(":fin\r\n");
        return sb.ToString();
    }
}
