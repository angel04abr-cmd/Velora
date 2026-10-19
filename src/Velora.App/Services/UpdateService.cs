// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Velora.App;

/// <summary>Una versión más nueva publicada en GitHub.</summary>
internal sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string ZipName, string ZipUrl, long ZipSize, string? HashUrl);

/// <summary>
/// Actualizaciones desde las «Releases» de GitHub. Flujo: se consulta la última versión publicada; si es mayor que la instalada,
/// se descarga el .zip, se comprueba su huella SHA-256 con la que acompaña a la versión, se descomprime en una carpeta temporal y se
/// lanza el instalador (<c>Instalar.ps1</c>), que espera a que Velora se cierre, reemplaza los archivos y la vuelve a abrir.
/// </summary>
internal static class UpdateService
{
    public const string Owner = "angel04abr-cmd";
    public const string Repo = "Velora";
    public const string RepositoryUrl = "https://github.com/" + Owner + "/" + Repo;

    private static readonly Regex AssetName = new(@"^Velora-(?<v>\d+\.\d+\.\d+)-win-x64(?<ind>-independiente)?\.zip$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Velora", CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>Versión de este programa (en las pruebas se puede simular otra con VELORA_FAKE_VERSION).</summary>
    public static Version CurrentVersion
    {
        get
        {
            string? fake = Environment.GetEnvironmentVariable("VELORA_FAKE_VERSION");
            if (Version.TryParse(fake, out var f)) return f;
            string? v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (v != null && Version.TryParse(v.Split('+', '-')[0], out var parsed)) return parsed;
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        }
    }

    public static string InstallDirectory => AppContext.BaseDirectory.TrimEnd('\\', '/');

    private static string DefaultInstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Velora");

    private static bool Sandbox => Environment.GetEnvironmentVariable("VELORA_UPDATE_SANDBOX") == "1";

    /// <summary>Solo se actualiza «en el sitio» la copia instalada con Instalar.ps1; una copia portátil abre la página de descargas.</summary>
    public static bool CanInstallInPlace =>
        Sandbox || string.Equals(InstallDirectory, DefaultInstallDirectory, StringComparison.OrdinalIgnoreCase);

    private static bool IsSelfContained => File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    // ------------------------------------------------------------------ consulta

    /// <summary>Devuelve la versión nueva, o null si ya se tiene la última.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // aún no hay ninguna versión publicada
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pr) && pr.GetBoolean()) return null;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        if (version <= CurrentVersion) return null;

        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepositoryUrl : RepositoryUrl;
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? "", Size: a.GetProperty("size").GetInt64()))
            .ToList();

        bool wantIndependent = IsSelfContained;
        var zip = assets.FirstOrDefault(a => AssetName.Match(a.Name) is { Success: true } m && m.Groups["ind"].Success == wantIndependent && m.Groups["v"].Value == version.ToString());
        if (zip.Name == null) return null; // la versión no trae el paquete que corresponde a este equipo
        var hash = assets.FirstOrDefault(a => a.Name.Equals(zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        return new UpdateInfo(version, tag, notes, page, zip.Name, zip.Url, zip.Size, hash.Name == null ? null : hash.Url);
    }

    private static void RequireOfficialUrl(string url)
    {
        string prefix = $"https://github.com/{Owner}/{Repo}/releases/download/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La dirección de descarga no es la oficial de Velora.");
    }

    // ------------------------------------------------------------------ descarga y preparación

    /// <summary>Descarga el paquete, comprueba su huella y lo descomprime. Devuelve la carpeta que contiene <c>Instalar.ps1</c>.</summary>
    public static async Task<string> PrepareAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        RequireOfficialUrl(info.ZipUrl);
        if (info.HashUrl == null) throw new InvalidOperationException("La versión no trae su huella SHA-256: no se puede comprobar la descarga.");
        RequireOfficialUrl(info.HashUrl);

        string work = Path.Combine(Path.GetTempPath(), "Velora-update", info.Version.ToString());
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        string zipPath = Path.Combine(work, info.ZipName);

        string expected;
        using (var hr = await Http.GetAsync(info.HashUrl, ct))
        {
            hr.EnsureSuccessStatusCode();
            string text = (await hr.Content.ReadAsStringAsync(ct)).Trim();
            expected = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (!Regex.IsMatch(expected, "^[0-9a-fA-F]{64}$")) throw new InvalidOperationException("El archivo de huella SHA-256 no es válido.");
        }

        using (var response = await Http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.ZipSize;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(zipPath)) actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La huella SHA-256 de la descarga no coincide: el archivo está dañado o fue alterado. No se instala.");

        string extract = Path.Combine(work, "contenido");
        SafeExtract(zipPath, extract);
        string script = Directory.EnumerateFiles(extract, "Instalar.ps1", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("El paquete descargado no contiene el instalador.");
        return Path.GetDirectoryName(script)!;
    }

    /// <summary>Descomprime comprobando que ninguna entrada se sale de la carpeta de destino.</summary>
    private static void SafeExtract(string zipPath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("El paquete contiene una ruta no permitida.");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>Borra descargas antiguas de actualizaciones (se llama al arrancar).</summary>
    public static void CleanOldDownloads()
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "Velora-update");
            if (!Directory.Exists(dir)) return;
            foreach (var d in Directory.EnumerateDirectories(dir))
                if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-1)) Directory.Delete(d, true);
        }
        catch (IOException)
        {
            // en uso o sin permiso: se intentará otro día
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------------ instalación

    /// <summary>Lanza el instalador de la versión descargada. Velora debe cerrarse justo después para que pueda reemplazar sus archivos.</summary>
    public static void LaunchInstaller(string extractedDir, string? reopenFile)
    {
        string script = Path.Combine(extractedDir, "Instalar.ps1");
        string log = Path.Combine(Path.GetTempPath(), "Velora-update", "instalacion.log");
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = extractedDir,
        };
        foreach (string a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script, "-Silencioso",
                     "-EsperarProceso", Environment.ProcessId.ToString(), "-Destino", InstallDirectory, "-Reabrir", "-Registro", log })
            psi.ArgumentList.Add(a);
        if (!string.IsNullOrEmpty(reopenFile))
        {
            psi.ArgumentList.Add("-AbrirArchivo");
            psi.ArgumentList.Add(reopenFile);
        }

        if (Sandbox)
        {
            psi.ArgumentList.Add("-SinRegistro");
            psi.ArgumentList.Add("-SinAccesoDirecto");
        }

        Process.Start(psi);
    }
}
