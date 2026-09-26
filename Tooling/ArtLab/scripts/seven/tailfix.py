# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Griffin V2 tail rotated -15 deg clear of the wing (producer note).
"""Griffin V2 (producer pick) tail readability: the V2 wing's trailing edge touches the tail tuft. Wing root stays
as chosen; the whole tail (stalk + tuft) is SAM-cut and rotated -15 deg about its haunch root, so the tuft moves
~45 px left and ~45 px down, clear of the wing.   python tailfix.py  (edits $ARTLAB_WORK/v2/pick_v2.png in place,
backup pick_v2_pretail.png)"""
from common import *
import cv2
from masks import _sam, sam_box

V2 = WORK / "v2"
ROOT = (290, 780)
ANG = -15.0


def sam_pts(img, pos, neg):
    sam_box(np.asarray(img), [0, 0, 10, 10]) if not _sam else None
    P, M = _sam["p"], _sam["m"]
    pts = [[float(x), float(y)] for x, y in pos + neg]
    inp = P(images=img, input_points=[[pts]], input_labels=[[[1] * len(pos) + [0] * len(neg)]], return_tensors="pt")
    with torch.no_grad():
        o = M(**inp, multimask_output=True)
    ms = P.post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    sc = o.iou_scores[0, 0].float().numpy()
    ok = [i for i in range(3) if all(ms[i][y, x] for x, y in pos) and not any(ms[i][y, x] for x, y in neg) and ms[i].mean() < 0.08]
    b = max(ok, key=lambda i: sc[i]) if ok else int(np.argmax(sc))
    return ms[b], float(sc[b])


src = V2 / "pick_v2.png"; bak = V2 / "pick_v2_pretail.png"
if not bak.exists():
    import shutil; shutil.copy(src, bak)
im = np.asarray(Image.open(bak).convert("RGB"))
tail, sc = sam_pts(Image.fromarray(im), [(150, 590), (125, 690), (200, 770)], [(250, 400), (420, 700), (300, 900)])
tail[:, 300:] = False                                         # never take body pixels right of the root
print("tail mask", sc, tail.mean())
td = cv2.dilate(tail.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
# remove the tail: paper where it is not touching the body; body pixels next to the root stay
paper = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, :30].reshape(-1, 3)]), 0)
pap = (paper[None, None] + np.random.default_rng(5).normal(0, 2.0, im.shape)).clip(0, 255)
out = im.astype(np.float32).copy()
ra = cv2.GaussianBlur(td.astype(np.float32), (0, 0), 2)[..., None]
out = out * (1 - ra) + pap * ra
# paste the rotated tail
M = cv2.getRotationMatrix2D(ROOT, -ANG, 1.0)                  # cv2 angle is CCW-positive in image coords
L = cv2.warpAffine(im.astype(np.float32), M, (W, H), flags=cv2.INTER_LINEAR)
A = cv2.warpAffine(cv2.GaussianBlur(tail.astype(np.float32), (0, 0), 1.0), M, (W, H), flags=cv2.INTER_LINEAR)
out = out * (1 - A[..., None]) + L * A[..., None]
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(src)
np.save(V2 / "tail_v2.npy", A > 0.5)
ys, xs = np.nonzero(A > 0.5); print("new tail bbox", xs.min(), ys.min(), xs.max(), ys.max())
