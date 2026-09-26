# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Reinhard colour lock, drift and palette fidelity for the seven finals.
"""Colour lock post-step + drift metric (character layer only).

reinhard(img, beast): Reinhard-style Lab statistics transfer of the character pixels toward the character pixels of
  the beast's fixed colour-blocked base (sketch_{beast}_block.png, i.e. its swatch applied to its design). Chroma (a,b)
  mean/std are transferred at `chroma` strength, lightness mean only at `light` strength so painterly value shading
  survives. Background is untouched.
drift(img): fraction of character pixels whose hue is off-palette cool (cyan/blue/violet/magenta: 170-330 deg in HSV)
  with saturation > 0.20 and value > 0.20. An image counts as drifted if that fraction > DRIFT_T (2%).
"""
from common import *
import cv2
from line_pass import auto_mask

DRIFT_T = 0.02


_sam = {}


def sam_mask(rgb_u8):
    """Busy/scenic background: SAM 2.1-small (CPU) with a centre-point + generous box prompt; keep the best-scoring
    candidate that covers 5-70% of the frame."""
    from transformers import Sam2Processor, Sam2Model
    if not _sam:
        _sam["p"] = Sam2Processor.from_pretrained(SAM); _sam["m"] = Sam2Model.from_pretrained(SAM).eval()
    h, w = rgb_u8.shape[:2]
    pts = [[[[w // 2, int(h * 0.5)], [w // 2, int(h * 0.62)]]]]
    inp = _sam["p"](images=Image.fromarray(rgb_u8), input_points=pts, input_labels=[[[1, 1]]],
                    input_boxes=[[[int(w * .06), int(h * .04), int(w * .94), int(h * .96)]]], return_tensors="pt")
    with torch.no_grad():
        o = _sam["m"](**inp, multimask_output=True)
    masks = _sam["p"].post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    sc = o.iou_scores[0, 0].numpy()
    ok = [i for i in range(len(masks)) if 0.05 < masks[i].mean() < 0.70]
    return masks[max(ok, key=lambda i: sc[i])] if ok else masks[int(np.argmax(sc))]


def char_mask(rgb_u8):
    """Plain background -> colour auto-mask (as round 2). Scenic background (auto-mask swallows > 55% of the frame or
    the border is not uniform) -> SAM."""
    rgb_u8 = np.asarray(rgb_u8)
    m = auto_mask(rgb_u8.astype(np.float32) / 255, 1.0).astype(bool)
    b = np.concatenate([rgb_u8[:8].reshape(-1, 3), rgb_u8[-8:].reshape(-1, 3), rgb_u8[:, :8].reshape(-1, 3),
                        rgb_u8[:, -8:].reshape(-1, 3)]).astype(np.float32)
    if m.mean() > 0.55 or b.std(0).mean() > 12:
        return sam_mask(rgb_u8)
    return m


def _lab(rgb_u8):
    return cv2.cvtColor(rgb_u8, cv2.COLOR_RGB2LAB).astype(np.float32)


def reinhard(img, beast, chroma=0.7, light=0.3, mask=None):
    rgb = np.asarray(img.convert("RGB"))
    m = char_mask(rgb) if mask is None else mask
    blk = np.asarray(Image.open(WORK / f"sketch_{beast}_block.png").convert("RGB"))
    bm = np.any(np.abs(blk.astype(int) - np.array(BG)) > 6, axis=-1)
    bm &= blk.mean(-1) > 45                                              # ignore the eye/ink pixels of the block
    L, T = _lab(rgb), _lab(blk)
    src, tgt = L[m], T[bm]
    out = L.copy()
    ms, ss, mt, st = src.mean(0), src.std(0) + 1e-3, tgt.mean(0), tgt.std(0) + 1e-3
    x = L[m]
    y = x.copy()
    y[:, 1:] = (x[:, 1:] - ms[1:]) / ss[1:] * st[1:] + mt[1:]           # chroma: full Reinhard
    y[:, 0] = x[:, 0] + (mt[0] - ms[0])                                   # lightness: mean shift only
    w = np.array([light, chroma, chroma], np.float32)
    z = x * (1 - w) + y * w
    # round 4: the std rescale pushed yellows negative on a* (green-tinted flames). Never let a pixel go greener than
    # both its own input and the swatch block's own a* floor (2nd percentile) -> intended greens (golem moss) survive.
    a_floor = np.percentile(tgt[:, 1], 2)
    z[:, 1] = np.maximum(z[:, 1], np.minimum(x[:, 1], a_floor))
    out[m] = z
    res =cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_LAB2RGB)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.2)[..., None]  # soft edge
    res = (res * a + rgb * (1 - a)).round().astype(np.uint8)
    return Image.fromarray(res)


def drift(img):
    rgb = np.asarray(img.convert("RGB"))
    m = char_mask(rgb)
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    hue = hsv[..., 0] * 360 / 256; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
    cool = (hue >= 170) & (hue <= 330) & (s > 0.20) & (v > 0.20)
    frac = float((cool & m).sum() / max(m.sum(), 1))
    return frac, frac > DRIFT_T


def redo_log(path):
    """Recompute drift for every entry of a log.json (after the mask upgrade)."""
    path = pathlib.Path(path); log = json.loads(path.read_text())
    for k, v in log.items():
        if isinstance(v, dict) and "beast" in v:
            name = k if "stage" in v else f"{path.parent.name[6:]}_{k}"
            f = path.parent / f"{name}.png"
            if f.exists():
                fr, d = drift(Image.open(f)); v["drift"] = round(fr, 4); v["drifted"] = d
    path.write_text(json.dumps(log, indent=1))


def fidelity(img, beast):
    """Palette fidelity: distance (CIELAB a*b*, 0-255 OpenCV scale ~ 1 unit = 1 Lab unit) between the character's mean
    chroma and the mean chroma of the beast's swatch block. Catches element bleed (e.g. fire-orange on the golem,
    white 'marshmallow' golem) that the cool-hue drift metric cannot see."""
    rgb = np.asarray(img.convert("RGB")); m = char_mask(rgb)
    blk = np.asarray(Image.open(WORK / f"sketch_{beast}_block.png").convert("RGB"))
    bm = np.any(np.abs(blk.astype(int) - np.array(BG)) > 6, axis=-1) & (blk.mean(-1) > 45)
    a = _lab(rgb)[m][:, 1:].mean(0); t = _lab(blk)[bm][:, 1:].mean(0)
    return float(np.linalg.norm(a - t))
