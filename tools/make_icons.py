"""Generate every Enhanced Snap icon asset from one design.

Design (product icon, Windows 11 / Fluent app-icon guidelines):
  demo00's staggered bands with its top ribbon turned into a WinUI-Gallery-style dark-blue title bar — a window —
  and demo00's bottom band as a cyan window snapped in front of it, flush with the left and bottom edges. Two layers,
  one metaphor (snapped windows), cyan-blue analogous palette.

Construction rules followed (learn.microsoft.com/windows/apps/design/iconography/app-icon-design):
  * 48 x 48 grid, live area 2..46; exterior corners 2 px at 48 (no interior corners in this design).
  * Front-facing, flat layers; the front window casts the two-part "separate metaphor" drop shadow, masked into the
    window below (nothing falls on the background).
  * Gradients: one step each, 120 degrees (light from the top left, mostly vertical), monochrome per layer.
  * Colour values in all three ranges; at least half of the icon passes 3:1 on the light and the dark theme
    (checked and printed below).
  * Below 48 px the icon is simplified per size (Fluent: "use icons at specific sizes rather than scaling them"):
    edges snapped to whole pixels, shadow replaced by a one-pixel cut-out around the front window.
  * Transparent background (unplated); Windows' theme variants (altform-unplated / lightunplated) are the same art.

Usage:  python tools/make_icons.py          (writes assets/ and packaging/Images/)
"""

from __future__ import annotations

import math
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
PACKAGE_ASSETS = ROOT / "packaging" / "Images"  # MSIX logos (Assets\ in the package is the apps' own folder)

# ---- design on the 48 grid -------------------------------------------------------------------------------------

WINDOW = (2, 2, 46, 33)          # back window (demo00's top + middle bands)
TITLE_BOTTOM = 9                 # its title bar: 7 px, like the WinUI Gallery icon's
FRONT = (2, 22, 34, 46)          # front window (demo00's bottom band), flush left and bottom
RADIUS = 2                       # exterior corners at 48 px

# Cyan-blue palette (Icon_Example00: WinUI 3 Gallery, Quick Assist, Live captions). (light end, dark end) per layer.
TITLE = ((0x0A, 0x6F, 0xD2), (0x0B, 0x4E, 0x9C))
BODY = ((0x12, 0x90, 0xE6), (0x0A, 0x6C, 0xC8))
FRONT_FILL = ((0x5E, 0xDC, 0xFF), (0x22, 0xB2, 0xF0))
PAUSED = {  # tray icon while snapping is paused: the same art in Fluent neutrals
    "title": ((0x6E, 0x6E, 0x6E), (0x55, 0x55, 0x55)),
    "body": ((0x8A, 0x8A, 0x8A), (0x70, 0x70, 0x70)),
    "front": ((0xD2, 0xD2, 0xD2), (0xB4, 0xB4, 0xB4)),
}

# "Separate metaphor" shadow at 48 px: (y offset, blur, opacity) — two shadows, the second softer.
SHADOWS = ((0, 2, 0.20), (2, 4, 0.22))
GRADIENT_ANGLE = 120             # design-tool convention: 0 = left to right, counter-clockwise; light end at the top left

ICO_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]
TRAY_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 64]
TARGET_SIZES = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256]
TITLE_BAR_SIZES = [16, 20, 24, 28, 32, 36, 40, 48]  # 16 epx at 100..300 % display scale


@dataclass(frozen=True)
class Palette:
    title: tuple
    body: tuple
    front: tuple


COLOUR = Palette(TITLE, BODY, FRONT_FILL)
GREY = Palette(PAUSED["title"], PAUSED["body"], PAUSED["front"])


# ---- rendering ---------------------------------------------------------------------------------------------------

def fitted(size: int):
    """Geometry in target pixels. Below 48 px every edge lands on a whole pixel and the title bar stays visible."""
    k = size / 48
    if size >= 48:
        scale = lambda box: tuple(v * k for v in box)  # noqa: E731
        return scale(WINDOW), TITLE_BOTTOM * k, scale(FRONT), RADIUS * k, False

    def snap(v: float) -> int:
        return int(math.floor(v * k + 0.5))

    window = tuple(snap(v) for v in WINDOW)
    front = tuple(snap(v) for v in FRONT)
    title = max(window[1] + 2, snap(TITLE_BOTTOM))
    front = (front[0], max(front[1], title + 3), front[2], front[3])  # keep some window body above the front window
    return window, title, front, max(0.75, RADIUS * k), True


def gradient(n: int, box, c0, c1) -> np.ndarray:
    """Linear gradient over a box at GRADIENT_ANGLE, light end (c0) at the top left."""
    x0, y0, x1, y1 = box
    a = math.radians(GRADIENT_ANGLE)
    dx, dy = -math.cos(a), math.sin(a)  # from the light end toward the dark end, in screen coordinates (y down)
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32) + 0.5
    proj = [cx * dx + cy * dy for cx, cy in ((x0, y0), (x1, y0), (x0, y1), (x1, y1))]
    lo, hi = min(proj), max(proj)
    t = np.clip(((xx * dx + yy * dy) - lo) / (hi - lo), 0, 1)[..., None]
    return np.array(c0, np.float32) * (1 - t) + np.array(c1, np.float32) * t


