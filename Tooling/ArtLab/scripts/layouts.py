"""Exploration inits: RANDOMISED rough colour-mass layouts (no lines; small 'proud' eyes), so img2img at high denoise
only inherits mass, proportion (head:body ~1:3-1:4) and palette, and the model invents the design.
Why: txt2img (even with IP 0) never drew a 'living hillside' (always a furry wolf/lion on a rock) and the fire-bird
style refs turned the kirin into a fire spirit (round 4).

  python layouts.py golem N  |  python layouts.py kirin N   -> $ARTLAB_WORK/layouts/{beast}_{i}_{block,soft}.png
  (numpy seeds 100+i; the finals used N = 16, of which 0-13 went to exploration)
"""
import sys
from common import *
from soften import soften
import cv2

SS = 2


def hx(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def poly(d, pts, c):
    d.polygon([(x * SS, y * SS) for x, y in pts], fill=c)


def ell(d, cx, cy, rx, ry, c):
    d.ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=c)


def limb(d, x0, y0, x1, y1, w0, w1, c):
    v = np.array([x1 - x0, y1 - y0], float); n = np.array([-v[1], v[0]]) / (np.linalg.norm(v) + 1e-6)
    poly(d, [(x0 + n[0] * w0 / 2, y0 + n[1] * w0 / 2), (x1 + n[0] * w1 / 2, y1 + n[1] * w1 / 2),
             (x1 - n[0] * w1 / 2, y1 - n[1] * w1 / 2), (x0 - n[0] * w0 / 2, y0 - n[1] * w0 / 2)], c)


def golem(d, r):
    C = {k: hx(v) for k, v in dict(stone="a89c8c", dark="8a7a68", deep="6b5f55", moss="6f8f4a", moss2="9fbf6a",
                                    sand="d8c8a8", flower="f2c96a").items()}
    ground = 1000
    bw, bh = r.uniform(250, 330), r.uniform(170, 240)               # hill body half-width / height
    legh = r.uniform(120, 170)
    cx = r.uniform(480, 540); by = ground - legh - bh * 0.85         # body centre y: legs clearly visible
    # back legs (darker), then body
    for lx in (cx + bw * 0.45, cx + bw * 0.15):
        limb(d, lx, by, lx + r.uniform(-10, 20), ground - 10, r.uniform(90, 120), r.uniform(80, 110), C["deep"])
    ell(d, cx, by - bh * 0.1, bw, bh, C["stone"])                  # hill body
    d.chord([(cx - bw * 0.95) * SS, (by - bh * 1.1) * SS, (cx + bw * 0.95) * SS, (by + bh * 0.5) * SS], 180, 360, fill=C["moss"])
    for _ in range(r.integers(3, 6)):                                # moss highlights
        ell(d, cx + r.uniform(-bw * .7, bw * .7), by - bh * r.uniform(.55, .9), r.uniform(40, 90), r.uniform(15, 30), C["moss2"])
    for _ in range(r.integers(2, 5)):                                # standing stones on the back
        sx = cx + r.uniform(-bw * .5, bw * .6); sh = r.uniform(70, 150); sw = r.uniform(26, 44)
        top = by - bh * 1.0 - sh * r.uniform(.6, 1.0)
        poly(d, [(sx - sw / 2, by - bh * .7), (sx - sw / 2.4, top + 10), (sx, top), (sx + sw / 2.4, top + 10), (sx + sw / 2, by - bh * .7)], C["dark"])
    for _ in range(r.integers(1, 3)):                                # wildflower patch
        fx, fy = cx + r.uniform(-bw * .6, bw * .4), by - bh * r.uniform(.7, .95)
        for _ in range(7):
            ell(d, fx + r.uniform(-35, 35), fy + r.uniform(-12, 12), 7, 7, C["flower"] if r.random() < .6 else (255, 246, 230))
    for _ in range(r.integers(2, 4)):                                # boulder bumps on the flank
        ell(d, cx + r.uniform(-bw * .5, bw * .8), by + r.uniform(0, bh * .6), r.uniform(25, 45), r.uniform(20, 35), C["dark"])
    # front legs
    for lx in (cx - bw * 0.55, cx - bw * 0.15):
        limb(d, lx, by + bh * .2, lx - r.uniform(0, 25), ground, r.uniform(105, 135), r.uniform(100, 125), C["stone"])
        ell(d, lx - 12, ground - 4, 55, 16, C["sand"])
    # head: front-left, ~1/3.5 of total height, sits low and forward (heavy, protective)
    hr = r.uniform(85, 110)
    hx_, hy = cx - bw * r.uniform(0.85, 1.0), by - bh * r.uniform(0.0, 0.4)
    ell(d, hx_, hy, hr * 1.15, hr, C["stone"])
    ell(d, hx_ - hr * .35, hy + hr * .35, hr * .7, hr * .45, C["sand"])     # muzzle/jaw
    d.chord([(hx_ - hr * 1.1) * SS, (hy - hr * 1.05) * SS, (hx_ + hr * 1.1) * SS, (hy + hr * .2) * SS], 190, 350, fill=C["moss"])  # mossy brow
    proud_eye(d, hx_ - hr * .45, hy - hr * .05, hr * .22, -8, "f2c96a")
    proud_eye(d, hx_ + hr * .3, hy - hr * .08, hr * .19, -8, "f2c96a")
    limb(d, hx_ - hr * .75, hy + hr * .55, hx_ - hr * .1, hy + hr * .62, 5, 4, hx("6b5f55"))   # firm mouth line


