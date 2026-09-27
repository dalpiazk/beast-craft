# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Stingling (shaman_t14): the stick removed, a stinger tail added.
"""Stingling (shaman_t14): producer "without the stick that's under it" + add a small visible stinger (poison barb)
with a glowing tip = ELEMENT ACCENT. The branch (both loops, the crossing under the body and the violet flower on it)
is removed to paper; 2 short legs with feet are kept/restored under the fur; a curved barb tail grows from the lower
back. Face and fur unchanged."""
import sys
from common import *
import cv2
from fixlib import *
from masks import line_mask
im = np.asarray(Image.open(WORK / "pick_before_fix.png").convert("RGB")).astype(np.float32)
lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
char = line_mask(im.astype(np.uint8))[0]
hsv = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32); hue = hsv[..., 0] * 360 / 256; sat = hsv[..., 1] / 255
R = np.zeros((H, W), np.uint8)
cv2.fillPoly(R, [np.array([(0, 730), (330, 730), (330, 1152), (0, 1152)], np.int32)], 1)
cv2.fillPoly(R, [np.array([(712, 440), (896, 440), (896, 1152), (560, 1152), (600, 820), (650, 720), (712, 640)], np.int32)], 1)
R = R.astype(bool) | polyline_mask((H, W), [(280, 1010), (380, 960), (470, 905), (560, 855), (650, 810)], 90)
brown = (hue > 10) & (hue < 60) & (sat > 0.28)
dark = lab[..., 0] < 88
violet = (hue > 230) & (hue < 300) & (sat > 0.2)
leaf = (hue > 60) & (hue < 110) & (sat > 0.35)
branch = R & (brown | dark | violet | leaf) & char
branch = cv2.morphologyEx(branch.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
branch = cv2.dilate(branch, np.ones((7, 7), np.uint8)).astype(bool) & R
# fill: fur (Telea from the non-branch fur) where the branch crossed in front of the fur, paper elsewhere
furzone = np.zeros((H, W), np.uint8)
cv2.fillPoly(furzone, [np.array([(275, 560), (705, 560), (690, 800), (640, 880), (560, 905), (420, 910), (300, 880), (275, 760)], np.int32)], 1)
furzone = furzone.astype(bool)
tel = cv2.inpaint(im.astype(np.uint8), branch.astype(np.uint8) * 255, 11, cv2.INPAINT_TELEA).astype(np.float32)
out = im.copy()
pap = paper_like(im)
af = cv2.GaussianBlur((branch & furzone).astype(np.float32), (0, 0), 1.5)[..., None]
ap = cv2.GaussianBlur((branch & ~furzone).astype(np.float32), (0, 0), 1.5)[..., None]
out = out * (1 - af) + tel * af
out = out * (1 - ap) + pap * ap
# leftover small specks of non-paper in R outside the fur -> paper
lab2 = cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
pp = np.median(pap.reshape(-1, 3), 0)
spk = R & ~furzone & (np.abs(out - pp).sum(-1) > 25)
n, lb, st, _ = cv2.connectedComponentsWithStats(spk.astype(np.uint8), 8)
spk = np.isin(lb, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] < 4000])
a3 = cv2.GaussianBlur(spk.astype(np.float32), (0, 0), 1.2)[..., None]
out = out * (1 - a3) + pap * a3
rm = branch | spk
flw = np.zeros((H, W), bool); flw[470:720, 725:860] = True     # the violet flower on the stick (incl. its glow) -> paper
af2 = cv2.GaussianBlur(flw.astype(np.float32), (0, 0), 3)[..., None]
out = out * (1 - af2) + pap * af2
fix = np.zeros((H, W), bool)
LEG, LEG_D, CLAW = (84, 92, 104), (58, 62, 74), (220, 214, 196)
# 2 short legs + feet under the fur (left mirrors the original right leg)
for x in (455, 560):
    leg = polyline_mask((H, W), [(x, 890), (x + 6, 975)], 42, 36)
    foot = np.zeros((H, W), np.uint8); cv2.ellipse(foot, (x + 14, 985), (34, 16), 0, 0, 360, 1, -1)
    m = leg | foot.astype(bool)
    paint(out, m, LEG if x > 500 else LEG_D); outline(out, m, 3, below=900)
    for t in (-1, 0, 1):
        cv2.ellipse(out, (x + 14 + t * 16, 998), (6, 5), 0, 0, 360, CLAW, -1)
    fix |= m
# stinger / poison barb from the lower back, curving out and up at the tip
st = arc((600, 870), (748, 880), -48, 30)
stm = polyline_mask((H, W), st, 30, 8)
paint(out, stm, (70, 60, 78)); outline(out, stm, 3)
barb = np.zeros((H, W), np.uint8); cv2.fillPoly(barb, [np.array([(736, 868), (782, 876), (740, 896)], np.int32)], 1)
paint(out, barb, (143, 184, 90)); outline(out, barb.astype(bool), 3)
glow = cv2.GaussianBlur(barb.astype(np.float32), (0, 0), 9) * 0.6
out = out * (1 - glow[..., None]) + np.array([200, 236, 150], np.float32) * glow[..., None]
fix |= stm | barb.astype(bool)
np.save(WORK / "accent_mask.npy", cv2.dilate(barb, np.ones((9, 9), np.uint8)).astype(bool))
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
np.save(WORK / "fix_mask.npy", fix); np.save(WORK / "keep_mask.npy", cv2.dilate(fix.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool))
print("stingling fix ok", int(rm.sum()))
