// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Velora.App.Viewer;
using Velora.Pdf;

namespace Velora.App;

public sealed class LinkHoverEventArgs(PageLink? link, Rect viewRect) : EventArgs
{
    public PageLink? Link { get; } = link;
    public Rect ViewRect { get; } = viewRect;
}

/// <summary>
/// Vista previa de a dónde lleva un vínculo, como en Papers: al dejar el puntero un momento sobre un vínculo interno aparece una miniatura de la zona
/// de destino; sobre uno externo, la dirección. Se dibuja una sola vez por vínculo, a poca resolución y se cancela al mover el puntero.
/// </summary>
public partial class MainWindow
{
    private const int PreviewWidthPx = 420, PreviewCropHeightPx = 260;

    private DispatcherTimer? _previewTimer;
    private Popup? _previewPopup;
    private Image? _previewImage;
    private TextBlock? _previewCaption;
    private CancellationTokenSource? _previewCts;
    private LinkHoverEventArgs? _previewPending;
    private readonly Dictionary<(PdfDocument Doc, int Page, int Y), BitmapSource> _previewCache = new();

    private void InitLinkPreview()
    {
        View.LinkHover += OnLinkHover;
        View.ViewScrolled += (_, _) => HideLinkPreview();
    }

    private void OnLinkHover(object? sender, LinkHoverEventArgs e)
    {
        HideLinkPreview();
        if (e.Link == null || _doc == null) return;
        _previewPending = e;
        _previewTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _previewTimer.Tick -= OnPreviewDelay;
        _previewTimer.Tick += OnPreviewDelay;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void HideLinkPreview()
    {
        _previewTimer?.Stop();
        _previewPending = null;
        _previewCts?.Cancel();
        _previewCts = null;
        if (_previewPopup != null) _previewPopup.IsOpen = false;
    }

    private async void OnPreviewDelay(object? sender, EventArgs e)
    {
        _previewTimer?.Stop();
        if (_previewPending is not { Link: { } link } hover || _doc == null) return;
        var doc = _doc;
        var cts = _previewCts = new CancellationTokenSource();
        try
        {
            if (link.Kind == LinkKind.Uri)
            {
                ShowPreview(hover.ViewRect, null, link.Uri ?? "");
                return;
            }

            if (link.TargetPage < 0 || link.TargetPage >= _sizes.Length) return;
            var image = await RenderPreviewAsync(doc, link, cts.Token);
            if (cts.IsCancellationRequested || doc != _doc || image == null) return;
            ShowPreview(hover.ViewRect, image, $"Página {link.TargetPage + 1}");
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    /// <summary>La zona de la página de destino (desde un poco por encima del punto al que apunta el vínculo) a poca resolución.</summary>
    private async Task<BitmapSource?> RenderPreviewAsync(PdfDocument doc, PageLink link, CancellationToken ct)
    {
        var size = _sizes[link.TargetPage];
        int rot = View.Rotation & 3;
        double pageW = rot % 2 == 0 ? size.Width : size.Height, pageH = rot % 2 == 0 ? size.Height : size.Width;
        int pw = PreviewWidthPx, ph = Math.Max(1, (int)Math.Round(pageH * pw / pageW));
        double k = pw / pageW;
        int cropH = Math.Min(ph, PreviewCropHeightPx);
        int y = 0;
        if (!float.IsNaN(link.TargetY) && rot == 0) y = (int)Math.Clamp(link.TargetY * k - 36, 0, Math.Max(0, ph - cropH));
        var key = (doc, link.TargetPage, y / 8);
        if (_previewCache.TryGetValue(key, out var cached)) return cached;

        using var tile = await doc.RenderTileAsync(new TileRequest(link.TargetPage, pw, ph, rot, 0, y, pw, cropH), PdfPriority.Urgente, ct);
        var bmp = BitmapSource.Create(tile.Width, tile.Height, 96, 96, PixelFormats.Pbgra32, null, tile.Pixels, tile.Stride);
        bmp.Freeze();
        if (_previewCache.Count >= 12) _previewCache.Remove(_previewCache.Keys.First());
        _previewCache[key] = bmp;
        return bmp;
    }

    /// <summary>Prueba: guarda en un PNG lo que muestra la vista previa abierta (la ventana flotante no sale en las capturas de la ventana).</summary>
    internal bool AutoSavePreview(string file)
    {
        if (_previewPopup is not { IsOpen: true, Child: FrameworkElement el }) return false;
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = new Size(Math.Ceiling(el.ActualWidth), Math.Ceiling(el.ActualHeight));
        var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        var back = new DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle((Brush)FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(size));
        bmp.Render(back);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(file);
        enc.Save(fs);
        return true;
    }

    private void ShowPreview(Rect viewRect, BitmapSource? image, string caption)
    {
        if (_previewPopup == null)
        {
            _previewImage = new Image { Stretch = Stretch.None, SnapsToDevicePixels = true };
            _previewCaption = new TextBlock { Margin = new Thickness(2, 6, 2, 0), FontSize = 12, Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = PreviewWidthPx };
            var inner = new StackPanel { Margin = new Thickness(8) };
            inner.Children.Add(new Border { Child = _previewImage, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), SnapsToDevicePixels = true });
            inner.Children.Add(_previewCaption);
            var frame = new Border
            {
                Child = inner,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 },
            };
            frame.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorSecondaryBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            ((Border)inner.Children[0]).SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            frame.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            _previewPopup = new Popup { AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, Placement = PlacementMode.Bottom, Child = frame, PopupAnimation = PopupAnimation.Fade };
        }

        _previewImage!.Source = image;
        _previewImage.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible;
        ((Border)_previewImage.Parent).BorderThickness = image == null ? new Thickness(0) : new Thickness(1);
        _previewCaption!.Text = caption;
        _previewPopup.PlacementTarget = View;
        // Debajo del vínculo; si no cabe, encima
        double need = (image?.PixelHeight ?? 0) + 70;
        bool below = viewRect.Bottom + need < View.ActualHeight || viewRect.Top < need;
        _previewPopup.Placement = PlacementMode.Relative;
        _previewPopup.HorizontalOffset = Math.Clamp(viewRect.Left, 0, Math.Max(0, View.ActualWidth - PreviewWidthPx - 40));
        _previewPopup.VerticalOffset = below ? viewRect.Bottom + 2 : viewRect.Top - need - 2;
        _previewPopup.IsOpen = true;
    }
}
