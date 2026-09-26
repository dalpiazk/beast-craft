# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque round 3, chin fang: paint-over attempt (REJECTED, left a flat patch); kept for the record.
"""Producer: stray gold fang/nub sticking up from the lower jaw, left of the mouth. Hand paint-over on the final
(2x px), nothing else touched: (1) mask = the nub's gold pixels + its own dark side strokes above the chin line;
(2) Telea fill from the surrounding pale chin fur; (3) the chin's bottom outline is re-inked straight through where
the nub's base had interrupted it.   python chinfix.py"""
from common import *
import cv2, shutil
p = T4 / "tarasque_final.png"; bak = WORK / "tarasque_final_prechin.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB"))
hsv = cv2.cvtColor(im, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
hue, sat, val = hsv[..., 0] * 360 / 256, hsv[..., 1] / 255, hsv[..., 2] / 255
box = np.zeros(im.shape[:2], bool); box[1755:1866, 560:628] = True
gold = (hue > 25) & (hue < 60) & (sat > 0.3) & box
dark = (val < 0.35) & box
dark[1848:] = False                                            # keep the chin outline itself
m = gold | dark
nub = np.zeros(im.shape[:2], np.uint8)
cv2.fillPoly(nub, [np.array([(586, 1758), (604, 1758), (628, 1856), (564, 1856)], np.int32)], 1)   # the whole nub shape incl. anti-aliased edge
m = m | nub.astype(bool)
m = cv2.dilate(m.astype(np.uint8), np.ones((9, 9), np.uint8)); m[1858:] = 0
# fill source = pale fur only (gold/dark neighbours would re-grow the nub): paint the hole with the median pale fur,
# then Telea only a 3 px seam so it blends
# fill = vertical gradient of the pale chin fur: top = the fur row just above the nub, bottom = the fur colour just
# above the chin line on the clean left side; + the paper-like grain; feathered 2 px (no rectangle edges)
top = np.median(im[1748:1756, 566:630].reshape(-1, 3), 0).astype(np.float32)
bot = np.median(im[1828:1840, 540:560].reshape(-1, 3), 0).astype(np.float32)
t = np.clip((np.arange(im.shape[0], dtype=np.float32) - 1756) / (1846 - 1756), 0, 1)[:, None, None]
grad = top[None, None] * (1 - t) + bot[None, None] * t
g = cv2.GaussianBlur(np.random.default_rng(7).normal(0, 1, im.shape[:2]).astype(np.float32), (0, 0), 1.2) * 4
fill = grad + g[..., None]
a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 2.0)[..., None]
out = im.astype(np.float32) * (1 - a) + fill * a
# re-ink the chin line where the nub base broke it
ink = np.zeros(im.shape[:2], np.uint8)
cv2.polylines(ink, [np.array([(548, 1844), (575, 1852), (605, 1856), (636, 1852)], np.int32)], False, 1, 12)
ia = cv2.GaussianBlur(ink.astype(np.float32), (0, 0), 1.2)[..., None] * box[..., None]
out = out * (1 - ia) + np.array([59, 28, 38], np.float32) * ia
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(p)
print("masked px", int(m.sum()), "gold", int(gold.sum()))
