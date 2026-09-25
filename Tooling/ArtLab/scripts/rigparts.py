"""Rig-ready exports (final): transparent character PNG (pivot at the feet) + round-1-style parts split.

  python rigparts.py BEAST [--no-inpaint]

Method (round-1 parts.py, adapted to the 2x finals):
  1. Character alpha: SAM 2.1 box mask on the final (at 1x, upsampled) -> cleanup: the plum contour ring is added
     back with a soft alpha from the colour distance to the local paper colour (so the bold outline is kept and
     the cream paper is not), tiny specks dropped, holes filled.
  2. Parts: SAM point prompts (pos/neg, optional box) per part from PARTS[beast] (coords in final 2x px).
     Overlaps -> front-most part wins (ORDER back->front); orphans -> nearest part; islands < 600 px merged.
  3. Occlusion fill: each part listed in EXTEND is extended under the parts drawn in front of it (hull of the part
     clipped to a dilation), pre-filled with a Telea inpaint of its own colours, then SDXL-inpainted at 0.55 on a
     crop around the region (house prompt), so the part has paint under its neighbours when it rotates.
  4. Pivots: root (body) = feet ground contact; child pivot = centre of its contact band with the parent.
Input: $ARTLAB_OUT/BEAST_final.png (the finish.py output) and, when present, $ARTLAB_WORK/BEAST_extra_mask.png (the
finish.py line mask, which wins over the SAM box mask: the golem's has the stones and no grass or paper gaps).
Outputs in $ARTLAB_OUT/rig/BEAST/: character.png (cropped, pivot = bottom centre), character_full.png (canvas),
parts/*.png, parts.json, parts_overlay.png, parts_recomposed.png. Then archive_rig.py copies them into the repo.
Per-beast points and polygons: PARTS/CLIPS below (phoenix, kirin) and parts/golem.json.
"""
import sys
from common import *
import cv2

INK = np.array([59, 28, 38], np.float32)

# coordinates in final-image px (1792 x 2304). pos/neg points per part; order = back -> front
PARTS = {
    "phoenix": dict(
        parts={
            "back_wing":  dict(pos=[(250, 1250), (220, 1120), (300, 1400)], neg=[(700, 1500), (560, 900)]),
            "tail":       dict(pos=[(1380, 1850), (1500, 1950), (1180, 1860), (1250, 1700)], neg=[(700, 1500), (1350, 1200)]),
            "legs":       dict(pos=[(400, 2080), (760, 2080), (470, 1960), (700, 1960)], neg=[(700, 1500)],
                               box=(250, 1880, 1000, 2240)),
            "body":       dict(pos=[(520, 1500), (800, 1560), (650, 1720), (900, 1400)], neg=[(650, 700), (1350, 1200), (400, 2100)]),
            "head":       dict(pos=[(640, 560), (850, 300), (720, 1000), (820, 1080), (900, 150)], neg=[(650, 1600)]),
            "front_wing": dict(pos=[(1350, 1250), (1470, 1050), (1200, 1400), (1100, 1450)], neg=[(650, 1500), (700, 800)]),
        },
        order=["back_wing", "tail", "legs", "body", "head", "front_wing"],
        parent={"body": None, "head": "body", "front_wing": "body", "back_wing": "body", "tail": "body", "legs": "body"},
        extend=["back_wing", "tail", "body"],
        desc={"back_wing": "orange flame feathers wing", "tail": "orange and yellow flame plume tail feathers",
              "body": "cream and orange feathered bird body"},
    ),
    "kirin": dict(
        parts={
            "tail":       dict(pos=[(1500, 1180), (1600, 1260), (1330, 1160)], neg=[(1150, 1300), (1400, 1500)]),
            "legs_back":  dict(pos=[(1330, 1750), (1300, 1950), (1260, 1550)], neg=[(1150, 1300), (900, 1800)],
                               box=(1150, 1480, 1450, 2090)),
            "legs_front": dict(pos=[(880, 1750), (860, 1980), (830, 1580)], neg=[(1000, 1300), (1300, 1800)],
                               box=(700, 1480, 1000, 2090)),
            "body":       dict(pos=[(1000, 1300), (1200, 1250), (800, 1350), (900, 1150)], neg=[(700, 800), (1500, 1200), (880, 1850)]),
            "head":       dict(pos=[(620, 850), (700, 700), (560, 900), (760, 640)], neg=[(930, 280), (1000, 1300)]),
            "mane":       dict(pos=[(850, 780), (900, 900), (820, 680)], neg=[(620, 850), (1000, 1300)]),
            "horn":       dict(pos=[(930, 300), (760, 420), (690, 540)], neg=[(650, 850)]),
        },
        order=["tail", "legs_back", "legs_front", "body", "mane", "head", "horn"],
        parent={"body": None, "head": "body", "mane": "body", "horn": "head", "tail": "body",
                "legs_front": "body", "legs_back": "body"},
        extend=["tail", "legs_back", "legs_front", "body"],
        desc={"tail": "curled cream tail", "legs_back": "cream deer legs, dark hooves", "legs_front": "cream deer legs, dark hooves",
              "body": "cream fur deer body, amber back"},
    ),
    # golem coordinates are filled in after the final is made (GOLEM_PARTS below)
}


