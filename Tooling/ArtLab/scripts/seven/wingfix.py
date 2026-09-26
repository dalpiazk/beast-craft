# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Griffin wing fix round 1 (REJECTED by the producer: wings out of the neck); kept for the record.
"""Griffin #1 (L8) wing fix, step 1 (Option A-style rough edit that feeds Option B's re-lock).
Producer: 'his wings are positioned as if his body is facing left but he's facing right'.
The near wing (viewer's right) grows out of the FRONT of the chest and sweeps forward (right). For a right-facing
3/4 body both wings root at the shoulders (upper back, behind the neck) and sweep up and BACK (left).
  python wingfix.py masks   -> $ARTLAB_WORK/wing_masks.png (SAM near/far wing masks over the pick, with a grid)
  python wingfix.py edit    -> $ARTLAB_WORK/pick.png (near wing cut, paper-filled, mirrored and re-seated at the shoulder)
"""
import sys
from common import *
import cv2
from masks import _sam, sam_box

SRC = WORK / "pick_before_wingfix.png"


def sam_pts(img, pos, neg):
    sam_box(np.asarray(img), [0, 0, 10, 10]) if not _sam else None
    P, M = _sam["p"], _sam["m"]
    pts = [[float(x), float(y)] for x, y in pos + neg]
    inp = P(images=img, input_points=[[pts]], input_labels=[[[1] * len(pos) + [0] * len(neg)]], return_tensors="pt")
    with torch.no_grad():
        o = M(**inp, multimask_output=True)
    ms = P.post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    sc = o.iou_scores[0, 0].float().numpy()
    ok = [i for i in range(3) if all(ms[i][y, x] for x, y in pos) and not any(ms[i][y, x] for x, y in neg) and ms[i].mean() < 0.2]
    b = max(ok, key=lambda i: sc[i]) if ok else int(np.argmax(sc))
    return ms[b], float(sc[b])


NEAR = dict(pos=[(730, 250), (760, 400), (650, 560)], neg=[(470, 700), (540, 420), (380, 300)])
FAR = dict(pos=[(320, 200), (300, 350)], neg=[(470, 700), (540, 420), (730, 300)])


def masks():
    im = Image.open(SRC).convert("RGB")
    near, s1 = sam_pts(im, NEAR["pos"], NEAR["neg"]); far, s2 = sam_pts(im, FAR["pos"], FAR["neg"])
    ov = np.asarray(im).astype(np.float32).copy()
    ov[near] = ov[near] * .5 + np.array([230, 40, 40]) * .5; ov[far] = ov[far] * .5 + np.array([40, 80, 230]) * .5
    o = Image.fromarray(ov.astype(np.uint8)); d = ImageDraw.Draw(o)
    for x in range(0, W, 100):
        d.line([(x, 0), (x, H)], fill=(0, 0, 0), width=1); d.text((x + 2, 2), str(x), fill=(0, 0, 0))
    for y in range(0, H, 100):
        d.line([(0, y), (W, y)], fill=(0, 0, 0), width=1); d.text((2, y + 2), str(y), fill=(0, 0, 0))
    o.save(WORK / "wing_masks.png")
    np.save(WORK / "near_wing.npy", near); np.save(WORK / "far_wing.npy", far)
    print("scores", s1, s2, near.mean(), far.mean())


ROOT_OLD = (600, 700)      # near-wing root on the pick (bottom of the wing, at the chest front)
ROOT_NEW = (385, 640)      # left shoulder / upper back, behind the neck (right-facing 3/4 body)
SCALE = 0.9


