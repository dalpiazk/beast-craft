"""In-game export: a rig's transparent character.png (feet pivot = bottom centre) -> the game sprite.

  python export_ingame.py SRC_character.png OUT.png [--content-size 504] [--pad 4] [--line-px 0]
                          [--preview DIR] [--accent-mask MASK.png [--accent-native RRGGBB] [--compare DIR]]

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

5. Optional `--accent-mask MASK.png` (softened by a `--accent-feather` px Gaussian at the master's size, default 4) (an enemy's element-accent region: 8-bit, the master's size, 255 = accent):
   split the sprite into a BASE (OUT.png) and an ACCENT OVERLAY (OUT_accent.png, the same frame and pivot) that the
   viewer draws over the base multiplied by the unit's element colour (docs/design/presentation-and-vfx.md,
   "Element accents"). See accent_split() for the maths: in the accent region every pixel C is read as a grey part
   plus k times the art's own accent colour N (least squares, kept feasible), the overlay is white at alpha k and the
   base keeps the rest, so base + overlay tinted N reproduces the art (checked and printed: the maximum per-channel
   difference over paper, 0-255), and any other tint T gives C + k (T - N). N is the region's own colour
   (`--accent-native`, else measured from the region and printed): the manifest's AccentNative.

Prints the frame size and the pivot (px from the top-left): the feet, which Tooling/PixelArt/illustrated.json
records for the art manifest.

With --preview DIR it also writes board-size previews (box-filtered like the viewer's mip chain) at 1080-canvas
fit-all and desktop-window sizes, for checking that the outline still reads.
"""
import argparse
import pathlib
import sys

import numpy as np
from PIL import Image, ImageFilter

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


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float64) / 255


def measure_native(rgba, mask):
    """The accent region's own colour: the mean of its most saturated quarter, scaled so its brightest channel is
    that quarter's 90th-percentile brightness. Rounded to 8 bits (the manifest stores it as #rrggbb)."""
    a = rgba[..., 3] / 255.0
    sel = (mask > 0.5) & (a > 0.5)
    px = rgba[..., :3][sel] / 255.0
    sat = px.max(1) - px.min(1)
    coloured = px[sat >= np.percentile(sat, 75)]
    mean = coloured.mean(0)
    bright = np.percentile(coloured.max(1), 90)
    n = np.clip(mean * (bright / max(mean.max(), 1e-6)), 0, 1)
    return np.round(n * 255) / 255


def accent_split(rgba, mask, native):
    """rgba: the framed sprite (uint8 HxWx4, straight alpha); mask: accent weight 0-1 (HxW); native: N (3, 0-1).
    Per pixel with colour C and alpha A in the region: least squares C ~ g*(1,1,1) + b*N (g, b >= 0); k = b, clamped
    so the base below stays in 0-1 (C - kN >= 0 and C - kN <= 1 - kA), times the mask weight. Overlay: white, alpha
    kA (8-bit). Base: (C - k'N) / (1 - k'A) with k' from the quantised overlay alpha, so drawing the overlay tinted N
    over the base gives back C. Only fully opaque pixels get overlay: at a soft silhouette edge (A < 1) the overlay
    would also cover a share of whatever lies behind the unit, which no base colour can undo, so the edge keeps the
    art's own colour. Returns (base uint8, overlay uint8, k)."""
    c = rgba[..., :3].astype(np.float64) / 255
    a = rgba[..., 3].astype(np.float64) / 255
    n = np.asarray(native, np.float64)
    one = np.ones(3)
    # normal equations for [g, b] against the basis (1, N)
    m11, m12, m22 = 3.0, n.sum(), (n * n).sum()
    r1, r2 = c.sum(-1), (c * n).sum(-1)
    det = m11 * m22 - m12 * m12
    g = (m22 * r1 - m12 * r2) / det
    b = (m11 * r2 - m12 * r1) / det
    b = np.where(g < 0, r2 / m22, b)                       # no grey part: project onto N alone
    k = np.clip(b, 0, None)
    with np.errstate(divide="ignore", invalid="ignore"):
        lo = np.where(n > 0, c / np.where(n > 0, n, 1), np.inf).min(-1)                  # C - kN >= 0
        den = a[..., None] - n
        hi = np.where(den > 0, (1 - c) / np.where(den > 0, den, 1), np.inf).min(-1)    # C - kN <= 1 - kA
    k = np.minimum(np.minimum(k, lo), np.minimum(hi, 0.98)) * np.clip(mask, 0, 1)
    k = np.where(a >= 1, k, 0)                              # opaque pixels only: see the docstring
    oa = np.round(k * a * 255).astype(np.uint8)
    overlay = np.zeros_like(rgba)
    overlay[..., :3] = np.where(oa[..., None] > 0, 255, 0)
    overlay[..., 3] = oa
    kq = np.where(a > 0, oa / 255 / np.where(a > 0, a, 1), 0)
    base_c = (c - kq[..., None] * n) / (1 - kq * a)[..., None]
    base = rgba.copy()
    region = oa > 0
    base[..., :3][region] = np.round(np.clip(base_c[region], 0, 1) * 255).astype(np.uint8)
    return base, overlay, k


