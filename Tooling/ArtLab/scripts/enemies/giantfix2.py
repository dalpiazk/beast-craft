# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): Giant (giant_t11) colour, eyes and size fix before the lock.
"""Giant = sheet #11 = giant_t11 (big white shaggy fur creature), producer notes:
 - "add some greens that fade to blacks or purples to bring in a bit more nature": moss/leaf greens worked into the
   white fur (mossy tips, lichen patches), fading to deep black-green and dusky Gloam purple in shadows / toward the
   base -> a Verdant Hollow elder, not a snow yeti. The off-palette blue rim becomes dusk purple.
 - readable limbs: 2 sturdy front legs with paws + 2 hind feet (the candidate's right-hand foot + one behind),
   1 face, 2 eyes, 1 mouth; wide Hex7 silhouette -> the whole creature is scaled to 0.86 and centred (it was cut off
   at the right edge).
 - eyes: magenta kept but pulled toward a dim hazy violet Gloam glow.
 - ELEMENT ACCENT: a cluster of crystal shards set into the fur of the crown (mask saved).
  python giantfix2.py edit | guide
"""
import sys
from common import *
import cv2
from masks import line_mask

SRC = WORK / "pick_before_fix.png"
SC, DX, DY = 0.86, 70, 100                        # scale about (0,0) then shift
INKC = np.array([59, 28, 38], np.float32)
DEEPG, PURP = np.array([28, 42, 30], np.float32), np.array([78, 60, 96], np.float32)
MOSS, LEAF, LICHEN = np.array([92, 124, 70], np.float32), np.array([146, 180, 96], np.float32), np.array([176, 190, 130], np.float32)
FUR_D, FUR = np.array([150, 150, 136], np.float32), np.array([214, 214, 196], np.float32)
CRYS, CRYS_H = np.array([184, 168, 216], np.float32), np.array([236, 230, 250], np.float32)
GROUND = 1040


def T(x, y):
    return int(x * SC + DX), int(y * SC + DY)


def paper(im):
    p = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, :30].reshape(-1, 3)]), 0)
    return (p[None, None] + np.random.default_rng(5).normal(0, 2.0, im.shape)).clip(0, 255)


