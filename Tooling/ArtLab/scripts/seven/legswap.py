# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Tarasque round 2: legs reordered left to right 1,3,4,2.
"""Tarasque leg order rework (producer): left->right order 1,3,4,2 = swap the two far legs and bring the near-right
leg inward. Each leg is moved as a whole cut-out from the approved design (own shape + angle kept), re-seated under
the shell; vacated spots -> paper; near legs pasted over far legs.
  python legswap.py build   -> $ARTLAB_WORK/v/pick_v4.png (+ legs_v4.npy), before = $ARTLAB_WORK/v/v3c_tarasque_11.png
  python legswap.py guide   -> after `gen4.py prep tarasque $ARTLAB_WORK/v/pick_v4.png`: sketch_v4_{canny,block,soft}
"""
import sys
from common import *
import cv2
from legfix import leg_poly, GROUND, paper

V = WORK / "v"
SRC = V / "v3c_tarasque_11.png"
CUT_Y = 902                      # legs are cut below the fur edge; above it the body/shell stays
BELLY = 930
# (name, x, top, tw, bw, lean, far) for the approved V3 round-2 legs
OLD = {1: (215, 885, 104, 116, -30, 0), 2: (390, 900, 70, 80, 20, 1), 3: (530, 905, 68, 78, -14, 1), 4: (700, 885, 104, 116, 18, 0)}
NEW_X = {1: 215, 3: 395, 4: 560, 2: 725}          # left->right: 1, 3, 4, 2


def leg_mask(x, top, tw, bw, lean, pad=10):
    m = np.zeros((H, W), np.uint8)
    p = leg_poly(x, top, tw + pad * 2, bw + pad * 2, lean).astype(np.int32)
    cv2.fillPoly(m, [p], 1)
    cv2.ellipse(m, (x, GROUND - 20), (int(bw * 0.62) + pad + 6, 26), 0, 0, 360, 1, -1)
    m[:CUT_Y] = 0
    return m.astype(bool)


def build():
    im = np.asarray(Image.open(SRC).convert("RGB")).astype(np.float32)
    lab = cv2.cvtColor(im.astype(np.uint8), cv2.COLOR_RGB2LAB).astype(np.float32)
    pap = np.median(np.concatenate([lab[:30].reshape(-1, 3), lab[:, :30].reshape(-1, 3)]), 0)
    notpaper = np.linalg.norm(lab - pap, axis=-1) > 10
    legcol = notpaper & (lab[..., 0] < 190) & (lab[..., 2] < 136)     # grey leg paint only (not the white chin/orange tusk)
    cut = {k: leg_mask(*v[:5]) & legcol for k, v in OLD.items() if NEW_X[k] != v[0]}   # leg 1 does not move
    out = im.copy()
    allm = np.zeros((H, W), bool)
    for m in cut.values():
        allm |= m
    fa = cv2.GaussianBlur(cv2.dilate(allm.astype(np.uint8), np.ones((7, 7), np.uint8)).astype(np.float32), (0, 0), 2)[..., None]
    # vacate: the fur band just under the shell (CUT_Y..945) is re-grown from the surrounding fur (Telea), only the
    # gap below it becomes paper
    band = cv2.dilate(allm.astype(np.uint8), np.ones((7, 7), np.uint8)); band[945:] = 0
    fur = cv2.inpaint(im.clip(0, 255).astype(np.uint8), band * 255, 9, cv2.INPAINT_TELEA).astype(np.float32)
    below = fa.copy(); below[:945] = 0
    above = fa.copy(); above[945:] = 0
    out = out * (1 - above) + fur * above
    out = out * (1 - below) + paper(im) * below
    legs_new = leg_mask(*OLD[1][:5]) & legcol
    for k in sorted(cut, key=lambda k: -OLD[k][5]):                         # far legs first, near legs over them
        dx = NEW_X[k] - OLD[k][0]
        M = np.float32([[1, 0, dx], [0, 1, 0]])
        L = cv2.warpAffine(im, M, (W, H), flags=cv2.INTER_LINEAR)
        A = cv2.warpAffine(cv2.GaussianBlur(cut[k].astype(np.float32), (0, 0), 1.0), M, (W, H))
        # fade the top 20 px of the moved leg into the fur so it meets the body under the shell
        ramp = np.clip((np.arange(H, dtype=np.float32)[:, None] - CUT_Y) / 30.0, 0, 1)
        A = A * np.maximum(ramp, 0.0)
        out = out * (1 - A[..., None]) + L * A[..., None]
        legs_new |= A > 0.5
        print("leg", k, "moved dx", dx)
    Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(V / "pick_v4.png")
    np.save(V / "legs_v4.npy", legs_new)


def guide():
    e = np.asarray(Image.open(WORK / "sketch_tarasque_canny.png").convert("L")).copy()
    for k, (x, top, tw, bw, lean, far) in OLD.items():
        nx = NEW_X[k]
        p = leg_poly(nx, top + (12 if far else 0), tw, bw, lean)
        cv2.polylines(e, [p[[1, 2]], p[[3, 0]]], False, 255, 2)
        cv2.ellipse(e, (nx, GROUND - 20), (int(bw * 0.62), 20), 0, 0, 360, 255, 2)
    lm = np.load(V / "legs_v4.npy")
    band = slice(BELLY + 6, GROUND - 30)
    e[band] = np.where(cv2.dilate(lm.astype(np.uint8), np.ones((5, 5), np.uint8))[band] > 0, e[band], 0)
    Image.fromarray(e).convert("RGB").save(V / "sketch_v4_canny.png")
    import shutil
    for k in ("block", "soft"):
        shutil.copy(WORK / f"sketch_tarasque_{k}.png", V / f"sketch_v4_{k}.png")
    print("guide v4")


if __name__ == "__main__":
    {"build": build, "guide": guide}[sys.argv[1]]()
