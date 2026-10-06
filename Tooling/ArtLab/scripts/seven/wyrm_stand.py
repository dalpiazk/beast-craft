# Beast Craft ArtLab (ad hoc, not part of a committed final): Frost Wyrm standing-pose candidates for Meshy image-to-3D.
"""Frost Wyrm re-pose candidates: sitting (approved design) -> standing, four legs down, for a rig-able 3D export.
Reuses the house pipeline (seven/common.py, seven/gen4.py's lock/explore_i2i machinery, the Reinhard colour lock)
with a custom colour-mass layout (wyrm_stand's build_block.py, from a Blender quadruped silhouette) instead of the
approved sitting art, so the pose is driven by structure/prompt, not by img2img off the sitting silhouette.

  python wyrm_stand.py cn SEED...       cn_img2img from sketch_frost_wyrm_{block,canny,soft}.png
  python wyrm_stand.py i2i SEED...      plain img2img (no canny), looser
  python wyrm_stand.py txt SEED...      txt2img, prompt-only fallback (no image conditioning at all)
"""
import sys
from common import *
from colour import reinhard, drift

STEPS, CFG = 26, 5.0
BEAST = "frost_wyrm"

# 77-token budget (tokcheck-style check run by hand): dropped 'quadruped' (redundant with 'four legs') and
# 'wingless' ('wings' is already an IP_NEG for this beast) to fit "four legs on the ground, standing" in.
SUBJ_STAND = ("ice dragon, four legs on the ground, standing, tail extended, pale blue, dark irises, "
              "swept back horns")
# only 'sitting' fits the negative's 77-token budget alongside the house negatives; the canny pose guide and
# the positive prompt's 'four legs on the ground, standing' carry the rest of the anti-sitting/anti-biped weight.
NEG_EXTRA = "sitting"


def prompt():
    return house_prompt(BEAST, subj=SUBJ_STAND)


def neg():
    return house_neg(BEAST) + ", " + NEG_EXTRA


def _pipes_load(kind):
    import gc
    gc.collect()
    if DEVICE == "xpu":
        torch.xpu.empty_cache()
    with Timer() as t:
        pipe = load_pipe(kind, ipa=True)
    print(f"loaded {kind} in {t.dt:.0f}s", flush=True)
    return pipe


def call(pipe, kw, tag):
    for attempt in range(4):
        from torch.nn.attention import sdpa_kernel, SDPBackend
        with Timer() as t, sdpa_kernel(SDPBackend.MATH):
            im = pipe(**kw).images[0]
        if np.asarray(im).max() > 8:
            return im, t.dt, attempt
        print("NaN/black output, retrying", tag, flush=True)
        import gc
        gc.collect()
        if DEVICE == "xpu":
            torch.xpu.empty_cache()
    return im, t.dt, 4


def _log(path, key, val):
    log = json.loads(path.read_text()) if path.exists() else {}
    log[key] = val
    path.write_text(json.dumps(log, indent=1))


def run_cn(seeds, cn=0.4, cn_end=0.55, strength=0.82, ipa=0.45, out=None, tag="cn"):
    out = out or (WORK / "standing"); out.mkdir(parents=True, exist_ok=True)
    pipe = _pipes_load("cn_img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    ctrl = Image.open(WORK / f"sketch_{BEAST}_canny.png").convert("RGB")
    init = Image.open(WORK / f"sketch_{BEAST}_soft.png").convert("RGB")
    p, n = prompt(), neg()
    for s in seeds:
        name = f"{tag}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=p, negative_prompt=n, image=init, control_image=ctrl, strength=strength,
                  controlnet_conditioning_scale=cn, control_guidance_end=cn_end,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}_prelock.png")
        im = reinhard(im, BEAST)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(seed=s, mode="cn", cn=cn, cn_end=cn_end, strength=strength, ipa=ipa,
                                           s=round(dt, 1), retries=tries, drift=round(f, 4), prompt=p, neg=n))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


def run_i2i(seeds, strength=0.88, ipa=0.45, out=None, tag="i2i"):
    out = out or (WORK / "standing"); out.mkdir(parents=True, exist_ok=True)
    pipe = _pipes_load("img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    init = Image.open(WORK / f"sketch_{BEAST}_soft.png").convert("RGB")
    p, n = prompt(), neg()
    for s in seeds:
        name = f"{tag}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=p, negative_prompt=n, image=init, strength=strength,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}_prelock.png")
        im = reinhard(im, BEAST)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(seed=s, mode="i2i", strength=strength, ipa=ipa,
                                           s=round(dt, 1), retries=tries, drift=round(f, 4), prompt=p, neg=n))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


def run_txt(seeds, ipa=0.45, out=None, tag="txt"):
    out = out or (WORK / "standing"); out.mkdir(parents=True, exist_ok=True)
    pipe = _pipes_load("txt2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    p, n = prompt(), neg()
    for s in seeds:
        name = f"{tag}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=p, negative_prompt=n, width=W, height=H,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}_prelock.png")
        im = reinhard(im, BEAST)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(seed=s, mode="txt", ipa=ipa,
                                           s=round(dt, 1), retries=tries, drift=round(f, 4), prompt=p, neg=n))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


if __name__ == "__main__":
    c = sys.argv[1]
    seeds = [int(x) for x in sys.argv[2:]]
    if c == "cn":
        run_cn(seeds)
    elif c == "i2i":
        run_i2i(seeds)
    elif c == "txt":
        run_txt(seeds)
    else:
        raise SystemExit(f"unknown mode {c}")
