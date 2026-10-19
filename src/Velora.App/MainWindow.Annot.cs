// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Velora.App.Viewer;
using Velora.Pdf;

namespace Velora.App;

public partial class MainWindow
{
    private static Color ColorOf(uint argb) => Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    // ================================================================== color

    private uint CurrentColor => View.Tool == ViewTool.Ink ? _inkColor : _markupColor;

    private void BuildColorDots()
    {
        ColorDots.Children.Clear();
        foreach (uint c in Palette)
        {
            var b = new Button { Style = (Style)FindResource("ColorDot"), Background = new SolidColorBrush(ColorOf(c)), Tag = c, ToolTip = "Usar este color" };
            b.Click += (_, _) =>
            {
                if (View.Tool == ViewTool.Ink) { _inkColor = c; View.InkColor = c; }
                else _markupColor = c;
                UpdateColorSwatch();
                ColorPopup.Visibility = Visibility.Collapsed;
            };
            ColorDots.Children.Add(b);
        }
    }

    private void UpdateColorSwatch()
    {
        ColorSwatch.Background = new SolidColorBrush(ColorOf(CurrentColor));
        View.InkColor = _inkColor;
    }

    private void OnColorButton(object sender, RoutedEventArgs e)
    {
        ColorPopup.Visibility = ColorPopup.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // ================================================================== herramientas

    private void SetTool(ViewTool tool)
    {
        View.Tool = tool;
        NoteToggle.IsChecked = tool == ViewTool.Note;
        InkToggle.IsChecked = tool == ViewTool.Ink;
        if (tool != ViewTool.Select) View.ClearSelection();
        UpdateColorSwatch();
        View.Cursor = tool == ViewTool.Select ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Cross;
        View.Focus();
    }

    // ================================================================== barra flotante sobre la selección

    private void OnViewSelection(object? sender, Rect? anchor)
    {
        if (anchor is not { } r || !View.HasSelection || _doc == null)
        {
            HideSelectionPopup();
            return;
        }

        SelectionPopup.Visibility = Visibility.Visible;
        SelectionPopup.UpdateLayout();
        SelectionPopup.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = SelectionPopup.DesiredSize;
        double areaW = ViewerArea.ActualWidth, areaH = ViewerArea.ActualHeight;
        double x = Math.Clamp(r.X - 8, 8, Math.Max(8, areaW - size.Width - 24));
        double y = r.Y - size.Height - 8;
        if (y < 8) y = Math.Min(areaH - size.Height - 8, r.Bottom + 8);
        SelectionPopup.Margin = new Thickness(x, y, 0, 0);
        CommandManager.InvalidateRequerySuggested();
    }

    private void HideSelectionPopup()
    {
        if (SelectionPopup.Visibility == Visibility.Visible) SelectionPopup.Visibility = Visibility.Collapsed;
    }

    private void HideFloaters()
    {
        HideSelectionPopup();
        HideNotePanel();
        ColorPopup.Visibility = Visibility.Collapsed;
    }

    private async void OnPopupCopy(object sender, RoutedEventArgs e) => await SafeRunAsync(CopySelectionAsync);

    private async Task CopySelectionAsync()
    {
        await View.CopySelectionAsync();
        ShowInfo(T.CopiedText);
    }

    private async Task ApplyMarkupAsync(MarkupKind kind)
    {
        HideSelectionPopup();
        await View.ApplyMarkupAsync(kind, _markupColor, Environment.UserName);
        UpdateTitle();
    }

    // ================================================================== notas

    private void PlaceNotePanel(Point at)
    {
        NotePanel.Visibility = Visibility.Visible;
        NotePanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = NotePanel.DesiredSize;
        double x = Math.Clamp(at.X + 14, 8, Math.Max(8, ViewerArea.ActualWidth - 280 - 24));
        double y = Math.Clamp(at.Y - 10, 8, Math.Max(8, ViewerArea.ActualHeight - size.Height - 8));
        NotePanel.Margin = new Thickness(x, y, 0, 0);
    }

    private void ShowNoteEditor(NoteRequest req)
    {
        _noteExisting = null;
        _notePage = req.Page;
        _noteU = req.U;
        _noteV = req.V;
        NoteTitle.Text = "Nueva nota";
        NoteText.Text = "";
        NoteText.IsReadOnly = false;
        NoteOk.Content = "Guardar";
        NoteDelete.Visibility = Visibility.Collapsed;
        PlaceNotePanel(req.ViewPoint);
        NoteText.Focus();
    }

    private void ShowNoteViewer(AnnotationClick c)
    {
        _noteExisting = c;
        NoteTitle.Text = $"{T.AnnotKind(c.Annotation.Kind)} · página {c.Page + 1}" + (c.Annotation.Author.Length > 0 ? $" · {c.Annotation.Author}" : "");
        NoteText.Text = c.Annotation.Contents;
        NoteText.IsReadOnly = true;
        NoteOk.Content = "Cerrar";
        NoteDelete.Visibility = Visibility.Visible;
        PlaceNotePanel(new Point(c.ViewRect.Right, c.ViewRect.Top));
    }

    private void HideNotePanel()
    {
        if (NotePanel.Visibility != Visibility.Visible) return;
        NotePanel.Visibility = Visibility.Collapsed;
        _noteExisting = null;
        View.Focus();
    }

    private async void OnNoteOk(object sender, RoutedEventArgs e)
    {
        if (_noteExisting != null)
        {
            HideNotePanel();
            return;
        }

        string text = NoteText.Text.Trim();
        HideNotePanel();
        if (text.Length == 0) return;
        await SafeRunAsync(async () =>
        {
            await View.AddNoteAsync(_notePage, _noteU, _noteV, text, _markupColor, Environment.UserName);
            SetTool(ViewTool.Select);
            UpdateTitle();
        });
    }

    private void OnNoteCancel(object sender, RoutedEventArgs e) => HideNotePanel();

    private async void OnNoteDelete(object sender, RoutedEventArgs e)
    {
        var c = _noteExisting;
        HideNotePanel();
        if (c == null) return;
        await SafeRunAsync(async () =>
        {
            await View.RemoveAnnotationAsync(c.Page, c.Annotation.Index);
            UpdateTitle();
        });
    }

    // ---- órdenes para las pruebas automáticas ----
    internal async Task AutoMarkupAsync(MarkupKind kind) => await ApplyMarkupAsync(kind);

    internal void AutoSidebar(string what)
    {
        if (what == "ocultar") { _settings.SidebarVisible = false; ApplySidebar(); return; }
        _settings.SidebarVisible = true;
        ApplySidebar();
        ShowSidebarTab(what switch { "indice" => "outline", "anotaciones" => "annots", "adjuntos" => "attach", _ => "thumbs" });
    }

    internal async Task AutoSearchAsync(string text)
    {
        ShowSearch();
        SearchBox.Text = text;
        _searchDebounce?.Stop();
        StartSearch();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!_searchDone && sw.ElapsedMilliseconds < 60000) await Task.Delay(20);
    }

    internal void AutoSetTool(string tool) => SetTool(tool switch { "nota" => ViewTool.Note, "tinta" => ViewTool.Ink, _ => ViewTool.Select });

    internal bool NotePanelVisible => NotePanel.Visibility == Visibility.Visible;
    internal bool SelectionPopupVisible => SelectionPopup.Visibility == Visibility.Visible;

    internal void AutoConfirmNote(string text)
    {
        NoteText.Text = text;
        OnNoteOk(this, new RoutedEventArgs());
    }
}
