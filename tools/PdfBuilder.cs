// SPDX-License-Identifier: GPL-3.0-or-later
// Generador mínimo de PDF para pruebas y demostraciones (texto, enlaces, índice, imágenes y trazados).
using System.IO.Compression;
using System.Text;

namespace Velora.Testing;

public sealed class LinkSpec
{
    public double X1, Y1, X2, Y2;
    public int? DestPage;
    public string? Uri;
}

public sealed class PageSpec
{
    public double Width = 612, Height = 792;
    public int Rotate;
    public string[] Lines = [];
    public List<LinkSpec> Links = new();
    /// <summary>Lado en píxeles de una imagen de ruido (0 = sin imagen). Hace la página «pesada».</summary>
    public int ImageSize;
    /// <summary>Cantidad de trazos vectoriales decorativos.</summary>
    public int PathCount;
}

public sealed class OutlineSpec
{
    public string Title = "";
    public int Page;
    public List<OutlineSpec> Children = new();
}

public static class PdfBuilder
{
    public static byte[] Build(IReadOnlyList<PageSpec> pages, IReadOnlyList<OutlineSpec>? outline = null, string? title = null, string? author = null)
    {
        var objs = new List<byte[]?>();
        int Reserve() { objs.Add(null); return objs.Count; }
        void Set(int n, string dict, byte[]? stream = null) => objs[n - 1] = Obj(n, dict, stream);

        int catalog = Reserve(), pagesRoot = Reserve(), font = Reserve();
        int info = Reserve();
        int outlinesRoot = outline is { Count: > 0 } ? Reserve() : 0;

        var pageObjs = new int[pages.Count];
        for (int i = 0; i < pages.Count; i++) pageObjs[i] = Reserve();

        // Índice
        if (outlinesRoot != 0)
        {
            int total = 0;
            int Build(IReadOnlyList<OutlineSpec> items, int parent, out int first, out int last)
            {
                first = last = 0;
                var nums = items.Select(_ => Reserve()).ToList();
                int count = 0;
                for (int k = 0; k < items.Count; k++)
                {
                    var it = items[k];
                    int ch = 0, cf = 0, cl = 0;
                    if (it.Children.Count > 0) ch = Build(it.Children, nums[k], out cf, out cl);
                    total++;
                    var sb = new StringBuilder($"<< /Title ({Esc(it.Title)}) /Parent {parent} 0 R");
                    sb.Append($" /Dest [{pageObjs[Math.Clamp(it.Page, 0, pages.Count - 1)]} 0 R /XYZ 72 {pages[Math.Clamp(it.Page, 0, pages.Count - 1)].Height - 72:0} 0]");
                    if (k > 0) sb.Append($" /Prev {nums[k - 1]} 0 R");
                    if (k < items.Count - 1) sb.Append($" /Next {nums[k + 1]} 0 R");
                    if (it.Children.Count > 0) sb.Append($" /First {cf} 0 R /Last {cl} 0 R /Count {ch}");
                    sb.Append(" >>");
                    Set(nums[k], sb.ToString());
                    count += 1 + ch;
                }

                first = nums[0];
                last = nums[^1];
                return count;
            }

            int cnt = Build(outline!, outlinesRoot, out int f, out int l);
            Set(outlinesRoot, $"<< /Type /Outlines /First {f} 0 R /Last {l} 0 R /Count {cnt} >>");
        }

        Set(font, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Set(info, $"<< /Producer (Velora test) /Title ({Esc(title ?? "Documento de prueba")}) /Author ({Esc(author ?? "Velora")}) /CreationDate (D:20260101120000) >>");
        Set(catalog, "<< /Type /Catalog /Pages " + pagesRoot + " 0 R" + (outlinesRoot != 0 ? $" /Outlines {outlinesRoot} 0 R /PageMode /UseOutlines" : "") + " >>");
        Set(pagesRoot, "<< /Type /Pages /Count " + pages.Count + " /Kids [" + string.Join(" ", pageObjs.Select(p => p + " 0 R")) + "] >>");

        var rnd = new Random(12345);
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var content = new StringBuilder();
            for (int k = 0; k < p.PathCount; k++)
            {
                content.Append($"{rnd.NextDouble():0.00} {rnd.NextDouble():0.00} {rnd.NextDouble():0.00} rg ");
                double x = rnd.NextDouble() * (p.Width - 60), y = rnd.NextDouble() * (p.Height - 60);
                content.Append($"{x:0.#} {y:0.#} m {x + 40 * rnd.NextDouble():0.#} {y + 5:0.#} l {x + 30:0.#} {y + 40 * rnd.NextDouble():0.#} l f ");
            }

            string? imgName = null;
            int imgObj = 0;
            if (p.ImageSize > 0)
            {
                imgObj = Reserve();
                imgName = "/Im1";
                Set(imgObj, $"<< /Type /XObject /Subtype /Image /Width {p.ImageSize} /Height {p.ImageSize} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode", NoiseImage(p.ImageSize, i));
                content.Append($"q {p.Width - 144:0} 0 0 {(p.Width - 144) * 0.6:0} 72 {p.Height / 2 - 100:0} cm {imgName} Do Q ");
            }

            content.Append("0 g BT /F1 12 Tf 14 TL 72 " + (p.Height - 72).ToString("0") + " Td ");
            foreach (var line in p.Lines) content.Append('(').Append(Esc(line)).Append(") Tj T* ");
            content.Append("ET");

            int contentObj = Reserve();
            Set(contentObj, "<< ", Encoding.Latin1.GetBytes(content.ToString()));

            var annots = new List<int>();
            foreach (var link in p.Links)
            {
                int a = Reserve();
                string action = link.Uri != null
                    ? $"/A << /S /URI /URI ({Esc(link.Uri)}) >>"
                    : $"/Dest [{pageObjs[Math.Clamp(link.DestPage ?? 0, 0, pages.Count - 1)]} 0 R /XYZ 72 {pages[Math.Clamp(link.DestPage ?? 0, 0, pages.Count - 1)].Height - 72:0} 0]";
                Set(a, $"<< /Type /Annot /Subtype /Link /Rect [{link.X1:0.##} {link.Y1:0.##} {link.X2:0.##} {link.Y2:0.##}] /Border [0 0 0] {action} >>");
                annots.Add(a);
            }

            string res = "/Font << /F1 " + font + " 0 R >>" + (imgObj != 0 ? $" /XObject << {imgName} {imgObj} 0 R >>" : "");
            Set(pageObjs[i],
                $"<< /Type /Page /Parent {pagesRoot} 0 R /MediaBox [0 0 {p.Width:0.##} {p.Height:0.##}] /Rotate {p.Rotate} /Contents {contentObj} 0 R /Resources << {res} >>" +
                (annots.Count > 0 ? " /Annots [" + string.Join(" ", annots.Select(a => a + " 0 R")) + "]" : "") + " >>");
        }

        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W("%PDF-1.5\n%âãÏÓ\n");
        var offsets = new long[objs.Count];
        for (int i = 0; i < objs.Count; i++)
        {
            offsets[i] = ms.Position;
            ms.Write(objs[i] ?? throw new InvalidOperationException("Objeto sin definir " + (i + 1)));
        }

        long xref = ms.Position;
        W($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objs.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] Obj(int n, string dict, byte[]? stream)
    {
        using var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.Latin1.GetBytes(s));
        W($"{n} 0 obj\n");
        if (stream == null)
        {
            W(dict + "\nendobj\n");
        }
        else
        {
            // En los objetos con flujo, «dict» llega sin cerrar para poder añadir /Length.
            W(dict + $" /Length {stream.Length} >>\nstream\n");
            ms.Write(stream);
            W("\nendstream\nendobj\n");
        }

        return ms.ToArray();
    }

    private static byte[] NoiseImage(int size, int seed)
    {
        var raw = new byte[size * size * 3];
        var rnd = new Random(seed * 7919 + 17);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int k = (y * size + x) * 3;
                raw[k] = (byte)((x * 255 / size + rnd.Next(48)) & 0xFF);
                raw[k + 1] = (byte)((y * 255 / size + rnd.Next(48)) & 0xFF);
                raw[k + 2] = (byte)(((x + y) * 127 / size + rnd.Next(64)) & 0xFF);
            }
        }

        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        return ms.ToArray();
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c is '(' or ')' or '\\') sb.Append('\\');
            sb.Append(c <= 0xFF ? c : '?');
        }

        return sb.ToString();
    }

    /// <summary>Página típica con unas líneas de texto numeradas.</summary>
    public static PageSpec TextPage(int number, int lines = 30)
    {
        var l = new List<string> { $"Pagina {number} - Documento de prueba de Velora" };
        for (int i = 1; i < lines; i++) l.Add($"Linea {i}: el veloz murcielago hindu comia feliz cardillo y kiwi {number}.");
        return new PageSpec { Lines = l.ToArray() };
    }
}
