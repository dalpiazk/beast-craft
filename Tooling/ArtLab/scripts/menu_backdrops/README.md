# Menu backdrops (per-region)

Scripts for the 12 region menu backdrops (UI direction D -- "painted world + storybook page"; see
`../../provenance/menu-backdrops.md` for the full provenance: prompts, seeds, the colour grade's
final parameters, and a "what didn't work" history). Each region's backdrop unlocks when a player
first reaches that region.

LOCAL-ONLY TOOLING, same as the rest of `Tooling/ArtLab/`: CI never runs `gen_backdrops.py` (it needs
the SDXL pipeline and a GPU-class machine). `grade.py` and `tokcheck.py` are cheap, model-free
(Pillow/numpy and a tokenizer load respectively) and safe to run on any machine with the ArtLab venv.

## Files

| File | What |
| --- | --- |
| `gen_backdrops.py` | txt2img generator for all 12 regions (two prompt styles -- see its module docstring) |
| `grade.py` | deterministic post-process colour grade (Pillow + numpy, no models) that unifies the 12 renders under one shared "painted journal" treatment |
| `tokcheck.py` | asserts every region's positive/negative prompt fits CLIP's 77-token window on both SDXL tokenizers |

## Regenerating one region's backdrop

```
python gen_backdrops.py r02 501 502       # two of r02 Emberreach's four seeds
python gen_backdrops.py r02 --force       # all four seeds, overwriting any that already exist
python gen_backdrops.py r02               # all four seeds, skipping any that already exist (default)
```

Run `python tokcheck.py` first after touching any prompt in `gen_backdrops.py` -- CLIP truncates
silently past 77 tokens (no error, no warning you'll notice without checking the log), and that
silent truncation is what caused most of the fix rounds in the provenance doc.

Output goes to `--out-dir`, else `$MENU_BACKDROPS_OUT`, else `<ArtLab lab folder>/work/menu_backdrops/raw`
(never into the repo, and never committed by this script). See `../common.py`'s own docstring for how
the lab folder itself is resolved from `BEASTCRAFT_ARTLAB`/`ARTLAB_WORK`.

## Grading a region

```
python grade.py <out_dir>/raw/r02_seed502.png --out-dir <out_dir>/graded
```

Grades every input with the producer-approved default parameters (see the provenance doc's parameter
table). Pass any of `--sat-reduce`, `--grade-strength`, `--haze-strength`, `--texture-opacity`,
`--vignette-strength`, etc. to try a different tuning; `grade.py --help` lists the full set. Output
never overwrites the input.

## Running in the foreground, in small batches

**Run `gen_backdrops.py` in the foreground, one region (four seeds) per invocation, not as a long-lived
background batch job.** During the actual generation runs for these backdrops, a background orchestrator
script that looped over all 12 regions in one process was repeatedly killed mid-run by the session
harness's own background-shell-pressure-reaper -- this is a session-level safeguard separate from (and
independent of) this script's own memory footprint; see the provenance doc's "what didn't work" section
for three separate kills and the free-RAM numbers at the time of each. None of the three kills left a
corrupt or partial file (each image is only written via `image.save()` after generation completes in
memory), but a killed background process also doesn't report back cleanly, so the net effect is lost
wall-clock time re-discovering what finished. Generating in the foreground, a few seeds at a time, sidesteps
the reaper entirely and lets you watch the per-seed timing and `black=` flag directly.

**Free system memory:** check free RAM before starting a region and leave real headroom -- the loaded
SDXL pipeline alone runs a sizeable working set (observed ~9-12 GB on the laptop this was built on), and
a kill was still observed once with free RAM reported as high as ~16 GB immediately beforehand, so **do
not assume a tight memory margin (e.g. 8 GB free) is enough**; the provenance doc's generation sessions
settled on waiting for roughly **15 GB free** before starting a region as a safer rule of thumb once that
pattern became clear, plus a short pause between regions. This is a pacing heuristic observed on one
machine, not a hard requirement enforced by the script -- `gen_backdrops.py` does not itself check free
memory or wait.

## What's deliberately not in this script

An InstantStyle IP-Adapter image-conditioning path (toward direction D's own backdrop, meant to pull
the rendering back from a "dark fantasy poster" drift without losing the prompt's region identity) was
tried and **rejected**: it leaked actual content -- a waterfall, green grass -- from its secondary
reference image into unrelated regions' renders, not just influencing the rendering style the way
InstantStyle's style-block-only injection is supposed to. The producer's actual fix was `grade.py`'s
deterministic post-process colour grade instead, applied to the plain prompt-only renders. That
IP-Adapter code path (and the `--style-ref`/`--refs`/`--style-scale` CLI surface that went with it) is
**not reproduced in `gen_backdrops.py`** -- this file is plain txt2img only, to keep it simple. See the
provenance doc's "what didn't work" section if a style-ref path is ever worth trying again; the
reasoning for why it leaked content (and the attention-slicing/IP-Adapter crash it also hit) is
recorded there in full.
