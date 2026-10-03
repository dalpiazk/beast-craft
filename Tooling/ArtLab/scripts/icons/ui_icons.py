"""Journal UI kit painted icons (issue #52, direction D): small painted icons for the glyphs
UiPainter.Glyph currently draws in code (the tab strips, the bottom nav, the back chevron, the
settings gear), replacing them when UiKit.Enabled.

Same base checkpoint and local-only pipeline as the skill icons (icons/icons.py) and the region
backdrops (menu_backdrops/gen_backdrops.py), adapted the same way icons.py itself adapts it for a
small bold glyph rather than a painted beast portrait: a procedural shape -- here, the SAME point
geometry UiPainter.Glyph already draws each glyph with in C#, redrawn in Pillow so the painted icon
keeps the vector glyph's own silhouette -- as an img2img init, SDXL painting texture and shading over
it at a moderate strength (0.5: enough to add paint, not enough to abandon the shape; icons.py's own
treatments run 0.45-0.72 for the same reason). The init's own silhouette mask becomes the final PNG's
alpha channel directly (not a colour-keyed cutout of the AI's output, which is only ever as reliable
as the prompt's "flat background" instruction, found unreliable in testing -- see provenance.md),
so the cutout is always pixel-exact regardless of what SDXL paints outside the shape. Prompt-safe
vocabulary only, no studio/franchise names.

Usage (ArtLab venv python; run from this directory):
  python ui_icons.py gen ICON [ICON ...] [--seeds N,N] [--out-dir DIR]
  python ui_icons.py list

ONE call does at most a couple of icons worth of seeds before exiting (the process loads the whole
SDXL pipeline once, generates, writes, and exits -- see menu_backdrops/README.md's "memory pacing"
note, same reasoning): keep any one invocation to a small icon list so it finishes in a few minutes.
Resumable: skips a seed whose output file already exists unless --force.
"""
import argparse
import math
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import common  # noqa: E402
from PIL import Image, ImageDraw, ImageFilter  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
OUT_DIR_DEFAULT = pathlib.Path(common.WORK) / "ui_icons" / "raw"
INIT_DIR_DEFAULT = pathlib.Path(common.WORK) / "ui_icons" / "init"

GEN_SIZE = 512
STEPS = 28
CFG = 6.0
STRENGTH = 0.5

# palette v2 (docs art direction, issue #49)
INK_PLUM = (46, 42, 69)
PAPER = (246, 238, 220)
APRICOT = (242, 169, 104)

HEAD = "no humans, no text, "
TAIL = (", small bold painted icon, gouache and ink illustration, soft brush texture, warm parchment "
        "background, hand-painted, masterpiece")
NEG = ("text, letters, words, watermark, signature, frame, border, ui, button, icon frame, chrome, "
       "metallic, glossy, bevel, 3d render, video game interface, photo, photorealistic, multiple "
       "objects, person, face, character, animal, scene, landscape, lowres, worst quality, low "
       "quality, blurry")


# ---------------------------------------------------------------------------- the 11 glyph shapes
# Each returns a 0..255 "L" mode PIL mask at (GEN_SIZE, GEN_SIZE): 255 = icon, 0 = background. The
# point geometry below mirrors UiPainter.Glyph's own P(x,y) helper (a 0..1 box mapped onto the
# square), so the painted icon keeps the same silhouette as the code-drawn glyph it replaces.

def _mask(draw_fn):
    img = Image.new("L", (GEN_SIZE, GEN_SIZE), 0)
    d = ImageDraw.Draw(img)
    s = GEN_SIZE

    def p(x, y):
        return (x * s, y * s)

    draw_fn(d, p, s)
    return img


def _thick_line(d, p0, p1, width, cap=True):
    d.line([p0, p1], fill=255, width=width)
    if cap:
        r = width / 2
        for cx, cy in (p0, p1):
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=255)


def mask_back(d, p, s):
    t = s * 0.1
    _thick_line(d, p(0.62, 0.2), p(0.34, 0.5), int(t * 1.2))
    _thick_line(d, p(0.34, 0.5), p(0.62, 0.8), int(t * 1.2))


