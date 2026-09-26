# Beast Craft ArtLab, remaining-seven finals (2026-09-26): swatch-relative off-palette QA of the seven finals (Leviathan, Thunderbird: the cool-drift metric does not apply).
"""New-beast QA: swatch-relative drift (the trio's 'cool drift' counts every teal/blue pixel, which is on-palette for
Water/Lightning). off-palette = share of character pixels (sat > 0.2, val > 0.2) whose hue is > 30 deg from every
chromatic swatch hue (swatch colours with sat > 0.15).   python offpal.py BEAST IMG..."""
import sys
from common import *
import cv2
from colour import char_mask


def offpal(img, beast):
    rgb = np.asarray(Image.open(img).convert("RGB")) if not isinstance(img, np.ndarray) else img
    m = char_mask(rgb)
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    hue = hsv[..., 0] * 360 / 256; s = hsv[..., 1] / 255; v = hsv[..., 2] / 255
    sw = np.array([[int(h[i:i + 2], 16) for i in (0, 2, 4)] for h, _ in SWATCH[beast]], np.uint8)[None]
    sh = cv2.cvtColor(sw, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)[0]
    hues = sh[sh[:, 1] / 255 > 0.15, 0] * 360 / 256
    d = np.min(np.abs(((hue[..., None] - hues[None, None]) + 180) % 360 - 180), -1)
    off = (d > 30) & (s > 0.2) & (v > 0.2) & m
    return float(off.sum() / max(m.sum(), 1))


if __name__ == "__main__":
    b = sys.argv[1]
    print(json.dumps({pathlib.Path(p).stem: round(offpal(p, b), 4) for p in sys.argv[2:]}))
