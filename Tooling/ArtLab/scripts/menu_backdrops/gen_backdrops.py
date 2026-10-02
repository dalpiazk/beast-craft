"""Per-region menu backdrop generator -- the 12 regions' painted background plates (UI direction D,
see `../../provenance/menu-backdrops.md`), one shared `common.py` txt2img call per region. ONE region
(4 seeds) per OS process is the recommended invocation, so the pipeline loads, generates, writes, and
the whole process exits between regions and memory is fully released -- see README.md's "memory
pacing" section for why.

Usage (ArtLab venv python, run from this directory or anywhere -- paths are resolved relative to
this file):
  python gen_backdrops.py <region_id> [seed ...] [--force] [--out-dir DIR]

Resumable: skips any seed whose output file already exists, so a killed/retried region only generates
what's missing. Pass --force to regenerate anyway (e.g. after a prompt tweak).

Output directory: `--out-dir`, else the `MENU_BACKDROPS_OUT` environment variable, else
`<ArtLab lab folder>/work/menu_backdrops/raw` (the lab folder itself comes from `common.py`'s
`BEASTCRAFT_ARTLAB`/`ARTLAB_WORK`, so nothing here is hard-coded to a user's home folder). Images are
NOT committed to the repo by this script or this commit -- see the provenance doc for how the 12
producer-approved picks are meant to reach `content/art/ui/menu_backdrops/` once the UI kit lands.

Two region groups, two prompt styles, carried over unchanged from how they were actually generated
(see the provenance doc, sections "Batch 1" and "Batch 2 round 14", for the full history):

- **r00 Hearthglen, r01 Verdant Hollow, r03 Tidefall** ("batch 1"): the original, full-sentence
  prompts written directly from `regions_brief.md`'s per-region prose, one simple shared negative
  prompt. These three never needed a re-prompt (they came through on-brief and artefact-free on the
  first batch) and were kept as-is per the producer rather than being rewritten into the style-anchor
  structure below. Seeds 401-404.
- **r02 Emberreach, r04-r11** ("batch 2", round 14 -- the final prompt state after 14 rounds of
  fixes; see the provenance doc's "what didn't work" section for the rounds in between): a shared
  `STYLE_ANCHOR` closing phrase plus a shared `NEG_BASE`/`NEG_BUILDINGS` family, with each region's
  own short, subject-first, noun-heavy clause and per-region negative additions. Seeds 501-504.

ROUND-6 NOTE: an InstantStyle IP-Adapter image-conditioning path (toward direction D's backdrop, to
pull the rendering back from round 5's "dark fantasy poster" drift) was tried and **rejected** -- it
leaked actual content (a waterfall, green grass) from its secondary reference into unrelated regions,
on top of r01's reference not being generally reusable. The producer's eventual fix was a deterministic
post-process colour grade applied to round 5's prompt-only images instead (`grade.py`, this directory).
That IP-Adapter code path is **not reproduced in this script** -- keeping this file plain txt2img only,
per the producer's "your call, document it" on simplicity vs. a flagged-off code path. See the
provenance doc's "what didn't work" section for the full reasoning and the rejected diff's shape.
"""
import argparse
import json
import os
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import common  # noqa: E402

HERE = pathlib.Path(__file__).resolve().parent
RESULTS_LOG = HERE / "gen_backdrops_results.jsonl"

GEN_W, GEN_H = 832, 1216
STEPS = 32
CFG = 6.0
RERUN_OFFSET = 5000  # added to a seed if the first attempt comes back black/NaN (see common.is_black)

BATCH1_SEEDS = [401, 402, 403, 404]
BATCH2_SEEDS = [501, 502, 503, 504]

# Batch 1's shared negative prompt (r00/r01/r03 only -- see module docstring).
BATCH1_NEGATIVE = (
    "text, watermark, logo, signature, characters, people, person, figure, animal, "
    "photo, photorealistic, 3d render, glossy, plastic, vector art, sticker, "
    "low quality, blurry, lowres, oversaturated, harsh gradient"
)

# Batch 2's shared style suffix and negative base (r02, r04-r11 -- round 14, the final state; see the
# provenance doc for the 14 rounds of fixes this went through). FIX HISTORY (short version; full
# detail in the provenance doc's "what didn't work" section):
#   round 2:  moved the style anchor from the end of the prompt to the front -- CLIP/SDXL silently
#             truncates anything past 77 tokens, and the original subject-first prompts ran
#             81-113 tokens, so the whole style anchor was landing in the truncated tail and never
#             reaching the model.
#   round 4:  dropped "pastel" from the anchor (it was washing scenes into flat, faceted, stained-
#             glass surfaces) and added composition terms so every region reads as an open landscape.
#   round 5:  moved the anchor back to the END, much shorter, with the subject clause first -- a
#             style-anchor-FIRST prompt was dominating region identity (SDXL's text encoders weight
#             earlier tokens more heavily), even though it was short enough to fit by then.
#   round 6:  tried IP-Adapter image conditioning instead of more prompt tuning -- rejected (content
#             leak from the secondary reference image); superseded by grade.py's post-process grade.
#   round 13: grade.py (this directory) -- a deterministic colour grade applied after generation,
#             instead of chasing one prompt/IP-Adapter setup that holds both identity and style.
#   round 14: r07 Thunderspire rewritten (its "forest of ... spires drinking lightning" wording was
#             personifying the spires into something creature-like -- every seed had a creature in
#             it); "creature" added to the shared negative base for every region.
STYLE_ANCHOR = "soft gouache painting, wet-blended watercolor, hand-painted animation background, wide vista"
NEG_BASE = (
    "text, watermark, logo, signature, stamp, red seal, characters, character, people, person, "
    "human, figure, silhouette of a person, animal, creature, heavy black ink, sumi-e, ink wash, "
    "high contrast, dark, low quality, blurry, oversaturated, harsh gradient"
)
NEG_BUILDINGS = ", buildings, architecture"
NEG_FLOATING = ", floating island, floating rock"
NEG_RUSTWOOD = ", autumn forest, maple leaves, monochrome"

