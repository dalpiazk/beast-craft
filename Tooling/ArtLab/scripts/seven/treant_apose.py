# Beast Craft ArtLab (ad hoc, not part of a committed final): Treant A-pose candidates for Meshy image-to-3D.
"""Re-compose the approved Treant's own rig parts into an A-pose (both arms free of the body, elbows bent,
feet apart) via compose_treant.py's collage, then a low/moderate-strength ControlNet img2img blend pass --
the same method as wyrm_stand_v2.py. See compose_treant.py and v2_prep.py (not committed) for the collage
and prep steps.

  python treant_apose.py cn SEED...   cn_img2img from sketch_treant_v2_{soft,canny}.png
"""
import sys
from common import *
from colour import drift

STEPS, CFG = 26, 5.0
BEAST = "treant"

SUBJ_V2 = "treant, arms spread down and out, bent elbows, twig hands, feet apart, bark body, round leaf crown"
SUBJ_V2_34 = ("treant, arms spread down and out, bent elbows, twig hands, feet apart, round leaf crown, "
              "three quarter view")


def prompt(threequarter=False):
    return house_prompt(BEAST, subj=(SUBJ_V2_34 if threequarter else SUBJ_V2))


def neg():
    return house_neg(BEAST)


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


def run_cn(seeds, cn=0.6, cn_end=0.8, strength=0.48, ipa=0.4, threequarter=False, out=None, tag="v2cn"):
    out = out or (WORK / "standing_v2")
    out.mkdir(parents=True, exist_ok=True)
    pipe = _pipes_load("cn_img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    ctrl = Image.open(WORK / f"sketch_{BEAST}_v2_canny.png").convert("RGB")
    init = Image.open(WORK / f"sketch_{BEAST}_v2_soft.png").convert("RGB")
    p, n = prompt(threequarter), neg()
    for s in seeds:
        name = f"{tag}_{s}"
        if (out / f"{name}.png").exists():
            continue
        kw = dict(prompt=p, negative_prompt=n, image=init, control_image=ctrl, strength=strength,
                  controlnet_conditioning_scale=cn, control_guidance_end=cn_end,
                  num_inference_steps=STEPS, guidance_scale=CFG, generator=gen(s), ip_adapter_image_embeds=emb)
        im, dt, tries = call(pipe, kw, name)
        im.save(out / f"{name}.png")
        f, d = drift(im)
        _log(out / "log.json", name, dict(seed=s, mode="v2cn", cn=cn, cn_end=cn_end, strength=strength, ipa=ipa,
                                           threequarter=threequarter, s=round(dt, 1), retries=tries,
                                           drift=round(f, 4), prompt=p, neg=n))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


if __name__ == "__main__":
    c = sys.argv[1]
    seeds = [int(x) for x in sys.argv[2:]]
    if c == "cn":
        run_cn(seeds)
    else:
        raise SystemExit(f"unknown mode {c}")
