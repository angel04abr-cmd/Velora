// SPDX-License-Identifier: GPL-3.0-or-later
// Textos de la interfaz que se construyen en el código. Todo el programa está en español.
// La terminología sigue la traducción al español de Papers/GNOME (po/es.po) cuando existe equivalente.
using Velora.Pdf;

namespace Velora.App;

internal static class T
{
    public const string AppName = "Velora";
    public const string Subtitle = "Visor de PDF rápido y ligero";

    public const string FileFilter = "Documentos PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*";
    public const string OpenTitle = "Abrir un documento";
    public const string SaveCopyTitle = "Guardar una copia";

    public const string Opening = "Abriendo «{0}»…";
    public const string PageOf = "de {0}";

    public const string Searching = "Buscando… {0} de {1} páginas";
    public const string NoResults = "Sin resultados";
    public const string ResultCount = "{0} de {1}";

    public const string Saved = "Cambios guardados.";
    public const string SavedCopy = "Copia guardada en «{0}».";
    public const string SavingCopy = "Guardando la copia «{0}»…";
    public const string CopiedText = "Texto copiado.";
    public const string UnsavedTitle = "Cambios sin guardar";
    public const string UnsavedAsk = "«{0}» tiene anotaciones sin guardar.\n\n¿Quieres guardarlas antes de continuar?";
    public const string SaveFailed = "No se pudo guardar el documento.";
    public const string ReplaceFailed = "No se pudo reemplazar el archivo original (puede estar protegido o en uso). Se guardó una copia con tus cambios en:\n{0}";
    public const string OpenFailed = "No se pudo abrir el documento.";
    public const string UnexpectedError = "Ha ocurrido un error inesperado, pero el programa sigue abierto.";
    public const string PasswordNeeded = "El documento «{0}» está protegido. Escribe la contraseña para abrirlo.";
    public const string PasswordWrong = "La contraseña no es correcta. Inténtalo de nuevo.";
    public const string LinkExternal = "Este documento quiere abrir un enlace externo:\n\n{0}\n\n¿Quieres abrirlo en el navegador?";
    public const string LinkBlocked = "Solo se pueden abrir enlaces web (http, https) y de correo (mailto).";
    public const string PrintDone = "Se envió «{0}» a la impresora.";
    public const string PrintFailed = "No se pudo imprimir.";
    public const string AttachmentSaved = "Adjunto guardado en «{0}».";
    public const string AttachmentFilter = "Todos los archivos (*.*)|*.*";

    public const string RecentEmpty = "Aún no has abierto ningún documento.";
    public const string RecentPage = "Página {0}";

    public const string OutlineEmpty = "Este documento no tiene índice.";
    public const string AnnotsEmpty = "Este documento no tiene anotaciones.";
    public const string AttachEmpty = "Este documento no tiene archivos adjuntos.";
    public const string AnnotsScanning = "Buscando anotaciones… {0} de {1} páginas";

    public const string ZoomFitWidth = "Ajustar al ancho";
    public const string ZoomFitPage = "Ajustar a la página";

    public static string AnnotKind(AnnotationKind k) => k switch
    {
        AnnotationKind.Nota => "Nota",
        AnnotationKind.TextoLibre => "Texto",
        AnnotationKind.Cuadrado => "Rectángulo",
        AnnotationKind.Circulo => "Círculo",
        AnnotationKind.Resaltado => "Resaltado",
        AnnotationKind.Subrayado => "Subrayado",
        AnnotationKind.Ondulado => "Subrayado ondulado",
        AnnotationKind.Tachado => "Tachado",
        AnnotationKind.Sello => "Sello",
        AnnotationKind.Tinta => "Dibujo",
        _ => "Anotación",
    };

    public static string FileSize(long bytes)
    {
        string[] u = ["bytes", "KB", "MB", "GB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes:N0} bytes" : $"{v:0.##} {u[i]}";
    }

    /// <summary>Tamaño de página en milímetros, con el nombre del formato si es conocido.</summary>
    public static string PageSizeText(PageSize s)
    {
        double wmm = s.Width / 72 * 25.4, hmm = s.Height / 72 * 25.4;
        string name = "";
        (string, double, double)[] known = [("A3", 297, 420), ("A4", 210, 297), ("A5", 148, 210), ("Carta", 215.9, 279.4), ("Legal", 215.9, 355.6), ("Tabloide", 279.4, 431.8)];
        foreach (var (n, a, b) in known)
        {
            if ((Math.Abs(wmm - a) < 2 && Math.Abs(hmm - b) < 2) || (Math.Abs(wmm - b) < 2 && Math.Abs(hmm - a) < 2))
            {
                name = " (" + n + ")";
                break;
            }
        }

        return $"{wmm:0} × {hmm:0} mm{name}";
    }
}
