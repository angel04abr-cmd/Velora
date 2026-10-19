// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Text.Json;

namespace Velora.App;

public sealed class RecentFile
{
    public string Path { get; set; } = "";
    public int Page { get; set; }
    public string ZoomMode { get; set; } = "FitWidth";
    public double Zoom { get; set; } = 1.0;
    public int Rotation { get; set; }
    public DateTime Opened { get; set; } = DateTime.Now;
}

/// <summary>Ajustes y archivos recientes, en %APPDATA%\Velora\settings.json (o en la carpeta de VELORA_DATA_DIR).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public List<RecentFile> Recent { get; set; } = new();
    public bool SidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 250;
    public string SidebarTab { get; set; } = "thumbs";
    public string ZoomMode { get; set; } = "FitWidth";
    public double Zoom { get; set; } = 1.0;
    public int Columns { get; set; } = 1;
    public bool CoverAlone { get; set; }
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public bool Maximized { get; set; }
    public uint MarkupColor { get; set; } = 0xFFFFEB3B;
    public uint InkColor { get; set; } = 0xFFE53935;

    public static string DataDir
    {
        get
        {
            string? o = Environment.GetEnvironmentVariable("VELORA_DATA_DIR");
            return !string.IsNullOrWhiteSpace(o) ? o : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Velora");
        }
    }

    private static string FilePath => System.IO.Path.Combine(DataDir, "settings.json");

    /// <summary>Carpeta de ajustes del nombre anterior del programa («Med»+«PDF»), de la que se copian los ajustes la primera vez.</summary>
    private static string LegacyDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Med" + "PDF");

    public static AppSettings Load()
    {
        try
        {
            string legacy = System.IO.Path.Combine(LegacyDir, "settings.json");
            if (!File.Exists(FilePath) && File.Exists(legacy) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELORA_DATA_DIR")))
            {
                Directory.CreateDirectory(DataDir);
                File.Copy(legacy, FilePath);
            }

            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    public RecentFile? Find(string path) =>
        Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    public void Touch(RecentFile entry)
    {
        Recent.RemoveAll(r => string.Equals(r.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        entry.Opened = DateTime.Now;
        Recent.Insert(0, entry);
        if (Recent.Count > 20) Recent.RemoveRange(20, Recent.Count - 20);
    }
}

internal static class ErrorLog
{
    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataDir);
            File.AppendAllText(System.IO.Path.Combine(AppSettings.DataDir, "errores.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // No hay dónde anotarlo; no se puede hacer más.
        }
    }
}
