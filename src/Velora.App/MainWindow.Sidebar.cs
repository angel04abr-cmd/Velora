// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Velora.App.Viewer;
using Velora.Pdf;
using Microsoft.Win32;

namespace Velora.App;

public partial class MainWindow
{
    private const double RailWidth = 44;
    private const double ThumbWidth = 150;

    // ================================================================== mostrar y ocultar

    private void ApplySidebar()
    {
        bool show = HasDoc && _settings.SidebarVisible;
        SidebarToggle.IsChecked = show;
        Sidebar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = show ? new GridLength(RailWidth + Math.Clamp(_settings.SidebarWidth, 150, 520)) : new GridLength(0);
        SplitterColumn.Width = show ? new GridLength(5) : new GridLength(0);
        SidebarSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSidebar()
    {
        _settings.SidebarVisible = !_settings.SidebarVisible;
        ApplySidebar();
        if (_settings.SidebarVisible) ShowSidebarTab(_tab);
    }

    private void OnSidebarToggle(object sender, RoutedEventArgs e)
    {
        _settings.SidebarVisible = SidebarToggle.IsChecked == true;
        ApplySidebar();
        if (_settings.SidebarVisible) ShowSidebarTab(_tab);
    }

    private void OnSidebarResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _settings.SidebarWidth = Math.Max(150, SidebarColumn.ActualWidth - RailWidth);
    }

