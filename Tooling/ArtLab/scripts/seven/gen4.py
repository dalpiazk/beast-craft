# Beast Craft ArtLab, remaining-seven finals (2026-09-26): prep (canny, swatch block, soft init) and lock for the seven finals; lock2.py drives its lock.
"""Round-4 generator (heroic/proud 'slightly chibi', bolder lines, round-3 colour lock kept).

  python gen4.py explore BEAST IPA SEED...     txt2img + InstantStyle (weighted refs) -> work/explore/{beast}_{seed}.png
  python gen4.py prep BEAST PICK.png           colour block + soft init + canny guide from the pick's own lines
  python gen4.py lock BEAST IPA CN OUTDIR SEED...  CN img2img from the soft block + IP + colour negs + Reinhard
  python gen4.py ab_explore                    scale A/B for the unlocked (explore) setting: golem/kirin x 0.5/0.6/0.7
"""
import sys
from common import *
from colour import reinhard, drift, fidelity, char_mask
from line_pass import auto_mask
import cv2

_pipes = {}
STEPS, CFG = 26, 5.0


def pipe_for(kind):
    if kind not in _pipes:
        _pipes.clear()
        import gc; gc.collect(); torch.xpu.empty_cache() if DEVICE == "xpu" else None
        with Timer() as t:
            _pipes[kind] = load_pipe(kind, ipa=True)
        print(f"loaded {kind} in {t.dt:.0f}s", flush=True)
    return _pipes[kind]


def call(pipe, kw, tag):
    """XPU gives intermittent NaN (black) images: detect and retry the same seed (up to 4 tries)."""
    for attempt in range(4):
        from torch.nn.attention import sdpa_kernel, SDPBackend
        with Timer() as t, sdpa_kernel(SDPBackend.MATH):   # XPU: default SDPA NaNs this session
            im = pipe(**kw).images[0]
        if np.asarray(im).max() > 8:
            return im, t.dt, attempt
        print("NaN/black output, retrying", tag, flush=True)
        import gc; gc.collect(); torch.xpu.empty_cache()
    return im, t.dt, 4


def _log(path, key, val):
    log = json.loads(path.read_text()) if path.exists() else {}
    log[key] = val
    path.write_text(json.dumps(log, indent=1))


def explore(beast, ipa, seeds, out=None, tag=None):
    out = out or WORK / "explore"; out.mkdir(parents=True, exist_ok=True)
    pipe = pipe_for("txt2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    for s in seeds:
        name = f"{tag or beast}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=house_prompt(beast), negative_prompt=house_neg(beast), width=W, height=H,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(beast=beast, seed=s, ipa=ipa, s=round(dt, 1), retries=tries,
                                           drift=round(f, 4), prompt=kw["prompt"], neg=kw["negative_prompt"]))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


def main_mask(rgb):
    """Largest connected component of the character mask (drops floating embers/petals)."""
    from masks import line_mask
    m = line_mask(rgb)[0].astype(np.uint8)          # SAM box mask: handles cream-on-cream (kirin)
    n, lab, st, _ = cv2.connectedComponentsWithStats(m, 8)
    if n > 2:
        m = (lab == 1 + int(np.argmax(st[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15)))
    return m.astype(bool)


def prep(beast, pick):
    """Design lock inputs from an approved image: (1) canny from its own lines (character only), (2) a colour block
    = the pick's character quantised to the beast's swatch (nearest swatch in Lab after a mean-shift smooth) on the
    cream BG, (3) the softened block (blur + light gradient + grain, round-3 soften()) as the img2img init."""
    im = Image.open(pick).convert("RGB").resize((W, H), Image.LANCZOS)
    rgb = np.asarray(im)
    m = main_mask(rgb)
    g = cv2.cvtColor(rgb, cv2.COLOR_RGB2GRAY)
    e = cv2.Canny(cv2.GaussianBlur(g, (3, 3), 0), 50, 130)
    e[~cv2.dilate(m.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)] = 0
    Image.fromarray(e).convert("RGB").save(WORK / f"sketch_{beast}_canny.png")
    sm = cv2.pyrMeanShiftFiltering(rgb, 12, 24)
    lab = cv2.cvtColor(sm, cv2.COLOR_RGB2LAB).astype(np.float32)
    sw = np.array([[int(h[i:i + 2], 16) for i in (0, 2, 4)] for h, _ in SWATCH[beast]], np.uint8)[None]
    swl = cv2.cvtColor(sw, cv2.COLOR_RGB2LAB).astype(np.float32)[0]
    # match on lightness-weighted Lab so the value structure (light/dark regions) of the pick survives
    d = ((lab[:, :, None, :] - swl[None, None]) ** 2 * np.array([1.5, 1, 1], np.float32)).sum(-1)
    q = sw[0][np.argmin(d, -1)]
    blk = np.where(m[..., None], q, np.array(BG, np.uint8)).astype(np.uint8)
    blk = cv2.medianBlur(blk, 7)
    Image.fromarray(blk).save(WORK / f"sketch_{beast}_block.png")
    from sketches_soft import soften
    soften(Image.fromarray(blk)).save(WORK / f"sketch_{beast}_soft.png")
    Image.fromarray((m * 255).astype(np.uint8)).save(WORK / f"sketch_{beast}_mask.png")
    contact_sheet([im, Image.fromarray(e).convert("RGB"), Image.fromarray(blk), WORK / f"sketch_{beast}_soft.png"],
                  WORK / f"prep_{beast}.png", cols=4, thumb=300, labels=["pick", "canny (own lines)", "swatch block", "soft init"])


def lock(beast, ipa, cn, out, seeds, strength=0.80, cn_end=0.7, tag="lock"):
    out = pathlib.Path(out); out.mkdir(parents=True, exist_ok=True)
    pipe = pipe_for("cn_img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    ctrl = Image.open(WORK / f"sketch_{beast}_canny.png").convert("RGB")
    init = Image.open(WORK / f"sketch_{beast}_soft.png").convert("RGB")
    for s in seeds:
        name = f"{tag}_{beast}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=house_prompt(beast), negative_prompt=house_neg(beast), image=init, control_image=ctrl,
                  strength=strength, controlnet_conditioning_scale=cn, control_guidance_end=cn_end,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}_prelock.png")
        im = reinhard(im, beast)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(beast=beast, seed=s, ipa=ipa, cn=cn, strength=strength, s=round(dt, 1),
                                           retries=tries, drift=round(f, 4), drifted=d, fid=round(fidelity(im, beast), 2),
                                           prompt=kw["prompt"], neg=kw["negative_prompt"]))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


