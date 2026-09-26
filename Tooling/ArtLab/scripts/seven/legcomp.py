# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque V3: legs from the 0.80 lock composited below the belly of the 0.60 lock.
"""Tarasque leg variants, pass 2: the 0.60 lock kept the painted legs as flat pegs. A 0.80 lock (same canny + leg
guide) re-paints them as real legs; it is composited only BELOW the belly line (feathered 40 px) so the approved
head/shell stay from the 0.60 lock.   python legcomp.py V"""
import sys
from common import *
import cv2
from legfix import VAR
v = int(sys.argv[1]); c = VAR.get(v, {'belly': 930}); hs = sys.argv[2] if len(sys.argv) > 2 else "11"
lo = np.asarray(Image.open(WORK / "v" / f"v{v}_tarasque_11.png").convert("RGB")).astype(np.float32)
hi = np.asarray(Image.open(WORK / "v" / f"v{v}hi_tarasque_{hs}.png").convert("RGB")).astype(np.float32)
y0 = c["belly"] - 70
a = np.clip((np.arange(H, dtype=np.float32) - y0) / 40.0, 0, 1)[:, None, None]
Image.fromarray((lo * (1 - a) + hi * a).clip(0, 255).astype(np.uint8)).save(WORK / "v" / f"v{v}c_tarasque_{hs}.png")
print("composited", v, "from y", y0)
