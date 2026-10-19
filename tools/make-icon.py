# SPDX-License-Identifier: GPL-3.0-or-later
"""Genera los iconos de Velora (velora.png y velora.ico) a partir del diseño original del autor.

Uso:  python tools/make-icon.py <imagen-original.(png|webp)> [carpeta-de-salida]

El diseño es obra del autor del proyecto (no usa ningún logotipo de terceros). Se recorta el margen
transparente, se deja un pequeño respiro y se crean los tamaños que Windows necesita.
Necesita la biblioteca Pillow:  pip install pillow
"""
import io
import struct
import sys
from pathlib import Path

from PIL import Image


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = Path(sys.argv[1])
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).resolve().parent.parent / "src" / "Velora.App" / "Resources"
    out.mkdir(parents=True, exist_ok=True)

    im = Image.open(src).convert("RGBA")
    # Recortar el margen transparente y centrar en un cuadrado con un 3 % de respiro
    box = im.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    if box:
        im = im.crop(box)
    side = int(max(im.size) * 1.03)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(im, ((side - im.width) // 2, (side - im.height) // 2), im)
    master = canvas.resize((1024, 1024), Image.LANCZOS)

    master.resize((512, 512), Image.LANCZOS).save(out / "velora.png", optimize=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    images = []
    for s in sizes:
        buf = io.BytesIO()
        master.resize((s, s), Image.LANCZOS).save(buf, "PNG", optimize=True)
        images.append((s, buf.getvalue()))

    # .ico con imágenes PNG (Windows Vista o posterior)
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for s, data in images:
        entries += struct.pack("<BBBBHHII", 0 if s >= 256 else s, 0 if s >= 256 else s, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    (out / "velora.ico").write_bytes(header + entries + b"".join(d for _, d in images))
    print(f"Iconos escritos en {out}")


if __name__ == "__main__":
    main()
