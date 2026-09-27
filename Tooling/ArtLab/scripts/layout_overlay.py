"""Battle layout overlay: the hex grid and a layout's obstacle cells drawn over any backdrop image, for review.

  python layout_overlay.py IMAGE --layout ARTKEY [--out OUT.png] [--board-rect X,Y,W,H] [--repo DIR]
  python layout_overlay.py --all [--images DIR] [--out-dir DIR] [--repo DIR]

Standard library plus Pillow only (no GPU, no NumPy); deterministic. Reads the repo's data:
  content/data/Encounters/battle-layouts.json   the layouts: (RegionId, ArtKey, Arena) -> obstacle cells (axial Q, R)
  content/data/Vfx/battle-art.json              each backdrop's BoardRect: where the arena's tiles box lies in the image

and draws, over the image, the arena's hexes exactly where the game puts them (pointy-top, columns 32 board px apart,
rows 27, odd rows shifted half a column right; the tiles' box mapped onto the board rect), the two deployment zones
(enemy rows red at the top, the player's blue at the bottom), and each obstacle cell filled orange with a bold
outline and its (Q, R): the hexes a painting must put its rocks, rubble, stumps or boughs on, and nowhere else on the
board.

The board rect: --board-rect (fractions of the image, as battle-art.json), else the backdrop's own BoardRect when the
image is a 9:16 slot image (1440 x 2560, or a placeholder at another 9:16 size), else a fit for a candidate painting
of another shape (the tiles' box centred, 94% of the image's width or 80% of its height, whichever is smaller, keeping
the hexes' true proportions), labelled "approximate" in the output.

--all: one PNG per layout into --out-dir, over <images>/<id>_preview.png when a --images folder (several: ';'-separated) holds the candidate preview
of the layout's backdrop id (backdrop/r01/<id>/<arena>), else over the repo's slot image
content/art/backdrops/<region>/<id>/<arena>.png.
"""
import argparse
import json
import pathlib
import sys

from PIL import Image, ImageDraw, ImageFont

