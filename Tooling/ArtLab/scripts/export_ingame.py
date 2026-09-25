"""In-game export: a rig's transparent character.png (feet pivot = bottom centre) -> the game sprite.

  python export_ingame.py SRC_character.png OUT.png [--content-size 504] [--pad 4] [--line-px 0]
                          [--preview DIR]

CPU only (Pillow, NumPy, OpenCV; no torch), deterministic.

1. Downscale the straight-alpha master with Lanczos in PREMULTIPLIED space (Pillow's "RGBa" mode), so the
   transparent edge does not pick up the colour hidden under alpha 0 (no dark or paper-coloured fringe), then back
   to straight alpha. The content's longer side is `--content-size` px (504 by default, so with the padding every
   beast fits a 512x512 frame: tall beasts are 504 px tall, the wide golem 504 px wide; see Tooling/ArtLab/README.md,
   "In-game export", for why ~512 px).
2. Optional `--line-px W`: a contour-only line pass (line_pass.py with the inner lines and ink re-colouring off)
   that thickens the plum outline for board size: an inside contour about W px wide at the sprite's own
   resolution (heavier on the shadow side, tapered at thin tips), the same W for every beast whatever its width.
   0 (the default) keeps the master's own outline.
3. Pad by `--pad` transparent px on every side (linear filtering at the texture edge then fades to transparent
   instead of clamping the feet row), then centre on a power-of-two canvas (512x512 at the default size) so the viewer can
   build a full mip chain on every GPU (GLES 2 needs power-of-two sizes for mipmaps). A square frame also keeps
   the turn-order portrait, which fits the whole frame, the same size for every beast.
4. Clear the colour under alpha 0 (straight alpha: invisible; it compresses better).

Prints the frame size and the pivot (px from the top-left): the feet, which Tooling/PixelArt/illustrated.json
records for the art manifest.

With --preview DIR it also writes board-size previews (box-filtered like the viewer's mip chain) at 1080-canvas
fit-all and desktop-window sizes, for checking that the outline still reads.
"""
import argparse
import pathlib
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))


def pot(n):
    p = 1
    while p < n:
        p *= 2
    return p


def downscale(master, longest):
    s = longest / max(master.width, master.height)
    size = (max(1, round(master.width * s)), max(1, round(master.height * s)))
    return master.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def thicken(sprite, px, work):
    """Contour-only line pass: line_pass.py on the sprite's own alpha, inner lines and ink re-colouring off.
    line_pass sizes are at an 896-wide reference and scale with the image width, so `px` is converted."""
    import line_pass
    outer = px * 896 / sprite.width
    src = work / "_contour_in.png"
    dst = work / "_contour_out.png"
    sprite.save(src)
    args = [str(src), str(dst), "--outer", str(outer), "--outer-var", "0.4", "--taper", "0.5",
            "--inner-strength", "0", "--ink-hi", "0"]
    line_pass.run(line_pass.parser().parse_args(args))
    out = Image.open(dst).convert("RGBA")
    out.load()
    src.unlink()
    dst.unlink()
    return out


def frame(sprite, pad):
    w, h = sprite.size
    fw = fh = pot(max(w, h) + 2 * pad)
    canvas = Image.new("RGBA", (fw, fh), (0, 0, 0, 0))
    # feet (bottom centre of the content) at (fw/2, fh - pad): the content's bottom edge sits pad px above the frame's
    x0 = fw // 2 - w // 2
    y0 = fh - pad - h
    canvas.alpha_composite(sprite, (x0, y0))
    a = np.array(canvas)
    a[a[..., 3] == 0] = 0
    return Image.fromarray(a), (x0 + w // 2, fh - pad)


def mip_preview(sprite, height):
    """What the viewer's mip chain + linear filter shows at `height` screen px (premultiplied box filter)."""
    im = sprite.convert("RGBa")
    while im.height // 2 >= height:
        im = im.resize((max(1, im.width // 2), max(1, im.height // 2)), Image.BOX)
    return im.resize((max(1, round(im.width * height / im.height)), height), Image.BILINEAR).convert("RGBA")


def preview(sprite, out_dir, name):
    out_dir.mkdir(parents=True, exist_ok=True)
    paper = (246, 238, 224, 255)
    for label, h in (("fitall_phone", 134), ("fitall_desktop", 67), ("maxzoom_phone", 211)):
        p = mip_preview(sprite, h)
        bg = Image.new("RGBA", p.size, paper)
        bg.alpha_composite(p)
        bg.convert("RGB").resize((p.width * 3, p.height * 3), Image.NEAREST).save(out_dir / f"{name}_{label}_x3.png")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--content-size", type=int, default=504)
    ap.add_argument("--pad", type=int, default=4)
    ap.add_argument("--line-px", type=float, default=0.0)
    ap.add_argument("--preview")
    a = ap.parse_args()

    master = Image.open(a.src).convert("RGBA")
    sprite = downscale(master, a.content_size)
    out = pathlib.Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if a.line_px > 0:
        sprite = thicken(sprite, a.line_px, out.parent)
    framed, pivot = frame(sprite, a.pad)
    framed.save(out, optimize=True)
    if a.preview:
        preview(framed, pathlib.Path(a.preview), out.stem)
    print(f"{out.name}: content {sprite.width}x{sprite.height}, frame {framed.width}x{framed.height}, "
          f"pivot {pivot[0]},{pivot[1]}")


if __name__ == "__main__":
    main()
