# Avisos de terceros y créditos

Velora es software libre bajo la **Licencia Pública General de GNU, versión 3 o posterior (GPL-3.0-or-later)**.
El texto completo está en [`LICENSE`](LICENSE). Este archivo recoge de dónde viene lo que no es original
y las licencias de los componentes que se distribuyen junto con el programa.

## Papers (GNOME) — inspiración y terminología

- Proyecto: Papers, visor de documentos de GNOME. <https://gitlab.gnome.org/GNOME/papers>
  (espejo: <https://github.com/GNOME/papers>).
- Licencia de Papers: **GPL-2.0-or-later**. Copyright © Proyecto GNOME y sus colaboradores
  (entre ellos Carlos García Campos, Red Hat, Inc. y los autores de Evince, de donde Papers procede).
- **Qué se tomó:**
  - Las *ideas de arquitectura* que hacen rápido a Papers, descritas en `docs/ANALISIS-PAPERS.md`: dibujar solo lo
    que se ve, caché de páginas ya dibujadas, cola de tareas con prioridades que se cancelan al hacer scroll y
    trabajo pesado fuera del hilo de la interfaz (en Papers: `libview/pps-job-scheduler.c`,
    `libview/pps-pixbuf-cache.c`, `libview/pps-jobs.c`).
  - La *terminología en español* (nombres de menús, acciones y mensajes), consultada en la traducción de la
    comunidad de GNOME (`po/es.po`, último traductor: Daniel Mustieles y colaboradores).
- **Qué no se tomó:** el código fuente de Papers (C y Rust, GTK4/libadwaita) no se ha copiado ni portado;
  Velora está escrito desde cero en C# con WPF. Tampoco se usan el nombre «Papers», su logotipo ni sus iconos:
  el icono de Velora es original.
- Velora es un proyecto independiente y **no está afiliado ni respaldado por el Proyecto GNOME**.

Como Velora se publica con la misma familia de licencia (GPL), las obligaciones de esa licencia se cumplen:
se conserva este aviso de autoría, se entrega el código fuente y se indica que es una obra distinta.

## PDFium — motor de lectura y dibujo de PDF

- Proyecto: PDFium. <https://pdfium.googlesource.com/pdfium/>
- Licencia de PDFium: **BSD de 3 cláusulas**, Copyright © 2014 Los autores de PDFium
  (texto en [`LICENSES/PDFium/pdfium.txt`](LICENSES/PDFium/pdfium.txt)). Los paquetes de binarios la indican en sus
  metadatos como «Apache-2.0»; el texto que acompaña a los binarios de esta versión es el BSD indicado, y las dos
  licencias permiten el uso, la modificación y la redistribución con aviso de copyright.
- Binarios (`pdfium.dll`): proyecto *pdfium-binaries* de Benoît Blanchon, paquete NuGet
  `bblanchon.PDFium.Win32` (licencia MIT del empaquetado: [`LICENSES/pdfium-binaries-MIT.txt`](LICENSES/pdfium-binaries-MIT.txt)).
- La biblioteca incluye componentes de terceros con sus propias licencias, copiadas en
  [`LICENSES/PDFium/`](LICENSES/PDFium): Abseil (Apache-2.0), AGG, dragonbox, fast_float, FreeType, HarfBuzz,
  ICU, Little CMS (lcms), libjpeg-turbo, OpenJPEG, libpng, llvm-libc, simdutf y zlib. Una copia de la
  Apache-2.0 está también en [`LICENSES/Apache-2.0.txt`](LICENSES/Apache-2.0.txt).
- Compatibilidad: todas esas licencias son compatibles con la GPL versión 3. Las partes con Apache-2.0 **no** son
  compatibles con la GPL versión 2 sola; por eso Velora usa «GPL-3.0 o posterior», opción que Papers permite al estar
  publicado bajo «GPL-2.0 o posterior».

## .NET y WPF

- Plataforma: .NET 10 y Windows Presentation Foundation. Licencia **MIT**, © Microsoft y la .NET Foundation.
  <https://github.com/dotnet/runtime> · <https://github.com/dotnet/wpf>

## Iconos

- Los símbolos de los botones son glifos de la fuente *Segoe Fluent Icons* / *Segoe MDL2 Assets* que Windows trae
  instalada (no se distribuyen con el programa).
- El icono de la aplicación es un diseño original del autor del proyecto (`diseno/velora-icono-original.webp`); los tamaños para Windows se generan con `tools/make-icon.py`.

## Marcas

«GNOME» y «Papers» son marcas o nombres de sus respectivos titulares. «Adobe» y «PDF» se mencionan solo para
describir el formato de archivo.
