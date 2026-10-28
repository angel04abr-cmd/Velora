// SPDX-License-Identifier: GPL-3.0-or-later
// Visor de páginas. La idea (dibujar solo lo que se ve, con una caché y una cola de tareas con
// prioridades que se cancelan al hacer scroll) está inspirada en libview de Papers/GNOME
// (pps-view.c, pps-pixbuf-cache.c); el código es propio.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Velora.Pdf;
using Velora.Pdf.Viewer;

namespace Velora.App.Viewer;

public enum ZoomMode { Custom, FitWidth, FitPage }

public enum ViewTool { Select, Note, Ink }

public sealed record NoteRequest(int Page, float U, float V, Point ViewPoint);

public sealed record AnnotationClick(int Page, PageAnnotation Annotation, Rect ViewRect);

public sealed class PdfView : FrameworkElement, IScrollInfo
{
    public const double MinZoom = 0.05, MaxZoom = 8.0;
    private const double Gap = 12, PageMargin = 16;
    private const int PrefetchBefore = 1, PrefetchAfter = 2;
    private static readonly double[] ZoomSteps = [0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 3, 4, 6, 8];

    private readonly record struct TileKey(int Page, int PxW, int PxH, int Rot, int Col, int Row, int Rev);

    private sealed class Pending(CancellationTokenSource cts, int priority)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public int Priority { get; } = priority;
    }

    private sealed class Preview
    {
        public int Rot;
        public readonly Dictionary<(int, int), (double X0, double Y0, double X1, double Y1, TileBmp Bmp)> Tiles = new();
    }

    /// <summary>
    /// Imagen de una baldosa. Se reutilizan los mapas de bits al expulsarlos de la caché: WPF libera la memoria de un
    /// BitmapSource solo cuando el recolector de basura lo decide, y con páginas grandes eso llegaba a ocupar gigabytes.
    /// </summary>
    private sealed class TileBmp(WriteableBitmap bmp, int w, int h)
    {
        public WriteableBitmap Bmp { get; } = bmp;
        public int W { get; } = w;
        public int H { get; } = h;
        public bool Retired { get; set; }
    }

    private const long MaxFreeBitmapBytes = 48L << 20;
    private readonly Dictionary<(int W, int H), Stack<WriteableBitmap>> _free = new();
    private long _freeBytes;

    private enum Drag { None, Pan, Select, Ink }

    // ---- documento y disposición ----
    private PdfDocument? _doc;
    private PageSize[] _sizes = [];
    private PageLayout? _layout;
    private long _generation;
    private CancellationTokenSource _lifetime = new();
    private ZoomMode _zoomMode = ZoomMode.FitWidth;
    private double _zoom = 1.0, _scale = 96.0 / 72;
    private int _rot, _cols = 1, _currentPage;
    private bool _cover;
    private double _dpi = 1;

    // ---- desplazamiento ----
    private double _offX, _offY, _originX, _targetY;
    private Size _viewport, _extent;
    private bool _animating;

    // ---- caché y trabajo en curso ----
    private readonly ByteLruCache<TileKey, TileBmp> _cache;
    private readonly Dictionary<TileKey, Pending> _pending = new();
    private readonly Dictionary<int, Preview> _previews = new();
    private readonly Dictionary<int, int> _rev = new();
    private readonly Dictionary<int, PageContent> _contents = new();
    private readonly Dictionary<int, Task<PageContent>> _contentTasks = new();
    private readonly Dictionary<int, CancellationTokenSource> _contentCts = new();
    // Lo que falló no se vuelve a pedir en cada cuadro (una página dañada provocaría un bucle de errores).
    private readonly HashSet<TileKey> _failedTiles = new();
    private readonly HashSet<int> _failedContent = new();

    // ---- interacción ----
    private Drag _drag;
    private Point _dragStart, _pressPt;
    private double _dragOffX, _dragOffY;
    private bool _moved;
    private PageLink? _pressLink;
    private PageAnnotation? _pressAnnot;
    private int _pressPage;
    private (int Page, int Idx)? _anchor, _focus;
    private int _inkPage;
    private readonly List<Point> _inkPoints = new();
    private readonly DispatcherTimer _autoScroll;
    private Point _lastMouse;
    private Dictionary<int, List<RectF>> _hitRects = new();
    private List<RectF> _currentHit = new();
    private int _currentHitPage = -1;

    // ---- aspecto ----
    private Brush _backdrop = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private Brush _selectionBrush = new SolidColorBrush(Color.FromArgb(90, 0x00, 0x78, 0xD4));
    private static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(110, 255, 214, 0)));
    private static readonly Brush CurrentHitBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 128, 0)));
    private static readonly Brush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)));

    public PdfView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        // Cada baldosa ocupa en realidad unas 2,4 veces su tamaño (WPF guarda además una copia para la tarjeta gráfica),
        // por eso el presupuesto es modesto: unas 30 páginas a tamaño normal.
        long budget = Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 32, 64L << 20, 128L << 20);
        // Se puede cambiar con la variable de entorno VELORA_CACHE_MB (útil en equipos con poca memoria).
        if (long.TryParse(Environment.GetEnvironmentVariable("VELORA_CACHE_MB"), out long mb) && mb >= 16) budget = mb << 20;
        _cache = new ByteLruCache<TileKey, TileBmp>(budget, b => (long)b.W * b.H * 4, Recycle);
        _autoScroll = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScroll.Tick += (_, _) => AutoScrollTick();
        Loaded += (_, _) => _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    // ================================================================== propiedades y eventos

    public event EventHandler? CurrentPageChanged;
    public event EventHandler? ZoomChanged;
    public event EventHandler? ViewScrolled;
    public event EventHandler<Rect?>? SelectionChanged;
    public event EventHandler<string>? UriRequested;
    public event EventHandler<NoteRequest>? NoteRequested;
    public event EventHandler<AnnotationClick>? AnnotationClicked;
    public event EventHandler<int>? AnnotationsChanged;
    public event EventHandler<int>? JumpedFromPage;
    public event EventHandler<Exception>? ErrorOccurred;

    public PdfDocument? Document => _doc;
    public int PageCount => _sizes.Length;
    public int CurrentPage => _currentPage;
    public double Zoom => _zoom;
    public ZoomMode ZoomMode => _zoomMode;
    public int Rotation => _rot;
    public int Columns => _cols;
    public bool CoverAlone => _cover;
    public ViewTool Tool { get; set; } = ViewTool.Select;
    public uint InkColor { get; set; } = 0xFFE53935;
    public float InkWidth { get; set; } = 2.5f;
    public bool HasSelection => OrderedSelection() != null;

    public Brush Backdrop
    {
        get => _backdrop;
        set { _backdrop = value; InvalidateVisual(); }
    }

    public Brush SelectionBrush
    {
        get => _selectionBrush;
        set { _selectionBrush = value; InvalidateVisual(); }
    }

    /// <summary>Páginas pedidas, dibujos en espera y memoria usada (para pruebas y diagnóstico).</summary>
    public (int Pending, int CachedTiles, long CacheBytes) Stats => (_pending.Count, _cache.Count, _cache.Bytes);

    // ================================================================== abrir / cerrar

    /// <summary>Fija zoom, giro y columnas antes de abrir un documento.</summary>
    public void Configure(ZoomMode mode, double zoom, int rotation, int columns, bool coverAlone)
    {
        _zoomMode = mode;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _rot = ((rotation % 4) + 4) % 4;
        _cols = columns >= 2 ? 2 : 1;
        _cover = coverAlone;
    }

    /// <summary>Texto seleccionado si ya está todo cargado (sin esperas); si no, null.</summary>
    public string? SelectedTextIfCached()
    {
        if (OrderedSelection() is not { } s) return null;
        var sb = new System.Text.StringBuilder();
        for (int p = s.A.Page; p <= s.B.Page; p++)
        {
            if (!_contents.TryGetValue(p, out var c)) return null;
            int from = p == s.A.Page ? s.A.Idx : 0;
            int to = p == s.B.Page ? s.B.Idx : c.CharCount - 1;
            if (sb.Length > 0) sb.Append("\r\n");
            sb.Append(TextMath.TextOfRange(c, from, to));
        }

        return sb.ToString();
    }

    public void Attach(PdfDocument doc, PageSize[] sizes, int startPage = 0)
    {
        Detach();
        _doc = doc;
        _sizes = sizes;
        _generation++;
        _lifetime = new CancellationTokenSource();
        _dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _offX = _offY = _targetY = 0;
        ApplyLayout(null, default);
        GoToPage(Math.Clamp(startPage, 0, sizes.Length - 1));
    }

    public void Detach()
    {
        _generation++;
        _lifetime.Cancel();
        foreach (var p in _pending.Values) p.Cts.Cancel();
        _pending.Clear();
        _cache.Clear();
        _free.Clear();
        _freeBytes = 0;
        _previews.Clear();
        _rev.Clear();
        _contents.Clear();
        _contentTasks.Clear();
        _contentCts.Clear();
        _failedTiles.Clear();
        _failedContent.Clear();
        _anchor = _focus = null;
        _hitRects.Clear();
        _currentHit.Clear();
        _doc = null;
        _sizes = [];
        _layout = null;
        _extent = default;
        _currentPage = 0;
        _drag = Drag.None;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    // ================================================================== disposición y zoom

    private void ApplyLayout(PagePoint? anchor, Point anchorView)
    {
        if (_doc == null || _sizes.Length == 0) return;
        double vw = Math.Max(50, _viewport.Width), vh = Math.Max(50, _viewport.Height);
        double scale;
        switch (_zoomMode)
        {
            case ZoomMode.FitWidth:
                scale = PageLayout.FitWidthScale(_sizes, _rot, vw, _cols, Gap, PageMargin);
                break;
            case ZoomMode.FitPage:
                scale = PageLayout.FitPageScale(_sizes[Math.Clamp(anchor?.Page ?? _currentPage, 0, _sizes.Length - 1)], _rot, vw, vh, _cols, Gap, PageMargin);
                break;
            default:
                scale = _zoom * 96 / 72;
                break;
        }

        scale = Math.Clamp(scale, MinZoom * 96 / 72, MaxZoom * 96 / 72);
        _scale = scale;
        _zoom = scale * 72 / 96;
        _layout = new PageLayout(_sizes, _rot, _scale, _dpi, _cols, _cover, Gap, PageMargin);
        _extent = new Size(_layout.ExtentWidth, _layout.ExtentHeight);
        _originX = Math.Max(0, (vw - _extent.Width) / 2);
        if (anchor is { } a)
        {
            var (x, y) = _layout.PointOf(a);
            _offX = x + _originX - anchorView.X;
            _offY = y - anchorView.Y;
        }

        ClampOffsets();
        _targetY = _offY;
        ScrollOwner?.InvalidateScrollInfo();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        AfterViewChange();
    }

    private PagePoint? AnchorAt(Point viewPoint) =>
        _layout?.Locate(viewPoint.X - _originX + _offX, viewPoint.Y + _offY);

    public void SetZoom(double zoom, Point? anchorView = null)
    {
        if (_layout == null) return;
        _zoomMode = ZoomMode.Custom;
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var pt = anchorView ?? new Point(_viewport.Width / 2, _viewport.Height / 2);
        ApplyLayout(AnchorAt(pt), pt);
    }

    public void ZoomIn(Point? anchorView = null)
    {
        double z = ZoomSteps.FirstOrDefault(s => s > _zoom * 1.01, MaxZoom);
        SetZoom(z, anchorView);
    }

    public void ZoomOut(Point? anchorView = null)
    {
        double z = ZoomSteps.LastOrDefault(s => s < _zoom / 1.01, MinZoom);
        SetZoom(z, anchorView);
    }

    public void SetZoomMode(ZoomMode mode)
    {
        if (_layout == null) return;
        _zoomMode = mode;
        int page = _currentPage;
        if (mode == ZoomMode.Custom)
        {
            ApplyLayout(AnchorAt(new Point(0, 0)), new Point(0, 0));
            return;
        }

        ApplyLayout(new PagePoint(page, 0, 0), new Point(0, 0));
        GoToPage(page);
    }

    public void Rotate(int quarterTurns)
    {
        if (_layout == null) return;
        var anchor = AnchorAt(new Point(0, 0));
        _rot = ((_rot + quarterTurns) % 4 + 4) % 4;
        _previews.Clear();
        ApplyLayout(anchor, new Point(0, 0));
    }

    public void SetRotation(int rot)
    {
        if (_layout == null) return;
        _rot = ((rot % 4) + 4) % 4;
        _previews.Clear();
        ApplyLayout(new PagePoint(_currentPage, 0, 0), new Point(0, 0));
        GoToPage(_currentPage);
    }

    public void SetColumns(int columns, bool coverAlone)
    {
        _cols = columns >= 2 ? 2 : 1;
        _cover = coverAlone;
        if (_layout == null) return;
        int page = _currentPage;
        _previews.Clear();
        ApplyLayout(new PagePoint(page, 0, 0), new Point(0, 0));
        GoToPage(page);
    }

    // ================================================================== medidas y desplazamiento (IScrollInfo)

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height;
        SetViewport(new Size(w, h));
        return new Size(w, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        SetViewport(finalSize);
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpi = newDpi.DpiScaleX;
        if (_layout == null) return;
        _cache.Clear();
        _free.Clear();
        _freeBytes = 0;
        _previews.Clear();
        ApplyLayout(AnchorAt(new Point(0, 0)), new Point(0, 0));
    }

    private void SetViewport(Size size)
    {
        if (size == _viewport) return;
        _viewport = size;
        if (_layout == null) return;
        if (_zoomMode != ZoomMode.Custom)
        {
            ApplyLayout(AnchorAt(new Point(0, 0)), new Point(0, 0));
        }
        else
        {
            _originX = Math.Max(0, (size.Width - _extent.Width) / 2);
            ClampOffsets();
            ScrollOwner?.InvalidateScrollInfo();
            AfterViewChange();
        }
    }

    private void ClampOffsets()
    {
        double maxX = Math.Max(0, _extent.Width - _viewport.Width);
        double maxY = Math.Max(0, _extent.Height - _viewport.Height);
        _offX = Snap(Math.Clamp(_offX, 0, maxX));
        _offY = Snap(Math.Clamp(_offY, 0, maxY));
    }

    private double Snap(double v) => Math.Round(v * _dpi) / _dpi;

    private void SetOffsets(double x, double y, bool stopAnimation = true)
    {
        double ox = _offX, oy = _offY;
        _offX = x;
        _offY = y;
        ClampOffsets();
        if (stopAnimation) _targetY = _offY;
        if (ox == _offX && oy == _offY) return;
        NoteScrollSpeed(Math.Abs(_offY - oy));
        ScrollOwner?.InvalidateScrollInfo();
        AfterViewChange();
        ViewScrolled?.Invoke(this, EventArgs.Empty);
    }

    private void AfterViewChange()
    {
        if (_layout == null) return;
        int page = _layout.MostVisiblePage(_offY, _offY + _viewport.Height);
        if (page != _currentPage)
        {
            _currentPage = page;
            CurrentPageChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
        UpdateWork();
    }

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offX;
    public double VerticalOffset => _offY;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => ScrollBy(-48, animated: true);
    public void LineDown() => ScrollBy(48, animated: true);
    public void LineLeft() => SetOffsets(_offX - 48, _offY);
    public void LineRight() => SetOffsets(_offX + 48, _offY);
    public void MouseWheelUp() => ScrollBy(-100, animated: true);
    public void MouseWheelDown() => ScrollBy(100, animated: true);
    public void MouseWheelLeft() => SetOffsets(_offX - 60, _offY);
    public void MouseWheelRight() => SetOffsets(_offX + 60, _offY);
    public void PageUp() => ScrollBy(-_viewport.Height * 0.9, animated: true);
    public void PageDown() => ScrollBy(_viewport.Height * 0.9, animated: true);
    public void PageLeft() => SetOffsets(_offX - _viewport.Width * 0.9, _offY);
    public void PageRight() => SetOffsets(_offX + _viewport.Width * 0.9, _offY);
    public void SetHorizontalOffset(double offset) => SetOffsets(offset, _offY);
    public void SetVerticalOffset(double offset) => SetOffsets(_offX, offset);
    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    private void ScrollBy(double dy, bool animated)
    {
        if (_layout == null) return;
        if (!animated)
        {
            SetOffsets(_offX, _offY + dy);
            return;
        }

        double max = Math.Max(0, _extent.Height - _viewport.Height);
        _targetY = Math.Clamp((_animating ? _targetY : _offY) + dy, 0, max);
        if (!_animating)
        {
            _animating = true;
            CompositionTarget.Rendering += AnimationStep;
        }
    }

    private void AnimationStep(object? sender, EventArgs e)
    {
        double d = _targetY - _offY;
        if (Math.Abs(d) < 0.75 / _dpi || _layout == null)
        {
            SetOffsets(_offX, _targetY, stopAnimation: false);
            StopAnimation();
            return;
        }

        SetOffsets(_offX, _offY + d * 0.32, stopAnimation: false);
        if (Math.Abs(_targetY - _offY) < 0.75 / _dpi) StopAnimation();
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= AnimationStep;
        _targetY = _offY;
    }

    // ================================================================== navegación

    public void GoToPage(int page, float? v = null)
    {
        if (_layout == null) return;
        StopAnimation();
        page = Math.Clamp(page, 0, _sizes.Length - 1);
        double y;
        if (v is { } vv && !float.IsNaN(vv))
        {
            var sz = _sizes[page];
            var (_, dy) = PageSpace.ToDisplay(_rot, sz.Width, sz.Height, 0, vv);
            var (dw, _) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
            y = _layout.Y(page) + dy * (_layout.Width(page) / dw) - 24;
        }
        else
        {
            y = _layout.RowTop(_layout.RowOf(page)) - (page == 0 ? PageMargin : Gap / 2);
        }

        SetOffsets(_offX, y);
        Focus();
    }

    public bool CanGoPrevious => _layout != null && _layout.FirstPageOfRow(_layout.RowOf(_currentPage)) > 0;
    public bool CanGoNext => _layout != null && _layout.LastPageOfRow(_layout.RowOf(_currentPage)) < _sizes.Length - 1;

    /// <summary>Va a la página (o pareja de páginas) anterior.</summary>
    public void GoToPreviousPage()
    {
        if (_layout == null) return;
        GoToPage(Math.Max(0, _layout.FirstPageOfRow(_layout.RowOf(_currentPage)) - 1));
    }

    /// <summary>Va a la página (o pareja de páginas) siguiente.</summary>
    public void GoToNextPage()
    {
        if (_layout == null) return;
        GoToPage(Math.Min(_sizes.Length - 1, _layout.LastPageOfRow(_layout.RowOf(_currentPage)) + 1));
    }

    /// <summary>Hace visible un rectángulo (coordenadas de página) cuando no está a la vista.</summary>
    public void ScrollToRect(int page, RectF rect)
    {
        if (_layout == null) return;
        var sz = _sizes[page];
        var d = PageSpace.ToDisplay(_rot, sz.Width, sz.Height, rect);
        var (dw, _) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
        double k = _layout.Width(page) / dw;
        double top = _layout.Y(page) + d.Top * k, bottom = _layout.Y(page) + d.Bottom * k;
        double left = _layout.X(page) + d.Left * k, right = _layout.X(page) + d.Right * k;
        double newY = _offY, newX = _offX;
        if (top < _offY + 40 || bottom > _offY + _viewport.Height - 40)
            newY = (top + bottom) / 2 - _viewport.Height / 3;
        double visL = _offX - _originX;
        if (left < visL + 20 || right > visL + _viewport.Width - 20)
            newX = (left + right) / 2 - _viewport.Width / 2 + _originX;
        SetOffsets(newX, newY);
    }

    // ================================================================== trabajo en segundo plano

    private int Rev(int page) => _rev.GetValueOrDefault(page);

    // ---- desplazamiento rápido: primero un borrador, la calidad completa cuando se detiene ----

    private DispatcherTimer? _settleTimer;
    private long _speedTick;
    private double _speed; // píxeles de pantalla por segundo (media suavizada)

    /// <summary>Mientras el desplazamiento es muy rápido solo se piden borradores baratos de las páginas; la calidad completa se pide al detenerse.</summary>
    private bool FastScrolling => _speed > Math.Max(1500, _viewport.Height * 2.5);

    private void NoteScrollSpeed(double delta)
    {
        long now = Environment.TickCount64;
        double dt = Math.Max(1, now - _speedTick) / 1000.0;
        _speedTick = now;
        _speed = dt > 0.25 ? delta / dt : _speed * 0.6 + (delta / dt) * 0.4;
        if (!FastScrolling) return;
        _settleTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(140) };
        _settleTimer.Tick -= OnSettle;
        _settleTimer.Tick += OnSettle;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void OnSettle(object? sender, EventArgs e)
    {
        _settleTimer?.Stop();
        _speed = 0;
        UpdateWork();
        InvalidateVisual();
    }

    /// <summary>Una página entera a muy poca resolución (unos 360 px de ancho como máximo): se ve borrosa pero al instante.</summary>
    private static (int W, int H) DraftSize(int pw, int ph)
    {
        int w = Math.Clamp(pw / 4, 48, 360);
        return (w, Math.Max(1, (int)Math.Round((double)ph * w / pw)));
    }

    private int PxW(int page) => Math.Max(1, (int)Math.Round(_layout!.Width(page) * _dpi));
    private int PxH(int page) => Math.Max(1, (int)Math.Round(_layout!.Height(page) * _dpi));

    private Rect PageRect(int p) =>
        new(Snap(_layout!.X(p) + _originX - _offX), Snap(_layout.Y(p) - _offY), _layout.Width(p), _layout.Height(p));

    private IEnumerable<TileGrid.Tile> VisibleTiles(Rect pageRect, int pw, int ph)
    {
        double vx0 = Math.Max(0, -pageRect.X), vx1 = Math.Min(pageRect.Width, _viewport.Width - pageRect.X);
        double vy0 = Math.Max(0, -pageRect.Y), vy1 = Math.Min(pageRect.Height, _viewport.Height - pageRect.Y);
        if (vx1 <= vx0 || vy1 <= vy0) return [];
        return TileGrid.Visible(pw, ph, vx0 * _dpi, vy0 * _dpi, vx1 * _dpi, vy1 * _dpi);
    }

    private TileKey KeyFor(int page, int pw, int ph, TileGrid.Tile t) => new(page, pw, ph, _rot, t.Column, t.Row, Rev(page));

    private void UpdateWork()
    {
        if (_layout == null || _doc == null || _viewport.Height <= 0 || _dpi <= 0) return;
        var (first, last) = _layout.PagesIn(_offY, _offY + _viewport.Height);
        var wanted = new Dictionary<TileKey, (PdfPriority Prio, int Sub, TileRequest Req)>();
        double mid = _offY + _viewport.Height / 2;
        bool fast = FastScrolling;

        for (int p = first; p <= last; p++)
        {
            var r = PageRect(p);
            if (r.Right < 0 || r.X > _viewport.Width) continue;
            int pw = PxW(p), ph = PxH(p);
            int sub = (int)Math.Abs(_layout.Y(p) + _layout.Height(p) / 2 - mid) / 8;
            var visible = VisibleTiles(r, pw, ph).ToList();
            bool complete = true;
            foreach (var t in visible)
            {
                var key = KeyFor(p, pw, ph, t);
                if (!_cache.TryGet(key, out _)) complete = false;
                if (!fast) wanted[key] = (PdfPriority.Urgente, sub, new TileRequest(p, pw, ph, _rot, t.X, t.Y, t.Width, t.Height));
            }

            // Borrador: lo primero que se dibuja de una página que aún no tiene su imagen completa
            if (!complete && pw > 96)
            {
                var (dw, dh) = DraftSize(pw, ph);
                var dkey = new TileKey(p, dw, dh, _rot, -1, -1, Rev(p));
                wanted[dkey] = (PdfPriority.Urgente, sub - 1_000_000, new TileRequest(p, dw, dh, _rot, 0, 0, dw, dh));
            }
        }

        // Páginas cercanas, para tenerlas listas (solo las que caben en una baldosa).
        for (int d = 1; !fast && d <= Math.Max(PrefetchBefore, PrefetchAfter); d++)
        {
            foreach (int p in new[] { first - d, last + d })
            {
                bool before = p < first;
                if (p < 0 || p >= _sizes.Length) continue;
                if (d > (before ? PrefetchBefore : PrefetchAfter)) continue;
                int pw = PxW(p), ph = PxH(p);
                var (cols, rows, _, _) = TileGrid.Dimensions(pw, ph);
                if (cols * rows != 1) continue;
                var t = new TileGrid.Tile(0, 0, 0, 0, pw, ph);
                wanted.TryAdd(KeyFor(p, pw, ph, t), (PdfPriority.Baja, d, new TileRequest(p, pw, ph, _rot, 0, 0, pw, ph)));
            }
        }

        // Cancelar lo que ya no hace falta
        foreach (var key in _pending.Keys.ToList())
        {
            if (!wanted.ContainsKey(key))
            {
                _pending[key].Cts.Cancel();
                _pending.Remove(key);
            }
        }

        foreach (var (key, w) in wanted)
        {
            if (_cache.TryGet(key, out _) || _failedTiles.Contains(key)) continue;
            if (_pending.TryGetValue(key, out var pd))
            {
                if (pd.Priority <= (int)w.Prio) continue;
                pd.Cts.Cancel();
                _pending.Remove(key);
            }

            Submit(key, w.Req, w.Prio, w.Sub);
        }

        // Texto, enlaces y anotaciones de lo visible
        for (int p = Math.Max(0, first - 1); p <= Math.Min(_sizes.Length - 1, last + 1); p++) GetContent(p);
        PruneContents(first, last);
        PrunePreviews(first, last);
    }

    private void PruneContents(int first, int last)
    {
        // Las lecturas de texto de páginas que ya se dejaron atrás se cancelan: al desplazarse muy rápido se acumulaban miles
        // y el trabajador de PDFium se pasaba minutos con páginas que ya no se ven, dejando las visibles en blanco.
        if (_contentTasks.Count <= 12 && _contents.Count <= 48) return;
        var sel = OrderedSelection();
        foreach (int p in _contentTasks.Keys.Union(_contents.Keys).ToList())
        {
            if (p >= first - 6 && p <= last + 6) continue;
            if (sel is { } s && p >= s.A.Page && p <= s.B.Page) continue;
            _contents.Remove(p);
            _contentTasks.Remove(p);
            if (_contentCts.Remove(p, out var cts)) cts.Cancel();
        }
    }

    private void PrunePreviews(int first, int last)
    {
        foreach (int p in _previews.Keys.ToList())
            if (p < first - 2 || p > last + 2) _previews.Remove(p);
    }

    private void Submit(TileKey key, TileRequest req, PdfPriority prio, int sub)
    {
        var cts = new CancellationTokenSource();
        _pending[key] = new Pending(cts, (int)prio);
        _ = RenderAsync(_doc!, key, req, prio, sub, cts, _generation);
    }

    private async Task RenderAsync(PdfDocument doc, TileKey key, TileRequest req, PdfPriority prio, int sub, CancellationTokenSource cts, long gen)
    {
        try
        {
            var tile = await doc.RenderTileAsync(req, prio, cts.Token, sub).ConfigureAwait(false);
            try
            {
                await Dispatcher.InvokeAsync(() => OnTileReady(key, req, tile, cts, gen));
            }
            finally
            {
                tile.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (gen == _generation && _pending.TryGetValue(key, out var p) && p.Cts == cts) _pending.Remove(key);
                if (gen == _generation && _failedTiles.Add(key))
                    ErrorOccurred?.Invoke(this, new PdfException($"No se pudo dibujar la página {key.Page + 1}: {ex.Message}", ex));
            });
        }
    }

    private WriteableBitmap TakeBitmap(int w, int h)
    {
        if (_free.TryGetValue((w, h), out var stack) && stack.Count > 0)
        {
            _freeBytes -= (long)w * h * 4;
            return stack.Pop();
        }

        return new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
    }

    /// <summary>Una baldosa sale de la caché: se guarda su mapa de bits para la siguiente.</summary>
    private void Recycle(TileBmp t)
    {
        foreach (var pv in _previews.Values)
        {
            foreach (var kv in pv.Tiles.Where(k => ReferenceEquals(k.Value.Bmp, t)).ToList()) pv.Tiles.Remove(kv.Key);
        }

        if (t.Retired) return;
        long bytes = (long)t.W * t.H * 4;
        if (_freeBytes + bytes > MaxFreeBitmapBytes) return;
        if (!_free.TryGetValue((t.W, t.H), out var stack)) _free[(t.W, t.H)] = stack = new Stack<WriteableBitmap>();
        stack.Push(t.Bmp);
        _freeBytes += bytes;
    }

    private void OnTileReady(TileKey key, TileRequest req, RenderedTile tile, CancellationTokenSource cts, long gen)
    {
        if (gen != _generation) return;
        if (_pending.TryGetValue(key, out var p) && p.Cts == cts) _pending.Remove(key);
        else return; // cancelado mientras tanto
        if (key.Rev != Rev(key.Page)) return;

        var wb = TakeBitmap(tile.Width, tile.Height);
        wb.WritePixels(new Int32Rect(0, 0, tile.Width, tile.Height), tile.Pixels, tile.Stride, 0);
        var bmp = new TileBmp(wb, tile.Width, tile.Height);
        _cache.Set(key, bmp);

        if (key.Rot == _rot)
        {
            if (!_previews.TryGetValue(key.Page, out var pv) || pv.Rot != key.Rot)
                _previews[key.Page] = pv = new Preview { Rot = key.Rot };
            pv.Tiles[(key.Col, key.Row)] = ((double)req.X / key.PxW, (double)req.Y / key.PxH,
                (double)(req.X + req.Width) / key.PxW, (double)(req.Y + req.Height) / key.PxH, bmp);
        }

        if (_layout != null)
        {
            var (first, last) = _layout.PagesIn(_offY, _offY + _viewport.Height);
            if (key.Page >= first && key.Page <= last) InvalidateVisual();
        }
    }

    /// <summary>Se llama tras cambiar anotaciones de una página: hay que volver a dibujarla.</summary>
    private void BumpPage(int page)
    {
        _rev[page] = Rev(page) + 1;
        // Las imágenes viejas se quedan como marcador (sin reutilizarse) hasta que llegue la nueva.
        foreach (var kv in _cache.Entries().Where(e => e.Key.Page == page)) kv.Value.Retired = true;
        _cache.RemoveWhere(k => k.Page == page);
        foreach (var key in _pending.Keys.Where(k => k.Page == page).ToList())
        {
            _pending[key].Cts.Cancel();
            _pending.Remove(key);
        }

        _contents.Remove(page);
        _contentTasks.Remove(page);
        _failedContent.Remove(page);
        _failedTiles.RemoveWhere(k => k.Page == page);
        UpdateWork();
        InvalidateVisual();
    }

    // ================================================================== contenido de las páginas (texto, enlaces)

    private PageContent? GetContent(int page)
    {
        if (_contents.TryGetValue(page, out var c)) return c;
        if (_failedContent.Contains(page)) return null;
        _ = ObserveAsync(GetContentAsync(page));
        return null;
    }

    private async Task ObserveAsync(Task<PageContent> t)
    {
        try
        {
            await t;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private Task<PageContent> GetContentAsync(int page, PdfPriority prio = PdfPriority.Urgente)
    {
        if (_contentTasks.TryGetValue(page, out var t)) return t;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _contentCts[page] = cts;
        t = LoadContentAsync(_doc!, page, prio, _generation, cts.Token);
        _contentTasks[page] = t;
        return t;
    }

    private async Task<PageContent> LoadContentAsync(PdfDocument doc, int page, PdfPriority prio, long gen, CancellationToken ct)
    {
        try
        {
            var c = await doc.GetPageContentAsync(page, prio, ct, 50);
            if (gen == _generation)
            {
                _contents[page] = c;
                InvalidateVisual();
            }

            return c;
        }
        catch (Exception ex)
        {
            if (gen == _generation)
            {
                _contentTasks.Remove(page);
                if (ex is not (OperationCanceledException or ObjectDisposedException)) _failedContent.Add(page);
            }

            throw;
        }
    }

    // ================================================================== dibujo

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(_backdrop, null, new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        if (_layout == null || _doc == null) return;
        double px = 1 / _dpi;
        var (first, last) = _layout.PagesIn(_offY, _offY + _viewport.Height);
        var sel = OrderedSelection();
        for (int p = first; p <= last; p++)
        {
            var r = PageRect(p);
            if (r.Right < 0 || r.X > _viewport.Width) continue;
            dc.DrawRectangle(BorderBrush_, null, new Rect(r.X - px, r.Y - px, r.Width + 2 * px, r.Height + 2 * px));
            dc.DrawRectangle(Brushes.White, null, r);
            DrawTiles(dc, p, r);
            DrawOverlays(dc, p, r, sel);
        }

        if (_drag == Drag.Ink && _inkPoints.Count > 1)
        {
            var pen = new Pen(new SolidColorBrush(ColorOf(InkColor)), Math.Max(1, InkWidth * _scale))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(_inkPoints[0], false, false);
                g.PolyLineTo(_inkPoints.Skip(1).ToList(), true, true);
            }

            dc.DrawGeometry(null, pen, geo);
        }
    }

    private static Color ColorOf(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private void DrawTiles(DrawingContext dc, int p, Rect r)
    {
        int pw = PxW(p), ph = PxH(p);
        var tiles = VisibleTiles(r, pw, ph).ToList();
        var found = new List<(TileGrid.Tile T, TileBmp B)>(tiles.Count);
        bool missing = false;
        foreach (var t in tiles)
        {
            if (_cache.TryGet(KeyFor(p, pw, ph, t), out var b)) found.Add((t, b));
            else missing = true;
        }

        if (missing && _previews.TryGetValue(p, out var pv) && pv.Rot == _rot)
        {
            if (pv.Tiles.TryGetValue((-1, -1), out var draft))
                dc.DrawImage(draft.Bmp.Bmp, new Rect(r.X, r.Y, r.Width, r.Height));
            foreach (var kv in pv.Tiles)
            {
                if (kv.Key == (-1, -1)) continue;
                var item = kv.Value;
                dc.DrawImage(item.Bmp.Bmp, new Rect(r.X + item.X0 * r.Width, r.Y + item.Y0 * r.Height, (item.X1 - item.X0) * r.Width, (item.Y1 - item.Y0) * r.Height));
            }
        }

        foreach (var (t, b) in found)
            dc.DrawImage(b.Bmp, new Rect(r.X + t.X / _dpi, r.Y + t.Y / _dpi, t.Width / _dpi, t.Height / _dpi));
    }

    private Rect ToView(int page, Rect pageRect, RectF r)
    {
        var sz = _sizes[page];
        var d = PageSpace.ToDisplay(_rot, sz.Width, sz.Height, r);
        var (dw, _) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
        double k = pageRect.Width / dw;
        return new Rect(pageRect.X + d.Left * k, pageRect.Y + d.Top * k, Math.Max(1, d.Width * k), Math.Max(1, d.Height * k));
    }

    private void DrawOverlays(DrawingContext dc, int p, Rect r, ((int Page, int Idx) A, (int Page, int Idx) B)? sel)
    {
        if (_hitRects.TryGetValue(p, out var hits))
            foreach (var h in hits) dc.DrawRoundedRectangle(HitBrush, null, ToView(p, r, h), 2, 2);
        if (_currentHitPage == p)
            foreach (var h in _currentHit) dc.DrawRoundedRectangle(CurrentHitBrush, null, ToView(p, r, h), 2, 2);

        if (sel is { } s && p >= s.A.Page && p <= s.B.Page && _contents.TryGetValue(p, out var c))
        {
            int from = p == s.A.Page ? s.A.Idx : 0;
            int to = p == s.B.Page ? s.B.Idx : c.CharCount - 1;
            foreach (var rect in TextMath.RectsForRange(c, from, to))
                dc.DrawRectangle(_selectionBrush, null, ToView(p, r, rect));
        }
    }

    // ================================================================== ratón

    private readonly record struct Hit(int Page, float U, float V, bool Inside);

    private Hit? HitTestPage(Point viewPt)
    {
        if (_layout == null) return null;
        var pp = _layout.Locate(viewPt.X - _originX + _offX, viewPt.Y + _offY);
        var sz = _sizes[pp.Page];
        var (dw, dh) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
        var (u, v) = PageSpace.FromDisplay(_rot, sz.Width, sz.Height, pp.FractionX * dw, pp.FractionY * dh);
        bool inside = pp.FractionX is >= 0 and <= 1 && pp.FractionY is >= 0 and <= 1;
        return new Hit(pp.Page, (float)u, (float)v, inside);
    }

    /// <summary>En las pruebas automáticas se ignora el ratón real para que no interfiera con el simulado.</summary>
    internal bool IgnoreRealPointer { get; set; }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (IgnoreRealPointer) return;
        if (PointerDown(e.GetPosition(this), e.ClickCount)) e.Handled = true;
    }

    /// <summary>Pulsación del botón principal en el punto dado (coordenadas de la vista). Devuelve true si se atendió por completo.</summary>
    internal bool PointerDown(Point pt, int clickCount)
    {
        Focus();
        if (_layout == null) return false;
        var hit = HitTestPage(pt);
        if (hit is not { } h) return false;
        _pressPt = pt;
        _lastMouse = pt;
        _moved = false;
        _pressLink = null;
        _pressAnnot = null;
        _pressPage = h.Page;

        if (Tool == ViewTool.Note)
        {
            if (h.Inside) NoteRequested?.Invoke(this, new NoteRequest(h.Page, h.U, h.V, pt));
            return true;
        }

        if (Tool == ViewTool.Ink)
        {
            if (!h.Inside) return false;
            ClearSelection();
            _inkPage = h.Page;
            _inkPoints.Clear();
            _inkPoints.Add(pt);
            _drag = Drag.Ink;
            CaptureMouse();
            return true;
        }

        var content = GetContent(h.Page);
        if (content != null && h.Inside)
        {
            _pressLink = content.Links.FirstOrDefault(l => l.Rect.Contains(h.U, h.V));
            _pressAnnot = content.Annotations.FirstOrDefault(a => a.Kind is AnnotationKind.Nota or AnnotationKind.TextoLibre && a.Rect.Contains(h.U, h.V));
            if (_pressLink != null || _pressAnnot != null)
            {
                ClearSelection();
                BeginPan(pt);
                return false;
            }

            int idx = TextMath.HitTest(content, h.U, h.V, out bool inside);
            if (idx >= 0 && (inside || TextMath.IsOverText(content, h.U, h.V)))
            {
                if (clickCount >= 3)
                {
                    var (a, b) = TextMath.LineAt(content, idx);
                    _anchor = (h.Page, a);
                    _focus = (h.Page, b);
                    _drag = Drag.None;
                    InvalidateVisual();
                    RaiseSelection();
                    return false;
                }

                if (clickCount == 2)
                {
                    var (a, b) = TextMath.WordAt(content, idx);
                    _anchor = (h.Page, a);
                    _focus = (h.Page, b);
                    _drag = Drag.None;
                    InvalidateVisual();
                    RaiseSelection();
                    return false;
                }

                _anchor = _focus = (h.Page, idx);
                _drag = Drag.Select;
                CaptureMouse();
                InvalidateVisual();
                RaiseSelectionCleared();
                return false;
            }
        }

        ClearSelection();
        BeginPan(pt);
        return false;
    }

    private void BeginPan(Point pt)
    {
        _drag = Drag.Pan;
        _dragStart = pt;
        _dragOffX = _offX;
        _dragOffY = _offY;
        StopAnimation();
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IgnoreRealPointer) return;
        PointerMove(e.GetPosition(this));
    }

    internal void PointerMove(Point pt)
    {
        if (_layout == null) return;
        _lastMouse = pt;
        switch (_drag)
        {
            case Drag.Pan:
                if (!_moved && (Math.Abs(pt.X - _pressPt.X) > 4 || Math.Abs(pt.Y - _pressPt.Y) > 4)) _moved = true;
                if (_moved)
                {
                    Cursor = Cursors.SizeAll;
                    SetOffsets(_dragOffX - (pt.X - _dragStart.X), _dragOffY - (pt.Y - _dragStart.Y));
                }

                break;
            case Drag.Select:
                if (!_moved && (Math.Abs(pt.X - _pressPt.X) > 2 || Math.Abs(pt.Y - _pressPt.Y) > 2)) _moved = true;
                UpdateSelectionFocus(pt);
                bool outside = pt.Y < 0 || pt.Y > _viewport.Height;
                if (outside && !_autoScroll.IsEnabled) _autoScroll.Start();
                else if (!outside && _autoScroll.IsEnabled) _autoScroll.Stop();
                break;
            case Drag.Ink:
                if (_inkPoints.Count == 0 || (pt - _inkPoints[^1]).LengthSquared > 1.5)
                {
                    _inkPoints.Add(pt);
                    InvalidateVisual();
                }

                break;
            default:
                UpdateCursor(pt);
                break;
        }
    }

    private void AutoScrollTick()
    {
        if (_drag != Drag.Select) { _autoScroll.Stop(); return; }
        double dy = _lastMouse.Y < 0 ? _lastMouse.Y : _lastMouse.Y > _viewport.Height ? _lastMouse.Y - _viewport.Height : 0;
        if (dy == 0) { _autoScroll.Stop(); return; }
        ScrollBy(Math.Clamp(dy, -80, 80) * 0.6, animated: false);
        UpdateSelectionFocus(_lastMouse);
    }

    private void UpdateSelectionFocus(Point pt)
    {
        var hit = HitTestPage(pt);
        if (hit is not { } h) return;
        var content = GetContent(h.Page);
        if (content == null) return;
        int idx = TextMath.HitTest(content, h.U, h.V, out _);
        if (idx < 0) return;
        if (_focus is { } f && f.Page == h.Page && f.Idx == idx) return;
        _focus = (h.Page, idx);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IgnoreRealPointer) return;
        PointerUp(e.GetPosition(this));
    }

    internal void PointerUp(Point pt)
    {
        _autoScroll.Stop();
        var drag = _drag;
        _drag = Drag.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        switch (drag)
        {
            case Drag.Select:
                if (!_moved) ClearSelection();
                else RaiseSelection();
                break;
            case Drag.Ink:
                _ = CommitInkAsync();
                break;
            case Drag.Pan:
                if (!_moved) ActivatePressed(pt);
                UpdateCursor(pt);
                break;
        }
    }

    private void ActivatePressed(Point pt)
    {
        if (_pressLink is { } link)
        {
            if (link.Kind == LinkKind.Uri && link.Uri != null)
            {
                UriRequested?.Invoke(this, link.Uri);
            }
            else if (link.Kind == LinkKind.Goto)
            {
                JumpedFromPage?.Invoke(this, _currentPage);
                GoToPage(link.TargetPage, float.IsNaN(link.TargetY) ? null : link.TargetY);
            }
        }
        else if (_pressAnnot is { } a && _layout != null)
        {
            var rect = ToView(_pressPage, PageRect(_pressPage), a.Rect);
            AnnotationClicked?.Invoke(this, new AnnotationClick(_pressPage, a, rect));
        }

        _pressLink = null;
        _pressAnnot = null;
    }

    private void UpdateCursor(Point pt)
    {
        if (_layout == null) { Cursor = Cursors.Arrow; return; }
        if (Tool != ViewTool.Select) { Cursor = Cursors.Cross; return; }
        var hit = HitTestPage(pt);
        Cursor = Cursors.Arrow;
        if (hit is not { Inside: true } h) return;
        if (!_contents.TryGetValue(h.Page, out var c)) return;
        if (c.Links.Any(l => l.Rect.Contains(h.U, h.V)) ||
            c.Annotations.Any(a => a.Kind is AnnotationKind.Nota or AnnotationKind.TextoLibre && a.Rect.Contains(h.U, h.V)))
            Cursor = Cursors.Hand;
        else if (TextMath.IsOverText(c, h.U, h.V))
            Cursor = Cursors.IBeam;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag == Drag.None) Cursor = Cursors.Arrow;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_layout == null || IgnoreRealPointer) return;
        e.Handled = true;
        Wheel(e.GetPosition(this), e.Delta, Keyboard.Modifiers);
    }

    internal void Wheel(Point pt, int delta, ModifierKeys modifiers)
    {
        if (_layout == null) return;
        double notches = delta / 120.0;
        if ((modifiers & ModifierKeys.Control) != 0)
            SetZoom(_zoom * Math.Pow(1.12, notches), pt);
        else if ((modifiers & ModifierKeys.Shift) != 0)
            SetOffsets(_offX - notches * 80, _offY);
        else
            ScrollBy(-notches * 100, animated: true);
    }

    /// <summary>Posición en la vista de un punto de una página (coordenadas de página), para pruebas.</summary>
    internal Point? ViewPointOf(int page, double u, double v)
    {
        if (_layout == null || page < 0 || page >= _sizes.Length) return null;
        var sz = _sizes[page];
        var (dx, dy) = PageSpace.ToDisplay(_rot, sz.Width, sz.Height, u, v);
        var (dw, _) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
        var r = PageRect(page);
        double k = r.Width / dw;
        return new Point(r.X + dx * k, r.Y + dy * k);
    }

    internal (double Width, double Height) ViewportSize => (_viewport.Width, _viewport.Height);

    internal string DebugPress => $"enlace={_pressLink != null}, nota={_pressAnnot != null}, arrastre={_drag}, página={_pressPage}";

    internal int ContentJobs => _contentTasks.Count;

    internal PageContent? ContentOf(int page) => _contents.GetValueOrDefault(page);

    internal Task<PageContent> EnsureContentAsync(int page) => GetContentAsync(page);

    // ================================================================== teclado

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_layout == null) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl) return;
        switch (e.Key)
        {
            case Key.PageDown: PageDown(); break;
            case Key.PageUp: PageUp(); break;
            case Key.Space: if (shift) PageUp(); else PageDown(); break;
            case Key.Down: LineDown(); break;
            case Key.Up: LineUp(); break;
            case Key.Right:
                if (_extent.Width > _viewport.Width) LineRight(); else GoToPage(_layout.LastPageOfRow(_layout.RowOf(_currentPage)) + 1);
                break;
            case Key.Left:
                if (_extent.Width > _viewport.Width) LineLeft(); else GoToPage(_layout.FirstPageOfRow(_layout.RowOf(_currentPage)) - 1);
                break;
            case Key.Home: GoToPage(0); break;
            case Key.End: GoToPage(_sizes.Length - 1); break;
            default: return;
        }

        e.Handled = true;
    }

    // ================================================================== selección y portapapeles

    private ((int Page, int Idx) A, (int Page, int Idx) B)? OrderedSelection()
    {
        if (_anchor is not { } a || _focus is not { } b) return null;
        if (a.Page > b.Page || (a.Page == b.Page && a.Idx > b.Idx)) (a, b) = (b, a);
        return (a, b);
    }

    public void ClearSelection()
    {
        bool had = _anchor != null;
        _anchor = _focus = null;
        if (had)
        {
            InvalidateVisual();
            RaiseSelectionCleared();
        }
    }

    private void RaiseSelectionCleared() => SelectionChanged?.Invoke(this, null);

    private void RaiseSelection()
    {
        InvalidateVisual();
        SelectionChanged?.Invoke(this, SelectionAnchorRect());
    }

    /// <summary>Rectángulo (en coordenadas de la vista) del extremo final de la selección, para colocar una barra flotante.</summary>
    private Rect? SelectionAnchorRect()
    {
        if (_focus is not { } f || _layout == null || !_contents.TryGetValue(f.Page, out var c)) return null;
        if (f.Idx >= c.CharCount) return null;
        var rects = TextMath.RectsForRange(c, Math.Max(0, f.Idx - 0), f.Idx);
        if (rects.Count == 0) return null;
        return ToView(f.Page, PageRect(f.Page), rects[0]);
    }

    public async Task<string> GetSelectedTextAsync()
    {
        if (OrderedSelection() is not { } s || _doc == null) return "";
        var sb = new System.Text.StringBuilder();
        for (int p = s.A.Page; p <= s.B.Page; p++)
        {
            var c = _contents.TryGetValue(p, out var cached) ? cached : await GetContentAsync(p);
            int from = p == s.A.Page ? s.A.Idx : 0;
            int to = p == s.B.Page ? s.B.Idx : c.CharCount - 1;
            if (sb.Length > 0) sb.Append("\r\n");
            sb.Append(TextMath.TextOfRange(c, from, to));
        }

        return sb.ToString();
    }

    public async Task CopySelectionAsync()
    {
        string text = await GetSelectedTextAsync();
        if (text.Length == 0) return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // El portapapeles estaba en uso por otro programa; se reintenta una vez.
            await Task.Delay(100);
            Clipboard.SetText(text);
        }
    }

    public async Task SelectAllAsync()
    {
        if (_doc == null || _sizes.Length == 0) return;
        int lastPage = _sizes.Length - 1;
        var last = await GetContentAsync(lastPage);
        _ = await GetContentAsync(0);
        _anchor = (0, 0);
        _focus = (lastPage, Math.Max(0, last.CharCount - 1));
        RaiseSelection();
    }

    // ================================================================== anotaciones

    public async Task ApplyMarkupAsync(MarkupKind kind, uint argb, string author)
    {
        if (OrderedSelection() is not { } s || _doc == null) return;
        var doc = _doc;
        try
        {
            for (int p = s.A.Page; p <= s.B.Page; p++)
            {
                var c = _contents.TryGetValue(p, out var cached) ? cached : await GetContentAsync(p);
                int from = p == s.A.Page ? s.A.Idx : 0;
                int to = p == s.B.Page ? s.B.Idx : c.CharCount - 1;
                var rects = TextMath.RectsForRange(c, from, to);
                if (rects.Count == 0) continue;
                await doc.AddMarkupAsync(p, kind, rects, argb, author);
                if (doc != _doc) return;
                BumpPage(p);
                AnnotationsChanged?.Invoke(this, p);
            }
        }
        finally
        {
            ClearSelection();
        }
    }

    public async Task AddNoteAsync(int page, float u, float v, string text, uint argb, string author)
    {
        if (_doc == null) return;
        await _doc.AddNoteAsync(page, u, v, text, argb, author);
        BumpPage(page);
        AnnotationsChanged?.Invoke(this, page);
    }

    public async Task RemoveAnnotationAsync(int page, int index)
    {
        if (_doc == null) return;
        await _doc.RemoveAnnotationAsync(page, index);
        BumpPage(page);
        AnnotationsChanged?.Invoke(this, page);
    }

    private async Task CommitInkAsync()
    {
        var pts = _inkPoints.ToList();
        int page = _inkPage;
        _inkPoints.Clear();
        InvalidateVisual();
        if (pts.Count < 2 || _doc == null || _layout == null) return;
        var sz = _sizes[page];
        var (dw, dh) = PageSpace.DisplaySize(_rot, sz.Width, sz.Height);
        var r = PageRect(page);
        var pagePts = new List<(float, float)>(pts.Count);
        foreach (var p in pts)
        {
            double fx = Math.Clamp((p.X - r.X) / r.Width, 0, 1), fy = Math.Clamp((p.Y - r.Y) / r.Height, 0, 1);
            var (u, v) = PageSpace.FromDisplay(_rot, sz.Width, sz.Height, fx * dw, fy * dh);
            pagePts.Add(((float)u, (float)v));
        }

        try
        {
            await _doc.AddInkAsync(page, pagePts, InkColor, InkWidth, Environment.UserName);
            BumpPage(page);
            AnnotationsChanged?.Invoke(this, page);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    // ================================================================== búsqueda

    public void SetSearchResults(IReadOnlyList<SearchHit>? hits, SearchHit? current)
    {
        _hitRects = new Dictionary<int, List<RectF>>();
        if (hits != null)
        {
            foreach (var h in hits)
            {
                if (!_hitRects.TryGetValue(h.Page, out var list)) _hitRects[h.Page] = list = new List<RectF>();
                list.AddRange(h.Rects);
            }
        }

        _currentHit = current?.Rects.ToList() ?? new List<RectF>();
        _currentHitPage = current?.Page ?? -1;
        InvalidateVisual();
        if (current is { Rects.Length: > 0 } c) ScrollToRect(c.Page, c.Rects[0]);
    }

    public void MarkPageChanged(int page) => BumpPage(page);

    /// <summary>Todo lo que se ve ya está dibujado (para pruebas y mediciones).</summary>
    internal bool AllVisibleReady()
    {
        if (_layout == null) return true; // sin documento no hay nada que esperar
        var (first, last) = _layout.PagesIn(_offY, _offY + _viewport.Height);
        for (int p = first; p <= last; p++)
        {
            var r = PageRect(p);
            if (r.Right < 0 || r.X > _viewport.Width) continue;
            int pw = PxW(p), ph = PxH(p);
            foreach (var t in VisibleTiles(r, pw, ph))
                if (!_cache.Contains(KeyFor(p, pw, ph, t)) && !_failedTiles.Contains(KeyFor(p, pw, ph, t))) return false;
        }

        return true;
    }

    /// <summary>Selecciona la primera aparición del texto en la página actual (solo para pruebas).</summary>
    internal async Task<bool> TestSelectTextAsync(string query)
    {
        if (_doc == null) return false;
        for (int p = _currentPage; p < _sizes.Length; p++)
        {
            var c = await GetContentAsync(p);
            var sb = new System.Text.StringBuilder(c.CharCount);
            foreach (int cp in c.Chars) sb.Append(cp is > 0 and < 0x10000 ? (char)cp : '?');
            int i = sb.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            _anchor = (p, i);
            _focus = (p, i + query.Length - 1);
            RaiseSelection();
            return true;
        }

        return false;
    }

    public PageContent? PeekContent(int page) => _contents.GetValueOrDefault(page);
}
