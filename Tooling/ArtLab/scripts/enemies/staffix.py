# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Shaman (#11, shaman_L1): the broken staff rebuilt, the near hand gripping it.
"""Shaman #11 (enemy candidate shaman_L1) fixes before the lock (producer: the staff floats in pieces; must show 2 arms
with hands gripping): (1) the broken zig-zag staff is removed to paper; (2) near arm (viewer-left) re-drawn as a
mossy sleeve ending in a 3-finger hand GRIPPING (3) one continuous gnarled root staff from the ground to a knotted
top with a mushroom cap = ELEMENT ACCENT region; (4) far arm (viewer-right) as a mossy sleeve with a 3-finger hand.
  python staffix.py edit   -> ../work/pick.png (+ fix masks)
  python staffix.py guide  -> after `gen4.py prep shaman ../work/pick.png`: adds arm/hand/staff contours to the canny
"""
import sys
from common import *
import cv2

SRC = WORK / "pick_before_fix.png"
INKC = (59, 28, 38)
BARK, BARK_D, BARK_L = (112, 86, 62), (72, 54, 40), (150, 120, 88)
MOSS, MOSS_D, MOSS_L = (104, 128, 86), (70, 92, 62), (140, 164, 110)
SKIN = (198, 192, 170)            # pale bark-skin of the hands (like the face mask)
ACC = (143, 184, 90)              # element accent (Nature-green default)
CAP_SPOTS = (232, 230, 200)
STAFF = [(300, 992), (306, 930), (298, 860), (304, 790), (296, 720), (304, 650), (300, 600)]
NEAR_ARM = [(392, 740), (362, 790), (318, 822)]           # shoulder -> elbow -> hand (on the staff)
FAR_ARM = [(548, 730), (585, 790), (596, 846)]


def paper(im):
    p = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, :30].reshape(-1, 3)]), 0)
    return (p[None, None] + np.random.default_rng(5).normal(0, 2.0, im.shape)).clip(0, 255)


def stroke(out, pts, w0, w1, col, ink=3, mask=None):
    m = np.zeros(out.shape[:2], np.uint8)
    for (x0, y0), (x1, y1), t in zip(pts[:-1], pts[1:], np.linspace(0, 1, len(pts) - 1)):
        w = int(w0 + (w1 - w0) * t)
        cv2.line(m, (x0, y0), (x1, y1), 1, w)
        cv2.circle(m, (x1, y1), w // 2, 1, -1)
    cv2.circle(m, pts[0], int(w0) // 2, 1, -1)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.0)[..., None]
    out[:] = out * (1 - a) + np.array(col, np.float32) * a
    if ink:
        cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        o = np.zeros(out.shape[:2], np.uint8); cv2.drawContours(o, cs, -1, 1, ink)
        oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .8)[..., None]
        out[:] = out * (1 - oa) + np.array(INKC, np.float32) * oa
    if mask is not None:
        mask |= m.astype(bool)
    return m.astype(bool)


def hand(out, cx, cy, flip, grip_x=None, mask=None):
    """3-finger mitten hand; when gripping, the fingers wrap around the staff at grip_x."""
    m = np.zeros(out.shape[:2], np.uint8)
    cv2.ellipse(m, (cx, cy), (20, 17), 0, 0, 360, 1, -1)
    for k in range(3):
        fy = cy - 10 + k * 11
        fx = (grip_x if grip_x is not None else cx + flip * 18)
        cv2.ellipse(m, (fx + flip * 2, fy), (11, 6), 0, 0, 360, 1, -1)
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), .9)[..., None]
    out[:] = out * (1 - a) + np.array(SKIN, np.float32) * a
    cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    o = np.zeros(out.shape[:2], np.uint8); cv2.drawContours(o, cs, -1, 1, 3)
    for k in range(1, 3):                                   # finger separations
        fy = cy - 10 + k * 11 - 5
        fx = (grip_x if grip_x is not None else cx + flip * 18)
        cv2.line(o, (fx - 8, fy), (fx + 10, fy), 1, 2)
    oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .7)[..., None]
    out[:] = out * (1 - oa) + np.array(INKC, np.float32) * oa
    if mask is not None:
        mask |= m.astype(bool)