def edit():
    im = np.asarray(Image.open(SRC).convert("RGB"))
    near = np.load(WORK / "near_wing.npy")
    near[:490, :600] = False                                     # SAM leaked onto the beak
    near = cv2.morphologyEx(near.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8)).astype(bool)
    near_d = cv2.dilate(near.astype(np.uint8), np.ones((11, 11), np.uint8)).astype(bool)   # + its outline/halo
    # 1) remove the near wing: Telea fill from the surroundings (paper + chest edge)
    hole = cv2.inpaint(im, near_d.astype(np.uint8) * 255, 15, cv2.INPAINT_TELEA)
    # the Telea fill smears wing/chest colour into the paper right of the chest edge -> plain paper there
    paper = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, -30:].reshape(-1, 3)]), 0)
    right = near_d.copy(); right[:, :596] = False
    pap = (paper[None, None] + np.random.default_rng(3).normal(0, 2.5, im.shape)).clip(0, 255)
    ra = cv2.GaussianBlur(right.astype(np.float32), (0, 0), 3)[..., None]
    hole = (hole * (1 - ra) + pap * ra).astype(np.uint8)
    # 2) cut the wing (with a soft alpha), mirror, scale, re-seat its root at the shoulder
    ys, xs = np.nonzero(near_d); x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    wing = im[y0:y1, x0:x1]; a = cv2.GaussianBlur(near[y0:y1, x0:x1].astype(np.float32), (0, 0), 1.2)
    wing = wing[:, ::-1]; a = a[:, ::-1]
    rx, ry = (x1 - 1 - ROOT_OLD[0]), ROOT_OLD[1] - y0             # root in the mirrored crop
    nw, nh = int(wing.shape[1] * SCALE), int(wing.shape[0] * SCALE)
    wing = cv2.resize(wing, (nw, nh), interpolation=cv2.INTER_AREA); a = cv2.resize(a, (nw, nh))
    ox, oy = int(ROOT_NEW[0] - rx * SCALE), int(ROOT_NEW[1] - ry * SCALE)
    out = hole.astype(np.float32).copy()
    X0, Y0 = max(ox, 0), max(oy, 0); X1, Y1 = min(ox + nw, W), min(oy + nh, H)
    sub = out[Y0:Y1, X0:X1]; ww = wing[Y0 - oy:Y1 - oy, X0 - ox:X1 - ox].astype(np.float32); aa = a[Y0 - oy:Y1 - oy, X0 - ox:X1 - ox, None]
    out[Y0:Y1, X0:X1] = sub * (1 - aa) + ww * aa
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(WORK / "pick.png")
    newm = np.zeros((H, W), np.float32); newm[Y0:Y1, X0:X1] = aa[..., 0]
    np.save(WORK / "near_wing_new.npy", newm > 0.5)
    Image.fromarray(hole).save(WORK / "wingfix_hole.png")
    print("placed at", ox, oy, nw, nh)


def fixprep():
    """After gen4.py prep: (1) the pale wings on cream give almost no canny -> add the wings' SAM contours and a
    low-threshold canny inside the wings to the lock image; (2) the sky-blue swatch accent (9cc7d9) was matched to
    pale wing feathers in the block -> map it to cream (fff3de)."""
    pick = np.asarray(Image.open(WORK / "pick.png").convert("RGB"))
    e = np.asarray(Image.open(WORK / "sketch_griffin_canny.png").convert("L")).copy()
    near = np.load(WORK / "near_wing_new.npy"); far = np.load(WORK / "far_wing.npy")
    g = cv2.cvtColor(pick, cv2.COLOR_RGB2GRAY)
    low = cv2.Canny(cv2.GaussianBlur(g, (3, 3), 0), 15, 45)
    wings = cv2.dilate((near | far).astype(np.uint8), np.ones((7, 7), np.uint8)).astype(bool)
    e[wings & (low > 0)] = 255
    for m in (near, far):
        cs, _ = cv2.findContours(m.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(e, cs, -1, 255, 1)
    Image.fromarray(e).convert("RGB").save(WORK / "sketch_griffin_canny.png")
    blk = np.asarray(Image.open(WORK / "sketch_griffin_block.png").convert("RGB")).copy()
    sky = np.all(np.abs(blk.astype(int) - np.array([0x9c, 0xc7, 0xd9])) < 12, -1)
    blk[sky] = (0xff, 0xf3, 0xde)
    Image.fromarray(blk).save(WORK / "sketch_griffin_block.png")
    from sketches_soft import soften
    soften(Image.fromarray(blk)).save(WORK / "sketch_griffin_soft.png")
    print("wing edges added", int((wings & (low > 0)).sum()), "sky px remapped", int(sky.sum()))


if __name__ == "__main__":
    {"masks": masks, "edit": edit, "fixprep": fixprep}[sys.argv[1]]()