def kirin(d, r):
    C = {k: hx(v) for k, v in dict(fur="fff3de", fur2="f3d9a0", gold="e8b04a", amber="c9822e", deep="8a4f24",
                                    peach="f2b89a").items()}
    ground = 1010
    bx, by = r.uniform(520, 570), r.uniform(650, 700)                # body centre
    bw, bh = r.uniform(200, 235), r.uniform(110, 135)
    legl = ground - by - bh * .4
    raise_leg = r.random() < .5
    # far legs
    for lx in (bx + bw * .55, bx - bw * .35):
        limb(d, lx, by + bh * .3, lx + r.uniform(-15, 15), ground - 15, 50, 32, C["fur2"])
        ell(d, lx, ground - 12, 18, 12, C["deep"])
    # tail tuft
    tx = bx + bw * 1.0
    for k in range(3):
        ell(d, tx + 30 + k * 25, by - 40 - k * 35 * r.uniform(.6, 1.2), 45 - k * 8, 30 - k * 4, C["fur2"] if k else C["gold"])
    ell(d, bx, by, bw, bh, C["fur"])                                  # body
    ell(d, bx - bw * .1, by + bh * .45, bw * .7, bh * .35, C["fur2"])
    for _ in range(r.integers(3, 6)):                                # amber scale accents along the back/flank
        ell(d, bx + r.uniform(-bw * .6, bw * .6), by - bh * r.uniform(.1, .6), r.uniform(14, 24), r.uniform(9, 14), C["amber"])
    # near legs
    for i, lx in enumerate((bx + bw * .45, bx - bw * .55)):
        if i == 1 and raise_leg:                                     # proud raised foreleg
            kx, ky = lx - 70, by + bh * 1.25
            limb(d, lx, by + bh * .2, kx, ky, 58, 40, C["fur"]); limb(d, kx, ky, kx + 25, ky + 110, 40, 32, C["fur"])
            ell(d, kx + 25, ky + 112, 22, 15, C["deep"])
        else:
            limb(d, lx, by + bh * .2, lx + r.uniform(-20, 10), ground, 58, 36, C["fur"]); ell(d, lx, ground - 4, 24, 15, C["deep"])
    # neck + head: upright, head ~1/3.5 of total height
    head_up = r.uniform(0.9, 1.15)
    nx, ny = bx - bw * .75, by - bh * .6
    hx_, hy = nx - r.uniform(20, 60), by - bh - r.uniform(150, 200) * head_up
    limb(d, nx + 30, by - bh * .2, hx_ + 20, hy + 40, 120, 80, C["fur"])
    # mane: flowing mass down the neck, pale gold with amber shadow
    for k in range(5):
        t = k / 4
        ell(d, hx_ + 60 + t * 90, hy + 10 + t * 170, 55 - t * 10, 60, C["gold"] if k % 2 else C["fur2"])
    hr = r.uniform(95, 115)
    ell(d, hx_, hy, hr * 1.05, hr, C["fur"])
    ell(d, hx_ - hr * .8, hy + hr * .3, hr * .55, hr * .4, C["fur2"])      # snout
    ell(d, hx_ + hr * .35, hy - hr * .8, hr * .22, hr * .45, C["fur2"])    # ear
    proud_eye(d, hx_ - hr * .15, hy - hr * .05, hr * .26, -12, "c9822e")
    ell(d, hx_ - hr * 1.2, hy + hr * .2, hr * .08, hr * .06, C["deep"])       # nostril
    # single curved horn (amber -> gold)
    hb = (hx_ - hr * .1, hy - hr * .9)
    pts = [(hb[0] + 4 * t * 10 * r.uniform(.9, 1.1), hb[1] - t * 130 * head_up) for t in np.linspace(0, 1, 8)]
    pts = [(x + 60 * (t ** 2), y) for (x, y), t in zip(pts, np.linspace(0, 1, 8))]
    for (x0, y0), (x1, y1), w in zip(pts[:-1], pts[1:], np.linspace(26, 6, 7)):
        limb(d, x0, y0, x1, y1, w, max(w - 3, 4), C["amber"])


