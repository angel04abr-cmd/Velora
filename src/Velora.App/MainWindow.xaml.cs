// SPDX-License-Identifier: GPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Velora.App.Viewer;
using Velora.Pdf;
using Microsoft.Win32;

namespace Velora.App;

public partial class MainWindow : Window
{
    private static readonly uint[] Palette = [0xFFFFEB3B, 0xFF9CCC65, 0xFF4FC3F7, 0xFFF48FB1, 0xFFFFA726, 0xFFE53935];

    private readonly AppSettings _settings;
    private PdfDocument? _doc;
    private PageSize[] _sizes = [];
    private ThumbLoader? _thumbLoader;
    private ThumbItem[] _thumbItems = [];
    private readonly Stack<int> _back = new(), _forward = new();
    private bool _syncingThumb, _updatingZoom, _opening, _fullScreen, _twoPages, _coverAlone;
    private string _tab;
    private uint _markupColor, _inkColor;
    private DispatcherTimer? _infoTimer;
    private int _notePage;
    private float _noteU, _noteV;
    private AnnotationClick? _noteExisting;
    private readonly ObservableCollection<AnnotItem> _annots = new();
    private CancellationTokenSource? _annotScanCts;
    private bool _annotsScanned;
    private (WindowState State, WindowStyle Style, ResizeMode Resize)? _beforeFullScreen;

    public MainWindow(AppSettings settings)
    {
        _settings = settings;
        App.Mark("ctor");
        InitializeComponent();
        App.Mark("XAML cargado");
        _tab = settings.SidebarTab;
        _markupColor = settings.MarkupColor;
        _inkColor = settings.InkColor;
        _twoPages = settings.Columns >= 2;
        _coverAlone = settings.CoverAlone;
        TwoPagesToggle.IsChecked = _twoPages;

        Width = Math.Clamp(settings.WindowWidth, MinWidth, SystemParameters.WorkArea.Width);
        Height = Math.Clamp(settings.WindowHeight, MinHeight, SystemParameters.WorkArea.Height);
        if (settings.Maximized) WindowState = WindowState.Maximized;

        View.ScrollOwner = Scroller;
        ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.BeginInvoke(ApplyTheme);

        BindCommands();
        BuildMenus(false);
        StateChanged += OnWindowStateChanged;
        BuildColorDots();
        HookView();
        UpdateColorSwatch();
        RefreshRecents();
        ApplySidebar();
        UpdateUi();

        Closing += OnWindowClosing;
        DragOver += OnDragOverFile;
        Drop += OnDropFile;
        PreviewMouseDown += OnPreviewMouseDownCloseFloaters;
        Loaded += (_, _) => App.Mark("Loaded");
        App.Mark("ctor listo");
    }

    internal PdfView ViewControl => View;
    internal FrameworkElement MenuMainForTest => MainMenu.Visibility == Visibility.Visible ? MainMenu : MenuHamburger;
    internal FrameworkElement HeaderTitleForTest => HeaderTitle;
    internal FrameworkElement HeaderBarForTest => HeaderBar;
    internal PdfDocument? CurrentDocument => _doc;
    internal bool IsSilent { get; set; }

    private bool HasDoc => _doc != null;

    private void ApplyTheme()
    {
        ThemeHelper.ApplyBrand();
        View.Backdrop = ThemeHelper.ViewerBackdrop();
        View.SelectionBrush = ThemeHelper.SelectionBrush();
    }

    // ================================================================== órdenes

    private void Bind(RoutedUICommand cmd, Action exec, Func<bool>? can = null)
    {
        CommandBindings.Add(new CommandBinding(cmd,
            (_, _) => SafeRun(exec),
            (_, e) => e.CanExecute = can?.Invoke() ?? true));
    }

    private void BindAsync(RoutedUICommand cmd, Func<Task> exec, Func<bool>? can = null)
    {
        CommandBindings.Add(new CommandBinding(cmd,
            async (_, _) => await SafeRunAsync(exec),
            (_, e) => e.CanExecute = can?.Invoke() ?? true));
    }

