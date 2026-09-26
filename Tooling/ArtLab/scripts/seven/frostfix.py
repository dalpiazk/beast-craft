# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Frost Wyrm #11: dragon horns, knowing half-lidded eye, one eye and one mouth (rounds 1-2).
"""Frost Wyrm #11 (candidate L1) producer fixes: (1) proper eyes, irises and pupils, half-lidded and knowing (the
candidate's eyes were blank pale almonds); (2) ram-curl horns -> swept-back dragon horns.
  python frostfix.py edit        -> $ARTLAB_WORK/pick.png (backup pick_before_fix.png) + eye/horn masks
  python frostfix.py guide       -> after `gen4.py prep frost_wyrm $ARTLAB_WORK/pick.png`: adds eye + horn contours to the canny
  python frostfix.py restore LOCK OUT   -> paste the painted eyes back onto a lock (golem_face.py restore method)
"""
import sys
from common import *
import cv2
from masks import _sam, sam_box

SRC = WORK / "pick_before_fix.png"
INKC = (59, 28, 38)
EYES = [dict(c=(372, 442), w=34, far=False)]   # round 2 (producer: "a small second eye ... and maybe a small second mouth"): ONE eye
ERASE = [((227, 452), (8, 32)), ((277, 505), (18, 10)), ((264, 425), (30, 22)), ((455, 468), (13, 17))]   # far-eye arc, candidate nostril/mini-mouth, old far eye+brow, cheek marks (1x ellipses)
HORNS = [  # base (on the back of the skull) -> sweep back -> tip curving up; width at base
    dict(p=[(402, 366), (462, 336), (515, 322), (560, 300), (590, 268)], w=44, far=False),
    dict(p=[(334, 356), (384, 320), (428, 302), (466, 280), (490, 250)], w=32, far=True),
]
MOUTH = [(258, 506), (290, 504), (306, 498), (314, 488)]   # flat line with a curl at the corner = wry smile
BONE, BONE_HI, BONE_SH = (226, 206, 160), (246, 232, 196), (176, 150, 104)


def sam_pts(img, pos, neg, maxfrac=0.05):
    sam_box(np.asarray(img), [0, 0, 10, 10]) if not _sam else None
    P, M = _sam["p"], _sam["m"]
    pts = [[float(x), float(y)] for x, y in pos + neg]
    inp = P(images=img, input_points=[[pts]], input_labels=[[[1] * len(pos) + [0] * len(neg)]], return_tensors="pt")
    with torch.no_grad():
        o = M(**inp, multimask_output=True)
    ms = P.post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    sc = o.iou_scores[0, 0].float().numpy()
    ok = [i for i in range(3) if not any(ms[i][y, x] for x, y in neg) and ms[i].mean() < maxfrac]
    b = max(ok, key=lambda i: sc[i]) if ok else int(np.argmax(sc))
    return ms[b], float(sc[b])


def horn_poly(p, w):
    pts = np.array(p, np.float32); out_l, out_r = [], []
    for i, (x, y) in enumerate(pts):
        d = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]; n = np.array([-d[1], d[0]]) / (np.linalg.norm(d) + 1e-6)
        ww = w * (1 - i / (len(pts) - 1)) * 0.5 + 1.5
        out_l.append((x + n[0] * ww, y + n[1] * ww)); out_r.append((x - n[0] * ww, y - n[1] * ww))
    return np.array(out_l + out_r[::-1], np.int32)


def eye_masks(c, w):
    """almond white, iris, pupil, lid (upper half-cover) as uint8 masks."""
    sh = (H, W)
    alm = np.zeros(sh, np.uint8); cv2.ellipse(alm, c, (w, int(w * .62)), -6, 0, 360, 1, -1)
    iris = np.zeros(sh, np.uint8); cv2.circle(iris, (c[0] - int(w * .12), c[1] + int(w * .05)), int(w * .52), 1, -1)
    pup = np.zeros(sh, np.uint8); cv2.ellipse(pup, (c[0] - int(w * .12), c[1] + int(w * .05)), (int(w * .2), int(w * .34)), 0, 0, 360, 1, -1)
    lid = np.zeros(sh, np.uint8)                       # half-lidded: upper ~45% of the almond covered by the lid
    cv2.rectangle(lid, (c[0] - w - 2, c[1] - w), (c[0] + w + 2, c[1] - int(w * .1)), 1, -1)
    return alm, iris & alm, pup & alm, lid & alm


