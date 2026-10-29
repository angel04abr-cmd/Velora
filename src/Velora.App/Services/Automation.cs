// SPDX-License-Identifier: GPL-3.0-or-later
// Pruebas automáticas dentro de la aplicación real: ejecuta órdenes sueltas (zoom, búsqueda, anotar, capturas,
// mediciones de velocidad) y las anota en un archivo de registro. Solo se activa con --auto.
using System.Diagnostics;
using System.IO;
using Velora.Updates;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Velora.App.Viewer;
using Velora.Pdf;

namespace Velora.App;

internal sealed class Automation
{
    private readonly MainWindow _w;
    private readonly LaunchArgs _args;

    public Automation(MainWindow window, LaunchArgs args)
    {
        _w = window;
        _args = args;
    }

    public static void Log(LaunchArgs args, string line)
    {
        if (args.Log == null) return;
        try
        {
            File.AppendAllText(args.Log, line + Environment.NewLine);
        }
        catch
        {
            // registro no disponible
        }
    }

    private void Log(string line) => Log(_args, line);

    public async Task RunAsync()
    {
        int errors = 0;
        V.IgnoreRealPointer = true;
        foreach (string raw in _args.Auto!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                await ExecAsync(raw);
            }
            catch (Exception ex)
            {
                errors++;
                Log($"ERROR en «{raw}»: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Log(errors == 0 ? "FIN correcto" : $"FIN con {errors} error(es)");
        if (!_args.Keep) Application.Current.Shutdown(errors == 0 ? 0 : 1);
    }

    private PdfView V => _w.ViewControl;

    private async Task ExecAsync(string raw)
    {
        int c = raw.IndexOf(':');
        string cmd = (c < 0 ? raw : raw[..c]).Trim().ToLowerInvariant();
        string arg = c < 0 ? "" : raw[(c + 1)..].Trim();
        switch (cmd)
        {
            case "esperar": await Task.Delay(int.Parse(arg)); break;
            case "listo": Log($"listo: {await WaitReadyAsync()} ms"); break;
            case "pagina": V.GoToPage(int.Parse(arg) - 1); break;
            case "zoom":
                if (arg == "ancho") V.SetZoomMode(ZoomMode.FitWidth);
                else if (arg == "pagina") V.SetZoomMode(ZoomMode.FitPage);
                else V.SetZoom(double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) / 100.0);
                break;
            case "girar": V.Rotate(int.Parse(arg)); break;
            case "columnas": V.SetColumns(int.Parse(arg), false); break;
            case "desplazar": V.SetVerticalOffset(V.VerticalOffset + double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)); break;
            case "barra": _w.AutoSidebar(arg); break;
            case "buscar":
                await _w.AutoSearchAsync(arg);
                Log($"buscar «{arg}»: {_w.SearchHitCount} resultados");
                break;
            case "seleccionar": Log($"seleccionar «{arg}»: {await SelectTextAsync(arg)}"); break;
            case "resaltar": await _w.AutoMarkupAsync(MarkupKind.Resaltado); break;
            case "subrayar": await _w.AutoMarkupAsync(MarkupKind.Subrayado); break;
            case "ondulado": await _w.AutoMarkupAsync(MarkupKind.Ondulado); break;
            case "tachar": await _w.AutoMarkupAsync(MarkupKind.Tachado); break;
            case "nota": await V.AddNoteAsync(V.CurrentPage, 330, 120, arg.Length > 0 ? arg : "Nota de prueba", 0xFFFFC107, "Prueba"); break;
            case "tinta": await AddInkAsync(); break;
            case "guardar": Log($"guardar: {await _w.SaveAsync()}"); break;
            case "copia": await _w.CurrentDocument!.SaveCopyAsync(arg); Log($"copia guardada: {new FileInfo(arg).Length} bytes"); break;
            case "captura": await CaptureAsync(arg); break;
            case "dialogo": await DialogShotAsync(arg); break;
            case "medir-saltos": await BenchJumpsAsync(int.Parse(arg)); break;
            case "medir-desplazamiento": await BenchScrollAsync(int.Parse(arg)); break;
            case "info": LogInfo(); break;
            case "gc": GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); LogInfo(); break;
            case "claves-lista":
                {
                    var found = new List<string>();
                    foreach (var b in new[] { "ListBoxItem", "ListViewItem", "TreeViewItem", "ComboBoxItem", "Selector", "ListItem" })
                        foreach (var m in new[] { "Background", "BackgroundSelected", "BackgroundSelectedPointerOver", "BackgroundSelectedPressed", "BackgroundPointerOver", "BackgroundPressed", "SelectedBackground", "SelectedPointerOverBackground", "Foreground", "ForegroundSelected", "SelectionIndicator", "SelectedBorderBrush", "BackgroundSelectedUnfocused" })
                        {
                            string k = b + m;
                            if (_w.TryFindResource(k) is SolidColorBrush br) found.Add(k + "=" + br.Color);
                        }
                    Log("listas: " + string.Join(" | ", found));
                    break;
                }
            case "claves-acento":
                {
                    string[] bases = { "ToggleButton", "AccentButton", "CheckBox", "RadioButton", "ComboBox", "ListBoxItem", "ListViewItem", "TreeViewItem", "ProgressBar", "Slider", "TextControl", "TextBox", "ScrollBar", "ToggleSwitch", "Hyperlink", "MenuItem", "Button", "TabViewItem", "NavigationViewItem", "ToolTip", "GridViewItem", "SelectionIndicator", "Focus" };
                    string[] mids = { "Background", "BorderBrush", "Foreground", "CheckBackgroundFill", "CheckBackgroundStroke", "OuterEllipseFill", "OuterEllipseStroke", "CheckGlyphFore", "Fill", "Stroke", "SelectedBackground", "SelectedBorderBrush", "SelectionIndicatorForeground", "Indicator", "BorderBrushFocused", "Underline", "BottomBorder", "ThumbFill", "TrackValueFill", "Highlight" };
                    string[] ends = { "", "Checked", "CheckedPointerOver", "CheckedPressed", "Selected", "SelectedPointerOver", "PointerOver", "Pressed", "Focused", "Indeterminate", "Active" };
                    var found = new SortedSet<string>();
                    foreach (var b in bases) foreach (var m in mids) foreach (var e in ends)
                    {
                        string k = b + m + e;
                        if (_w.TryFindResource(k) is SolidColorBrush br && (br.Color.R == 0x3F && br.Color.G == 0x48 && br.Color.B == 0x4D || br.Color.R == 0x25 && br.Color.G == 0x2B && br.Color.B == 0x32 || br.Color.R == 0x0D && br.Color.G == 0x11 && br.Color.B == 0x18)) found.Add(k + "=" + br.Color);
                    }
                    Log("recursos con el color de acento antiguo: " + string.Join(" | ", found));
                    break;
                }
            case "claves":
                foreach (string k in new[] { "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush",
                    "LayerFillColorDefaultBrush", "LayerOnMicaBaseAltFillColorDefaultBrush", "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush",
                    "ControlFillColorDefaultBrush", "ControlStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush", "CardStrokeColorDefaultBrush",
                    "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "TextFillColorDisabledBrush", "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush",
                    "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentFillColorDisabledBrush", "TextOnAccentFillColorPrimaryBrush", "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush", "FocusStrokeColorOuterBrush", "ControlStrongStrokeColorDefaultBrush", "SystemAccentColorPrimaryBrush", "ControlAltFillColorSecondaryBrush", "WindowBackgroundBrush" })
                {
                    var r = _w.TryFindResource(k) as SolidColorBrush;
                    Log($"clave {k}: {(r == null ? "NO existe" : r.Color.ToString())}");
                }
                foreach (string k in new[] { "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3", "AccentFillColorDefault", "AccentFillColorSecondary", "AccentFillColorTertiary", "ControlAltFillColorQuarternaryBrush", "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver", "AccentButtonBackground", "ToggleButtonBackgroundCheckedBrush", "ControlFillColorSecondaryBrush" })
                {
                    var r = _w.TryFindResource(k);
                    Log($"recurso {k}: {(r == null ? "NO existe" : r is SolidColorBrush b ? "brush " + b.Color : r.ToString())}");
                }
                Log($"tema oscuro (registro): {ThemeHelper.IsDark()}");
                break;
            case "texto": Log($"texto de la selección: «{(await V.GetSelectedTextAsync()).Replace("\r\n", "⏎")}»"); break;
            case "recientes": Log($"abiertos/recientes: {string.Join(" | ", System.IO.File.Exists(Path.Combine(AppSettings.DataDir, "settings.json")) ? new[] { "settings.json existe" } : new[] { "sin settings.json" })}"); break;
            case "clic": await PointerOnTextAsync(arg, 1); break;
            case "doble": await PointerOnTextAsync(arg, 2); break;
            case "triple": await PointerOnTextAsync(arg, 3); break;
            case "arrastrar": await DragTextAsync(arg); break;
            case "enlace": await ClickLinkAsync(arg); break;
            case "rueda": V.Wheel(new Point(V.ViewportSize.Width / 2, V.ViewportSize.Height / 2), int.Parse(arg.Split(',')[0]), arg.Contains("ctrl") ? System.Windows.Input.ModifierKeys.Control : System.Windows.Input.ModifierKeys.None); break;
            case "herramienta": _w.AutoSetTool(arg); break;
            case "dibujar-raton": await InkByMouseAsync(); break;
            case "nota-raton": await NoteByMouseAsync(arg); break;
            case "estado": Log($"estado: selección={V.HasSelection}, barra flotante={_w.SelectionPopupVisible}, panel de nota={_w.NotePanelVisible}, página={V.CurrentPage + 1}, zoom={V.Zoom:P0}"); break;
            case "volver": await _w.AutoBackAsync(); break;
            case "presentacion": await PresentationShotAsync(arg); break;
            case "imprimir-xps": await PrintXpsAsync(arg); break;
            case "arranque": Log($"arranque: {App.MarksText()}, ahora {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms"); break;
            case "anotaciones":
                {
                    var pc = await V.EnsureContentAsync(V.CurrentPage);
                    Log($"anotaciones en la página {V.CurrentPage + 1}: " + string.Join(", ", pc.Annotations.Select(a => $"{a.Kind}#{a.ColorArgb:X8}")));
                    break;
                }
            case "pagina-siguiente": V.GoToNextPage(); Log($"página siguiente → {V.CurrentPage + 1} (puede ir atrás: {V.CanGoPrevious}, adelante: {V.CanGoNext})"); break;
            case "pagina-anterior": V.GoToPreviousPage(); Log($"página anterior → {V.CurrentPage + 1} (puede ir atrás: {V.CanGoPrevious}, adelante: {V.CanGoNext})"); break;
            case "zonas": await HitZonesAsync(); break;
            case "actualizar-comprobar":
                {
                    var info = await UpdateService.CheckAsync();
                    Log(info == null ? $"actualización: ya se tiene la última (actual {UpdateService.CurrentVersion})" : $"actualización: disponible {info.Version} ({info.MsiName}, {info.MsiSize / 1048576.0:0.0} MB, huella: {(info.HashUrl != null ? "sí" : "NO")}); se instala en el sitio: {UpdateService.CanInstallInPlace}");
                    break;
                }
            case "actualizar-aplicar":
                {
                    var info = await UpdateService.CheckAsync() ?? throw new InvalidOperationException("no hay versión nueva");
                    Log($"actualización: aplicando {info.Version}…");
                    await _w.ApplyUpdateAsync(info);
                    await Task.Delay(30000); // la ventana se cierra sola al lanzar el instalador
                    break;
                }
            case "enlaces-diag":
                foreach (int pg in arg.Split(',').Select(int.Parse)) Log(await V.Document!.DiagnoseLinksAsync(pg - 1));
                break;
            case "menu": Log($"menú {arg}: {await _w.AutoOpenMenuAsync(int.Parse(arg))}"); break;
            case "maximizar": _w.WindowState = WindowState.Maximized; await Task.Delay(500); LogWindowRects("maximizada"); break;
            case "restaurar": _w.WindowState = WindowState.Normal; await Task.Delay(500); LogWindowRects("restaurada"); break;
            case "salir": Application.Current.Shutdown(0); break;
            default: throw new InvalidOperationException("orden desconocida");
        }
    }

    private void LogInfo()
    {
        var (pending, tiles, bytes) = V.Stats;
        Log($"render tier {System.Windows.Media.RenderCapability.Tier >> 16}");
        Log($"info: página {V.CurrentPage + 1}/{V.PageCount}, zoom {V.Zoom:P0}, giro {V.Rotation * 90}°, columnas {V.Columns}, baldosas en caché {tiles} ({bytes / 1048576.0:0.0} MB), pendientes {pending} (cola de PDFium {Velora.Pdf.PdfWorker.Shared.Pending}, lecturas de texto {V.ContentJobs}), reaperturas de PDFium {V.Document?.Recycles} (última {V.Document?.LastRecycleMs} ms, total {V.Document?.TotalRecycleMs} ms), memoria del proceso {Process.GetCurrentProcess().WorkingSet64 / 1048576} MB, privada {Process.GetCurrentProcess().PrivateMemorySize64 / 1048576} MB, montón .NET {GC.GetTotalMemory(false) / 1048576} MB");
    }

    private async Task<long> WaitReadyAsync(int timeoutMs = 20000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (V.AllVisibleReady() && V.Stats.Pending == 0) return sw.ElapsedMilliseconds;
            await Task.Delay(1);
        }

        return -1;
    }

    private async Task<bool> SelectTextAsync(string query)
    {
        bool ok = await V.TestSelectTextAsync(query);
        await Task.Delay(50);
        return ok;
    }

    private async Task AddInkAsync()
    {
        var doc = V.Document!;
        var pts = new List<(float, float)>();
        for (int i = 0; i <= 60; i++) pts.Add((120 + i * 6f, 300 + (float)(Math.Sin(i / 6.0) * 30)));
        await doc.AddInkAsync(V.CurrentPage, pts, 0xFFE53935, 2.5f, "Prueba");
        V.MarkPageChanged(V.CurrentPage);
    }

    private async Task CaptureAsync(string path)
    {
        await WaitReadyAsync();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Save(_w, path);
        Log($"captura: {path}");
    }

    private static void Save(Window w, string path)
    {
        var content = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(w);
        int pw = (int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), ph = (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY);
        var bmp = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bmp.Render(back);
        bmp.Render(content);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private async Task DialogShotAsync(string what)
    {
        bool keep = what.EndsWith("!");
        if (keep) what = what.TrimEnd('!');
        string dir = Path.GetDirectoryName(_args.Log ?? Path.Combine(Path.GetTempPath(), "x"))!;
        Window? dlg = what switch
        {
            "propiedades" => new PropertiesDialog(await _w.CurrentDocument!.GetPropertiesAsync()),
            "acerca" => new AboutDialog(),
            "contrasena" => new PasswordDialog("protegido.pdf", true),
            "manual" => new DocumentWindow("Manual de uso de Velora", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MANUAL.md")), null),
            "licencias" => new DocumentWindow("Licencias y créditos", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AVISOS-DE-TERCEROS.md")), null),
            "actualizacion" => new UpdateDialog(new Velora.Updates.UpdateInfo(new Version(1, 0, 4), "v1.0.4",
                "- **Desplazamiento rápido en libros grandes**: al arrastrar la barra o girar la rueda muy deprisa por un libro de miles de páginas, las páginas y las miniaturas se quedaban en blanco y a veces ya no cargaban. Ahora se ve al instante un borrador borroso de cada página y la calidad completa llega al detenerte.\n- Velora ya no se pone a leer el texto de miles de páginas que pasaste de largo.\n- Menos memoria: tras desplazarse a toda velocidad por un libro de 8000 páginas el uso bajó de casi 3 GB a menos de 1 GB.",
                "https://github.com/angel04abr-cmd/Velora/releases/tag/v1.0.4", "Velora-1.0.4-x64.msi", "https://example.invalid/x.msi", 4825088, null),
                "1.0.3", true, (_, _) => Task.CompletedTask, null),
            _ => throw new InvalidOperationException("diálogo desconocido"),
        };
        if (what is not ("manual" or "licencias" or "actualizacion")) dlg.Owner = _w;
        else if (what != "actualizacion") { dlg.Width = 760; dlg.Height = 760; }
        dlg.Show();
        await Task.Delay(700);
        string file = Path.Combine(dir, $"dialogo-{what}.png");
        var root = (FrameworkElement)dlg.Content;
        var dpi = VisualTreeHelper.GetDpi(dlg);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle((Brush)_w.FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(back);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(file)) enc.Save(fs);
        if (keep) await Task.Delay(9000);
        dlg.Close();
        Log($"captura de diálogo: {file} (ventana {dlg.ActualWidth:0}x{dlg.ActualHeight:0}, contenido {root.ActualWidth:0}x{root.ActualHeight:0})");
    }

    private static string Stats(List<double> v)
    {
        v.Sort();
        double P(double q) => v[Math.Min(v.Count - 1, (int)(q * v.Count))];
        return $"n={v.Count} mínimo={v[0]:0.0} mediana={P(0.5):0.0} p95={P(0.95):0.0} máximo={v[^1]:0.0}";
    }

    /// <summary>Salta a páginas lejanas (como arrastrar la barra de desplazamiento) y mide cuánto tarda en verse la página.</summary>
    private async Task BenchJumpsAsync(int count)
    {
        int n = V.PageCount;
        var rnd = new Random(7);
        var times = new List<double>();
        for (int i = 0; i < count; i++)
        {
            int target = rnd.Next(n);
            var sw = Stopwatch.StartNew();
            V.GoToPage(target);
            long ms = await WaitReadyAsync();
            times.Add(ms < 0 ? 20000 : sw.Elapsed.TotalMilliseconds);
        }

        Log($"saltos a página aleatoria (ms hasta ver la página completa): {Stats(times)}");
        LogInfo();
    }

    /// <summary>Desplazamiento continuo a 60 cuadros por segundo: mide si la ventana se queda atascada mientras se dibuja.</summary>
    private async Task BenchScrollAsync(int frames)
    {
        var deltas = new List<double>();
        var sw = Stopwatch.StartNew();
        double last = 0;
        var tcs = new TaskCompletionSource();
        int count = 0;
        void Step(object? s, EventArgs e)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            if (last > 0) deltas.Add(now - last);
            last = now;
            V.SetVerticalOffset(V.VerticalOffset + 36);
            if (++count >= frames)
            {
                CompositionTarget.Rendering -= Step;
                tcs.TrySetResult();
            }
        }

        CompositionTarget.Rendering += Step;
        await tcs.Task;
        int pagesCrossed = V.CurrentPage;
        Log($"desplazamiento continuo de {frames} cuadros (ms entre cuadros): {Stats(deltas)}; páginas recorridas hasta la {pagesCrossed + 1}");
        await WaitReadyAsync();
        LogInfo();
    }

    // ---- simulación de ratón sobre el texto ----
    private async Task<(PageContent C, int From, int To)> FindAsync(string text)
    {
        for (int p = V.CurrentPage; p < V.PageCount; p++)
        {
            var c = await V.EnsureContentAsync(p);
            var sb = new System.Text.StringBuilder(c.CharCount);
            foreach (int cp in c.Chars) sb.Append(cp is > 0 and < 0x10000 ? (char)cp : '?');
            int i = sb.ToString().IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return (c, i, i + text.Length - 1);
        }

        throw new InvalidOperationException("texto no encontrado: " + text);
    }

    private Point CenterOf(PageContent c, int idx)
    {
        var b = c.BoxOf(idx);
        return V.ViewPointOf(c.Page, (b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2)!.Value;
    }

    private async Task PointerOnTextAsync(string text, int clicks)
    {
        var (c, from, _) = await FindAsync(text);
        V.GoToPage(c.Page);
        await WaitReadyAsync();
        var pt = CenterOf(c, from);
        for (int i = 1; i <= clicks; i++)
        {
            V.PointerDown(pt, i);
            V.PointerUp(pt);
        }

        Log($"{(clicks == 1 ? "clic" : clicks == 2 ? "doble clic" : "triple clic")} sobre «{text}» → selección: «{(await V.GetSelectedTextAsync()).Replace("\r\n", "⏎")}»");
    }

    private async Task DragTextAsync(string text)
    {
        var (c, from, to) = await FindAsync(text);
        V.GoToPage(c.Page);
        await WaitReadyAsync();
        var a = CenterOf(c, from);
        var b = CenterOf(c, to);
        V.PointerDown(a, 1);
        for (int i = 1; i <= 8; i++) V.PointerMove(new Point(a.X + (b.X - a.X) * i / 8, a.Y + (b.Y - a.Y) * i / 8));
        V.PointerUp(b);
        Log($"arrastre sobre «{text}» → selección: «{(await V.GetSelectedTextAsync()).Replace("\r\n", "⏎")}», barra flotante={_w.SelectionPopupVisible}");
    }

    private async Task ClickLinkAsync(string kind)
    {
        for (int p = V.CurrentPage; p < V.PageCount; p++)
        {
            var c = await V.EnsureContentAsync(p);
            var link = c.Links.FirstOrDefault(l => kind == "interno" ? l.Kind == LinkKind.Goto : l.Kind == LinkKind.Uri && l.Uri!.StartsWith("http"));
            if (link == null) continue;
            V.GoToPage(p);
            await WaitReadyAsync();
            string? uri = null;
            _w.IsSilent = true;
            _w.UriSink = u => uri = u;
            var pt = V.ViewPointOf(p, (link.Rect.Left + link.Rect.Right) / 2, (link.Rect.Top + link.Rect.Bottom) / 2)!.Value;
            int before = V.CurrentPage;
            V.PointerDown(pt, 1);
            Log($"  enlace en {link.Rect}, clic en {pt}, estado tras pulsar: {V.DebugPress}");
            V.PointerUp(pt);
            await Task.Delay(150);
            Log($"enlace {kind} (página {p + 1}): ahora en página {V.CurrentPage + 1} (antes {before + 1}), uri pedida={uri ?? "ninguna"}");
            return;
        }

        throw new InvalidOperationException("no hay enlace " + kind);
    }

    private async Task InkByMouseAsync()
    {
        _w.AutoSetTool("tinta");
        var (w, h) = V.ViewportSize;
        var start = new Point(w / 2 - 120, h / 2);
        V.PointerDown(start, 1);
        for (int i = 1; i <= 40; i++) V.PointerMove(new Point(start.X + i * 6, start.Y + Math.Sin(i / 5.0) * 25));
        V.PointerUp(new Point(start.X + 240, start.Y));
        await Task.Delay(400);
        _w.AutoSetTool("seleccion");
        var c = await V.EnsureContentAsync(V.CurrentPage);
        Log($"dibujo con el ratón: anotaciones de tinta en la página = {c.Annotations.Count(a => a.Kind == AnnotationKind.Tinta)}");
    }

    private async Task NoteByMouseAsync(string text)
    {
        _w.AutoSetTool("nota");
        var (w, h) = V.ViewportSize;
        var pt = new Point(w / 2 + 60, h / 3);
        V.PointerDown(pt, 1);
        V.PointerUp(pt);
        Log($"nota: panel visible={_w.NotePanelVisible}");
        _w.AutoConfirmNote(text.Length > 0 ? text : "Nota creada con el ratón");
        await Task.Delay(500);
        var doc = V.Document!;
        var list = await doc.GetPageAnnotationsAsync(V.CurrentPage, PdfPriority.Interactivo);
        Log($"nota: anotaciones de tipo nota en la página = {list.Count(a => a.Kind == AnnotationKind.Nota)}; texto = «{list.FirstOrDefault(a => a.Kind == AnnotationKind.Nota)?.Contents}»");
    }

    private async Task PresentationShotAsync(string file)
    {
        var w = new PresentationWindow(V.Document!, Enumerable.Range(0, V.PageCount).Select(_ => new PageSize(612, 792)).ToArray(), V.CurrentPage) { Owner = _w };
        w.Show();
        await Task.Delay(1200);
        Save(w, file);
        w.Close();
        Log($"presentación capturada: {file}");
    }

    private async Task PrintXpsAsync(string file)
    {
        // Comprueba el dibujo de páginas para impresión sin necesitar una impresora: se escribe un archivo XPS.
        var doc = V.Document!;
        var sizes = await doc.GetPageSizesAsync();
        var pages = new List<int> { 0, Math.Min(1, doc.PageCount - 1) };
        var paginator = PdfPrinter.CreatePaginator(doc, sizes, pages, new Size(793.7, 1122.5));
        var sw = Stopwatch.StartNew();
        var xd = new System.Windows.Xps.Packaging.XpsDocument(file, FileAccess.ReadWrite);
        var writer = System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xd);
        writer.Write(paginator);
        xd.Close();

        Log($"impresión a XPS: {new FileInfo(file).Length} bytes, {paginator.PageCount} páginas, {sw.ElapsedMilliseconds} ms");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 rect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    /// <summary>Pregunta a Windows qué hay bajo el centro de cada elemento de la cabecera: 1 = se puede hacer clic, 2 = se arrastra la ventana, 9 = botón de maximizar.</summary>
    private async Task HitZonesAsync()
    {
        await Task.Delay(200);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_w).Handle;
        string Zone(FrameworkElement e, double fx = 0.5)
        {
            if (!e.IsVisible) return "oculto";
            var p = e.PointToScreen(new Point(e.ActualWidth * fx, e.ActualHeight / 2));
            long lp = ((long)(short)(int)p.Y << 16) | (ushort)(short)(int)p.X;
            int r = (int)SendMessage(hwnd, 0x0084, IntPtr.Zero, (IntPtr)lp);
            return r switch { 1 => "clic", 2 => "arrastrar ventana", 9 => "botón maximizar", 20 => "borde", 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 => "borde", _ => "otro(" + r + ")" };
        }

        var results = new List<string>
        {
            "icono=" + Zone((FrameworkElement)_w.FindName("HeaderLeft")!, 0.03),
            "menú=" + Zone(_w.MenuMainForTest),
            "panel lateral=" + Zone(_w.SidebarToggle),
            "casilla de página=" + Zone(_w.PageBox),
            "zoom=" + Zone(_w.ZoomBox),
            "buscar=" + Zone(_w.FindToggle),
            "título (hueco)=" + Zone(_w.HeaderTitleForTest),
            "minimizar=" + Zone(_w.MinButton),
            "maximizar=" + Zone(_w.MaxButton),
            "cerrar=" + Zone(_w.CloseButton),
        };
        Log("zonas de la cabecera: " + string.Join(", ", results));
    }

    private void LogWindowRects(string estado)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_w).Handle;
        GetWindowRect(hwnd, out var r);
        var wa = SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(_w);
        var head = _w.HeaderBarForTest.PointToScreen(new Point(0, 0));
        var closeRight = _w.CloseButton.PointToScreen(new Point(_w.CloseButton.ActualWidth, 0));
        Log($"ventana {estado}: rect px=({r.Left},{r.Top})-({r.Right},{r.Bottom}); área de trabajo px=({wa.Left * dpi.DpiScaleX:0},{wa.Top * dpi.DpiScaleY:0})-({wa.Right * dpi.DpiScaleX:0},{wa.Bottom * dpi.DpiScaleY:0}); cabecera empieza en ({head.X:0},{head.Y:0}); borde derecho del botón cerrar en {closeRight.X:0}");
    }
}
