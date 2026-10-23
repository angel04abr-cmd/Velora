// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Velora.Pdf.Native;
using Microsoft.Win32.SafeHandles;

namespace Velora.Pdf;

/// <summary>
/// Documento PDF abierto. Todas las llamadas a PDFium se ejecutan en el hilo de <see cref="PdfWorker"/>;
/// los métodos públicos son asíncronos y se pueden llamar desde cualquier hilo.
/// </summary>
public sealed unsafe class PdfDocument : IDisposable
{
    private const int MaxOpenPages = 8;

    private delegate uint BufReader(void* buffer, uint length);

    private sealed class PageEntry
    {
        public nint Page;
        public nint Text;
        public float Width, Height;
        public Affine ToPageSpace;
        public Affine FromPageSpace;
        public long Tick;
    }

    private readonly PdfWorker _worker = PdfWorker.Shared;
    private readonly Dictionary<int, PageEntry> _pages = new();
    private nint _doc;
    private FileReader? _reader;
    private GCHandle _readerHandle;
    private FpdfFileAccess* _access;
    private long _tick;
    private bool _closed;
    private volatile bool _modified;
    private byte[]? _passwordBytes;
    private readonly HashSet<int> _touched = new();
    private long _avgPageBytes;

    /// <summary>
    /// PDFium conserva en memoria (hasta cerrar el documento) las imágenes y objetos de cada página que se ha leído.
    /// En documentos escaneados eso crecía sin límite al recorrerlos. Cuando lo estimado supera este tope y no hay
    /// cambios sin guardar, se vuelve a abrir el documento para soltar esa memoria.
    /// </summary>
    internal static long RecycleBytes = 256L << 20;

    /// <summary>
    /// La estimación por tamaño de archivo se queda corta en libros escaneados (cada página leída retiene mucho más de lo que pesa en el archivo):
    /// además se mira cuánto ha crecido de verdad la memoria del proceso desde la última vez que se soltó.
    /// </summary>
    internal static long GrowthBytes => RecycleBytes + (128L << 20);

    private long _memBaseline;
    private int _memCheckedAt;

    private PdfDocument(string path, long fileSize)
    {
        Path = path;
        FileSize = fileSize;
    }

    public string Path { get; }
    public long FileSize { get; }
    public int PageCount { get; private set; }

    /// <summary>Hay anotaciones sin guardar.</summary>
    public bool IsModified => _modified;

    // ------------------------------------------------------------------ apertura

    public static Task<PdfDocument> OpenAsync(string path, string? password = null, CancellationToken ct = default) =>
        PdfWorker.Shared.Run(PdfPriority.Interactivo, () => OpenCore(path, password), ct);

