# Beast Craft ArtLab, remaining-seven finals (2026-09-26): halo clean and bold line pass of the seven finals (the Griffin with --extra).
"""Round-4 finishing after the detail pass: SAM box mask (pale areas) -> halo clean -> bold line pass.

  python finish.py IN_2x.png OUT.png [--halo 22] [line_pass args...]

Halo clean: the soft-block img2img leaves a soft orange/cream glow band just outside the painted edge; it reads
'soft/babyish'. Pixels in a band of `halo` px (at 896 ref) outside the mask are inpainted (Telea) from the background
beyond the band, feathered, so the bold contour sits on clean ground (round-2 crispness, painterly fill kept).
"""
import sys
from common import *
import cv2
from masks import line_mask
import line_pass

BOLD = ["--outer", "5.5", "--outer-var", "0.4", "--taper", "0.5", "--inner-strength", "0.8", "--inner-lo", "0.05",
        "--inner-hi", "0.16", "--inner-kernel", "7", "--ink-hi", "0.3"]


def halo_clean(rgb, m, u, band=22):
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(2 * band * u) | 1,) * 2)
    ring = (cv2.dilate(m.astype(np.uint8), k) > 0) & ~m
    # inpaint at half res (speed), then use only inside the ring
    s = 0.5
    small = cv2.resize(rgb, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
    hole = cv2.resize((ring | m).astype(np.uint8), small.shape[1::-1], interpolation=cv2.INTER_NEAREST)
    fill = cv2.inpaint(small, hole, 9, cv2.INPAINT_TELEA)
    fill = cv2.resize(fill, rgb.shape[1::-1], interpolation=cv2.INTER_CUBIC)
    # grain so the inpainted band matches the paper texture
    g = np.random.default_rng(1).normal(0, 3, rgb.shape).astype(np.float32)
    a = cv2.GaussianBlur(ring.astype(np.float32), (0, 0), 3 * u)[..., None] * ~m[..., None]
    out = rgb.astype(np.float32) * (1 - a) + (fill.astype(np.float32) + g) * a
    return out.clip(0, 255).astype(np.uint8)


def tighten(rgb, m, u, max_in=20, ratio=0.75):
    """Pull the mask edge inward past the desaturated tan/cream glow rim the soft-block img2img paints around the
    character (measured on the phoenix: chroma 35-53 in a ~15 px band vs ~70 inside). A rim pixel is peeled when its
    chroma is below `ratio` x the LOCAL interior chroma (blurred chroma of pixels > 30 px inside), so uniformly
    low-chroma beasts (grey golem, cream kirin) are barely touched. Peeling proceeds from the edge inward only."""
    lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
    chroma = np.hypot(lab[..., 1] - 128, lab[..., 2] - 128)
    d = cv2.distanceTransform(m.astype(np.uint8), cv2.DIST_L2, 5)
    deep = (d > 30 * u).astype(np.float32)
    sig = 25 * u
    interior = cv2.GaussianBlur(chroma * deep, (0, 0), sig) / (cv2.GaussianBlur(deep, (0, 0), sig) + 1e-3)
    halo = m & (d < max_in * u) & (chroma < ratio * interior)
    out = m.copy()
    for _ in range(int(max_in * u)):
        edge = out & (cv2.erode(out.astype(np.uint8), np.ones((3, 3), np.uint8)) == 0)
        peel = edge & halo
        if not peel.any():
            break
        out &= ~peel
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(13 * u) | 1,) * 2)     # smooth away peel notches
    out = cv2.morphologyEx(out.astype(np.uint8), cv2.MORPH_OPEN, k)
    out = cv2.morphologyEx(out, cv2.MORPH_CLOSE, k)
    return cv2.GaussianBlur(out.astype(np.float32), (0, 0), 4 * u) > 0.5


def add_points(small, m1, pts):
    """Final (golem stones): SAM 2.1 point prompts for pale parts the box mask drops (standing stones). Per point keep
    the best-scoring candidate that contains the point and is small (< 2% of the frame); OR it into the mask."""
    from masks import _sam, sam_box
    sam_box(small, [0, 0, 10, 10]) if not _sam else None          # load
    P, M = _sam["p"], _sam["m"]
    added = []
    for x, y in pts:
        inp = P(images=Image.fromarray(small), input_points=[[[[float(x), float(y)]]]], input_labels=[[[1]]], return_tensors="pt")
        with torch.no_grad():
            o = M(**inp, multimask_output=True)
        ms = P.post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
        sc = o.iou_scores[0, 0].float().numpy()
        ok = [i for i in range(len(ms)) if ms[i][y, x] and 0.0002 < ms[i].mean() < 0.02]
        if ok:
            b = max(ok, key=lambda i: sc[i]); m1 = m1 | ms[b]; added.append([x, y, round(float(sc[b]), 3), int(ms[b].sum())])
    print("point masks", added, flush=True)
    return m1


def drop_ground_green(small, m1, y0):
    """Final (golem): grass tufts beside the feet are ground, not character -> drop green pixels below y0 (1x px) from
    the mask, then keep only components that are not tiny, so the contour follows the stone feet."""
    hsv = cv2.cvtColor(small, cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
    hue = hsv[..., 0] * 360 / 256; sat = hsv[..., 1] / 255
    g = (hue > 55) & (hue < 170) & (sat > 0.18)
    g[:y0] = False
    g = cv2.dilate(g.astype(np.uint8), np.ones((5, 5), np.uint8)).astype(bool)
    m = (m1 & ~g).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    n, lab, st, _ = cv2.connectedComponentsWithStats(m, 8)
    if n > 2:
        big = st[1:, cv2.CC_STAT_AREA].max()
        m = np.isin(lab, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] > 0.01 * big]).astype(np.uint8)
    print("ground green dropped px", int((m1 & ~m.astype(bool)).sum()), flush=True)
    return m.astype(bool)