# clip polygons (drawn on a 1556x2000 preview -> x1.152 to final px). SAM gives the painted edges; the polygon bounds
# each part so SAM's whole-body candidates cannot leak (and is the fallback).
D = 1792 / 1556
CLIPS = {
    "phoenix": {
        "head": [(280, 60), (950, 60), (950, 780), (890, 1000), (700, 1080), (480, 1100), (360, 1060), (300, 850)],
        "back_wing": [(110, 900), (330, 900), (330, 1150), (260, 1560), (110, 1560)],
        "front_wing": [(1330, 790), (1350, 1400), (1000, 1450), (760, 1350), (700, 1150), (930, 1150), (1150, 1000)],
        "tail": [(1360, 1380), (1420, 1920), (900, 1920), (870, 1650), (960, 1470)],
        "legs": [(200, 1690), (900, 1690), (900, 2000), (200, 2000)],
        "body": [(200, 1050), (800, 980), (960, 1450), (880, 1760), (200, 1780), (130, 1400)],
    },
    "kirin": {
        "horn": [(560, 200), (910, 200), (910, 310), (690, 545), (560, 545)],
        "head": [(380, 470), (740, 470), (765, 650), (700, 830), (540, 830), (380, 800)],
        "mane": [(700, 540), (780, 600), (880, 950), (820, 1000), (730, 900), (700, 700)],
        "body": [(540, 820), (1110, 950), (1270, 1100), (1270, 1300), (540, 1310)],
        "tail": [(1090, 950), (1490, 950), (1490, 1190), (1200, 1190), (1090, 1100)],
        "legs_front": [(620, 1270), (860, 1270), (860, 2000), (620, 2000)],
        "legs_back": [(1050, 1240), (1270, 1240), (1270, 2000), (1050, 2000)],
    },
}
for _b, _c in CLIPS.items():
    for _k, _poly in _c.items():
        PARTS[_b]["parts"][_k]["clip"] = [(x * D, y * D) for x, y in _poly]


def load_golem_parts():
    p = pathlib.Path(__file__).with_name("parts") / "golem.json"
    if p.exists():
        d = json.loads(p.read_text())
        for k, v in d["parts"].items():
            for kk in ("pos", "neg"):
                v[kk] = [tuple(x) for x in v.get(kk, [])]
            if "box" in v:
                v["box"] = tuple(v["box"])
        PARTS["golem"] = d


_sam = {}


def sam():
    from transformers import Sam2Processor, Sam2Model
    if not _sam:
        _sam["p"] = Sam2Processor.from_pretrained(SAM); _sam["m"] = Sam2Model.from_pretrained(SAM).eval()
    return _sam["p"], _sam["m"]


def sam_multi(img_small, s, pt, neg):
    p, m = sam()
    pts = [[float(pt[0] * s), float(pt[1] * s)]] + [[float(x * s), float(y * s)] for x, y in neg]
    inp = p(images=img_small, input_points=[[pts]], input_labels=[[[1] + [0] * len(neg)]], return_tensors="pt")
    with torch.no_grad():
        o = m(**inp, multimask_output=True)
    ms = p.post_process_masks(o.pred_masks, inp["original_sizes"])[0][0].numpy().astype(bool)
    return ms, o.iou_scores[0, 0].float().numpy()


def poly_mask(shape, poly, s=1.0):
    pm = np.zeros(shape, np.uint8)
    cv2.fillPoly(pm, [np.array([[x * s, y * s] for x, y in poly], np.int32)], 1)
    return pm.astype(bool)


