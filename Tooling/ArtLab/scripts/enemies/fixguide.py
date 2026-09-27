# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): the generic guide step of the enemy fixes (fix contours into the canny, painted colours kept).
"""Generic guide step for enemy fixes: after `gen4.py prep TYPE ../work/pick.png`, add the contours of
work/fix_mask.npy to the canny and keep the painted colours of the fixed region in the swatch block / soft init
(unblurred where work/keep_mask.npy exists).   python fixguide.py TYPE   (ARTLAB_OUT = that enemy's folder)"""
import sys
from common import *
import cv2
t = sys.argv[1]
e = np.asarray(Image.open(WORK / f"sketch_{t}_canny.png").convert("L")).copy()
fp = WORK / "fix_mask.npy"
pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
blk = np.asarray(Image.open(WORK / f"sketch_{t}_block.png").convert("RGB")).copy()
if fp.exists():
    fm = np.load(fp).astype(np.uint8)
    cs, _ = cv2.findContours(fm, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE); cv2.drawContours(e, cs, -1, 255, 2)
    blk[fm.astype(bool)] = pick[fm.astype(bool)]
Image.fromarray(e).convert("RGB").save(WORK / f"sketch_{t}_canny.png")
Image.fromarray(blk).save(WORK / f"sketch_{t}_block.png")
from sketches_soft import soften
s = np.asarray(soften(Image.fromarray(blk))).copy()
kp = WORK / "keep_mask.npy"
if kp.exists():
    km = np.load(kp); s[km] = pick[km]
Image.fromarray(s).save(WORK / f"sketch_{t}_soft.png")
print("fixguide ok", t)
