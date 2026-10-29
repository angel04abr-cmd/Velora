// SPDX-License-Identifier: GPL-3.0-or-later
using Velora.Pdf.Viewer;

namespace Velora.Tests;

public class PageRangeTests
{
    [Fact]
    public void Intervalos_y_paginas_sueltas()
    {
        var r = PageRanges.Parse("1-3, 7, 10-11", 20, out var problem);
        Assert.Null(problem);
        Assert.Equal([0, 1, 2, 6, 9, 10], r);
    }

    [Fact]
    public void Sin_repetir_y_con_el_intervalo_al_reves()
    {
        Assert.Equal([2, 3, 4, 0], PageRanges.Parse("5-3, 3, 1", 10, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("1-99")]
    [InlineData("3-")]
    [InlineData("99999999999")]
    public void Texto_no_valido_da_un_motivo(string text)
    {
        Assert.Null(PageRanges.Parse(text, 10, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }
}
