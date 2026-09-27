"""An enemy's element-accent mask, aligned to its archived character.png (the master export_ingame.py reads).

  python accent_mask_master.py ACCENT_MASK.npy PARTS.json OUT.png [--whole-islands]

ACCENT_MASK.npy is the boolean mask the enemy finals saved (work/accent_mask.npy): 1x, on the 896x1152 working canvas,
the painted final (1792x2304) halved. It is doubled (nearest) onto the 2x canvas and cropped to character.png's frame:
parts.json's character.pivot (the feet on the 2x canvas) minus pivot_local (the feet in character.png) is where
character.png's top-left sits on the canvas (it may lie off the canvas: character.png is padded so the feet are its
bottom centre). Pixels outside the character's alpha are cleared. --whole-islands widens the mask to every detached
piece of the character (an alpha island apart from the body) that it touches: the Caster's floating spell orb, whose
saved mask covered only the inner ring of the orb the lock painted. Writes an 8-bit PNG (255 = accent), the size of
character.png. CPU only, deterministic.
"""
import json
import pathlib
import sys

import numpy as np
from PIL import Image


def whole_islands(res, alpha):
    import cv2
    n, lab, st, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8), 8)
    body = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
    for i in range(1, n):
        if i != body and res[lab == i].any():
            res |= lab == i
    return res


def main(npy, parts, out, islands=False):
    mask = np.load(npy).astype(bool)
    rig = json.loads(pathlib.Path(parts).read_text(encoding="utf-8"))
    char = pathlib.Path(parts).parent / rig["character"]["file"]
    alpha = np.asarray(Image.open(char).convert("RGBA"))[..., 3]
    big = np.repeat(np.repeat(mask, 2, 0), 2, 1)
    px, py = rig["character"]["pivot"]
    lx, ly = rig["character"]["pivot_local"]
    ox, oy = px - lx, py - ly
    h, w = alpha.shape
    res = np.zeros((h, w), bool)
    # the overlap of character.png's frame with the canvas
    x0, y0 = max(0, ox), max(0, oy)
    x1, y1 = min(big.shape[1], ox + w), min(big.shape[0], oy + h)
    if x1 > x0 and y1 > y0:
        res[y0 - oy:y1 - oy, x0 - ox:x1 - ox] = big[y0:y1, x0:x1]
    res &= alpha > 0
    if islands:
        res = whole_islands(res, alpha)
    Image.fromarray((res * 255).astype(np.uint8)).save(out, optimize=True)
    print(f"{pathlib.Path(out).name}: {w}x{h}, accent {res.sum() / max(1, (alpha > 0).sum()) * 100:.1f}% of the character")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3], "--whole-islands" in sys.argv[4:])
