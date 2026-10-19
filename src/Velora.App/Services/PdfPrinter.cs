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

/// <summary>Impresión con el cuadro estándar de Windows. Cada página se dibuja con PDFium a 300 ppp.</summary>
internal static class PdfPrinter
{
    public static Task<bool> PrintAsync(Window owner, PdfDocument doc, PageSize[] sizes, int currentPage)
    {
        var pd = new PrintDialog
        {
            UserPageRangeEnabled = true,
            CurrentPageEnabled = true,
            MinPage = 1,
            MaxPage = (uint)doc.PageCount,
            PageRange = new PageRange(1, doc.PageCount),
        };
        if (pd.ShowDialog() != true) return Task.FromResult(false);

        var pages = new List<int>();
        switch (pd.PageRangeSelection)
        {
            case PageRangeSelection.CurrentPage:
                pages.Add(currentPage);
                break;
            case PageRangeSelection.UserPages:
                for (int p = Math.Max(1, pd.PageRange.PageFrom); p <= Math.Min(doc.PageCount, pd.PageRange.PageTo); p++) pages.Add(p - 1);
                break;
            default:
                for (int p = 0; p < doc.PageCount; p++) pages.Add(p);
                break;
        }

        if (pages.Count == 0) return Task.FromResult(false);
        var area = new Size(pd.PrintableAreaWidth, pd.PrintableAreaHeight);
        if (area.Width <= 0 || area.Height <= 0) area = new Size(793, 1122);
        var paginator = new PdfPaginator(doc, sizes, pages, area);
        pd.PrintDocument(paginator, Path.GetFileName(doc.Path));
        return Task.FromResult(true);
    }

    internal static DocumentPaginator CreatePaginator(PdfDocument doc, PageSize[] sizes, List<int> pages, Size area) =>
        new PdfPaginator(doc, sizes, pages, area);

    private sealed class PdfPaginator : DocumentPaginator
    {
        private const double TargetDpi = 300;
        private readonly PdfDocument _doc;
        private readonly PageSize[] _sizes;
        private readonly List<int> _pages;

        public PdfPaginator(PdfDocument doc, PageSize[] sizes, List<int> pages, Size area)
        {
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
            // Si la página es apaisada y el papel vertical (o al revés), se gira para aprovechar el papel.
            bool rotate = (s.Width > s.Height) != (area.Width > area.Height);
            double pw = rotate ? s.Height : s.Width, ph = rotate ? s.Width : s.Height;
            double k = Math.Min(area.Width / pw, area.Height / ph);
            double wDip = pw * k, hDip = ph * k;
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
