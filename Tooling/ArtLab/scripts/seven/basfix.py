# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Basilisk round-2 #7: the far-front leg recoloured from peach to shaded plum before the lock.
"""Basilisk #7 (candidate basilisk_v2_L2) anatomy check before the lock: exactly 4 legs, one visible eye, one mouth,
tapering uncoiled tail. Found: 3 clear legs (near hind, peach far-front, near front) + an ambiguous toe blob. Fix:
(1) the peach front leg (off-palette, reads as a different limb colour) -> far-front leg in shaded plum;
(2) the dark segment between them is the far hind leg (kept). Count after the fix: 4.
  python basfix.py  -> $ARTLAB_WORK/pick.png (backup pick_before_fix.png) + legs_mask.npy"""
from common import *
import cv2, shutil
src = WORK / "pick_before_fix.png"
if not src.exists():
    shutil.copy(WORK / "pick.png", src)
im = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
out = im.copy()
# (1) peach far-front leg region -> plum, keeping its own value structure (luminance-preserving recolour)
leg = np.zeros((H, W), np.uint8)
cv2.fillPoly(leg, [np.array([(522, 850), (585, 848), (590, 960), (615, 1000), (540, 1002), (530, 960)], np.int32)], 1)
lab = cv2.cvtColor(im.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
paper = np.median(np.concatenate([lab[:30].reshape(-1, 3), lab[:, :30].reshape(-1, 3)]), 0)
notpaper = np.linalg.norm(lab - paper, axis=-1) > 14
peach = (lab[..., 2] > 136) & (lab[..., 0] > 95) & leg.astype(bool) & notpaper   # warm/light leg pixels only
tgt = cv2.cvtColor(np.array([[[74, 47, 94]]], np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)[0, 0]
nl = lab.copy()
nl[..., 0] = lab[..., 0] * 0.55 + 10                                            # darker (far side)
nl[..., 1] = tgt[1]; nl[..., 2] = tgt[2]
rec = cv2.cvtColor(nl.clip(0, 255).astype(np.uint8), cv2.COLOR_LAB2RGB).astype(np.float32)
a = cv2.GaussianBlur(peach.astype(np.float32), (0, 0), 1.5)[..., None]
out = out * (1 - a) + rec * a
# (2) the far hind leg already exists (dark, between the near hind leg and the far-front leg); leave it
fh = np.zeros((H, W), np.uint8)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
np.save(WORK / "legs_edit_mask.npy", (leg | fh).astype(bool))
print("peach px recoloured", int(peach.sum()), "far hind px", int(fh.sum()))
