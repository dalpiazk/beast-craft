"""Beast Craft pixel art: text-grid sprites -> game PNGs + manifest (+ local previews).

Usage (from the repo root; Pillow 12.3.0, see requirements.txt):
    python Tooling/PixelArt/build.py

Reads palette.json and sprites/*.txt and writes the GAME ASSETS (committed):
  content/art/pixel/<name>.png                             1x native; multi-frame = horizontal strip
  content/art/pixel/pixel-art-manifest.json
                                                        every sprite (file, frame size, frames,
                                                        frame ms, kind, ArtKey) + the palette
The manifest is schema v2 (BeastCraft.Vfx.ArtManifestData): per sprite its frame size, pivot,
PixelsPerUnit (32: one hex column step), Filter "point" and Premultiplied false (straight-alpha
PNGs), plus optional Tint and Animations.

Illustrated sprites (Filter "linear") are not built here. Their PNGs are exported from the masters by
Tooling/ArtLab/scripts/export_ingame.py into content/art/beasts/, and the hand-kept illustrated.json
lists them (name, file, ArtKey, feet pivot, WorldHeight). build.py merges those entries into the
same manifest, sorted by name with the rest: it reads each PNG's size and the top row of its art and
derives PixelsPerUnit = (PivotY - top row) / WorldHeight, so a species' in-game height is one number
in illustrated.json and a rebuild. It never writes those PNGs, so the rebuild stays byte-for-byte.

and LOCAL PREVIEWS (git-ignored) under Tooling/PixelArt/preview/:
  <name>_x8.png, <name>.gif (multi-frame), <name>_tiled_x4.png (tiles), map_mock_x4.png,
  contact_sheet.png

Everything is deterministic: the same sources and Pillow version regenerate the game assets byte
for byte (integer-only geometry, no system RNG, no timestamps, LF-only JSON).

Sprite file format (see STYLE.md):
  # name: phoenix              header lines start with '#', "key: value"
  # label: Phoenix (Fire)
  # kind: beast | enemy | item | tile | marker | particle | fx | hex | icon | ui
  # size: 32x32
  # outline: auto | none       auto = 1px K outline around the silhouette (4-neighbour)
  # autoshade: yes | no        yes = rim-light/shade the *mid* colour of each ramp
  # noshade: yh                chars autoshade must leave alone (optional)
  # nooutline: Y               chars that never get an outline: glows, haze (optional)
  # frame_ms: 70               frame duration of a multi-frame sprite (optional)
  # artkey: beast/phoenix         the key game data names this art by (species/enemy ArtKey)
  # pivot: 16,24                 anchor in pixels from the top-left (optional; default: a beast or
                                 enemy's feet, (w/2, 3h/4), which stand on the tile centre;
                                 anything else, its centre)
  # clip: idle beast_phoenix_idle
                                 a named animation clip: every frame of that built strip, at its
                                 frame_ms (optional; repeatable as clip2:, clip3:, ...)
  <grid rows, one char per pixel; short rows are padded with '.'>
  ---                          frame separator (optional)

Alias sprites (no grid rows, no PNG): a placeholder that reuses another sprite's PNG under its
own name and ArtKey, told apart by a tint (an enemy with no art of its own yet):
  # alias: enemy_brute_gloamed   the built sprite whose PNG it reuses
  # tint: l                      palette char the viewer multiplies it by (manifest: #rrggbb)

Generated kinds (no grid rows; the header drives an integer-only generator):
  kind: fx      generator: burst, frames: N, ramp: <chars hot -> cold>, seed: <int>
                an expanding, hollowing flame ring with ragged edges and late embers.
  kind: fx      generator: ring, ramp: <chars outer -> inner>, thickness: <px>
                a circle (or, for a non-square size, an ellipse) outline: shockwaves, ground auras.
  kind: fx      generator: disc, ramp: <chars centre -> edge>
                a filled disc in bands, its last band dithered: glows.
  kind: fx      generator: blob, ramp: <chars centre -> edge>, seed: <int>
                a ragged filled disc with a few specks: ground decals (scorch, frost, ooze).
  These are drawn white/grey so the VFX data can tint them (tints multiply).
  kind: hex     a pointy-top hex (size 32x36; rows step 27 px, columns 32 px):
                texture: <tile sprite>   fill the hex by tiling that sprite, or
                fill: <char>             fill it with one colour, and/or
                edge: <char>             draw its 1px rim in that colour.

Skill icons (no sprite files): one 24x24 placeholder per skill, avatar active and passive in
content/data/Skills/skill-library.json that names an ArtKey (skill/<id>): a disc in its element's
colours (avatar actives lilac, passives peach) with the skill's initial in a 5x7 pixel font,
named skill_<id>, category skill. Regenerated from the library on every build, so a new skill
gets its icon by running this script.
"""
import json
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parent
REPO = ROOT.parent.parent
OUT = REPO / "content" / "art" / "pixel"
PREVIEW = ROOT / "preview"
MANIFEST = "pixel-art-manifest.json"
ILLUSTRATED = ROOT / "illustrated.json"
TRANSPARENT = "."
OUTLINE = "K"


