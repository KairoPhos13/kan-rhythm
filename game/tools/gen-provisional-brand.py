#!/usr/bin/env python3
"""Generate kan!'s provisional logo art (made from scratch, no font, no osu! art).

Design (owner, 2026-11-09): a white ring whose disc fades from white at the rim to
gold at the centre, and the word "kan!" in white with silver borders. The letters
are monoline strokes with round caps, drawn as geometry so no font licence is
involved.

Outputs (paths relative to the repository's game/ folder):
  brand/Textures/Menu/logo.png                  1024x1024  main-menu logo (over the
                                                           code-drawn gold disc)
  brand/Textures/Icons/Logo.png                 90x90      small logo
  brand/Textures/Intro/Triangles/logo-*.png     1295x1295  the triangles intro: green =
                                                           line, red = draw order
  client/assets/lazer.png (1024), client/assets/lazer-nuget.png (128),
  client/osu.Desktop/lazer.ico, client/osu.Desktop/beatmap.ico   app icons

Usage: python3 game/tools/gen-provisional-brand.py   (needs numpy, scipy, Pillow)
"""

from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

GAME = Path(__file__).resolve().parent.parent

# Geometry in a 1024 canvas (matches the menu logo's ring so the code-drawn disc lines up).
C = 511.5          # centre
RING_OUT = 485.0   # ring outer radius
RING_IN = 436.0    # ring inner radius

W = 54.0           # letter stroke width (centre line to centre line)
BORDER = 10.0      # silver border around the white letters
XH = 186.0         # x-height
ASC = 90.0         # ascender above the x-height (k and !)
GAP = 44.0         # space between letters

WHITE = np.array([255, 255, 255], float)
SILVER_LIGHT = np.array([246, 248, 252], float)  # top of the silver border
SILVER_DARK = np.array([128, 136, 150], float)   # bottom of the silver border
GOLD_IN = np.array([255, 179, 0], float)    # ffb300, the target-ring gold
GOLD_MID = np.array([255, 217, 138], float)  # ffd98a, the glow
SHADOW = np.array([60, 40, 0], float)


def arc(cx, cy, r, a0, a1, n=200):
    """Points on a circle arc, angles in degrees, 0 = +x, counter-clockwise in screen (y down)."""
    t = np.radians(np.linspace(a0, a1, n))
    return np.stack([cx + r * np.cos(t), cy - r * np.sin(t)], 1)


def line(p, q, n=200):
    t = np.linspace(0, 1, n)[:, None]
    return np.asarray(p, float) * (1 - t) + np.asarray(q, float) * t


def glyphs():
    """Centre-line strokes of "kan!" in drawing order, as (points, kind) lists, plus total width."""
    h, a, r_w = XH, ASC, W / 2
    strokes: list[np.ndarray] = []
    x = 0.0
    base = 0.0  # baseline at y = 0, up is negative

    # k: stem, upper arm, lower leg
    top = base - h - a + r_w
    strokes.append(line((x, top), (x, base - r_w)))
    j = (x, base - 0.40 * h)
    strokes.append(line(j, (x + 102, base - h + r_w)))
    arm_end = np.array((x + 102, base - h + r_w))
    mid = np.asarray(j) + 0.32 * (arm_end - np.asarray(j))
    strokes.append(line(mid, (x + 108, base - r_w)))
    x += 108 + GAP + r_w

    # a: bowl and stem (single storey)
    r = (h - W) / 2
    cx = x + r
    cy = base - h / 2
    strokes.append(arc(cx, cy, r, 90, 450, 400))
    strokes.append(line((cx + r, base - h + r_w), (cx + r, base - r_w)))
    x = cx + r + GAP + W

    # n: stem, arch and right stem in one stroke
    r = 64.0
    arch_y = base - h + r_w + r
    n_path = np.concatenate([
        line((x, base - r_w), (x, base - h + r_w)),
        line((x, base - h + r_w), (x, arch_y)),
    ])
    strokes.append(n_path[:200])
    strokes.append(np.concatenate([
        arc(x + r, arch_y, r, 180, 0, 200),
        line((x + 2 * r, arch_y), (x + 2 * r, base - r_w)),
    ]))
    x += 2 * r + GAP + W

    # !: bar and dot
    strokes.append(line((x, base - h - a + r_w), (x, base - 0.40 * h)))
    strokes.append(line((x, base - r_w), (x, base - r_w + 0.01), 4))
    return strokes, x


