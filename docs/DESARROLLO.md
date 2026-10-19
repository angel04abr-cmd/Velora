# Guía de desarrollo

## Marca y apariencia

Colores: carmesí `#C8102E`, blanco suave `#F7F7F9`, grafito `#454650`. `Services/ThemeHelper.ApplyBrand()` sustituye el acento de Windows por el carmesí
y, en el tema claro, las superficies y el texto, sobrescribiendo recursos del tema Fluent de WPF (`AccentFillColor*`, `ToggleButtonBackgroundChecked*`,
`SystemColors.Highlight*`, etc.); se repite al cambiar el tema de Windows. Para regenerar los iconos desde el diseño: `python tools\make-icon.py disenoelora-icono-original.webp`
(necesita `pip install pillow`).

## Requisitos

- Windows 10/11 x64, **.NET SDK 10** (`dotnet --version` ≥ 10.0). PowerShell 5.1 o superior.
- Se descargan solos al compilar (NuGet): `bblanchon.PDFium.Win32` (el motor), xUnit (pruebas).

## Proyectos

| Carpeta | Contenido |
|---|---|
| `src/Velora.Pdf` | Motor, sin dependencias de WPF: enlaces con PDFium (`Native/Pdfium.cs`), el hilo único de PDFium con cola de prioridades (`PdfWorker.cs`), el documento (`PdfDocument.cs`) y los cálculos del visor (`Viewer/`: disposición de páginas, baldosas, caché, texto, giro) |
| `src/Velora.Updates` | Actualizaciones sin WPF: consulta de GitHub, descarga con comprobación SHA-256 y script de instalación (probado con un servidor falso) |
| `installer/` | `Velora.wxs` (instalador MSI con WiX 6) y `Licencia.rtf` |
| `src/Velora.App` | La aplicación WPF: `Viewer/PdfView.cs` (el visor), `MainWindow*.cs/xaml` (ventana, barra lateral, búsqueda, anotaciones), `Dialogs/`, `Services/` |
| `tests/Velora.Tests` | Pruebas: motor contra PDF reales generados al vuelo, cálculos del visor, caché, texto |
| `tools/` | `PdfBuilder.cs` (genera PDF de prueba), `GenPdf` (crea PDF de demostración y pesados), `Probar.ps1` (ejecuta la app real con órdenes automáticas), `make-icon.py`, `make-licencia-rtf.py` (texto de licencia del instalador) |
| `referencia-papers/` | Copia de consulta del código de Papers (no forma parte de Velora; está fuera del control de versiones) |

## Cómo funciona por dentro

1. **Un solo hilo para PDFium.** PDFium no es seguro entre hilos. `PdfWorker` ejecuta todas las llamadas en un hilo dedicado con una
   cola de prioridades: `Interactivo` (abrir, guardar, anotar) > `Urgente` (lo visible) > `Alta` (miniaturas) > `Baja` (páginas cercanas) > `Fondo`
   (búsqueda). A igual prioridad gana lo más cercano al centro de la vista. Las tareas llevan un `CancellationToken`: al hacer scroll rápido
   las que ya no se ven se descartan sin ejecutarse.
2. **Baldosas.** `PdfView` reparte cada página en baldosas (una sola si cabe en 2048 px; si no, trozos de 1024 px) y solo pide las visibles.
   `RenderTileAsync` dibuja con `FPDF_RenderPageBitmap` solo ese trozo.
3. **Caché.** `ByteLruCache` guarda las baldosas con un presupuesto de memoria (64–128 MB según el equipo, `VELORA_CACHE_MB` lo cambia). Se reutilizan los
   `WriteableBitmap` de las baldosas expulsadas (WPF no libera la memoria de un `BitmapSource` hasta que el recolector de basura lo decide).
   Al hacer zoom se enseñan las baldosas anteriores, escaladas, hasta que llegan las nuevas.
4. **Texto, enlaces y anotaciones.** `GetPageContentAsync` devuelve de una vez el texto con la caja de cada carácter, los enlaces y las
   anotaciones de una página en *coordenadas de página* (puntos, origen arriba-izquierda). La transformación desde las coordenadas internas del
   PDF (recuadro de recorte, rotación propia) se obtiene de `FPDF_PageToDevice`. Así, pasar el ratón por el texto no pide nada al motor.
5. **Memoria de PDFium.** PDFium conserva lo leído hasta cerrar el documento. `PdfDocument` cuenta las páginas tocadas y, al superar ~256 MB estimados
   y sin cambios sin guardar, cierra y vuelve a abrir el documento (`RecycleBytes`).