REGIONS = {
    # --- batch 1: kept as originally generated, per producer instruction. ---
    "r00": dict(
        name="Hearthglen",
        prompt=(
            "gouache background painting, soft wet-blended washes, details painted last, a sheltered "
            "green vale with warm hedgerows and clover meadows, an old stone mill with a water wheel "
            "in the middle distance, a mossy apple orchard at the edge, soft golden late-afternoon "
            "light, gentle rolling hills, calm pale butter-gold sky in the upper middle, hand-painted "
            "animation background art, warm and cozy, no characters, no text, painterly, masterpiece, "
            "absurdres"
        ),
        negative=BATCH1_NEGATIVE,
        seeds=BATCH1_SEEDS,
    ),
    "r01": dict(
        name="Verdant Hollow",
        prompt=(
            "gouache background painting, soft wet-blended washes, details painted last, sun-dappled "
            "woodland canopy with deep green moss and ferns, ancient moss-covered standing stones in "
            "a forest clearing, dappled sunbeams breaking through leaves, soft green-gold haze, "
            "fireflies glowing faintly in the shade, calm open clearing in the upper middle, "
            "hand-painted animation background art, no characters, no text, painterly, masterpiece, "
            "absurdres"
        ),
        negative=BATCH1_NEGATIVE,
        seeds=BATCH1_SEEDS,
    ),
    "r03": dict(
        name="Tidefall",
        prompt=(
            "gouache background painting, soft wet-blended washes, details painted last, a sea cave "
            "with a waterfall of tide pouring through weathered stone arches, drowned ruins and old "
            "pillars half submerged in turquoise water, cool blue-green light, frosted icy pools in "
            "the shadows, a calm pale open sky framed by the cave mouth in the upper middle, "
            "hand-painted animation background art, no characters, no text, painterly, masterpiece, "
            "absurdres"
        ),
        negative=BATCH1_NEGATIVE,
        seeds=BATCH1_SEEDS,
    ),
    # --- batch 2, round 14 (final): style-anchor suffix + shared negative base. ---
    "r02": dict(
        name="Emberreach",
        prompt=(
            "volcanic ash plain, dark basalt ridges, glowing orange cracks, smouldering embers, "
            "warm smoky dusk sky, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS + NEG_FLOATING + ", eruption",
        seeds=BATCH2_SEEDS,
    ),
    "r04": dict(
        name="Stormcrag",
        prompt=(
            "jagged grey crags, dark storm clouds, rain curtains, distant lightning, wind-bent grass, "
            "moody light breaking through, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS + NEG_FLOATING,
        seeds=BATCH2_SEEDS,
    ),
    "r05": dict(
        name="Rustwood",
        prompt=(
            "forest of iron-bark trees, rust-red and copper foliage, metallic sheen on bark, rusted "
            "iron ruins in the undergrowth, ore-streaked ground, soft overcast light, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS + NEG_RUSTWOOD,
        seeds=BATCH2_SEEDS,
    ),
    "r06": dict(
        name="Frostmere",
        prompt=(
            "vast frozen lake to the horizon, cracked glowing blue-white ice, snowfields, dark pine "
            "treeline, pale winter sky, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS,
        seeds=BATCH2_SEEDS,
    ),
    "r07": dict(
        name="Thunderspire",
        # round 14: reworded away from "forest of ... spires drinking lightning" (personifying the
        # spires read as creature-like to the model -- every round-5 seed had a creature in it).
        prompt=(
            "storm plateau of tall glassy crystal spires, lightning striking the spire tips, dark "
            "purple-blue storm sky, rain-slicked rock, empty landscape, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS + ", bird, monster, robot, mech",
        seeds=BATCH2_SEEDS,
    ),
    "r08": dict(
        name="Deepwild",
        prompt=(
            "ancient overgrown deep forest, colossal moss-grown trunks like towers, dense glowing "
            "undergrowth, deep green teal haze, soft light shafts, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS,
        seeds=BATCH2_SEEDS,
    ),
    "r09": dict(
        name="Cinder Throne",
        prompt=(
            "charred volcanic citadel of black glass and rock, rivers of glowing lava, molten "
            "throne-like peak, ember-lit ridges, smoky red-orange glow, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS,
        seeds=BATCH2_SEEDS,
    ),
    "r10": dict(
        name="Worldcrown",
        prompt=(
            "mountain summit crowning the world, waterfall and river of fire pouring side by side "
            "from one peak, swirling violet-gold haze, layered clouds, " + STYLE_ANCHOR
        ),
        negative=NEG_BASE + NEG_BUILDINGS,
        seeds=BATCH2_SEEDS,
    ),
    "r11": dict(
        name="Duskmeridian",
        prompt=(
            "endless twilight sky realm, no ground, radiant gold dawn and deep violet dusk sharing "
            "one horizon, floating ancient stone arches, soft luminous haze, " + STYLE_ANCHOR
        ),
        # no NEG_BUILDINGS here: r11's brief explicitly wants floating stone arches.
        negative=NEG_BASE,
        seeds=BATCH2_SEEDS,
    ),
}


def default_out_dir():
    env = os.environ.get("MENU_BACKDROPS_OUT")
    if env:
        return pathlib.Path(env)
    return common.WORK / "menu_backdrops" / "raw"


def log_result(rec):
    with open(RESULTS_LOG, "a", encoding="utf-8") as f:
        f.write(json.dumps(rec) + "\n")


def parse_args():
    p = argparse.ArgumentParser(
        description="Per-region menu backdrop generator (direction D, plain SDXL txt2img, no "
                     "IP-Adapter). See the module docstring for the two prompt-style batches.")
    p.add_argument("region", choices=sorted(REGIONS))
    p.add_argument("seeds", nargs="*", type=int, default=None,
                    help="subset of the region's default seeds (see REGIONS[<id>]['seeds']); "
                         "default: all 4")
    p.add_argument("--force", action="store_true",
                    help="regenerate even seeds whose PNG already exists (default: skip them)")
    p.add_argument("--out-dir", default=None,
                    help="output directory (default: $MENU_BACKDROPS_OUT, else "
                         "<ArtLab lab folder>/work/menu_backdrops/raw)")
    return p.parse_args()


def main():
    a = parse_args()
    rid = a.region
    region = REGIONS[rid]
    rname, prompt, negative, default_seeds = region["name"], region["prompt"], region["negative"], region["seeds"]
    requested = a.seeds if a.seeds else default_seeds
    bad = [s for s in requested if s not in default_seeds]
    if bad:
        print(f"usage: seeds must be a subset of {default_seeds}, got unknown {bad}")
        sys.exit(1)

    out_dir = pathlib.Path(a.out_dir) if a.out_dir else default_out_dir()
    out_dir.mkdir(parents=True, exist_ok=True)

    def out_path_for(seed):
        return out_dir / f"{rid}_seed{seed}.png"

    todo = requested if a.force else [s for s in requested if not out_path_for(s).exists()]
    if not todo:
        print(f"{rid} {rname}: all {len(requested)} requested seeds already present in {out_dir}, "
              f"nothing to do (pass --force to regenerate anyway)")
        return

    print(f"device={common.DEVICE} dtype={common.DTYPE}")
    print(f"{rid} {rname}: generating seeds {todo} (skipping {[s for s in requested if s not in todo]}) "
          f"-> {out_dir}")

    with common.Timer() as t_load:
        pipe = common.load_pipe("txt2img")
    try:
        pipe.enable_attention_slicing("auto")
        print("attention slicing: enabled")
    except Exception as e:
        print(f"attention slicing: not available ({e})")
    print(f"pipeline loaded in {t_load.dt:.1f}s")

    t_start = time.perf_counter()
    for seed in todo:
        use_seed = seed
        attempt = 0
        while True:
            with common.Timer() as t_gen:
                image = pipe(
                    prompt=prompt,
                    negative_prompt=negative,
                    width=GEN_W,
                    height=GEN_H,
                    num_inference_steps=STEPS,
                    guidance_scale=CFG,
                    generator=common.gen(use_seed),
                ).images[0]
            black = common.is_black(image)
            print(f"{rid} {rname}: seed {seed} (used {use_seed}) attempt {attempt}: "
                  f"{t_gen.dt:.1f}s black={black}", flush=True)
            if not black or attempt >= 2:
                break
            attempt += 1
            use_seed = seed + RERUN_OFFSET * attempt
        out_path = out_path_for(seed)
        image.save(out_path)
        log_result({
            "region": rid, "name": rname, "seed": seed, "used_seed": use_seed,
            "time_s": round(t_gen.dt, 1), "black": bool(black), "path": str(out_path),
            "ts": time.strftime("%Y-%m-%dT%H:%M:%S"),
        })
        del image
        common.empty_cache()

    t_total = time.perf_counter() - t_start
    print(f"{rid} {rname}: done, {len(todo)} images in {t_total:.1f}s "
          f"(pipeline load {t_load.dt:.1f}s separate)")


if __name__ == "__main__":
    main()
