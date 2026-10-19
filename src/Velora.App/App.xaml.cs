// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Windows;

namespace Velora.App;

public partial class App : Application
{
    private static readonly List<(string Label, double Ms)> Marks = new();

    /// <summary>Anota un hito del arranque (milisegundos desde que se lanzó el programa), para medir la velocidad de inicio.</summary>
    internal static void Mark(string label)
    {
        try
        {
            Marks.Add((label, (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds));
        }
        catch
        {
            // sin datos de arranque
        }
    }

    internal static string MarksText() => string.Join(", ", Marks.Select(m => $"{m.Label} {m.Ms:0} ms"));

    protected override void OnStartup(StartupEventArgs e)
    {
        Mark("OnStartup");
        // Todo el programa está en español, incluidos los textos que pone el propio WPF (menús de copiar y pegar, etc.).
        var es = new System.Globalization.CultureInfo("es-ES");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = es;
        System.Globalization.CultureInfo.CurrentUICulture = es;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("es-ES")));

        base.OnStartup(e);
        var args = LaunchArgs.Parse(e.Args);
        if (args.DataDir != null) Environment.SetEnvironmentVariable("VELORA_DATA_DIR", args.DataDir);
        if (args.Theme != null) ThemeMode = args.Theme == "oscuro" ? ThemeMode.Dark : args.Theme == "claro" ? ThemeMode.Light : ThemeMode.System;

        DispatcherUnhandledException += (_, ev) =>
        {
            // Un error inesperado no debe cerrar el programa: se anota y se avisa.
            ErrorLog.Write(ev.Exception);
            try
            {
                if (MainWindow is MainWindow w) w.ReportError(ev.Exception);
                ev.Handled = true;
            }
            catch
            {
                // sin ventana donde avisar
            }
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            ErrorLog.Write(ev.Exception);
            ev.SetObserved();
        };

        if (Environment.GetEnvironmentVariable("VELORA_SOFTWARE") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Se adelanta el trabajo lento: el hilo de PDFium arranca (y abre el documento) mientras se construye la ventana.
        Task<Velora.Pdf.PdfDocument>? preopened = null;
        if (args.Path != null && args.Password == null && System.IO.File.Exists(args.Path))
        {
            try
            {
                preopened = Velora.Pdf.PdfDocument.OpenAsync(System.IO.Path.GetFullPath(args.Path));
            }
            catch
            {
                preopened = null;
            }
        }
        else
        {
            _ = Velora.Pdf.PdfWorker.Shared.Pending;
        }

        ThemeHelper.ApplyBrand(); // colores de la marca antes de crear ninguna ventana
        var settings = AppSettings.Load();
        var window = new MainWindow(settings) { IsSilent = args.Silent, AutoPassword = args.Password };
        MainWindow = window;
        if (args.Size is { } s)
        {
            window.Width = s.Width;
            window.Height = s.Height;
        }

        Mark("ventana creada");
        window.ContentRendered += (_, _) => Mark("ventana pintada");
        window.Show();
        _ = StartAsync(window, args, preopened);
    }

    private static async Task StartAsync(MainWindow window, LaunchArgs args, Task<Velora.Pdf.PdfDocument>? preopened)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (args.Path != null)
        {
            await window.OpenFileAsync(args.Path, args.Page is { } p ? p - 1 : null, preopened);
            Mark("documento abierto");
            if (args.Log != null) Automation.Log(args, $"abrir: {sw.ElapsedMilliseconds} ms hasta tener el documento abierto");
        }

        if (args.Updated != null) window.ShowUpdated(args.Updated);
        UpdateService.CleanOldDownloads();
        if (args.Auto != null) await new Automation(window, args).RunAsync();
        else if (!args.Silent)
        {
            // Comprobación automática de actualizaciones, un poco después de arrancar para no retrasar la apertura.
            await Task.Delay(3000);
            await window.CheckForUpdatesAsync(manual: false);
        }
    }
}

/// <summary>Opciones de la línea de comandos: el archivo a abrir y algunas opciones para pruebas automáticas.</summary>
public sealed class LaunchArgs
{
    public string? Path;
    public int? Page;
    public string? Theme;
    public string? Auto;
    public string? Log;
    public string? DataDir;
    public string? Password;
    public string? Updated;
    public bool Silent;
    public bool Keep;
    public Size? Size;

    public static LaunchArgs Parse(string[] argv)
    {
        var a = new LaunchArgs();
        for (int i = 0; i < argv.Length; i++)
        {
            string s = argv[i];
            string? Next() => i + 1 < argv.Length ? argv[++i] : null;
            switch (s)
            {
                case "--tema": a.Theme = Next(); break;
                case "--auto": a.Auto = Next(); break;
                case "--registro": a.Log = Next(); break;
                case "--datos": a.DataDir = Next(); break;
                case "--clave": a.Password = Next(); break;
                case "--actualizado": a.Updated = Next(); break;
                case "--pagina": if (int.TryParse(Next(), out int p)) a.Page = p; break;
                case "--silencioso": a.Silent = true; break;
                case "--mantener": a.Keep = true; break;
                case "--tam":
                    var parts = (Next() ?? "").Split('x');
                    if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h)) a.Size = new Size(w, h);
                    break;
                default:
                    if (!s.StartsWith("--") && a.Path == null) a.Path = s;
                    break;
            }
        }

        return a;
    }
}