6. **Guardado.** `FPDF_SaveAsCopy` (primero incremental, que conserva firmas; si falla, completo) escribe un temporal; después se cierra el documento,
   se reemplaza el original y se reabre en el mismo punto.
7. **Abrir.** `FPDF_LoadCustomDocument` con lectura por bloques (`RandomAccess.Read`): no se carga el archivo entero. Límite de 4 GB (el motor usa `unsigned long`).

## Pruebas

```powershell
dotnet test tests\Velora.Tests
```

Prueban el motor con PDF que genera `tools/PdfBuilder.cs` (texto, enlaces, índice, imágenes, páginas giradas, anotaciones guardadas y reabiertas,
prioridades y cancelación de tareas, reciclado de memoria) y los cálculos del visor.

### Probar la aplicación real

```powershell
dotnet run --project tools\GenPdf -c Release -- pruebas        # crea demo.pdf, 2000-paginas.pdf y pesado-imagenes.pdf (~550 MB)
.\tools\Probar.ps1 -Pdf pruebas\demo.pdf -Limpiar -Auto "listo;buscar:murcielago;captura:pruebas\salida\a.png;salir"
```

`--auto` ejecuta órdenes separadas por `;` (ver `Services/Automation.cs`): `zoom:150`, `pagina:5`, `girar:1`, `columnas:2`, `buscar:texto`,
`seleccionar:texto`, `resaltar`, `nota:texto`, `tinta`, `guardar`, `enlace:interno`, `clic:`/`doble:`/`triple:`/`arrastrar:` (ratón simulado),
`captura:ruta.png`, `dialogo:propiedades|acerca|contrasena`, `presentacion:ruta.png`, `imprimir-xps:ruta`, `medir-saltos:N`,
`medir-desplazamiento:N`, `info`, `gc`… El resultado va a `pruebas\salida\log.txt`. Con `--silencioso` los avisos no abren ventanas.
Otras opciones: `--tema claro|oscuro`, `--tam 1200x800`, `--datos carpeta`, `--clave contraseña`, `--mantener`.

### Cifras de referencia (equipo de desarrollo)

| Prueba | Resultado |
|---|---|
| Abrir un PDF de 550 MB (80 páginas con imágenes de 1500×1500) | ~170 ms hasta tenerlo abierto, primera página dibujada en ~250 ms |
| Saltos a páginas al azar en ese PDF (cada página con una imagen grande y 800 trazados) | mediana ≈ 150–190 ms, máximo ≈ 450 ms hasta ver la página completa |
| PDF de 2000 páginas, saltos al azar | mediana ≈ 80 ms |
| Desplazamiento continuo mientras se dibuja | 16,6 ms entre cuadros (60 fps), el peor cuadro ≈ 20–25 ms: la ventana no se atasca |
| Memoria con un PDF de 2000 páginas tras muchos saltos | ≈ 500–800 MB según el tamaño de la caché (la caché cuesta ≈ 2,7× lo que mide en WPF) |

## Cabecera y menús

La ventana usa `WindowChrome` (barra de título propia de 44 px). `HeaderBar` contiene el icono, el menú, las herramientas y los botones de la ventana; las zonas que
deben recibir clics llevan `WindowChrome.IsHitTestVisibleInChrome`, y el resto arrastra la ventana. El botón de maximizar devuelve `HTMAXBUTTON` en `WM_NCHITTEST`
para que Windows 11 muestre sus disposiciones. Al maximizar se compensa el desborde de la ventana con un margen calculado a partir del área de trabajo del monitor.
Los elementos del menú se crean desde código (`MainWindow.Menus.cs`) porque WPF no deja pasar un `MenuItem` de un menú a otro: así se puede recoger el menú en un botón ☰.
`AdaptToolbar` calcula los anchos de cada grupo y va ocultando lo menos importante hasta que todo cabe. Comprobación: `Probar.ps1 … -Auto "zonas;maximizar"` pregunta
a Windows qué hay bajo cada elemento (clic, arrastre o botón de maximizar).

## Instalador (MSI)

