# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque round 3, chin fang: SDXL inpaint attempt (REJECTED, flat patch); kept for the record.
"""Chin fix pass 2: the hand paint-over (chinfix.py) removed the nub but left a flat patch. Small SDXL mask-inpaint
(strength 0.55, 24 steps, math SDPA) over the patch only, starting from the paint-over, so the fur texture and
shading match; then the chin outline is re-inked on top. Nothing outside the mask changes.  python chinfix2.py"""
from common import *
import cv2
import rigparts as R
R._CUR["beast"] = "tarasque"
p = T4 / "tarasque_final.png"
im = np.asarray(Image.open(p).convert("RGB"))
m = np.zeros(im.shape[:2], np.uint8)
cv2.fillPoly(m, [np.array([(584, 1752), (606, 1752), (634, 1846), (560, 1846)], np.int32)], 1)
m = cv2.dilate(m, np.ones((9, 9), np.uint8)); m[1846:] = 0
ip = R.inpaint_pipe()
out, tries = R.sd_inpaint(ip, im, m.astype(bool), "pale grey white fur, lion chin, soft fur texture", seed=21)
out = out.astype(np.float32)
a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 2)[..., None]
out = im * (1 - a) + out * a
ink = np.zeros(im.shape[:2], np.uint8)
cv2.polylines(ink, [np.array([(548, 1844), (575, 1852), (605, 1856), (636, 1852)], np.int32)], False, 1, 12)
box = np.zeros(im.shape[:2], np.float32); box[1830:1870, 540:645] = 1
ia = cv2.GaussianBlur(ink.astype(np.float32), (0, 0), 1.2)[..., None] * box[..., None]
out = out * (1 - ia) + np.array([59, 28, 38], np.float32) * ia
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(p); print("inpaint tries", tries)
