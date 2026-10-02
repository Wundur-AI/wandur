#!/usr/bin/env python3
"""Draws the disk image window's background: scripts/macos/dmg-background.tiff.

  python3 scripts/macos/dmg-background.py [--preview out.png]

Run it on a Mac (it uses the Avenir Next system font and tiffutil) after changing the layout, and commit
the .tiff; the release builds only read it. The layout must agree with scripts/macos/dmg-settings.py:
a 640 by 400 point window, 128 point icons centred at (160, 196) for the app and (480, 196) for the
Applications shortcut.

Finder draws the icon labels itself, black in Light Mode and white in Dark Mode, so the band the labels
sit on is a mid tone where either reads. The palette is the app icon's: night violet above, the warm
path below, and a dotted trail of light from the app to Applications.

--preview also writes a picture with the app icon and both label colours drawn in, to check the result.
"""
import math
import random
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = Path(__file__).resolve().parent
WIDTH, HEIGHT = 640, 400
APP, APPLICATIONS, ICON_Y, ICON = 160, 480, 196, 128
FONT = "/System/Library/Fonts/Avenir Next.ttc"
# Index in the collection: 2 is Demi Bold, 5 is Medium (Avenir Next.ttc on macOS 15 and later).
DEMI_BOLD, MEDIUM = 2, 5

# Top to bottom: night sky, the icon's violet, a dusky mauve band for the labels, the warm path light.
STOPS = [(0.00, (17, 12, 38)), (0.36, (58, 42, 104)), (0.62, (146, 114, 150)), (1.00, (220, 166, 112))]
IVORY = (246, 236, 220)
INK = (36, 24, 63)
GOLD = (255, 205, 120)


def blend(a, b, t):
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))


def colour_at(fraction):
    for (f0, c0), (f1, c1) in zip(STOPS, STOPS[1:]):
        if fraction <= f1:
            return blend(c0, c1, (fraction - f0) / (f1 - f0))
    return STOPS[-1][1]


def draw(scale):
    w, h = WIDTH * scale, HEIGHT * scale
    image = Image.new("RGB", (w, h))
    pixels = ImageDraw.Draw(image)
    for y in range(h):
        pixels.line([(0, y), (w, y)], fill=colour_at(y / (h - 1)))

    # Stars in the upper sky only, the same ones at every scale.
    stars = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    sky = ImageDraw.Draw(stars)
    rng = random.Random(7)
    for _ in range(70):
        x, y = rng.uniform(0, WIDTH), rng.uniform(0, HEIGHT * 0.42)
        r = rng.choice([0.6, 0.8, 1.0, 1.3]) * scale
        alpha = round(rng.uniform(70, 190) * (1 - y / (HEIGHT * 0.5)))
        sky.ellipse([x * scale - r, y * scale - r, x * scale + r, y * scale + r], fill=(255, 244, 225, alpha))
    image.paste(stars, (0, 0), stars)

    # The trail: dots along a gentle arc from the app to Applications, growing and warming, with a glow
    # and an arrowhead, the path in the icon carried across the window.
    start, end, lift = APP + ICON / 2 + 14, APPLICATIONS - ICON / 2 - 22, 22
    points = []
    for i in range(15):
        t = i / 14
        x = start + (end - start) * t
        y = ICON_Y - lift * math.sin(math.pi * t)
        points.append((x, y, t))
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    halo = ImageDraw.Draw(glow)
    for x, y, t in points:
        r = (2.2 + 2.4 * t) * scale * 2.4
        halo.ellipse([x * scale - r, y * scale - r, x * scale + r, y * scale + r], fill=GOLD + (round(70 + 60 * t),))
    glow = glow.filter(ImageFilter.GaussianBlur(5 * scale))
    image.paste(glow, (0, 0), glow)
    dots = ImageDraw.Draw(image)
    for x, y, t in points:
        r = (1.6 + 2.0 * t) * scale
        dots.ellipse([x * scale - r, y * scale - r, x * scale + r, y * scale + r], fill=blend(GOLD, (255, 248, 230), 0.5 * t))
    # Arrowhead: a chevron past the last dot, along the direction the trail ends in.
    (x0, y0, _), (x1, y1, _) = points[-2], points[-1]
    angle = math.atan2(y1 - y0, x1 - x0)
    tip = ((x1 + 22 * math.cos(angle)) * scale, (y1 + 22 * math.sin(angle)) * scale)
    for side in (-1, 1):
        wing = angle + math.pi - side * 0.6
        end_point = (tip[0] + 14 * scale * math.cos(wing), tip[1] + 14 * scale * math.sin(wing))
        dots.line([tip, end_point], fill=(255, 248, 230), width=round(3 * scale))
        r = 1.2 * scale
        dots.ellipse([end_point[0] - r, end_point[1] - r, end_point[0] + r, end_point[1] + r], fill=(255, 248, 230))
    r = 1.2 * scale
    dots.ellipse([tip[0] - r, tip[1] - r, tip[0] + r, tip[1] + r], fill=(255, 248, 230))

    text = ImageDraw.Draw(image)
    wordmark = ImageFont.truetype(FONT, 30 * scale, index=DEMI_BOLD)
    text.text((w / 2, 52 * scale), "Wandur", font=wordmark, fill=IVORY, anchor="mm")
    caption = ImageFont.truetype(FONT, 14 * scale, index=MEDIUM)
    text.text((w / 2, 358 * scale), "Drag Wandur into Applications to install it", font=caption, fill=INK, anchor="mm")
    return image


def preview(path):
    image = draw(2).convert("RGBA")
    icon = Image.open(HERE.parent.parent / "src/Wandur.Desktop/Assets/icon-256.png").convert("RGBA").resize((ICON * 2, ICON * 2))
    folder = Image.new("RGBA", (ICON * 2, ICON * 2), (0, 0, 0, 0))
    ImageDraw.Draw(folder).rounded_rectangle([24, 48, ICON * 2 - 24, ICON * 2 - 24], 24, fill=(110, 170, 230, 255))
    for x, art in ((APP, icon), (APPLICATIONS, folder)):
        image.paste(art, (round((x - ICON / 2) * 2), round((ICON_Y - ICON / 2) * 2)), art)
    label = ImageFont.truetype(FONT, 26, index=MEDIUM)
    marks = ImageDraw.Draw(image)
    # Finder's labels: black in Light Mode on the left half of each, white in Dark Mode on the right.
    for x, name in ((APP, "Wandur"), (APPLICATIONS, "Applications")):
        marks.text(((x - 34) * 2, (ICON_Y + ICON / 2 + 12) * 2), name, font=label, fill=(0, 0, 0), anchor="mm")
        marks.text(((x + 40) * 2, (ICON_Y + ICON / 2 + 12) * 2), name, font=label, fill=(255, 255, 255), anchor="mm")
    image.save(path)


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "--preview":
        preview(sys.argv[2])
        return
    with tempfile.TemporaryDirectory() as work:
        one, two = Path(work) / "background.png", Path(work) / "background@2x.png"
        draw(1).save(one, dpi=(72, 72))
        draw(2).save(two, dpi=(144, 144))
        # One TIFF holding both sizes, so Finder picks the sharp one on a Retina display.
        out = HERE / "dmg-background.tiff"
        subprocess.run(["tiffutil", "-cathidpicheck", str(one), str(two), "-out", str(out)], check=True)
        print(f"Wrote {out}")


if __name__ == "__main__":
    main()