def edit():
    import shutil
    if not SRC.exists():
        shutil.copy(WORK / "pick.png", SRC)
    im = np.asarray(Image.open(SRC).convert("RGB"))
    horns, sc = sam_pts(Image.fromarray(im), [(245, 320), (480, 310), (550, 370), (215, 380)], [(340, 450), (300, 700), (360, 330)])
    print("ram horn mask", round(sc, 3), round(float(horns.mean()), 4))
    hd = cv2.dilate(horns.astype(np.uint8), np.ones((9, 9), np.uint8))
    out = cv2.inpaint(im, hd * 255, 11, cv2.INPAINT_TELEA).astype(np.float32)
    # outside the head silhouette the ram curls become plain paper (Telea smears head colour outward)
    paper = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, -30:].reshape(-1, 3)]), 0)
    from colour import char_mask
    body = char_mask(im) & ~horns
    body = cv2.morphologyEx(body.astype(np.uint8), cv2.MORPH_OPEN, np.ones((7, 7), np.uint8)).astype(bool)
    outside = hd.astype(bool) & ~cv2.dilate(body.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    oa = cv2.GaussianBlur(outside.astype(np.float32), (0, 0), 2)[..., None]
    out = out * (1 - oa) + (paper + np.random.default_rng(3).normal(0, 2, im.shape)) * oa
    # round 2: erase the far-eye arc, the candidate's small nostril/second mouth and stray cheek marks -> skin
    er = np.zeros((H, W), np.uint8)
    for c_, ax in ERASE:
        cv2.ellipse(er, c_, ax, 0, 0, 360, 1, -1)
    skin = np.array([250, 249, 242], np.float32)
    ea = cv2.GaussianBlur(er.astype(np.float32), (0, 0), 3)[..., None]
    out = out * (1 - ea) + (skin + np.random.default_rng(4).normal(0, 1.5, out.shape)) * ea
    # swept-back dragon horns (far first)
    hmask = np.zeros((H, W), bool)
    for h in sorted(HORNS, key=lambda h: -h["far"]):
        m = np.zeros((H, W), np.uint8); cv2.fillPoly(m, [horn_poly(h["p"], h["w"])], 1)
        hi = np.zeros((H, W), np.uint8); cv2.fillPoly(hi, [horn_poly([(x - 3, y - 4) for x, y in h["p"]], h["w"] * .4)], 1)
        col = np.zeros_like(out); col[:] = BONE_SH if h["far"] else BONE; col[(hi & m).astype(bool)] = BONE_HI if not h["far"] else BONE
        a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), 1.0)[..., None]
        out = out * (1 - a) + col * a
        cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        ol = np.zeros((H, W), np.uint8); cv2.drawContours(ol, cs, -1, 1, 3)
        la = cv2.GaussianBlur(ol.astype(np.float32), (0, 0), .8)[..., None]
        out = out * (1 - la) + np.array(INKC, np.float32) * la
        hmask |= m.astype(bool)
    # eyes: white almond, dark-blue iris, pupil, highlight, half lid (skin) + heavy lid line + brow = knowing look
    emask = np.zeros((H, W), bool)
    for e in EYES:
        c, w = e["c"], e["w"]
        alm, iris, pup, lid = eye_masks(c, w)
        skin = out[c[1] - w - 8:c[1] - w, c[0] - 10:c[0] + 10].reshape(-1, 3).mean(0)
        for m, colr in ((alm, (246, 248, 250)), (iris, (44, 86, 138)), (pup, (20, 24, 40)), (lid, tuple(skin))):
            a = cv2.GaussianBlur(m.astype(np.float32), (0, 0), .7)[..., None]
            out = out * (1 - a) + np.array(colr, np.float32) * a
        ly = c[1] - int(w * .1)
        ink = np.zeros((H, W), np.uint8)
        cv2.ellipse(ink, c, (w, int(w * .62)), -6, 180, 360, 1, 2)                           # lower almond edge
        cv2.line(ink, (c[0] - w, ly + 2), (c[0] + w, ly - 2), 1, max(3, w // 7))           # heavy lid line
        cv2.line(ink, (c[0] - w, c[1] - int(w * .85)), (c[0] + int(w * .9), c[1] - int(w * 1.05)), 1, max(3, w // 8))  # brow, raised at the outer end (wry)
        hl = np.zeros((H, W), np.uint8); cv2.circle(hl, (c[0] - int(w * .3), ly + int(w * .18)), max(2, w // 7), 1, -1)
        a = cv2.GaussianBlur(ink.astype(np.float32), (0, 0), .7)[..., None]
        out = out * (1 - a) + np.array(INKC, np.float32) * a
        a = cv2.GaussianBlur(hl.astype(np.float32), (0, 0), .6)[..., None]
        out = out * (1 - a) + np.array([255, 255, 255], np.float32) * a
        emask |= cv2.dilate(alm, np.ones((5, 5), np.uint8)).astype(bool)
    mo = np.zeros((H, W), np.uint8); cv2.polylines(mo, [np.array(MOUTH, np.int32)], False, 1, 3)
    a = cv2.GaussianBlur(mo.astype(np.float32), (0, 0), .7)[..., None]
    out = out * (1 - a) + np.array(INKC, np.float32) * a
    emask |= cv2.dilate(mo, np.ones((7, 7), np.uint8)).astype(bool)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
    np.save(WORK / "eyes_mask.npy", emask); np.save(WORK / "horns_mask.npy", hmask)
    print("edit ok")


def guide():
    e = np.asarray(Image.open(WORK / "sketch_frost_wyrm_canny.png").convert("L")).copy()
    for h in HORNS:
        cv2.polylines(e, [horn_poly(h["p"], h["w"])], True, 255, 2)
    cv2.polylines(e, [np.array(MOUTH, np.int32)], False, 255, 2)
    for ev in EYES:
        c, w = ev["c"], ev["w"]
        cv2.ellipse(e, c, (w, int(w * .62)), -6, 0, 360, 255, 1)
        cv2.circle(e, (c[0] - int(w * .12), c[1] + int(w * .05)), int(w * .52), 255, 1)
        cv2.line(e, (c[0] - w, c[1] - int(w * .1) + 2), (c[0] + w, c[1] - int(w * .1) - 2), 255, 2)
    Image.fromarray(e).convert("RGB").save(WORK / "sketch_frost_wyrm_canny.png")
    blk = np.asarray(Image.open(WORK / "sketch_frost_wyrm_block.png").convert("RGB")).copy()
    pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
    em = np.load(WORK / "eyes_mask.npy")
    blk[em] = pick[em]                                  # keep the painted eyes in the colour block (not quantised away)
    Image.fromarray(blk).save(WORK / "sketch_frost_wyrm_block.png")
    from sketches_soft import soften
    s = np.asarray(soften(Image.fromarray(blk))).copy()
    s[em] = pick[em]                                    # and unblurred in the soft init (golem eyes.py lesson)
    Image.fromarray(s).save(WORK / "sketch_frost_wyrm_soft.png")
    print("guide ok")


def restore(lock_p, out_p):
    pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
    lk = np.asarray(Image.open(lock_p).convert("RGB")).astype(np.float32)
    em = np.load(WORK / "eyes_mask.npy")
    a = cv2.GaussianBlur(em.astype(np.float32), (0, 0), 1.5)[..., None]
    Image.fromarray((lk * (1 - a) + pick * a).clip(0, 255).astype(np.uint8)).save(out_p)
    print("eyes restored")


if __name__ == "__main__":
    c = sys.argv[1]
    if c == "edit": edit()
    elif c == "guide": guide()
    elif c == "restore": restore(sys.argv[2], sys.argv[3])
