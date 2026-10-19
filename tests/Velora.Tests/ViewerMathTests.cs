// SPDX-License-Identifier: GPL-3.0-or-later
using Velora.Pdf;
using Velora.Pdf.Viewer;

namespace Velora.Tests;

public class LayoutTests
{
    private static PageSize[] Pages(int n, float w = 600, float h = 800) => Enumerable.Repeat(new PageSize(w, h), n).ToArray();

    [Fact]
    public void Una_columna_apila_las_paginas()
    {
        var l = new PageLayout(Pages(3), 0, 1.0, 1.0, 1, false, gap: 10, margin: 20);
        Assert.Equal(3, l.RowCount);
        Assert.Equal(20, l.Y(0));
        Assert.Equal(20 + 800 + 10, l.Y(1));
        Assert.Equal(20 + 2 * 810, l.Y(2));
        Assert.Equal(600 + 40, l.ExtentWidth);
        Assert.Equal(20, l.X(0));
        Assert.Equal(20 + 3 * 800 + 2 * 10 + 20, l.ExtentHeight);
    }

    [Fact]
    public void La_rotacion_intercambia_ancho_y_alto()
    {
        var l = new PageLayout(Pages(1, 600, 800), 1, 1.0, 1.0, 1, false);
        Assert.Equal(800, l.Width(0));
        Assert.Equal(600, l.Height(0));
    }

    [Fact]
    public void Doble_pagina_con_portada()
    {
        var l = new PageLayout(Pages(5), 0, 1.0, 1.0, 2, true, gap: 10, margin: 10);
        // filas: [0], [1,2], [3,4]
        Assert.Equal(3, l.RowCount);
        Assert.Equal(l.Y(1), l.Y(2));
        Assert.True(l.X(1) < l.X(2));
        double center = l.ExtentWidth / 2;
        Assert.True(l.X(0) >= center, "la portada va a la derecha");
        Assert.Equal(1, l.RowOf(1));
        Assert.Equal(1, l.RowOf(2));
        Assert.Equal(2, l.RowOf(4));
    }

    [Fact]
    public void Doble_pagina_sin_portada_ultima_impar_a_la_izquierda()
    {
        var l = new PageLayout(Pages(3), 0, 1.0, 1.0, 2, false);
        Assert.Equal(2, l.RowCount);
        Assert.True(l.X(2) + l.Width(2) <= l.ExtentWidth / 2);
    }

    [Fact]
    public void Paginas_visibles_con_busqueda_binaria()
    {
        var l = new PageLayout(Pages(1000), 0, 1.0, 1.0, 1, false, gap: 10, margin: 10);
        var (first, last) = l.PagesIn(10 + 500 * 810 + 5, 10 + 500 * 810 + 900);
        Assert.Equal(500, first);
        Assert.Equal(501, last);
        Assert.Equal(0, l.PagesIn(0, 5).First);
        Assert.Equal(999, l.PagesIn(l.ExtentHeight - 5, l.ExtentHeight + 500).Last);
    }

    [Fact]
    public void Pagina_mas_visible_y_localizar_punto()
    {
        var l = new PageLayout(Pages(10), 0, 1.0, 1.0, 1, false, gap: 10, margin: 10);
        // Ventana de 600 de alto que empieza a mitad de la página 3
        double top = l.Y(3) + 700;
        Assert.Equal(4, l.MostVisiblePage(top, top + 600));
        var p = l.Locate(l.X(2) + 150, l.Y(2) + 400);
        Assert.Equal(2, p.Page);
        Assert.Equal(0.25, p.FractionX, 3);
        Assert.Equal(0.5, p.FractionY, 3);
        var (x, y) = l.PointOf(p);
        Assert.Equal(l.X(2) + 150, x, 3);
        Assert.Equal(l.Y(2) + 400, y, 3);
    }

    [Fact]
    public void Tamano_se_ajusta_al_pixel_fisico()
    {
        var l = new PageLayout(Pages(1, 595, 842), 0, 96.0 / 72, 1.5, 1, false);
        double wpx = l.Width(0) * 1.5;
        Assert.Equal(Math.Round(wpx), wpx, 6);
    }

    [Fact]
    public void Ajustar_al_ancho_y_a_la_pagina()
    {
        double s = PageLayout.FitWidthScale(Pages(3), 0, 1000, 1, 10, 20);
        Assert.Equal((1000 - 40) / 600.0, s, 6);
        double s2 = PageLayout.FitWidthScale(Pages(3), 0, 1000, 2, 10, 20);
        Assert.Equal((1000 - 40 - 10) / 1200.0, s2, 6);
        double fp = PageLayout.FitPageScale(new PageSize(600, 800), 0, 1000, 500, 1, 10, 20);
        Assert.Equal((500 - 40) / 800.0, fp, 6);
    }

    [Fact]
    public void Disposicion_de_miles_de_paginas_es_rapida()
    {
        var pages = Pages(20000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) _ = new PageLayout(pages, 0, 1.3, 1.25, 1, false);
        Assert.True(sw.ElapsedMilliseconds < 500, "10 disposiciones de 20000 páginas tardaron " + sw.ElapsedMilliseconds + " ms");
    }
}

public class TileGridTests
{
    [Fact]
    public void Pagina_normal_es_una_sola_baldosa()
    {
        var t = TileGrid.All(1240, 1754).ToList();
        var one = Assert.Single(t);
        Assert.Equal(new TileGrid.Tile(0, 0, 0, 0, 1240, 1754), one);
    }

