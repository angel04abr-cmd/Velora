# SPDX-License-Identifier: GPL-3.0-or-later
"""Genera installer/Licencia.rtf, el texto que muestra el instalador (MSI) antes de instalar.

Es un resumen en español de la licencia de Velora; el texto legal completo (GPL-3.0) va en LICENSE.txt, dentro de la carpeta instalada.
Uso:  python tools/make-licencia-rtf.py
"""
from pathlib import Path

PARRAFOS = [
    ("Velora - Visor de PDF rápido y ligero", True),
    ("Velora es software libre: puedes usarlo, estudiarlo, modificarlo y compartirlo según los términos de la Licencia Pública General de GNU, "
     "versión 3 o posterior (GPL-3.0-or-later). El texto completo está en el archivo LICENSE.txt de la carpeta de instalación.", False),
    ("En resumen: puedes usarlo gratis, para lo que quieras; si lo modificas y lo distribuyes, debes hacerlo también bajo la GPL y entregar el código fuente. "
     "El código está en https://github.com/angel04abr-cmd/Velora.", False),
    ("Créditos: la idea de un visor rápido (dibujar solo lo visible, memoria de páginas y trabajo en segundo plano con prioridades) se inspira en Papers, "
     "el visor de documentos de GNOME (GPL-2.0 o posterior); Velora es un programa independiente y no está afiliado al Proyecto GNOME. "
     "El motor de PDF es PDFium (licencia BSD) y la plataforma es .NET (licencia MIT). Los avisos completos están en AVISOS-DE-TERCEROS.md y en la carpeta LICENSES.", False),
    ("Velora se distribuye SIN NINGUNA GARANTÍA, en la medida que permita la ley.", False),
    ("Al pulsar «Acepto» confirmas que has leído este resumen. Pulsa «Siguiente» para continuar.", False),
]


def rtf_escape(text: str) -> str:
    out = []
    for ch in text:
        o = ord(ch)
        if ch in "\\{}":
            out.append("\\" + ch)
        elif o < 128:
            out.append(ch)
        else:
            out.append(f"\\u{o if o < 32768 else o - 65536}?")
    return "".join(out)


def main() -> None:
    body = []
    for text, bold in PARRAFOS:
        t = rtf_escape(text)
        body.append(("\\b " + t + "\\b0" if bold else t) + "\\par\\par")
    rtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fswiss Segoe UI;}}\\fs20 " + "\n".join(body) + "}"
    out = Path(__file__).resolve().parent.parent / "installer" / "Licencia.rtf"
    out.write_text(rtf, encoding="ascii")
    print("Escrito", out)


if __name__ == "__main__":
    main()
