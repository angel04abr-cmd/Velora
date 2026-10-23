// SPDX-License-Identifier: GPL-3.0-or-later
using Velora.Pdf;

namespace Velora.Tests;

/// <summary>Guardado de anotaciones sobre el propio archivo: lo que hace la aplicación al guardar o al cerrar la ventana.</summary>
public class SaveTests
{
    private static async Task<int> CountAsync(string path)
    {
        using var d = await PdfDocument.OpenAsync(path);
        var c = await d.GetPageContentAsync(0, PdfPriority.Urgente);
        return c.Annotations.Count(a => a.Kind is AnnotationKind.Nota or AnnotationKind.Tinta or AnnotationKind.Resaltado);
    }

    [Fact]
    public async Task Cerrando_el_documento_se_puede_reemplazar_el_original()
    {
        // Al cerrar la ventana con «Guardar»: PDFium mantiene el archivo abierto y Windows no deja reemplazarlo hasta cerrar el documento.
        string path = Files.Simple("reemplazo.pdf");
        long originalSize = new FileInfo(path).Length;
        using var doc = await PdfDocument.OpenAsync(path);
        await doc.AddNoteAsync(0, 400, 200, "Nota", 0xFFFFC107, "Prueba");
        string tmp = path + ".velora-tmp";
        await doc.SaveCopyAsync(tmp);
        await doc.CloseAsync();
        File.Move(tmp, path, overwrite: true);
        doc.MarkSaved();

        Assert.False(doc.IsModified);
        Assert.True(new FileInfo(path).Length > originalSize);
        Assert.False(File.Exists(tmp));
        Assert.Equal(1, await CountAsync(path));
    }

    [Fact]
    public async Task Guardar_varias_veces_sobre_el_mismo_archivo_no_duplica_ni_pierde_anotaciones()
    {
        string path = Files.Simple("varias.pdf");
        for (int round = 1; round <= 3; round++)
        {
            using (var doc = await PdfDocument.OpenAsync(path))
            {
                await doc.AddNoteAsync(0, 100 + round * 40, 200, "Nota " + round, 0xFFFFC107, "Prueba");
                await doc.AddInkAsync(0, [(300, 400), (320, 420), (350, 410)], 0xFFE53935, 2f, "Prueba");
                string tmp = path + ".velora-tmp";
                await doc.SaveCopyAsync(tmp);
                await doc.CloseAsync();
                File.Move(tmp, path, overwrite: true);
            }

            Assert.Equal(round * 2, await CountAsync(path));
        }
    }

    [Fact]
    public async Task Un_original_de_solo_lectura_no_se_pierde_y_el_guardado_falla_de_forma_controlada()
    {
        string path = Files.Simple("solo-lectura.pdf");
        byte[] before = File.ReadAllBytes(path);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using var doc = await PdfDocument.OpenAsync(path);
            await doc.AddNoteAsync(0, 400, 200, "Nota", 0xFFFFC107, "Prueba");
            string tmp = path + ".velora-tmp";
            await doc.SaveCopyAsync(tmp);
            Assert.ThrowsAny<Exception>(() => File.Move(tmp, path, overwrite: true));
            // el original queda intacto y la copia temporal existe para que la aplicación la guarde con otro nombre
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(File.Exists(tmp));
            Assert.Equal(1, await CountAsync(tmp));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
