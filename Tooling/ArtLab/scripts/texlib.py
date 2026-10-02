"""Procedural painted-journal texture helpers (Pillow + numpy only, no network, no models). Shared by
the journal/settings UI mocks (menu_backdrops/grade.py, and formerly the settings_a/b/c/d.py mocks,
scratch-only) and by Tooling/UiKit/build_kit.py, which builds the shipped kit textures
(content/art/ui/kit/) from these same generators.
"""
import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops, ImageOps
import math, random

# ---- palette (art direction v2, issue #49) ----
INK_PLUM   = (46, 42, 69)      # #2E2A45
PAPER      = (246, 238, 220)   # #F6EEDC
SAGE       = (143, 174, 139)   # #8FAE8B
SKY_TEAL   = (111, 179, 184)   # #6FB3B8
APRICOT    = (242, 169, 104)   # #F2A968
COOL_SHADOW= (124, 122, 174)   # #7C7AAE
# no pure black anywhere; darkest tone is ink plum or a mix toward it
NEAR_BLACK = (40, 36, 58)

# Repo-relative, not a personal path: this file lives at Tooling/ArtLab/scripts/texlib.py, so three
# parents up is the repository root.
FONT_PATH = str(pathlib.Path(__file__).resolve().parents[3] / "content" / "fonts" / "Fredoka-SemiBold.ttf")

def font(size):
    return ImageFont.truetype(FONT_PATH, size)

def rng(seed):
    return np.random.default_rng(seed)

# ---------------------------------------------------------------- noise/paper