`installer/Velora.wxs` (WiX 6.0.2, la última versión sin cuota OSMF; `build.ps1 -Instalador` la instala en `.tools\` la primera vez) crea `Velora-x.y.z-x64.msi`:
instalación **para todo el equipo** en `C:\Program Files\Velora` (pide administrador), acceso en el menú Inicio apuntando al `.exe` (así Windows ofrece «Desinstalar»),
entrada en «Aplicaciones instaladas», registro como programa para `.pdf` (aparece en «Abrir con» y en Aplicaciones predeterminadas sin quitarle el predeterminado a nadie),
acceso opcional en el escritorio, comprobación de que está el .NET 10 Desktop Runtime y actualización de versiones anteriores (`MajorUpgrade`). Marca en el registro
`HKLM\Software\Velora` (`Installer=msi`, `InstallDir`) para que la aplicación sepa que puede actualizarse sola. Las versiones 1.0.0 a 1.0.2 se instalaban solo para el usuario
con `Instalar.ps1`; el MSI avisa y pide desinstalarlas antes. `.github/workflows/probar-instalador.yml` instala el MSI en un Windows limpio, comprueba archivos, acceso,
registro y «Aplicaciones instaladas», arranca el programa, lo desinstala comprobando que no queda nada y prueba la actualización desde una versión anterior.

## Actualizaciones por dentro

`Velora.Updates/UpdateClient.cs` consulta `https://api.github.com/repos/angel04abr-cmd/Velora/releases/latest`, compara la etiqueta (`vX.Y.Z`) con la versión instalada
(nunca baja de versión; ignora borradores y versiones de prueba), elige el asset `Velora-X.Y.Z-x64.msi` y exige el `.sha256` que lo acompaña. Descarga solo desde
`https://github.com/angel04abr-cmd/Velora/releases/download/…`, verifica la huella (si no coincide, borra la descarga y no instala) y `UpdateService` (en la aplicación)
escribe y lanza oculto un `.cmd` que espera a que Velora se cierre, ejecuta `msiexec /i … /passive /norestart` (Windows pide permiso de administrador) y vuelve a abrir
Velora con el mismo documento (con el aviso «se actualizó» solo si la instalación terminó bien). `CanInstallInPlace` exige la marca del MSI en el registro; una copia portátil
abre la página de descargas. Variable para pruebas: `VELORA_FAKE_VERSION` (simula otra versión instalada).
**Límite:** la huella protege contra descargas dañadas o cambiadas por el camino, pero no contra alguien que controle la cuenta de GitHub (no hay firma de código).

## Publicar una versión

1. Sube `<Version>` en los `.csproj` de `src/` y añade la sección `## [x.y.z]` a `CHANGELOG.md` (sirve de notas de la versión).
2. Haz commit de todo y ejecuta `.\build.ps1 -Publicar`: pasa las pruebas, empaqueta, crea la etiqueta `vx.y.z`, la sube y crea la Release con el `.msi`, el `.zip` y sus `.sha256`.
3. Las instalaciones de Velora la detectan solas (como mucho al día siguiente, o al momento con *Ayuda → Buscar actualizaciones…*).

La integración continua (`.github/workflows/compilar.yml`) compila y ejecuta las pruebas en cada `push`.

## Actualizar

- **Una versión nueva de Velora:** sube `<Version>` en `src/Velora.Pdf/Velora.Pdf.csproj` y `src/Velora.App/Velora.App.csproj`,
  ejecuta `.\build.ps1 -Probar -Instalar` (o `-Publicar` para que las instalaciones se actualicen solas).
- **Una versión nueva de PDFium:** cambia la versión de `bblanchon.PDFium.Win32` en `Velora.Pdf.csproj`
  (<https://www.nuget.org/packages/bblanchon.PDFium.Win32>), ejecuta las pruebas y, si el paquete trae licencias nuevas, actualiza `LICENSES/`.
- **Seguir a Papers:** `git -C referencia-papers pull`; compara `po/es.po` (terminología) y los cambios de `libview/` si quieres reutilizar ideas.
  Recuerda que copiar código de Papers (GPL-2.0-or-later) exige mantener sus avisos de autoría; este proyecto no copia su código.

## Ideas para más adelante

Formularios (`FPDF_FFLDraw`), firmas digitales (ahora solo se cuenta cuántas hay), DjVu/CBR/TIFF (Papers los admite), OCR, pestañas,
recarga automática cuando el archivo cambia en disco, panel de fuentes en las propiedades, reflujo del texto.

## Límites conocidos

- Un PDF malformado puede hacer fallar al propio PDFium (código nativo) y cerrar el programa; es el mismo motor que usa Chrome y está muy probado,
  pero no hay aislamiento en otro proceso.
- La impresión se ejecuta en el hilo de la ventana: con documentos de cientos de páginas la ventana queda ocupada mientras se envían.
- Los archivos de más de 4 GB no se pueden abrir.
