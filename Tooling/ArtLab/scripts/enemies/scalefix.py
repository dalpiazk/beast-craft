# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Champion (brute_L1): scaled 1.22x about the feet for its three hexes.
"""Champion (brute_L1): 3-hex (Triangle) mini-boss -> the design is kept but scaled 1.22x about the feet so the
silhouette reads bigger than the Brute at board scale (swaggering, fills the frame)."""
import sys
from common import *
import cv2
from fixlib import paper_like
im = np.asarray(Image.open(WORK / "pick_before_fix.png").convert("RGB")).astype(np.float32)
S, CX, CY = 1.22, 440, 1010
M = np.float32([[S, 0, CX - S * CX], [0, S, CY - S * CY + 40]])
L = cv2.warpAffine(im, M, (W, H), flags=cv2.INTER_LANCZOS4, borderValue=tuple(np.median(im[:30].reshape(-1, 3), 0).tolist()))
Image.fromarray(L.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
print("champion scaled", M.tolist())
