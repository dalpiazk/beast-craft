"""Reusable, deterministic linework pass (no diffusion). Runs on every asset.

  python line_pass.py IN.png OUT.png [--mask M.png | (alpha channel) | auto] [--sam]
         [--line-color 3b1c26] [--outer 5.0] [--outer-var 0.45] [--shadow-dir 1,1] [--taper 0.45]
         [--inner-kernel 5] [--inner-lo 0.07] [--inner-hi 0.25] [--inner-strength 0.9] [--inner-grow 0.6]
         [--ink-lo 0.10] [--ink-hi 0.30] [--edge-weight 0.0] [--edge-lo 70] [--edge-hi 150] [--min-len 12]
         [--palette 0.0] [--save-mask M.png] [--debug DIR]

Pixel sizes are given at the 896-px-wide reference and scale with image width.

1. Character mask: --mask file > alpha channel > auto (background colour sampled from the border, OKLab distance,
   hole fill, largest components). --sam refines the auto mask with SAM 2.1 (box + interior point prompt) and keeps
   it when it agrees with the auto mask (IoU > 0.8).
2. Optional --palette F: nearest-colour map to the fixed warm 24-colour house palette in OKLab, blended at F
   (0.6 = last round's 60/40), character pixels only.
3. Outer contour: an inside stroke from the mask edge. Width = outer * (1 + outer_var * dot(outward normal, shadow
   dir)) * low-frequency jitter, so the shadow side (default bottom-right) is heavier; clamped to taper * local
   half-thickness so flame and feather tips taper to fine points. Solid-ink blend in the line colour.
4. Existing ink: near-black pixels (luminance smoothstep ink_hi -> ink_lo) on/near the character are re-coloured to
   the warm line colour (black -> plum/brown), keeping some of their own value variation.
5. Inner lines: morphological black-hat of luminance (the model's own thin dark lines) + optional weak Canny colour
   boundaries (off by default: they draw lines along cel-shading bands and muddy the colours), thresholded with a smoothstep, short fragments removed, restricted to inside the contour; multiplied
   in with a slightly lighter version of the line colour, so inner lines are thinner and lighter than the contour.
Only pixels under line alpha change; flat colour areas stay clean.
"""
import argparse, pathlib
import numpy as np
import cv2
from PIL import Image

PAL_HEX = ["2e1418", "4a2224", "7a2e22", "b8342a", "e2482e", "f06a2c", "f79a3a", "fbc04a", "ffe07a", "fff1c4",
           "f7e2c0", "e9c9a0", "c98a6a", "e59aa0", "8fbfe0", "b9d8ec", "e6eef2", "fbf6ee",
           "5f8f4a", "86b45a", "b3d27a", "3f6b3e", "d8c890", "a99a7a"]


def hex3(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255


def oklab(c):
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    M1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929], [0.2119034982, 0.6806995451, 0.1073969566],
                   [0.0883024619, 0.2817188376, 0.6299787005]])
    M2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468], [1.9779984951, -2.4285922050, 0.4505937099],
                   [0.0259040371, 0.7827717662, -0.8086757660]])
    return np.cbrt(c @ M1.T) @ M2.T


def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / max(hi - lo, 1e-6), 0, 1)
    return t * t * (3 - 2 * t)


def fill_holes(m, max_frac=0.003):
    """Fill enclosed holes smaller than max_frac of the mask area (specks of bg-like colour inside the character);
    larger enclosed regions are real background seen through gaps (e.g. between wing and tail) and stay open."""
    m = m.astype(bool)
    n, lab, st, _ = cv2.connectedComponentsWithStats((~m).astype(np.uint8), 4)
    h, w = m.shape
    out = m.copy()
    for i in range(1, n):
        x, y, bw, bh, area = st[i]
        touches = x == 0 or y == 0 or x + bw == w or y + bh == h
        if not touches and area < max_frac * m.sum():
            out[lab == i] = True
    return out


def auto_mask(rgb, u):
    lab = oklab(rgb.reshape(-1, 3)).reshape(rgb.shape)
    b = max(4, int(6 * u))
    border = np.concatenate([lab[:b].reshape(-1, 3), lab[-b:].reshape(-1, 3), lab[:, :b].reshape(-1, 3), lab[:, -b:].reshape(-1, 3)])
    bg = np.median(border, 0)
    dist = np.linalg.norm(lab - bg, axis=-1)
    thr = max(0.06, np.percentile(np.linalg.norm(border - bg, axis=-1), 99) * 1.5)
    m = (dist > thr).astype(np.uint8)
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(5 * u) | 1,) * 2)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, k)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, k)
    n, lab_, st, _ = cv2.connectedComponentsWithStats(m, 8)
    if n > 1:
        big = st[1:, cv2.CC_STAT_AREA].max()
        keep = [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] > 0.02 * big]
        m = np.isin(lab_, keep)
    return fill_holes(m)


