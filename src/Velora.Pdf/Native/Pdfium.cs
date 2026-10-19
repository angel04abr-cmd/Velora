// SPDX-License-Identifier: GPL-3.0-or-later
// Enlaces (P/Invoke) con PDFium. Las firmas siguen las cabeceras oficiales de PDFium
// (fpdfview.h, fpdf_text.h, fpdf_doc.h, fpdf_annot.h, fpdf_save.h, fpdf_attachment.h).
// En Windows «unsigned long» mide 32 bits, por eso se usa uint.
using System.Runtime.InteropServices;

namespace Velora.Pdf.Native;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FpdfFileAccess
{
    public uint FileLen;
    public delegate* unmanaged[Cdecl]<void*, uint, byte*, uint, int> GetBlock;
    public void* Param;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FpdfFileWrite
{
    public int Version;
    public delegate* unmanaged[Cdecl]<FpdfFileWrite*, void*, uint, int> WriteBlock;
    // PDFium solo lee los dos campos anteriores; este se usa para recuperar nuestro estado.
    public nint Context;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FsRectF
{
    public float Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FsQuadPointsF
{
    public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FsPointF
{
    public float X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FsSizeF
{
    public float Width, Height;
}

internal static unsafe class Pdfium
{
    private const string Lib = "pdfium";

    // Banderas de dibujo
    public const int RenderAnnot = 0x01;
    public const int RenderLcdText = 0x02;
    public const int RenderLimitedImageCache = 0x200;
    public const int RenderPrinting = 0x800;

    public const int BitmapBgrx = 3, BitmapBgra = 4;

    // Errores
    public const uint ErrSuccess = 0, ErrFile = 2, ErrFormat = 3, ErrPassword = 4, ErrSecurity = 5;

    // Subtipos de anotación
    public const int AnnotText = 1, AnnotLink = 2, AnnotFreeText = 3, AnnotSquare = 5, AnnotCircle = 6,
        AnnotHighlight = 9, AnnotUnderline = 10, AnnotSquiggly = 11, AnnotStrikeout = 12,
        AnnotStamp = 13, AnnotInk = 15, AnnotPopup = 16, AnnotFileAttachment = 17;

    // Acciones
    public const uint ActionGoto = 1, ActionUri = 3;

    // Guardado
    public const uint SaveIncremental = 1, SaveNoIncremental = 2;

    // Búsqueda
    public const uint FindMatchCase = 1, FindWholeWord = 2;

    // ---- Biblioteca y documentos ----
    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern nint FPDF_LoadCustomDocument(FpdfFileAccess* access, byte* password);
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(nint doc);
    [DllImport(Lib)] public static extern uint FPDF_GetLastError();
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(nint doc);
    [DllImport(Lib)] public static extern int FPDF_GetFileVersion(nint doc, int* version);
    [DllImport(Lib)] public static extern uint FPDF_GetDocPermissions(nint doc);
    [DllImport(Lib)] public static extern int FPDF_GetSecurityHandlerRevision(nint doc);
    [DllImport(Lib)] public static extern int FPDF_GetPageSizeByIndexF(nint doc, int index, FsSizeF* size);
    [DllImport(Lib)] public static extern uint FPDF_GetMetaText(nint doc, byte* tag, void* buffer, uint len);
    [DllImport(Lib)] public static extern uint FPDF_GetPageLabel(nint doc, int index, void* buffer, uint len);

    // ---- Páginas y dibujo ----
    [DllImport(Lib)] public static extern nint FPDF_LoadPage(nint doc, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(nint page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(nint page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(nint page);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
    [DllImport(Lib)] public static extern int FPDF_PageToDevice(nint page, int startX, int startY, int sizeX, int sizeY, int rotate, double px, double py, int* dx, int* dy);
    [DllImport(Lib)] public static extern nint FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);
    [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(nint bitmap);

    // ---- Texto ----
    [DllImport(Lib)] public static extern nint FPDFText_LoadPage(nint page);
    [DllImport(Lib)] public static extern void FPDFText_ClosePage(nint textPage);
    [DllImport(Lib)] public static extern int FPDFText_CountChars(nint textPage);
    [DllImport(Lib)] public static extern uint FPDFText_GetUnicode(nint textPage, int index);
    [DllImport(Lib)] public static extern int FPDFText_GetCharBox(nint textPage, int index, double* left, double* right, double* bottom, double* top);
    [DllImport(Lib)] public static extern int FPDFText_CountRects(nint textPage, int start, int count);
    [DllImport(Lib)] public static extern int FPDFText_GetRect(nint textPage, int rectIndex, double* left, double* top, double* right, double* bottom);
    [DllImport(Lib)] public static extern nint FPDFText_FindStart(nint textPage, ushort* findWhat, uint flags, int startIndex);
    [DllImport(Lib)] public static extern int FPDFText_FindNext(nint handle);
    [DllImport(Lib)] public static extern int FPDFText_GetSchResultIndex(nint handle);
    [DllImport(Lib)] public static extern int FPDFText_GetSchCount(nint handle);
    [DllImport(Lib)] public static extern void FPDFText_FindClose(nint handle);
    [DllImport(Lib)] public static extern nint FPDFLink_LoadWebLinks(nint textPage);
    [DllImport(Lib)] public static extern int FPDFLink_CountWebLinks(nint linkPage);
    [DllImport(Lib)] public static extern int FPDFLink_GetURL(nint linkPage, int index, ushort* buffer, int len);
    [DllImport(Lib)] public static extern int FPDFLink_CountRects(nint linkPage, int index);
    [DllImport(Lib)] public static extern int FPDFLink_GetRect(nint linkPage, int linkIndex, int rectIndex, double* left, double* top, double* right, double* bottom);
    [DllImport(Lib)] public static extern void FPDFLink_CloseWebLinks(nint linkPage);

    // ---- Marcadores, enlaces y acciones ----
    [DllImport(Lib)] public static extern nint FPDFBookmark_GetFirstChild(nint doc, nint bookmark);
    [DllImport(Lib)] public static extern nint FPDFBookmark_GetNextSibling(nint doc, nint bookmark);
    [DllImport(Lib)] public static extern uint FPDFBookmark_GetTitle(nint bookmark, void* buffer, uint len);
    [DllImport(Lib)] public static extern int FPDFBookmark_GetCount(nint bookmark);
    [DllImport(Lib)] public static extern nint FPDFBookmark_GetDest(nint doc, nint bookmark);
    [DllImport(Lib)] public static extern nint FPDFBookmark_GetAction(nint bookmark);
    [DllImport(Lib)] public static extern uint FPDFAction_GetType(nint action);
    [DllImport(Lib)] public static extern nint FPDFAction_GetDest(nint doc, nint action);
    [DllImport(Lib)] public static extern uint FPDFAction_GetURIPath(nint doc, nint action, void* buffer, uint len);
    [DllImport(Lib)] public static extern int FPDFDest_GetDestPageIndex(nint doc, nint dest);
    [DllImport(Lib)] public static extern int FPDFDest_GetLocationInPage(nint dest, int* hasX, int* hasY, int* hasZoom, float* x, float* y, float* zoom);
    [DllImport(Lib)] public static extern int FPDFLink_Enumerate(nint page, int* startPos, nint* linkAnnot);
    [DllImport(Lib)] public static extern nint FPDFLink_GetDest(nint doc, nint link);
    [DllImport(Lib)] public static extern nint FPDFLink_GetAction(nint link);
    [DllImport(Lib)] public static extern int FPDFLink_GetAnnotRect(nint linkAnnot, FsRectF* rect);

    // ---- Anotaciones ----
    [DllImport(Lib)] public static extern nint FPDFPage_CreateAnnot(nint page, int subtype);
    [DllImport(Lib)] public static extern int FPDFPage_GetAnnotCount(nint page);
    [DllImport(Lib)] public static extern nint FPDFPage_GetAnnot(nint page, int index);
    [DllImport(Lib)] public static extern void FPDFPage_CloseAnnot(nint annot);
    [DllImport(Lib)] public static extern int FPDFPage_RemoveAnnot(nint page, int index);
    [DllImport(Lib)] public static extern int FPDFAnnot_GetSubtype(nint annot);
    [DllImport(Lib)] public static extern int FPDFAnnot_AddInkStroke(nint annot, FsPointF* points, nuint count);
    [DllImport(Lib)] public static extern int FPDFAnnot_SetColor(nint annot, int type, uint r, uint g, uint b, uint a);
    [DllImport(Lib)] public static extern int FPDFAnnot_GetColor(nint annot, int type, uint* r, uint* g, uint* b, uint* a);
    [DllImport(Lib)] public static extern int FPDFAnnot_AppendAttachmentPoints(nint annot, FsQuadPointsF* quad);
    [DllImport(Lib)] public static extern int FPDFAnnot_SetRect(nint annot, FsRectF* rect);
    [DllImport(Lib)] public static extern int FPDFAnnot_GetRect(nint annot, FsRectF* rect);
    [DllImport(Lib)] public static extern int FPDFAnnot_SetBorder(nint annot, float h, float v, float width);
    [DllImport(Lib)] public static extern int FPDFAnnot_SetStringValue(nint annot, byte* key, ushort* value);
    [DllImport(Lib)] public static extern uint FPDFAnnot_GetStringValue(nint annot, byte* key, void* buffer, uint len);
    [DllImport(Lib)] public static extern int FPDFAnnot_SetFlags(nint annot, int flags);
    [DllImport(Lib)] public static extern int FPDFAnnot_GetObjectCount(nint annot);
    [DllImport(Lib)] public static extern nint FPDFAnnot_GetObject(nint annot, int index);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetStrokeColor(nint obj, uint* r, uint* g, uint* b, uint* a);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetFillColor(nint obj, uint* r, uint* g, uint* b, uint* a);

    // ---- Adjuntos y firmas ----
    [DllImport(Lib)] public static extern int FPDFDoc_GetAttachmentCount(nint doc);
    [DllImport(Lib)] public static extern nint FPDFDoc_GetAttachment(nint doc, int index);
    [DllImport(Lib)] public static extern uint FPDFAttachment_GetName(nint attachment, void* buffer, uint len);
    [DllImport(Lib)] public static extern int FPDFAttachment_GetFile(nint attachment, void* buffer, uint len, uint* outLen);
    [DllImport(Lib)] public static extern int FPDF_GetSignatureCount(nint doc);

    // ---- Guardado ----
    [DllImport(Lib)] public static extern int FPDF_SaveAsCopy(nint doc, FpdfFileWrite* write, uint flags);
}