def value_noise(w, h, cell, seed, octaves=1):
    """Smooth low-frequency noise upsampled with bicubic, 0..1 float array."""
    r = rng(seed)
    out = np.zeros((h, w), dtype=np.float32)
    amp = 1.0
    total = 0.0
    c = cell
    for o in range(octaves):
        gw, gh = max(2, w // c + 2), max(2, h // c + 2)
        grid = r.random((gh, gw)).astype(np.float32)
        im = Image.fromarray((grid * 255).astype(np.uint8), "L").resize((w, h), Image.BICUBIC)
        out += (np.asarray(im, dtype=np.float32) / 255.0) * amp
        total += amp
        amp *= 0.5
        c = max(2, c // 2)
    return out / total

def paper_texture(w, h, base_rgb, seed=1, mottle_strength=14, grain_strength=10):
    """Warm painted-paper base: colour fill + big soft mottling + fine grain."""
    base = np.array(base_rgb, dtype=np.float32)
    img = np.tile(base, (h, w, 1))
    mottle = value_noise(w, h, 90, seed, octaves=2)
    mottle = (mottle - 0.5) * 2 * mottle_strength
    grain = value_noise(w, h, 3, seed + 7, octaves=1)
    grain = (grain - 0.5) * 2 * grain_strength
    fiber = (rng(seed + 3).random((h, w)).astype(np.float32) - 0.5) * 6
    img = img + mottle[..., None] + grain[..., None] + fiber[..., None]
    img = np.clip(img, 0, 255).astype(np.uint8)
    out = Image.fromarray(img, "RGB")
    return out

def watercolor_wash(w, h, color, seed, blobs=5, alpha=46, spread=0.5):
    """A soft, pooled watercolour wash layer (RGBA) to multiply/screen over paper."""
    layer = Image.new("L", (w, h), 0)
    r = rng(seed)
    for i in range(blobs):
        cx = int(r.integers(int(w * 0.05), int(w * 0.95)))
        cy = int(r.integers(int(h * 0.05), int(h * 0.95)))
        rad = int(r.integers(int(min(w, h) * 0.15 * spread), int(min(w, h) * 0.4 * spread) + 2))
        blob = Image.new("L", (w, h), 0)
        bd = ImageDraw.Draw(blob)
        pts = []
        n = 14
        for k in range(n):
            ang = 2 * math.pi * k / n
            rr = rad * (0.7 + 0.3 * r.random())
            pts.append((cx + rr * math.cos(ang), cy + rr * math.sin(ang)))
        bd.polygon(pts, fill=255)
        blob = blob.filter(ImageFilter.GaussianBlur(rad * 0.25))
        layer = ImageChops.lighter(layer, blob)
    layer = layer.filter(ImageFilter.GaussianBlur(4))
    arr = (np.asarray(layer, dtype=np.float32) / 255.0) * alpha
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., 0] = color[0]
    rgba[..., 1] = color[1]
    rgba[..., 2] = color[2]
    rgba[..., 3] = arr.astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")

def deckled_rect_mask(w, h, jitter=6, seed=1, margin=0):
    """Mask for a rectangle with a torn/deckled edge."""
    r = rng(seed)
    mask = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(mask)
    pts = []
    step = 14
    x0, y0, x1, y1 = margin, margin, w - margin, h - margin
    # top
    for x in range(x0, x1 + 1, step):
        pts.append((x, y0 + r.normal(0, jitter)))
    # right
    for y in range(y0, y1 + 1, step):
        pts.append((x1 + r.normal(0, jitter), y))
    # bottom
    for x in range(x1, x0 - 1, -step):
        pts.append((x, y1 + r.normal(0, jitter)))
    # left
    for y in range(y1, y0 - 1, -step):
        pts.append((x0 + r.normal(0, jitter), y))
    d.polygon(pts, fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(1.2))
    return mask

def rounded_mask(w, h, radius):
    m = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([0, 0, w - 1, h - 1], radius=radius, fill=255)
    return m

def jitter_points(p0, p1, amp, n, seed):
    r = rng(seed)
    x0, y0 = p0; x1, y1 = p1
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length, dx / length
    pts = []
    for i in range(n + 1):
        t = i / n
        bx, by = x0 + dx * t, y0 + dy * t
        off = amp * math.sin(t * math.pi) * 0.4 + r.normal(0, amp * 0.5)
        pts.append((bx + nx * off, by + ny * off))
    return pts

def ink_line(draw, p0, p1, fill, width=3, amp=1.6, seed=1, n=10):
    pts = jitter_points(p0, p1, amp, n, seed)
    draw.line(pts, fill=fill, width=width, joint="curve")
    r = max(1, width // 2)
    draw.ellipse([pts[0][0]-r, pts[0][1]-r, pts[0][0]+r, pts[0][1]+r], fill=fill)
    draw.ellipse([pts[-1][0]-r, pts[-1][1]-r, pts[-1][0]+r, pts[-1][1]+r], fill=fill)

def stitch_line(draw, p0, p1, color, dash=16, gap=10, width=3, seed=1):
    x0, y0 = p0; x1, y1 = p1
    length = math.hypot(x1 - x0, y1 - y0)
    n = max(1, int(length // (dash + gap)))
    r = rng(seed)
    ux, uy = (x1 - x0) / length, (y1 - y0) / length
    nx, ny = -uy, ux
    pos = 0
    for i in range(n + 1):
        jig = r.normal(0, 1.1)
        sx, sy = x0 + ux * pos + nx * jig, y0 + uy * pos + ny * jig
        ex_, ey_ = x0 + ux * min(pos + dash, length) + nx * jig, y0 + uy * min(pos + dash, length) + ny * jig
        draw.line([(sx, sy), (ex_, ey_)], fill=color, width=width)
        pos += dash + gap

def soft_shadow(base_img, mask, offset=(0, 10), blur=14, opacity=70, color=INK_PLUM):
    w, h = base_img.size
    shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    solid = Image.new("RGBA", (w, h), color + (255,))
    shadow.paste(solid, offset, mask)
    shadow = shadow.filter(ImageFilter.GaussianBlur(blur))
    a = shadow.split()[3].point(lambda v: int(v * opacity / 255))
    shadow.putalpha(a)
    base_img.alpha_composite(shadow)

def paste_rounded(base, patch, pos, radius):
    w, h = patch.size
    m = rounded_mask(w, h, radius)
    base.paste(patch, pos, m)
    return m

def text_w(draw, text, f):
    bbox = draw.textbbox((0, 0), text, font=f)
    return bbox[2] - bbox[0], bbox[3] - bbox[1], bbox[1]

def draw_text_painted(img_rgba, pos, text, f, fill, ink_shadow=None, jitter_seed=None):
    """Draw text with a faint colour-matched ink halo underneath for a painted (not vector) feel."""
    draw = ImageDraw.Draw(img_rgba)
    x, y = pos
    if ink_shadow:
        halo = Image.new("RGBA", img_rgba.size, (0, 0, 0, 0))
        hd = ImageDraw.Draw(halo)
        hd.text((x, y + 2), text, font=f, fill=ink_shadow + (90,))
        halo = halo.filter(ImageFilter.GaussianBlur(2.2))
        img_rgba.alpha_composite(halo)
    if jitter_seed is not None:
        r = rng(jitter_seed)
        cx = x
        for ch in text:
            dy = r.normal(0, 1.1)
            draw.text((cx, y + dy), ch, font=f, fill=fill)
            cx += draw.textlength(ch, font=f)
    else:
        draw.text((x, y), text, font=f, fill=fill)

def mix(c1, c2, t):
    return tuple(int(c1[i] * (1 - t) + c2[i] * t) for i in range(3))

def paint_fill_rounded(w, h, radius, base_color, seed, wash_color=None, grain=10, mottle=10):
    base = paper_texture(w, h, base_color, seed=seed, mottle_strength=mottle, grain_strength=grain)
    base = base.convert("RGBA")
    if wash_color:
        wash = watercolor_wash(w, h, wash_color, seed + 11, blobs=3, alpha=30, spread=0.6)
        base.alpha_composite(wash)
    mask = rounded_mask(w, h, radius)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(base, (0, 0), mask)
    return out, mask