    private static PdfDocument OpenCore(string path, string? password)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new PdfException("No se encuentra el archivo.");
        if (info.Length > uint.MaxValue) throw new PdfException("El archivo supera los 4 GB, que es el límite del motor PDF.");
        if (info.Length == 0) throw new PdfException("El archivo está vacío.");

        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PdfException("No se puede abrir el archivo: " + ex.Message, ex);
        }

        var doc = new PdfDocument(path, info.Length);
        doc._reader = new FileReader(handle);
        doc._readerHandle = GCHandle.Alloc(doc._reader);
        doc._access = (FpdfFileAccess*)NativeMemory.AllocZeroed((nuint)sizeof(FpdfFileAccess));
        doc._access->FileLen = (uint)info.Length;
        doc._access->GetBlock = &GetBlockCallback;
        doc._access->Param = (void*)GCHandle.ToIntPtr(doc._readerHandle);

        byte[]? pw = string.IsNullOrEmpty(password) ? null : Encoding.UTF8.GetBytes(password + "\0");
        nint d;
        if (pw == null) d = Pdfium.FPDF_LoadCustomDocument(doc._access, null);
        else fixed (byte* p = pw) d = Pdfium.FPDF_LoadCustomDocument(doc._access, p);

        if (d == 0)
        {
            uint err = Pdfium.FPDF_GetLastError();
            doc.ReleaseNative();
            throw err switch
            {
                Pdfium.ErrPassword => new PdfPasswordException(pw != null),
                Pdfium.ErrFormat => new PdfException("El archivo no es un PDF válido o está dañado."),
                Pdfium.ErrSecurity => new PdfException("El documento usa un tipo de seguridad que no se puede abrir."),
                Pdfium.ErrFile => new PdfException("No se pudo leer el archivo."),
                _ => new PdfException("No se pudo abrir el documento (código " + err + ")."),
            };
        }

        doc._doc = d;
        doc.PageCount = Pdfium.FPDF_GetPageCount(d);
        doc._avgPageBytes = info.Length / Math.Max(1, doc.PageCount);
        if (doc.PageCount <= 0)
        {
            doc.CloseCore();
            throw new PdfException("El documento no tiene páginas.");
        }

        return doc;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int GetBlockCallback(void* param, uint position, byte* buffer, uint size)
    {
        try
        {
            var reader = (FileReader)GCHandle.FromIntPtr((nint)param).Target!;
            return reader.Read(position, new Span<byte>(buffer, (int)size)) ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class FileReader : IDisposable
    {
        private readonly SafeFileHandle _handle;

        public FileReader(SafeFileHandle handle) => _handle = handle;

        public bool Read(long position, Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                int n = RandomAccess.Read(_handle, destination, position);
                if (n <= 0) return false;
                destination = destination[n..];
                position += n;
            }

            return true;
        }

        public void Dispose() => _handle.Dispose();
    }

    // ------------------------------------------------------------------ cierre

    public void Dispose() => _ = CloseAsync();

    /// <summary>Cierra el documento en el hilo de PDFium (después de lo que ya esté en marcha) y libera el archivo.</summary>
    public Task CloseAsync() => _worker.Run(PdfPriority.Interactivo, CloseCore);

    private void CloseCore()
    {
        if (_closed) return;
        _closed = true;
        foreach (var e in _pages.Values) ClosePage(e);
        _pages.Clear();
        if (_doc != 0)
        {
            Pdfium.FPDF_CloseDocument(_doc);
            _doc = 0;
        }

        ReleaseNative();
    }

    private void ReleaseNative()
    {
        if (_access != null)
        {
            NativeMemory.Free(_access);
            _access = null;
        }

        if (_readerHandle.IsAllocated) _readerHandle.Free();
        _reader?.Dispose();
        _reader = null;
    }

    private Task<T> Job<T>(PdfPriority priority, Func<T> work, CancellationToken ct = default, int sub = 0) =>
        _worker.Run(priority, () =>
        {
            MaybeRecycle();
            return work();
        }, ct, sub);

    private Task Job(PdfPriority priority, Action work, CancellationToken ct = default, int sub = 0) =>
        Job<object?>(priority, () => { work(); return null; }, ct, sub);

    private void MaybeRecycle()
    {
        if (_closed || _modified || _doc == 0) return;
        if (_touched.Count * _avgPageBytes * 13 / 10 >= RecycleBytes)
        {
            RecycleCore();
            return;
        }

        if (_touched.Count - _memCheckedAt < 8) return;
        _memCheckedAt = _touched.Count;
        if (_memBaseline == 0) _memBaseline = PrivateBytes();
        else if (PrivateBytes() - _memBaseline > GrowthBytes) RecycleCore();
    }

    private static long PrivateBytes()
    {
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        return p.PrivateMemorySize64;
    }

    private void RecycleCore()
    {
        foreach (var e in _pages.Values) ClosePage(e);
        _pages.Clear();
        _touched.Clear();
        _memCheckedAt = 0;
        Pdfium.FPDF_CloseDocument(_doc);
        _doc = 0;
        nint d;
        if (_passwordBytes == null) d = Pdfium.FPDF_LoadCustomDocument(_access, null);
        else fixed (byte* p = _passwordBytes) d = Pdfium.FPDF_LoadCustomDocument(_access, p);
        if (d == 0) throw new PdfException("No se pudo volver a abrir el documento.");
        _doc = d;
        _memBaseline = PrivateBytes();
        Recycles++;
    }

    /// <summary>Veces que se ha vuelto a abrir el documento para liberar memoria (diagnóstico).</summary>
    public int Recycles { get; private set; }

    private void ThrowIfClosed()
    {
        if (_closed) throw new ObjectDisposedException(nameof(PdfDocument));
    }

    private static void ClosePage(PageEntry e)
    {
        if (e.Text != 0) Pdfium.FPDFText_ClosePage(e.Text);
        if (e.Page != 0) Pdfium.FPDF_ClosePage(e.Page);
        e.Text = 0;
        e.Page = 0;
    }

    // ------------------------------------------------------------------ páginas

    private PageEntry GetPage(int index)
    {
        ThrowIfClosed();
        if ((uint)index >= (uint)PageCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (_pages.TryGetValue(index, out var e))
        {
            e.Tick = ++_tick;
            return e;
        }

        if (_pages.Count >= MaxOpenPages)
        {
            int oldest = -1;
            long min = long.MaxValue;
            foreach (var kv in _pages)
            {
                if (kv.Value.Tick < min)
                {
                    min = kv.Value.Tick;
                    oldest = kv.Key;
                }
            }

            if (oldest >= 0)
            {
                ClosePage(_pages[oldest]);
                _pages.Remove(oldest);
            }
        }

        nint page = Pdfium.FPDF_LoadPage(_doc, index);
        if (page == 0) throw new PdfException("No se pudo leer la página " + (index + 1) + ".");
        _touched.Add(index);

        e = new PageEntry
        {
            Page = page,
            Width = Pdfium.FPDF_GetPageWidthF(page),
            Height = Pdfium.FPDF_GetPageHeightF(page),
            Tick = ++_tick,
        };
        BuildTransform(e);
        _pages[index] = e;
        return e;
    }

    /// <summary>
    /// Calcula cómo pasar de las coordenadas internas del PDF (origen abajo-izquierda, con el recuadro de
    /// recorte y la rotación propias de la página) a coordenadas de página (origen arriba-izquierda, en puntos).
    /// Se le pregunta a PDFium con tres puntos y se obtiene la transformación afín exacta.
    /// </summary>
    private static void BuildTransform(PageEntry e)
    {
        const int scale = 1000;
        const double span = 10000;
        int sx = Math.Max(1, (int)(e.Width * scale));
        int sy = Math.Max(1, (int)(e.Height * scale));
        int x0, y0, x1, y1, x2, y2;
        Pdfium.FPDF_PageToDevice(e.Page, 0, 0, sx, sy, 0, 0, 0, &x0, &y0);
        Pdfium.FPDF_PageToDevice(e.Page, 0, 0, sx, sy, 0, span, 0, &x1, &y1);
        Pdfium.FPDF_PageToDevice(e.Page, 0, 0, sx, sy, 0, 0, span, &x2, &y2);
        double k = 1.0 / scale;
        e.ToPageSpace = new Affine(
            (x1 - x0) / span * k, (y1 - y0) / span * k,
            (x2 - x0) / span * k, (y2 - y0) / span * k,
            x0 * k, y0 * k);
        e.FromPageSpace = e.ToPageSpace.Invert();
    }

    private nint GetText(PageEntry e)
    {
        if (e.Text == 0) e.Text = Pdfium.FPDFText_LoadPage(e.Page);
        return e.Text;
    }

    public Task<PageSize[]> GetPageSizesAsync(CancellationToken ct = default) =>
        Job(PdfPriority.Urgente, () =>
        {
            ThrowIfClosed();
            var sizes = new PageSize[PageCount];
            FsSizeF s;
            for (int i = 0; i < sizes.Length; i++)
            {
                if (Pdfium.FPDF_GetPageSizeByIndexF(_doc, i, &s) != 0 && s.Width > 0 && s.Height > 0)
                    sizes[i] = new PageSize(s.Width, s.Height);
                else
                    sizes[i] = i > 0 ? sizes[i - 1] : new PageSize(612, 792);
            }

            return sizes;
        }, ct);

    // ------------------------------------------------------------------ dibujo

    public Task<RenderedTile> RenderTileAsync(TileRequest r, PdfPriority priority, CancellationToken ct = default, int sub = 0) =>
        Job(priority, () => RenderTileCore(r), ct, sub);

    private RenderedTile RenderTileCore(TileRequest r)
    {
        var e = GetPage(r.Page);
        int w = r.Width, h = r.Height;
        if (w <= 0 || h <= 0 || (long)w * h > 120_000_000) throw new ArgumentException("Tamaño de baldosa no válido.");
        byte[] buf = ArrayPool<byte>.Shared.Rent(w * h * 4);
        try
        {
            fixed (byte* p = buf)
            {
                nint bmp = Pdfium.FPDFBitmap_CreateEx(w, h, Pdfium.BitmapBgra, p, w * 4);
                if (bmp == 0) throw new PdfException("No hay memoria suficiente para dibujar la página.");
                Pdfium.FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                int flags = Pdfium.RenderLimitedImageCache | (r.Annotations ? Pdfium.RenderAnnot : 0) | (r.Printing ? Pdfium.RenderPrinting : 0);
                Pdfium.FPDF_RenderPageBitmap(bmp, e.Page, -r.X, -r.Y, r.PagePxWidth, r.PagePxHeight, r.Rotation & 3, flags);
                Pdfium.FPDFBitmap_Destroy(bmp);
            }
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buf);
            throw;
        }

        return new RenderedTile(buf, w, h);
    }

    // ------------------------------------------------------------------ contenido de página

    public Task<PageContent> GetPageContentAsync(int page, PdfPriority priority, CancellationToken ct = default, int sub = 0) =>
        Job(priority, () => GetPageContentCore(page), ct, sub);

    private PageContent GetPageContentCore(int page)
    {
        var e = GetPage(page);
        nint tp = GetText(e);
        int n = tp != 0 ? Pdfium.FPDFText_CountChars(tp) : 0;
        var chars = new int[n];
        var boxes = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            chars[i] = (int)Pdfium.FPDFText_GetUnicode(tp, i);
            double l, r, b, t;
            if (Pdfium.FPDFText_GetCharBox(tp, i, &l, &r, &b, &t) != 0)
            {
                var rect = e.ToPageSpace.Apply(new RectF((float)l, (float)b, (float)r, (float)t));
                boxes[4 * i] = rect.Left;
                boxes[4 * i + 1] = rect.Top;
                boxes[4 * i + 2] = rect.Right;
                boxes[4 * i + 3] = rect.Bottom;
            }
        }

        var links = new List<PageLink>();
        ReadLinkAnnotations(e, links);
        if (tp != 0) ReadWebLinks(e, tp, links);

        return new PageContent(page, e.Width, e.Height, chars, boxes, links.ToArray(), ReadAnnotations(e));
    }

    private RectF PdfRectToPage(PageEntry e, double left, double bottom, double right, double top) =>
        e.ToPageSpace.Apply(new RectF((float)left, (float)Math.Min(bottom, top), (float)right, (float)Math.Max(bottom, top)));

    private void ReadLinkAnnotations(PageEntry e, List<PageLink> links)
    {
        int pos = 0;
        nint link;
        int guard = 0;
        while (Pdfium.FPDFLink_Enumerate(e.Page, &pos, &link) != 0 && guard++ < 20000)
        {
            FsRectF rc;
            if (Pdfium.FPDFLink_GetAnnotRect(link, &rc) == 0) continue;
            var rect = PdfRectToPage(e, rc.Left, rc.Bottom, rc.Right, rc.Top);
            nint action = Pdfium.FPDFLink_GetAction(link);
            uint type = action != 0 ? Pdfium.FPDFAction_GetType(action) : 0;
            if (type == Pdfium.ActionUri)
            {
                string uri = ReadAscii((buf, len) => Pdfium.FPDFAction_GetURIPath(_doc, action, buf, len));
                if (uri.Length > 0) links.Add(new PageLink(rect, LinkKind.Uri, -1, float.NaN, float.NaN, uri));
                continue;
            }

            nint dest = type == Pdfium.ActionGoto ? Pdfium.FPDFAction_GetDest(_doc, action) : 0;
            if (dest == 0 && type == 0) dest = Pdfium.FPDFLink_GetDest(_doc, link);
            if (dest == 0) continue;
            int target = Pdfium.FPDFDest_GetDestPageIndex(_doc, dest);
            if (target < 0 || target >= PageCount) continue;
            var (tx, ty) = DestLocation(dest);
            if (!float.IsNaN(tx) || !float.IsNaN(ty))
            {
                // La posición se da en coordenadas PDF de la página de destino.
                var te = GetPage(target);
                var (px, py) = te.ToPageSpace.Apply(float.IsNaN(tx) ? 0 : tx, float.IsNaN(ty) ? 0 : ty);
                tx = float.IsNaN(tx) ? float.NaN : (float)px;
                ty = float.IsNaN(ty) ? float.NaN : (float)py;
            }

            links.Add(new PageLink(rect, LinkKind.Goto, target, tx, ty, null));
        }
    }

    private static (float X, float Y) DestLocation(nint dest)
    {
        int hasX, hasY, hasZoom;
        float x, y, zoom;
        if (Pdfium.FPDFDest_GetLocationInPage(dest, &hasX, &hasY, &hasZoom, &x, &y, &zoom) == 0)
            return (float.NaN, float.NaN);
        return (hasX != 0 ? x : float.NaN, hasY != 0 ? y : float.NaN);
    }

    private void ReadWebLinks(PageEntry e, nint tp, List<PageLink> links)
    {
        nint wl = Pdfium.FPDFLink_LoadWebLinks(tp);
        if (wl == 0) return;
        try
        {
            int count = Math.Min(Pdfium.FPDFLink_CountWebLinks(wl), 5000);
            for (int i = 0; i < count; i++)
            {
                int len = Pdfium.FPDFLink_GetURL(wl, i, null, 0);
                if (len <= 1) continue;
                var buf = new ushort[len];
                fixed (ushort* p = buf) Pdfium.FPDFLink_GetURL(wl, i, p, len);
                string url = new string(MemoryMarshal.Cast<ushort, char>(buf.AsSpan(0, len - 1)));
                int rects = Pdfium.FPDFLink_CountRects(wl, i);
                for (int r = 0; r < rects; r++)
                {
                    double l, t, rr, b;
                    if (Pdfium.FPDFLink_GetRect(wl, i, r, &l, &t, &rr, &b) == 0) continue;
                    links.Add(new PageLink(PdfRectToPage(e, l, b, rr, t), LinkKind.Uri, -1, float.NaN, float.NaN, url));
                }
            }
        }
        finally
        {
            Pdfium.FPDFLink_CloseWebLinks(wl);
        }
    }

    private PageAnnotation[] ReadAnnotations(PageEntry e)
    {
        int count = Pdfium.FPDFPage_GetAnnotCount(e.Page);
        if (count <= 0) return [];
        var list = new List<PageAnnotation>();
        for (int i = 0; i < Math.Min(count, 5000); i++)
        {
            nint a = Pdfium.FPDFPage_GetAnnot(e.Page, i);
            if (a == 0) continue;
            try
            {
                var kind = KindOf(Pdfium.FPDFAnnot_GetSubtype(a));
                if (kind == AnnotationKind.Otra) continue;
                FsRectF rc;
                if (Pdfium.FPDFAnnot_GetRect(a, &rc) == 0) continue;
                uint argb = ReadAnnotColor(a, kind);
                list.Add(new PageAnnotation(i, kind, PdfRectToPage(e, rc.Left, rc.Bottom, rc.Right, rc.Top),
                    GetAnnotString(a, "Contents"), GetAnnotString(a, "T"), argb));
            }
            finally
            {
                Pdfium.FPDFPage_CloseAnnot(a);
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// Color de una anotación. PDFium solo lo da por <c>FPDFAnnot_GetColor</c> mientras la anotación no tiene aún su
    /// apariencia; cuando ya la tiene (tras dibujarla o al reabrir un archivo guardado) hay que leerlo de los
    /// objetos dibujados: relleno en resaltados y notas, trazo en el resto.
    /// </summary>
    private static uint ReadAnnotColor(nint annot, AnnotationKind kind)
    {
        uint r, g, b, al;
        if (Pdfium.FPDFAnnot_GetColor(annot, 0, &r, &g, &b, &al) != 0) return (al << 24) | (r << 16) | (g << 8) | b;
        bool fillFirst = kind is AnnotationKind.Resaltado or AnnotationKind.Nota or AnnotationKind.Sello;
        int n = Math.Min(Pdfium.FPDFAnnot_GetObjectCount(annot), 64);
        for (int pass = 0; pass < 2; pass++)
        {
            bool fill = fillFirst ? pass == 0 : pass == 1;
            for (int i = 0; i < n; i++)
            {
                nint obj = Pdfium.FPDFAnnot_GetObject(annot, i);
                if (obj == 0) continue;
                int ok = fill ? Pdfium.FPDFPageObj_GetFillColor(obj, &r, &g, &b, &al) : Pdfium.FPDFPageObj_GetStrokeColor(obj, &r, &g, &b, &al);
                if (ok != 0 && al > 0) return 0xFF000000 | (r << 16) | (g << 8) | b;
            }
        }

        return 0xFFFFEB3B;
    }

    private static AnnotationKind KindOf(int subtype) => subtype switch
    {
        Pdfium.AnnotText => AnnotationKind.Nota,
        Pdfium.AnnotFreeText => AnnotationKind.TextoLibre,
        Pdfium.AnnotSquare => AnnotationKind.Cuadrado,
        Pdfium.AnnotCircle => AnnotationKind.Circulo,
        Pdfium.AnnotHighlight => AnnotationKind.Resaltado,
        Pdfium.AnnotUnderline => AnnotationKind.Subrayado,
        Pdfium.AnnotSquiggly => AnnotationKind.Ondulado,
        Pdfium.AnnotStrikeout => AnnotationKind.Tachado,
        Pdfium.AnnotStamp => AnnotationKind.Sello,
        Pdfium.AnnotInk => AnnotationKind.Tinta,
        _ => AnnotationKind.Otra,
    };

    private static string GetAnnotString(nint annot, string key)
    {
        Span<byte> k = stackalloc byte[key.Length + 1];
        for (int i = 0; i < key.Length; i++) k[i] = (byte)key[i];
        k[key.Length] = 0;
        fixed (byte* kp = k)
        {
            byte* keyPtr = kp;
            return ReadWide((buf, len) => Pdfium.FPDFAnnot_GetStringValue(annot, keyPtr, buf, len));
        }
    }

    /// <summary>Solo las anotaciones de una página (más ligero que pedir todo el contenido).</summary>
    public Task<PageAnnotation[]> GetPageAnnotationsAsync(int page, PdfPriority priority = PdfPriority.Fondo, CancellationToken ct = default) =>
        Job(priority, () => ReadAnnotations(GetPage(page)), ct, 1000);

    // ------------------------------------------------------------------ cadenas de PDFium

    private static string ReadWide(BufReader read)
    {
        uint need = read(null, 0);
        if (need <= 2) return "";
        var buf = new byte[need];
        fixed (byte* p = buf) read(p, need);
        return Encoding.Unicode.GetString(buf, 0, (int)need - 2);
    }

    private static string ReadAscii(BufReader read)
    {
        uint need = read(null, 0);
        if (need <= 1) return "";
        var buf = new byte[need];
        fixed (byte* p = buf) read(p, need);
        return Encoding.ASCII.GetString(buf, 0, (int)need - 1);
    }

    // ------------------------------------------------------------------ índice (marcadores)

    public Task<IReadOnlyList<OutlineNode>> GetOutlineAsync(CancellationToken ct = default) =>
        Job(PdfPriority.Alta, () =>
        {
            ThrowIfClosed();
            int budget = 20000;
            return BuildOutline(0, 0, ref budget);
        }, ct);

    private IReadOnlyList<OutlineNode> BuildOutline(nint parent, int depth, ref int budget)
    {
        var list = new List<OutlineNode>();
        nint b = Pdfium.FPDFBookmark_GetFirstChild(_doc, parent);
        while (b != 0 && budget > 0)
        {
            budget--;
            nint bm = b;
            string title = ReadWide((buf, len) => Pdfium.FPDFBookmark_GetTitle(bm, buf, len));
            nint dest = Pdfium.FPDFBookmark_GetDest(_doc, b);
            if (dest == 0)
            {
                nint action = Pdfium.FPDFBookmark_GetAction(b);
                if (action != 0 && Pdfium.FPDFAction_GetType(action) == Pdfium.ActionGoto)
                    dest = Pdfium.FPDFAction_GetDest(_doc, action);
            }

            int page = dest != 0 ? Pdfium.FPDFDest_GetDestPageIndex(_doc, dest) : -1;
            var (x, y) = dest != 0 ? DestLocation(dest) : (float.NaN, float.NaN);
            int count = Pdfium.FPDFBookmark_GetCount(b);
            var children = depth < 16 ? BuildOutline(b, depth + 1, ref budget) : [];
            list.Add(new OutlineNode(title, page, x, y, count > 0, children));
            b = Pdfium.FPDFBookmark_GetNextSibling(_doc, b);
        }

        return list;
    }

    /// <summary>Convierte una posición en coordenadas PDF (como las del índice) a coordenadas de página.</summary>
    public Task<(float X, float Y)> PdfPointToPageAsync(int page, float x, float y, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            var e = GetPage(page);
            var (px, py) = e.ToPageSpace.Apply(float.IsNaN(x) ? 0 : x, float.IsNaN(y) ? 0 : y);
            return (float.IsNaN(x) ? float.NaN : (float)px, float.IsNaN(y) ? float.NaN : (float)py);
        }, ct);

    // ------------------------------------------------------------------ propiedades, adjuntos

    public Task<DocumentProperties> GetPropertiesAsync(CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            ThrowIfClosed();
            string Meta(string tag)
            {
                var t = Encoding.ASCII.GetBytes(tag + "\0");
                fixed (byte* tp = t)
                {
                    byte* tagPtr = tp;
                    return ReadWide((buf, len) => Pdfium.FPDF_GetMetaText(_doc, tagPtr, buf, len));
                }
            }

            int ver = 0;
            string version = Pdfium.FPDF_GetFileVersion(_doc, &ver) != 0 ? $"{ver / 10}.{ver % 10}" : "—";
            uint perm = Pdfium.FPDF_GetDocPermissions(_doc);
            FsSizeF size;
            var first = Pdfium.FPDF_GetPageSizeByIndexF(_doc, 0, &size) != 0 ? new PageSize(size.Width, size.Height) : new PageSize(0, 0);
            return new DocumentProperties(
                System.IO.Path.GetFileName(Path), Path, FileSize, PageCount, version,
                Meta("Title"), Meta("Author"), Meta("Subject"), Meta("Keywords"), Meta("Creator"), Meta("Producer"),
                ParsePdfDate(Meta("CreationDate")), ParsePdfDate(Meta("ModDate")), first,
                Pdfium.FPDF_GetSecurityHandlerRevision(_doc) != -1,
                (perm & 4) != 0, (perm & 16) != 0, (perm & 8) != 0,
                Math.Max(0, Pdfium.FPDF_GetSignatureCount(_doc)));
        }, ct);

    internal static DateTime? ParsePdfDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("D:", StringComparison.Ordinal)) s = s[2..];
        var digits = new StringBuilder();
        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c)) break;
            digits.Append(c);
        }

        string d = digits.ToString().PadRight(14, '0');
        if (digits.Length < 4) return null;
        if (!int.TryParse(d[..4], out int y) || !int.TryParse(d.AsSpan(4, 2), out int mo) || !int.TryParse(d.AsSpan(6, 2), out int da) ||
            !int.TryParse(d.AsSpan(8, 2), out int h) || !int.TryParse(d.AsSpan(10, 2), out int mi) || !int.TryParse(d.AsSpan(12, 2), out int se))
            return null;
        if (digits.Length < 6) { mo = 1; da = 1; }
        else if (digits.Length < 8) da = 1;
        try
        {
            return new DateTime(y, Math.Clamp(mo, 1, 12), Math.Clamp(da, 1, 28 + (mo == 2 ? 0 : 3)), Math.Clamp(h, 0, 23), Math.Clamp(mi, 0, 59), Math.Clamp(se, 0, 59));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<AttachmentInfo>> GetAttachmentsAsync(CancellationToken ct = default) =>
        Job(PdfPriority.Alta, () =>
        {
            ThrowIfClosed();
            int n = Math.Max(0, Pdfium.FPDFDoc_GetAttachmentCount(_doc));
            var list = new List<AttachmentInfo>(n);
            for (int i = 0; i < n; i++)
            {
                nint a = Pdfium.FPDFDoc_GetAttachment(_doc, i);
                if (a == 0) continue;
                string name = ReadWide((buf, len) => Pdfium.FPDFAttachment_GetName(a, buf, len));
                list.Add(new AttachmentInfo(i, string.IsNullOrWhiteSpace(name) ? "adjunto-" + (i + 1) : name));
            }

            return (IReadOnlyList<AttachmentInfo>)list;
        }, ct);

    public Task SaveAttachmentAsync(int index, string destination, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            ThrowIfClosed();
            nint a = Pdfium.FPDFDoc_GetAttachment(_doc, index);
            if (a == 0) throw new PdfException("No se encuentra el adjunto.");
            uint size = 0;
            Pdfium.FPDFAttachment_GetFile(a, null, 0, &size);
            var data = new byte[size];
            if (size > 0)
            {
                fixed (byte* p = data)
                {
                    if (Pdfium.FPDFAttachment_GetFile(a, p, size, &size) == 0) throw new PdfException("No se pudo extraer el adjunto.");
                }
            }

            File.WriteAllBytes(destination, data);
        }, ct);

    // ------------------------------------------------------------------ búsqueda

    public Task<SearchHit[]> SearchPageAsync(int page, string query, bool matchCase, bool wholeWord,
        PdfPriority priority = PdfPriority.Fondo, CancellationToken ct = default, int sub = 0) =>
        Job(priority, () => SearchCore(page, query, matchCase, wholeWord), ct, sub);

    private SearchHit[] SearchCore(int page, string query, bool matchCase, bool wholeWord)
    {
        var e = GetPage(page);
        nint tp = GetText(e);
        if (tp == 0 || query.Length == 0) return [];
        var q = new ushort[query.Length + 1];
        for (int i = 0; i < query.Length; i++) q[i] = query[i];
        uint flags = (matchCase ? Pdfium.FindMatchCase : 0) | (wholeWord ? Pdfium.FindWholeWord : 0);
        var hits = new List<SearchHit>();
        nint h;
        fixed (ushort* qp = q) h = Pdfium.FPDFText_FindStart(tp, qp, flags, 0);
        if (h == 0) return [];
        try
        {
            while (Pdfium.FPDFText_FindNext(h) != 0 && hits.Count < 2000)
            {
                int start = Pdfium.FPDFText_GetSchResultIndex(h);
                int count = Pdfium.FPDFText_GetSchCount(h);
                int nr = Pdfium.FPDFText_CountRects(tp, start, count);
                var rects = new List<RectF>(Math.Max(nr, 1));
                for (int r = 0; r < nr; r++)
                {
                    double l, t, rr, b;
                    if (Pdfium.FPDFText_GetRect(tp, r, &l, &t, &rr, &b) != 0) rects.Add(PdfRectToPage(e, l, b, rr, t));
                }

                hits.Add(new SearchHit(page, start, count, rects.ToArray()));
            }
        }
        finally
        {
            Pdfium.FPDFText_FindClose(h);
        }

        return hits.ToArray();
    }

    // ------------------------------------------------------------------ anotaciones

    private static readonly byte[] KeyContents = Encoding.ASCII.GetBytes("Contents\0");
    private static readonly byte[] KeyAuthor = Encoding.ASCII.GetBytes("T\0");

    private static void SetAnnotString(nint annot, byte[] key, string value)
    {
        var w = new ushort[value.Length + 1];
        for (int i = 0; i < value.Length; i++) w[i] = value[i];
        fixed (byte* k = key)
        fixed (ushort* v = w)
            Pdfium.FPDFAnnot_SetStringValue(annot, k, v);
    }

    private static void SetColor(nint annot, uint argb)
    {
        Pdfium.FPDFAnnot_SetColor(annot, 0, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF, argb >> 24);
    }

    private static FsRectF ToFs(RectF pdf) => new() { Left = pdf.Left, Top = pdf.Bottom, Right = pdf.Right, Bottom = pdf.Top };

    /// <summary>Añade resaltado, subrayado, ondulado o tachado sobre rectángulos de texto (coordenadas de página).</summary>
    public Task AddMarkupAsync(int page, MarkupKind kind, IReadOnlyList<RectF> rects, uint argb, string author, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            var e = GetPage(page);
            if (rects.Count == 0) return;
            int subtype = kind switch
            {
                MarkupKind.Resaltado => Pdfium.AnnotHighlight,
                MarkupKind.Subrayado => Pdfium.AnnotUnderline,
                MarkupKind.Ondulado => Pdfium.AnnotSquiggly,
                _ => Pdfium.AnnotStrikeout,
            };
            nint a = Pdfium.FPDFPage_CreateAnnot(e.Page, subtype);
            if (a == 0) throw new PdfException("No se pudo crear la anotación.");
            try
            {
                RectF? union = null;
                foreach (var pageRect in rects)
                {
                    // De coordenadas de página a coordenadas PDF (origen abajo-izquierda). RectF queda
                    // normalizado: «Top» es la y menor (borde inferior) y «Bottom» la y mayor (borde superior).
                    var r = e.FromPageSpace.Apply(pageRect);
                    // Orden de los puntos que espera PDFium/Acrobat: arriba-izq., arriba-der., abajo-izq., abajo-der.
                    var q = new FsQuadPointsF
                    {
                        X1 = r.Left, Y1 = r.Bottom, X2 = r.Right, Y2 = r.Bottom,
                        X3 = r.Left, Y3 = r.Top, X4 = r.Right, Y4 = r.Top,
                    };
                    Pdfium.FPDFAnnot_AppendAttachmentPoints(a, &q);
                    union = union == null ? r : union.Value.Union(r);
                }

                var fs = ToFs(union!.Value);
                Pdfium.FPDFAnnot_SetRect(a, &fs);
                SetColor(a, argb);
                SetAnnotString(a, KeyAuthor, author);
                Pdfium.FPDFAnnot_SetFlags(a, 4);
            }
            finally
            {
                Pdfium.FPDFPage_CloseAnnot(a);
            }

            _modified = true;
        }, ct);

    /// <summary>Añade una nota (icono con texto) con la esquina superior izquierda en el punto dado.</summary>
    public Task AddNoteAsync(int page, float x, float y, string text, uint argb, string author, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            var e = GetPage(page);
            nint a = Pdfium.FPDFPage_CreateAnnot(e.Page, Pdfium.AnnotText);
            if (a == 0) throw new PdfException("No se pudo crear la nota.");
            try
            {
                var r = e.FromPageSpace.Apply(new RectF(x, y, x + 22, y + 22));
                var fs = ToFs(r);
                Pdfium.FPDFAnnot_SetRect(a, &fs);
                SetColor(a, argb);
                SetAnnotString(a, KeyContents, text);
                SetAnnotString(a, KeyAuthor, author);
                Pdfium.FPDFAnnot_SetFlags(a, 4);
            }
            finally
            {
                Pdfium.FPDFPage_CloseAnnot(a);
            }

            _modified = true;
        }, ct);

    /// <summary>Añade un trazo a mano alzada. Los puntos están en coordenadas de página.</summary>
    public Task AddInkAsync(int page, IReadOnlyList<(float X, float Y)> points, uint argb, float widthPt, string author, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            var e = GetPage(page);
            if (points.Count < 2) return;
            nint a = Pdfium.FPDFPage_CreateAnnot(e.Page, Pdfium.AnnotInk);
            if (a == 0) throw new PdfException("No se pudo crear el dibujo.");
            try
            {
                var pts = new FsPointF[points.Count];
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                for (int i = 0; i < pts.Length; i++)
                {
                    var (px, py) = e.FromPageSpace.Apply(points[i].X, points[i].Y);
                    pts[i] = new FsPointF { X = (float)px, Y = (float)py };
                    minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                    minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                }

                float pad = widthPt + 1;
                var rect = new FsRectF { Left = (float)minX - pad, Bottom = (float)minY - pad, Right = (float)maxX + pad, Top = (float)maxY + pad };
                Pdfium.FPDFAnnot_SetRect(a, &rect);
                SetColor(a, argb);
                Pdfium.FPDFAnnot_SetBorder(a, 0, 0, widthPt);
                fixed (FsPointF* pp = pts) Pdfium.FPDFAnnot_AddInkStroke(a, pp, (nuint)pts.Length);
                SetAnnotString(a, KeyAuthor, author);
                Pdfium.FPDFAnnot_SetFlags(a, 4);
            }
            finally
            {
                Pdfium.FPDFPage_CloseAnnot(a);
            }

            _modified = true;
        }, ct);

    public Task RemoveAnnotationAsync(int page, int annotIndex, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            var e = GetPage(page);
            if (Pdfium.FPDFPage_RemoveAnnot(e.Page, annotIndex) == 0) throw new PdfException("No se pudo eliminar la anotación.");
            _modified = true;
        }, ct);

    // ------------------------------------------------------------------ guardado

    /// <summary>
    /// Guarda una copia (con las anotaciones) en <paramref name="destination"/>. Primero intenta añadir los
    /// cambios al final del archivo original (conserva firmas); si no se puede, reescribe el documento.
    /// </summary>
    public Task SaveCopyAsync(string destination, CancellationToken ct = default) =>
        Job(PdfPriority.Interactivo, () =>
        {
            ThrowIfClosed();
            if (!TrySave(destination, Pdfium.SaveIncremental) && !TrySave(destination, Pdfium.SaveNoIncremental))
                throw new PdfException("No se pudo guardar el documento.");
        }, ct);

    private bool TrySave(string destination, uint flags)
    {
        using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        var writer = new SaveWriter(fs);
        var handle = GCHandle.Alloc(writer);
        var fw = (FpdfFileWrite*)NativeMemory.AllocZeroed((nuint)sizeof(FpdfFileWrite));
        try
        {
            fw->Version = 1;
            fw->WriteBlock = &WriteBlockCallback;
            fw->Context = GCHandle.ToIntPtr(handle);
            bool ok = Pdfium.FPDF_SaveAsCopy(_doc, fw, flags) != 0 && !writer.Failed;
            fs.Flush();
            return ok;
        }
        finally
        {
            NativeMemory.Free(fw);
            handle.Free();
        }
    }

    private sealed class SaveWriter(Stream stream)
    {
        public bool Failed;

        public bool Write(ReadOnlySpan<byte> data)
        {
            try
            {
                stream.Write(data);
                return true;
            }
            catch
            {
                Failed = true;
                return false;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WriteBlockCallback(FpdfFileWrite* self, void* data, uint size)
    {
        try
        {
            var w = (SaveWriter)GCHandle.FromIntPtr(self->Context).Target!;
            return w.Write(new ReadOnlySpan<byte>(data, (int)size)) ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Marca el documento como guardado (tras escribirlo en disco).</summary>
    public void MarkSaved() => _modified = false;
}
