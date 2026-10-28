// SPDX-License-Identifier: GPL-3.0-or-later
namespace Velora.Pdf;

/// <summary>Rectángulo en puntos (1/72 de pulgada). Origen arriba a la izquierda salvo que se indique otra cosa.</summary>
public readonly record struct RectF(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(float x, float y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    public RectF Inflate(float d) => new(Left - d, Top - d, Right + d, Bottom + d);

    public RectF Union(RectF o) => new(Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));

    /// <summary>Distancia al cuadrado desde el punto al rectángulo (0 si está dentro).</summary>
    public float DistanceSquared(float x, float y)
    {
        float dx = x < Left ? Left - x : x > Right ? x - Right : 0;
        float dy = y < Top ? Top - y : y > Bottom ? y - Bottom : 0;
        return dx * dx + dy * dy;
    }
}

/// <summary>Transformación afín: x' = A·x + C·y + E ; y' = B·x + D·y + F.</summary>
public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    public Affine Invert()
    {
        double det = A * D - B * C;
        if (Math.Abs(det) < 1e-12) return Identity;
        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        double ie = -(ia * E + ic * F), jf = -(ib * E + id * F);
        return new Affine(ia, ib, ic, id, ie, jf);
    }

    /// <summary>Transforma un rectángulo y devuelve su caja envolvente.</summary>
    public RectF Apply(RectF r)
    {
        var (x1, y1) = Apply(r.Left, r.Top);
        var (x2, y2) = Apply(r.Right, r.Bottom);
        var (x3, y3) = Apply(r.Left, r.Bottom);
        var (x4, y4) = Apply(r.Right, r.Top);
        return new RectF(
            (float)Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
            (float)Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
            (float)Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
            (float)Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }
}

public readonly record struct PageSize(float Width, float Height);

/// <summary>Petición de un trozo («baldosa») de una página ya dibujada a un tamaño en píxeles.</summary>
public readonly record struct TileRequest(
    int Page,
    int PagePxWidth,
    int PagePxHeight,
    int Rotation,
    int X,
    int Y,
    int Width,
    int Height,
    bool Annotations = true,
    bool Printing = false)
{
    public long ByteSize => (long)Width * Height * 4;
}

/// <summary>
/// Reserva de memoria para los píxeles de las baldosas. El reparto compartido de .NET llega a guardar cientos de megas de buffers grandes
/// al desplazarse muy rápido; este es propio y limitado (pocos buffers por tamaño, ninguno por encima de 32 MB).
/// </summary>
internal static class PixelPool
{
    public static readonly System.Buffers.ArrayPool<byte> Instance = System.Buffers.ArrayPool<byte>.Create(32 << 20, 3);
}

/// <summary>Píxeles BGRA (opacos, ya con el fondo blanco) de una baldosa. Devolver con Dispose para reutilizar la memoria.</summary>
public sealed class RenderedTile : IDisposable
{
    private byte[]? _pixels;

    internal RenderedTile(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;
    public byte[] Pixels => _pixels ?? throw new ObjectDisposedException(nameof(RenderedTile));

    public void Dispose()
    {
        var p = Interlocked.Exchange(ref _pixels, null);
        if (p != null) PixelPool.Instance.Return(p);
    }
}

public enum LinkKind { Goto, Uri }

public sealed record PageLink(RectF Rect, LinkKind Kind, int TargetPage, float TargetX, float TargetY, string? Uri);

public enum AnnotationKind
{
    Otra, Nota, TextoLibre, Cuadrado, Circulo, Resaltado, Subrayado, Ondulado, Tachado, Sello, Tinta
}

public sealed record PageAnnotation(int Index, AnnotationKind Kind, RectF Rect, string Contents, string Author, uint ColorArgb);

/// <summary>Texto, cajas de cada carácter, enlaces y anotaciones de una página (coordenadas de página, arriba-izquierda, en puntos).</summary>
public sealed class PageContent
{
    public PageContent(int page, float width, float height, int[] chars, float[] boxes, PageLink[] links, PageAnnotation[] annotations)
    {
        Page = page;
        Width = width;
        Height = height;
        Chars = chars;
        Boxes = boxes;
        Links = links;
        Annotations = annotations;
    }

    public int Page { get; }
    public float Width { get; }
    public float Height { get; }
    /// <summary>Un punto de código Unicode por carácter de PDFium (puede incluir saltos de línea generados).</summary>
    public int[] Chars { get; }
    /// <summary>Cuatro valores por carácter: izquierda, arriba, derecha, abajo.</summary>
    public float[] Boxes { get; }
    public PageLink[] Links { get; }
    public PageAnnotation[] Annotations { get; }

    public int CharCount => Chars.Length;

    public RectF BoxOf(int i) => new(Boxes[4 * i], Boxes[4 * i + 1], Boxes[4 * i + 2], Boxes[4 * i + 3]);

    public bool HasBox(int i) => Boxes[4 * i + 2] > Boxes[4 * i] && Boxes[4 * i + 3] > Boxes[4 * i + 1];
}

public sealed record SearchHit(int Page, int Start, int Count, RectF[] Rects);

public sealed record OutlineNode(string Title, int Page, float X, float Y, bool Expanded, IReadOnlyList<OutlineNode> Children);

public sealed record AttachmentInfo(int Index, string Name);

public sealed record DocumentProperties(
    string FileName,
    string FullPath,
    long FileSize,
    int PageCount,
    string PdfVersion,
    string Title,
    string Author,
    string Subject,
    string Keywords,
    string Creator,
    string Producer,
    DateTime? Created,
    DateTime? Modified,
    PageSize FirstPageSize,
    bool Protected,
    bool CanPrint,
    bool CanCopy,
    bool CanModify,
    int Signatures);

public enum MarkupKind { Resaltado, Subrayado, Ondulado, Tachado }

public class PdfException : Exception
{
    public PdfException(string message) : base(message) { }
    public PdfException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>El documento está cifrado y hace falta (o es incorrecta) la contraseña.</summary>
public sealed class PdfPasswordException : PdfException
{
    public PdfPasswordException(bool wrong) : base(wrong ? "La contraseña no es correcta." : "Este documento está protegido con contraseña.")
    {
        Wrong = wrong;
    }

    public bool Wrong { get; }
}
