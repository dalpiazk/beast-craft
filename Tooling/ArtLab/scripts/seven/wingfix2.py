# Beast Craft ArtLab, remaining-seven finals (2026-09-26): Griffin wing fix round 2: the V2 variant the producer picked (wings rooted on the back behind the ruff).
"""Griffin #1 wing fix, round 2. Producer: 'the wings appear to be coming out of the griffins' necks'.
Anatomy: both wings root on the BACK at the shoulder blades (withers), behind the neck/ruff; the far wing's root is
hidden behind the body, only its upper part rises above the back line.

  python wingfix2.py build     -> $ARTLAB_WORK/v2/pick_v{1,2,3}.png (+ masks, roots): body without wings on paper,
                                  far wing behind the body, near wing re-seated on the back line, tilted back
  python wingfix2.py guide V   -> after gen4 prep on pick_vV: canny + wing contours + anatomy guide (back line,
                                  scapula bump, wing-root arc); block sky->cream. Writes $ARTLAB_WORK/v2/sketch_vV_*.png
  python wingfix2.py inpaint   -> SDXL mask-inpaint of the wing/back junction on each pick_vV (GPU)
"""
import sys
from common import *
import cv2
from colour import char_mask

V2 = WORK / "v2"; V2.mkdir(exist_ok=True)
SRC = WORK / "pick_before_wingfix.png"
ROOT_OLD = (600, 700)                     # near-wing root on the original L8
# variants: near root (1x), tilt back (deg, CCW), scale; far wing root/tilt/scale (root hidden behind the body)
VAR = {
    1: dict(root=(335, 630), rot=8, s=0.90, froot=(372, 610), frot=0, fs=0.74),
    2: dict(root=(320, 660), rot=15, s=0.90, froot=(360, 640), frot=6, fs=0.74),
    3: dict(root=(315, 690), rot=22, s=0.85, froot=(352, 668), frot=12, fs=0.72),
}
BACK = [(300, 560), (316, 600), (322, 650), (312, 710), (298, 770)]   # back line behind the ruff -> haunch (1x)


def paper_like(im):
    p = np.median(np.concatenate([im[:30].reshape(-1, 3), im[:, -30:].reshape(-1, 3), im[:, :30].reshape(-1, 3)]), 0)
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    g = (1.02 - 0.04 * ((xx / W) * .4 + (yy / H) * .6))[..., None]
    return (p[None, None] * g + np.random.default_rng(5).normal(0, 2.0, im.shape)).clip(0, 255)


def wing_rgba():
    im = np.asarray(Image.open(SRC).convert("RGB"))
    near = np.load(WORK / "near_wing.npy"); near[:490, :600] = False
    near = cv2.morphologyEx(near.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8)).astype(bool)
    near_d = cv2.dilate(near.astype(np.uint8), np.ones((11, 11), np.uint8)).astype(bool)
    ys, xs = np.nonzero(near_d); x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    rgb = im[y0:y1, x0:x1][:, ::-1]
    a = cv2.GaussianBlur(near[y0:y1, x0:x1].astype(np.float32), (0, 0), 1.2)[:, ::-1]
    root = (x1 - 1 - ROOT_OLD[0], ROOT_OLD[1] - y0)          # root inside the mirrored crop
    return rgb, a, root, near_d


def place(rgb, a, root, dst_root, rot, s):
    """Scale about the root, rotate CCW by rot about the root, translate the root to dst_root -> full-canvas layers."""
    M = cv2.getRotationMatrix2D((float(root[0]), float(root[1])), rot, s)
    M[0, 2] += dst_root[0] - root[0]; M[1, 2] += dst_root[1] - root[1]
    L = cv2.warpAffine(rgb.astype(np.float32), M, (W, H), flags=cv2.INTER_LINEAR, borderValue=0)
    A = cv2.warpAffine(a, M, (W, H), flags=cv2.INTER_LINEAR, borderValue=0)
    return L, A


def build():
    im = np.asarray(Image.open(SRC).convert("RGB"))
    wrgb, wa, wroot, near_d = wing_rgba()
    far = np.load(WORK / "far_wing.npy")
    far_d = cv2.dilate(far.astype(np.uint8), np.ones((11, 11), np.uint8)).astype(bool)
    body = char_mask(im) & ~near_d & ~far_d
    body = cv2.morphologyEx(body.astype(np.uint8), cv2.MORPH_OPEN, np.ones((7, 7), np.uint8))
    n, lab, st, _ = cv2.connectedComponentsWithStats(body, 8)
    body = np.isin(lab, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] > 3000]).astype(np.float32)
    ba = cv2.GaussianBlur(body, (0, 0), 1.2)[..., None]
    base = im * ba + paper_like(im) * (1 - ba)                           # body only, on paper
    for v, c in VAR.items():
        out = base.copy()
        FL, FA = place(wrgb, wa, wroot, c["froot"], c["frot"], c["fs"])
        FA = FA * (1 - cv2.dilate(body, np.ones((5, 5), np.uint8)))     # far wing: only where the body is not
        FL = FL * 0.93 + np.array([246, 238, 224]) * 0.07                # a touch of air (it is further away)
        out = out * (1 - FA[..., None]) + FL * FA[..., None]
        NL, NA = place(wrgb, wa, wroot, c["root"], c["rot"], c["s"])
        out = out * (1 - NA[..., None]) + NL * NA[..., None]
        Image.fromarray(out.clip(0, 255).astype(np.uint8)).save(V2 / f"pick_v{v}.png")
        np.save(V2 / f"near_v{v}.npy", NA > 0.5); np.save(V2 / f"far_v{v}.npy", FA > 0.5)
        np.save(V2 / "body.npy", body > 0.5)
        print("built", v, c)


