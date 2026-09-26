# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque #2: four legs, the V3 walking stride (rough edit and canny guide).
"""Tarasque #2 (L2) leg fix. Producer: 'appears to have only 2 legs' -> 4 sturdy legs, a quadruped river-beast, legs
visible under the shell, far legs partly visible behind. Same method as the griffin wing fix:
rough colour edit (legs painted in the pick's own steel/iron colours + paper gaps under the belly so the legs read
as separate) -> canny from own lines + an anatomy guide (leg contours, belly line, claws) -> re-lock from the edit.

  python legfix.py build          -> $ARTLAB_WORK/v/pick_v{1,2,3}.png
  python legfix.py guide V        -> after `gen4.py prep tarasque $ARTLAB_WORK/v/pick_vV.png`: $ARTLAB_WORK/v/sketch_vV_{canny,block,soft}.png
"""
import sys
from common import *
import cv2
from colour import char_mask

V = WORK / "v"; V.mkdir(exist_ok=True)
SRC = WORK / "pick_before_legs.png"
GROUND = 1005
# pass 2: darker under-fur tones (pale flat grey pegs were re-painted as floor reflections by the lock)
NEAR, NEAR_HI, NEAR_SH, FAR, CLAW, INKC = (74, 82, 100), (108, 116, 134), (48, 52, 66), (44, 46, 58), (206, 210, 216), (59, 28, 38)
# legs: (x_bottom_centre, top_y, top_w, bottom_w, lean_px (bottom x offset vs top), far?)
VAR = {   # pass 3: V1/V2 legs start inside the fur mass and splay slightly (straight separated pegs were dropped by SAM
          # from the character mask and re-painted as ghosts; V3's leaning, overlapping legs worked)
    1: dict(name="stubby", belly=950, legs=[(245, 865, 100, 114, -14, 0), (425, 885, 70, 78, 10, 1),
                                           (645, 865, 104, 118, 14, 0), (800, 860, 66, 76, 10, 1)]),
    2: dict(name="sturdy pillars", belly=915, legs=[(240, 830, 116, 128, -12, 0), (420, 845, 80, 88, 8, 1),
                                                   (640, 830, 120, 132, 12, 0), (800, 825, 76, 84, 8, 1)]),
    # V3 round 2 (producer: "might have 5 legs"): far-right leg (560) overlapped the near-right leg and its shade
    # stripe read as a 5th leg -> far legs moved to 390/520, near-right leg less lean, and gaps kept between all four
    3: dict(name="walking stride", belly=930, legs=[(215, 885, 104, 116, -30, 0), (390, 900, 70, 80, 20, 1),
                                                   (700, 885, 104, 116, 18, 0), (530, 905, 68, 78, -14, 1)], stripes=False),
}


def leg_poly(x, top, tw, bw, lean):
    xt = x - lean
    return np.array([(xt - tw / 2, top), (xt + tw / 2, top), (x + bw / 2, GROUND - 22), (x - bw / 2, GROUND - 22)], np.int32)


def draw_leg(img, alpha_top, x, top, tw, bw, lean, far, stripes=True):
    """Paint one leg (base + lit/shade stripes + foot pad + 3 claws). Returns its mask."""
    h, w = img.shape[:2]
    m = np.zeros((h, w), np.uint8)
    cv2.fillPoly(m, [leg_poly(x, top, tw, bw, lean)], 1)
    cv2.ellipse(m, (x, GROUND - 20), (int(bw * 0.62), 20), 0, 0, 360, 1, -1)
    base = np.zeros_like(img, dtype=np.float32); base[:] = FAR if far else NEAR
    if not far and stripes:
        hi = np.zeros((h, w), np.uint8); cv2.fillPoly(hi, [leg_poly(x - int(bw * .28), top, int(tw * .25), int(bw * .22), lean)], 1)
        sh = np.zeros((h, w), np.uint8); cv2.fillPoly(sh, [leg_poly(x + int(bw * .3), top, int(tw * .3), int(bw * .28), lean)], 1)
        base[hi.astype(bool)] = NEAR_HI; base[sh.astype(bool)] = NEAR_SH
    # soft blend into the body at the top (fur overlaps the leg)
    yy = np.arange(h, dtype=np.float32)[:, None]
    ramp = np.clip((yy - top) / alpha_top, 0, 1)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.5) * ramp
    fur = cv2.GaussianBlur(np.random.default_rng(x).normal(0, 1, (h, w)).astype(np.float32), (0, 0), sigmaX=1.2, sigmaY=6) * 26
    base = base + fur[..., None]                        # vertical fur streaks
    out = img * (1 - a[..., None]) + base * a[..., None]
    # claws (pale steel triangles) on the foot front edge
    claws = np.zeros((h, w), np.uint8)
    for k in (-1, 0, 1):
        cxk = x + k * int(bw * 0.3)
        cv2.fillPoly(claws, [np.array([(cxk - 13, GROUND - 26), (cxk + 13, GROUND - 26), (cxk, GROUND + 2)], np.int32)], 1)
    ca = cv2.GaussianBlur(claws.astype(np.float32), (0, 0), 1.0)[..., None]
    out = out * (1 - ca) + np.array(CLAW, np.float32) * ca
    return out, (m | claws).astype(bool)