def load_palette():
    data = json.loads((ROOT / "palette.json").read_text(encoding="utf-8"))
    colors = {}
    for ch, hexv in data["colors"].items():
        if hexv is None:
            colors[ch] = (0, 0, 0, 0)
        else:
            h = hexv.lstrip("#")
            colors[ch] = (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)
    shade = {}
    for dark, mid, light in data["ramps"]:
        shade.setdefault(mid, (dark, light))  # first ramp that names a mid wins
    return data, colors, shade


def parse_sprite(path, colors):
    meta, frames, cur = {}, [], []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.rstrip()
        if line.startswith("#"):
            if ":" in line:
                k, v = line[1:].split(":", 1)
                meta[k.strip()] = v.strip()
            continue
        if line.strip() == "---":
            frames.append(cur)
            cur = []
            continue
        if line == "" and not cur:
            continue
        cur.append(line)
    if cur:
        frames.append(cur)
    w, h = (int(x) for x in meta.get("size", "32x32").split("x"))
    out = []
    for fi, rows in enumerate(frames):
        while rows and rows[-1] == "":
            rows.pop()
        if len(rows) > h:
            sys.exit(f"{path.name} frame {fi}: {len(rows)} rows > {h}")
        grid = []
        for ri, row in enumerate(rows):
            if len(row) > w:
                sys.exit(f"{path.name} frame {fi} row {ri}: {len(row)} cols > {w}: {row!r}")
            for ch in row:
                if ch not in colors:
                    sys.exit(f"{path.name} frame {fi} row {ri}: unknown colour {ch!r}")
            grid.append(list(row.ljust(w, TRANSPARENT)))
        while len(grid) < h:
            grid.append([TRANSPARENT] * w)
        out.append(grid)
    meta.setdefault("name", path.stem)
    meta.setdefault("label", meta["name"])
    meta.setdefault("kind", "beast")
    for key in ("fill", "edge"):
        if key in meta and meta[key] not in colors:
            sys.exit(f"{path.name}: unknown {key} colour {meta[key]!r}")
    for ch in meta.get("ramp", "").split():
        if ch not in colors:
            sys.exit(f"{path.name}: unknown ramp colour {ch!r}")
    return meta, (w, h), out


def autoshade(grid, shade):
    """Rim light on edges facing the top-left light, core shadow on edges facing away.
    Only touches the MID colour of a ramp, so hand-placed darks/lights always win."""
    h, w = len(grid), len(grid[0])

    def empty(x, y):
        return not (0 <= x < w and 0 <= y < h) or grid[y][x] in (TRANSPARENT, OUTLINE)

    res = [row[:] for row in grid]
    for y in range(h):
        for x in range(w):
            ch = grid[y][x]
            if ch not in shade:
                continue
            dark, light = shade[ch]
            if empty(x, y - 1) or empty(x - 1, y):
                res[y][x] = light
            elif empty(x, y + 1) or empty(x + 1, y):
                res[y][x] = dark
    return res


def outline(grid, authored, glow=frozenset()):
    """1px K outline on transparent pixels that touch the silhouette (4-neighbour).
    Pixels authored as a `glow` char (light, haze, sparks) do not cast an outline."""
    h, w = len(grid), len(grid[0])
    res = [row[:] for row in grid]
    for y in range(h):
        for x in range(w):
            if grid[y][x] != TRANSPARENT:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and authored[ny][nx] not in glow and grid[ny][nx] != TRANSPARENT:
                    res[y][x] = OUTLINE
                    break
    return res


# ---------------------------------------------------------------------------------------------
# Generators (integer-only, so every platform draws the same pixels)
# ---------------------------------------------------------------------------------------------

