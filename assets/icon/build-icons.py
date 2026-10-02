#!/usr/bin/env python3
"""Renders the WorriorVex icon to the raster formats the platforms need.

worriorvex.svg is the reference drawing; this script draws the same geometry with Pillow so no
SVG renderer is required. Run it from the repository root after changing the design:

    python3 assets/icon/build-icons.py

It writes the PNG, ICO and (on macOS, using iconutil) ICNS files next to it and refreshes the
copies used by the desktop host and the UI.
"""
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SIZE = 1024
SCALE = 4  # drawn large, then reduced, for smooth edges

PAGE_TOP = (0x25, 0x3A, 0x59)
PAGE_BOTTOM = (0x12, 0x1C, 0x2D)
FOLD = (0x46, 0x63, 0x8F, 255)
LEFT_V = (0xF2, 0xF5, 0xF9, 255)
RIGHT_V = (0x6F, 0xA8, 0xEC, 255)


def s(value):
    return int(round(value * SCALE))


def stroke(draw, points, colour, width=104):
    scaled = [(s(x), s(y)) for x, y in points]
    draw.line(scaled, fill=colour, width=s(width), joint="curve")
    radius = s(width) / 2
    for x, y in scaled:
        draw.ellipse([x - radius, y - radius, x + radius, y + radius], fill=colour)


def render():
    full = SIZE * SCALE

    # The page: a rounded square with its top-right corner cut off.
    mask = Image.new("L", (full, full), 0)
    mask_draw = ImageDraw.Draw(mask)
    mask_draw.rounded_rectangle([s(64), s(64), s(960), s(960)], radius=s(180), fill=255)
    mask_draw.polygon([(s(730), s(64)), (s(960), s(64)), (s(960), s(294))], fill=0)

    gradient = Image.new("RGBA", (1, full))
    for y in range(full):
        t = y / (full - 1)
        gradient.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(PAGE_TOP, PAGE_BOTTOM)) + (255,))
    page = gradient.resize((full, full))

    image = Image.new("RGBA", (full, full), (0, 0, 0, 0))
    image.paste(page, (0, 0), mask)
    draw = ImageDraw.Draw(image)

    # The folded corner.
    fold_mask = Image.new("L", (full, full), 0)
    fold_draw = ImageDraw.Draw(fold_mask)
    fold_draw.rounded_rectangle([s(730), s(4), s(1020), s(294)], radius=s(60), fill=255)
    fold_draw.polygon([(s(730), s(0)), (s(1024), s(0)), (s(1024), s(294)), (s(960), s(294)), (s(730), s(64))], fill=0)
    image.paste(Image.new("RGBA", (full, full), FOLD), (0, 0), fold_mask)

    # The W: the right V first, the left V on top where they meet.
    stroke(draw, [(512, 440), (643, 720), (774, 350)], RIGHT_V)
    stroke(draw, [(250, 350), (381, 720), (512, 440)], LEFT_V)

    return image.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    master = render()
    master.save(HERE / "worriorvex-1024.png")
    master.resize((512, 512), Image.LANCZOS).save(HERE / "worriorvex-512.png")
    master.resize((256, 256), Image.LANCZOS).save(HERE / "worriorvex-256.png")
    master.save(HERE / "worriorvex.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    if sys.platform == "darwin" and shutil.which("iconutil"):
        with tempfile.TemporaryDirectory() as temp:
            iconset = Path(temp) / "worriorvex.iconset"
            iconset.mkdir()
            for points in (16, 32, 128, 256, 512):
                master.resize((points, points), Image.LANCZOS).save(iconset / f"icon_{points}x{points}.png")
                master.resize((points * 2, points * 2), Image.LANCZOS).save(iconset / f"icon_{points}x{points}@2x.png")
            subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(HERE / "worriorvex.icns")], check=True)

    desktop = ROOT / "src" / "WorriorVex.Desktop" / "Assets"
    desktop.mkdir(parents=True, exist_ok=True)
    shutil.copy(HERE / "worriorvex.ico", desktop / "worriorvex.ico")
    shutil.copy(HERE / "worriorvex-256.png", desktop / "worriorvex.png")

    ui = ROOT / "src" / "WorriorVex.UI" / "wwwroot" / "img"
    ui.mkdir(parents=True, exist_ok=True)
    shutil.copy(HERE / "worriorvex.svg", ui / "worriorvex.svg")


if __name__ == "__main__":
    main()
