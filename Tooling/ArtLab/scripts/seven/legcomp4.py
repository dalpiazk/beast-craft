# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque round 2: head/shell from the approved V3 source, legs from the 0.80 lock.
"""Leg-swap composite: head/shell from the APPROVED V3 round-2 composite (unchanged), legs from the 0.80 lock of the
leg-swap edit, blended in below y 870 (40 px ramp); then the background is replaced by clean paper outside the
character mask (the re-locks had grown a grainy paper texture)."""
from common import *
import cv2
from masks import line_mask
top = np.asarray(Image.open(WORK / "v" / "v3c_tarasque_11.png").convert("RGB")).astype(np.float32)
hi = np.asarray(Image.open(WORK / "v" / "v4hi_tarasque_11.png").convert("RGB")).astype(np.float32)
y0 = 870
a = np.clip((np.arange(H, dtype=np.float32) - y0) / 40.0, 0, 1)[:, None, None]
out = top * (1 - a) + hi * a
m = line_mask(out.clip(0, 255).astype(np.uint8))[0]
m = cv2.dilate(m.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(np.float32)
m = cv2.GaussianBlur(m, (0, 0), 2)[..., None]
lab = cv2.cvtColor(top.astype(np.uint8), cv2.COLOR_RGB2LAB)
pap = np.median(np.concatenate([top[:40].reshape(-1, 3), top[:, -40:].reshape(-1, 3)]), 0)
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
paper = pap[None, None] * (1.02 - 0.04 * ((xx / W) * .4 + (yy / H) * .6))[..., None] + np.random.default_rng(3).normal(0, 1.5, top.shape)
out = out * m + paper * (1 - m)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "v" / "v4c_tarasque_11.png"); print("composited v4 (approved head/shell + new legs)")
