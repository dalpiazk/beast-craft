# Beast Craft ArtLab, remaining-seven finals (2026-09-26): the Leviathan's neck speck retouch (Telea).
"""Remove isolated dark specks the detail pass leaves inside the body (not lines: small, round-ish, far from the
contour and from the eye/mouth region).   python despeck.py BEAST FACE_CX FACE_CY FACE_R (1x)"""
import sys
from common import *
import cv2
b = sys.argv[1]; fcx, fcy, fr = [int(v) * 2 for v in sys.argv[2:5]]
min_y = int(sys.argv[5]) * 2 if len(sys.argv) > 5 else 0      # only below this y (1x): skips scale-detailed head/mane
p = T4 / f"{b}_final.png"; rgb = np.asarray(Image.open(p).convert("RGB"))
m = np.asarray(Image.open(WORK / f"{b}_lines_mask.png").convert("L")) > 127
inner = cv2.erode(m.astype(np.uint8), np.ones((21, 21), np.uint8)).astype(bool)
L = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB)[..., 0].astype(np.float32)
loc = cv2.GaussianBlur(L, (0, 0), 12)
dark = (L < loc - 45) & inner & (loc > 185)          # dark dots on LIGHT paint only (keeps scale/feather lines)
yy, xx = np.mgrid[:rgb.shape[0], :rgb.shape[1]]
dark &= ((xx - fcx) ** 2 + (yy - fcy) ** 2 > fr ** 2) & (yy > min_y)
n, lab, st, cen = cv2.connectedComponentsWithStats(dark.astype(np.uint8), 8)
hole = np.zeros(dark.shape, np.uint8); found = []
for i in range(1, n):
    x, y, w, h, a = st[i]
    if 6 <= a <= 500 and max(w, h) <= 30 and max(w, h) / max(min(w, h), 1) < 3:
        hole[lab == i] = 1; found.append([int(cen[i][0]), int(cen[i][1]), int(a)])
hole = cv2.dilate(hole, np.ones((7, 7), np.uint8))
out = cv2.inpaint(rgb, hole * 255, 6, cv2.INPAINT_TELEA)
Image.fromarray(rgb).save(WORK / f"{b}_final_prespeck.png"); Image.fromarray(out).save(p)
print("specks removed", len(found), found[:20])