def edit():
    """Round 2: KEEP the candidate's own two arms (near arm with its hand at ~(375,815) that held the old staff; far arm
    across the belly). Only the staff is rebuilt: the broken zig-zag is removed to paper, a continuous gnarled root
    staff is placed just left of the body (x ~318, clear of the face), and the near hand is extended into a gripping
    fist around it (same arm colour). Staff top = knotted fork + mushroom cap = ELEMENT ACCENT."""
    im = np.asarray(Image.open(SRC).convert("RGB")).astype(np.float32)
    out = im.copy()
    rm = np.zeros(im.shape[:2], np.uint8)
    cv2.fillPoly(rm, [np.array([(150, 775), (360, 775), (360, 850), (395, 850), (395, 1010), (150, 1010)], np.int32)], 1)
    lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
    pinkish = (lab[..., 1] > 132) | (lab[..., 2] < 122)                      # the old staff is pink/blue-purple
    keep = (~pinkish) & (np.arange(W)[None, :] > 355)                       # keep the green hand/leg pixels
    rm = rm.astype(bool) & ~keep
    ra = cv2.GaussianBlur(rm.astype(np.float32), (0, 0), 2)[..., None]
    out = out * (1 - ra) + paper(im) * ra
    fix = np.zeros(im.shape[:2], bool)
    armcol = np.median(im[800:835, 368:392].reshape(-1, 3), 0)               # the candidate's own hand colour
    # staff (behind the fist)
    staff = [(322, 996), (316, 930), (322, 860), (314, 780), (320, 700), (314, 620), (318, 540), (316, 500)]
    stroke(out, staff, 28, 24, BARK, mask=fix)
    for y in (620, 740, 900):
        cv2.ellipse(out, (318, y), (17, 9), 0, 0, 360, BARK_D, -1)
    stroke(out, [(x - 6, y) for x, y in staff], 7, 5, BARK_L, ink=0)
    for sgn in (-1, 1):
        stroke(out, [(316, 510), (316 + sgn * 26, 488), (316 + sgn * 30, 462)], 14, 9, BARK, mask=fix)
    cap = np.zeros(im.shape[:2], np.uint8)
    cv2.ellipse(cap, (316, 470), (50, 32), 0, 180, 360, 1, -1); cv2.rectangle(cap, (266, 468), (366, 480), 1, -1)
    ca = cv2.GaussianBlur(cap.astype(np.float32), (0, 0), 1.0)[..., None]
    out = out * (1 - ca) + np.array(ACC, np.float32) * ca
    for x, y in ((298, 452), (332, 448), (316, 464), (346, 464)):
        cv2.circle(out, (x, y), 5, CAP_SPOTS, -1)
    cs, _ = cv2.findContours(cap, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    o = np.zeros(im.shape[:2], np.uint8); cv2.drawContours(o, cs, -1, 1, 3)
    oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .8)[..., None]
    out = out * (1 - oa) + np.array(INKC, np.float32) * oa
    fix |= cap.astype(bool); np.save(WORK / "accent_mask.npy", cap.astype(bool))
    # the near hand reaches the staff: wrist bridge + fist wrapping the staff, in the arm's own colour
    stroke(out, [(372, 812), (348, 814)], 30, 28, tuple(armcol), mask=fix)
    fist = np.zeros(im.shape[:2], np.uint8)
    cv2.ellipse(fist, (330, 814), (22, 24), 0, 0, 360, 1, -1)
    fa = cv2.GaussianBlur(fist.astype(np.float32), (0, 0), .9)[..., None]
    out = out * (1 - fa) + armcol * fa
    o = np.zeros(im.shape[:2], np.uint8)
    cs, _ = cv2.findContours(fist, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE); cv2.drawContours(o, cs, -1, 1, 3)
    for k in range(1, 4):                                                    # 3 finger creases over the staff
        cv2.line(o, (312, 796 + k * 9), (336, 798 + k * 9), 1, 2)
    oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .7)[..., None]
    out = out * (1 - oa) + np.array(INKC, np.float32) * oa
    fix |= fist.astype(bool)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
    np.save(WORK / "fix_mask.npy", fix)
    print("edit ok, arm colour", armcol.round())


def guide():
    e = np.asarray(Image.open(WORK / "sketch_shaman_canny.png").convert("L")).copy()
    fix = np.load(WORK / "fix_mask.npy").astype(np.uint8)
    cs, _ = cv2.findContours(fix, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(e, cs, -1, 255, 2)
    Image.fromarray(e).convert("RGB").save(WORK / "sketch_shaman_canny.png")
    blk = np.asarray(Image.open(WORK / "sketch_shaman_block.png").convert("RGB")).copy()
    pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
    fm = fix.astype(bool)
    blk[fm] = pick[fm]                                   # keep the painted arms/hands/staff colours in the block
    Image.fromarray(blk).save(WORK / "sketch_shaman_block.png")
    from sketches_soft import soften
    soften(Image.fromarray(blk)).save(WORK / "sketch_shaman_soft.png")
    print("guide ok")


if __name__ == "__main__":
    {"edit": edit, "guide": guide}[sys.argv[1]]()