# HexLayout (src/BeastCraft.Presentation/Board/HexLayout.cs) and HexGrid.DimensionsFor / deployment depth.
TILE_W, TILE_H, COL, ROW = 32, 36, 32, 27
ARENAS = {"Small": (5, 7, 2), "Medium": (8, 11, 3), "Large": (11, 15, 4)}   # width, height, deployment depth


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
    """Every tile as (Q, R), HexGrid's odd-r offset rectangle."""
    min_c = -(width // 2)
    min_r = -((height - 1) // 2)
    for row in range(min_r, min_r + height):
        for col in range(min_c, min_c + width):
            yield col - (row >> 1), row


def centre(q, r):
    """HexLayout.Center: the tile's centre in board px."""
    return COL * q + COL / 2 * r, ROW * r


def corners(q, r):
    x, y = centre(q, r)
    return [(x, y - TILE_H / 2), (x + TILE_W / 2, y - TILE_H / 4), (x + TILE_W / 2, y + TILE_H / 4),
            (x, y + TILE_H / 2), (x - TILE_W / 2, y + TILE_H / 4), (x - TILE_W / 2, y - TILE_H / 4)]


def load(repo):
    layouts = json.loads((repo / "content/data/Encounters/battle-layouts.json").read_text(encoding="utf-8"))["Layouts"]
    backdrops = json.loads((repo / "content/data/Vfx/battle-art.json").read_text(encoding="utf-8"))["Backdrops"]
    return layouts, {b["ArtKey"]: b for b in backdrops}


def fit_rect(image_size, width, height):
    """A board rect (fractions) for an image that is not a slot image: the tiles' box centred, true proportions."""
    w, h = image_size
    _, _, tw, th = board_bounds(width, height)
    scale = min(0.94 * w / tw, 0.80 * h / th)
    bw, bh = tw * scale / w, th * scale / h
    return {"X": (1 - bw) / 2, "Y": (1 - bh) / 2, "Width": bw, "Height": bh}


def overlay(image, layout, rect, approximate=False):
    width, height, depth = ARENAS[layout["Arena"]]
    w, h = image.size
    left, top, tw, th = board_bounds(width, height)
    sx, sy = rect["Width"] * w / tw, rect["Height"] * h / th
    ox, oy = rect["X"] * w - left * sx, rect["Y"] * h - top * sy

    def px(points):
        return [(ox + x * sx, oy + y * sy) for x, y in points]

    blocked = {(c["Q"], c["R"]) for c in layout["Cells"]}
    min_r = -((height - 1) // 2)
    max_r = min_r + height - 1
    base = image.convert("RGBA")
    fill = Image.new("RGBA", base.size, (0, 0, 0, 0))
    lines = Image.new("RGBA", base.size, (0, 0, 0, 0))
    df, dl = ImageDraw.Draw(fill), ImageDraw.Draw(lines)
    line_w = max(1, round(min(w, h) / 500))
    for q, r in tiles(width, height):
        shape = px(corners(q, r))
        if (q, r) in blocked:
            df.polygon(shape, fill=(255, 140, 20, 150))
        elif r <= min_r + depth - 1:
            df.polygon(shape, fill=(230, 60, 60, 45))
        elif r >= max_r - depth + 1:
            df.polygon(shape, fill=(60, 140, 240, 45))
        dl.line(shape + [shape[0]], fill=(255, 250, 235, 150), width=line_w)
    for q, r in sorted(blocked):
        shape = px(corners(q, r))
        dl.line(shape + [shape[0]], fill=(255, 120, 0, 255), width=3 * line_w)
    out = Image.alpha_composite(Image.alpha_composite(base, fill), lines)
    d = ImageDraw.Draw(out)
    size = max(12, round(min(w, h) / 45))
    try:
        font = ImageFont.load_default(size=size)
        small = ImageFont.load_default(size=max(10, size * 2 // 3))
    except TypeError:
        font = small = ImageFont.load_default()
    for q, r in sorted(blocked):
        x, y = centre(q, r)
        d.text((ox + x * sx, oy + y * sy), "%d,%d" % (q, r), fill=(40, 20, 0, 255), font=small, anchor="mm")
    title = "%s  %s  %d obstacles%s" % (layout["ArtKey"], layout["Arena"], len(blocked), "  (board rect approximate)" if approximate else "")
    d.rectangle([0, 0, w, size * 2], fill=(20, 16, 28, 200))
    d.text((size // 2, size // 2), title, fill=(255, 240, 200, 255), font=font)
    return out


def rect_for(image, layout, backdrops, override):
    if override:
        x, y, bw, bh = (float(v) for v in override.split(","))
        return {"X": x, "Y": y, "Width": bw, "Height": bh}, False
    w, h = image.size
    backdrop = backdrops.get(layout["ArtKey"])
    if backdrop is not None and abs(w / h - 9 / 16) < 0.01:
        return backdrop["BoardRect"], False
    width, height, _ = ARENAS[layout["Arena"]]
    return fit_rect(image.size, width, height), True


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("image", nargs="?")
    parser.add_argument("--layout", help="the layout's ArtKey, e.g. backdrop/r01/sun0/medium")
    parser.add_argument("--out")
    parser.add_argument("--board-rect", help="X,Y,W,H in fractions of the image")
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--images", help="--all: folders of <id>_preview.png candidate paintings, separated by ';'")
    parser.add_argument("--out-dir", default="layout-overlays")
    parser.add_argument("--repo", default=str(pathlib.Path(__file__).resolve().parents[3]))
    args = parser.parse_args()
    repo = pathlib.Path(args.repo)
    layouts, backdrops = load(repo)

    if args.all:
        out_dir = pathlib.Path(args.out_dir)
        out_dir.mkdir(parents=True, exist_ok=True)
        for layout in layouts:
            _, region, bid, arena = layout["ArtKey"].split("/")
            candidates = [pathlib.Path(folder) / (bid + "_preview.png") for folder in (args.images or "").split(";") if folder]
            found = [c for c in candidates if c.is_file()]
            path = found[0] if found else repo / "content/art/backdrops" / region / bid / (arena + ".png")
            with Image.open(path) as im:
                rect, approximate = rect_for(im, layout, backdrops, None)
                out = overlay(im, layout, rect, approximate)
            name = "%s_%s_%s.png" % (region, bid, arena)
            out.convert("RGB").save(out_dir / name)
            print("wrote", out_dir / name, "over", path.name)
        return

    if not args.image or not args.layout:
        parser.error("give IMAGE and --layout, or --all")
    layout = next((l for l in layouts if l["ArtKey"] == args.layout), None)
    if layout is None:
        sys.exit("no layout " + args.layout + " in battle-layouts.json")
    with Image.open(args.image) as im:
        rect, approximate = rect_for(im, layout, backdrops, args.board_rect)
        out = overlay(im, layout, rect, approximate)
    target = args.out or (pathlib.Path(args.image).stem + "_layout.png")
    out.convert("RGB").save(target)
    print("wrote", target)


if __name__ == "__main__":
    main()