def draw_leg(out, top, bot, wt, wb, col, legs):
    m = np.zeros(out.shape[:2], np.uint8)
    (x0, y0), (x1, y1) = top, bot
    cv2.fillPoly(m, [np.array([(x0 - wt // 2, y0), (x0 + wt // 2, y0), (x1 + wb // 2, y1), (x1 - wb // 2, y1)], np.int32)], 1)
    cv2.ellipse(m, (x1, y1), (wb // 2 + 14, 24), 0, 0, 360, 1, -1)       # paw
    rng = np.random.default_rng(x0)
    fur = cv2.GaussianBlur(rng.normal(0, 1, out.shape[:2]).astype(np.float32), (0, 0), sigmaX=1.5, sigmaY=7) * 14
    a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.2)[..., None]
    out[:] = out * (1 - a) + (col + fur[..., None]) * a
    for t in (-1, 0, 1):                                                  # 3 toe claws
        cv2.ellipse(out, (x1 + t * (wb // 3), y1 + 14), (10, 8), 0, 0, 360, (70, 62, 58), -1)
    o = np.zeros(out.shape[:2], np.uint8); cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    cv2.drawContours(o, cs, -1, 1, 3); o[:y0 + 30] = 0
    oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .8)[..., None]
    out[:] = out * (1 - oa) + INKC * oa
    legs |= m.astype(bool)


def edit():
    im = np.asarray(Image.open(SRC).convert("RGB")).astype(np.float32)
    m0 = line_mask(im.astype(np.uint8))[0].astype(np.float32)
    M = np.float32([[SC, 0, DX], [0, SC, DY]])
    L = cv2.warpAffine(im, M, (W, H), borderMode=cv2.BORDER_REPLICATE)
    mm = cv2.warpAffine(m0, M, (W, H))
    out = paper(im)
    legs = np.zeros((H, W), bool)
    a = cv2.GaussianBlur(mm, (0, 0), 1.5)[..., None]
    out = out * (1 - a) + L * a
    body = mm > 0.5
    # the candidate's own limbs are kept: 2 front paws (bottom centre) + hind foot with toes (right) + far hind leg
    # (left, the old blue rim). They are made readable with claw marks + a dark gap line between the front paws.
    for (x, y) in ((345, 1080), (455, 1085)):
        px, py = T(x, y)
        cv2.ellipse(legs.view(np.uint8), (px, py), (52, 34), 0, 0, 360, 1, -1)
        for t in (-1, 0, 1):
            cv2.line(out, (px + t * 20, py + 8), (px + t * 22, py + 24), (60, 52, 50), 5)
    gx, gy = T(400, 1040)
    cv2.line(out, (gx, gy - 30), (gx + 2, gy + 30), tuple(INKC.tolist()), 4)
    for (x, y) in ((700, 1060), (780, 1075), (850, 1085)):                  # hind-foot toes
        px, py = T(x, y)
        cv2.line(out, (px, py), (px + 14, py + 12), (60, 52, 50), 5)
        cv2.ellipse(legs.view(np.uint8), (px, py), (40, 26), 0, 0, 360, 1, -1)
    # colour grade
    lab = cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
    Lc = lab[..., 0] / 255.0
    yy = np.arange(H, dtype=np.float32)[:, None]
    t = np.clip((yy - 420) / (GROUND - 420), 0, 1)
    rng = np.random.default_rng(11)
    noise = cv2.GaussianBlur(rng.normal(0, 1, (H, W)).astype(np.float32), (0, 0), 16)
    noise = (noise - noise.min()) / (noise.max() - noise.min())
    green = MOSS * (1 - noise[..., None]) + LEAF * noise[..., None]
    dark = DEEPG * noise[..., None] + PURP * (1 - noise[..., None])
    shadow = (1 - Lc) ** 1.2                                              # darker fur -> more dusk
    k = np.clip(t ** 1.2 * 0.8 + shadow * 0.6, 0, 1)[..., None]
    tgt = (green * (1 - k) + dark * k) * (0.6 + 0.7 * Lc[..., None])
    # keep bright fur tips pale-mossy (lichen) instead of white: highlight -> lichen
    hi = np.clip((Lc - 0.72) / 0.2, 0, 1)[..., None]
    tgt = tgt * (1 - hi) + LICHEN * hi
    face = np.zeros((H, W), np.uint8)
    cv2.ellipse(face, T(270, 360), (int(170 * SC), int(80 * SC)), -12, 0, 360, 1, -1)
    fa = cv2.GaussianBlur(face.astype(np.float32), (0, 0), 14)
    wgt = np.clip(0.45 + 0.4 * t + 0.2 * (noise - .5), 0, 0.9) * body * (1 - 0.75 * fa)
    # the candidate's saturated blue rim (left) -> fully dusk purple
    hsv = cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    blue = body & (hsv[..., 0] * 360 / 256 > 180) & (hsv[..., 0] * 360 / 256 < 260) & (hsv[..., 1] > 60)
    wgt = np.where(blue, 0.95, wgt)
    tgt = np.where(blue[..., None], PURP * (0.7 + 0.6 * Lc[..., None]), tgt)
    wgt = cv2.GaussianBlur(wgt.astype(np.float32), (0, 0), 2.5)[..., None]
    out = out * (1 - wgt) + tgt * wgt
    # eyes: magenta -> dim hazy violet Gloam glow (kept shape)
    hsv = cv2.cvtColor(out.clip(0, 255).astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    hue = hsv[..., 0] * 360 / 256
    eyes = body & (hue > 285) & (hue < 345) & (hsv[..., 1] > 90) & (np.arange(H)[:, None] < T(0, 420)[1])
    eyes = cv2.dilate(eyes.astype(np.uint8), np.ones((3, 3), np.uint8)).astype(bool)
    ea = cv2.GaussianBlur(eyes.astype(np.float32), (0, 0), 1)[..., None]
    out = out * (1 - ea) + np.array([196, 150, 232], np.float32) * ea
    glow = cv2.GaussianBlur(eyes.astype(np.float32), (0, 0), 7)[..., None] * 0.35
    out = out * (1 - glow) + np.array([214, 196, 240], np.float32) * glow
    # ELEMENT ACCENT: crystal shards set into the crown fur
    cr = np.zeros((H, W), np.uint8)
    for (bx, by, h, w_, ang) in ((330, 338, 95, 34, -14), (385, 342, 74, 28, 8), (282, 352, 58, 24, -32), (432, 360, 54, 22, 26)):
        tip = (bx + int(np.sin(np.radians(ang)) * h), by - int(np.cos(np.radians(ang)) * h))
        cv2.fillPoly(cr, [np.array([(bx - w_ // 2, by), tip, (bx + w_ // 2, by)], np.int32)], 1)
    ca = cv2.GaussianBlur(cr.astype(np.float32), (0, 0), 1)[..., None]
    shade = np.linspace(0, 1, W, dtype=np.float32)[None, :, None]
    out = out * (1 - ca) + (CRYS * (1 - .4 * shade) + CRYS_H * .4 * shade) * ca
    o = np.zeros((H, W), np.uint8); cs, _ = cv2.findContours(cr, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE); cv2.drawContours(o, cs, -1, 1, 3)
    oa = cv2.GaussianBlur(o.astype(np.float32), (0, 0), .8)[..., None]
    out = out * (1 - oa) + INKC * oa
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
    np.save(WORK / "legs_mask.npy", legs); np.save(WORK / "accent_mask.npy", cr.astype(bool)); np.save(WORK / "eyes_mask.npy", eyes)
    fx, fy = T(270, 360)
    json.dump(dict(face_ellipse_1x=[fx, fy, 170, 90], scale=SC, shift=[DX, DY]), open(WORK / "giantfix2.json", "w"))
    print("edit ok face", fx, fy, "legs", int(legs.sum()), "crystal", int(cr.sum()), "eyes", int(eyes.sum()))


def guide():
    e = np.asarray(Image.open(WORK / "sketch_giant_canny.png").convert("L")).copy()
    for nm in ("legs_mask", "accent_mask"):
        m = np.load(WORK / f"{nm}.npy").astype(np.uint8)
        cs, _ = cv2.findContours(m, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(e, cs, -1, 255, 2)
    Image.fromarray(e).convert("RGB").save(WORK / "sketch_giant_canny.png")
    pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
    blk = np.asarray(Image.open(WORK / "sketch_giant_block.png").convert("RGB")).copy()
    ch = np.asarray(Image.open(WORK / "sketch_giant_mask.png").convert("L")) > 127
    blk[ch] = cv2.medianBlur(pick, 7)[ch]
    Image.fromarray(blk).save(WORK / "sketch_giant_block.png")
    from sketches_soft import soften
    s = np.asarray(soften(Image.fromarray(blk))).copy()
    em = cv2.dilate(np.load(WORK / "eyes_mask.npy").astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
    s[em] = pick[em]                                                    # eyes unblurred in the init
    Image.fromarray(s).save(WORK / "sketch_giant_soft.png")
    print("guide ok")


if __name__ == "__main__":
    {"edit": edit, "guide": guide}[sys.argv[1]]()
