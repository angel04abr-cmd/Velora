// SPDX-License-Identifier: GPL-3.0-or-later
namespace Velora.Pdf.Viewer;

/// <summary>Dónde cae un punto del documento: página y posición relativa (0..1) dentro de ella.</summary>
public readonly record struct PagePoint(int Page, double FractionX, double FractionY);

/// <summary>
/// Disposición de todas las páginas en una columna o en dos («doble página», con o sin portada suelta).
/// Todas las medidas son píxeles independientes del dispositivo (DIP) y el tamaño de cada página
/// se ajusta al píxel físico para que se vea nítida. Es una clase pura (sin WPF), por eso se puede probar.
/// </summary>
public sealed class PageLayout
{
    private readonly double[] _x, _y, _w, _h;
    private readonly double[] _rowTop, _rowBottom;
    private readonly int[] _rowFirst, _rowCount;

    public PageLayout(IReadOnlyList<PageSize> sizes, int rotation, double scale, double dpiScale, int columns, bool coverAlone,
        double gap = 12, double margin = 16)
    {
        if (sizes.Count == 0) throw new ArgumentException("Hace falta al menos una página.", nameof(sizes));
        Columns = columns >= 2 ? 2 : 1;
        CoverAlone = coverAlone && Columns == 2;
        Gap = gap;
        Margin = margin;
        int n = sizes.Count;
        PageCount = n;
        _x = new double[n];
        _y = new double[n];
        _w = new double[n];
        _h = new double[n];

        bool swap = (rotation & 1) != 0;
        double maxW = 0;
        for (int i = 0; i < n; i++)
        {
            double wpt = swap ? sizes[i].Height : sizes[i].Width;
            double hpt = swap ? sizes[i].Width : sizes[i].Height;
            _w[i] = Math.Max(1, Math.Round(wpt * scale * dpiScale)) / dpiScale;
            _h[i] = Math.Max(1, Math.Round(hpt * scale * dpiScale)) / dpiScale;
            maxW = Math.Max(maxW, _w[i]);
        }

        // Filas
        var firsts = new List<int>();
        var counts = new List<int>();
        if (Columns == 1)
        {
            for (int i = 0; i < n; i++) { firsts.Add(i); counts.Add(1); }
        }
        else
        {
            int i = 0;
            if (CoverAlone) { firsts.Add(0); counts.Add(1); i = 1; }
            for (; i < n; i += 2) { firsts.Add(i); counts.Add(Math.Min(2, n - i)); }
        }

        RowCount = firsts.Count;
        _rowFirst = firsts.ToArray();
        _rowCount = counts.ToArray();
        _rowTop = new double[RowCount];
        _rowBottom = new double[RowCount];

        ExtentWidth = 2 * margin + (Columns == 1 ? maxW : 2 * maxW + gap);
        double center = ExtentWidth / 2;
        double top = margin;
        for (int r = 0; r < RowCount; r++)
        {
            double rowH = 0;
            for (int k = 0; k < _rowCount[r]; k++) rowH = Math.Max(rowH, _h[_rowFirst[r] + k]);
            _rowTop[r] = top;
            _rowBottom[r] = top + rowH;
            for (int k = 0; k < _rowCount[r]; k++)
            {
                int p = _rowFirst[r] + k;
                _y[p] = top + (rowH - _h[p]) / 2;
                if (Columns == 1)
                {
                    _x[p] = center - _w[p] / 2;
                }
                else
                {
                    bool left;
                    if (_rowCount[r] == 2) left = k == 0;
                    else left = !(CoverAlone && p == 0); // portada: a la derecha; última impar: a la izquierda
                    _x[p] = left ? center - gap / 2 - _w[p] : center + gap / 2;
                }
            }

            top += rowH + gap;
        }

        ExtentHeight = _rowBottom[RowCount - 1] + margin;
    }

    public int PageCount { get; }
    public int RowCount { get; }
    public int Columns { get; }
    public bool CoverAlone { get; }
    public double Gap { get; }
    public double Margin { get; }
    public double ExtentWidth { get; }
    public double ExtentHeight { get; }

    public double X(int page) => _x[page];
    public double Y(int page) => _y[page];
    public double Width(int page) => _w[page];
    public double Height(int page) => _h[page];