def hash32(*values):
    """A small integer hash (FNV-1a over the values' 32-bit words): deterministic noise."""
    h = 2166136261
    for v in values:
        v &= 0xFFFFFFFF
        for shift in (0, 8, 16, 24):
            h ^= (v >> shift) & 0xFF
            h = (h * 16777619) & 0xFFFFFFFF
    return h


def burst_frames(meta, w, h):
    """An expanding flame ring: a hot core that grows, hollows and cools, with ragged edges and
    embers in the late frames. All distances are in half-pixels, squared, so no sqrt or trig."""
    n = int(meta.get("frames", "8"))
    ramp = meta["ramp"].split()
    seed = int(meta.get("seed", "1"))
    levels = len(ramp)
    max_r = min(w, h) - 2          # outer radius at the last frame, half-pixels
    frames = []
    for f in range(n):
        ease_num = f * (2 * (n - 1) - f)          # ease-out quadratic, 0 .. (n-1)^2
        ease_den = (n - 1) * (n - 1)
        outer = 4 + (max_r - 4) * ease_num // ease_den
        inner = 0 if f < 2 else outer * (f - 1) // n
        heat = f * (levels - 1) // (n - 1)       # how far down the ramp the whole frame has cooled
        grid = [[TRANSPARENT] * w for _ in range(h)]
        for y in range(h):
            for x in range(w):
                dx, dy = 2 * x + 1 - w, 2 * y + 1 - h
                d2 = dx * dx + dy * dy
                jitter = (hash32(seed, f, x, y) % 5) - 2        # -2 .. +2 half-pixels
                r_out = outer + jitter
                if r_out < 1:
                    continue
                if d2 <= r_out * r_out and d2 >= inner * inner:
                    band = 0
                    for j in range(1, levels):
                        edge = inner + (r_out - inner) * j // levels
                        if d2 > edge * edge:
                            band = j
                    ci = min(levels - 1, heat + band // 2)
                    grid[y][x] = ramp[ci]
                elif f >= n // 2 and r_out * r_out < d2 <= (r_out + 6) * (r_out + 6) and hash32(seed, f, y, x, 7) % 17 == 0:
                    grid[y][x] = ramp[min(levels - 1, heat)]     # a drifting ember
        frames.append(grid)
    return frames


def ellipse_d2(x, y, w, h):
    """Squared distance of pixel (x, y) from the centre, in doubled units scaled so the ellipse
    through the frame's edges is d2 == w * w (a circle when w == h). Integer only."""
    dx, dy = 2 * x + 1 - w, 2 * y + 1 - h
    return dx * dx + (dy * w // h) * (dy * w // h)


def ring_frames(meta, w, h):
    ramp = meta["ramp"].split()
    thick = int(meta.get("thickness", "2"))
    outer = w - 1
    inner = max(0, outer - 2 * thick * len(ramp))
    grid = [[TRANSPARENT] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            d2 = ellipse_d2(x, y, w, h)
            if inner * inner < d2 <= outer * outer:
                band = 0
                for j in range(1, len(ramp)):
                    edge = outer - 2 * thick * j
                    if d2 <= edge * edge:
                        band = j
                grid[y][x] = ramp[band]
    return [grid]


def disc_frames(meta, w, h):
    ramp = meta["ramp"].split()
    outer = w - 1
    grid = [[TRANSPARENT] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            d2 = ellipse_d2(x, y, w, h)
            if d2 > outer * outer:
                continue
            band = 0
            for j in range(1, len(ramp)):
                edge = outer * j // len(ramp)
                if d2 > edge * edge:
                    band = j
            if band == len(ramp) - 1 and (x + y) % 2 == 1:
                continue  # dither the outer band
            grid[y][x] = ramp[band]
    return [grid]


def blob_frames(meta, w, h):
    ramp = meta["ramp"].split()
    seed = int(meta.get("seed", "1"))
    speck = meta.get("speck")
    outer = w - 3
    grid = [[TRANSPARENT] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            d2 = ellipse_d2(x, y, w, h)
            jitter = (hash32(seed, x // 3, y // 3) % 7) - 3        # coarse ragged edge, -3 .. +3
            r = outer + 2 * jitter
            if d2 > r * r:
                continue
            band = 0
            for j in range(1, len(ramp)):
                edge = r * j // len(ramp)
                if d2 > edge * edge:
                    band = j
            grid[y][x] = ramp[band]
            if speck and hash32(seed, x, y, 5) % 23 == 0:
                grid[y][x] = speck
    return [grid]


def hex_inside(x, y, w, h):
    """Pointy-top hex through the pixel centres: |dx| <= w/2 and |dy| <= h/2 - |dx| * (h/4) / (w/2)."""
    dx, dy = abs(2 * x + 1 - w), abs(2 * y + 1 - h)
    # In doubled units: dy <= h - dx * h / (2 w)  <=>  2 w dy + h dx <= 2 w h
    return dx <= w and 2 * w * dy + h * dx <= 2 * w * h


def hex_frame(meta, w, h, built):
    texture = None
    if "texture" in meta:
        if meta["texture"] not in built:
            sys.exit(f"{meta['name']}: texture {meta['texture']!r} is not a built sprite")
        texture = built[meta["texture"]]
    fill, edge = meta.get("fill"), meta.get("edge")
    grid = [[TRANSPARENT] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if not hex_inside(x, y, w, h):
                continue
            rim = any(not (0 <= x + dx < w and 0 <= y + dy < h) or not hex_inside(x + dx, y + dy, w, h)
                      for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if rim and edge:
                grid[y][x] = edge
            elif texture is not None:
                grid[y][x] = texture[y % len(texture)][x % len(texture[0])]
            elif fill:
                grid[y][x] = fill
    return [grid]



# ---------------------------------------------------------------------------------------------
# Skill icons (placeholders generated from the skill library)
# ---------------------------------------------------------------------------------------------

SKILLS = REPO / "content" / "data" / "Skills" / "skill-library.json"
ICON = 24

# dark (lower-right shade), mid (fill), light (upper-left rim) per element; the viewer's element
# colours are the mids.
ICON_RAMPS = {
    "Fire": ("r", "o", "y"), "Water": ("b", "c", "C"), "Earth": ("n", "m", "M"),
    "Air": ("g", "A", "a"), "Lightning": ("q", "h", "Y"), "Ice": ("c", "C", "4"),
    "Nature": ("g", "G", "l"), "Metal": ("1", "2", "3"), "Light": ("M", "H", "w"),
    "Dark": ("p", "P", "u"), "None": ("p", "P", "u"),
}
ACTIVE_RAMP = ("P", "u", "4")
PASSIVE_RAMP = ("S", "s", "4")

FONT_5X7 = {  # 7 rows of 5, top to bottom
    "A": ".###.#...##...#######...##...##...#",
    "B": "####.#...##...#####.#...##...#####.",
    "C": ".###.#...##....#....#....#...#.###.",
    "D": "####.#...##...##...##...##...#####.",
    "E": "######....#....####.#....#....#####",
    "F": "######....#....####.#....#....#....",
    "G": ".###.#...##....#.####...##...#.####",
    "H": "#...##...##...#######...##...##...#",
    "I": ".###...#....#....#....#....#...###.",
    "J": "..###...#....#....#.#..#.#..#..##..",
    "K": "#...##..#.#.#..##...#.#..#..#.#...#",
    "L": "#....#....#....#....#....#....#####",
    "M": "#...###.###.#.##.#.##...##...##...#",
    "N": "#...###..##.#.##..###...##...##...#",
    "O": ".###.#...##...##...##...##...#.###.",
    "P": "####.#...##...#####.#....#....#....",
    "Q": ".###.#...##...##...##.#.##..#..##.#",
    "R": "####.#...##...#####.#.#..#..#.#...#",
    "S": ".#####....#.....###.....#....#####.",
    "T": "#####..#....#....#....#....#....#..",
    "U": "#...##...##...##...##...##...#.###.",
    "V": "#...##...##...##...##...#.#.#...#..",
    "W": "#...##...##...##.#.##.#.###.###...#",
    "X": "#...##...#.#.#...#...#.#.#...##...#",
    "Y": "#...##...#.#.#...#....#....#....#..",
    "Z": "#####....#...#...#...#...#....#####",
}


def skill_entries():
    """(id, display name, ramp, kind label) for every skill-library entry that names an ArtKey."""
    data = json.loads(SKILLS.read_text(encoding="utf-8"))
    out = []
    for group, id_key, ramp_of in (("BeastSkills", "SkillId", lambda e: ICON_RAMPS.get(e.get("Element") or "None", ICON_RAMPS["None"])),
                                   ("AvatarActives", "SkillId", lambda e: ACTIVE_RAMP),
                                   ("AvatarPassives", "PassiveId", lambda e: PASSIVE_RAMP)):
        for e in data.get(group, []):
            key = e.get("ArtKey") or ""
            if not key:
                continue
            if not key.startswith("skill/"):
                sys.exit(f"{e[id_key]}: skill ArtKey {key!r} does not start with 'skill/'")
            out.append((key[len("skill/"):], key, e.get("DisplayName") or e[id_key], ramp_of(e), group))
    return out


def skill_icon_grid(initial, ramp):
    """A 24x24 disc: dark K rim, fill shaded dark lower-right / light upper-left, and the initial
    (5x7, doubled) in w with a K outline, centred. Integer only."""
    dark, mid, light = ramp
    w = h = ICON
    grid = [[TRANSPARENT] * w for _ in range(h)]
    outer = w - 2                              # doubled units: radius 11 px
    for y in range(h):
        for x in range(w):
            d2 = ellipse_d2(x, y, w, h)
            if d2 > outer * outer:
                continue
            ch = mid
            dx, dy = 2 * x + 1 - w, 2 * y + 1 - h
            inner = outer - 6                  # a 3 px shading band inside the rim
            if d2 > inner * inner:
                ch = light if dx + dy < 0 else dark
            grid[y][x] = ch
    glyph = FONT_5X7.get(initial.upper())
    if glyph is not None:
        ox, oy = (w - 10) // 2, (h - 14) // 2
        pixels = set()
        for gy in range(7):
            for gx in range(5):
                if glyph[gy * 5 + gx] == "#":
                    for sy in range(2):
                        for sx in range(2):
                            pixels.add((ox + gx * 2 + sx, oy + gy * 2 + sy))
        for (px, py) in pixels:
            for ddx, ddy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1)):
                q = (px + ddx, py + ddy)
                if q not in pixels and 0 <= q[0] < w and 0 <= q[1] < h:
                    grid[q[1]][q[0]] = OUTLINE
        for (px, py) in pixels:
            grid[py][px] = "w"
    return grid


def skill_icon_sprites():
    """The generated skill icons as (meta, size, frames), like parse_sprite's."""
    sprites = []
    for skill_id, key, name, ramp, group in skill_entries():
        initial = next((c for c in name if c.isalpha()), "?")
        meta = {"name": f"skill_{skill_id}", "label": name, "kind": "skill", "artkey": key, "outline": "auto"}
        sprites.append((meta, (ICON, ICON), [skill_icon_grid(initial, ramp)]))
    return sprites

# ---------------------------------------------------------------------------------------------

GENERATORS = {"ring": ring_frames, "disc": disc_frames, "blob": blob_frames}


def to_image(grid, colors):
    h, w = len(grid), len(grid[0])
    img = Image.new("RGBA", (w, h))
    img.putdata([colors[ch] for row in grid for ch in row])
    return img


def scale(img, k):
    return img.resize((img.width * k, img.height * k), Image.NEAREST)


def font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:  # very old Pillow
        return ImageFont.load_default()


def build():
    data, colors, shade = load_palette()
    OUT.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(exist_ok=True)

    parsed = [parse_sprite(path, colors) for path in sorted((ROOT / "sprites").glob("*.txt"))]
    parsed += skill_icon_sprites()
    # Derived sprites (hexes built from tiles, aliases of built sprites) go after everything they
    # may depend on.
    parsed.sort(key=lambda p: (2 if "alias" in p[0] else 1 if p[0]["kind"] == "hex" else 0, p[0]["name"]))

    built = {}      # name -> final grid of frame 0 (what a hex's texture reads)
    sprites = []
    for meta, (w, h), frames in parsed:
        if "alias" in meta:
            source = next((s for s in sprites if s[0]["name"] == meta["alias"]), None)
            if source is None:
                sys.exit(f"{meta['name']}: alias {meta['alias']!r} is not a built sprite")
            if meta.get("tint") not in colors or colors[meta["tint"]][3] == 0:
                sys.exit(f"{meta['name']}: alias needs a tint (a palette colour)")
            sprites.append((meta, source[1]))
            print(f"alias {meta['name']:22s} -> {meta['alias']} tinted {meta['tint']}")
            continue
        if meta["kind"] == "fx" and meta.get("generator") == "burst":
            frames = burst_frames(meta, w, h)
        elif meta["kind"] == "fx" and meta.get("generator") in GENERATORS:
            frames = GENERATORS[meta["generator"]](meta, w, h)
        elif meta["kind"] == "hex":
            frames = hex_frame(meta, w, h, built)
        if not frames:
            sys.exit(f"{meta['name']}: no frames")
        imgs, finals = [], []
        for authored in frames:
            grid = authored
            if meta.get("autoshade", "no") == "yes":
                skip = set(meta.get("noshade", "").replace(" ", ""))
                grid = autoshade(grid, {k: v for k, v in shade.items() if k not in skip})
            if meta.get("outline", "auto") == "auto":
                grid = outline(grid, authored, set(meta.get("nooutline", "").replace(" ", "")))
            finals.append(grid)
            imgs.append(to_image(grid, colors))
        built[meta["name"]] = finals[0]
        strip = Image.new("RGBA", (w * len(imgs), h))
        for i, im in enumerate(imgs):
            strip.paste(im, (i * w, 0))
        name = meta["name"]
        strip.save(OUT / f"{name}.png", optimize=False)
        write_previews(meta, imgs, strip, w, h)
        sprites.append((meta, imgs))
        print(f"built {name:22s} {w}x{h} x{len(imgs)} frame(s)")

    sprites.sort(key=lambda s: s[0]["name"])
    by_name = {m["name"]: (m, ims) for m, ims in sprites}
    mock = map_mock(by_name)
    if mock is not None:
        scale(mock, 4).save(PREVIEW / "map_mock_x4.png")
    contact_sheet(sprites, mock)
    write_manifest(data, sprites)


def write_previews(meta, imgs, strip, w, h):
    name = meta["name"]
    scale(strip, 8).save(PREVIEW / f"{name}_x8.png")
    if len(imgs) > 1:
        big = [scale(im, 8) for im in imgs]
        big[0].save(PREVIEW / f"{name}.gif", save_all=True, append_images=big[1:],
                    duration=int(meta.get("frame_ms", "300")), loop=0, disposal=2)
    if meta["kind"] == "tile":
        tiled = Image.new("RGBA", (w * 3, h * 3))
        for ty in range(3):
            for tx in range(3):
                tiled.paste(imgs[0], (tx * w, ty * h))
        scale(tiled, 4).save(PREVIEW / f"{name}_tiled_x4.png")


PIXELS_PER_UNIT = 32  # one hex column step (HexLayout.ColumnStep) in placeholder pixels


def pivot_of(m, w, h):
    if "pivot" in m:
        x, y = (int(v) for v in m["pivot"].split(","))
        if not (0 <= x <= w and 0 <= y <= h):
            sys.exit(f"{m['name']}: pivot {x},{y} is off its {w}x{h} frame")
        return x, y
    if m["kind"] in ("beast", "enemy"):
        return w // 2, h * 3 // 4
    return w // 2, h // 2


def clips_of(m, by_name):
    clips = []
    for key in sorted(k for k in m if k == "clip" or (k.startswith("clip") and k[4:].isdigit())):
        name, sheet = m[key].split()
        if sheet not in by_name:
            sys.exit(f"{m['name']}: clip {name!r} names {sheet!r}, which is not a built sprite")
        sm, sims = by_name[sheet]
        frame_ms = int(sm.get("frame_ms", "0")) or 125
        clips.append({"Name": name, "Sheet": sheet, "Frames": list(range(len(sims))),
                      "Fps": max(1, round(1000 / frame_ms)), "Loop": True})
    return clips


def sprite_entry(m, ims, palette, by_name):
    w, h = ims[0].width, ims[0].height
    px, py = pivot_of(m, w, h)
    entry = {
        "Name": m["name"],
        "File": f"{m.get('alias', m['name'])}.png",
        "Kind": "sprite",
        "Category": m["kind"],
        "Label": m["label"],
        "ArtKey": m.get("artkey", ""),
        "FrameWidth": w,
        "FrameHeight": h,
        "Frames": len(ims),
        "FrameMs": int(m.get("frame_ms", "0")),
        "PivotX": px,
        "PivotY": py,
        "PixelsPerUnit": PIXELS_PER_UNIT,
        "Filter": "point",
        "Premultiplied": False,
    }
    if "tint" in m:
        entry["Tint"] = palette[m["tint"]]
    clips = clips_of(m, by_name)
    if clips:
        entry["Animations"] = clips
    return entry


def illustrated_entries():
    """The hand-kept illustrated sprites (illustrated.json) as manifest entries: frame size from the PNG,
    PixelsPerUnit from WorldHeight (feet to the art's top row, in world units)."""
    if not ILLUSTRATED.exists():
        return []
    entries = []
    for s in json.loads(ILLUSTRATED.read_text(encoding="utf-8"))["Sprites"]:
        path = (OUT / s["File"]).resolve()
        if not path.is_file():
            sys.exit(f"illustrated {s['Name']}: {s['File']} is not a file")
        with Image.open(path) as im:
            w, h = im.size
            bbox = im.convert("RGBA").getchannel("A").getbbox()
        px, py = s["PivotX"], s["PivotY"]
        if bbox is None or not (0 <= px <= w and bbox[1] < py <= h):
            sys.exit(f"illustrated {s['Name']}: pivot {px},{py} is off its {w}x{h} frame's art")
        if not s["WorldHeight"] > 0:
            sys.exit(f"illustrated {s['Name']}: WorldHeight must be above 0")
        entries.append({
            "Name": s["Name"],
            "File": s["File"],
            "Kind": "sprite",
            "Category": s["Category"],
            "Label": s["Label"],
            "ArtKey": s["ArtKey"],
            "FrameWidth": w,
            "FrameHeight": h,
            "Frames": 1,
            "FrameMs": 0,
            "PivotX": px,
            "PivotY": py,
            "PixelsPerUnit": round((py - bbox[1]) / s["WorldHeight"], 3),
            "Filter": "linear",
            "Premultiplied": False,
        })
        print(f"illustrated {s['Name']:22s} {w}x{h} height {s['WorldHeight']} -> {entries[-1]['PixelsPerUnit']} px/unit")
    return entries


def write_manifest(data, sprites):
    """The game's index of the art: PascalCase fields like the rest of the game data."""
    palette = {ch: hexv for ch, hexv in data["colors"].items() if hexv is not None}
    by_name = {m["name"]: (m, ims) for m, ims in sprites}
    entries = [sprite_entry(m, ims, palette, by_name) for m, ims in sprites] + illustrated_entries()
    names = [e["Name"] for e in entries]
    if len(set(names)) != len(names):
        sys.exit("illustrated.json repeats a sprite name")
    entries.sort(key=lambda e: e["Name"])
    manifest = {
        "_readme": "GENERATED by Tooling/PixelArt/build.py -- do not edit (illustrated sprites: edit "
                   "Tooling/PixelArt/illustrated.json and rebuild). Art manifest schema v2 "
                   "(BeastCraft.Vfx.ArtManifestData). Every sprite: its PNG (a horizontal strip of Frames frames, "
                   "each FrameWidth x FrameHeight), Kind (sprite; spine is reserved), Category, ArtKey (the key "
                   "species and enemies name their art by; an alias entry reuses another sprite's File with a "
                   "Tint), the pivot (PivotX/PivotY: pixels from the frame's top-left; a character's feet), "
                   "PixelsPerUnit (source pixels per hex column step), Filter (point for the pixel art, linear "
                   "for illustrated art, whose File is relative to this folder), Premultiplied (false: straight alpha, premultiplied on load) and "
                   "optional Animations (named clips); plus the palette (char -> colour) that VFX colours are "
                   "named from.",
        "SchemaVersion": 2,
        "Palette": palette,
        "Sprites": entries,
    }
    text = json.dumps(manifest, indent=2, ensure_ascii=True) + "\n"
    (OUT / MANIFEST).write_bytes(text.encode("ascii"))


MAP = [  # g grass, p path, s scorched rock, M marker over path
    "ggggggss",
    "ppgggsss",
    "gpppMsss",
    "gggppppp",
]


def map_mock(by_name):
    need = {"g": "tile_grass", "p": "tile_path", "s": "tile_scorched"}
    if not all(v in by_name for v in need.values()):
        return None
    t = 16
    img = Image.new("RGBA", (t * len(MAP[0]), t * len(MAP)))
    for y, row in enumerate(MAP):
        for x, ch in enumerate(row):
            base = need.get(ch, "tile_path")
            img.paste(by_name[base][1][0], (x * t, y * t))
            if ch == "M" and "marker_camp" in by_name:
                mk = by_name["marker_camp"][1][0]
                img.alpha_composite(mk, (x * t, y * t))
    return img


SECTIONS = [
    ("Beasts 32x32", ("beast",)),
    ("Enemies 32x32", ("enemy",)),
    ("Animations (strips; see preview/*.gif)", ("anim",)),
    ("Items 16x16", ("item",)),
    ("Map tiles 16x16 (2x2 tiled) + marker", ("tile", "marker")),
    ("Hex tiles 32x36 + particles", ("hex", "particle")),
    ("VFX layers (rings, glows, decals, rays, glyphs) + status icons", ("fx", "icon")),
    ("Skill icons 24x24 (generated placeholders) + UI", ("skill", "ui")),
]


def contact_sheet(sprites, mock):
    S = 6                      # sheet scale
    CELL = 32 * S              # 192 px cells
    PAD, LABEL = 16, 22
    BG = (233, 223, 200, 255)  # parchment
    CARD = (246, 240, 226, 255)
    INK = (40, 30, 48, 255)
    cols = 4
    f_title, f_label = font(20), font(14)

    rows = []  # list of (title, [(label, image)])
    for title, kinds in SECTIONS:
        cells = []
        for meta, imgs in sprites:
            kind = meta["kind"]
            if len(imgs) > 1:
                kind = "anim"
            if kind not in kinds:
                continue
            if kind == "anim":
                gap = 4
                strip = Image.new("RGBA", (imgs[0].width * len(imgs) + gap * (len(imgs) - 1), imgs[0].height))
                for i, im in enumerate(imgs):
                    strip.paste(im, (i * (im.width + gap), 0))
                k = S
                while k > 1 and strip.width * k > cols * CELL:
                    k -= 1
                cells.append((meta["label"], scale(strip, k)))
            elif kind == "tile":
                t = Image.new("RGBA", (imgs[0].width * 2, imgs[0].height * 2))
                for ty in range(2):
                    for tx in range(2):
                        t.paste(imgs[0], (tx * imgs[0].width, ty * imgs[0].height))
                cells.append((meta["label"], scale(t, S)))
            elif kind == "hex":
                cells.append((meta["label"], scale(imgs[0], 4)))
            else:
                k = S if imgs[0].width > 8 else S * 4
                cells.append((meta["label"], scale(imgs[0], k)))
        if cells:
            rows.append((title, cells))

    width = PAD + cols * (CELL + PAD)
    small_h = 32 * 2 + 32 + 3 * PAD
    height = PAD
    for _, cells in rows:
        spans = sum(max(1, -(-im.width // CELL)) for _, im in cells)
        n_lines = (spans + cols - 1) // cols + 1
        height += 28 + n_lines * (CELL + LABEL + PAD)
    height += 28 + small_h * 2
    if mock is not None:
        height += 28 + mock.height * 4 + PAD
    sheet = Image.new("RGBA", (width, height), BG)
    d = ImageDraw.Draw(sheet)
    y = PAD
    for title, cells in rows:
        d.text((PAD, y), title, fill=INK, font=f_title)
        y += 28
        col = 0
        for label, im in cells:
            span = min(cols, max(1, -(-im.width // CELL)))
            if col + span > cols:
                col, y = 0, y + CELL + LABEL + PAD
            cx = PAD + col * (CELL + PAD)
            cw = span * CELL + (span - 1) * PAD
            d.rectangle([cx, y, cx + cw - 1, y + CELL - 1], fill=CARD)
            sheet.alpha_composite(im, (cx + max(0, (cw - im.width) // 2), y + max(0, (CELL - im.height) // 2)))
            d.text((cx, y + CELL + 3), label, fill=INK, font=f_label)
            col += span
        y += CELL + LABEL + PAD

    d.text((PAD, y), "Actual size (1x and 2x) on light and dark UI grounds", fill=INK, font=f_title)
    y += 28
    for ground in ((246, 240, 226, 255), (43, 37, 54, 255)):
        d.rectangle([PAD, y, width - PAD, y + small_h - PAD], fill=ground)
        x = PAD * 2
        for meta, imgs in sprites:
            im = imgs[0]
            if meta["kind"] not in ("beast", "enemy"):
                continue
            if x + im.width * 2 > width - PAD * 2:
                break
            sheet.alpha_composite(im, (x, y + PAD))
            sheet.alpha_composite(scale(im, 2), (x, y + PAD + 32 + 8))
            x += im.width * 2 + 12
        y += small_h
    if mock is not None:
        d.text((PAD, y), "Map mock-up (tiles + camp marker, x4)", fill=INK, font=f_title)
        y += 28
        sheet.alpha_composite(scale(mock, 4), (PAD, y))
    sheet.save(PREVIEW / "contact_sheet.png")
    print(f"preview/contact_sheet.png {sheet.width}x{sheet.height}")


if __name__ == "__main__":
    build()