def rounded(n: int, box, r: float, corners=(True, True, True, True)) -> np.ndarray:
    m = Image.new("L", (n, n), 0)
    x0, y0, x1, y1 = box
    ImageDraw.Draw(m).rounded_rectangle((x0, y0, x1 - 1, y1 - 1), radius=r, fill=255, corners=corners)
    return np.asarray(m, np.float32) / 255


def render(size: int, palette: Palette = COLOUR) -> Image.Image:
    window, title_bottom, front, radius, small = fitted(size)
    ss = 8 if size <= 64 else 4
    n = size * ss
    up = lambda box: tuple(v * ss for v in box)  # noqa: E731
    w_box, f_box = up(window), up(front)
    t_box = (w_box[0], w_box[1], w_box[2], title_bottom * ss)
    r = radius * ss

    w = rounded(n, w_box, r)
    t = w * rounded(n, t_box, r, (True, True, False, False))
    f = rounded(n, f_box, r)

    rgb = np.zeros((n, n, 3), np.float32)
    alpha = np.zeros((n, n), np.float32)

    def fill(mask, colour):
        nonlocal rgb, alpha
        rgb = rgb * (1 - mask[..., None]) + colour * mask[..., None]
        alpha = alpha * (1 - mask) + mask

    fill(w, gradient(n, w_box, *palette.body))
    fill(t, gradient(n, t_box, *palette.title))
    if small:
        # One pixel of air around the front window where it overlaps the back one (a shadow would only blur here).
        gap = rounded(n, (f_box[0] - ss, f_box[1] - ss, f_box[2] + ss, f_box[3] + ss), r + ss)
        cut = gap * (1 - f)
        rgb *= (1 - cut)[..., None]
        alpha *= 1 - cut
    else:
        k = n / 48
        shade = np.zeros((n, n), np.float32)
        for dy, blur, opacity in SHADOWS:
            m = Image.fromarray((f * 255).astype(np.uint8))
            shifted = Image.new("L", (n, n), 0)
            shifted.paste(m, (0, int(round(dy * k))))
            soft = shifted.filter(ImageFilter.GaussianBlur(blur * k / 2))
            shade = np.maximum(shade, np.asarray(soft, np.float32) / 255 * opacity)
        shade *= alpha * (1 - f)  # only on the window below, never on the background
        rgb *= (1 - shade)[..., None]
    fill(f, gradient(n, f_box, *palette.front))

    out = np.dstack([rgb * alpha[..., None] / np.maximum(alpha, 1e-6)[..., None], alpha * 255])
    image = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")
    return image.reduce(ss) if ss > 1 else image


def svg() -> str:
    """The 48 px master as SVG (for designers; the PNG/ICO assets are rendered by this script)."""
    def lin(name, box, c):
        x0, y0, x1, y1 = box
        a = math.radians(GRADIENT_ANGLE)
        dx, dy = -math.cos(a), math.sin(a)
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        half = (abs((x1 - x0) * dx) + abs((y1 - y0) * dy)) / 2
        hexc = lambda rgb: "#%02X%02X%02X" % rgb  # noqa: E731
        return (f'<linearGradient id="{name}" gradientUnits="userSpaceOnUse" x1="{cx - dx * half:.3f}" y1="{cy - dy * half:.3f}" '
                f'x2="{cx + dx * half:.3f}" y2="{cy + dy * half:.3f}"><stop offset="0" stop-color="{hexc(c[0])}"/>'
                f'<stop offset="1" stop-color="{hexc(c[1])}"/></linearGradient>')

    wx0, wy0, wx1, wy1 = WINDOW
    fx0, fy0, fx1, fy1 = FRONT
    title = (f'M{wx0},{TITLE_BOTTOM}V{wy0 + RADIUS}a{RADIUS},{RADIUS} 0 0 1 {RADIUS},-{RADIUS}H{wx1 - RADIUS}'
             f'a{RADIUS},{RADIUS} 0 0 1 {RADIUS},{RADIUS}V{TITLE_BOTTOM}Z')
    s1, s2 = SHADOWS
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48">
  <title>Enhanced Snap</title>
  <defs>
    {lin("body", WINDOW, BODY)}
    {lin("title", (wx0, wy0, wx1, TITLE_BOTTOM), TITLE)}
    {lin("front", FRONT, FRONT_FILL)}
    <clipPath id="window"><rect x="{wx0}" y="{wy0}" width="{wx1 - wx0}" height="{wy1 - wy0}" rx="{RADIUS}"/></clipPath>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="{s1[0]}" stdDeviation="{s1[1] / 2}" flood-color="#000" flood-opacity="{s1[2]}"/>
      <feDropShadow dx="0" dy="{s2[0]}" stdDeviation="{s2[1] / 2}" flood-color="#000" flood-opacity="{s2[2]}"/>
    </filter>
  </defs>
  <rect x="{wx0}" y="{wy0}" width="{wx1 - wx0}" height="{wy1 - wy0}" rx="{RADIUS}" fill="url(#body)"/>
  <path d="{title}" fill="url(#title)"/>
  <g clip-path="url(#window)">
    <rect x="{fx0}" y="{fy0}" width="{fx1 - fx0}" height="{fy1 - fy0}" rx="{RADIUS}" fill="#000" filter="url(#shadow)"/>
  </g>
  <rect x="{fx0}" y="{fy0}" width="{fx1 - fx0}" height="{fy1 - fy0}" rx="{RADIUS}" fill="url(#front)"/>