def layout():
    strokes, width = glyphs()
    total_h = XH + ASC
    dx = C - width / 2
    dy = C + total_h / 2 - 6  # optical centre: a touch above the geometric one
    return [s + np.array([dx, dy]) for s in strokes]


def distance_fields(strokes, size, scale):
    """Distance (canvas units) to the nearest letter centre line, and that point's draw order."""
    grid = np.ones((size, size), bool)
    order = np.zeros((size, size), float)
    n = len(strokes)
    for i, s in enumerate(strokes):
        dense = np.concatenate([line(s[k], s[k + 1], 12) for k in range(len(s) - 1)]) if len(s) > 1 else s
        prog = (i + np.linspace(0, 1, len(dense))) / n
        px = np.clip(np.round(dense * scale).astype(int), 0, size - 1)
        grid[px[:, 1], px[:, 0]] = False
        order[px[:, 1], px[:, 0]] = prog
    dist, idx = ndimage.distance_transform_edt(grid, return_indices=True)
    return dist / scale, order[idx[0], idx[1]]


def smooth(edge, value, aa):
    """1 inside (value < edge), 0 outside, linear over aa units."""
    return np.clip((edge - value) / aa + 0.5, 0, 1)


def radius_field(size, scale):
    yy, xx = np.mgrid[0:size, 0:size].astype(float) / scale
    return np.hypot(xx - C, yy - C), np.degrees(np.arctan2(xx - C, -(yy - C))) % 360


def menu_logo(opaque_disc: bool, size=1024, ss=2):
    """RGBA logo. opaque_disc: paint the gold disc too (app icons); otherwise the game draws it."""
    n = size * ss
    scale = n / 1024
    strokes = layout()
    d, _ = distance_fields(strokes, n, scale)
    r, _ = radius_field(n, scale)
    aa = 1.2 / scale

    rgb = np.zeros((n, n, 3))
    alpha = np.zeros((n, n))

    def over(colour, a):
        nonlocal rgb, alpha
        a = np.clip(a, 0, 1)
        out_a = a + alpha * (1 - a)
        safe = np.where(out_a > 0, out_a, 1)
        rgb = (colour * a[..., None] + rgb * (alpha * (1 - a))[..., None]) / safe[..., None]
        alpha = out_a

    disc = smooth(RING_IN + 2, r, aa)
    if opaque_disc:
        t = np.clip(r / RING_IN, 0, 1)[..., None]
        gold = GOLD_IN * (1 - t) + GOLD_MID * t
        over(gold, disc)
    # soft shadow of the ring on the outside
    ring = smooth(RING_OUT, r, aa) * (1 - smooth(RING_IN, r, aa))
    shadow = ndimage.gaussian_filter(smooth(RING_OUT, r, aa), 10 * scale) * (1 - smooth(RING_OUT, r, aa))
    over(SHADOW, 0.35 * shadow)
    # white veil: white at the rim, fading to clear (gold) towards the centre
    t = np.clip((r - 0.40 * RING_IN) / (0.60 * RING_IN), 0, 1)
    veil = (t ** 1.8) * 0.90 * disc
    over(WHITE, veil)
    over(WHITE, ring)
    # letters: soft shadow, silver border, white fill
    letter_out = smooth(W / 2 + BORDER, d, aa)
    over(SHADOW, 0.22 * ndimage.gaussian_filter(letter_out, 6 * scale) * disc)
    # silver: light at the top of the word, darker at the bottom, and a bevel that is
    # brighter on the border's inner edge than on its outer edge
    yy = np.mgrid[0:n, 0:1][0][:, :1].astype(float) / scale
    top, bottom = C - (XH + ASC) / 2, C + (XH + ASC) / 2
    v = np.clip((yy - top) / (bottom - top), 0, 1)
    bevel = np.clip((d - W / 2) / BORDER, 0, 1)
    shade = np.clip(0.15 + 0.6 * v + 0.35 * bevel, 0, 1)[..., None]
    silver = SILVER_LIGHT * (1 - shade) + SILVER_DARK * shade
    over(silver, letter_out)
    over(WHITE, smooth(W / 2, d, aa))

    img = np.dstack([rgb, alpha * 255]).clip(0, 255).astype(np.uint8)
    return Image.fromarray(img, "RGBA").resize((size, size), Image.LANCZOS)