def peel_paper(rgb, m, u, protect, max_in=40, tol=13.0):
    """Final (golem): SAM's mask includes a band of bare paper above the moss (the lock's soft glow, repainted as paper
    by the detail pass) -> peel edge pixels whose Lab distance to the paper colour is < tol, from the edge inward only,
    never inside `protect` (pale face + standing stones, which are close to the paper colour)."""
    lab = cv2.cvtColor(rgb, cv2.COLOR_RGB2LAB).astype(np.float32)
    b = np.concatenate([lab[:20].reshape(-1, 3), lab[:, :20].reshape(-1, 3), lab[:, -20:].reshape(-1, 3)])
    paper = np.median(b, 0)
    near = (np.linalg.norm(lab - paper, axis=-1) < tol) & ~protect
    out = m.copy()
    for _ in range(int(max_in * u)):
        edge = out & (cv2.erode(out.astype(np.uint8), np.ones((3, 3), np.uint8)) == 0)
        peel = edge & near
        if not peel.any():
            break
        out &= ~peel
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (int(9 * u) | 1,) * 2)
    out = cv2.morphologyEx(out.astype(np.uint8), cv2.MORPH_OPEN, k).astype(bool)
    print("paper peeled px", int((m & ~out).sum()), flush=True)
    return cv2.GaussianBlur(out.astype(np.float32), (0, 0), 3 * u) > 0.5


def finish(inp, out, band=28, extra=(), tight=True, pts=None, ground=None, peel=False, extra_mask=None):
    rgb = np.asarray(Image.open(inp).convert("RGB"))
    u = rgb.shape[1] / 896
    small = cv2.resize(rgb, (896, round(896 * rgb.shape[0] / rgb.shape[1])), interpolation=cv2.INTER_AREA)
    m1, _, _ = line_mask(small)                                  # SAM at 1x (CPU speed), upsampled
    if extra_mask:                                               # new-beast finals: known part masks (pale wings on cream)
        xm = np.asarray(Image.open(extra_mask).convert("L").resize(small.shape[1::-1])) > 127
        m1 = m1 | xm
        m1 = cv2.morphologyEx(m1.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))).astype(bool)
        from line_pass import fill_holes
        m1 = fill_holes(m1.astype(np.uint8), 0.003).astype(bool)
    if ground:
        m1 = drop_ground_green(small, m1, ground)
    m_nopts = m1.copy()
    if pts:
        m1 = add_points(small, m1, pts)
        m1 = cv2.morphologyEx(m1.astype(np.uint8), cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))).astype(bool)
    m = cv2.resize(m1.astype(np.float32), rgb.shape[1::-1], interpolation=cv2.INTER_LINEAR)
    m = cv2.GaussianBlur(m, (0, 0), 2 * u) > 0.5
    if peel:
        from golem_face import FACE
        prot = np.zeros(small.shape[:2], np.uint8)
        cv2.ellipse(prot, FACE[0], (FACE[1][0] + 10, FACE[1][1] + 10), 0, 0, 360, 1, -1)
        prot = prot.astype(bool) | (m1 & ~m_nopts)                      # face + the SAM stone masks
        prot = cv2.resize(prot.astype(np.uint8), rgb.shape[1::-1], interpolation=cv2.INTER_NEAREST).astype(bool)
        m = peel_paper(rgb, m, u, prot)
    if tight:   # phoenix only: peels the tan glow rim; on grey/cream beasts it eats pale stones/legs (golem)
        m = tighten(rgb, m, u)
    mp = str(out).replace(".png", "_mask.png")
    Image.fromarray((m * 255).astype(np.uint8)).save(mp)
    clean = halo_clean(rgb, m, u, band) if band > 0 else rgb
    cp = str(out).replace(".png", "_clean.png")
    Image.fromarray(clean).save(cp)
    line_pass.run(line_pass.parser().parse_args([cp, str(out), "--mask", mp] + BOLD + list(extra)))


if __name__ == "__main__":
    args = sys.argv[1:]
    band = 28
    tight = "--no-tighten" not in args
    args = [x for x in args if x != "--no-tighten"]
    pts = None
    if "--points" in args:     # "x,y;x,y" at 896-wide (1x) coordinates
        i = args.index("--points"); pts = [tuple(int(v) for v in q.split(",")) for q in args[i + 1].split(";")]; del args[i:i + 2]
    peel = "--peel-paper" in args
    args = [x for x in args if x != "--peel-paper"]
    ground = None
    if "--ground-green" in args:
        i = args.index("--ground-green"); ground = int(args[i + 1]); del args[i:i + 2]
    if "--halo" in args:
        i = args.index("--halo"); band = float(args[i + 1]); del args[i:i + 2]
    xmask = None
    if "--extra" in args:
        i = args.index("--extra"); xmask = args[i + 1]; del args[i:i + 2]
    finish(args[0], args[1], band, args[2:], tight, pts, ground, peel, xmask)