</svg>
"""


# ---- checks --------------------------------------------------------------------------------------------------------

def luminance(rgb: np.ndarray) -> np.ndarray:
    c = rgb / 255
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return c[..., 0] * 0.2126 + c[..., 1] * 0.7152 + c[..., 2] * 0.0722


def contrast_share(image: Image.Image, background: tuple[int, int, int]) -> float:
    """Share of the icon's opaque area with at least 3:1 contrast against a theme background."""
    a = np.asarray(image, np.float32)
    opaque = a[..., 3] > 200
    lum = luminance(a[..., :3])
    bg = luminance(np.array(background, np.float32))
    ratio = (np.maximum(lum, bg) + 0.05) / (np.minimum(lum, bg) + 0.05)
    return float((ratio[opaque] >= 3.0).mean())


# ---- outputs -------------------------------------------------------------------------------------------------------

def save_ico(path: Path, sizes: list[int], palette: Palette = COLOUR) -> None:
    images = [render(s, palette) for s in sizes]
    images[-1].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])


def plated(width: int, height: int, scale: float, icon_size: int) -> Image.Image:
    """A tile-sized transparent canvas with the icon centred (MSIX tiles, Store logo, splash screen)."""
    canvas = Image.new("RGBA", (round(width * scale), round(height * scale)), (0, 0, 0, 0))
    s = round(icon_size * scale)
    icon = render(s)
    canvas.alpha_composite(icon, ((canvas.width - s) // 2, (canvas.height - s) // 2))
    return canvas


def package_assets() -> None:
    PACKAGE_ASSETS.mkdir(parents=True, exist_ok=True)
    for old in PACKAGE_ASSETS.glob("*.png"):
        old.unlink()
    scales = {100: 1.0, 125: 1.25, 150: 1.5, 200: 2.0, 400: 4.0}
    for s in TARGET_SIZES:  # app list / taskbar / Start (unplated, so the theme variants are the same art)
        image = render(s)
        for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
            image.save(PACKAGE_ASSETS / f"Square44x44Logo.targetsize-{s}{suffix}.png")
    for scale, k in scales.items():
        render(round(44 * k)).save(PACKAGE_ASSETS / f"Square44x44Logo.scale-{scale}.png")
        render(round(50 * k)).save(PACKAGE_ASSETS / f"StoreLogo.scale-{scale}.png")
        plated(71, 71, k, 44).save(PACKAGE_ASSETS / f"SmallTile.scale-{scale}.png")
        plated(150, 150, k, 84).save(PACKAGE_ASSETS / f"MedTile.scale-{scale}.png")
        plated(310, 150, k, 84).save(PACKAGE_ASSETS / f"WideTile.scale-{scale}.png")
        plated(310, 310, k, 172).save(PACKAGE_ASSETS / f"LargeTile.scale-{scale}.png")
        plated(620, 300, k, 160).save(PACKAGE_ASSETS / f"SplashScreen.scale-{scale}.png")


def main() -> int:
    ASSETS.mkdir(exist_ok=True)
    save_ico(ASSETS / "EnhancedSnap.ico", ICO_SIZES)
    save_ico(ASSETS / "Tray.ico", TRAY_SIZES)
    save_ico(ASSETS / "TrayPaused.ico", TRAY_SIZES, GREY)
    for s in TITLE_BAR_SIZES + [64, 256]:  # settings title bar (16 epx at every display scale), About page, README
        render(s).save(ASSETS / f"AppIcon-{s}.png")
    render(1024).save(ASSETS / "AppIcon-1024.png")
    (ASSETS / "AppIcon.svg").write_text(svg(), encoding="utf-8")
    package_assets()

    big = render(256)
    light, dark = contrast_share(big, (243, 243, 243)), contrast_share(big, (32, 32, 32))
    print(f"3:1 contrast: {light:.0%} of the icon on the light theme, {dark:.0%} on the dark theme (needs >= 50% each)")
    for name in ("EnhancedSnap.ico", "Tray.ico", "TrayPaused.ico", "AppIcon-256.png", "AppIcon.svg"):
        print(f"wrote {ASSETS / name} ({(ASSETS / name).stat().st_size} bytes)")
    print(f"wrote {len(list(PACKAGE_ASSETS.glob('*.png')))} package assets to {PACKAGE_ASSETS}")
    return 0 if light >= 0.5 and dark >= 0.5 else 1


if __name__ == "__main__":
    sys.exit(main())
