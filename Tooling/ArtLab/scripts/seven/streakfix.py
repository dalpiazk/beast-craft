# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Griffin: the stray ghost stroke above the head crest paper-filled.
"""Stray fragment above the griffin's head crest: a pale/tan ghost stroke on the paper (2x px ~850-1040, 650-700),
outside the character outline. Paper-filled (paper colour + grain) only outside the line mask, nothing else touched."""
from common import *
import cv2, shutil
p = T4 / "griffin_final.png"; bak = WORK / "griffin_final_prestreak.png"
if not bak.exists():
    shutil.copy(p, bak)
im = np.asarray(Image.open(bak).convert("RGB")).astype(np.float32)
lm = np.asarray(Image.open(WORK / "griffin_lines_mask.png").convert("L").resize((im.shape[1], im.shape[0]))) > 127
lm = cv2.dilate(lm.astype(np.uint8), np.ones((15, 15), np.uint8)).astype(bool)
box = np.zeros(im.shape[:2], bool); box[630:712, 820:1060] = True
m = box & ~lm
pap = np.median(im[560:620, 820:1060].reshape(-1, 3), 0)
fill = pap[None, None] + np.random.default_rng(9).normal(0, 1.5, im.shape)
a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 3)[..., None]
Image.fromarray((im * (1 - a) + fill * a).clip(0, 255).astype(np.uint8)).save(p); print("streak px", int(m.sum()))