def proud_eye(d, cx, cy, w, tilt, iris):
    """Almond eye (not a big round baby eye) with an amber iris, highlight and a heavy brow stroke above
    = proud/determined read."""
    e = Image.new("L", (int(w * 2.4) * SS, int(w * 2.4) * SS), 0)
    ImageDraw.Draw(e).ellipse([int(w * .2) * SS, int(w * .75) * SS, int(w * 2.2) * SS, int(w * 1.65) * SS], fill=255)
    e = e.rotate(tilt, resample=Image.BICUBIC)
    ox, oy = int((cx - w * 1.2) * SS), int((cy - w * 1.2) * SS)
    d._image.paste(Image.new("RGB", e.size, hx("3b1c26")), (ox, oy), e)
    ell(d, cx + w * .1, cy + w * .05, w * .45, w * .38, hx(iris))
    ell(d, cx - w * .1, cy - w * .12, w * .16, w * .13, (255, 250, 235))
    limb(d, cx - w * 1.1, cy - w * .55 + tilt * .4, cx + w * 1.0, cy - w * .8 - tilt * .3, w * .32, w * .18, hx("3b1c26"))


DRAW = {"golem": golem, "kirin": kirin}


def make(beast, n, seed0=100):
    out = WORK / "layouts"; out.mkdir(parents=True, exist_ok=True)
    ims = []
    for i in range(n):
        r = np.random.default_rng(seed0 + i)
        img = Image.new("RGB", (W * SS, H * SS), BG)
        DRAW[beast](ImageDraw.Draw(img), r)
        blk = img.resize((W, H), Image.LANCZOS)
        blk.save(out / f"{beast}_{i}_block.png")
        s = soften(blk, seed=i); s.save(out / f"{beast}_{i}_soft.png")
        ims.append(blk)
    contact_sheet(ims, out / f"{beast}_layouts.jpg", cols=8, thumb=180)


if __name__ == "__main__":
    make(sys.argv[1], int(sys.argv[2]))