def mask_gear(d, p, s):
    t = s * 0.1
    d.ellipse([p(0.26, 0.26), p(0.74, 0.74)], outline=255, width=int(t * 1.4))
    for i in range(8):
        a = i * math.pi / 4
        x0, y0 = 0.5 + 0.3 * math.cos(a), 0.5 + 0.3 * math.sin(a)
        x1, y1 = 0.5 + 0.42 * math.cos(a), 0.5 + 0.42 * math.sin(a)
        _thick_line(d, p(x0, y0), p(x1, y1), int(t * 1.3))
    d.ellipse([p(0.4, 0.4), p(0.6, 0.6)], fill=255)


def mask_map(d, p, s):
    pts = [p(0.16, 0.26), p(0.39, 0.18), p(0.61, 0.26), p(0.84, 0.18), p(0.84, 0.74), p(0.61, 0.82), p(0.39, 0.74), p(0.16, 0.82)]
    d.polygon(pts, outline=255, width=int(s * 0.07))
    _thick_line(d, p(0.39, 0.2), p(0.39, 0.74), int(s * 0.05), cap=False)
    _thick_line(d, p(0.61, 0.26), p(0.61, 0.8), int(s * 0.05), cap=False)


def mask_roster(d, p, s):
    d.ellipse([p(0.31, 0.45), p(0.69, 0.83)], fill=255)
    for cx, cy, r in ((0.26, 0.42, 0.085), (0.41, 0.28, 0.085), (0.59, 0.28, 0.085), (0.74, 0.42, 0.085)):
        d.ellipse([p(cx - r, cy - r), p(cx + r, cy + r)], fill=255)


def mask_grove(d, p, s):
    _thick_line(d, p(0.5, 0.86), p(0.5, 0.5), int(s * 0.13))
    d.ellipse([p(0.3, 0.16), p(0.7, 0.56)], fill=255)
    d.ellipse([p(0.12, 0.33), p(0.52, 0.63)], fill=255)
    d.ellipse([p(0.48, 0.33), p(0.88, 0.63)], fill=255)


def mask_avatar(d, p, s):
    d.ellipse([p(0.35, 0.19), p(0.65, 0.49)], fill=255)
    d.pieslice([p(0.24, 0.5), p(0.76, 1.12)], 180, 360, fill=255)


def mask_inventory(d, p, s):
    d.rounded_rectangle([p(0.2, 0.4), p(0.8, 0.82)], radius=s * 0.06, fill=255)
    d.arc([p(0.35, 0.22), p(0.65, 0.57)], 180, 360, fill=255, width=int(s * 0.07))


def mask_battle(d, p, s):
    t = int(s * 0.1)
    _thick_line(d, p(0.24, 0.78), p(0.8, 0.2), t)
    _thick_line(d, p(0.76, 0.78), p(0.2, 0.2), t)


def mask_star(d, p, s):
    pts = []
    for i in range(10):
        a = (-90 + i * 36) * math.pi / 180.0
        r = 0.42 if i % 2 == 0 else 0.18
        pts.append(p(0.5 + r * math.cos(a), 0.54 + r * math.sin(a)))
    d.polygon(pts, fill=255)


def mask_speaker(d, p, s):
    pts = [p(0.16, 0.62), p(0.16, 0.38), p(0.34, 0.38), p(0.56, 0.18), p(0.56, 0.82), p(0.34, 0.62)]
    d.polygon(pts, fill=255)
    d.arc([p(0.38, 0.3), p(0.78, 0.7)], -45, 45, fill=255, width=int(s * 0.055))
    d.arc([p(0.26, 0.18), p(0.9, 0.82)], -55, 55, fill=255, width=int(s * 0.05))


def mask_lock(d, p, s):
    d.rounded_rectangle([p(0.28, 0.46), p(0.72, 0.82)], radius=s * 0.06, fill=255)
    d.arc([p(0.36, 0.2), p(0.64, 0.6)], 180, 360, fill=255, width=int(s * 0.1))


