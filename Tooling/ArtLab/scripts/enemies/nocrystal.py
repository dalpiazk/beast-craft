# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Giant round 2: the crown crystals removed; the element accent moved to the moss fur.
"""Producer: "Lose the purple horns on his head" -> remove the 4 crown crystal shards from the final (2x px).
Within the shards' region: above the fur crown line -> paper; below -> fur re-grown from the surrounding moss fur
(Telea on the fur only); the crown outline is re-inked along a smooth curve only where the shards interrupted it.
Element accent moves to the mossy/lichen fur patches (mask saved).  python nocrystal.py  (backup work/giant_final_crystals.png)"""
from common import *
import cv2, shutil
p = T4 / "giant_final.png"; bak = WORK / "giant_final_crystals.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB"))
Hh, Ww = im.shape[:2]
hsv = cv2.cvtColor(im, cv2.COLOR_RGB2HSV_FULL).astype(np.float32); hue = hsv[..., 0] * 360 / 256
lab = cv2.cvtColor(im, cv2.COLOR_RGB2LAB).astype(np.float32)
zone = np.zeros((Hh, Ww), np.uint8)
cv2.fillPoly(zone, [np.array([(480, 480), (940, 480), (940, 760), (480, 760)], np.int32)], 1)
lav = (((hue > 230) & (hue < 320) & (hsv[..., 1] > 30)) | ((lab[..., 0] > 150) & (lab[..., 2] < 122))) & zone.astype(bool)
dark = (lab[..., 0] < 90) & zone.astype(bool)
# shards = lavender fill + their outline strokes (dark pixels near the lavender)
near = cv2.dilate(lav.astype(np.uint8), np.ones((31, 31), np.uint8)).astype(bool)
shard = (lav | (dark & near)).astype(np.uint8)
shard = cv2.morphologyEx(shard, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
n, lb, st, _ = cv2.connectedComponentsWithStats(shard, 8)
shard = np.isin(lb, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] > 300]).astype(np.uint8)
# the whole crown span is rebuilt (the shards + the broken outline between them): box x 470-960, y 470-712
hole = np.zeros((Hh, Ww), bool); hole[470:732, 470:960] = True; hole[722:, 690:835] = False; hole[712:745, 835:905] = True   # keep the right eye; take the 4th shard's base
pts = np.array([(440, 652), (500, 640), (560, 624), (620, 610), (690, 603), (760, 608), (830, 632), (890, 664), (935, 698), (965, 716), (1000, 733)], np.float32)
xs = np.arange(Ww); cy = np.interp(xs, pts[:, 0], pts[:, 1]) + 7 * np.sin((xs - 440) / 38.0) * ((xs > 470) & (xs < 960))   # shaggy fur ripple
above = np.arange(Hh)[:, None] < cy[None, :]
out = im.astype(np.float32).copy()
pap = np.median(np.concatenate([im[:40].reshape(-1, 3), im[:, -40:].reshape(-1, 3)]), 0)
paper = pap[None, None] + np.random.default_rng(5).normal(0, 1.8, im.shape)
fur_hole = hole & ~above
# fur re-grown from the rows below the box (the paper above is masked too so it cannot bleed in)
mask_fill = np.zeros((Hh, Ww), np.uint8); mask_fill[:732, 440:990] = 1; mask_fill[722:, 690:835] = 0
fur = cv2.inpaint(im, mask_fill * 255, 13, cv2.INPAINT_TELEA).astype(np.float32)
fur = cv2.GaussianBlur(fur, (0, 0), sigmaX=9, sigmaY=3)                  # kill the Telea vertical streaks
g = cv2.GaussianBlur(np.random.default_rng(3).normal(0, 1, (Hh, Ww)).astype(np.float32), (0, 0), sigmaX=4, sigmaY=1.5) * 7
fur = fur + g[..., None]
yy = np.arange(Hh, dtype=np.float32)[:, None]
feather = np.clip((732 - yy) / 12, 0, 1) * np.clip((np.arange(Ww)[None, :] - 470) / 16, 0, 1) * np.clip((960 - np.arange(Ww)[None, :]) / 16, 0, 1)
a_f = (cv2.GaussianBlur(fur_hole.astype(np.float32), (0, 0), 1.5) * feather)[..., None]
a_p = cv2.GaussianBlur((hole & above).astype(np.float32), (0, 0), 1.5)[..., None]
out = out * (1 - a_f) + fur * a_f
out = out * (1 - a_p) + paper * a_p
ink = np.zeros((Hh, Ww), np.uint8)
cv2.polylines(ink, [np.stack([xs[440:1001], cy[440:1001]], 1).astype(np.int32)], False, 1, 13)
ink[:, :440] = 0; ink[:, 1001:] = 0; ink = ink.astype(bool)
ia = cv2.GaussianBlur(ink.astype(np.float32), (0, 0), 1.3)[..., None]
out = out * (1 - ia) + np.array([59, 28, 38], np.float32) * ia
res = out.clip(0, 255).astype(np.uint8)
Image.fromarray(res).save(p)
# new ELEMENT ACCENT: mossy/lichen green fur patches (1x mask), inside the character only
lm = np.asarray(Image.open(WORK / "giant_lines_mask.png").convert("L").resize((Ww, Hh))) > 127
lm = cv2.erode(lm.astype(np.uint8), np.ones((15, 15), np.uint8)).astype(bool)
h2 = cv2.cvtColor(res, cv2.COLOR_RGB2HSV_FULL).astype(np.float32); hu = h2[..., 0] * 360 / 256
moss = lm & (hu > 60) & (hu < 150) & (h2[..., 1] > 55) & (h2[..., 2] > 70)
face = np.zeros((Hh, Ww), np.uint8); cv2.ellipse(face, (604, 818), (360, 200), -12, 0, 360, 1, -1)
moss &= ~face.astype(bool)
moss = cv2.morphologyEx(moss.astype(np.uint8), cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
m1 = cv2.resize(moss, (W, H), interpolation=cv2.INTER_AREA) > 0
np.save(WORK / "accent_mask.npy", m1); Image.fromarray((m1 * 255).astype(np.uint8)).save(WORK / "accent_mask.png")
np.save(WORK / "accent_mask_crystals_old.npy", np.zeros(1))
print("shard px", int(shard.sum()), "moss accent frac", round(float(m1.mean()), 3))
