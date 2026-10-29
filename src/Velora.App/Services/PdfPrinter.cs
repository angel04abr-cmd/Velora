// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Velora.Pdf;

namespace Velora.App;

/// <summary>Impresión: la ventana de Velora con vista previa elige los ajustes y cada página se dibuja con PDFium a 300 ppp.</summary>
internal static class PdfPrinter
{
    public static Task<bool> PrintAsync(Window owner, PdfDocument doc, PageSize[] sizes, int currentPage) =>
        Task.FromResult(new PrintWindow(owner, doc, sizes, currentPage).ShowDialog() == true);

    internal static DocumentPaginator CreatePaginator(PdfDocument doc, PageSize[] sizes, List<int> pages, Size area, bool autoRotate = true) =>
        new PdfPaginator(doc, sizes, pages, area, autoRotate);

    /// <summary>
    /// Cómo cabe una página en el área imprimible: si hay que girarla (página apaisada en papel vertical o al revés, solo en modo automático)
    /// y el tamaño que ocupa, en unidades de 1/96 de pulgada.
    /// </summary>
    internal static (bool Rotate, double Width, double Height) Fit(PageSize s, Size area, bool autoRotate)
    {
        bool rotate = autoRotate && (s.Width > s.Height) != (area.Width > area.Height);
        double pw = rotate ? s.Height : s.Width, ph = rotate ? s.Width : s.Height;
        double k = Math.Min(area.Width / pw, area.Height / ph);
        return (rotate, pw * k, ph * k);
    }

    private sealed class PdfPaginator : DocumentPaginator
    {
        private const double TargetDpi = 300;
        private readonly PdfDocument _doc;
        private readonly PageSize[] _sizes;
        private readonly List<int> _pages;
        private readonly bool _autoRotate;

        public PdfPaginator(PdfDocument doc, PageSize[] sizes, List<int> pages, Size area, bool autoRotate)
        {
            _autoRotate = autoRotate;
            _doc = doc;
            _sizes = sizes;
            _pages = pages;
            PageSize = area;
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _pages.Count;
        public override Size PageSize { get; set; }
        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            int page = _pages[pageNumber];
            var s = _sizes[page];
            var area = PageSize;
            // Si la página es apaisada y el papel vertical (o al revés), se gira para aprovechar el papel (solo en modo automático).
            var (rotate, wDip, hDip) = Fit(s, area, _autoRotate);
            double pxPerDip = TargetDpi / 96.0;
            int wPx = Math.Clamp((int)(wDip * pxPerDip), 1, 6000), hPx = Math.Clamp((int)(hDip * pxPerDip), 1, 6000);

            // Se pide a PDFium el dibujo ya girado. El hilo de PDFium no usa la ventana, así que esperar aquí no la bloquea.
            using var tile = _doc.RenderTileAsync(new TileRequest(page, wPx, hPx, rotate ? 1 : 0, 0, 0, wPx, hPx, Annotations: true, Printing: true),
                PdfPriority.Interactivo).GetAwaiter().GetResult();
            var bmp = BitmapSource.Create(tile.Width, tile.Height, TargetDpi, TargetDpi, PixelFormats.Pbgra32, null, tile.Pixels, tile.Stride);
            bmp.Freeze();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(bmp, new Rect((area.Width - wDip) / 2, (area.Height - hDip) / 2, wDip, hDip));
            }

            return new DocumentPage(visual, area, new Rect(area), new Rect(area));
        }
    }
}
