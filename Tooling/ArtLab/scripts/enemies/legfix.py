# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Stalker (brute_L3): the far hind leg added before the lock.
"""Stalker (brute_L3): 3 legs were visible (the far hind leg hidden). A far hind leg is painted between the near hind
and near front legs, darker (far side), with a paw, so the count reads 4. Antlers = ELEMENT ACCENT."""
import sys
from common import *
import cv2
from fixlib import *
im = np.asarray(Image.open(WORK / "pick_before_fix.png").convert("RGB")).astype(np.float32)
out = im.copy()
leg = polyline_mask((H, W), [(392, 880), (384, 950), (392, 992)], 30, 24)
paw = np.zeros((H, W), np.uint8); cv2.ellipse(paw, (398, 994), (22, 11), 0, 0, 360, 1, -1)
m = leg | paw.astype(bool)
lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB)
behind = lab[..., 0] > 200                                                  # only over paper (behind the other legs)
m2 = m & (behind | (np.arange(H)[:, None] > 930))
paint(out, m2, (86, 78, 80)); outline(out, m2, 3, below=900)
for t in (-1, 1):
    cv2.line(out, (398 + t * 8, 996), (400 + t * 8, 1004), tuple(INKC.tolist()), 2)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
np.save(WORK / "fix_mask.npy", m2); np.save(WORK / "keep_mask.npy", cv2.dilate(m2.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool))
print("stalker leg ok", int(m2.sum()))
