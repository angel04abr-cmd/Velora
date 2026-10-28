# Historial de cambios

## [1.0.4] - 2026-10-28

- **Desplazamiento rápido en libros grandes**: al arrastrar la barra o girar la rueda muy deprisa por un libro de miles de páginas, las páginas y las miniaturas se quedaban en blanco
  y a veces ya no cargaban. Ahora se ve al instante un borrador borroso de cada página y la calidad completa llega al detenerte.
- Velora ya no se pone a leer el texto de miles de páginas que pasaste de largo (se acumulaba una cola de trabajo que dejaba las páginas visibles esperando).
- Menos memoria: tras desplazarse a toda velocidad por un libro de 8000 páginas el uso bajó de casi 3 GB a menos de 1 GB (se limitó la reserva de memoria para dibujar las páginas).

## [1.0.3] - 2026-10-20

- **Instalador normal de Windows (.msi)**: Velora se instala en `C:\Program Files\Velora`, aparece en *Configuración → Aplicaciones → Aplicaciones instaladas* y se
  desinstala desde allí (también desde el menú Inicio). Sustituye al instalador por usuario (`Instalar.ps1`) de las versiones 1.0.0 a 1.0.2, que no aparecía donde se esperaba.
  Si tienes una de esas versiones, desinstálala antes (el instalador te avisa).
- Las actualizaciones automáticas ahora descargan y ejecutan el .msi (Windows pide permiso de administrador) y comprueban su huella SHA-256.
- Pruebas automáticas del instalador (instalar, arrancar, desinstalar y actualizar) en un Windows limpio de GitHub, y 15 pruebas nuevas del sistema de actualizaciones.
- **Cabecera**: el texto de los menús (Archivo, Ver…) ya no sale cortado por abajo y los botones de minimizar, maximizar y cerrar se ven bien en el tema oscuro.
  Con la ventana estrecha los menús siguen recogiéndose en un solo botón ☰.
- **Guardar al cerrar**: si cerrabas la ventana con anotaciones sin guardar y elegías «Guardar», Windows podía negarse a reemplazar el archivo (PDFium lo mantenía abierto) y
  no se podía cerrar con comodidad. Ahora se cierra el documento antes de reemplazarlo y, si aun así no se puede, se guarda con otro nombre: nunca se pierden las anotaciones.
- **Ventana maximizada**: a veces la cabecera quedaba unos píxeles fuera de la pantalla (los botones de la ventana cortados por el borde); ahora siempre queda ajustada.
- **Libros muy grandes** (miles de páginas escaneadas): la memoria crecía sin límite al recorrerlos. Ahora Velora también mira cuánto ha crecido de verdad y suelta lo que no usa.
- Pruebas nuevas del guardado (reemplazar el original, guardar varias veces seguidas, archivo de solo lectura).

## [1.0.2] - 2026-10-19

- **Cabecera unificada**: el icono, el menú (Archivo, Ver, Anotar, Ir, Ayuda), las herramientas y los botones de minimizar, maximizar y cerrar forman una sola barra
  de título continua. Se arrastra desde cualquier hueco, admite doble clic para maximizar y el botón de maximizar muestra las disposiciones de ventana de Windows 11.
- La cabecera se adapta a ventanas estrechas: el menú se recoge en un botón ☰ y se ocultan por orden los botones menos usados.
- **Actualizaciones automáticas** desde GitHub: Velora busca una versión nueva al arrancar (una vez al día), muestra un aviso y, si lo aceptas, descarga el paquete,
  comprueba su huella SHA-256, lo instala y se vuelve a abrir en el mismo documento. También en *Ayuda → Buscar actualizaciones…*.
- El proyecto se publica en GitHub con integración continua (compila y ejecuta las pruebas en cada cambio).

## [1.0.1] - 2026-10-19

- Barra de herramientas adaptable a ventanas estrechas; las flechas ↑ ↓ pasan a la página anterior y siguiente (el historial queda en `Alt+←` / `Alt+→`).
- Icono de panel para mostrar u ocultar la barra lateral.

## [1.0.0] - 2026-10-19

- Primera versión: lectura rápida de PDF (PDFium), miniaturas, índice, búsqueda, selección de texto, anotaciones (resaltar, subrayar, tachar, notas, dibujo),
  guardado, impresión, presentación, PDF con contraseña, interfaz completa en español con tema claro y oscuro.
