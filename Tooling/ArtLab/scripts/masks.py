"""Line-pass character mask (round 4): SAM 2.1-small (CPU, box-only prompt) as the primary mask, so pale areas that
the colour auto-mask misses (cream belly/neck/mane on the cream ground) are inside the contour; the colour auto-mask
adds only detached parts (embers). Then a light close/open, small-hole fill and a sigma-2.5 smooth.

  python masks.py IN.png OUT_mask.png
"""
import sys
from common import *
import cv2
from line_pass import auto_mask, fill_holes


_sam = {}


def sam_box(rgb_u8, box):
    """SAM 2.1-small on CPU with a BOX-ONLY prompt (the centre-point prompt of round 3 misses cream-on-cream areas;
    box-only returns the whole creature incl. pale belly/neck). Best-scoring candidate covering 5-75% of the frame."""
    from transformers import Sam2Processor, Sam2Model
    if not _sam:
        _sam["p"] = Sam2Processor.from_pretrained(SAM); _sam["m"] = Sam2Model.from_pretrained(SAM).eval()
    inp = _sam["p"](images=Image.fromarray(rgb_u8), input_boxes=[[box]], return_tensors="pt")
    with torch.no_grad():
        o = _sam["m"](**inp, multimask_output=True)
    ms = _sam["p"].post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    sc = o.iou_scores[0, 0].numpy()
    ok = [i for i in range(len(ms)) if 0.05 < ms[i].mean() < 0.75]
    return ms[max(ok, key=lambda i: sc[i])] if ok else ms[int(np.argmax(sc))]


def line_mask(rgb_u8):
    u = rgb_u8.shape[1] / 896
    a = auto_mask(rgb_u8.astype(np.float32) / 255, u).astype(bool)
    ys, xs = np.nonzero(a)
    pad = int(12 * u)
    box = [max(0, int(xs.min()) - pad), max(0, int(ys.min()) - pad), min(rgb_u8.shape[1] - 1, int(xs.max()) + pad),
           min(rgb_u8.shape[0] - 1, int(ys.max()) + pad)]
    s = sam_box(rgb_u8, box)
    # SAM is primary (tight to the painted edge, includes pale areas, excludes the soft glow halo); the colour
    # auto-mask only contributes DETACHED parts (embers/petals) that are > 25 px from the SAM body
    far = cv2.dilate(s.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(25 * u) | 1,) * 2)) == 0
    n, lab, st, _ = cv2.connectedComponentsWithStats(a.astype(np.uint8), 8)
    det = np.zeros_like(a)
    for i in range(1, n):
        comp = lab == i
        if (comp & far).sum() > 0.8 * comp.sum():
            det |= comp
    m = (s | det).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(11 * u) | 1,) * 2))
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(7 * u) | 1,) * 2))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m, 8)
    if n > 1:
        big = st[1:, cv2.CC_STAT_AREA].max()
        m = np.isin(lab, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] > 0.002 * big]).astype(np.uint8)
    m = fill_holes(m, 0.003).astype(np.float32)
    m = cv2.GaussianBlur(m, (0, 0), 2.5 * u) > 0.5
    return m, a, s


if __name__ == "__main__":
    rgb = np.asarray(Image.open(sys.argv[1]).convert("RGB"))
    m, a, s = line_mask(rgb)
    Image.fromarray((m * 255).astype(np.uint8)).save(sys.argv[2])
    print(f"mask {m.mean():.3f}  auto {a.mean():.3f}  sam {s.mean():.3f}")
