// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using Velora.Updates;

namespace Velora.App;

/// <summary>
/// Pegamento entre la aplicación y <see cref="UpdateClient"/>: versión actual, si esta copia está instalada con el MSI (y por tanto
/// puede actualizarse sola) y el arranque del script que instala la versión nueva.
/// </summary>
internal static class UpdateService
{
    public const string Owner = "angel04abr-cmd";
    public const string Repo = "Velora";
    public const string RepositoryUrl = "https://github.com/" + Owner + "/" + Repo;

    private static readonly HttpClient Http = CreateClient();
    private static readonly UpdateClient Client = new(Http, Owner, Repo);

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

    /// <summary>
    /// El instalador MSI deja en <c>HKLM\Software\Velora</c> la marca «Installer = msi» y la carpeta de instalación. Solo la copia que coincide
    /// con esa carpeta puede actualizarse sola; una copia portátil (del .zip) abre la página de descargas.
    /// </summary>
    public static bool CanInstallInPlace
    {
        get
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = hklm.OpenSubKey(@"Software\Velora");
                if (k?.GetValue("Installer") as string != "msi") return false;
                string? dir = k.GetValue("InstallDir") as string;
                return dir != null && Path.GetFullPath(dir).TrimEnd('\\').Equals(Path.GetFullPath(InstallDirectory), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) => Client.CheckAsync(CurrentVersion, ct);

    private static string WorkRoot => Path.Combine(Path.GetTempPath(), "Velora-update");

    /// <summary>Descarga el instalador de la versión indicada y comprueba su huella. Devuelve la ruta del .msi.</summary>
    public static Task<string> PrepareAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct = default) =>
        Client.DownloadAsync(info, Path.Combine(WorkRoot, info.Version.ToString()), progress, ct);

    /// <summary>Borra descargas antiguas de actualizaciones (se llama al arrancar).</summary>
    public static void CleanOldDownloads()
    {
        try
        {
            if (!Directory.Exists(WorkRoot)) return;
            foreach (var d in Directory.EnumerateDirectories(WorkRoot))
                if (Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-1)) Directory.Delete(d, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // en uso o sin permiso: se intentará otro día
        }
    }

    /// <summary>
    /// Lanza (oculto) el script que espera a que Velora se cierre, instala el MSI y la vuelve a abrir.
    /// Quien llama debe cerrar la aplicación justo después.
    /// </summary>
    public static void LaunchApply(string msiPath, UpdateInfo info, string? reopenFile)
    {
        string exe = Path.Combine(InstallDirectory, "Velora.exe");
        string script = UpdateClient.BuildApplyScript(msiPath, exe, Environment.ProcessId, info.Version.ToString(), reopenFile);
        string file = Path.Combine(Path.GetDirectoryName(msiPath)!, "aplicar-actualizacion.cmd");
        File.WriteAllText(file, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{file}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(msiPath)!,
        });
    }
}