def sam_part(img_small, s, spec, fg_small):
    """Round-1 SAM point prompts, made robust for painted (non-flat) finals: one SAM call per positive point (with
    all negatives); of the 3 candidates keep the best-scoring one that contains its point, excludes every negative
    point and is not larger than max_frac of the character; union over points; clip to the part's polygon if given
    (the polygon is also the fallback when SAM returns nothing usable)."""
    shp = fg_small.shape
    clip = poly_mask(shp, spec["clip"], s) if "clip" in spec else np.ones(shp, bool)
    maxa = spec.get("max_frac", 0.45) * fg_small.sum()
    out = np.zeros(shp, bool); sc_used = []
    for pt in spec["pos"]:
        ms, sc = sam_multi(img_small, s, pt, spec["neg"])
        px, py = int(pt[0] * s), int(pt[1] * s)
        ok = []
        for i in range(len(ms)):
            mm = ms[i] & fg_small
            if not ms[i][py, px] or mm.sum() > maxa or mm.sum() < 50:
                continue
            if any(ms[i][int(y * s), int(x * s)] for x, y in spec["neg"]):
                continue
            ok.append(i)
        if ok:
            b = max(ok, key=lambda i: sc[i]); out |= ms[b]; sc_used.append(float(sc[b]))
    out &= clip & fg_small
    src = "sam"
    if out.sum() < 0.3 * (clip & fg_small).sum() and "clip" in spec:
        out = clip & fg_small; src = "polygon"
    return out, (round(float(np.mean(sc_used)), 3) if sc_used else 0.0), src


def dil(m, r):
    return cv2.dilate(m.astype(np.uint8), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1,) * 2)).astype(bool)


