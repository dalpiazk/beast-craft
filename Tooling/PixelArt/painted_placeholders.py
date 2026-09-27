"""Beast Craft: placeholders for the painted art slots (docs/art/hollow-art-slots.md).

Usage (from the repo root; the PixelArt venv, Pillow 12.3.0):
    Tooling/PixelArt/.venv/Scripts/python Tooling/PixelArt/painted_placeholders.py

Writes simple, soft, deterministic stand-ins for art the art lane will paint, at the slot's file name,
so the engine support works now and the painted art drops in later as files (build.py merges them into
the manifest from illustrated.json's "Painted" list; a slot's data never names a size in pixels):

  content/art/backdrops/<region>/<id>/<arena>.png  battle backdrops: a soft gradient with low-frequency noise and
                                               a lighter clearing where the board sits (read from
                                               content/data/Vfx/battle-art.json's BoardRect), in the backdrop
                                               family's colours (sun*, ruin*, dusk*, shroom*), with its layout's obstacles
                                               (content/data/Encounters/battle-layouts.json) painted on their hexes
                                               as rocks, stone stumps, mossy boulders or a tree stump under a mushroom cluster; 3/8 of the final size
  content/art/ui/skill_icon/*.png              the skill-icon frame and rarity rings (final size)
  content/art/vfx/<element>/<kind>.png         painted VFX hero frames per element (burst, ring, decal, glyph,
                                               ember) and content/art/vfx/status/<status>.png per status: soft
                                               shapes in the element's or status's colours, half the final size

and LOCAL PREVIEWS (git-ignored) under Tooling/PixelArt/preview/:
  backdrop_guide_<region>_<id>_<arena>.png     the backdrop with the arena's hex tiles drawn where the engine
                                               will put them (a painting guide for the art lane)

Deterministic: integer hash noise, Pillow's own resampling, no system RNG, no timestamps. Never
overwrites a file that is not a placeholder: a slot whose PNG has no "BeastCraft-Placeholder" text chunk
is left alone (the painted art has landed), unless --force.
"""
import json
import math
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFilter, PngImagePlugin

ROOT = pathlib.Path(__file__).resolve().parent
REPO = ROOT.parent.parent
ART = REPO / "content" / "art"
PREVIEW = ROOT / "preview"
BATTLE_ART = REPO / "content" / "data" / "Vfx" / "battle-art.json"
MARK = "BeastCraft-Placeholder"

# HexLayout (src/BeastCraft.Presentation/Board/HexLayout.cs) and HexGrid.DimensionsFor.
TILE_W, TILE_H, COL, ROW = 32, 36, 32, 27
ARENAS = {"Small": (5, 7), "Medium": (8, 11), "Large": (11, 15)}
BACKDROP_SIZE = (540, 960)           # placeholders at 3/8 of the final 1440x2560 (board rects are fractions)
LAYOUTS = REPO / "content" / "data" / "Encounters" / "battle-layouts.json"
# Per backdrop family (by the id's prefix): canopy, floor, clearing colours, and the obstacle look.
FAMILIES = {
    "sun": ((30, 58, 34), (70, 118, 58), (134, 176, 92), "rock"),
    "ruin": ((38, 44, 40), (84, 92, 80), (142, 146, 128), "rubble"),
    "dusk": ((22, 22, 44), (46, 58, 72), (86, 104, 110), "boulder"),
    "shroom": ((26, 40, 22), (62, 84, 44), (112, 132, 72), "mushroom"),
}


def hash32(*values):
    """FNV-1a over the values' 32-bit words (as build.py): deterministic noise."""
    h = 2166136261
    for v in values:
        v &= 0xFFFFFFFF
        for shift in (0, 8, 16, 24):
            h ^= (v >> shift) & 0xFF
            h = (h * 16777619) & 0xFFFFFFFF
    return h


def mix32(*values):
    """hash32 through murmur3's finaliser: FNV alone leaves stripes in a grid of small integers."""
    h = hash32(*values)
    h ^= h >> 16
    h = (h * 0x85EBCA6B) & 0xFFFFFFFF
    h ^= h >> 13
    h = (h * 0xC2B2AE35) & 0xFFFFFFFF
    h ^= h >> 16
    return h