ICONS = {
    "back": ([101, 102], "a single bold left-pointing chevron arrow shape, thick rounded stroke", mask_back),
    "gear": ([111, 112], "a single mechanical cog gear wheel", mask_gear),
    "map": ([121, 122], "a single folded travel map with trail lines", mask_map),
    "roster": ([131, 132], "a single paw print", mask_roster),
    "grove": ([141, 142], "a single small round tree on a trunk", mask_grove),
    "avatar": ([151, 152], "a single simple person bust silhouette", mask_avatar),
    "inventory": ([161, 162], "a single closed travel satchel bag with a strap", mask_inventory),
    "battle": ([171, 172], "a pair of crossed swords", mask_battle),
    "star": ([181, 182], "a single five pointed star", mask_star),
    "speaker": ([191, 192], "a single speaker cone with sound waves", mask_speaker),
    "lock": ([201, 202], "a single padlock", mask_lock),
}


def prompt(icon):
    return HEAD + ICONS[icon][1] + TAIL


def make_init(icon):
    """The init image for img2img: the icon's own ink-plum silhouette on parchment -- flat colour,
    no AI, so the shape SDXL paints over is exactly the vector glyph's own geometry."""
    mask = _mask(ICONS[icon][2])
    mask = mask.filter(ImageFilter.GaussianBlur(1.0))
    base = Image.new("RGB", (GEN_SIZE, GEN_SIZE), PAPER)
    fill = Image.new("RGB", (GEN_SIZE, GEN_SIZE), INK_PLUM)
    init = Image.composite(fill, base, mask)
    return init, mask


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("gen")
    g.add_argument("icons", nargs="+")
    g.add_argument("--seeds", default=None, help="comma-separated seeds, overriding ICONS' default pair")
    g.add_argument("--out-dir", default=str(OUT_DIR_DEFAULT))
    g.add_argument("--init-dir", default=str(INIT_DIR_DEFAULT))
    g.add_argument("--force", action="store_true")
    sub.add_parser("list")
    a = ap.parse_args()

    if a.cmd == "list":
        for name, (seeds, subject, _mask_fn) in ICONS.items():
            print(f"{name:12s} seeds={seeds} {subject}")
        return

    out_dir = pathlib.Path(a.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    init_dir = pathlib.Path(a.init_dir)
    init_dir.mkdir(parents=True, exist_ok=True)

    unknown = [i for i in a.icons if i not in ICONS]
    if unknown:
        sys.exit(f"unknown icon(s): {unknown}; see `python ui_icons.py list`")

    pipe = common.load_pipe("img2img")
    t_load = time.time()
    print(f"pipeline loaded on {common.DEVICE} ({common.DTYPE})")

    for icon in a.icons:
        default_seeds, _subject, _mask_fn = ICONS[icon]
        seeds = [int(x) for x in a.seeds.split(",")] if a.seeds else default_seeds
        init, mask = make_init(icon)
        init_path = init_dir / f"{icon}_init.png"
        init.save(init_path)
        mask_path = init_dir / f"{icon}_mask.png"
        mask.save(mask_path)

        for seed in seeds:
            out_path = out_dir / f"{icon}_{seed}.png"
            if out_path.exists() and not a.force:
                print(f"skip {out_path} (exists)")
                continue
            t0 = time.time()
            gen = common.gen(seed)
            painted = pipe(prompt=prompt(icon), negative_prompt=NEG, image=init, strength=STRENGTH,
                           num_inference_steps=STEPS, guidance_scale=CFG, generator=gen).images[0]
            if common.is_black(painted):
                print(f"{icon} seed {seed}: black/NaN, retrying at +5000")
                gen = common.gen(seed + 5000)
                painted = pipe(prompt=prompt(icon), negative_prompt=NEG, image=init, strength=STRENGTH,
                               num_inference_steps=STEPS, guidance_scale=CFG, generator=gen).images[0]

            # Cut out by the INIT's own mask (not a colour key of the AI's output): paste the
            # painted pixels onto transparent, masked exactly to the procedural silhouette.
            rgba = painted.convert("RGBA")
            cut = Image.new("RGBA", rgba.size, (0, 0, 0, 0))
            cut.paste(rgba, (0, 0), mask)
            cut.save(out_path)
            print(f"wrote {out_path} ({time.time() - t0:.1f}s)")

    print(f"total (load + gen): {time.time() - t_load:.1f}s")


if __name__ == "__main__":
    main()
