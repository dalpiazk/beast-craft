# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Caster (brute_L8): the glowing orb between the antlers (the element accent).
"""Caster (brute_L8): the spell glow between the antlers = ELEMENT ACCENT. A small glowing orb is painted between
the two antlers (it was not clearly present), with a soft halo and thin spark lines toward both antler tips.
Two legs kept as drawn (producer)."""
import sys
from common import *
import cv2
from fixlib import *
im = np.asarray(Image.open(WORK / "pick_before_fix.png").convert("RGB")).astype(np.float32)
out = im.copy()
C, R_ = (500, 420), 40
halo = np.zeros((H, W), np.float32); cv2.circle(halo, C, 95, 1, -1); halo = cv2.GaussianBlur(halo, (0, 0), 30) * 0.55
out = out * (1 - halo[..., None]) + np.array([196, 228, 150], np.float32) * halo[..., None]
for tip in ((300, 250), (680, 330)):                                   # spark threads to the antler tips
    ln = polyline_mask((H, W), arc(C, tip, 18 if tip[0] < 500 else -18), 4)
    paint(out, ln, (214, 240, 170), 1.2)
orb = np.zeros((H, W), np.uint8); cv2.circle(orb, C, R_, 1, -1)
paint(out, orb, (143, 184, 90), 1.0)
core = np.zeros((H, W), np.uint8); cv2.circle(core, (C[0] - 10, C[1] - 10), 16, 1, -1)
paint(out, core, (236, 250, 210), 4)
outline(out, orb.astype(bool), 3)
acc = cv2.dilate(orb, np.ones((9, 9), np.uint8)).astype(bool)
np.save(WORK / "accent_mask.npy", acc)
fix = orb.astype(bool)
Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
np.save(WORK / "fix_mask.npy", fix); np.save(WORK / "keep_mask.npy", cv2.dilate(orb, np.ones((25, 25), np.uint8)).astype(bool))
print("orb ok")
