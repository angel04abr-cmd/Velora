# Velora

**Velora PDF: visor de PDF rápido y ligero, completamente en español, para Windows.**

Velora nació al probar [Papers](https://gitlab.gnome.org/GNOME/papers), el visor de documentos de GNOME en Linux,
que abre incluso PDF muy pesados con una fluidez notable. Velora recoge las ideas que lo hacen rápido
(dibujar solo lo que se ve, guardar en memoria lo ya dibujado y trabajar en segundo plano con prioridades) y las
construye de cero para Windows con **C# / WPF** y el motor **PDFium**, el mismo que usa Chrome.

> Versión 1.0.4 · Licencia **GPL-3.0 o posterior** · No está afiliado al Proyecto GNOME (ver [avisos](AVISOS-DE-TERCEROS.md)).

## Descargar e instalar

Ve a **[Releases](https://github.com/angel04abr-cmd/Velora/releases/latest)**, descarga **`Velora-x.y.z-x64.msi`** y haz doble clic. Es un instalador normal de Windows:
pide permiso de administrador, instala en `C:\Program Files\Velora`, crea el acceso en el menú Inicio y aparece en **Configuración → Aplicaciones → Aplicaciones instaladas**,
desde donde se desinstala. Necesitas Windows 10 u 11 y el [Runtime de escritorio de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (el instalador avisa si falta).
Después Velora **se actualiza sola**: avisa cuando hay una versión nueva (*Ayuda → Buscar actualizaciones…* para comprobarlo cuando quieras).
También hay una copia portátil (`Velora-x.y.z-win-x64.zip`: descomprimir y ejecutar `Velora.exe`) que no se actualiza sola.
Más abajo: cómo [compilarla tú](#para-desarrolladores).

## Qué puedes hacer

| Área | Funciones |
|---|---|
| **Leer** | Desplazamiento continuo y suave · zoom con Ctrl + rueda, «ajustar al ancho» y «ajustar a la página» · girar páginas · una o dos páginas a la vez (con portada suelta) · pantalla completa · modo presentación |
| **Moverte** | Miniaturas · índice (marcadores) · ir a una página · enlaces dentro del documento con «volver atrás» · enlaces web (con confirmación) |
| **Buscar** | Resultados resaltados en todo el documento · siguiente/anterior · distinguir mayúsculas · palabra completa |
| **Texto** | Seleccionar arrastrando, doble clic (palabra) o triple clic (línea) · copiar al portapapeles |
| **Anotar** | Resaltar · subrayar · subrayado ondulado · tachar · notas · dibujo a mano alzada · 6 colores · lista de anotaciones · eliminar |
| **Guardar** | Las anotaciones se escriben en el propio PDF y las ven otros lectores · «guardar una copia» · imprimir con el cuadro de Windows |
| **Ventana** | Cabecera unificada (menú, herramientas y botones de la ventana en una sola barra) que se adapta a ventanas estrechas · menú completo con todos los atajos |
| **Más** | PDF con contraseña · adjuntos del PDF · propiedades del documento · recuerda la página y el zoom de cada documento · tema claro y oscuro de Windows |

Todavía no incluye (ver [DESARROLLO](docs/DESARROLLO.md#ideas-para-más-adelante)): rellenar formularios, firmas
digitales, otros formatos (DjVu, cómics, TIFF) y reconocimiento de texto (OCR).

## Imagen de marca

| Color | Valor | Uso |
|---|---|---|
| Carmesí | `#C8102E` | Color principal: botones activos, selección, acentos |
| Blanco suave | `#F7F7F9` | Superficies claras |
| Grafito | `#454650` | Texto e iconos en el tema claro |

Nombre comercial **Velora**, nombre descriptivo **Velora PDF**. Estilo de interfaz: Fluent Design de Windows 11 (temas claro y oscuro
automáticos). El icono original está en `diseno/`; los colores se aplican en `src/Velora.App/Services/ThemeHelper.cs`.

## Instalar desde el código

Necesitas **Windows 10 u 11** y el [SDK de .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

1. `.\build.ps1 -Instalar` crea el instalador `dist\Velora-1.0.4-x64.msi` y lo ejecuta (Windows pide permiso de administrador). La primera vez descarga WiX (en `.tools\`).
2. Para que los PDF se abran siempre con Velora: *Configuración → Aplicaciones → Aplicaciones predeterminadas → Velora → .pdf*.

**Actualizar:** Velora lo hace sola (ver arriba), o ejecuta el `.msi` de la versión nueva (reemplaza a la anterior y conserva tus ajustes).
**Desinstalar:** *Configuración → Aplicaciones instaladas → Velora → Desinstalar* (o clic derecho sobre Velora en el menú Inicio → Desinstalar).
Si tenías la versión 1.0.0 a 1.0.2 (se instalaba solo para tu usuario con `Instalar.ps1`), desinstálala primero desde *Aplicaciones instaladas*.

Guía de uso: [docs/MANUAL.md](docs/MANUAL.md).

## Para desarrolladores

```powershell
dotnet test tests\Velora.Tests          # 37 pruebas automáticas
dotnet run --project src\Velora.App     # ejecutar desde el código
.\build.ps1 -Probar -Zip                # probar y empaquetar
```

Estructura: `src\Velora.Pdf` (motor: PDFium, cola de tareas, cálculos del visor, sin dependencias de WPF),
`src\Velora.App` (la aplicación WPF), `tests` (pruebas), `tools` (utilidades y scripts). Más detalles en
[docs/DESARROLLO.md](docs/DESARROLLO.md) y el análisis de Papers en [docs/ANALISIS-PAPERS.md](docs/ANALISIS-PAPERS.md).

## Licencia

Velora es software libre: GPL-3.0-or-later (ver [LICENSE](LICENSE)). Incluye PDFium y otros componentes con sus licencias
([AVISOS-DE-TERCEROS.md](AVISOS-DE-TERCEROS.md)). Se distribuye **sin garantía**.
