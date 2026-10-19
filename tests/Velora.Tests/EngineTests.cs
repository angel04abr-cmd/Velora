// SPDX-License-Identifier: GPL-3.0-or-later
using Velora.Pdf;
using Velora.Testing;

namespace Velora.Tests;

internal static class Files
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "VeloraTests-" + Environment.ProcessId);

    public static string Create(string name, byte[] data)
    {
        Directory.CreateDirectory(Dir);
        string p = Path.Combine(Dir, name);
        File.WriteAllBytes(p, data);
        return p;
    }

    public static string Path_(string name)
    {
        Directory.CreateDirectory(Dir);
        return Path.Combine(Dir, name);
    }

    public static string Simple(string name = "simple.pdf", int pages = 3)
    {
        var specs = new List<PageSpec>();
        for (int i = 1; i <= pages; i++)
        {
            var p = PdfBuilder.TextPage(i, 20);
            if (i == 1)
            {
                // Línea 1 está a y = 792-72-14 = 706 aprox.; enlaces sobre ella
                p.Links.Add(new LinkSpec { X1 = 72, Y1 = 690, X2 = 300, Y2 = 718, DestPage = 2 });
                p.Links.Add(new LinkSpec { X1 = 72, Y1 = 640, X2 = 300, Y2 = 660, Uri = "https://example.org/prueba" });
            }

            specs.Add(p);
        }

        var outline = new List<OutlineSpec>
        {
            new() { Title = "Capítulo uno", Page = 0, Children = { new OutlineSpec { Title = "Sección 1.1", Page = 1 } } },
            new() { Title = "Capítulo dos", Page = 2 },
        };
        return Create(name, PdfBuilder.Build(specs, outline, "Mi título", "Autora Prueba"));
    }
}

public class EngineTests
{
    [Fact]
    public async Task Abre_y_lee_numero_de_paginas_y_tamanos()
    {
        var specs = new List<PageSpec> { PdfBuilder.TextPage(1), new PageSpec { Width = 300, Height = 500 }, new PageSpec { Width = 300, Height = 500, Rotate = 90 } };
        string path = Files.Create("sizes.pdf", PdfBuilder.Build(specs));
        using var doc = await PdfDocument.OpenAsync(path);
        Assert.Equal(3, doc.PageCount);
        var sizes = await doc.GetPageSizesAsync();
        Assert.Equal(612, sizes[0].Width, 1);
        Assert.Equal(792, sizes[0].Height, 1);
        Assert.Equal(300, sizes[1].Width, 1);
        Assert.Equal(500, sizes[1].Height, 1);
        // /Rotate 90: el ancho y el alto se intercambian
        Assert.Equal(500, sizes[2].Width, 1);
        Assert.Equal(300, sizes[2].Height, 1);
    }

    [Fact]
    public async Task Dibuja_una_pagina_con_texto_negro_sobre_blanco()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        using var tile = await doc.RenderTileAsync(new TileRequest(0, 612, 792, 0, 0, 0, 612, 792), PdfPriority.Urgente);
        Assert.Equal(612, tile.Width);
        Assert.Equal(792, tile.Height);
        int dark = 0, white = 0;
        for (int i = 0; i < tile.Width * tile.Height; i++)
        {
            byte b = tile.Pixels[i * 4];
            if (b < 100) dark++;
            else if (b > 240) white++;
        }

