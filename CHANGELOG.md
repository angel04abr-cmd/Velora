# Historial de cambios

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