def triangles_textures(size=1295, ss=2):
    """The triangles intro's line art: green = line, red = when it appears (0 first, 255 never)."""
    n = size * ss
    # the intro texture is the menu geometry scaled so the ring's outer line touches the edge
    k = (size / 2 - 4) / RING_OUT          # texture pixels per canvas unit
    scale = k * ss                         # supersampled pixels per canvas unit
    offset = n / 2 - C * scale
    d, prog = distance_fields([s * scale + offset for s in layout()], n, 1.0)
    d /= scale
    yy, xx = np.mgrid[0:n, 0:n].astype(float)
    r = np.hypot(xx - n / 2, yy - n / 2) / scale
    ang = np.degrees(np.arctan2(xx - n / 2, -(yy - n / 2))) % 360

    out = []
    for width, delay in ((5.0, 0.0), (7.5, 0.03)):
        lw = width / k / 2  # half line width in canvas units
        aa = 1.0 / scale
        edge = W / 2 + BORDER
        letter_line = smooth(lw, np.abs(d - edge), aa)
        ring_line = np.maximum(smooth(lw, np.abs(r - RING_OUT), aa), smooth(lw, np.abs(r - RING_IN), aa))
        green = np.maximum(letter_line, ring_line)
        red = np.full((n, n), 1.0)
        red = np.where(letter_line > 0, np.minimum(red, 0.02 + 0.30 * prog + delay), red)
        ring_prog = 0.04 + 0.86 * (ang / 360) + delay
        red = np.where((ring_line > 0) & (letter_line <= 0), ring_prog, red)
        img = np.dstack([red * 255, green * 255, np.zeros((n, n)), np.full((n, n), 255)])
        im = Image.fromarray(img.clip(0, 255).astype(np.uint8), "RGBA")
        # red must not be blurred into the lines' neighbours: resize with NEAREST for red
        g = im.getchannel("G").resize((size, size), Image.LANCZOS)
        rch = im.getchannel("R").resize((size, size), Image.NEAREST)
        z = Image.new("L", (size, size), 0)
        a = Image.new("L", (size, size), 255)
        out.append(Image.merge("RGBA", (rch, g, z, a)))
    return out  # highlight, background


# Menu backgrounds: calm, warm light. Each one blends two palette colours over a deep
# base, with soft out-of-focus circles and faint sound-wave rings. Values from the
# note palette and the "perfect" palette.
BACKGROUNDS = [
    # base (deep),  glow A,     glow B
    ("1d1630", "ffb300", "9c86e6"),
    ("10242a", "5fc9bf", "ffd98a"),
    ("241a12", "ff8a3d", "ffe36e"),
    ("161c33", "1897fd", "e2a6ff"),
    ("1a2618", "30c05e", "ffd98a"),
    ("2a1424", "ff5eb0", "ffb300"),
    ("12202c", "74cbf0", "9c86e6"),
    ("201a10", "c68e15", "fffaf0"),
]


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], float)