def paper(im):
    p = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, -30:].reshape(-1, 3), im[:, :30].reshape(-1, 3)]), 0)
    return (p[None, None] + np.random.default_rng(5).normal(0, 2.0, im.shape)).clip(0, 255)


def build():
    im = np.asarray(Image.open(SRC).convert("RGB")).astype(np.float32)
    body = char_mask(im.astype(np.uint8))
    for v, c in VAR.items():
        out = im.copy()
        legs_m = np.zeros(body.shape, bool)
        # 1) paper gap under the belly (everything below the belly line becomes paper, then legs are drawn back)
        under = np.zeros(body.shape, bool); under[c["belly"]:] = True
        under &= cv2.dilate(body.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
        ga = cv2.GaussianBlur(under.astype(np.float32), (0, 0), 3)[..., None]
        out = out * (1 - ga) + paper(im) * ga
        # 2) belly shadow edge (the underside of the shell/fur)
        sh = np.zeros(body.shape, np.uint8)
        xs = np.nonzero(body[c["belly"] - 5])[0]
        if len(xs):
            cv2.line(sh, (int(xs.min()) + 20, c["belly"]), (int(xs.max()) - 20, c["belly"]), 1, 10)
        sa = cv2.GaussianBlur(sh.astype(np.float32), (0, 0), 4)[..., None] * 0.55
        out = out * (1 - sa) + np.array(NEAR_SH, np.float32) * sa
        # 3) far legs first (behind), then near legs
        for leg in sorted(c["legs"], key=lambda l: -l[5]):
            x, top, tw, bw, lean, far = leg
            out, lm = draw_leg(out, (28 if not far else 18) + (40 if v < 3 else 0), x, top, tw, bw, lean, far, c.get('stripes', True))
            legs_m |= lm
        Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(V / f"pick_v{v}.png")
        np.save(V / f"legs_v{v}.npy", legs_m)
        print("built", v, c["name"])


def guide(v):
    c = VAR[v]
    e = np.asarray(Image.open(WORK / "sketch_tarasque_canny.png").convert("L")).copy()
    # anatomy guide: every leg outline (near + far), the foot pads, claws, and the belly line
    for x, top, tw, bw, lean, far in c["legs"]:
        p = leg_poly(x, top + (12 if far else 0), tw, bw, lean)
        cv2.polylines(e, [p[[1, 2]], p[[3, 0]]], False, 255, 2)       # the two leg sides
        cv2.ellipse(e, (x, GROUND - 20), (int(bw * 0.62), 20), 0, 0, 360, 255, 2)
        for k in (-1, 0, 1):
            cxk = x + k * int(bw * 0.3)
            cv2.polylines(e, [np.array([(cxk - 13, GROUND - 26), (cxk, GROUND + 2), (cxk + 13, GROUND - 26)], np.int32)], False, 255, 1)
    body = char_mask(np.asarray(Image.open(V / f"pick_v{v}.png").convert("RGB")))
    xs = np.nonzero(body[c["belly"] - 5])[0]
    if len(xs):
        cv2.line(e, (int(xs.min()) + 20, c["belly"]), (int(xs.max()) - 20, c["belly"]), 255, 2)
    e[c["belly"] + 6:GROUND - 30] = np.where(cv2.dilate(np.load(V / f"legs_v{v}.npy").astype(np.uint8), np.ones((5, 5), np.uint8))[c["belly"] + 6:GROUND - 30] > 0,
                                               e[c["belly"] + 6:GROUND - 30], 0)   # no stray fur edges in the gaps
    Image.fromarray(e).convert("RGB").save(V / f"sketch_v{v}_canny.png")
    import shutil
    for k in ("block", "soft"):
        shutil.copy(WORK / f"sketch_tarasque_{k}.png", V / f"sketch_v{v}_{k}.png")
    print("guide", v)


if __name__ == "__main__":
    {"build": build, "guide": lambda: guide(int(sys.argv[2]))}[sys.argv[1]]() if sys.argv[1] == "build" else guide(int(sys.argv[2]))