def hull(mask):
    cs, _ = cv2.findContours(mask.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    h = np.zeros(mask.shape, np.uint8)
    if cs:
        cv2.fillPoly(h, [cv2.convexHull(np.vstack(cs))], 1)
    return h.astype(bool)


def char_alpha(rgb, extra_mask=None):
    """SAM box mask + contour-ring cleanup -> float alpha in [0,1]."""
    from masks import line_mask
    H2, W2 = rgb.shape[:2]; u = W2 / 896
    small = cv2.resize(rgb, (896, round(896 * H2 / W2)), interpolation=cv2.INTER_AREA)
    m1, _, _ = line_mask(small)
    m = cv2.GaussianBlur(cv2.resize(m1.astype(np.float32), (W2, H2)), (0, 0), 2 * u) > 0.5
    if extra_mask is not None:      # the finish.py line mask (golem: + stones, - ground grass, - paper gaps) wins
        m = extra_mask
    # paper colour estimate: inpaint the character + ring from the surrounding paper
    ring = dil(m, int(22 * u)) & ~m
    s = 0.25
    sm = cv2.resize(rgb, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
    hole = cv2.resize(dil(m, int(24 * u)).astype(np.uint8), sm.shape[1::-1], interpolation=cv2.INTER_NEAREST)
    paper = cv2.resize(cv2.inpaint(sm, hole, 7, cv2.INPAINT_TELEA), (W2, H2), interpolation=cv2.INTER_CUBIC).astype(np.float32)
    dist = np.linalg.norm(rgb.astype(np.float32) - paper, axis=-1)
    a_ring = np.clip((dist - 10) / 45, 0, 1) * ring
    alpha = np.maximum(m.astype(np.float32), a_ring)
    # cleanup: drop specks (< 0.3% of the largest component), fill small holes
    b = (alpha > 0.5).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(b, 8)
    if n > 2:
        big = st[1:, cv2.CC_STAT_AREA].max()
        keep = np.isin(lab, [i for i in range(1, n) if st[i, cv2.CC_STAT_AREA] >= 0.003 * big])
        alpha *= dil(keep, 3)
    inv = (alpha < 0.5).astype(np.uint8)
    n, lab, st, _ = cv2.connectedComponentsWithStats(inv, 4)
    for i in range(1, n):
        x, y, w_, h_, a_ = st[i]
        if a_ < 0.0003 * H2 * W2 and x > 0 and y > 0 and x + w_ < W2 and y + h_ < H2:
            alpha[lab == i] = 1.0
    return alpha, m


def feet(mask_legs_or_all):
    ys, xs = np.nonzero(mask_legs_or_all)
    fy = int(ys.max())
    band = mask_legs_or_all[max(0, fy - 40):fy + 1]
    bx = np.nonzero(band)[1]
    return int((bx.min() + bx.max()) / 2), fy


def inpaint_pipe():
    import diffusers as D
    vae = D.AutoencoderKL.from_pretrained(VAE, dtype=DTYPE)
    ip = D.StableDiffusionXLInpaintPipeline.from_pretrained(BASE, vae=vae, dtype=DTYPE).to(DEVICE)
    ip.scheduler = D.DPMSolverMultistepScheduler.from_config(ip.scheduler.config, use_karras_sigmas=True,
                                                             algorithm_type="dpmsolver++")
    ip.set_progress_bar_config(disable=True)
    return ip


def sd_inpaint(ip, rgb, mask, prompt, seed=7):
    """Crop around the mask, fit to <= 1024 on the long side (multiple of 8), inpaint, paste back. Retries NaN/black."""
    ys, xs = np.nonzero(dil(mask, 60))
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    # at least 512 px context
    cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
    hh, ww = max(y1 - y0, 512) // 2, max(x1 - x0, 512) // 2
    y0, y1 = max(0, cy - hh), min(rgb.shape[0], cy + hh); x0, x1 = max(0, cx - ww), min(rgb.shape[1], cx + ww)
    crop, mc = rgb[y0:y1, x0:x1], mask[y0:y1, x0:x1]
    sc = min(1.0, 1024 / max(crop.shape[:2]))
    tw, th = max(8, round(crop.shape[1] * sc / 8) * 8), max(8, round(crop.shape[0] * sc / 8) * 8)
    ci = Image.fromarray(crop).resize((tw, th), Image.LANCZOS)
    mi = Image.fromarray((mc * 255).astype(np.uint8)).resize((tw, th), Image.NEAREST)
    neg = ("flat colors, vector art, sticker, glossy, violet, purple, cyan, magenta, blue, text, lowres, "
           "worst quality, low quality, blurry, 3d, extra limbs, face, eyes")
    for attempt in range(6):
        res = ip(prompt=f"no humans, {prompt}, painterly, soft cel shading, warm light, simple background, masterpiece",
                 negative_prompt=neg, image=ci, mask_image=mi, width=tw, height=th, strength=0.55,
                 num_inference_steps=24, guidance_scale=5.0, generator=gen(seed + attempt)).images[0]
        r = np.asarray(res)
        if r.max() > 8 and np.isfinite(r).all():
            break
        print("NaN/black inpaint, retry", flush=True)
        empty_cache()
    else:
        return rgb, attempt + 1
    r = np.asarray(res.resize((x1 - x0, y1 - y0), Image.LANCZOS))
    out = rgb.copy()
    out[y0:y1, x0:x1][mc] = r[mc]
    return out, attempt


def main(beast, do_inpaint=True):
    if beast == "golem":
        load_golem_parts()
    cfg = PARTS[beast]
    OD = OUT / "rig" / beast; (OD / "parts").mkdir(parents=True, exist_ok=True)
    rgb = np.asarray(Image.open(OUT / f"{beast}_final.png").convert("RGB"))
    H2, W2 = rgb.shape[:2]
    extra = None
    xm = WORK / f"{beast}_extra_mask.png"
    if xm.exists():
        extra = np.asarray(Image.open(xm).convert("L").resize((W2, H2))) > 127
    alpha, core = char_alpha(rgb, extra)
    fg = alpha > 0.5

    s = 896 / W2
    small = Image.fromarray(cv2.resize(rgb, (896, round(H2 * s)), interpolation=cv2.INTER_AREA))
    ORDER = cfg["order"]; PARENT = cfg["parent"]
    raw, scores = {}, {}
    fg_small = cv2.resize(fg.astype(np.uint8), small.size, interpolation=cv2.INTER_NEAREST).astype(bool)
    for k in ORDER:
        mk, sc, src = sam_part(small, s, cfg["parts"][k], fg_small)
        raw[k] = cv2.resize(mk.astype(np.uint8), (W2, H2), interpolation=cv2.INTER_NEAREST).astype(bool)
        scores[k] = dict(iou=sc, src=src)
        print(f"SAM {k}: {sc} {src} area={raw[k].mean():.3f}", flush=True)

    owner = np.full((H2, W2), -1, np.int16); overlap = np.zeros((H2, W2), np.int16)
    for i, k in enumerate(ORDER):
        mk = raw[k] & fg
        overlap += mk; owner[mk] = i
    n_overlap = int(((overlap > 1) & fg).sum())
    orphan = fg & (owner < 0); n_orphan = int(orphan.sum())
    if n_orphan:
        dists = np.stack([cv2.distanceTransform((owner != i).astype(np.uint8), cv2.DIST_L2, 5) for i in range(len(ORDER))])
        near = np.argmin(dists, 0); owner[orphan] = near[orphan]
    owner[~fg] = -1
    # orphans assigned outside a part's own clip polygon go to the root part instead (e.g. golem haunch -> body)
    root_i = ORDER.index([k for k in ORDER if PARENT[k] is None][0])
    n_reclip = 0
    for i, k in enumerate(ORDER):
        spec = cfg["parts"][k]
        if i == root_i or "clip" not in spec:
            continue
        cl = dil(poly_mask((H2, W2), spec["clip"]), 12)
        bad = (owner == i) & ~cl
        owner[bad] = root_i; n_reclip += int(bad.sum())
    n_island = 0
    for i, k in enumerate(ORDER):
        nl, lab, st, _ = cv2.connectedComponentsWithStats((owner == i).astype(np.uint8), 8)
        if nl <= 2:
            continue
        keep = 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))
        for j in range(1, nl):
            if j != keep and st[j, cv2.CC_STAT_AREA] < 600:
                sel = lab == j
                ring = dil(sel, 2) & ~sel & (owner >= 0) & (owner != i)
                if ring.any():
                    owner[sel] = np.bincount(owner[ring]).argmax(); n_island += int(sel.sum())
    n_fg = int(fg.sum())
    stats = dict(fg_px=n_fg, overlap_px=n_overlap, orphan_px=n_orphan, island_px=n_island, reclip_px=n_reclip,
                 auto_fixed_pct=round(100 * (n_overlap + n_orphan + n_island) / n_fg, 2), sam_iou=scores)
    print(stats, flush=True)
    final = {k: owner == i for i, k in enumerate(ORDER)}

    # --- transparent character (full canvas + cropped with the pivot at bottom centre)
    fx, fy = feet(fg)
    a8 = (alpha * 255).round().astype(np.uint8)
    Image.fromarray(np.dstack([rgb, a8])).save(OD / "character_full.png")
    ys, xs = np.nonzero(a8 > 0)
    half = int(max(fx - xs.min(), xs.max() - fx)) + 8
    top = int(ys.min()) - 8
    crop = np.zeros((fy - top + 1, 2 * half, 4), np.uint8)
    sx0 = fx - half
    for (yy0, yy1) in [(max(top, 0), fy + 1)]:
        src = np.dstack([rgb, a8])[yy0:yy1, max(sx0, 0):min(fx + half, W2)]
        crop[yy0 - top:yy1 - top, max(sx0, 0) - sx0:max(sx0, 0) - sx0 + src.shape[1]] = src
    Image.fromarray(crop).save(OD / "character.png")

    # --- overlay
    cols = np.array([[80, 80, 255], [255, 160, 0], [0, 200, 0], [230, 40, 40], [255, 255, 0], [200, 0, 255], [0, 220, 220]])
    ov = rgb.astype(np.float32).copy()
    for i in range(len(ORDER)):
        ov[owner == i] = 0.45 * ov[owner == i] + 0.55 * cols[i % len(cols)]
    Image.fromarray(ov.astype(np.uint8)).save(OD / "parts_overlay.png")

    # --- occlusion fill
    ip = inpaint_pipe() if do_inpaint else None
    layers, inp_log = {}, {}
    for i, k in enumerate(ORDER):
        mk = final[k]
        part_rgb = rgb.copy()
        front = np.any([final[j] for j in ORDER[i + 1:]], axis=0) if i + 1 < len(ORDER) else np.zeros_like(mk)
        region = np.zeros_like(mk)
        if k in cfg.get("extend", []) and mk.any():
            region = hull(mk) & dil(mk, 110) & front & ~mk
            if k == "body":      # body keeps paint under everything attached to it
                region = hull(mk) & front & ~mk
        pa = np.where(mk, alpha, 0).astype(np.float32)
        if region.sum() > 400:
            ys_, xs_ = np.nonzero(dil(mk | region, 16))
            y0, y1, x0, x1 = ys_.min(), ys_.max() + 1, xs_.min(), xs_.max() + 1
            srcc = np.where(mk[..., None], rgb, 0).astype(np.uint8)[y0:y1, x0:x1]
            fill = rgb.copy()
            fill[y0:y1, x0:x1] = cv2.inpaint(srcc, (~mk[y0:y1, x0:x1]).astype(np.uint8) * 255, 9, cv2.INPAINT_TELEA)
            inner = region & ~dil(~(mk | region), 10)             # keep the new edge's own paint only inside
            part_rgb = np.where(mk[..., None], rgb, fill)
            tries = 0
            if ip is not None:
                inp_mask = dil(region, 8) & ~cv2.erode(mk.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
                prefill = part_rgb.astype(np.uint8)
                with Timer() as t:
                    part_rgb, tries = sd_inpaint(ip, prefill, inp_mask, cfg["desc"].get(k, k))
                # palette guard: the inpaint model sometimes paints cyan/teal flecks (kirin neck) -> any off-palette cool
                # pixel in the inpainted area falls back to the Telea pre-fill (the part's own colours)
                hsv = cv2.cvtColor(part_rgb.astype(np.uint8), cv2.COLOR_RGB2HSV_FULL).astype(np.float32)
                hue = hsv[..., 0] * 360 / 256
                lo = 150 if beast == "golem" else 75          # golem moss is legitimately green
                cool = dil((hue >= lo) & (hue <= 330) & (hsv[..., 1] > 30) & inp_mask, 3) & inp_mask
                part_rgb = np.where(cool[..., None], prefill, part_rgb)
                n_cool = int(cool.sum())
                inp_log[k] = dict(px=int(region.sum()), s=round(t.dt, 1), retries=tries, cool_reverted_px=n_cool)
                print(f"inpaint {k}: {region.sum()} px {t.dt:.1f}s", flush=True)
            pa = np.maximum(pa, cv2.GaussianBlur(region.astype(np.float32), (0, 0), 1.5) * (region | dil(region, 2)))
        layers[k] = np.dstack([part_rgb, (pa * 255).round().astype(np.uint8)])
    stats["inpaint"] = inp_log

    # --- pivots, crop, save
    root = [k for k in ORDER if PARENT[k] is None][0]
    meta = dict(beast=beast, element=ELEMENT[beast], canvas=[W2, H2], order_back_to_front=ORDER,
                character=dict(file="character.png", full_canvas_file="character_full.png", pivot=[fx, fy],
                               pivot_local=[half, fy - top], note="character.png is cropped so the feet pivot is its bottom centre"),
                parts={})
    for k in ORDER:
        al = layers[k][..., 3] > 0
        if not al.any():
            continue
        ys_, xs_ = np.nonzero(al)
        x0, y0, x1, y1 = xs_.min(), ys_.min(), xs_.max() + 1, ys_.max() + 1
        if PARENT[k] is None:
            piv = (fx, fy)
        else:
            contact = dil(final[k], 16) & dil(final[PARENT[k]], 16)
            cy, cx = np.nonzero(contact) if contact.any() else np.nonzero(al)
            piv = (int(cx.mean()), int(cy.mean()))
        Image.fromarray(layers[k][y0:y1, x0:x1]).save(OD / "parts" / f"{k}.png")
        meta["parts"][k] = dict(file=f"parts/{k}.png", parent=PARENT[k], offset=[int(x0), int(y0)],
                                size=[int(x1 - x0), int(y1 - y0)], pivot=[int(piv[0]), int(piv[1])],
                                pivot_local=[int(piv[0] - x0), int(piv[1] - y0)])
    meta["root_pivot_note"] = f"root part '{root}' pivots at the feet ground contact; child pivots = centre of contact with the parent. Canvas px (2x finals)."
    meta["mask_stats"] = stats
    (OD / "parts.json").write_text(json.dumps(meta, indent=1))
    comp = Image.new("RGBA", (W2, H2), (246, 238, 224, 255))
    for k in ORDER:
        if k in meta["parts"]:
            comp.alpha_composite(Image.open(OD / "parts" / f"{k}.png"), tuple(meta["parts"][k]["offset"]))
    comp.convert("RGB").save(OD / "parts_recomposed.png")
    # recompose error vs the final inside the character
    rc = np.asarray(comp.convert("RGB")).astype(np.float32)
    err = float(np.abs(rc - rgb.astype(np.float32))[fg].mean())
    stats["recompose_mae"] = round(err, 2)
    (OD / "parts.json").write_text(json.dumps(meta, indent=1))
    print(json.dumps(stats))


if __name__ == "__main__":
    main(sys.argv[1], "--no-inpaint" not in sys.argv)