    private void BindCommands()
    {
        Func<bool> hasDoc = () => HasDoc;
        BindAsync(AppCommands.Open, OpenWithDialogAsync);
        BindAsync(AppCommands.CloseDocument, CloseDocumentAsync, hasDoc);
        Bind(AppCommands.Exit, Close);
        Bind(AppCommands.Manual, OpenManual);
        BindAsync(AppCommands.CheckUpdates, () => CheckForUpdatesAsync(manual: true));
        Bind(AppCommands.AutoUpdates, () => { _settings.AutoUpdateCheck = !_settings.AutoUpdateCheck; _settings.Save(); });
        Bind(AppCommands.NewWindow, () => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false }));
        BindAsync(AppCommands.Save, () => SaveAsync(), () => _doc?.IsModified == true);
        BindAsync(AppCommands.SaveCopy, SaveCopyAsync, hasDoc);
        BindAsync(AppCommands.Print, PrintAsync, hasDoc);
        Bind(AppCommands.Find, ShowSearch, hasDoc);
        Bind(AppCommands.FindNext, () => StepSearch(+1), hasDoc);
        Bind(AppCommands.FindPrevious, () => StepSearch(-1), hasDoc);
        Bind(AppCommands.PreviousPage, () => View.GoToPreviousPage(), () => HasDoc && View.CanGoPrevious);
        Bind(AppCommands.NextPage, () => View.GoToNextPage(), () => HasDoc && View.CanGoNext);
        Bind(AppCommands.GoToPage, () => { PageBox.Focus(); PageBox.SelectAll(); }, hasDoc);
        Bind(AppCommands.ZoomIn, () => View.ZoomIn(), hasDoc);
        Bind(AppCommands.ZoomOut, () => View.ZoomOut(), hasDoc);
        Bind(AppCommands.FitWidth, () => View.SetZoomMode(ZoomMode.FitWidth), hasDoc);
        Bind(AppCommands.FitPage, () => View.SetZoomMode(ZoomMode.FitPage), hasDoc);
        Bind(AppCommands.Zoom100, () => View.SetZoom(1.0), hasDoc);
        Bind(AppCommands.RotateRight, () => View.Rotate(1), hasDoc);
        Bind(AppCommands.RotateLeft, () => View.Rotate(-1), hasDoc);
        Bind(AppCommands.Presentation, StartPresentation, hasDoc);
        Bind(AppCommands.FullScreen, ToggleFullScreen);
        Bind(AppCommands.Sidebar, ToggleSidebar, hasDoc);
        BindAsync(AppCommands.Properties, ShowPropertiesAsync, hasDoc);
        Bind(AppCommands.Back, GoBack, () => _back.Count > 0);
        Bind(AppCommands.Forward, GoForward, () => _forward.Count > 0);
        Bind(AppCommands.TwoPages, ToggleTwoPages, hasDoc);
        Bind(AppCommands.CoverAlone, ToggleCover, hasDoc);
        Bind(AppCommands.About, () => new AboutDialog { Owner = this }.ShowDialog());
        Bind(AppCommands.Escape, OnEscape);
        BindAsync(AppCommands.Highlight, () => ApplyMarkupAsync(MarkupKind.Resaltado), () => View.HasSelection);
        BindAsync(AppCommands.Underline, () => ApplyMarkupAsync(MarkupKind.Subrayado), () => View.HasSelection);
        BindAsync(AppCommands.Squiggly, () => ApplyMarkupAsync(MarkupKind.Ondulado), () => View.HasSelection);
        BindAsync(AppCommands.Strike, () => ApplyMarkupAsync(MarkupKind.Tachado), () => View.HasSelection);
        Bind(AppCommands.NoteTool, () => SetTool(NoteToggle.IsChecked == true ? ViewTool.Note : ViewTool.Select), hasDoc);
        Bind(AppCommands.InkTool, () => SetTool(InkToggle.IsChecked == true ? ViewTool.Ink : ViewTool.Select), hasDoc);
        // Las mismas herramientas desde el menú «⋯» (cuando la ventana es estrecha y los botones se ocultan)
        Bind(AppCommands.NoteToolMenu, () => SetTool(View.Tool == ViewTool.Note ? ViewTool.Select : ViewTool.Note), hasDoc);
        Bind(AppCommands.InkToolMenu, () => SetTool(View.Tool == ViewTool.Ink ? ViewTool.Select : ViewTool.Ink), hasDoc);
        Bind(AppCommands.ColorMenu, () => ColorPopup.Visibility = Visibility.Visible, hasDoc);
        Bind(AppCommands.OpenFolder, () =>
        {
            if (_doc != null) Process.Start("explorer.exe", $"/select,\"{_doc.Path}\"");
        }, hasDoc);

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
            async (_, _) => await SafeRunAsync(CopySelectionAsync),
            (_, e) => e.CanExecute = View.HasSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll,
            async (_, _) => await SafeRunAsync(() => View.SelectAllAsync()),
            (_, e) => e.CanExecute = HasDoc && !SearchBox.IsKeyboardFocused && !PageBox.IsKeyboardFocused && !NoteText.IsKeyboardFocused));
    }

    private void SafeRun(Action a)
    {
        try
        {
            a();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private async Task SafeRunAsync(Func<Task> a)
    {
        try
        {
            await a();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    internal void ReportError(Exception ex)
    {
        ErrorLog.Write(ex);
        string msg = ex is PdfException ? ex.Message : T.UnexpectedError + "\n\n" + ex.Message;
        Error(msg);
    }

    internal void Error(string message)
    {
        if (IsSilent)
        {
            ShowInfo(message);
            return;
        }

        MessageDialog.Show(this, message, MessageDialog.Kind.Warning, ["Aceptar"], 0, 0);
    }

    /// <summary>Pregunta con botones en español. Devuelve el número del botón elegido (-1 si se cierra la ventana).</summary>
    private int Ask(string message, string[] buttons, int defaultIndex, int cancelIndex, MessageDialog.Kind kind = MessageDialog.Kind.Question)
    {
        if (IsSilent) return defaultIndex;
        return MessageDialog.Show(this, message, kind, buttons, defaultIndex, cancelIndex);
    }

    internal void ShowInfo(string text)
    {
        InfoText.Text = text;
        InfoBar.Visibility = Visibility.Visible;
        _infoTimer?.Stop();
        _infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _infoTimer.Tick += (_, _) =>
        {
            _infoTimer!.Stop();
            InfoBar.Visibility = Visibility.Collapsed;
        };
        _infoTimer.Start();
    }

    private void ShowLoading(string text)
    {
        LoadingText.Text = text;
        Loading.Visibility = Visibility.Visible;
    }

    private void HideLoading() => Loading.Visibility = Visibility.Collapsed;

    // ================================================================== abrir y cerrar documentos

    private async Task OpenWithDialogAsync()
    {
        var dlg = new OpenFileDialog { Filter = T.FileFilter, Title = T.OpenTitle, CheckFileExists = true };
        if (_doc != null) dlg.InitialDirectory = Path.GetDirectoryName(_doc.Path);
        if (dlg.ShowDialog(this) == true) await OpenFileAsync(dlg.FileName);
    }

    internal async Task OpenFileAsync(string path, int? startPage = null, Task<PdfDocument>? preopened = null)
    {
        if (_opening) return;
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            Error(T.OpenFailed);
            return;
        }

        if (!File.Exists(path))
        {
            Error($"No se encuentra el archivo:\n{path}");
            _settings.Recent.RemoveAll(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            RefreshRecents();
            return;
        }

        if (!await ConfirmDiscardAsync()) return;
        _opening = true;
        string name = Path.GetFileName(path);
        ShowLoading(string.Format(T.Opening, name));
        string? password = null;
        try
        {
            while (true)
            {
                try
                {
                    var doc = await (preopened ?? PdfDocument.OpenAsync(path, password));
                    preopened = null;
                    var sizes = await doc.GetPageSizesAsync();
                    InstallDocument(doc, sizes, startPage);
                    break;
                }
                catch (PdfPasswordException ex)
                {
                    preopened = null;
                    HideLoading();
                    if (IsSilent)
                    {
                        // En las pruebas automáticas no se muestra el cuadro: se usa la clave recibida una sola vez.
                        if (AutoPassword == null || ex.Wrong)
                        {
                            Error(ex.Message);
                            return;
                        }

                        password = AutoPassword;
                    }
                    else
                    {
                        password = PasswordDialog.Ask(this, name, ex.Wrong);
                        if (password == null) return;
                    }

                    ShowLoading(string.Format(T.Opening, name));
                }
            }
        }
        catch (PdfException ex)
        {
            Error(T.OpenFailed + "\n\n" + ex.Message);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
        finally
        {
            _opening = false;
            HideLoading();
        }
    }

    internal string? AutoPassword { get; set; }

    private void InstallDocument(PdfDocument doc, PageSize[] sizes, int? startPage)
    {
        App.Mark("instalar documento: inicio");
        SaveViewState();
        CloseDocumentCore();
        _doc = doc;
        _sizes = sizes;
        var recent = _settings.Find(doc.Path);
        var mode = ZoomMode.FitWidth;
        double zoom = 1.0;
        int rot = 0, page = 0;
        if (recent != null)
        {
            Enum.TryParse(recent.ZoomMode, out mode);
            zoom = recent.Zoom;
            rot = recent.Rotation;
            page = recent.Page;
        }

        View.Configure(mode, zoom, rot, _twoPages ? 2 : 1, _coverAlone);
        View.Tool = ViewTool.Select;
        NoteToggle.IsChecked = InkToggle.IsChecked = false;
        View.Attach(doc, sizes, Math.Clamp(startPage ?? page, 0, sizes.Length - 1));
        Welcome.Visibility = Visibility.Collapsed;
        BuildThumbs();
        _annots.Clear();
        _annotsScanned = false;
        AnnotList.ItemsSource = _annots;
        OutlineTree.ItemsSource = null;
        OutlineEmpty.Visibility = Visibility.Collapsed;
        AttachList.ItemsSource = null;
        AttachEmpty.Visibility = Visibility.Collapsed;
        _ = LoadOutlineAsync(doc);
        _ = LoadAttachmentsAsync(doc);
        _settings.Touch(new RecentFile { Path = doc.Path, Page = View.CurrentPage, ZoomMode = View.ZoomMode.ToString(), Zoom = View.Zoom, Rotation = View.Rotation });
        _settings.Save();
        ApplySidebar();
        ShowSidebarTab(_tab);
        UpdateUi();
        View.Focus();
        App.Mark("instalar documento: fin");
        if (SearchBar.Visibility == Visibility.Visible && SearchBox.Text.Length > 0) StartSearch();
    }

    private async Task CloseDocumentAsync()
    {
        if (_doc == null || !await ConfirmDiscardAsync()) return;
        SaveViewState();
        _settings.Save();
        CloseDocumentCore();
        UpdateUi();
    }

    private void SaveViewState()
    {
        if (_doc == null) return;
        var e = _settings.Find(_doc.Path) ?? new RecentFile { Path = _doc.Path };
        e.Page = View.CurrentPage;
        e.ZoomMode = View.ZoomMode.ToString();
        e.Zoom = View.Zoom;
        e.Rotation = View.Rotation;
        _settings.Touch(e);
    }

    private void CloseDocumentCore()
    {
        _searchCts?.Cancel();
        _annotScanCts?.Cancel();
        _thumbLoader?.Dispose();
        _thumbLoader = null;
        _thumbItems = [];
        ThumbList.ItemsSource = null;
        _back.Clear();
        _forward.Clear();
        HideFloaters();
        View.SetSearchResults(null, null);
        _hits.Clear();
        _hitIndex = -1;
        SearchStatus.Text = "";
        View.Detach();
        _doc?.Dispose();
        _doc = null;
        _sizes = [];
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_doc is not { IsModified: true }) return true;
        int r = Ask(string.Format(T.UnsavedAsk, Path.GetFileName(_doc.Path)), ["Guardar", "No guardar", "Cancelar"], 0, 2, MessageDialog.Kind.Warning);
        if (r == 2 || r < 0) return false;
        if (r == 0) return await SaveAsync();
        return true;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_doc is { IsModified: true } && !IsSilent)
        {
            int r = Ask(string.Format(T.UnsavedAsk, Path.GetFileName(_doc.Path)), ["Guardar", "No guardar", "Cancelar"], 0, 2, MessageDialog.Kind.Warning);
            if (r == 2 || r < 0)
            {
                e.Cancel = true;
                return;
            }

            if (r == 0)
            {
                // Se guarda de forma síncrona: la ventana se está cerrando.
                try
                {
                    SaveBlocking();
                }
                catch (Exception ex)
                {
                    ReportError(ex);
                    e.Cancel = true;
                    return;
                }
            }
        }

        SaveViewState();
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        _settings.MarkupColor = _markupColor;
        _settings.InkColor = _inkColor;
        _settings.Columns = _twoPages ? 2 : 1;
        _settings.CoverAlone = _coverAlone;
        _settings.Save();
    }

    /// <summary>
    /// Guarda al cerrar la ventana. Hay que cerrar primero el documento: PDFium mantiene el archivo abierto y Windows no deja
    /// reemplazarlo. Si aun así no se puede reemplazar, los cambios se guardan con otro nombre; nunca se pierden.
    /// </summary>
    private void SaveBlocking()
    {
        var doc = _doc!;
        string path = doc.Path;
        string tmp = path + ".velora-tmp";
        try
        {
            doc.SaveCopyAsync(tmp).GetAwaiter().GetResult();
        }
        catch
        {
            DeleteQuietly(tmp);
            throw;
        }

        // A partir de aquí la ventana se cierra pase lo que pase: el documento ya no se puede seguir usando.
        doc.CloseAsync().GetAwaiter().GetResult();
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string alt = AlternatePath(path);
            File.Move(tmp, alt);
            Error(string.Format(T.ReplaceFailed, alt));
        }
        finally
        {
            doc.MarkSaved(); // el documento ya está cerrado: si falla algo, que el siguiente intento de cerrar no vuelva a preguntar
        }
    }

    /// <summary>Nombre libre «X (con anotaciones).pdf» en la misma carpeta, para cuando no se puede reemplazar el original.</summary>
    private static string AlternatePath(string path)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileNameWithoutExtension(path);
        string alt = Path.Combine(dir, name + " (con anotaciones).pdf");
        for (int i = 2; File.Exists(alt); i++) alt = Path.Combine(dir, $"{name} (con anotaciones {i}).pdf");
        return alt;
    }

    // ================================================================== guardar e imprimir

    /// <summary>
    /// Guarda las anotaciones en el propio archivo. Se escribe primero en un archivo temporal; después se cierra el
    /// documento (para soltar el archivo), se reemplaza el original y se vuelve a abrir en el mismo punto.
    /// Si el original no se puede reemplazar, los cambios quedan en una copia y se continúa trabajando con ella.
    /// </summary>
    internal async Task<bool> SaveAsync()
    {
        if (_doc == null) return true;
        var doc = _doc;
        string path = doc.Path;
        string tmp = path + ".velora-tmp";
        try
        {
            await doc.SaveCopyAsync(tmp);
        }
        catch (Exception ex)
        {
            DeleteQuietly(tmp);
            ErrorLog.Write(ex);
            Error(T.SaveFailed + "\n\n" + ex.Message);
            return false;
        }

        string target = path;
        string? note = null;
        try
        {
            SaveViewState();
            int page = View.CurrentPage;
            CloseDocumentCore();
            await doc.CloseAsync();
            try
            {
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                string alt = AlternatePath(path);
                File.Move(tmp, alt);
                target = alt;
                note = string.Format(T.ReplaceFailed, alt);
                _settings.Touch(new RecentFile { Path = alt, Page = page, ZoomMode = View.ZoomMode.ToString(), Zoom = View.Zoom, Rotation = View.Rotation });
            }

            // Se vuelve a abrir el archivo guardado, para ver exactamente lo que quedó en disco.
            var fresh = await PdfDocument.OpenAsync(target);
            var sizes = await fresh.GetPageSizesAsync();
            InstallDocument(fresh, sizes, page);
            if (note != null) Error(note);
            else ShowInfo(T.Saved);
            return note == null;
        }
        catch (Exception ex)
        {
            DeleteQuietly(tmp);
            ErrorLog.Write(ex);
            Error(T.SaveFailed + "\n\n" + ex.Message);
            UpdateUi();
            return false;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // nada que hacer
        }
    }

    private async Task SaveCopyAsync()
    {
        if (_doc == null) return;
        var dlg = new SaveFileDialog
        {
            Title = T.SaveCopyTitle,
            Filter = "Documento PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_doc.Path) + " (copia).pdf",
            InitialDirectory = Path.GetDirectoryName(_doc.Path),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        if (string.Equals(Path.GetFullPath(dlg.FileName), _doc.Path, StringComparison.OrdinalIgnoreCase))
        {
            await SaveAsync();
            return;
        }

        await _doc.SaveCopyAsync(dlg.FileName);
        ShowInfo(string.Format(T.SavedCopy, Path.GetFileName(dlg.FileName)));
    }

    private async Task PrintAsync()
    {
        if (_doc == null) return;
        bool ok = await PdfPrinter.PrintAsync(this, _doc, _sizes, View.CurrentPage);
        if (ok) ShowInfo(string.Format(T.PrintDone, Path.GetFileName(_doc.Path)));
    }

    // ================================================================== vista

    private void HookView()
    {
        View.CurrentPageChanged += (_, _) =>
        {
            UpdatePageBox();
            CommandManager.InvalidateRequerySuggested();
            SyncThumbSelection();
        };
        View.ZoomChanged += (_, _) =>
        {
            UpdateZoomText();
            HideSelectionPopup();
        };
        View.ViewScrolled += (_, _) => HideSelectionPopup();
        View.SelectionChanged += OnViewSelection;
        View.UriRequested += (_, uri) => OpenUri(uri);
        View.JumpedFromPage += (_, from) => PushHistory(from);
        View.NoteRequested += (_, req) => ShowNoteEditor(req);
        View.AnnotationClicked += (_, c) => ShowNoteViewer(c);
        View.AnnotationsChanged += (_, _) =>
        {
            UpdateTitle();
            RefreshAnnotsIfShown();
            CommandManager.InvalidateRequerySuggested();
        };
        View.ErrorOccurred += (_, ex) =>
        {
            ErrorLog.Write(ex);
            ShowInfo(ex is PdfException ? ex.Message : T.UnexpectedError);
        };
    }

    // ================================================================== cabecera unificada

    /// <summary>
    /// Si la ventana es estrecha, la cabecera se ajusta sola para que nada se corte: primero se estrecha el cuadro de zoom, después
    /// el menú se recoge en un solo botón y por último se ocultan grupos de botones (siguen en los menús).
    /// </summary>
    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e) => AdaptToolbar();

    private bool _adaptingToolbar, _menuCollapsed;

    private static double Wide(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return e.DesiredSize.Width;
    }

    private void AdaptToolbar()
    {
        if (_adaptingToolbar || HeaderBar.ActualWidth <= 0) return;
        _adaptingToolbar = true;
        try
        {
            // 1) Todo completo y se mide por separado cada cosa que puede cambiar (medir la cabecera entera no sirve: WPF guarda el
            //    resultado y no cambia durante el mismo ciclo de diseño).
            ZoomBox.Width = 150;
            FrameworkElement[] groups = [GroupViewOpts, GroupAnnot, PageCountText, ZoomBox];
            foreach (var g in groups) g.Visibility = Visibility.Visible;
            var (wFull, wCompact) = MenuWidths();
            SetMenuCollapsed(false);
            double need = wFull + Wide(HeaderTools) + Wide(CaptionButtons);
            double[] saving = [42, wFull - wCompact, Wide(GroupViewOpts), Wide(GroupAnnot), Wide(PageCountText) + 14, 112];
            double available = HeaderBar.ActualWidth - 20; // un respiro entre bloques

            // 2) Se quita lo menos importante, en orden, hasta que quepa.
            for (int i = 0; i < saving.Length && need > available; i++)
            {
                switch (i)
                {
                    case 0: ZoomBox.Width = 108; break;
                    case 1: SetMenuCollapsed(true); break;
                    default: groups[i - 2].Visibility = Visibility.Collapsed; break;
                }

                need -= saving[i];
            }

            if (Environment.GetEnvironmentVariable("VELORA_DEBUG_TOOLBAR") == "1")
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                File.AppendAllText(Path.Combine(AppSettings.DataDir, "toolbar.log"),
                    $"ancho {HeaderBar.ActualWidth:0}: izq {wFull:0}/{wCompact:0} herr {Wide(HeaderTools):0} botones {Wide(CaptionButtons):0} -> necesita {need:0} disponible {available:0} (menú recogido: {_menuCollapsed}){Environment.NewLine}");
            }

            // La barra lateral no puede ocupar más de la mitad de una ventana estrecha.
            SidebarColumn.MaxWidth = Math.Max(150, ActualWidth * 0.45);
            UpdateZoomText();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
        finally
        {
            _adaptingToolbar = false;
        }
    }

    // ---- botones de la ventana ----

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;
        MaxGlyph.Text = max ? "" : "";
        MaxButton.ToolTip = max ? "Restaurar" : "Maximizar";
        if (max && !_fullScreen) Dispatcher.BeginInvoke(ApplyMaximizedMargin, DispatcherPriority.Loaded);
        else RootGrid.Margin = new Thickness(0);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect r);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    /// <summary>
    /// Una ventana con barra de título propia, al maximizarse, se pasa unos píxeles por cada borde (los de redimensión).
    /// Se mide cuánto se sale respecto al área de trabajo del monitor y se compensa con un margen igual.
    /// </summary>
    private void ApplyMaximizedMargin()
    {
        if (WindowState != WindowState.Maximized || _fullScreen) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var mi = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var r) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref mi)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        RootGrid.Margin = new Thickness(
            Math.Max(0, mi.Work.Left - r.Left) / dpi.DpiScaleX, Math.Max(0, mi.Work.Top - r.Top) / dpi.DpiScaleY,
            Math.Max(0, r.Right - mi.Work.Right) / dpi.DpiScaleX, Math.Max(0, r.Bottom - mi.Work.Bottom) / dpi.DpiScaleY);
    }

    // ---- Alt: la ventana maximizada y el botón de maximizar con el menú de ajuste de Windows 11 ----

    private const int WmSize = 0x0005, SizeMaximized = 2, WmNcHitTest = 0x0084, WmNcMouseMove = 0x00A0, WmNcLButtonDown = 0x00A1, WmNcLButtonUp = 0x00A2, WmNcMouseLeave = 0x02A2, HtMaxButton = 9;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src) src.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WmNcHitTest when MaxButton.IsVisible && MaxButton.ActualWidth > 0:
                {
                    long l = lParam.ToInt64();
                    var pt = MaxButton.PointFromScreen(new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF)));
                    bool inside = pt.X >= 0 && pt.Y >= 0 && pt.X < MaxButton.ActualWidth && pt.Y < MaxButton.ActualHeight;
                    MaxButton.Tag = inside ? "hover" : null;
                    if (inside)
                    {
                        // Devolver «botón de maximizar» hace que Windows 11 muestre su cuadro de disposiciones de ventana.
                        handled = true;
                        return (IntPtr)HtMaxButton;
                    }

                    break;
                }
            case WmNcMouseLeave:
                MaxButton.Tag = null;
                break;
            case WmSize when wParam.ToInt32() == SizeMaximized && !_fullScreen:
                // Windows ya dio su tamaño final a la ventana: se compensa lo que se sale por los bordes (el aviso de cambio de estado de WPF llega a veces antes).
                Dispatcher.BeginInvoke(ApplyMaximizedMargin, DispatcherPriority.Normal);
                break;
            case WmNcLButtonDown when wParam.ToInt32() == HtMaxButton:
                handled = true; // el clic se completa al soltar
                break;
            case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                handled = true;
                OnMaximize(this, new RoutedEventArgs());
                break;
        }

        return IntPtr.Zero;
    }

    // ---- menús ----

    // ================================================================== teclas globales

    private void OnEscape()
    {
        if (NotePanel.Visibility == Visibility.Visible) { HideNotePanel(); return; }
        if (ColorPopup.Visibility == Visibility.Visible) { ColorPopup.Visibility = Visibility.Collapsed; return; }
        if (_fullScreen) { ToggleFullScreen(); return; }
        if (View.Tool != ViewTool.Select) { SetTool(ViewTool.Select); return; }
        if (View.HasSelection) { View.ClearSelection(); return; }
        if (SearchBar.Visibility == Visibility.Visible) { CloseSearch(); return; }
    }

    private void OnPreviewMouseDownCloseFloaters(object sender, MouseButtonEventArgs e)
    {
        if (ColorPopup.Visibility == Visibility.Visible && !IsInside(e.OriginalSource as DependencyObject, ColorPopup) && !IsInside(e.OriginalSource as DependencyObject, ColorButton))
            ColorPopup.Visibility = Visibility.Collapsed;
    }

    private static bool IsInside(DependencyObject? d, DependencyObject container)
    {
        while (d != null)
        {
            if (ReferenceEquals(d, container)) return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }

        return false;
    }
}
