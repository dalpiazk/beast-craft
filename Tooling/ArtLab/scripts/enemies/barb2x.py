# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Stingling: the glowing barb repainted sharp after the lock; accent mask re-saved.
"""Post-finish: the lock softened the glowing barb into a dull knob. It is repainted on the final (2x px) as a sharp
hooked stinger with a glowing green tip (accent 8fb85a) and plum outline; the accent mask is re-saved at 1x."""
import sys
from common import *
import cv2, shutil
from fixlib import *
p = T4 / "stingling_final.png"; bak = WORK / "stingling_final_prebarb.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB")).astype(np.float32); Hh, Ww = im.shape[:2]
out = im.copy(); pap = paper_like(im)
clear = np.zeros((Hh, Ww), np.uint8); cv2.ellipse(clear, (1506, 1776), (56, 48), 0, 0, 360, 1, -1); clear = clear.astype(bool)
clear[:, :1456] = False; clear[:1748, :1490] = False
tel = np.broadcast_to(np.median(im[1830:1890, 1480:1600].reshape(-1, 3), 0), im.shape).astype(np.float32)   # local paper tone
g = np.random.default_rng(3).normal(0, 2.5, im.shape).astype(np.float32)
a = cv2.GaussianBlur(clear.astype(np.float32), (0, 0), 2)[..., None]; out = out * (1 - a) + (tel + g) * a
# hooked barb: base on the tail end, curving up to a sharp point
barb = np.zeros((Hh, Ww), np.uint8)
pts = np.array(arc((1436, 1708), (1600, 1660), 12, 12)[:-1] + [(1600, 1660)] + arc((1600, 1660), (1440, 1746), -50, 14)[1:], np.int32)
cv2.fillPoly(barb, [pts], 1); barb = barb.astype(bool)
paint(out, barb, (143, 184, 90))
sh = barb.copy(); sh[:, 1500:] = False
paint(out, sh, (104, 128, 76), 8)
core = polyline_mask((Hh, Ww), [(1500, 1732), (1540, 1714), (1578, 1684)], 16, 6)
paint(out, core, (222, 246, 180), 4)
o = np.zeros((Hh, Ww), np.uint8); cs, _ = cv2.findContours(barb.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
cv2.drawContours(o, cs, -1, 1, 7); o[:, :1452] = 0; o[1712:1740, :1466] = 0
paint(out, o, INKC, .8)   # no seam where the barb sits on the tail
glow = cv2.GaussianBlur(barb.astype(np.float32), (0, 0), 26); glow = glow / glow.max() * 0.7 * (1 - cv2.GaussianBlur(barb.astype(np.float32), (0, 0), 2))
out = out * (1 - glow[..., None]) + np.array([196, 236, 140], np.float32) * glow[..., None]
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(p)
m1 = cv2.resize(cv2.dilate(barb.astype(np.uint8), np.ones((15, 15), np.uint8)), (W, H), interpolation=cv2.INTER_NEAREST).astype(bool)
np.save(WORK / "accent_mask.npy", m1); print("barb ok", int(m1.sum()))
