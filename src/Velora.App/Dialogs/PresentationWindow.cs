// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Velora.Pdf;

namespace Velora.App;

/// <summary>Modo presentación: una página a pantalla completa. Flechas, espacio o clic para avanzar; Esc para salir.</summary>
internal sealed class PresentationWindow : Window
{
    private readonly PdfDocument _doc;
    private readonly PageSize[] _sizes;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _label = new() { Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(0, 0, 18, 14), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0 };
    private readonly Dictionary<int, BitmapSource> _cache = new();
    private readonly DispatcherTimer _fade;
    private CancellationTokenSource? _cts;
    private int _page;
    private bool _black;

    public PresentationWindow(PdfDocument doc, PageSize[] sizes, int startPage)
    {
        _doc = doc;
        _sizes = sizes;
        _page = Math.Clamp(startPage, 0, sizes.Length - 1);
        LastPage = _page;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        Cursor = Cursors.Arrow;
        var grid = new Grid();
        grid.Children.Add(_image);
        grid.Children.Add(_label);
        Content = grid;
        _fade = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fade.Tick += (_, _) =>
        {
            _fade.Stop();
            _label.Opacity = 0;
            Cursor = Cursors.None;
        };
        Loaded += (_, _) => Show(_page);
        SizeChanged += (_, _) => { _cache.Clear(); Show(_page); };
        KeyDown += OnKey;
        MouseLeftButtonDown += (_, _) => Move(+1);
        MouseRightButtonDown += (_, _) => Move(-1);
        MouseWheel += (_, e) => Move(e.Delta < 0 ? +1 : -1);
        MouseMove += (_, _) => Cursor = Cursors.Arrow;
    }

    public int LastPage { get; private set; }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.Right: case Key.Down: case Key.Space: case Key.PageDown: case Key.Enter: Move(+1); break;
            case Key.Left: case Key.Up: case Key.PageUp: case Key.Back: Move(-1); break;
            case Key.Home: Show(0); break;
            case Key.End: Show(_sizes.Length - 1); break;
            case Key.B: case Key.OemPeriod:
                _black = !_black;
                _image.Visibility = _black ? Visibility.Hidden : Visibility.Visible;
                break;
            default: return;
        }

        e.Handled = true;
    }

    private void Move(int d) => Show(Math.Clamp(_page + d, 0, _sizes.Length - 1));

    private (int W, int H) TargetPixels(int page)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double aw = ActualWidth * dpi.DpiScaleX, ah = ActualHeight * dpi.DpiScaleY;
        var s = _sizes[page];
        double k = Math.Min(aw / s.Width, ah / s.Height);
        return (Math.Max(1, (int)(s.Width * k)), Math.Max(1, (int)(s.Height * k)));
    }

    private async void Show(int page)
    {
        _page = page;
        LastPage = page;
        _label.Text = $"{page + 1} / {_sizes.Length}";
        _label.Opacity = 0.9;
        _fade.Stop();
        _fade.Start();
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            if (!_cache.TryGetValue(page, out var bmp))
            {
                bmp = await Render(page, PdfPriority.Interactivo, cts.Token);
                _cache[page] = bmp;
            }

            if (cts.IsCancellationRequested) return;
            _image.Source = bmp;
            foreach (int old in _cache.Keys.Where(k => Math.Abs(k - page) > 2).ToList()) _cache.Remove(old);
            if (page + 1 < _sizes.Length && !_cache.ContainsKey(page + 1))
            {
                var next = await Render(page + 1, PdfPriority.Baja, cts.Token);
                _cache[page + 1] = next;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    private async Task<BitmapSource> Render(int page, PdfPriority prio, CancellationToken ct)
    {
        var (w, h) = TargetPixels(page);
        using var tile = await _doc.RenderTileAsync(new TileRequest(page, w, h, 0, 0, 0, w, h), prio, ct).ConfigureAwait(false);
        var bmp = BitmapSource.Create(tile.Width, tile.Height, 96, 96, PixelFormats.Pbgra32, null, tile.Pixels, tile.Stride);
        bmp.Freeze();
        return bmp;
    }
}