def layout_canny(beast, i):
    blk = np.asarray(Image.open(WORK / "layouts" / f"{beast}_{i}_block.png").convert("RGB"))
    a = cv2.GaussianBlur(blk, (3, 3), 0)
    e = np.max([cv2.Canny(a[..., c], 12, 36) for c in range(3)], axis=0)
    return Image.fromarray(cv2.dilate(e, np.ones((2, 2), np.uint8))).convert("RGB")


def explore_i2i(beast, ipa, idxs, strength=0.88, seed0=400, out=None, cn=0.0, cn_end=0.5, tag=None):
    """Design exploration from the randomised colour-mass layouts (layouts.py): plain img2img (no canny) at high
    denoise + InstantStyle + colour/fire negatives, then Reinhard to the beast swatch (same as the lock)."""
    out = out or WORK / "explore"; out.mkdir(parents=True, exist_ok=True)
    pipe = pipe_for("cn_img2img" if cn > 0 else "img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    for i in idxs:
        s = seed0 + i; name = f"{tag or beast}_{i}"
        if (out / f"{name}.png").exists():
            continue
        init = Image.open(WORK / "layouts" / f"{beast}_{i}_soft.png").convert("RGB")
        kw = dict(prompt=house_prompt(beast), negative_prompt=house_neg(beast), image=init, strength=strength,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        if cn > 0:
            kw.update(control_image=layout_canny(beast, i), controlnet_conditioning_scale=cn, control_guidance_end=cn_end)
        im, dt, tries = call(pipe, kw, name)
        f, d = drift(im)
        _log(out / "log.json", name, dict(beast=beast, layout=i, seed=s, ipa=ipa, strength=strength, cn=cn, s=round(dt, 1),
                                           retries=tries, drift=round(f, 4), prompt=kw["prompt"], neg=kw["negative_prompt"]))
        im.save(out / f"{name}.png")
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


if __name__ == "__main__":
    c = sys.argv[1]
    if c == "explore":
        explore(sys.argv[2], float(sys.argv[3]), [int(x) for x in sys.argv[4:]])
    elif c == "prep":
        prep(sys.argv[2], sys.argv[3])
    elif c == "lock":
        lock(sys.argv[2], float(sys.argv[3]), float(sys.argv[4]), sys.argv[5], [int(x) for x in sys.argv[6:]],
             tag=os.environ.get("LOCK_TAG", f"ip{sys.argv[3]}"))
    elif c == "explore_i2i":
        explore_i2i(sys.argv[2], float(sys.argv[3]), [int(x) for x in sys.argv[4:]])
    elif c == "ab_explore":
        for sc in (0.5, 0.6, 0.7):
            for b in ("golem", "kirin"):
                explore(b, sc, [11, 22], out=WORK / "ab_scale", tag=f"ip{sc}_{b}")

