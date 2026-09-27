# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Giant: toe claws and splits on the three visible feet after the finish.
"""Post-finish limb readability on the t11 Giant (2x px): toe claws + ink toe splits on the 2 front paws and the
right hind foot, and an ink separation between the two front paws, so the 4 limbs read at board size
(2 front paws, far hind leg left in dusk shadow, near hind foot right).  python pawfix.py  (backup work/giant_final_prepaws.png)"""
from common import *
import cv2, shutil
p = T4 / "giant_final.png"; bak = WORK / "giant_final_prepaws.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB")).copy()
INK = (59, 28, 38); CLAW = (228, 222, 204)
def toes(cx, cy, n, sp, ang=0):
    for k in range(n):
        x = cx + (k - (n - 1) / 2) * sp
        cv2.line(im, (int(x), cy - 34), (int(x + ang), cy - 4), INK, 7)             # toe split
        cv2.fillPoly(im, [np.array([(int(x - 9), cy - 2), (int(x + 9), cy - 2), (int(x + ang / 2), cy + 20)], np.int32)], CLAW)
        cv2.polylines(im, [np.array([(int(x - 9), cy - 2), (int(x + ang / 2), cy + 20), (int(x + 9), cy - 2)], np.int32)], False, INK, 3)
toes(700, 2060, 3, 36)          # front paw A (1x ~350,1030)
toes(900, 2066, 3, 36)          # front paw B (1x ~450,1033)
toes(1590, 2070, 3, 44, ang=12) # hind foot toes (1x ~795,1035)
cv2.line(im, (800, 1960), (806, 2070), INK, 8)                                     # split between the front paws
Image.fromarray(im).save(p); print("paws ok")