def value_noise(size, cells, seed):
    """A smooth greyscale field: a cells-sized grid of hashed values, bicubic-upscaled to size."""
    cw, ch = cells
    small = Image.new("L", (cw, ch))
    small.putdata([mix32(seed, x, y) % 256 for y in range(ch) for x in range(cw)])
    return small.resize(size, Image.BICUBIC)


def board_bounds(width, height):
    """HexLayout.BoardBounds: the arena's tiles box in board px (tile (0, 0)'s centre the origin)."""
    min_c = -(width // 2)
    max_c = min_c + width - 1
    min_r = -((height - 1) // 2)
    max_r = min_r + height - 1
    left = min_c * COL - TILE_W / 2
    right = max_c * COL + (COL / 2 if height > 1 else 0) + TILE_W / 2
    top = min_r * ROW - TILE_H / 2
    bottom = max_r * ROW + TILE_H / 2
    return left, top, right - left, bottom - top


def tiles(width, height):
    """Every tile's centre in board px (HexGrid's odd-r offset rectangle; HexLayout.Center)."""
    min_c = -(width // 2)
    min_r = -((height - 1) // 2)
    for row in range(min_r, min_r + height):
        for col in range(min_c, min_c + width):
            q = col - (row >> 1)
            yield COL * q + COL / 2 * row, ROW * row


def mix(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def backdrop(region, arena, rect, seed, family="sun"):
    """A soft forest floor in the family's colours: dark canopy at the edges, a lighter clearing under the board."""
    w, h = BACKDROP_SIZE
    big = value_noise((w, h), (9, 16), seed)
    fine = value_noise((w, h), (45, 80), seed + 7)
    bx, by, bw, bh = rect["X"] * w, rect["Y"] * h, rect["Width"] * w, rect["Height"] * h
    cx, cy = bx + bw / 2, by + bh / 2
    rx, ry = bw * 0.62, bh * 0.60
    canopy, moss, meadow = FAMILIES[family][:3]
    big_px, fine_px = big.load(), fine.load()
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(w):
            d = ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2
            clearing = 1 - smooth((d - 0.55) / 0.9)
            n = (big_px[x, y] - 128) / 128.0
            f = (fine_px[x, y] - 128) / 128.0
            base = mix(canopy, moss, smooth(0.35 + 0.35 * n))
            c = mix(base, meadow, clearing * (0.75 + 0.2 * n))
            k = 1 + 0.06 * f
            px[x, y] = tuple(max(0, min(255, round(v * k))) for v in c)
    return img


def guide(img, width, height, rect):
    """The backdrop with the arena's hexes where the engine draws them (image px from board px)."""
    w, h = img.size
    left, top, tw, th = board_bounds(width, height)
    sx, sy = rect["Width"] * w / tw, rect["Height"] * h / th
    ox, oy = rect["X"] * w - left * sx, rect["Y"] * h - top * sy
    out = img.convert("RGBA")
    d = ImageDraw.Draw(out)
    for (x, y) in tiles(width, height):
        pts = [(x, y - TILE_H / 2), (x + TILE_W / 2, y - TILE_H / 4), (x + TILE_W / 2, y + TILE_H / 4),
               (x, y + TILE_H / 2), (x - TILE_W / 2, y + TILE_H / 4), (x - TILE_W / 2, y - TILE_H / 4)]
        d.polygon([(ox + px * sx, oy + py * sy) for px, py in pts], outline=(255, 240, 200, 255))
    d.rectangle([rect["X"] * w, rect["Y"] * h, (rect["X"] + rect["Width"]) * w, (rect["Y"] + rect["Height"]) * h],
                outline=(255, 90, 60, 255))
    return out


def is_placeholder(path):
    if not path.exists():
        return True
    with Image.open(path) as im:
        return MARK in (im.text or {})


def save(img, path, force):
    if not force and not is_placeholder(path):
        print(f"kept    {path.relative_to(REPO)} (painted art)")
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    info = PngImagePlugin.PngInfo()
    info.add_text(MARK, "generated by Tooling/PixelArt/painted_placeholders.py; replace with the painted slot")
    img.save(path, pnginfo=info, optimize=False)
    print(f"wrote   {path.relative_to(REPO)} {img.size[0]}x{img.size[1]}")
    return True


def obstacle(img, cells, width, height, rect, look, seed):
    """Paints each obstacle cell: a grey rock (meadow), a squared stone stump with rubble (ruins) or a dark mossy
    boulder (dusk), sitting on its hex with a soft shadow, so the placeholder shows the blocked hexes."""
    w, h = img.size
    left, top, tw, th = board_bounds(width, height)
    sx, sy = rect["Width"] * w / tw, rect["Height"] * h / th
    ox, oy = rect["X"] * w - left * sx, rect["Y"] * h - top * sy
    d = ImageDraw.Draw(img, "RGBA")
    for i, (q, r) in enumerate(cells):
        x, y = ox + (COL * q + COL / 2 * r) * sx, oy + ROW * r * sy
        rx, ry = TILE_W / 2 * sx * 0.78, TILE_H / 2 * sy * 0.62
        d.ellipse([x - rx, y - ry * 0.35, x + rx, y + ry * 0.75], fill=(0, 0, 0, 90))
        jitter = (mix32(seed, i) % 7) - 3
        if look == "mushroom":
            d.rectangle([x - rx * 0.5, y - ry * 0.4, x + rx * 0.5, y + ry * 0.4], fill=(110, 78, 50, 255), outline=(52, 36, 24, 255), width=2)
            d.ellipse([x - rx * 0.5, y - ry * 0.55, x + rx * 0.5, y - ry * 0.25], fill=(176, 140, 96, 255))
            for k, (mx, my) in enumerate(((-0.7, -0.6), (0.55, -0.8), (0.1, -1.1))):
                cx, cy = x + mx * rx, y + my * ry + jitter
                d.ellipse([cx - rx * 0.32, cy - ry * 0.28, cx + rx * 0.32, cy + ry * 0.12], fill=(196, 72, 58, 255), outline=(90, 30, 26, 255), width=1)
                d.ellipse([cx - rx * 0.1, cy - ry * 0.18, cx - rx * 0.02, cy - ry * 0.1], fill=(250, 236, 220, 255))
        elif look == "rubble":
            d.rectangle([x - rx * 0.55, y - ry * 1.3, x + rx * 0.55, y + ry * 0.35], fill=(150, 148, 136, 255), outline=(70, 66, 60, 255), width=2)
            d.rectangle([x - rx * 0.55, y - ry * 1.3, x + rx * 0.55, y - ry * 1.0], fill=(186, 182, 168, 255))
            d.polygon([(x - rx, y + ry * 0.4), (x - rx * 0.5, y - ry * 0.1 + jitter), (x - rx * 0.2, y + ry * 0.45)], fill=(120, 118, 108, 255))
            d.polygon([(x + rx * 0.3, y + ry * 0.45), (x + rx * 0.7, y + jitter), (x + rx, y + ry * 0.4)], fill=(128, 124, 112, 255))
        else:
            base, light = ((118, 122, 128), (178, 182, 186)) if look == "rock" else ((52, 66, 58), (96, 128, 88))
            d.ellipse([x - rx, y - ry * 1.15 + jitter, x + rx, y + ry * 0.5], fill=base + (255,), outline=(36, 34, 40, 255), width=2)
            d.ellipse([x - rx * 0.6, y - ry * 1.0 + jitter, x + rx * 0.2, y - ry * 0.3], fill=light + (255,))
    return img


def backdrops(force):
    data = json.loads(BATTLE_ART.read_text(encoding="utf-8"))
    layouts = {l["ArtKey"]: l for l in json.loads(LAYOUTS.read_text(encoding="utf-8"))["Layouts"]}
    for i, b in enumerate(data["Backdrops"]):
        region, arena = b["RegionId"], b["Arena"]
        bid = b["ArtKey"].split("/")[2]
        family = next(f for f in FAMILIES if bid.startswith(f))
        width, height = ARENAS[arena]
        img = backdrop(region, arena, b["BoardRect"], 4100 + i, family)
        layout = layouts.get(b["ArtKey"])
        if layout is not None:
            cells = [(c["Q"], c["R"]) for c in layout["Cells"]]
            obstacle(img, cells, width, height, b["BoardRect"], FAMILIES[family][3], 6100 + i)
        save(img, ART / "backdrops" / region / bid / f"{arena.lower()}.png", force)
        guide(img, width, height, b["BoardRect"]).save(PREVIEW / f"backdrop_guide_{region}_{bid}_{arena.lower()}.png")


ICON = 256                            # skill-icon layers: frame and rarity rings share one square canvas
RARITY_COLORS = {                     # (light, mid, dark)
    "common": ((236, 232, 222), (184, 178, 168), (104, 98, 92)),
    "rare": ((170, 220, 255), (70, 150, 226), (26, 70, 140)),
    "epic": ((226, 186, 255), (160, 100, 220), (84, 40, 140)),
    "legendary": ((255, 240, 170), (242, 190, 70), (150, 96, 20)),
    "gloam": ((214, 176, 250), (122, 75, 166), (58, 36, 88)),
}
FRAME_COLORS = ((238, 214, 164), (185, 140, 88), (96, 64, 40))


def ring(size, inner, outer, colors, glow=0.0, samples=4):
    """A soft round band from inner to outer (fractions of the radius): lit upper-left, shaded lower-right,
    anti-aliased by supersampling; with glow, a faint halo fading past the outer edge."""
    light, mid, dark = colors
    img = Image.new("RGBA", (size, size))
    px = img.load()
    half = size / 2
    for y in range(size):
        for x in range(size):
            cover = 0.0
            for sy in range(samples):
                for sx in range(samples):
                    dx = (x + (sx + 0.5) / samples - half) / half
                    dy = (y + (sy + 0.5) / samples - half) / half
                    r = (dx * dx + dy * dy) ** 0.5
                    if inner <= r <= outer:
                        cover += 1
            cover /= samples * samples
            dx, dy = (x + 0.5 - half) / half, (y + 0.5 - half) / half
            r = (dx * dx + dy * dy) ** 0.5
            halo = glow * max(0.0, 1 - (r - outer) / 0.08) if r > outer else 0.0
            a = max(cover, halo * 0.6)
            if a <= 0:
                continue
            across = (r - inner) / max(1e-6, outer - inner)          # 0 inner edge .. 1 outer edge
            lit = smooth(0.5 - 0.45 * (dx + dy) / max(1e-6, r))       # upper-left light
            bevel = 1 - abs(across - 0.45) * 1.6
            c = mix(mix(dark, mid, lit), light, max(0.0, bevel) * lit * 0.8)
            px[x, y] = c + (round(255 * min(1.0, a)),)
    return img


# The painted VFX hero frames: per element, and per status (docs/art/hollow-art-slots.md). Placeholders at
# half the final size; the manifest sizes them by WorldWidth, so the final art drops in at any resolution.
ELEMENTS = {                          # (glow, mid, deep)
    "fire": ((255, 226, 150), (242, 110, 40), (140, 30, 30)),
    "water": ((200, 236, 255), (60, 150, 220), (30, 60, 140)),
    "earth": ((240, 214, 160), (180, 130, 80), (90, 60, 40)),
    "air": ((226, 255, 244), (110, 200, 180), (40, 110, 100)),
    "lightning": ((255, 250, 190), (255, 200, 60), (180, 110, 30)),
    "ice": ((236, 250, 255), (150, 220, 245), (70, 140, 200)),
    "nature": ((226, 255, 170), (120, 200, 80), (40, 100, 50)),
    "metal": ((240, 240, 248), (170, 170, 190), (90, 90, 110)),
    "light": ((255, 255, 230), (255, 230, 150), (200, 160, 80)),
    "dark": ((220, 180, 255), (130, 80, 190), (50, 30, 80)),
}
STATUSES = {
    "stun": (255, 230, 90), "shield": (140, 210, 255), "burn": (255, 130, 50), "poison": (140, 220, 90),
    "taunt": (240, 90, 70), "cleanse": (250, 250, 255), "heal": (150, 240, 130), "buff": (130, 230, 170),
    "debuff": (190, 120, 240),
}
VFX_SIZES = {"burst": 256, "ring": 256, "decal": 256, "glyph": 128, "ember": 64, "status": 256}


def soft(size, draw_fn, blur, supersample=2):
    """A greyscale coverage mask drawn by draw_fn(draw, s) on a supersampled canvas of side s, downsampled and blurred."""
    big = size * supersample
    mask = Image.new("L", (big, big), 0)
    draw_fn(ImageDraw.Draw(mask), big)
    mask = mask.resize((size, size), Image.LANCZOS)
    return mask.filter(ImageFilter.GaussianBlur(blur)) if blur > 0 else mask


def colorize(mask, color, core=None, core_mask=None):
    """RGBA: color at the mask's alpha; with core, a brighter colour where core_mask is (a hot centre)."""
    img = Image.new("RGBA", mask.size, color + (0,))
    img.putalpha(mask)
    if core is not None:
        hot = Image.new("RGBA", mask.size, core + (0,))
        hot.putalpha(core_mask)
        img = Image.alpha_composite(img, hot)
    return img


def disc(cx, cy, r):
    return [cx - r, cy - r, cx + r, cy + r]


def burst_frame(colors, size):
    glow, mid, _ = colors

    def rays(d, s):
        c = s / 2
        for i in range(10):
            a = i * math.pi * 2 / 10 + (0.12 if i % 2 else 0)
            long = s * (0.47 if i % 2 == 0 else 0.34)
            w = 0.16
            d.polygon([(c + math.cos(a - w) * s * 0.08, c + math.sin(a - w) * s * 0.08), (c + math.cos(a) * long, c + math.sin(a) * long),
                       (c + math.cos(a + w) * s * 0.08, c + math.sin(a + w) * s * 0.08)], fill=255)
        d.ellipse(disc(c, c, s * 0.2), fill=255)

    body = soft(size, rays, size * 0.02)
    core = soft(size, lambda d, s: d.ellipse(disc(s / 2, s / 2, s * 0.14), fill=255), size * 0.05)
    return colorize(body, mid, glow, core)


def ring_frame(colors, size):
    glow, mid, _ = colors

    def band(d, s):
        c = s / 2
        d.ellipse(disc(c, c, s * 0.46), fill=255)
        d.ellipse(disc(c, c, s * 0.38), fill=0)

    body = soft(size, band, size * 0.015)
    inner = soft(size, lambda d, s: (d.ellipse(disc(s / 2, s / 2, s * 0.43), fill=255), d.ellipse(disc(s / 2, s / 2, s * 0.41), fill=0)), size * 0.01)
    return colorize(body, mid, glow, inner)


def decal_frame(colors, size, seed):
    _, mid, deep = colors

    def blob(d, s):
        c = s / 2
        pts = []
        for i in range(24):
            a = i * math.pi * 2 / 24
            r = s * (0.36 + 0.08 * ((mix32(seed, i) % 1000) / 1000.0))
            pts.append((c + math.cos(a) * r, c + math.sin(a) * r * 0.9))
        d.polygon(pts, fill=205)
        d.ellipse(disc(c, c, s * 0.2), fill=235)

    body = soft(size, blob, size * 0.03)
    return colorize(body, mix(deep, mid, 0.25))


def glyph_frame(colors, size):
    glow, mid, _ = colors

    def rune(d, s):
        c = s / 2
        w = max(2, int(s * 0.06))
        d.ellipse(disc(c, c, s * 0.4), outline=255, width=w)
        d.polygon([(c, c - s * 0.26), (c + s * 0.23, c + s * 0.14), (c - s * 0.23, c + s * 0.14)], outline=255, width=w)
        d.line([(c, c - s * 0.26), (c, c + s * 0.14)], fill=255, width=w)

    body = soft(size, rune, size * 0.012)
    halo = soft(size, rune, size * 0.05)
    return colorize(halo, mid, glow, body)


def ember_frame(colors, size):
    glow, mid, _ = colors
    body = soft(size, lambda d, s: d.ellipse(disc(s / 2, s / 2, s * 0.3), fill=255), size * 0.1)
    core = soft(size, lambda d, s: d.ellipse(disc(s / 2, s / 2, s * 0.12), fill=255), size * 0.05)
    return colorize(body, mid, glow, core)


def status_frame(name, color, size):
    """A soft symbol per status: the placeholder reads at a glance, the painting replaces it."""
    white = (255, 255, 255)

    def symbol(d, s):
        c = s / 2
        w = max(2, int(s * 0.07))
        if name == "stun":
            for i in range(5):
                a = i * math.pi * 2 / 5 - math.pi / 2
                x, y = c + math.cos(a) * s * 0.3, c + math.sin(a) * s * 0.14
                star = [(x + math.cos(b * math.pi / 5 - math.pi / 2) * s * (0.07 if b % 2 == 0 else 0.03),
                         y + math.sin(b * math.pi / 5 - math.pi / 2) * s * (0.07 if b % 2 == 0 else 0.03)) for b in range(10)]
                d.polygon(star, fill=255)
        elif name == "shield":
            pts = [(c + math.cos(math.pi / 6 + i * math.pi / 3) * s * 0.4, c + math.sin(math.pi / 6 + i * math.pi / 3) * s * 0.4) for i in range(6)]
            d.polygon(pts, outline=255, width=w)
            d.polygon([(x * 0.85 + c * 0.15, y * 0.85 + c * 0.15) for x, y in pts], fill=90)
        elif name == "burn":
            d.polygon([(c, c - s * 0.4), (c + s * 0.22, c + s * 0.05), (c + s * 0.16, c + s * 0.3), (c - s * 0.16, c + s * 0.3), (c - s * 0.22, c + s * 0.05)], fill=255)
            d.ellipse(disc(c, c + s * 0.14, s * 0.2), fill=255)
        elif name == "poison":
            for (x, y, r) in ((0.42, 0.58, 0.16), (0.62, 0.42, 0.11), (0.6, 0.66, 0.08)):
                d.ellipse(disc(s * x, s * y, s * r), outline=255, width=w)
        elif name == "taunt":
            for dx in (-0.12, 0.12):
                d.rectangle([c + s * dx - s * 0.04, c - s * 0.32, c + s * dx + s * 0.04, c + s * 0.1], fill=255)
                d.ellipse(disc(c + s * dx, c + s * 0.24, s * 0.05), fill=255)
        elif name == "cleanse":
            d.polygon([(c, c - s * 0.42), (c + s * 0.08, c - s * 0.08), (c + s * 0.42, c), (c + s * 0.08, c + s * 0.08), (c, c + s * 0.42),
                       (c - s * 0.08, c + s * 0.08), (c - s * 0.42, c), (c - s * 0.08, c - s * 0.08)], fill=255)
        elif name == "heal":
            d.rectangle([c - s * 0.09, c - s * 0.34, c + s * 0.09, c + s * 0.34], fill=255)
            d.rectangle([c - s * 0.34, c - s * 0.09, c + s * 0.34, c + s * 0.09], fill=255)
        elif name in ("buff", "debuff"):
            k = -1 if name == "buff" else 1
            d.polygon([(c, c + k * s * 0.38), (c + s * 0.3, c + k * s * 0.02), (c + s * 0.12, c + k * s * 0.02), (c + s * 0.12, c - k * s * 0.34),
                       (c - s * 0.12, c - k * s * 0.34), (c - s * 0.12, c + k * s * 0.02), (c - s * 0.3, c + k * s * 0.02)], fill=255)

    halo = soft(size, symbol, size * 0.045)
    body = soft(size, symbol, size * 0.008)
    glow = soft(size, lambda d, s: d.ellipse(disc(s / 2, s / 2, s * 0.44), fill=70), size * 0.08)
    base = colorize(glow, color)
    return Image.alpha_composite(base, colorize(halo, color, mix(color, white, 0.6), body))


def vfx_frames(force):
    for i, (element, colors) in enumerate(ELEMENTS.items()):
        folder = ART / "vfx" / element
        save(burst_frame(colors, VFX_SIZES["burst"]), folder / "burst.png", force)
        save(ring_frame(colors, VFX_SIZES["ring"]), folder / "ring.png", force)
        save(decal_frame(colors, VFX_SIZES["decal"], 5200 + i), folder / "decal.png", force)
        save(glyph_frame(colors, VFX_SIZES["glyph"]), folder / "glyph.png", force)
        save(ember_frame(colors, VFX_SIZES["ember"]), folder / "ember.png", force)
    for name, color in STATUSES.items():
        save(status_frame(name, color, VFX_SIZES["status"]), ART / "vfx" / "status" / f"{name}.png", force)


def icon_layers(force):
    save(ring(ICON, 0.82, 0.985, FRAME_COLORS), ART / "ui" / "skill_icon" / "frame.png", force)
    for name, colors in RARITY_COLORS.items():
        save(ring(ICON, 0.86, 0.975, colors, glow=1.0), ART / "ui" / "skill_icon" / f"ring_{name}.png", force)


def main():
    force = "--force" in sys.argv
    PREVIEW.mkdir(exist_ok=True)
    backdrops(force)
    icon_layers(force)
    vfx_frames(force)


if __name__ == "__main__":
    main()