def background(index, base, glow_a, glow_b, size=(2732, 1537)):
    rng = np.random.default_rng(1000 + index)
    w, h = size[0] // 4, size[1] // 4  # smooth art: compute small, scale up
    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    u, v = xx / w, yy / h
    rgb = np.broadcast_to(hex_rgb(base), (h, w, 3)).copy()

    def glow(colour, cx, cy, radius, strength):
        nonlocal rgb
        d = np.hypot((u - cx) * w / h, v - cy) / radius
        a = (np.exp(-d * d) * strength)[..., None]
        rgb = rgb * (1 - a) + hex_rgb(colour) * a

    ax, ay = rng.uniform(0.15, 0.4), rng.uniform(0.2, 0.8)
    bx, by = rng.uniform(0.6, 0.9), rng.uniform(0.2, 0.8)
    glow(glow_a, ax, ay, 0.75, 0.55)
    glow(glow_b, bx, by, 0.65, 0.45)
    # out-of-focus circles (bokeh), soft and few
    for _ in range(14):
        cx, cy = rng.uniform(0, 1), rng.uniform(0, 1)
        rad = rng.uniform(0.03, 0.11)
        col = hex_rgb(glow_a if rng.random() < 0.5 else glow_b)
        d = np.hypot((u - cx) * w / h, v - cy)
        a = (np.clip((rad - d) / (rad * 0.25), 0, 1) * rng.uniform(0.06, 0.16))[..., None]
        rgb = rgb * (1 - a) + col * a
    # faint sound-wave rings around glow A
    d = np.hypot((u - ax) * w / h, v - ay)
    rings = (0.5 + 0.5 * np.cos(d * 2 * np.pi * 7)) ** 12 * np.exp(-d * 1.6) * 0.075 * np.clip(d / 0.06, 0, 1)
    rgb = rgb * (1 - rings[..., None]) + 255 * rings[..., None]
    # gentle vignette keeps the centre (logo) calm
    vig = np.clip(1 - 0.35 * (np.hypot(u - 0.5, (v - 0.5) * h / w) * 1.6) ** 2, 0, 1)[..., None]
    rgb = rgb * (0.65 + 0.35 * vig)

    img = Image.fromarray(rgb.clip(0, 255).astype(np.uint8), "RGB").resize(size, Image.BICUBIC)
    # fine grain against banding
    arr = np.asarray(img, float) + rng.normal(0, 1.2, (size[1], size[0], 1))
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGB")


def main():
    brand = GAME / "brand" / "Textures"
    (brand / "Menu").mkdir(parents=True, exist_ok=True)
    (brand / "Icons").mkdir(parents=True, exist_ok=True)
    (brand / "Intro" / "Triangles").mkdir(parents=True, exist_ok=True)

    logo = menu_logo(opaque_disc=False)
    logo.save(brand / "Menu" / "logo.png", optimize=True)

    icon = menu_logo(opaque_disc=True)
    icon.resize((90, 90), Image.LANCZOS).save(brand / "Icons" / "Logo.png", optimize=True)

    hi, bg = triangles_textures()
    hi.save(brand / "Intro" / "Triangles" / "logo-highlight.png", optimize=True)
    bg.save(brand / "Intro" / "Triangles" / "logo-background.png", optimize=True)

    for i, (base, a, b) in enumerate(BACKGROUNDS, start=1):
        background(i, base, a, b).save(brand / "Menu" / f"menu-background-{i}.jpg", quality=90)

    client = GAME / "client"
    icon.save(client / "assets" / "lazer.png", optimize=True)
    icon.resize((128, 128), Image.LANCZOS).save(client / "assets" / "lazer-nuget.png", optimize=True)
    sizes = [(s, s) for s in (16, 24, 32, 48, 64, 128, 256)]
    icon.save(client / "osu.Desktop" / "lazer.ico", sizes=sizes)
    icon.save(client / "osu.Desktop" / "beatmap.ico", sizes=sizes)
    print("written:", brand, client / "assets", client / "osu.Desktop")


if __name__ == "__main__":
    main()
