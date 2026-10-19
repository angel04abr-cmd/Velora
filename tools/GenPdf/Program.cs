// SPDX-License-Identifier: GPL-3.0-or-later
// Genera PDF de prueba para Velora: uso  GenPdf <carpeta de salida>
using Velora.Testing;

string outDir = args.Length > 0 ? args[0] : "pruebas";
Directory.CreateDirectory(outDir);

// 1) Documento de demostración (pequeño, con índice, enlaces y varios tamaños)
{
    var pages = new List<PageSpec>();
    for (int i = 1; i <= 12; i++)
    {
        var p = PdfBuilder.TextPage(i, 34);
        if (i == 1)
        {
            p.Lines = new[]
            {
                "Guia de demostracion de Velora",
                "",
                "Este documento sirve para probar el visor: texto seleccionable, indice,",
                "enlaces entre paginas, busqueda y anotaciones.",
                "",
                "Ir a la pagina 5 (enlace interno)",
                "Visitar example.org (enlace externo)",
            }.Concat(p.Lines.Skip(8)).ToArray();
            p.Links.Add(new LinkSpec { X1 = 72, Y1 = 792 - 72 - 14 * 5 - 3, X2 = 280, Y2 = 792 - 72 - 14 * 5 + 11, DestPage = 4 });
            p.Links.Add(new LinkSpec { X1 = 72, Y1 = 792 - 72 - 14 * 6 - 3, X2 = 260, Y2 = 792 - 72 - 14 * 6 + 11, Uri = "https://example.org/" });
        }

        if (i == 8) { p.Width = 842; p.Height = 595; } // una página apaisada
        p.PathCount = 30;
        pages.Add(p);
    }

    var outline = new List<OutlineSpec>
    {
        new() { Title = "Introducción", Page = 0 },
        new() { Title = "Capítulo 1: Lectura", Page = 2, Children = { new OutlineSpec { Title = "1.1 Texto", Page = 3 }, new OutlineSpec { Title = "1.2 Enlaces", Page = 4 } } },
        new() { Title = "Capítulo 2: Anotaciones", Page = 7 },
        new() { Title = "Apéndice", Page = 11 },
    };
    File.WriteAllBytes(Path.Combine(outDir, "demo.pdf"), PdfBuilder.Build(pages, outline, "Guía de demostración", "Equipo Velora"));
}

// 2) Muchas páginas (2000) con texto y dibujos vectoriales
{
    var pages = new List<PageSpec>();
    for (int i = 1; i <= 2000; i++)
    {
        var p = PdfBuilder.TextPage(i, 40);
        p.PathCount = 250;
        pages.Add(p);
    }

    var outline = new List<OutlineSpec>();
    for (int c = 0; c < 20; c++)
    {
        var o = new OutlineSpec { Title = $"Capítulo {c + 1}", Page = c * 100 };
        for (int s = 0; s < 5; s++) o.Children.Add(new OutlineSpec { Title = $"Sección {c + 1}.{s + 1}", Page = c * 100 + s * 20 });
        outline.Add(o);
    }

    File.WriteAllBytes(Path.Combine(outDir, "2000-paginas.pdf"), PdfBuilder.Build(pages, outline, "Dos mil páginas"));
}

// 3) Pesado: imágenes grandes en cada página
{
    int count = args.Length > 1 && int.TryParse(args[1], out int c) ? c : 80;
    var pages = new List<PageSpec>();
    for (int i = 1; i <= count; i++)
    {
        var p = PdfBuilder.TextPage(i, 30);
        p.ImageSize = 1500;
        p.PathCount = 800;
        pages.Add(p);
    }

    File.WriteAllBytes(Path.Combine(outDir, "pesado-imagenes.pdf"), PdfBuilder.Build(pages, null, "PDF pesado con imágenes"));
}

foreach (var f in Directory.GetFiles(outDir, "*.pdf")) Console.WriteLine($"{Path.GetFileName(f)}: {new FileInfo(f).Length / 1048576.0:0.0} MB");
