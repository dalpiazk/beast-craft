"""soften(): the colour-only img2img init (blur sigma 10, warm top-left light gradient, grain sigma 14)."""
from common import *
import cv2


def soften(block, seed=0):
    a = np.asarray(block).astype(np.float32)
    a = cv2.GaussianBlur(a, (0, 0), 10)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    light = 1.06 - 0.14 * ((xx / W) * 0.4 + (yy / H) * 0.6)
    a = a * light[..., None]
    a += np.random.default_rng(seed).normal(0, 14, a.shape).astype(np.float32)
    return Image.fromarray(a.clip(0, 255).astype(np.uint8))
