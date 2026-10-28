// SPDX-License-Identifier: GPL-3.0-or-later
using Velora.Pdf;
using Velora.Testing;

namespace Velora.Tests;

public class LinkTests
{
    [Fact]
    public async Task Un_indice_con_muchos_enlaces_a_paginas_distintas_se_lee_completo()
    {
        // Antes se abría la página de destino de cada enlace y, al pasar el máximo de páginas abiertas, se cerraba la que se estaba leyendo:
        // un índice de 26 entradas solo reconocía 11.
        var specs = new List<PageSpec>();
        for (int i = 1; i <= 30; i++)
        {
            var p = PdfBuilder.TextPage(i, 10);
            if (i == 1)
                for (int k = 0; k < 26; k++)
                    p.Links.Add(new LinkSpec { X1 = 72, Y1 = 700 - k * 20, X2 = 300, Y2 = 716 - k * 20, DestPage = 2 + k });
            specs.Add(p);
        }

        string path = Files.Create("indice.pdf", PdfBuilder.Build(specs, null, "Índice"));
        using var doc = await PdfDocument.OpenAsync(path);
        var c = await doc.GetPageContentAsync(0, PdfPriority.Urgente);
        var gotos = c.Links.Where(l => l.Kind == LinkKind.Goto).Select(l => l.TargetPage).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(2, 26).ToList(), gotos);
        // y el texto de la página sigue accesible después de leer los enlaces
        Assert.True(c.CharCount > 0);
    }
}
