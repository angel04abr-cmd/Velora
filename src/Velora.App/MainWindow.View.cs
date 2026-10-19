// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Velora.App.Viewer;
using Velora.Pdf;

namespace Velora.App;

public partial class MainWindow
{
    // ================================================================== estado de la interfaz

    private void UpdateUi()
    {
        bool has = HasDoc;
        PageBox.IsEnabled = has;
        ZoomBox.IsEnabled = has;
        SidebarToggle.IsEnabled = has;
        FindToggle.IsEnabled = has;
        NoteToggle.IsEnabled = has;
        InkToggle.IsEnabled = has;
        TwoPagesToggle.IsEnabled = has;
        ColorButton.IsEnabled = has;
        Welcome.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (!has)
        {
            SearchBar.Visibility = Visibility.Collapsed;
            FindToggle.IsChecked = false;
            RefreshRecents();
            PageBox.Text = "";
            PageCountText.Text = "";
            ZoomBox.Text = "";
            ApplySidebar();
        }

        UpdatePageBox();
        UpdateZoomText();
        UpdateTitle();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateTitle()
    {
        Title = _doc == null ? T.AppName : $"{(_doc.IsModified ? "● " : "")}{Path.GetFileName(_doc.Path)} — {T.AppName}";
        HeaderTitle.Text = _doc == null ? "" : $"{(_doc.IsModified ? "● " : "")}{Path.GetFileName(_doc.Path)}";
    }

    private void UpdatePageBox()
    {
        if (_doc == null) return;
        PageCountText.Text = string.Format(T.PageOf, _doc.PageCount);
        if (!PageBox.IsKeyboardFocusWithin) PageBox.Text = (View.CurrentPage + 1).ToString();
    }

    private void UpdateZoomText()
    {
        if (_doc == null) return;
        _updatingZoom = true;
        try
        {
            string pct = $"{Math.Round(View.Zoom * 100)} %";
            // Con el cuadro estrecho solo cabe el porcentaje
            ZoomBox.Text = ZoomBox.Width < 130 ? pct : View.ZoomMode switch
            {
                ZoomMode.FitWidth => $"{T.ZoomFitWidth} ({pct})",
                ZoomMode.FitPage => $"{T.ZoomFitPage} ({pct})",
                _ => pct,
            };
        }
        finally
        {
            _updatingZoom = false;
        }
    }

    private void OnPageBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(PageBox.Text.Trim(), out int n) && _doc != null)
            {
                PushHistory(View.CurrentPage);
                View.GoToPage(Math.Clamp(n, 1, _doc.PageCount) - 1);
            }

            UpdatePageBox();
            View.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdatePageBox();
            View.Focus();
            e.Handled = true;
        }
    }

    private void OnPageBoxFocus(object sender, KeyboardFocusChangedEventArgs e) => PageBox.SelectAll();

    private void OnPageBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e) => UpdatePageBox();

    private void OnZoomSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingZoom || _doc == null || ZoomBox.SelectedItem is not ComboBoxItem item) return;
        string tag = item.Tag as string ?? "";
        switch (tag)
        {
            case "width": View.SetZoomMode(ZoomMode.FitWidth); break;
            case "page": View.SetZoomMode(ZoomMode.FitPage); break;
            default:
                if (double.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z))
                    View.SetZoom(z);
                break;
        }

        Dispatcher.BeginInvoke(() => View.Focus(), DispatcherPriority.Input);
    }

    private void OnZoomBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _doc == null) return;
        string t = new string(ZoomBox.Text.Where(c => char.IsDigit(c) || c == ',' || c == '.').ToArray()).Replace(',', '.');
        if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct) && pct > 0)
            View.SetZoom(pct / 100.0);
        else
            UpdateZoomText();
        View.Focus();
        e.Handled = true;
    }

    private void ToggleTwoPages()
    {
        _twoPages = !_twoPages;
        TwoPagesToggle.IsChecked = _twoPages;
        View.SetColumns(_twoPages ? 2 : 1, _coverAlone);
    }

    private void ToggleCover()
    {
        _coverAlone = !_coverAlone;
        if (_twoPages) View.SetColumns(2, _coverAlone);
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _beforeFullScreen = (WindowState, WindowStyle, ResizeMode);
            _fullScreen = true;
            HeaderBar.Visibility = Visibility.Collapsed; // en pantalla completa no hay cabecera
            RootGrid.Margin = new Thickness(0);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            var b = _beforeFullScreen!.Value;
            _fullScreen = false;
            HeaderBar.Visibility = Visibility.Visible;
            WindowStyle = b.Style;
            ResizeMode = b.Resize;
            WindowState = b.State;
            OnWindowStateChanged(this, EventArgs.Empty);
        }
    }

    private void StartPresentation()
    {
        if (_doc == null) return;
        var w = new PresentationWindow(_doc, _sizes, View.CurrentPage) { Owner = this };
        w.ShowDialog();
        View.GoToPage(w.LastPage);
    }

    private async Task ShowPropertiesAsync()
    {
        if (_doc == null) return;
        var props = await _doc.GetPropertiesAsync();
        new PropertiesDialog(props) { Owner = this }.ShowDialog();
    }

    // ================================================================== historial de saltos

    private void PushHistory(int fromPage)
    {
        if (_back.Count == 0 || _back.Peek() != fromPage) _back.Push(fromPage);
        _forward.Clear();
        CommandManager.InvalidateRequerySuggested();
    }

    private void GoBack()
    {
        if (_back.Count == 0) return;
        _forward.Push(View.CurrentPage);
        View.GoToPage(_back.Pop());
        CommandManager.InvalidateRequerySuggested();
    }

    private void GoForward()
    {
        if (_forward.Count == 0) return;
        _back.Push(View.CurrentPage);
        View.GoToPage(_forward.Pop());
        CommandManager.InvalidateRequerySuggested();
    }

    // ================================================================== enlaces

    internal Action<string>? UriSink { get; set; }

    private void OpenUri(string uri)
    {
        if (IsSilent && UriSink != null)
        {
            UriSink(uri);
            return;
        }

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeMailto))
        {
            ShowInfo(T.LinkBlocked);
            return;
        }

        if (Ask(string.Format(T.LinkExternal, u.AbsoluteUri), ["Abrir en el navegador", "Cancelar"], 1, 1) != 0) return;
        try
        {
            Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    // ================================================================== arrastrar y soltar, recientes

    private static bool IsPdfDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0;

    private void OnDragOverFile(object sender, DragEventArgs e)
    {
        e.Effects = IsPdfDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDropFile(object sender, DragEventArgs e)
    {
        if (!IsPdfDrop(e)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        await SafeRunAsync(() => OpenFileAsync(files[0]));
    }

    private void RefreshRecents()
    {
        var items = _settings.Recent.Where(r => File.Exists(r.Path)).Take(8).Select(r => new RecentItem(r)).ToList();
        RecentList.ItemsSource = items;
        RecentList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentTitle.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmpty.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnRecentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentItem item) return;
        RecentList.SelectedItem = null;
        await SafeRunAsync(() => OpenFileAsync(item.Path));
    }
}