def composite(base, overlay, tint, paper):
    """base, then overlay multiplied by tint, over paper (straight alpha in, 0-1 RGB out), as the viewer blends."""
    ba = base[..., 3:4] / 255.0
    oa = overlay[..., 3:4] / 255.0
    out = paper * (1 - ba) + base[..., :3] / 255.0 * ba
    return out * (1 - oa) + (overlay[..., :3] / 255.0) * np.asarray(tint) * oa


def fidelity(sprite, base, overlay, native, paper=(246 / 255, 238 / 255, 224 / 255)):
    """Max and mean per-channel |difference| (0-255, 8-bit output) between the art and base + overlay tinted N."""
    p = np.asarray(paper)
    sa = sprite[..., 3:4] / 255.0
    orig = p * (1 - sa) + sprite[..., :3] / 255.0 * sa
    diff = np.abs(np.round(orig * 255) - np.round(composite(base, overlay, native, p) * 255))
    return int(diff.max()), float(diff.mean()), diff


def export_accent(framed, mask_src, a, out, sprite_size, offset):
    """The accent mask downscaled exactly as the sprite (premultiplied Lanczos) and framed the same, then split."""
    m = Image.open(mask_src).convert("L")
    if a.accent_feather > 0:   # soften the hard mask edge (master px) so a tinted region fades into the art
        m = m.filter(ImageFilter.GaussianBlur(a.accent_feather))
    m = m.resize(sprite_size, Image.LANCZOS)
    canvas = Image.new("L", framed.size, 0)
    canvas.paste(m, offset)
    mask = np.asarray(canvas, np.float64) / 255
    rgba = np.asarray(framed).copy()
    native = hex_rgb(a.accent_native) if a.accent_native else measure_native(rgba, mask)
    base, overlay, k = accent_split(rgba, mask, native)
    worst, mean, diff = fidelity(rgba, base, overlay, native)
    Image.fromarray(base).save(out, optimize=True)
    acc = out.with_name(out.stem + "_accent.png")
    Image.fromarray(overlay).save(acc, optimize=True)
    hexn = "".join(f"{round(v * 255):02x}" for v in native)
    print(f"{acc.name}: accent native #{hexn}, overlay coverage {(overlay[..., 3] > 0).mean() * 100:.1f}% of the frame, "
          f"Nature-tint fidelity max {worst}/255, mean {mean:.3f}")
    if a.compare:
        d = pathlib.Path(a.compare)
        d.mkdir(parents=True, exist_ok=True)
        p = np.array([246, 238, 224]) / 255.0
        sa = rgba[..., 3:4] / 255.0
        orig = (p * (1 - sa) + rgba[..., :3] / 255.0 * sa) * 255
        comp = composite(base, overlay, native, p) * 255
        amp = np.clip(255 - diff.max(-1, keepdims=True).repeat(3, -1) * 16, 0, 255)
        sheet = np.concatenate([orig, comp, amp], 1).astype(np.uint8)
        Image.fromarray(sheet).save(d / f"{out.stem}_nature_check.png")
    return hexn, worst


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--content-size", type=int, default=504)
    ap.add_argument("--pad", type=int, default=4)
    ap.add_argument("--line-px", type=float, default=0.0)
    ap.add_argument("--preview")
    ap.add_argument("--accent-mask")
    ap.add_argument("--accent-native")
    ap.add_argument("--accent-feather", type=float, default=4.0)
    ap.add_argument("--compare")
    a = ap.parse_args()

    master = Image.open(a.src).convert("RGBA")
    sprite = downscale(master, a.content_size)
    out = pathlib.Path(a.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if a.line_px > 0:
        sprite = thicken(sprite, a.line_px, out.parent)
    framed, pivot = frame(sprite, a.pad)
    framed.save(out, optimize=True)
    if a.accent_mask:
        x0 = framed.width // 2 - sprite.width // 2
        y0 = framed.height - a.pad - sprite.height
        export_accent(framed, a.accent_mask, a, out, sprite.size, (x0, y0))
    if a.preview:
        preview(framed, pathlib.Path(a.preview), out.stem)
    print(f"{out.name}: content {sprite.width}x{sprite.height}, frame {framed.width}x{framed.height}, "
          f"pivot {pivot[0]},{pivot[1]}")


if __name__ == "__main__":
    main()
