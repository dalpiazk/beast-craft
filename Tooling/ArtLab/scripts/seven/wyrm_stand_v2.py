# Beast Craft ArtLab (ad hoc, not part of a committed final): Frost Wyrm standing-pose v2 -- re-compose the
# approved art's own rig parts (not a 3D-model silhouette) into a standing pose, then a LOW-strength
# img2img/canny blend pass to fuse seams only, so the design (horns, ice shards, wings, face) is inherited
# pixel-for-pixel rather than re-imagined. See wyrm_stand.py (v1, rejected for design drift from borrowing
# the Basilisk's body plan) and compose_parts.py / v2_prep.py (not committed) for the collage.
"""
  python wyrm_stand_v2.py cn SEED...   cn_img2img from sketch_frost_wyrm_v2_{soft,canny}.png, low strength
"""
import sys
from common import *
from colour import drift

STEPS, CFG = 26, 5.0
BEAST = "frost_wyrm"

# 77 tokens with the house head/personality/tail. Dropped 'wingless' (v1's subject) -- the producer wants the
# fin kept as a visible small wing this time -- and dropped the colour-mass-only pose words that aren't
# needed now that the collage itself carries the standing structure.
SUBJ_V2 = "ice dragon, four legs, standing, tail extended, crystal wings, pale blue, dark irises, swept back horns"


def prompt():
    return house_prompt(BEAST, subj=SUBJ_V2)


def neg():
    # house negatives minus 'wings' (IP_NEG for this beast normally includes it, to stop actual wings
    # sprouting on the 'wingless' v1 design -- here the collage deliberately keeps the fin/wing part, so
    # fighting it in the negative would just invite the model to paint it out).
    return (STYLE_NEG + ", " + COLOUR_NEG[BEAST] + ", pokemon, kyurem, glaceon, ram horns, blank eyes, "
            + NEG_TAIL + ", sitting")


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


def run_cn(seeds, cn=0.6, cn_end=0.8, strength=0.45, ipa=0.4, out=None, tag="v2cn"):
    out = out or (WORK / "standing_v2")
    out.mkdir(parents=True, exist_ok=True)
    pipe = _pipes_load("cn_img2img")
    style_only(pipe, ipa)
    emb = style_embeds(pipe)
    ctrl = Image.open(WORK / f"sketch_{BEAST}_v2_canny.png").convert("RGB")
    init = Image.open(WORK / f"sketch_{BEAST}_v2_soft.png").convert("RGB")
    p, n = prompt(), neg()
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
                                           s=round(dt, 1), retries=tries, drift=round(f, 4), prompt=p, neg=n))
        print(name, f"{dt:.1f}s drift {f:.3f}", flush=True)


if __name__ == "__main__":
    c = sys.argv[1]
    seeds = [int(x) for x in sys.argv[2:]]
    if c == "cn":
        run_cn(seeds)
    else:
        raise SystemExit(f"unknown mode {c}")
