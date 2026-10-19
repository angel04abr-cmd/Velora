# Análisis de Papers (GNOME)

Estudio del repositorio <https://github.com/GNOME/papers> (copia en `referencia-papers/`, versión 51.0) hecho para crear Velora.

## Licencia, en sencillo

Papers usa la **GPL versión 2 o posterior** (archivo `COPYING`; cada archivo lleva `SPDX-License-Identifier: GPL-2.0-or-later`).

- **Puedes**: usarlo, estudiarlo, modificarlo, regalarlo y venderlo (cobrando por el programa o por servicios).
- **Debes**, si lo distribuyes: usar la misma licencia GPL para tu versión, entregar o poner a disposición el código fuente, conservar los avisos de
  copyright y licencia, indicar que lo modificaste y no añadir restricciones que quiten esos mismos derechos a los demás.
- **No cubre** el nombre «Papers» ni su logotipo (identidad de GNOME): no deben usarse como si fueran tuyos.
- Si lo usas solo tú, no tienes que publicar nada.
- Como dice «o posterior», una obra derivada puede publicarse bajo GPL-3.0 o posterior (lo que hace Velora, para poder incluir PDFium).

Velora **no copia código de Papers**: reimplementa sus ideas. De Papers solo toma conceptos y la terminología en español (traducción de
la comunidad GNOME), por eso se acredita y se publica también bajo GPL.

## Qué es y de qué está hecho

Visor de documentos de GNOME para Linux: PDF, DjVu, cómics (CBR/CBZ) y TIFF. GTK4 + libadwaita.

| Parte | Carpeta | Lenguaje | Función |
|---|---|---|---|
| Interfaz | `shell/src` | Rust (~11 400 líneas) | Ventana, barras laterales, búsqueda, propiedades, firmas, impresión |
| Visor | `libview` | C | `pps-view.c` (7 700 líneas): páginas, scroll, zoom, selección, anotaciones, presentación |
| Documentos | `libdocument` | C | Interfaz común y comportamiento por formato |
| Motores | `libdocument/backend` | C | `pdf` (Poppler), `djvu`, `comics`, `tiff` |
| Miniaturas | `thumbnailer` | Rust | Previsualizaciones en el explorador de archivos |
| Traducciones | `po` | gettext | Más de 80 idiomas (incluido `es.po`) |

Se compila con Meson + Cargo o se instala por Flatpak; no hay versión de Windows (exigiría GTK4, libadwaita y Poppler con MSYS2).

## Por qué es rápido (y cómo se llevó a Velora)

| Idea de Papers | Dónde | En Velora |
|---|---|---|
| Motor de dibujo rápido (Poppler + Cairo) | `libdocument/backend/pdf/pps-poppler.c` | PDFium (`PdfDocument.RenderTileCore`) |
| Solo se dibuja lo visible y un par de páginas vecinas | `libview/pps-pixbuf-cache.c` | `PdfView.UpdateWork`: baldosas visibles + 1 antes y 2 después |
| Cola de tareas con prioridades: urgente (visible) > alta (miniaturas) > baja (vecinas) > ninguna | `libview/pps-job-scheduler.h` | `PdfWorker` y `PdfPriority` |
| Trabajo pesado fuera del hilo de la interfaz; se cancela lo que ya no se ve | `libview/pps-jobs.c` | Tareas con `CancellationToken`; los resultados vuelven a la interfaz con `Dispatcher` |
| Tamaño de todas las páginas conocido de antemano | `libview/pps-page-cache.c` | `GetPageSizesAsync` + `PageLayout` (búsqueda binaria) |
| Barra lateral, búsqueda, anotaciones, propiedades | `shell/src/*.rs` | `MainWindow.*.cs`, `Dialogs/` |

Archivos clave para estudiar: `libview/pps-view.c`, `libview/pps-pixbuf-cache.c`, `libview/pps-job-scheduler.c`, `libview/pps-jobs.c`,
`libdocument/backend/pdf/pps-poppler.c`, `shell/src/window.rs`, `shell/src/document_view.rs`.

## Decisiones para Velora

- **Windows nativo con C#/WPF** (misma tecnología que el resto de proyectos del autor) en lugar de portar GTK4/Rust a Windows.
- **PDFium** como motor: velocidad de Chrome, binarios listos para Windows y licencia permisiva (BSD). Poppler en Windows es muy difícil de compilar.
- **Español completo**: interfaz, mensajes y documentación; textos propios con la terminología de la traducción de GNOME.
- **Primera versión solo PDF**; el resto de formatos y las firmas quedan para más adelante.