    private void OnSidebarTab(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) ShowSidebarTab(tag);
    }

    private void ShowSidebarTab(string tab)
    {
        _tab = tab;
        _settings.SidebarTab = tab;
        TabThumbs.IsChecked = tab == "thumbs";
        TabOutline.IsChecked = tab == "outline";
        TabAnnots.IsChecked = tab == "annots";
        TabAttach.IsChecked = tab == "attach";
        ThumbList.Visibility = tab == "thumbs" ? Visibility.Visible : Visibility.Collapsed;
        OutlinePanel.Visibility = tab == "outline" ? Visibility.Visible : Visibility.Collapsed;
        AnnotPanel.Visibility = tab == "annots" ? Visibility.Visible : Visibility.Collapsed;
        AttachPanel.Visibility = tab == "attach" ? Visibility.Visible : Visibility.Collapsed;
        SidebarTitle.Text = tab switch
        {
            "outline" => "Índice",
            "annots" => "Anotaciones",
            "attach" => "Adjuntos",
            _ => "Miniaturas",
        };
        if (tab == "annots") _ = ScanAnnotationsAsync();
        if (tab == "thumbs") SyncThumbSelection();
    }

    // ================================================================== miniaturas

    private void BuildThumbs()
    {
        if (_doc == null) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _thumbLoader = new ThumbLoader(_doc, dpi) { CurrentPage = () => View.CurrentPage };
        _thumbItems = new ThumbItem[_sizes.Length];
        for (int i = 0; i < _sizes.Length; i++)
        {
            double h = ThumbWidth * _sizes[i].Height / Math.Max(1, _sizes[i].Width);
            double w = ThumbWidth;
            if (h > 240) { w = ThumbWidth * 240 / h; h = 240; }
            _thumbItems[i] = new ThumbItem(_thumbLoader, i, Math.Round(w), Math.Round(h));
        }

        ThumbList.ItemsSource = _thumbItems;
        SyncThumbSelection();
    }

    private void SyncThumbSelection()
    {
        if (_thumbItems.Length == 0 || _tab != "thumbs" || Sidebar.Visibility != Visibility.Visible) return;
        int p = View.CurrentPage;
        if (p < 0 || p >= _thumbItems.Length) return;
        _syncingThumb = true;
        try
        {
            ThumbList.SelectedIndex = p;
            ThumbList.ScrollIntoView(_thumbItems[p]);
        }
        finally
        {
            _syncingThumb = false;
        }
    }

    private void OnThumbSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingThumb || ThumbList.SelectedItem is not ThumbItem t) return;
        PushHistory(View.CurrentPage);
        View.GoToPage(t.Page);
    }

    private void OnThumbCleanUp(object sender, CleanUpVirtualizedItemEventArgs e)
    {
        if (e.Value is ThumbItem t) _thumbLoader?.Cancel(t);
    }

    // ================================================================== índice

    private async Task LoadOutlineAsync(PdfDocument doc)
    {
        try
        {
            var outline = await doc.GetOutlineAsync();
            if (doc != _doc) return;
            OutlineTree.ItemsSource = outline;
            OutlineEmpty.Visibility = outline.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TabOutline.IsEnabled = true;
            if (outline.Count > 0 && _settings.SidebarTab == "thumbs" && false) ShowSidebarTab("outline");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    private async void OnOutlineSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not OutlineNode node || node.Page < 0 || _doc == null) return;
        await SafeRunAsync(async () =>
        {
            var doc = _doc;
            var (_, y) = await doc.PdfPointToPageAsync(node.Page, node.X, node.Y);
            if (doc != _doc) return;
            PushHistory(View.CurrentPage);
            View.GoToPage(node.Page, float.IsNaN(y) ? null : y);
        });
    }

    // ================================================================== adjuntos

    private async Task LoadAttachmentsAsync(PdfDocument doc)
    {
        try
        {
            var list = await doc.GetAttachmentsAsync();
            if (doc != _doc) return;
            AttachList.ItemsSource = list;
            AttachEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    /// <summary>El nombre de un adjunto viene del PDF: se quitan rutas y caracteres no válidos antes de proponerlo.</summary>
    private static string SafeFileName(string name)
    {
        string n = Path.GetFileName(name.Replace('/', '\\'));
        foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().Trim('.');
        return n.Length == 0 ? "adjunto" : n;
    }

    private async void OnSaveAttachment(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachmentInfo att } || _doc == null) return;
        await SafeRunAsync(async () =>
        {
            var dlg = new SaveFileDialog { Title = "Guardar el adjunto", FileName = SafeFileName(att.Name), Filter = T.AttachmentFilter };
            if (dlg.ShowDialog(this) != true) return;
            await _doc.SaveAttachmentAsync(att.Index, dlg.FileName);
            ShowInfo(string.Format(T.AttachmentSaved, Path.GetFileName(dlg.FileName)));
        });
    }

    // ================================================================== anotaciones (lista)

    private void RefreshAnnotsIfShown()
    {
        _annotsScanned = false;
        if (_tab == "annots" && Sidebar.Visibility == Visibility.Visible) _ = ScanAnnotationsAsync();
    }

    private async Task ScanAnnotationsAsync()
    {
        if (_doc == null || _annotsScanned) return;
        _annotScanCts?.Cancel();
        var cts = _annotScanCts = new CancellationTokenSource();
        var doc = _doc;
        _annotsScanned = true;
        _annots.Clear();
        AnnotEmpty.Visibility = Visibility.Visible;
        try
        {
            int n = doc.PageCount;
            for (int p = 0; p < n; p++)
            {
                if (p % 5 == 0 || p == n - 1) AnnotEmpty.Text = string.Format(T.AnnotsScanning, p + 1, n);
                var list = await doc.GetPageAnnotationsAsync(p, PdfPriority.Fondo, cts.Token);
                if (doc != _doc || cts.IsCancellationRequested) return;
                foreach (var a in list) _annots.Add(new AnnotItem(p, a));
            }

            AnnotEmpty.Text = T.AnnotsEmpty;
            AnnotEmpty.Visibility = _annots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            if (doc == _doc) _annotsScanned = false;
        }
        catch (Exception ex)
        {
            _annotsScanned = false;
            ErrorLog.Write(ex);
        }
    }

    private void OnAnnotSelected(object sender, SelectionChangedEventArgs e)
    {
        if (AnnotList.SelectedItem is not AnnotItem item) return;
        PushHistory(View.CurrentPage);
        View.GoToPage(item.Page);
        View.ScrollToRect(item.Page, item.Annotation.Rect);
        AnnotList.SelectedItem = null;
    }

    private async void OnDeleteAnnotation(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AnnotItem item } || _doc == null) return;
        await SafeRunAsync(async () =>
        {
            await View.RemoveAnnotationAsync(item.Page, item.Annotation.Index);
            // Los números de las demás anotaciones de la página cambian: se vuelve a leer la lista.
            _annotsScanned = false;
            await ScanAnnotationsAsync();
        });
    }

    internal Task AutoBackAsync()
    {
        GoBack();
        return Task.CompletedTask;
    }
}
