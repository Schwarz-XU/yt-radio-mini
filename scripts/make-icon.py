#!/usr/bin/env python3
"""Generates assets/app.ico (the application icon) with Pillow.

Run:  python3 scripts/make-icon.py
The icon is a small retro radio drawn in the same colours as the UI (see RetroTheme.cs, which follows the Claude Design canvas).
It is committed to the repository; this script only exists so that the icon can be reproduced or tweaked.
"""
import pathlib
from PIL import Image, ImageDraw

SHELL, SHELL_EDGE = (220, 203, 162), (140, 120, 82)
LCD, LCD_TEXT = (25, 40, 31), (200, 245, 155)
BUTTON, BUTTON_RIM = (91, 61, 40), (194, 154, 85)  # walnut with a brass ring
SLOT = (162, 141, 98)

SIZES = [16, 24, 32, 48, 64, 128, 256]
BIG = 1024  # draw large, then downsample for smooth edges


def draw_icon() -> Image.Image:
    img = Image.new("RGBA", (BIG, BIG), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def box(x0, y0, x1, y1):
        return [x0 * BIG, y0 * BIG, x1 * BIG, y1 * BIG]

    # body
    d.rounded_rectangle(box(0.03, 0.14, 0.97, 0.90), radius=0.13 * BIG, fill=SHELL, outline=SHELL_EDGE, width=int(0.02 * BIG))

    # LCD with a few text bars
    d.rounded_rectangle(box(0.11, 0.24, 0.89, 0.54), radius=0.06 * BIG, fill=LCD)
    d.rounded_rectangle(box(0.17, 0.31, 0.72, 0.37), radius=0.03 * BIG, fill=LCD_TEXT)
    d.rounded_rectangle(box(0.17, 0.43, 0.54, 0.48), radius=0.025 * BIG, fill=(127, 166, 106))

    # speaker slots
    for i in range(4):
        y = 0.62 + i * 0.065
        d.rounded_rectangle(box(0.12, y, 0.40, y + 0.032), radius=0.016 * BIG, fill=SLOT)

    # two round buttons
    for cx in (0.58, 0.79):
        d.ellipse(box(cx - 0.09, 0.71 - 0.09 + 0.012, cx + 0.09, 0.71 + 0.09 + 0.012), fill=SLOT)  # shadow
        d.ellipse(box(cx - 0.09, 0.71 - 0.09, cx + 0.09, 0.71 + 0.09), fill=BUTTON, outline=BUTTON_RIM, width=int(0.012 * BIG))

    # play triangle on the left button, "next" glyph on the right one
    d.polygon([(0.55 * BIG, 0.66 * BIG), (0.55 * BIG, 0.76 * BIG), (0.63 * BIG, 0.71 * BIG)], fill=LCD_TEXT)
    d.polygon([(0.755 * BIG, 0.66 * BIG), (0.755 * BIG, 0.76 * BIG), (0.805 * BIG, 0.71 * BIG)], fill=LCD_TEXT)
    d.rectangle(box(0.81, 0.66, 0.83, 0.76), fill=LCD_TEXT)
    return img


def main() -> None:
    out = pathlib.Path(__file__).resolve().parent.parent / "assets" / "app.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    base = draw_icon()
    frames = [base.resize((s, s), Image.LANCZOS) for s in SIZES]
    frames[-1].save(out, format="ICO", sizes=[(s, s) for s in SIZES], append_images=frames[:-1])
    print(f"wrote {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
