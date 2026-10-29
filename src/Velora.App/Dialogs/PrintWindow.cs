// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velora.Pdf;

namespace Velora.App;

/// <summary>
/// Ventana de impresión de Velora, con vista previa de cada hoja (el cuadro estándar de Windows no la ofrece para programas propios):
/// impresora, copias, orientación, color y páginas a la izquierda; la hoja tal como saldrá, a la derecha.
/// </summary>
internal sealed class PrintWindow : Window
{
    private readonly PdfDocument _doc;
    private readonly PageSize[] _sizes;
    private readonly int _currentPage;

    private readonly ComboBox _printers = new() { MinHeight = 32 };
    private readonly TextBox _copies = new() { Text = "1", Width = 64, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _orientation = new() { MinHeight = 32 };
    private readonly ComboBox _color = new() { MinHeight = 32 };
    private readonly RadioButton _all = new() { Content = "Todo el documento", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _current = new() { Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _custom = new() { Content = "Páginas:", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _range = new() { MinHeight = 30, Margin = new Thickness(22, 2, 0, 0), IsEnabled = false, ToolTip = "Por ejemplo: 1-5, 8, 11-13" };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button _print = new() { Content = "Imprimir", MinWidth = 110, Padding = new Thickness(16, 7, 16, 7), IsDefault = true };
    private readonly Image _image = new() { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MinWidth = 190 };
    private readonly Button _prev = new() { Content = "", Width = 36, Height = 32 };
    private readonly Button _next = new() { Content = "", Width = 36, Height = 32 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Opacity = 0.8 };

    private List<PrintQueue> _queues = [];
    private List<int> _pages = [];
    private int _index;
    private CancellationTokenSource? _previewCts;
    private bool _ready;

    public PrintWindow(Window owner, PdfDocument doc, PageSize[] sizes, int currentPage)
    {
        _doc = doc;
        _sizes = sizes;
        _currentPage = currentPage;
        Title = "Imprimir";
        Width = 940;
        Height = 700;
        MinWidth = 760;
        MinHeight = 560;
        Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png"));
        _current.Content = $"Página actual ({currentPage + 1})";

        var icon = (FontFamily)Application.Current.FindResource("IconFont");
        _prev.FontFamily = _next.FontFamily = icon;
        _orientation.ItemsSource = new[] { "Automática (la mejor para cada página)", "Vertical", "Horizontal" };
        _orientation.SelectedIndex = 0;
        _color.ItemsSource = new[] { "Color", "Blanco y negro" };
        _color.SelectedIndex = 0;
        _error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");

        // ---- columna izquierda ----
        var props = new Button { Content = "Propiedades de la impresora…", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5) };
        props.Click += (_, _) => OpenPrinterProperties();
        var minus = new Button { Content = "−", Width = 32, Height = 30, Margin = new Thickness(0, 0, 4, 0) };
        var plus = new Button { Content = "+", Width = 32, Height = 30, Margin = new Thickness(4, 0, 0, 0) };
        minus.Click += (_, _) => _copies.Text = Math.Max(1, CopyCount() - 1).ToString();
        plus.Click += (_, _) => _copies.Text = Math.Min(999, CopyCount() + 1).ToString();
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { minus, _copies, plus } };

        var left = new StackPanel { Margin = new Thickness(24, 20, 16, 12), Width = 300 };
        void Section(string title, UIElement content)
        {
            left.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 5) });
            left.Children.Add(content);
        }

