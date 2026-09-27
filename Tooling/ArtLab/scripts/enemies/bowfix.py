# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Archer (#1, archer_L10): one curved strung twig bow, nocked arrow, accent fletching.
"""Archer (archer_L10): rebuild ONE intact curved twig bow with a taut string. The old straight stick is removed;
the bow is held left of the body by the near hand (existing arm extended into a fist), the string drawn back to the
draw hand at the chest, arrow nocked; fletching = ELEMENT ACCENT. The Gloam wisp is kept."""
import sys
from common import *
import cv2
from fixlib import *
im = np.asarray(Image.open(WORK / "pick_before_fix.png").convert("RGB")).astype(np.float32)
out = im.copy()
lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
hsv = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32); hue = hsv[..., 0] * 360 / 256
# 1) old stick (warm brown/pink, not the lavender wisp, not green leaves) left of the body -> paper / wisp colour
zone = np.zeros((H, W), bool); zone[780:840, 250:372] = True
stick = zone & (hue < 40) & (hsv[..., 1] > 40)
stick = cv2.dilate(stick.astype(np.uint8), np.ones((7, 7), np.uint8)).astype(bool) & zone
inpaint = cv2.inpaint(im.clip(0, 255).astype(np.uint8), stick.astype(np.uint8) * 255, 7, cv2.INPAINT_TELEA).astype(np.float32)
a = cv2.GaussianBlur(stick.astype(np.float32), (0, 0), 1.2)[..., None]
out = out * (1 - a) + inpaint * a
# 1b) the old stick's other end (behind the draw hand, up to the right wisp) -> repainted as the quiver/paper around it
band = polyline_mask((H, W), [(478, 764), (560, 730), (640, 700), (668, 690)], 22)
tan = band & (lab[..., 0] > 95) & (hue < 60)
tan = cv2.dilate(tan.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool) & band
inp2 = cv2.inpaint(out.clip(0, 255).astype(np.uint8), tan.astype(np.uint8) * 255, 9, cv2.INPAINT_TELEA).astype(np.float32)
a2 = cv2.GaussianBlur(tan.astype(np.float32), (0, 0), 1.2)[..., None]
out = out * (1 - a2) + inp2 * a2
fix = np.zeros((H, W), bool)
BARK, BARK_L = (112, 80, 52), (156, 118, 80)
armcol = np.array([160, 132, 114], np.float32)          # the candidate's own hand colour (tan)
# 2) bow: two limbs curving away from the archer, grip at (318, 800)
top = arc((318, 800), (338, 630), -34); bot = arc((318, 800), (338, 970), 34)
bow = polyline_mask((H, W), top, 16, 8) | polyline_mask((H, W), bot, 16, 8)
paint(out, bow, BARK); paint(out, polyline_mask((H, W), [(x - 3, y) for x, y in top[2:-3]], 4) , BARK_L, .6)
outline(out, bow, 3); fix |= bow
# 3) taut string, drawn back to the draw hand at (420, 796)
string = polyline_mask((H, W), [(338, 630), (420, 796), (338, 970)], 3)
paint(out, string, (236, 228, 206), .5); fix |= string
# 4) arrow: shaft from the draw hand forward past the bow, arrowhead, fletching = ACCENT
shaft = polyline_mask((H, W), [(428, 796), (262, 798)], 6)
paint(out, shaft, (98, 72, 50), .6); outline(out, shaft, 2); fix |= shaft
head = np.zeros((H, W), np.uint8); cv2.fillPoly(head, [np.array([(264, 786), (236, 798), (264, 810)], np.int32)], 1)
paint(out, head, (150, 150, 140), .6); outline(out, head.astype(bool), 2); fix |= head.astype(bool)
fl = np.zeros((H, W), np.uint8)
cv2.fillPoly(fl, [np.array([(404, 796), (428, 780), (440, 782), (420, 796)], np.int32)], 1)
cv2.fillPoly(fl, [np.array([(404, 798), (428, 814), (440, 812), (420, 798)], np.int32)], 1)
paint(out, fl, (143, 184, 90), .6); outline(out, fl.astype(bool), 2); fix |= fl.astype(bool)
np.save(WORK / "accent_mask.npy", fl.astype(bool))
# 5) bow hand: forearm from the existing near hand (372,776) to a fist gripping the bow at (318,800)
fore = polyline_mask((H, W), [(376, 776), (340, 796)], 26, 22)
paint(out, fore, armcol); outline(out, fore & ~cv2.dilate(bow.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool), 2)
fist = np.zeros((H, W), np.uint8); cv2.ellipse(fist, (322, 800), (18, 20), 0, 0, 360, 1, -1)
paint(out, fist, armcol); outline(out, fist.astype(bool), 3)
for k in range(1, 3):
    cv2.line(out, (306, 790 + k * 7), (330, 792 + k * 7), tuple(INKC.tolist()), 2)
fix |= fore | fist.astype(bool)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
np.save(WORK / "fix_mask.npy", fix); np.save(WORK / "keep_mask.npy", cv2.dilate(fix.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool))
print("bow ok")
