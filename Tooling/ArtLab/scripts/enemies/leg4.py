# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Stalker: the far hind leg repainted solid after the lock.
"""Post-finish: the far hind leg (between the near hind and near front legs) came out ghost-pale in the lock. It is
repainted on the final (2x px) as a solid darker leg with a paw and plum outline, so exactly 4 legs read."""
import sys
from common import *
import cv2, shutil
from fixlib import *
p = T4 / "stalker_final.png"; bak = WORK / "stalker_final_preleg.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB")).astype(np.float32); Hh, Ww = im.shape[:2]
leg = polyline_mask((Hh, Ww), [(768, 1850), (762, 1930), (768, 1985)], 44, 36)
paw = np.zeros((Hh, Ww), np.uint8); cv2.ellipse(paw, (774, 1992), (32, 17), 0, 0, 360, 1, -1)
m = leg | paw.astype(bool); m[:1856] = False
clear = np.zeros((Hh, Ww), bool); clear[1868:2030, 700:796] = True
out = im.copy(); pap = paper_like(im)
a = cv2.GaussianBlur((clear & ~m).astype(np.float32), (0, 0), 1.5)[..., None]; out = out * (1 - a) + pap * a
paint(out, m, (92, 82, 78)); outline(out, m, 7, below=1872)
for t in (-1, 1):
    cv2.line(out, (774 + t * 12, 1996), (776 + t * 12, 2010), tuple(INKC.tolist()), 4)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(p); print("leg4 ok")