        left.Children.Add(new TextBlock { Text = "Imprimir", FontSize = 22, FontWeight = FontWeights.SemiBold });
        left.Children.Add(new TextBlock { Text = Path.GetFileName(doc.Path), Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 4) });
        Section("Impresora", new StackPanel { Children = { _printers, props } });
        Section("Copias", copyRow);
        Section("Orientación", _orientation);
        Section("Color", _color);
        Section("Páginas", new StackPanel { Children = { _all, _current, _custom, _range, _error } });

        // ---- zona de la vista previa ----
        var sheetHost = new Border { Margin = new Thickness(8, 20, 24, 8), Padding = new Thickness(20), CornerRadius = new CornerRadius(8), Child = new Grid { Children = { _image, _message } } };
        sheetHost.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 24, 10), Children = { _prev, _pageLabel, _next } };

        var cancel = new Button { Content = "Cancelar", MinWidth = 110, Padding = new Thickness(16, 7, 16, 7), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        _print.Click += (_, _) => DoPrint();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 10, 24, 18), Children = { _print, cancel } };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(left, 0);
        Grid.SetRowSpan(left, 2);
        Grid.SetColumn(sheetHost, 1);
        Grid.SetColumn(nav, 1);
        Grid.SetRow(nav, 1);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        grid.Children.Add(sheetHost);
        grid.Children.Add(nav);
        grid.Children.Add(buttons);
        Content = grid;

        _prev.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
        _printers.SelectionChanged += (_, _) => Refresh();
        _orientation.SelectionChanged += (_, _) => Refresh();
        _color.SelectionChanged += (_, _) => Refresh();
        foreach (var r in new[] { _all, _current, _custom })
            r.Checked += (_, _) =>
            {
                _range.IsEnabled = _custom.IsChecked == true;
                Refresh();
                if (_range.IsEnabled) _range.Focus();
            };
        _range.TextChanged += (_, _) => Refresh();
        _copies.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _copies.LostFocus += (_, _) => _copies.Text = Math.Clamp(CopyCount(), 1, 999).ToString();
        _copies.PreviewMouseWheel += (_, e) =>
        {
            if (!_copies.IsKeyboardFocused) return;
            _copies.Text = Math.Clamp(CopyCount() + Math.Sign(e.Delta), 1, 999).ToString();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_range.IsKeyboardFocused || _copies.IsKeyboardFocused) return;
            if (e.Key is Key.Left or Key.PageUp) { Step(-1); e.Handled = true; }
            else if (e.Key is Key.Right or Key.PageDown) { Step(1); e.Handled = true; }
        };
        Closed += (_, _) => _previewCts?.Cancel();
        Loaded += async (_, _) => await LoadPrintersAsync();
    }

    private int CopyCount() => int.TryParse(_copies.Text, out int n) ? n : 1;

    private PrintQueue? Queue => _printers.SelectedIndex >= 0 && _printers.SelectedIndex < _queues.Count ? _queues[_printers.SelectedIndex] : null;

    private async Task LoadPrintersAsync()
    {
        _message.Text = "Buscando impresoras…";
        _message.Visibility = Visibility.Visible;
        _print.IsEnabled = false;
        string? defaultName = null;
        List<PrintQueue> list = [];
        try
        {
            // Los objetos de impresora pertenecen al hilo que los crea (el de la ventana): se listan aquí, tras dejar que se pinte «Buscando…».
            await Task.Delay(30);
            using var server = new LocalPrintServer();
            list = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]).OrderBy(q => q.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            try
            {
                defaultName = LocalPrintServer.GetDefaultPrintQueue().FullName;
            }
            catch (PrintSystemException)
            {
                // sin impresora predeterminada
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        _queues = list;
        _printers.ItemsSource = list.Select(q => q.Name).ToList();
        if (list.Count == 0)
        {
            _message.Text = "No hay ninguna impresora instalada. Instala una en Configuración de Windows para poder imprimir.";
            return;
        }

        int def2 = defaultName == null ? 0 : list.FindIndex(q => q.FullName == defaultName);
        _ready = true;
        _printers.SelectedIndex = Math.Max(0, def2);
    }

    private void OpenPrinterProperties()
    {
        if (Queue is not { } q) return;
        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry /e /n \"{q.FullName}\"") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    /// <summary>Ajustes elegidos: el tique de impresión de la impresora con la orientación, el color y las copias.</summary>
    private PrintTicket? BuildTicket()
    {
        if (Queue is not { } q) return null;
        PrintTicket ticket;
        try
        {
            q.Refresh();
            ticket = q.UserPrintTicket ?? q.DefaultPrintTicket;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            ticket = new PrintTicket();
        }

        ticket = new PrintTicket(ticket.GetXmlStream());
        ticket.CopyCount = Math.Clamp(CopyCount(), 1, 999);
        if (_orientation.SelectedIndex == 1) ticket.PageOrientation = PageOrientation.Portrait;
        else if (_orientation.SelectedIndex == 2) ticket.PageOrientation = PageOrientation.Landscape;
        ticket.OutputColor = _color.SelectedIndex == 1 ? OutputColor.Monochrome : OutputColor.Color;
        return ticket;
    }

    private List<int>? SelectedPages(out string? problem)
    {
        problem = null;
        if (_current.IsChecked == true) return [_currentPage];
        if (_custom.IsChecked == true) return Velora.Pdf.Viewer.PageRanges.Parse(_range.Text, _doc.PageCount, out problem);
        return Enumerable.Range(0, _doc.PageCount).ToList();
    }

    private void Refresh()
    {
        if (!_ready || !IsLoaded) return;
        var pages = SelectedPages(out string? problem);
        _error.Text = problem ?? "";
        _error.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        _pages = pages ?? [];
        _print.IsEnabled = _pages.Count > 0 && Queue != null;
        _index = Math.Clamp(_index, 0, Math.Max(0, _pages.Count - 1));
        _ = ShowPreviewAsync();
    }

    private void Step(int d)
    {
        if (_pages.Count == 0) return;
        _index = Math.Clamp(_index + d, 0, _pages.Count - 1);
        _ = ShowPreviewAsync();
    }

    private Size PrintableArea(PrintTicket ticket)
    {
        try
        {
            var pd = new PrintDialog { PrintQueue = Queue, PrintTicket = ticket };
            if (pd.PrintableAreaWidth > 0 && pd.PrintableAreaHeight > 0) return new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        return ticket.PageOrientation == PageOrientation.Landscape ? new Size(1122, 793) : new Size(793, 1122);
    }

    private async Task ShowPreviewAsync()
    {
        _previewCts?.Cancel();
        _prev.IsEnabled = _index > 0;
        _next.IsEnabled = _index < _pages.Count - 1;
        if (_pages.Count == 0 || BuildTicket() is not { } ticket)
        {
            _image.Source = null;
            _pageLabel.Text = "";
            return;
        }

        var cts = _previewCts = new CancellationTokenSource();
        int page = _pages[_index];
        _pageLabel.Text = $"Hoja {_index + 1} de {_pages.Count}  ·  página {page + 1}";
        try
        {
            var area = PrintableArea(ticket);
            var (rotate, wDip, hDip) = PdfPrinter.Fit(_sizes[page], area, _orientation.SelectedIndex == 0);
            // La hoja se dibuja en una imagen de unos 900 px de lado mayor, con la página centrada como saldrá en el papel
            double scale = 900 / Math.Max(area.Width, area.Height);
            int sheetW = (int)(area.Width * scale), sheetH = (int)(area.Height * scale);
            int pw = Math.Max(1, (int)(wDip * scale)), ph = Math.Max(1, (int)(hDip * scale));
            using var tile = await _doc.RenderTileAsync(new TileRequest(page, pw, ph, rotate ? 1 : 0, 0, 0, pw, ph, Annotations: true, Printing: true), PdfPriority.Urgente, cts.Token);
            if (cts.IsCancellationRequested) return;
            var pageBmp = BitmapSource.Create(tile.Width, tile.Height, 96, 96, PixelFormats.Pbgra32, null, tile.Pixels, tile.Stride);
            pageBmp.Freeze();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, sheetW, sheetH));
                dc.DrawImage(pageBmp, new Rect((sheetW - pw) / 2.0, (sheetH - ph) / 2.0, pw, ph));
                if (_color.SelectedIndex == 1)
                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), null, new Rect(0, 0, sheetW, sheetH));
            }

            var sheet = new RenderTargetBitmap(sheetW, sheetH, 96, 96, PixelFormats.Pbgra32);
            sheet.Render(visual);
            BitmapSource shown = sheet;
            if (_color.SelectedIndex == 1) shown = new FormatConvertedBitmap(sheet, PixelFormats.Gray8, null, 0);
            shown.Freeze();
            _image.Source = shown;
            _message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _message.Text = "No se pudo dibujar la vista previa de esta página.";
            _message.Visibility = Visibility.Visible;
        }
    }

    private void DoPrint()
    {
        var pages = SelectedPages(out string? problem);
        if (pages == null || Queue is not { } queue || BuildTicket() is not { } ticket)
        {
            _error.Text = problem ?? "Elige una impresora.";
            _error.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var area = PrintableArea(ticket);
            var pd = new PrintDialog { PrintQueue = queue, PrintTicket = ticket };
            pd.PrintDocument(PdfPrinter.CreatePaginator(_doc, _sizes, pages, area, _orientation.SelectedIndex == 0), Path.GetFileName(_doc.Path));
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageDialog.Show(this, "No se pudo enviar el documento a la impresora.\n\n" + ex.Message, MessageDialog.Kind.Warning, ["Aceptar"], 0, 0);
        }
    }
}
