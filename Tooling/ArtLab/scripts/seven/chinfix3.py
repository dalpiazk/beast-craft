# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque round 3: the stray chin fang removed by clone stamp (the method used for the final).
"""Chin fix, final method (clone stamp): the nub region is replaced by the chin fur directly left of it (same rows,
shifted 44 px right), so the lower-jaw shading continues naturally into the gold jaw band; feathered 3 px; the chin
outline below is untouched. Paint-over / inpaint attempts (chinfix.py, chinfix2.py) left a visible flat patch.
python chinfix3.py  (reads work/tarasque_final_prechin.png)"""
from common import *
import cv2
src = np.asarray(Image.open(WORK / "tarasque_final_prechin.png").convert("RGB")).astype(np.float32)
m = np.zeros(src.shape[:2], np.uint8)
cv2.fillPoly(m, [np.array([(584, 1752), (606, 1752), (628, 1850), (568, 1850)], np.int32)], 1)
m = cv2.dilate(m, np.ones((5, 5), np.uint8)); m[1852:] = 0
# upper rows (tip of the nub, y < 1785): clone from the pale fur to the RIGHT (the left side there holds the mouth
# line's end); lower rows: clone the lower-jaw shade from the LEFT (source x >= 522, clear of the lip mark)
yy = np.arange(src.shape[0])[:, None]
from_right = np.roll(src, -44, axis=1); from_left = np.roll(src, 44, axis=1)
sw = np.clip((yy - 1778) / 14.0, 0, 1)[..., None].astype(np.float32)          # 0 = right source, 1 = left source
clone = from_right * (1 - sw) + from_left * sw
a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 3)[..., None]
out = src * (1 - a) + clone * a
# bottom band just above the chin outline (leftover gold base + a dark bit): clone the clean pale shade from 28 px above
band = np.zeros(src.shape[:2], np.uint8)
cv2.fillPoly(band, [np.array([(566, 1822), (636, 1822), (636, 1849), (566, 1846)], np.int32)], 1)
ba = cv2.GaussianBlur(band.astype(np.float32), (0, 0), 2)[..., None]
out = out * (1 - ba) + np.roll(out, 28, axis=0) * ba          # from the already-clean rows 28 px above
# smooth clone seams inside the repaired area only (fur-scale blur), and turn any gold sliver left on the chin
# outline edge into outline ink
reg = cv2.dilate((m | band).astype(np.uint8), np.ones((7, 7), np.uint8)).astype(np.float32); reg[1840:] = 0
sm = cv2.GaussianBlur(out, (0, 0), 5)                         # fur-scale blur hides the clone seams
ra = cv2.GaussianBlur(reg, (0, 0), 2)[..., None]
out = out * (1 - ra) + sm * ra
hsv = cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
gold = (hsv[..., 0] * 360 / 256 > 25) & (hsv[..., 0] * 360 / 256 < 60) & (hsv[..., 1] > 70)
gold[:1832] = False; gold[1862:] = False; gold[:, :560] = False; gold[:, 640:] = False
out[gold] = (59, 28, 38)
# merge the specks into the outline: one continuous ink stroke along its top edge
ink = np.zeros(src.shape[:2], np.uint8)
cv2.polylines(ink, [np.array([(560, 1847), (598, 1851), (640, 1849)], np.int32)], False, 1, 8)
ia = cv2.GaussianBlur(ink.astype(np.float32), (0, 0), 1.0)[..., None]
out = out * (1 - ia) + np.array([59, 28, 38], np.float32) * ia
# last specks: a 1-px ink tick above the outline (x~637) and a gold sliver on it (x~575) -> Telea
sp = np.zeros(src.shape[:2], np.uint8)
cv2.rectangle(sp, (632, 1828), (644, 1846), 1, -1); cv2.rectangle(sp, (568, 1838), (590, 1848), 1, -1)
out = cv2.inpaint(out.clip(0, 255).astype(np.uint8), sp * 255, 4, cv2.INPAINT_TELEA).astype(np.float32)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(T4 / "tarasque_final.png"); print("clone ok")
