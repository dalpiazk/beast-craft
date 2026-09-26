# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Basilisk: the far-hind leg given back its foot after the lock (4 legs).
"""After the lock the far-hind leg (the dark shape at 1x x 510-550, y 925-980) had lost its foot and merged into the
belly (final showed 3 legs). Give it a foot: Re-create it on the locked pick
(1x) from the near-hind leg's own shape: cut the near-hind lower leg + foot, darken it (far side), shift it right
into the gap before the far-front leg and paste it BEHIND everything (only onto paper / belly shadow), then re-run
the detail/line chain + rig.   python farhind.py IN OUT"""
import sys
from common import *
import cv2
im = np.asarray(Image.open(sys.argv[1]).convert("RGB")).astype(np.float32)
lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
pap = np.median(np.concatenate([lab[:30].reshape(-1, 3), lab[:, :30].reshape(-1, 3)]), 0)
paper = np.linalg.norm(lab - pap, axis=-1) < 12
src = np.zeros((H, W), np.uint8)
cv2.fillPoly(src, [np.array([(440, 930), (505, 930), (505, 1000), (425, 1000)], np.int32)], 1)   # near-hind lower leg + foot
src = cv2.erode((src.astype(bool) & ~paper).astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)   # no pale edge halo
DX, DY = 68, -4  # over the footless far-hind stub (x 510-550), down to the ground, behind the lighter front leg
M = np.float32([[1, 0, DX], [0, 1, DY]])
L = cv2.warpAffine(im, M, (W, H)) * 0.8                       # darker: far side
A = cv2.warpAffine(cv2.GaussianBlur(src.astype(np.float32), (0, 0), 1.0), M, (W, H))
behind = paper | ((lab[..., 0] < 75) & (np.arange(H)[:, None] > 920))   # paper or the dark stub/belly shadow, never the lighter front leg
A = A * cv2.GaussianBlur(behind.astype(np.float32), (0, 0), 1.0)

out = im * (1 - A[..., None]) + L * A[..., None]
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(sys.argv[2]); print("far hind leg px", int((A > .5).sum()))