def sam_refine(rgb, m):
    import sys
    sys.path.insert(0, str(pathlib.Path(__file__).parent))
    from common import SAM, DEVICE
    import torch
    from transformers import Sam2Processor, Sam2Model
    proc = Sam2Processor.from_pretrained(SAM)
    model = Sam2Model.from_pretrained(SAM).to(DEVICE).eval()
    ys, xs = np.nonzero(m)
    box = [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())]
    dt = cv2.distanceTransform(m.astype(np.uint8), cv2.DIST_L2, 5)
    py, px = np.unravel_index(np.argmax(dt), dt.shape)
    img = Image.fromarray((rgb * 255).astype(np.uint8))
    inp = proc(images=img, input_points=[[[[int(px), int(py)]]]], input_labels=[[[1]]], input_boxes=[[box]],
               return_tensors="pt").to(DEVICE)
    with torch.no_grad():
        o = model(**inp, multimask_output=True)
    masks = proc.post_process_masks(o.pred_masks.cpu(), inp["original_sizes"])[0][0].numpy().astype(bool)
    ious = [(s & m).sum() / max((s | m).sum(), 1) for s in masks]
    best = int(np.argmax(ious))
    print(f"SAM agreement IoU {ious[best]:.3f}")
    return fill_holes(masks[best]) if ious[best] > 0.8 else m


def palette_blend(rgb, m, f):
    P = np.stack([hex3(h) for h in PAL_HEX])
    lab = oklab(rgb[m]); pl = oklab(P)
    idx = np.argmin(((lab[:, None] - pl[None]) ** 2).sum(-1), 1)
    out = rgb.copy(); out[m] = f * P[idx] + (1 - f) * rgb[m]
    return out


