# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): shared paint helpers of the enemy fixes (1x px, float RGB).
"""Shared paint helpers for enemy final fixes (1x px, float RGB arrays)."""
import numpy as np, cv2
INKC = np.array([59, 28, 38], np.float32)


def paint(out, m, col, blur=1.0):
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), blur)[..., None]
    out[:] = out * (1 - a) + np.array(col, np.float32) * a


def outline(out, m, w=3, below=None):
    o = np.zeros(m.shape, np.uint8)
    cs, _ = cv2.findContours(m.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(o, cs, -1, 1, w)
    if below is not None:
        o[:below] = 0
    paint(out, o, INKC, .8)


def polyline_mask(shape, pts, w0, w1=None):
    w1 = w0 if w1 is None else w1
    m = np.zeros(shape, np.uint8)
    pts = [tuple(int(v) for v in p) for p in pts]
    for k, ((x0, y0), (x1, y1)) in enumerate(zip(pts[:-1], pts[1:])):
        w = int(w0 + (w1 - w0) * k / max(len(pts) - 2, 1))
        cv2.line(m, (x0, y0), (x1, y1), 1, max(w, 1)); cv2.circle(m, (x1, y1), max(w // 2, 1), 1, -1)
    cv2.circle(m, pts[0], max(int(w0) // 2, 1), 1, -1)
    return m.astype(bool)


def arc(p0, p1, bulge, n=24):
    """Quadratic curve from p0 to p1 with the control point pushed sideways by `bulge` px."""
    p0, p1 = np.array(p0, np.float32), np.array(p1, np.float32)
    mid = (p0 + p1) / 2; d = p1 - p0; nrm = np.array([-d[1], d[0]]) / (np.linalg.norm(d) + 1e-6)
    c = mid + nrm * bulge
    t = np.linspace(0, 1, n)[:, None]
    return ((1 - t) ** 2 * p0 + 2 * (1 - t) * t * c + t ** 2 * p1).tolist()


def paper_like(im):
    p = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, :30].reshape(-1, 3), im[:, -30:].reshape(-1, 3)]), 0)
    return (p[None, None] + np.random.default_rng(5).normal(0, 1.8, im.shape)).clip(0, 255)