        Assert.True(dark > 500, "debería haber píxeles oscuros (texto), hay " + dark);
        Assert.True(white > tile.Width * tile.Height / 2, "la mayor parte debería ser blanca");
    }

    [Fact]
    public async Task Una_baldosa_coincide_con_ese_trozo_de_la_pagina_entera()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        using var full = await doc.RenderTileAsync(new TileRequest(0, 1224, 1584, 0, 0, 0, 1224, 1584), PdfPriority.Urgente);
        using var part = await doc.RenderTileAsync(new TileRequest(0, 1224, 1584, 0, 100, 120, 400, 300), PdfPriority.Urgente);
        int diff = 0;
        for (int y = 0; y < 300; y++)
        {
            for (int x = 0; x < 400; x++)
            {
                int a = ((120 + y) * 1224 + 100 + x) * 4;
                int b = (y * 400 + x) * 4;
                if (Math.Abs(full.Pixels[a] - part.Pixels[b]) > 2) diff++;
            }
        }

        Assert.True(diff < 50, "la baldosa debe ser idéntica al trozo; diferencias: " + diff);
    }

    [Fact]
    public async Task Contenido_de_pagina_texto_y_posiciones()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        string text = Velora.Pdf.Viewer.TextMath.TextOfRange(c, 0, c.CharCount - 1);
        Assert.Contains("Pagina 1 - Documento de prueba de Velora", text);
        // La primera letra empieza en x≈72 y cerca de la parte de arriba (792-72 = 720 desde abajo → y≈60..80 desde arriba)
        var first = c.BoxOf(0);
        Assert.InRange(first.Left, 70, 76);
        Assert.InRange(first.Top, 55, 85);
        Assert.True(first.Bottom > first.Top);
        // Líneas siguientes más abajo
        int idx = text.IndexOf("Linea 5", StringComparison.Ordinal);
        Assert.True(idx > 0);
        for (int i = 0; i < c.CharCount; i++)
        {
            if (!c.HasBox(i)) continue;
            var b = c.BoxOf(i);
            Assert.InRange(b.Left, -1, 613);
            Assert.InRange(b.Top, -1, 793);
        }
    }

    [Fact]
    public async Task Contenido_de_pagina_rotada_queda_dentro_de_la_pagina()
    {
        var p = PdfBuilder.TextPage(1, 10);
        p.Rotate = 90;
        string path = Files.Create("rot.pdf", PdfBuilder.Build([p]));
        using var doc = await PdfDocument.OpenAsync(path);
        var sizes = await doc.GetPageSizesAsync();
        Assert.Equal(792, sizes[0].Width, 1);
        Assert.Equal(612, sizes[0].Height, 1);
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        Assert.True(c.CharCount > 20);
        for (int i = 0; i < c.CharCount; i++)
        {
            if (!c.HasBox(i)) continue;
            var b = c.BoxOf(i);
            Assert.InRange(b.Left, -1, 793);
            Assert.InRange(b.Top, -1, 613);
            Assert.InRange(b.Right, -1, 793);
            Assert.InRange(b.Bottom, -1, 613);
        }

        // Con Rotate 90 el texto se lee de arriba abajo por el lado derecho: la «P» del título (línea más alta del PDF)
        // queda pegada a la derecha (x ≈ 720) y arriba (y ≈ 72).
        int pi = -1;
        for (int i = 0; i + 2 < c.CharCount; i++)
        {
            if (c.Chars[i] == 'P' && c.Chars[i + 1] == 'a' && c.Chars[i + 2] == 'g') { pi = i; break; }
        }

        Assert.True(pi >= 0);
        var box = c.BoxOf(pi);
        Assert.InRange(box.Left, 700, 735);
        Assert.InRange(box.Top, 68, 80);
    }

    [Fact]
    public async Task Enlaces_internos_y_externos()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        var go = Assert.Single(c.Links, l => l.Kind == LinkKind.Goto);
        Assert.Equal(2, go.TargetPage);
        Assert.InRange(go.Rect.Left, 70, 74);
        Assert.InRange(go.Rect.Top, 70, 100); // 792-718 = 74
        var uri = c.Links.First(l => l.Kind == LinkKind.Uri && l.Uri!.StartsWith("https://example.org"));
        Assert.Equal("https://example.org/prueba", uri.Uri);
    }

    [Fact]
    public async Task Indice_de_marcadores()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var outline = await doc.GetOutlineAsync();
        Assert.Equal(2, outline.Count);
        Assert.Equal("Capítulo uno", outline[0].Title);
        Assert.Equal(0, outline[0].Page);
        Assert.Single(outline[0].Children);
        Assert.Equal("Sección 1.1", outline[0].Children[0].Title);
        Assert.Equal(1, outline[0].Children[0].Page);
        Assert.Equal(2, outline[1].Page);
    }

    [Fact]
    public async Task Busqueda_encuentra_coincidencias_con_rectangulos()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var hits = await doc.SearchPageAsync(1, "murcielago", false, false);
        Assert.Equal(19, hits.Length);
        Assert.All(hits, h =>
        {
            Assert.Equal(1, h.Page);
            Assert.NotEmpty(h.Rects);
            Assert.InRange(h.Rects[0].Left, 60, 400);
        });
        Assert.Empty(await doc.SearchPageAsync(1, "NOEXISTE", false, false));
        Assert.Equal(19, (await doc.SearchPageAsync(1, "MURCIELAGO", false, false)).Length);
        Assert.Empty(await doc.SearchPageAsync(1, "MURCIELAGO", true, false));
    }

    [Fact]
    public async Task Propiedades_del_documento()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var p = await doc.GetPropertiesAsync();
        Assert.Equal("Mi título", p.Title);
        Assert.Equal("Autora Prueba", p.Author);
        Assert.Equal(3, p.PageCount);
        Assert.Equal("1.5", p.PdfVersion);
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), p.Created);
        Assert.False(p.Protected);
        Assert.Equal(612, p.FirstPageSize.Width, 1);
    }

    [Fact]
    public async Task Archivos_no_validos_dan_error_claro()
    {
        string junk = Files.Create("junk.pdf", System.Text.Encoding.ASCII.GetBytes("esto no es un pdf, solo texto"));
        var ex = await Assert.ThrowsAsync<PdfException>(() => PdfDocument.OpenAsync(junk));
        Assert.Contains("PDF", ex.Message);
        string empty = Files.Create("empty.pdf", []);
        await Assert.ThrowsAsync<PdfException>(() => PdfDocument.OpenAsync(empty));
        await Assert.ThrowsAsync<PdfException>(() => PdfDocument.OpenAsync(Files.Path_("noexiste.pdf")));
    }

    [Fact]
    public async Task Anotaciones_resaltado_nota_tinta_se_guardan_y_se_reabren()
    {
        string path = Files.Simple("anot.pdf");
        using var doc = await PdfDocument.OpenAsync(path);
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);

        using var before = await doc.RenderTileAsync(new TileRequest(0, 612, 792, 0, 0, 0, 612, 792), PdfPriority.Urgente);
        var rects = Velora.Pdf.Viewer.TextMath.RectsForRange(c, 0, 10);
        await doc.AddMarkupAsync(0, MarkupKind.Resaltado, rects, 0xFFFFEB3B, "Prueba");
        await doc.AddNoteAsync(0, 400, 200, "Una nota de prueba", 0xFFFFC107, "Prueba");
        await doc.AddInkAsync(0, [(300, 400), (320, 420), (350, 410), (380, 450)], 0xFFE53935, 2f, "Prueba");
        Assert.True(doc.IsModified);

        using var after = await doc.RenderTileAsync(new TileRequest(0, 612, 792, 0, 0, 0, 612, 792), PdfPriority.Urgente);
        Assert.NotEqual(before.Pixels.AsSpan(0, 612 * 792 * 4).ToArray(), after.Pixels.AsSpan(0, 612 * 792 * 4).ToArray());
        var c2 = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        Assert.Contains(c2.Annotations, a => a.Kind == AnnotationKind.Resaltado);
        Assert.Contains(c2.Annotations, a => a.Kind == AnnotationKind.Nota && a.Contents == "Una nota de prueba");
        Assert.Contains(c2.Annotations, a => a.Kind == AnnotationKind.Tinta);

        // Resaltado donde se espera (sobre las primeras letras)
        var hl = c2.Annotations.First(a => a.Kind == AnnotationKind.Resaltado);
        Assert.InRange(hl.Rect.Left, 60, 80);
        Assert.InRange(hl.Rect.Top, 50, 90);

        string saved = Files.Path_("anot-guardado.pdf");
        await doc.SaveCopyAsync(saved);
        using var reopened = await PdfDocument.OpenAsync(saved);
        var c3 = await reopened.GetPageContentAsync(0, PdfPriority.Urgente);
        Assert.Equal(3, c3.Annotations.Count(a => a.Kind is AnnotationKind.Resaltado or AnnotationKind.Nota or AnnotationKind.Tinta));
        using var rendered = await reopened.RenderTileAsync(new TileRequest(0, 612, 792, 0, 0, 0, 612, 792), PdfPriority.Urgente);
        Assert.NotEqual(before.Pixels.AsSpan(0, 612 * 792 * 4).ToArray(), rendered.Pixels.AsSpan(0, 612 * 792 * 4).ToArray());

        // Borrar una anotación
        var note = c3.Annotations.First(a => a.Kind == AnnotationKind.Nota);
        await reopened.RemoveAnnotationAsync(0, note.Index);
        var c4 = await reopened.GetPageContentAsync(0, PdfPriority.Urgente);
        Assert.DoesNotContain(c4.Annotations, a => a.Kind == AnnotationKind.Nota);
    }

    [Fact]
    public async Task Tareas_urgentes_se_adelantan_a_las_de_fondo_y_se_pueden_cancelar()
    {
        var worker = PdfWorker.Shared;
        var order = new List<string>();
        var gate = new ManualResetEventSlim();
        var block = worker.Run(PdfPriority.Interactivo, () => gate.Wait(5000));
        using var cts = new CancellationTokenSource();
        var bg = worker.Run(PdfPriority.Fondo, () => { lock (order) order.Add("fondo"); });
        var low = worker.Run(PdfPriority.Baja, () => { lock (order) order.Add("baja"); });
        var urgent = worker.Run(PdfPriority.Urgente, () => { lock (order) order.Add("urgente"); });
        var canceled = worker.Run(PdfPriority.Urgente, () => { lock (order) order.Add("cancelada"); }, cts.Token);
        var nearer = worker.Run(PdfPriority.Urgente, () => { lock (order) order.Add("urgente-cerca"); }, default, -5);
        cts.Cancel();
        gate.Set();
        await Task.WhenAll(block, bg, low, urgent, nearer);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.Equal(["urgente-cerca", "urgente", "baja", "fondo"], order);
    }

    [Fact]
    public async Task Paginas_pesadas_se_dibujan_rapido()
    {
        var specs = new List<PageSpec>();
        for (int i = 0; i < 6; i++)
        {
            var p = PdfBuilder.TextPage(i + 1, 40);
            p.ImageSize = 1400;
            p.PathCount = 400;
            specs.Add(p);
        }

        string path = Files.Create("pesado.pdf", PdfBuilder.Build(specs));
        long size = new FileInfo(path).Length;
        Assert.True(size > 3_000_000, "el archivo de prueba debería pesar varios MB; pesa " + size);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var doc = await PdfDocument.OpenAsync(path);
        long open = sw.ElapsedMilliseconds;
        sw.Restart();
        using var t = await doc.RenderTileAsync(new TileRequest(3, 1275, 1650, 0, 0, 0, 1275, 1650), PdfPriority.Urgente);
        long render = sw.ElapsedMilliseconds;
        Assert.True(open < 1500, "abrir tardó " + open + " ms");
        Assert.True(render < 3000, "dibujar tardó " + render + " ms");
    }

    [Fact]
    public async Task Se_vuelve_a_abrir_el_documento_para_liberar_memoria_y_sigue_funcionando()
    {
        var specs = new List<PageSpec>();
        for (int i = 0; i < 8; i++)
        {
            var p = PdfBuilder.TextPage(i + 1, 10);
            p.ImageSize = 600;
            specs.Add(p);
        }

        string path = Files.Create("reciclar.pdf", PdfBuilder.Build(specs, null, "Reciclar"));
        long old = PdfDocument.RecycleBytes;
        PdfDocument.RecycleBytes = 1; // cualquier página leída supera el tope
        try
        {
            using var doc = await PdfDocument.OpenAsync(path);
            for (int round = 0; round < 3; round++)
            {
                for (int p = 0; p < 8; p++)
                {
                    using var t = await doc.RenderTileAsync(new TileRequest(p, 300, 400, 0, 0, 0, 300, 400), PdfPriority.Urgente);
                    Assert.Equal(300, t.Width);
                }
            }

            Assert.True(doc.Recycles >= 3, "debería haberse reabierto varias veces; fueron " + doc.Recycles);
            var c = await doc.GetPageContentAsync(5, PdfPriority.Urgente);
            Assert.Contains("Pagina 6", Velora.Pdf.Viewer.TextMath.TextOfRange(c, 0, c.CharCount - 1));

            // Con cambios sin guardar no se reabre (se perderían)
            await doc.AddNoteAsync(0, 100, 100, "x", 0xFFFFC107, "t");
            int before = doc.Recycles;
            for (int p = 0; p < 8; p++) using (await doc.RenderTileAsync(new TileRequest(p, 300, 400, 0, 0, 0, 300, 400), PdfPriority.Urgente)) { }
            Assert.Equal(before, doc.Recycles);
            var c0 = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
            Assert.Contains(c0.Annotations, a => a.Kind == AnnotationKind.Nota);
        }
        finally
        {
            PdfDocument.RecycleBytes = old;
        }
    }

    [Fact]
    public async Task Seleccion_de_linea_y_palabra_sobre_texto_real()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple());
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        var (a, b) = Velora.Pdf.Viewer.TextMath.LineAt(c, 0);
        string line = Velora.Pdf.Viewer.TextMath.TextOfRange(c, a, b);
        Assert.Equal("Pagina 1 - Documento de prueba de Velora", line.Trim());
        var (wa, wb) = Velora.Pdf.Viewer.TextMath.WordAt(c, 12);
        Assert.Equal("Documento", Velora.Pdf.Viewer.TextMath.TextOfRange(c, wa, wb));
    }

    [Fact]
    public async Task Las_anotaciones_conservan_su_color()
    {
        using var doc = await PdfDocument.OpenAsync(Files.Simple("colores.pdf"));
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        await doc.AddMarkupAsync(0, MarkupKind.Resaltado, Velora.Pdf.Viewer.TextMath.RectsForRange(c, 0, 5), 0xFF8BC34A, "t");
        await doc.AddNoteAsync(0, 300, 300, "n", 0xFFE53935, "t");
        await doc.AddInkAsync(0, [(100, 400), (150, 450), (200, 400)], 0xFF1E88E5, 2f, "t");
        var list = (await doc.GetPageContentAsync(0, PdfPriority.Urgente)).Annotations;
        void Check(IReadOnlyList<PageAnnotation> l, string cuando)
        {
            uint Rgb(AnnotationKind k) => l.First(a => a.Kind == k).ColorArgb & 0xFFFFFF;
            Assert.True(Rgb(AnnotationKind.Resaltado) == 0x8BC34Au, cuando + ": resaltado " + Rgb(AnnotationKind.Resaltado).ToString("X6"));
            // El icono de una nota lo dibuja PDFium siempre en amarillo; su color real (/C) solo se lee antes de dibujarla.
            Assert.True(Rgb(AnnotationKind.Nota) is 0xE53935u or 0xFFFF00u, cuando + ": nota " + Rgb(AnnotationKind.Nota).ToString("X6"));
            Assert.True(Rgb(AnnotationKind.Tinta) == 0x1E88E5u, cuando + ": tinta " + Rgb(AnnotationKind.Tinta).ToString("X6"));
        }

        Check(list, "recién creadas");

        // Después de dibujar la página las anotaciones ya tienen apariencia propia: el color debe seguir leyéndose.
        using (await doc.RenderTileAsync(new TileRequest(0, 612, 792, 0, 0, 0, 612, 792), PdfPriority.Urgente)) { }
        Check((await doc.GetPageContentAsync(0, PdfPriority.Urgente)).Annotations, "tras dibujar");

        string saved = Files.Path_("colores-guardado.pdf");
        await doc.SaveCopyAsync(saved);
        using var reopened = await PdfDocument.OpenAsync(saved);
        Check((await reopened.GetPageContentAsync(0, PdfPriority.Urgente)).Annotations, "tras guardar y reabrir");
    }
}