def outer_alpha(m, u, a):
    mu = m.astype(np.uint8)
    d_in = cv2.distanceTransform(mu, cv2.DIST_L2, 5)
    sm = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 3 * u)
    gy, gx = np.gradient(sm)
    nrm = np.sqrt(gx ** 2 + gy ** 2) + 1e-6
    nx, ny = -gx / nrm, -gy / nrm                           # outward normal
    sd = np.array([float(v) for v in a.shadow_dir.split(",")]); sd /= np.linalg.norm(sd)
    facing = nx * sd[0] + ny * sd[1]
    rng = np.random.default_rng(a.seed)                      # deterministic low-frequency "hand" jitter
    jit = cv2.resize(rng.normal(0, 1, (max(2, m.shape[0] // 96), max(2, m.shape[1] // 96))).astype(np.float32),
                     m.shape[::-1], interpolation=cv2.INTER_CUBIC)
    w = a.outer * u * (1 + a.outer_var * facing) * (1 + 0.12 * np.clip(jit, -2, 2))
    k = int(8 * a.outer * u) | 1
    local_r = cv2.dilate(d_in, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k)))   # local half-thickness
    w = np.minimum(w, np.maximum(a.taper * local_r, 0.8))
    cov = np.clip(w + 0.5 - d_in, 0, 1) * m
    return cv2.GaussianBlur(cov.astype(np.float32), (0, 0), 0.6 * max(u, 1)), w


def inner_alpha(rgb, m, u, a):
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    k = int(a.inner_kernel * u) | 1
    ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (k, k))
    bh = cv2.morphologyEx(lum, cv2.MORPH_BLACKHAT, ker)
    al = smoothstep(a.inner_lo, a.inner_hi, bh)
    if a.edge_weight > 0:
        lab = cv2.cvtColor((rgb * 255).astype(np.uint8), cv2.COLOR_RGB2LAB)
        lab = cv2.bilateralFilter(lab, int(5 * u) | 1, 40, 5 * u)
        e = np.zeros(m.shape, np.uint8)
        for ch in range(3):
            e |= cv2.Canny(lab[..., ch], a.edge_lo, a.edge_hi)
        al = np.maximum(al, a.edge_weight * (e > 0))
    # drop short fragments (painterly texture specks)
    bw = (al > 0.3).astype(np.uint8)
    n, lab_, st, _ = cv2.connectedComponentsWithStats(bw, 8)
    small = np.zeros(n, bool)
    small[1:] = np.maximum(st[1:, cv2.CC_STAT_WIDTH], st[1:, cv2.CC_STAT_HEIGHT]) < a.min_len * u
    al = al * ~(small[lab_] & (bw > 0))
    al[al < 0.12] = 0
    if a.inner_grow > 0:
        g = cv2.dilate(al, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3)), iterations=max(1, round(a.inner_grow * u)))
        al = np.maximum(al, 0.8 * g)
    inside = cv2.erode(m.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
    return cv2.GaussianBlur((al * inside).astype(np.float32), (0, 0), 0.5 * max(u, 1)) * a.inner_strength


def run(a):
    im = Image.open(a.inp)
    has_alpha = im.mode in ("RGBA", "LA")
    rgb = np.asarray(im.convert("RGB")).astype(np.float32) / 255
    u = rgb.shape[1] / 896
    if a.mask:
        m = np.asarray(Image.open(a.mask).convert("L").resize(rgb.shape[1::-1], Image.BILINEAR)) > 127
    elif has_alpha:
        m = np.asarray(im.getchannel("A")) > 127
    else:
        m = auto_mask(rgb, u)
        if a.sam:
            m = sam_refine(rgb, m)
    m = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.2 * u) > 0.5         # smooth jaggies
    if a.save_mask:
        Image.fromarray((m * 255).astype(np.uint8)).save(a.save_mask)
    if a.palette > 0:
        rgb = palette_blend(rgb, m, a.palette)
    lc = hex3(a.line_color)
    ao, wfield = outer_alpha(m, u, a)
    ai = inner_alpha(rgb, m, u, a)
    # existing ink (the model's near-black lines, incl. the AA fringe just outside the mask) is re-coloured to the
    # warm line colour, keeping a little of its own darkness variation
    lum = rgb @ np.array([0.299, 0.587, 0.114], np.float32)
    near = cv2.dilate(m.astype(np.uint8), np.ones((5, 5), np.uint8), iterations=max(1, round(u))) > 0
    ak = (1 - smoothstep(a.ink_lo, a.ink_hi, lum)) * near if a.ink_hi > 0 else np.zeros_like(lum)
    ink_col = lc[None, None] * (0.85 + 0.6 * lum[..., None])
    out = rgb * (1 - ak[..., None]) + ink_col * ak[..., None]
    inner_col = np.clip(lc * 1.7, 0, 1)                                        # multiply colour, lighter than the contour
    ai = ai * (1 - ak)                                                         # don't double-darken recoloured ink
    out = out * (1 - ai[..., None]) + (out * inner_col) * ai[..., None]
    out = out * (1 - ao[..., None]) + lc * ao[..., None]                       # solid warm contour
    res = Image.fromarray((out.clip(0, 1) * 255).round().astype(np.uint8))
    if has_alpha:
        res.putalpha(im.getchannel("A"))
    res.save(a.out)
    if a.debug:
        d = pathlib.Path(a.debug); d.mkdir(parents=True, exist_ok=True)
        Image.fromarray(((1 - np.maximum(np.maximum(ao, ai), ak)) * 255).astype(np.uint8)).save(d / "line_alpha.png")
        Image.fromarray(((1 - ao) * 255).astype(np.uint8)).save(d / "outer_alpha.png")
        Image.fromarray(((1 - ai) * 255).astype(np.uint8)).save(d / "inner_alpha.png")
    print(f"saved {a.out}  mask px {int(m.sum())}  outer width px {np.percentile(wfield[m & (ao > 0.5)], [5, 50, 95]).round(1)}")


def parser():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inp"); ap.add_argument("out")
    ap.add_argument("--mask"); ap.add_argument("--sam", action="store_true"); ap.add_argument("--save-mask")
    ap.add_argument("--line-color", default="3b1c26")
    ap.add_argument("--outer", type=float, default=5.0); ap.add_argument("--outer-var", type=float, default=0.45)
    ap.add_argument("--shadow-dir", default="1,1"); ap.add_argument("--taper", type=float, default=0.45)
    ap.add_argument("--inner-kernel", type=float, default=5); ap.add_argument("--inner-lo", type=float, default=0.07)
    ap.add_argument("--inner-hi", type=float, default=0.25); ap.add_argument("--inner-strength", type=float, default=0.9)
    ap.add_argument("--inner-grow", type=float, default=0.6)
    ap.add_argument("--ink-lo", type=float, default=0.10); ap.add_argument("--ink-hi", type=float, default=0.30)
    ap.add_argument("--edge-weight", type=float, default=0.0); ap.add_argument("--edge-lo", type=int, default=70)
    ap.add_argument("--edge-hi", type=int, default=150); ap.add_argument("--min-len", type=float, default=12)
    ap.add_argument("--palette", type=float, default=0.0); ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--debug")
    return ap


if __name__ == "__main__":
    run(parser().parse_args())