    public int RowOf(int page)
    {
        int lo = 0, hi = RowCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowFirst[mid] <= page) lo = mid; else hi = mid - 1;
        }

        return lo;
    }

    /// <summary>Fila que contiene la coordenada y (o la más cercana por debajo/encima).</summary>
    public int RowAt(double y)
    {
        int lo = 0, hi = RowCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_rowBottom[mid] + Gap / 2 < y) lo = mid + 1; else hi = mid;
        }

        return lo;
    }

    /// <summary>Páginas (primera, última) que tocan el tramo vertical [top, bottom] del documento.</summary>
    public (int First, int Last) PagesIn(double top, double bottom)
    {
        int r0 = RowAt(top);
        int r1 = RowAt(bottom);
        int first = _rowFirst[r0];
        int last = _rowFirst[r1] + _rowCount[r1] - 1;
        return (first, last);
    }

    public int FirstPageOfRow(int row) => _rowFirst[row];
    public int LastPageOfRow(int row) => _rowFirst[row] + _rowCount[row] - 1;

    public double RowTop(int row) => _rowTop[row];

    /// <summary>Página con más superficie visible dentro de [top, bottom].</summary>
    public int MostVisiblePage(double top, double bottom)
    {
        var (first, last) = PagesIn(top, bottom);
        int best = first;
        double bestArea = -1;
        for (int p = first; p <= last; p++)
        {
            double vis = Math.Min(bottom, _y[p] + _h[p]) - Math.Max(top, _y[p]);
            double area = Math.Max(0, vis) / Math.Max(1, _h[p]);
            if (area > bestArea + 1e-9) { bestArea = area; best = p; }
        }

        return best;
    }

    public PagePoint Locate(double x, double y)
    {
        var (first, last) = PagesIn(y, y);
        int best = first;
        double bestDist = double.MaxValue;
        for (int p = first; p <= last; p++)
        {
            double dx = x < _x[p] ? _x[p] - x : x > _x[p] + _w[p] ? x - _x[p] - _w[p] : 0;
            double dy = y < _y[p] ? _y[p] - y : y > _y[p] + _h[p] ? y - _y[p] - _h[p] : 0;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = p; }
        }

        return new PagePoint(best, (x - _x[best]) / _w[best], (y - _y[best]) / _h[best]);
    }

    public (double X, double Y) PointOf(PagePoint p) =>
        (_x[p.Page] + p.FractionX * _w[p.Page], _y[p.Page] + p.FractionY * _h[p.Page]);

    /// <summary>Zoom (DIP por punto) que hace que la disposición ocupe justo el ancho disponible.</summary>
    public static double FitWidthScale(IReadOnlyList<PageSize> sizes, int rotation, double viewportWidth, int columns,
        double gap = 12, double margin = 16)
    {
        bool swap = (rotation & 1) != 0;
        double maxW = 0;
        foreach (var s in sizes) maxW = Math.Max(maxW, swap ? s.Height : s.Width);
        int cols = columns >= 2 && sizes.Count > 1 ? 2 : 1;
        double avail = viewportWidth - 2 * margin - (cols - 1) * gap;
        return Math.Max(0.01, avail / (cols * Math.Max(1, maxW)));
    }

    /// <summary>Zoom que hace que una página entera quepa en la ventana.</summary>
    public static double FitPageScale(PageSize page, int rotation, double viewportWidth, double viewportHeight, int columns,
        double gap = 12, double margin = 16)
    {
        bool swap = (rotation & 1) != 0;
        double w = swap ? page.Height : page.Width;
        double h = swap ? page.Width : page.Height;
        int cols = columns >= 2 ? 2 : 1;
        double sx = (viewportWidth - 2 * margin - (cols - 1) * gap) / (cols * Math.Max(1, w));
        double sy = (viewportHeight - 2 * margin) / Math.Max(1, h);
        return Math.Max(0.01, Math.Min(sx, sy));
    }
}

/// <summary>Reparto de una página (ya a su tamaño en píxeles) en baldosas que se dibujan por separado.</summary>
public static class TileGrid
{
    /// <summary>Las páginas de hasta este tamaño (por lado) se dibujan de una sola vez.</summary>
    public const int WholePageLimit = 2048;
    public const int TileSize = 1024;

    public readonly record struct Tile(int Column, int Row, int X, int Y, int Width, int Height);

    public static (int Columns, int Rows, int TileW, int TileH) Dimensions(int pagePxW, int pagePxH)
    {
        if (pagePxW <= WholePageLimit && pagePxH <= WholePageLimit)
            return (1, 1, pagePxW, pagePxH);
        return ((pagePxW + TileSize - 1) / TileSize, (pagePxH + TileSize - 1) / TileSize, TileSize, TileSize);
    }

    /// <summary>Baldosas que tocan el rectángulo visible (en píxeles de la página).</summary>
    public static IEnumerable<Tile> Visible(int pagePxW, int pagePxH, double visLeft, double visTop, double visRight, double visBottom)
    {
        var (cols, rows, tw, th) = Dimensions(pagePxW, pagePxH);
        int c0 = Math.Clamp((int)Math.Floor(visLeft / tw), 0, cols - 1);
        int c1 = Math.Clamp((int)Math.Floor((visRight - 0.001) / tw), 0, cols - 1);
        int r0 = Math.Clamp((int)Math.Floor(visTop / th), 0, rows - 1);
        int r1 = Math.Clamp((int)Math.Floor((visBottom - 0.001) / th), 0, rows - 1);
        for (int r = r0; r <= r1; r++)
        {
            for (int c = c0; c <= c1; c++)
            {
                int x = c * tw, y = r * th;
                yield return new Tile(c, r, x, y, Math.Min(tw, pagePxW - x), Math.Min(th, pagePxH - y));
            }
        }
    }

    public static IEnumerable<Tile> All(int pagePxW, int pagePxH) =>
        Visible(pagePxW, pagePxH, 0, 0, pagePxW, pagePxH);
}