    [Fact]
    public void Pagina_grande_se_reparte_y_solo_se_piden_las_visibles()
    {
        var all = TileGrid.All(5000, 7000).ToList();
        Assert.Equal(5 * 7, all.Count);
        Assert.Equal(5000, all.Where(t => t.Row == 0).Sum(t => t.Width));
        Assert.Equal(7000, all.Where(t => t.Column == 0).Sum(t => t.Height));
        var vis = TileGrid.Visible(5000, 7000, 1000, 1000, 1500, 1500).ToList();
        Assert.Equal(4, vis.Count);
        Assert.All(vis, t => Assert.True(t.Column <= 1 && t.Row <= 1));
        // Justo en el borde de una baldosa no se pide la siguiente
        Assert.Single(TileGrid.Visible(5000, 7000, 0, 0, 1024, 1024));
    }
}

public class CacheTests
{
    [Fact]
    public void Expulsa_lo_menos_usado_al_pasar_el_presupuesto()
    {
        var evicted = new List<string>();
        var c = new ByteLruCache<int, string>(100, s => s.Length, evicted.Add);
        c.Set(1, new string('a', 40));
        c.Set(2, new string('b', 40));
        Assert.True(c.TryGet(1, out _)); // 1 pasa a ser el más reciente
        c.Set(3, new string('c', 40));   // supera 100: sale el 2
        Assert.False(c.Contains(2));
        Assert.True(c.Contains(1));
        Assert.True(c.Contains(3));
        Assert.Equal(80, c.Bytes);
        Assert.Single(evicted);
        c.RemoveWhere(k => k == 1);
        Assert.Equal(40, c.Bytes);
        c.Clear();
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void Conserva_al_menos_la_ultima_aunque_supere_el_presupuesto()
    {
        var c = new ByteLruCache<int, string>(10, s => s.Length);
        c.Set(1, new string('a', 50));
        Assert.True(c.Contains(1));
    }
}

public class TextMathTests
{
    private static PageContent Make(string text, float charW = 6, float lineH = 14, int perLine = 10)
    {
        var chars = text.Select(ch => (int)ch).ToArray();
        var boxes = new float[chars.Length * 4];
        for (int i = 0; i < chars.Length; i++)
        {
            int line = i / perLine, col = i % perLine;
            boxes[4 * i] = 10 + col * charW;
            boxes[4 * i + 1] = 10 + line * lineH;
            boxes[4 * i + 2] = 10 + (col + 1) * charW;
            boxes[4 * i + 3] = 10 + line * lineH + 12;
        }

        return new PageContent(0, 200, 200, chars, boxes, [], []);
    }

    [Fact]
    public void Busca_el_caracter_bajo_el_punto()
    {
        var c = Make("abcdefghijklmnopqrst");
        Assert.Equal(0, TextMath.HitTest(c, 12, 15, out bool inside));
        Assert.True(inside);
        Assert.Equal(13, TextMath.HitTest(c, 10 + 3 * 6 + 2, 10 + 14 + 3, out inside));
        Assert.True(inside);
        // Fuera del texto: el más cercano
        int far = TextMath.HitTest(c, 190, 12, out inside);
        Assert.False(inside);
        Assert.Equal(9, far);
    }

    [Fact]
    public void Rectangulos_por_linea()
    {
        var c = Make("abcdefghijklmnopqrst"); // dos líneas de 10
        var r = TextMath.RectsForRange(c, 2, 14);
        Assert.Equal(2, r.Count);
        Assert.Equal(10 + 2 * 6, r[0].Left);
        Assert.Equal(10 + 10 * 6, r[0].Right);
        Assert.Equal(10, r[1].Left);
        Assert.Equal(10 + 5 * 6, r[1].Right);
    }

    [Fact]
    public void Texto_y_palabras()
    {
        var c = Make("hola mundo!x");
        Assert.Equal("hola mun", TextMath.TextOfRange(c, 0, 7));
        Assert.Equal((0, 3), TextMath.WordAt(c, 1));
        Assert.Equal((5, 11), TextMath.WordAt(c, 7));
        Assert.Equal((4, 4), TextMath.WordAt(c, 4));
        var d = new PageContent(0, 1, 1, [65, '\r', '\n', 66, 0xFFFE, 0x1F600], new float[24], [], []);
        Assert.Equal("A\r\nB\U0001F600", TextMath.TextOfRange(d, 0, 5));
    }
}

public class PageSpaceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Ida_y_vuelta(int rot)
    {
        var (x, y) = PageSpace.ToDisplay(rot, 600, 800, 100, 50);
        var (u, v) = PageSpace.FromDisplay(rot, 600, 800, x, y);
        Assert.Equal(100, u, 6);
        Assert.Equal(50, v, 6);
    }

    [Fact]
    public void Esquinas_giradas()
    {
        // Esquina superior izquierda de la página sin girar: tras girar 90° va arriba a la derecha.
        var (x, y) = PageSpace.ToDisplay(1, 600, 800, 0, 0);
        Assert.Equal((800.0, 0.0), (x, y));
        var (x3, y3) = PageSpace.ToDisplay(3, 600, 800, 0, 0);
        Assert.Equal((0.0, 600.0), (x3, y3));
        var (x2, y2) = PageSpace.ToDisplay(2, 600, 800, 0, 0);
        Assert.Equal((600.0, 800.0), (x2, y2));
        Assert.Equal((800.0, 600.0), PageSpace.DisplaySize(1, 600, 800));
    }
}
