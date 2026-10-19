// SPDX-License-Identifier: GPL-3.0-or-later
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velora.Pdf;

namespace Velora.App;

/// <summary>Una miniatura de la barra lateral. La imagen se pide solo cuando la lista la necesita.</summary>
public sealed class ThumbItem : INotifyPropertyChanged
{
    private readonly ThumbLoader _loader;
    private BitmapSource? _image;

    public ThumbItem(ThumbLoader loader, int page, double width, double height)
    {
        _loader = loader;
        Page = page;
        Width = width;
        Height = height;
    }

    public int Page { get; }
    public double Width { get; }
    public double Height { get; }
    public string Label => (Page + 1).ToString();
    internal bool Loading { get; set; }
    internal CancellationTokenSource? Cts { get; set; }

    public ImageSource? Image
    {
        get
        {
            if (_image == null) _loader.Request(this);
            return _image;
        }
    }

    internal void SetImage(BitmapSource? img)
    {
        _image = img;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Dibuja las miniaturas en segundo plano (prioridad «alta») y se queda con las últimas para no gastar memoria.</summary>
public sealed class ThumbLoader
{
    private const int Keep = 260;
    private readonly PdfDocument _doc;
    private readonly Queue<ThumbItem> _loaded = new();
    private readonly double _dpi;
    private readonly System.Windows.Threading.Dispatcher _ui;
    private bool _disposed;

    public ThumbLoader(PdfDocument doc, double dpi)
    {
        _doc = doc;
        _dpi = dpi;
        _ui = System.Windows.Application.Current.Dispatcher;
    }

    public Func<int> CurrentPage { get; set; } = () => 0;

    public void Request(ThumbItem item)
    {
        if (item.Loading || _disposed) return;
        item.Loading = true;
        var cts = new CancellationTokenSource();
        item.Cts = cts;
        _ = LoadAsync(item, cts);
    }

    public void Cancel(ThumbItem item)
    {
        if (!item.Loading) return;
        item.Cts?.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var t in _loaded) t.Cts?.Cancel();
        _loaded.Clear();
    }

    private async Task LoadAsync(ThumbItem item, CancellationTokenSource cts)
    {
        try
        {
            int pw = Math.Max(8, (int)Math.Round(item.Width * _dpi)), ph = Math.Max(8, (int)Math.Round(item.Height * _dpi));
            BitmapSource bmp;
            int sub = Math.Abs(item.Page - CurrentPage());
            using (var tile = await _doc.RenderTileAsync(new TileRequest(item.Page, pw, ph, 0, 0, 0, pw, ph), PdfPriority.Alta, cts.Token, sub).ConfigureAwait(false))
            {
                bmp = BitmapSource.Create(tile.Width, tile.Height, 96, 96, PixelFormats.Pbgra32, null, tile.Pixels, tile.Stride);
                bmp.Freeze();
            }

            await _ui.InvokeAsync(() =>
            {
                item.Loading = false;
                if (_disposed) return;
                item.SetImage(bmp);
                _loaded.Enqueue(item);
                while (_loaded.Count > Keep) _loaded.Dequeue().SetImage(null);
            });
        }
        catch (OperationCanceledException)
        {
            await _ui.InvokeAsync(() => item.Loading = false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            await _ui.InvokeAsync(() => item.Loading = false);
        }
    }
}

public sealed class AnnotItem
{
    public AnnotItem(int page, PageAnnotation annotation)
    {
        Page = page;
        Annotation = annotation;
        var c = Color.FromRgb((byte)(annotation.ColorArgb >> 16), (byte)(annotation.ColorArgb >> 8), (byte)annotation.ColorArgb);
        var b = new SolidColorBrush(c);
        b.Freeze();
        ColorBrush = b;
    }

    public int Page { get; }
    public PageAnnotation Annotation { get; }
    public Brush ColorBrush { get; }
    public string Title => $"{T.AnnotKind(Annotation.Kind)} · página {Page + 1}";
    public string Detail => Annotation.Contents.Trim();
    public Visibility DetailVisibility => Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class RecentItem
{
    public RecentItem(RecentFile file)
    {
        File = file;
        Name = System.IO.Path.GetFileName(file.Path);
        string dir = System.IO.Path.GetDirectoryName(file.Path) ?? "";
        Detail = $"{dir}  ·  {string.Format(T.RecentPage, file.Page + 1)}";
    }

    public RecentFile File { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Path => File.Path;
}