def guide(v):
    """Run after `gen4.py prep griffin $ARTLAB_WORK/v2/pick_vV.png` (which writes WORK/sketch_griffin_*)."""
    c = VAR[v]
    pick = np.asarray(Image.open(V2 / f"pick_v{v}.png").convert("RGB"))
    e = np.asarray(Image.open(WORK / "sketch_griffin_canny.png").convert("L")).copy()
    near, far = np.load(V2 / f"near_v{v}.npy"), np.load(V2 / f"far_v{v}.npy")
    g = cv2.cvtColor(pick, cv2.COLOR_RGB2GRAY)
    low = cv2.Canny(cv2.GaussianBlur(g, (3, 3), 0), 15, 45)
    wings = cv2.dilate((near | far).astype(np.uint8), np.ones((7, 7), np.uint8)).astype(bool)
    e[wings & (low > 0)] = 255
    for m in (near, far):
        cs, _ = cv2.findContours(m.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        cv2.drawContours(e, cs, -1, 255, 1)
    # anatomy guide: back line (behind the ruff -> haunch), scapula bump at the root, wing-root arc
    cv2.polylines(e, [np.array(BACK, np.int32)], False, 255, 2)
    rx, ry = c["root"]
    cv2.ellipse(e, (rx + 8, ry - 6), (34, 22), -20, 180, 360, 255, 2)       # scapula bump on the back
    cv2.ellipse(e, (rx, ry), (46, 30), -30 + c["rot"], 200, 340, 255, 2)   # wing-root arc (feathers wrap the shoulder)
    Image.fromarray(e).convert("RGB").save(V2 / f"sketch_v{v}_canny.png")
    blk = np.asarray(Image.open(WORK / "sketch_griffin_block.png").convert("RGB")).copy()
    sky = np.all(np.abs(blk.astype(int) - np.array([0x9c, 0xc7, 0xd9])) < 12, -1); blk[sky] = (0xff, 0xf3, 0xde)
    Image.fromarray(blk).save(V2 / f"sketch_v{v}_block.png")
    from sketches_soft import soften
    soften(Image.fromarray(blk)).save(V2 / f"sketch_v{v}_soft.png")
    print("guide", v, "wing edges", int((wings & (low > 0)).sum()), "sky", int(sky.sum()))


def inpaint():
    """SDXL inpaint of the junction (near-wing base + back line band) so feathers blend into the back."""
    from rigparts import inpaint_pipe, sd_inpaint
    ip = inpaint_pipe()
    log = {}
    for v, c in VAR.items():
        pick = np.asarray(Image.open(V2 / f"pick_v{v}.png").convert("RGB"))
        m = np.zeros((H, W), np.uint8)
        rx, ry = c["root"]
        cv2.ellipse(m, (rx + 6, ry - 10), (58, 70), -15, 0, 360, 1, -1)
        cv2.polylines(m, [np.array(BACK[:3], np.int32)], False, 1, 18)
        big = cv2.resize(pick, (2 * W, 2 * H), interpolation=cv2.INTER_LANCZOS4)
        mb = cv2.resize(m, (2 * W, 2 * H), interpolation=cv2.INTER_NEAREST).astype(bool)
        with Timer() as t:
            out, tries = sd_inpaint(ip, big, mb, "griffin feathered wing root on the back at the shoulder blades, "
                                                 "golden feathers blending into tawny back", seed=11)
        res = cv2.resize(out, (W, H), interpolation=cv2.INTER_AREA)
        Image.fromarray(pick).save(V2 / f"pick_v{v}_preinpaint.png"); Image.fromarray(res).save(V2 / f"pick_v{v}.png")
        Image.fromarray(m * 255).save(V2 / f"junction_mask_v{v}.png")
        log[v] = dict(s=round(t.dt, 1), retries=tries); print("inpaint", v, log[v], flush=True)
    (V2 / "inpaint_log.json").write_text(json.dumps(log, indent=1))


if __name__ == "__main__":
    c = sys.argv[1]
    if c == "build":
        build()
    elif c == "guide":
        guide(int(sys.argv[2]))
    elif c == "inpaint":
        inpaint()
