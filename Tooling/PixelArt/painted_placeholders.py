"""Beast Craft: placeholders for the painted art slots (docs/art/hollow-art-slots.md).

Usage (from the repo root; the PixelArt venv, Pillow 12.3.0):
    Tooling/PixelArt/.venv/Scripts/python Tooling/PixelArt/painted_placeholders.py

Writes simple, soft, deterministic stand-ins for art the art lane will paint, at the slot's file name,
so the engine support works now and the painted art drops in later as files (build.py merges them into
the manifest from illustrated.json's "Painted" list; a slot's data never names a size in pixels):

  content/art/backdrops/<region>/<arena>.png   battle backdrops: a soft green gradient with low-frequency
                                               noise and a lighter clearing where the board sits (read from
                                               content/data/Vfx/battle-art.json's BoardRect), half the final size

and LOCAL PREVIEWS (git-ignored) under Tooling/PixelArt/preview/:
  backdrop_guide_<region>_<arena>.png          the backdrop with the arena's hex tiles drawn where the engine
                                               will put them (a painting guide for the art lane)

Deterministic: integer hash noise, Pillow's own resampling, no system RNG, no timestamps. Never
overwrites a file that is not a placeholder: a slot whose PNG has no "BeastCraft-Placeholder" text chunk
is left alone (the painted art has landed), unless --force.
"""
import json
import pathlib
import sys

from PIL import Image, ImageDraw, PngImagePlugin

ROOT = pathlib.Path(__file__).resolve().parent
REPO = ROOT.parent.parent
ART = REPO / "content" / "art"
PREVIEW = ROOT / "preview"
BATTLE_ART = REPO / "content" / "data" / "Vfx" / "battle-art.json"
MARK = "BeastCraft-Placeholder"

# HexLayout (src/BeastCraft.Presentation/Board/HexLayout.cs) and HexGrid.DimensionsFor.
TILE_W, TILE_H, COL, ROW = 32, 36, 32, 27
ARENAS = {"Small": (5, 7), "Medium": (8, 11), "Large": (11, 15)}
BACKDROP_SIZE = (720, 1280)          # placeholders at half the final 1440x2560


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


def backdrop(region, arena, rect, seed):
    """A soft green forest floor: dark canopy at the edges, a lighter mossy clearing under the board."""
    w, h = BACKDROP_SIZE
    big = value_noise((w, h), (9, 16), seed)
    fine = value_noise((w, h), (45, 80), seed + 7)
    bx, by, bw, bh = rect["X"] * w, rect["Y"] * h, rect["Width"] * w, rect["Height"] * h
    cx, cy = bx + bw / 2, by + bh / 2
    rx, ry = bw * 0.62, bh * 0.60
    canopy, moss, meadow = (22, 44, 32), (52, 92, 52), (104, 146, 78)
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


def backdrops(force):
    data = json.loads(BATTLE_ART.read_text(encoding="utf-8"))
    for i, b in enumerate(data["Backdrops"]):
        region, arena = b["RegionId"], b["Arena"]
        width, height = ARENAS[arena]
        img = backdrop(region, arena, b["BoardRect"], 4100 + i)
        save(img, ART / "backdrops" / region / f"{arena.lower()}.png", force)
        guide(img, width, height, b["BoardRect"]).save(PREVIEW / f"backdrop_guide_{region}_{arena.lower()}.png")


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


def icon_layers(force):
    save(ring(ICON, 0.82, 0.985, FRAME_COLORS), ART / "ui" / "skill_icon" / "frame.png", force)
    for name, colors in RARITY_COLORS.items():
        save(ring(ICON, 0.86, 0.975, colors, glow=1.0), ART / "ui" / "skill_icon" / f"ring_{name}.png", force)


def main():
    force = "--force" in sys.argv
    PREVIEW.mkdir(exist_ok=True)
    backdrops(force)
    icon_layers(force)


if __name__ == "__main__":
    main()
