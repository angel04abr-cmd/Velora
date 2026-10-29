# Manual de Velora

## La ventana

Arriba hay una sola barra: el **icono**, el **menú** (Archivo, Ver, Anotar, Ir y Ayuda, con todos los atajos de teclado), las **herramientas**
(panel lateral, página anterior y siguiente, zoom, giro, dos páginas, buscar, notas y dibujo) y los botones de la ventana. Se arrastra desde cualquier hueco;
doble clic maximiza. Con la ventana estrecha el menú se recoge en un botón ☰ y se ocultan por orden los botones menos usados (todo sigue en el menú).

## Actualizaciones

Velora busca una versión nueva en GitHub cuando arranca (como mucho una vez al día) y abre una ventana con las novedades de la versión nueva y los botones **Actualizar ahora**, **Más tarde** y **Omitir esta versión** (la descarga se sigue en esa misma ventana).
Al aceptar, descarga el instalador (.msi), comprueba su huella SHA-256 y lo ejecuta: Windows pide permiso de administrador, Velora se cierra, se instala la versión nueva y se abre de nuevo con tu documento. *Ayuda → Buscar actualizaciones…* lo comprueba al momento;
*Ayuda → Buscar actualizaciones automáticamente* lo activa o desactiva. Solo se actualiza sola la copia instalada con el instalador (.msi); una copia portátil abre la página de descargas.

## Abrir documentos

- **Abrir…** (`Ctrl+O`), **arrastrar** un PDF a la ventana, o hacer doble clic en un PDF si Velora es tu lector predeterminado.
- En la pantalla de inicio aparecen los **documentos recientes**. Velora recuerda la página, el zoom y el giro de cada uno.
- Si el PDF tiene contraseña, Velora la pide. `Ctrl+N` abre otra ventana; `Ctrl+W` cierra el documento.

## Leer

| Acción | Cómo |
|---|---|
| Desplazarte | Rueda del ratón, barra de desplazamiento, flechas, `Re Pág` / `Av Pág`, `Espacio` / `Mayús+Espacio`. También puedes **arrastrar** la página con el ratón (en una zona sin texto) |
| Ir a una página | Flechas ↑ ↓ de la barra (página anterior / siguiente, también `Ctrl+Re Pág` / `Ctrl+Av Pág`) · escribe el número en la casilla (`Ctrl+L`) · `Inicio` / `Fin` · clic en una miniatura o en el índice |
| Acercar y alejar | `Ctrl` + rueda · `Ctrl +` / `Ctrl −` · el cuadro de zoom · `Ctrl+0` ajustar al ancho · `Ctrl+9` ajustar a la página · `Ctrl+1` tamaño real |
| Girar | `Ctrl+→` / `Ctrl+←` |
| Dos páginas | Botón de dos páginas (el menú ⋯ permite dejar la portada suelta) |
| Pantalla completa | `F11` (salir con `Esc`) |
| Presentación | `F5`: flechas, espacio o clic para avanzar; clic derecho para retroceder; `B` pantalla en negro; `Esc` sale |
| Barra lateral | Botón de panel (arriba a la izquierda) o `F9`: miniaturas, índice, anotaciones y adjuntos |
| Vista previa de un vínculo | Deja el puntero un momento sobre un vínculo del documento (por ejemplo, una entrada del índice): aparece una miniatura de la zona a la que lleva, o la dirección si es una página web |
| Volver atrás / adelante | `Alt+←` / `Alt+→` (después de usar un enlace, el índice o una miniatura) |

## Ayuda

`F1` abre este manual en una ventana de Velora. **Ayuda → Acerca de Velora** muestra la versión y, con **Licencias y créditos**, de dónde viene lo que usa el programa.

## Ventana estrecha

Si reduces la ventana, la barra de herramientas oculta por orden los botones menos usados (giro y dos páginas, imprimir, notas y dibujo, abrir);
todos siguen disponibles en el menú **⋯** de la derecha.

## Buscar

`Ctrl+F` abre la barra de búsqueda. Escribe y los resultados se marcan en amarillo (el actual en naranja) mientras
Velora recorre el documento; `F3` / `Mayús+F3` (o `Intro`) pasan al siguiente o al anterior. Puedes distinguir mayúsculas y
buscar palabras completas. `Esc` cierra la búsqueda.

## Seleccionar y copiar texto

Arrastra sobre el texto. **Doble clic** selecciona una palabra y **triple clic** una línea. Al seleccionar aparece una barra flotante con
*Copiar*, *Resaltar*, *Subrayar*, *Ondulado* y *Tachar*. También `Ctrl+C` y `Ctrl+A` (todo el documento).

## Anotar

1. Selecciona texto y elige **Resaltar**, **Subrayar**, **Ondulado** o **Tachar**.
2. **Nota** (icono de globo): haz clic en la página, escribe y pulsa *Guardar*. Haz clic en el icono de una nota para leerla o eliminarla.
3. **Dibujar** (icono de lápiz): arrastra sobre la página. `Esc` vuelve al modo normal.
4. El círculo de color cambia el color (el de dibujo es independiente del de resaltado). El icono de las notas siempre se dibuja en amarillo.
5. La pestaña **Anotaciones** de la barra lateral las lista; desde allí puedes ir a cada una o eliminarla.

Las anotaciones se escriben en el PDF al **Guardar** (`Ctrl+S`). Si el archivo original no se puede sustituir (solo lectura, en uso),
Velora guarda una copia llamada «… (con anotaciones).pdf» y sigue trabajando con ella. El título de la ventana muestra ● cuando hay cambios sin guardar.
**Guardar una copia…** (`Ctrl+Mayús+S`) escribe el PDF con las anotaciones en otro archivo sin tocar el original.

## Imprimir

`Ctrl+P` abre la ventana de impresión de Velora, con **vista previa** de cada hoja (flechas ← → para pasar de una a otra): elige la impresora, las copias,
la orientación, color o blanco y negro y las páginas (todo el documento, la actual o un intervalo como `1-3, 7`). **Propiedades de la impresora…** abre los ajustes propios de tu impresora.
Las páginas se dibujan a 300 ppp y, en orientación automática, se giran solas si el papel es de otra orientación.

## Datos que guarda Velora

`%APPDATA%\Velora\settings.json`: ajustes, documentos recientes y última posición. Para empezar de cero, borra esa carpeta.
`errores.log` (en la misma carpeta) anota los errores inesperados.

## Preguntas frecuentes

- **¿Por qué es rápido?** Solo se dibuja lo que se ve (más un par de páginas cercanas); lo ya dibujado se guarda en una caché con límite de
  memoria; todo el trabajo pesado va en un hilo aparte con prioridades (lo visible primero) y se cancela lo que ya no se necesita al
  hacer scroll rápido. La ventana nunca espera al motor.
- **¿Por qué usa tanta memoria con un PDF de cientos de imágenes?** PDFium guarda en memoria lo que ya leyó; Velora vuelve a abrir el
  documento cuando lo estimado supera unos 256 MB (si no hay cambios sin guardar) y limita su caché de imágenes. Puedes bajar el límite de
  la caché con la variable de entorno `VELORA_CACHE_MB` (por ejemplo `64`).
- **Los enlaces externos piden confirmación** para evitar abrir direcciones sin que lo sepas. Solo se abren `http`, `https` y `mailto`.
