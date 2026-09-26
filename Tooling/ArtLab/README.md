# Beast Craft art lab (AI-assisted beast art)

The local, offline pipeline that made the ten beasts (the starter trio Phoenix, Golem, Kirin, then the other seven): an SDXL anime model steered by our
own approved style references, a design lock, a detail pass, a deterministic line pass, rig parts and the in-game
export. **The producer is the art director**: every design is a producer pick, and every final is producer-approved
(see `docs/art/art-brief.md` for the policy and the store-disclosure rule). How each final was made is recorded in
`provenance/<beast>.md`.

LOCAL-ONLY TOOLING: CI never runs it. Its outputs are committed (`content/art/beasts`, `content/art/source`).
Nothing here needs the network once the models are downloaded; `common.py` sets `HF_HUB_OFFLINE=1`.

## Contents
| Path | What |
| --- | --- |
| `scripts/common.py` | Paths (from environment variables), device and dtype, pipeline loading, InstantStyle, seeds |
| `scripts/beasts.py` | The house style: prompt template, per-beast subjects, negatives and colour swatches |
| `scripts/tokcheck.py` | Asserts every prompt fits CLIP's 77 tokens |
| `scripts/layouts.py` | Randomised colour-mass layouts: the exploration inits |
| `scripts/soften.py` | The soft img2img init (blur, light gradient, grain) |
| `scripts/generate.py` | `explore_i2i` (the candidates), `prep` (lock inputs from a pick) and `lock` (6 seeds) |
| `scripts/colour.py` | Reinhard colour lock to the swatch, cool-drift and palette-distance metrics |
| `scripts/masks.py` | The character mask: SAM 2.1 box prompt plus detached parts |
| `scripts/face.py` | Face protection (golem): eyes into the init, eyes restored on the lock, detail-pass masks |
| `scripts/detail_pass.py` | 2x Lanczos plus a tiled SDXL img2img pass with the tile ControlNet |
| `scripts/line_pass.py` | The deterministic line pass (no model): warm plum contour, re-coloured ink, inner lines |
| `scripts/finish.py` | Halo clean plus the bold line preset, with the golem's stone, grass and paper fixes |
| `scripts/finish_chain.sh`, `scripts/golem_chain.sh` | The detail + finish chains as run for the finals |
| `scripts/rigparts.py`, `scripts/parts/golem.json` | Transparent character (feet pivot) and the rig parts split, with occlusion fill |
| `scripts/parts_sheet.py` | Parts review sheet with a pose test |
| `scripts/sheets.py` | The candidate sheet, and the lock's consistency sheet and numbers |
| `scripts/rmblack.py` | Deletes NaN/black outputs so a fresh process redoes them |
| `scripts/archive_rig.py` | Copies a rig into `content/art/source/<beast>/` (lossless re-save) |
| `scripts/export_ingame.py` | The in-game sprite for `content/art/beasts/<beast>/` |
| `scripts/seven/` | The scripts **as run** for the other seven finals (Leviathan, Thunderbird, Griffin, Frost Wyrm, Treant, Tarasque, Basilisk), kept apart because they differ from the trio's cleaned-up set above (the trio finals as style refs, XPU math SDPA and no VAE tiling, `rigparts.py`'s inpaint validator and pivot overrides). Paths from `BEASTCRAFT_ARTLAB`, `ARTLAB_OUT` (one beast's folder), `ARTLAB_WORK` and `ARTLAB_FINALS` (see its `common.py`). Each file's first line names what it produced |
| `scripts/seven/` shared | `common.py`, `beasts.py`, `gen4.py` (prep, lock), `lock2.py` (pick-init lock), `colour.py`, `masks.py`, `sketches_soft.py`, `detail_pass.py`, `line_pass.py`, `finish.py`, `golem_face.py`, `facemask.py`, `chain.sh` (the finish chain), `offpal.py`, `despeck.py`, `rigparts.py` + `<beast>_parts.json`, `parts_sheet.py` |
| `scripts/seven/` fixes | Griffin `wingfix.py` (round 1, rejected), `wingfix2.py`, `tailfix.py`, `streakfix.py`; Frost Wyrm `frostfix.py`; Tarasque `legfix.py`, `legcomp.py`, `legswap.py`, `legcomp4.py`, `chinfix3.py` (`chinfix.py`, `chinfix2.py` rejected); Basilisk `basfix.py`, `farhind.py` |
| `refs/` | The three style references (our own approved round-2 images `d_406`, `a_104`, `b_208`) |
| `provenance/` | Per final: prompts, seeds, settings, the chosen candidate and the producer's decision |
| `requirements.txt` | The package versions used |

## Setup
Tested on Windows 11 with an Intel Arc 140V iGPU (16 GB shared) through PyTorch XPU, in bf16. CUDA works the same
way (`common.py` picks `xpu`, then `cuda`, then `cpu`); CPU works but is very slow.

1. Install [uv](https://docs.astral.sh/uv/), then create the venv with Python 3.12 inside the lab folder:
   ```
   set BEASTCRAFT_ARTLAB=%LOCALAPPDATA%\BeastCraftArtLab
   uv venv --python 3.12 %BEASTCRAFT_ARTLAB%\venv
   ```
2. Install torch for XPU first, then the rest (pinned in `requirements.txt`):
   ```
   uv pip install --python %BEASTCRAFT_ARTLAB%\venv --index-url https://download.pytorch.org/whl/xpu torch==2.14.0+xpu torchvision==0.29.0+xpu
   uv pip install --python %BEASTCRAFT_ARTLAB%\venv -r Tooling/ArtLab/requirements.txt
   ```
3. Download the models below into `%BEASTCRAFT_ARTLAB%\models\<org>\<name>` (for example with
   `huggingface-cli download <repo> --local-dir ...`), with network access for this step only. Read each model card's
   licence first; keep a copy of each card in `%BEASTCRAFT_ARTLAB%\cards\`.
4. Run the scripts from `Tooling/ArtLab/scripts` with the venv's Python.

**Environment variables** (`common.py`; nothing is hard-coded to a user's folders):
| Variable | Default | What |
| --- | --- | --- |
| `BEASTCRAFT_ARTLAB` | `%LOCALAPPDATA%\BeastCraftArtLab` (else `~/.cache/BeastCraftArtLab`) | models, the HF cache, the venv |
| `ARTLAB_WORK` | `<lab>/work` | intermediates: layouts, candidates, lock seeds, masks, logs |
| `ARTLAB_OUT` | `<lab>/out` | `<beast>_final.png`, `rig/<beast>/`, review sheets |
| `ARTLAB_REFS` | `Tooling/ArtLab/refs` | the style references |
| `PY` (chain scripts) | the lab venv's Python | the interpreter |

Models, the venv and outputs never go into the repo.

## Models (tools only; none ships with the game)
| Model (Hugging Face repo) | Files used | Licence | Role |
| --- | --- | --- | --- |
| `cagliostrolab/animagine-xl-4.0` | the diffusers folders (unet, text encoders, tokenizers, scheduler) | **CreativeML Open RAIL++-M** (dated July 26, 2023; the card adopts Stability AI's SDXL licence "without any modifications or additional restrictions") | the base SDXL model: txt2img, img2img, inpaint |
| `madebyollin/sdxl-vae-fp16-fix` | the VAE | **MIT** | a half-precision-safe SDXL VAE (fewer NaN/black images) |
| `xinsir/controlnet-canny-sdxl-1.0` | `diffusion_pytorch_model.safetensors`, `config.json` | **Apache-2.0** | canny ControlNet: layout edges in exploration, the pick's own lines in the lock |
| `xinsir/controlnet-tile-sdxl-1.0` | `diffusion_pytorch_model.safetensors`, `config.json` | **Apache-2.0** | tile ControlNet in the detail pass |
| `h94/IP-Adapter` | `sdxl_models/ip-adapter_sdxl_vit-h.safetensors` | **Apache-2.0** | InstantStyle (the adapter on the style block only), from our own style references |
| `laion/CLIP-ViT-H-14-laion2B-s32B-b79K` (OpenCLIP ViT-H/14, LAION-2B) | the image encoder, downloaded as h94/IP-Adapter's `models/image_encoder/` | **MIT** (model card `license: mit`) | encodes the style references for the IP-Adapter |
| `facebook/sam2.1-hiera-small` | the transformers `model.safetensors` | **Apache-2.0** | character masks and rig-part masks |

About 15 GB on disk in total. **What the RAIL++-M licence means for us:** it places use restrictions on the *model*
(Attachment A: no unlawful use, no harming minors, no harassment, and so on) and redistribution conditions if we
ever shipped the model, which we do not. On outputs it says, in Section III, paragraph 6 ("The Output You
Generate"): *"Except as set forth herein, Licensor claims no rights in the Output You generate using the Model. You
are accountable for the Output you generate and its subsequent uses. No use of the output can contravene any
provision as stated in the License."* The same notices are in `THIRD-PARTY-NOTICES.md`.

**Caveats (not licence blockers):** Animagine XL 4.0 was fine-tuned on about 8.4M anime images "from various
sources" (its card), so it knows existing characters by tag. The prompts avoid artist, character and franchise tags,
negate the obvious lookalikes (`pokemon, moltres, ho-oh, ...`), and every candidate gets a human IP-likeness check
before it can be picked. Pure AI output may not be copyrightable in some jurisdictions; the producer's selection and
direction and the scripted edits are recorded in the provenance files. Get legal advice before relying on either.

## The house style (exact settings)
**Common to every beast** (`beasts.py`, `common.py`):
- Animagine XL 4.0 with the fp16-fix VAE, bf16, 896x1152, DPM++ 2M Karras, **26 steps, CFG 5**, CPU generator seeds.
- **Prompt:** `no humans, chibi, majestic, proud, mythical creature, {SUBJECT}, heroic pose, dynamic, elegant, bold
  clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, angry, violet,
  purple, cyan, magenta, blue, [fire, flames: non-fire beasts], {IP_NEG}, lowres, bad anatomy, extra legs, text,
  worst quality, low quality, blurry, 3d`
- **InstantStyle:** the IP-Adapter on `up.block_0 = [0, s, 0]` only, with the weighted mean embedding of
  `refs/d_406` 0.5, `a_104` 0.3, `b_208` 0.2.
- **Colour lock:** the init is the pick quantised to the beast's swatch (`SWATCH` in `beasts.py`), softened; after
  generation, Reinhard toward the swatch block (chroma 0.7, lightness 0.3, with an a\* clamp).
- **Lines:** the "bold" preset in warm plum `#3b1c26`: outer 5.5 (at the 896-wide reference, so 6-16 px at 2x),
  outer-var 0.4, taper 0.5, inner strength 0.8, inner-lo 0.05, inner-hi 0.16, inner kernel 7, ink-hi 0.3.
- **QA gate:** cool drift at most 2% and palette distance at most about 10; the lock's mask IoU at least 0.85.

**Per beast:**
| | Phoenix (Fire, Fierce) | Kirin (Light, Mystic) | Golem (Earth, Cute) |
| --- | --- | --- | --- |
| Subject | `phoenix, bird, pointed beak, swept flame crest, curved neck, raised flame wings, long plume tail, cream and orange feathers, bird legs` | `qilin, deer, single curved golden horn, short cream mane, amber scales on back, amber eyes, serene, cream fur, dark hooves` | `rock golem, quadruped, mossy hill back, standing stones, wildflowers, thick stone legs, round face, big amber eyes, curious, grey stone` |
| IP negatives | `pokemon, moltres, ho-oh, torchic, charmander, fanart` | `pokemon, rapidash, xerneas, unicorn, horse, wolf` | `pokemon, torterra, turtwig, tortoise, sprout, wolf` |
| Design source | round-2 txt2img `d_406` | exploration #9 (layout 9, seed 409) | exploration #10 (layout 10, seed 410) |
| Lock | IP 0.6, canny 0.55 (end 0.7), strength 0.80 | IP 0.45, the same | IP 0.45, the same, pick's eyes in the init |
| Lock pick | seed 55 | seed 22 | seed 11, then the pick's eyes restored |
| Detail pass | 0.65 / tile 0.3 / end 0.6 / seed 7 | the same | body 0.55 / tile 0.4 / end 0.6 / seed 8 (face masked out); face at 1x 0.25 / tile 0.6 / end 0.8 / seed 8; prompt `majestic` -> `cute` |
| Finish | bold, tighten on | bold, `--no-tighten` | bold, `--no-tighten --peel-paper --ground-green 900 --points "426,450;500,430;625,440;697,462"` |
| In-game | `--line-px 6`, WorldHeight 1.25 | `--line-px 6`, WorldHeight 1.15 | `--line-px 6`, WorldHeight 1.0 |

## Per-asset workflow
Run from `Tooling/ArtLab/scripts`; `$W` is `ARTLAB_WORK`, `$O` is `ARTLAB_OUT`.

1. **Candidate sheet.** Draw colour-mass layouts and generate candidates, then build the sheet:
   ```
   python layouts.py golem 16
   python generate.py explore_i2i golem 0 1 2 3 4 5 6 7 8 9 10 11 12 13     # IP 0.4, img2img 0.80, canny 0.4 (end 0.6), seeds 400+i
   python sheets.py explore golem PICK "why" "rejects"                     # after the pick, to record it
   ```
   The candidates must pass the IP-likeness check by eye (no franchise lookalikes); note rejects on the sheet.
2. **Producer pick + notes.** The art director picks a candidate and a personality word (Fierce, Cute, Mystic, ...)
   and says what to keep or change. Record it in `provenance/<beast>.md` before going on. A personality word can
   change the subject (the golem's `determined` -> `curious`, `round face, big amber eyes`); re-run `tokcheck.py`.
3. **Lock.**
   ```
   python generate.py prep golem $W/explore/golem_10.png
   python face.py init golem $W/explore/golem_10.png                        # only if the blur erases small features
   python generate.py lock golem --ipa 0.35 --tag ab0.35 --out $W/ab 11 22  # A/B 2-3 InstantStyle scales on 2 seeds first
   python generate.py lock golem --ipa 0.45 --cn 0.55 11 22 33 44 55 66     # then the 6-seed lock
   python sheets.py seeds golem $W/seeds lock 11                            # consistency: IoU >= 0.85, drift <= 2%
   python face.py restore golem $W/explore/golem_10.png $W/seeds/lock_golem_11.png $W/golem_lock_eyes.png
   ```
   Pick a seed (with the producer), then restore signature details from the pick if the lock lost them.
4. **Finish.** `sh finish_chain.sh phoenix $W/seeds/lock_phoenix_55.png` (kirin: add `--no-tighten`), or for the
   golem `python face.py masks golem LOCK.png` then `sh golem_chain.sh $W/golem_lock_eyes.png`. Look at the result at
   100%, check drift and palette distance, and get the producer's approval of the final.
5. **Parts.** Write the beast's SAM points and clip polygons (`PARTS`/`CLIPS` in `rigparts.py`, or
   `parts/<beast>.json`), then `python rigparts.py golem` and `python parts_sheet.py golem`. Check
   `parts_overlay.png` and the pose test. The cuts are machine-made: an artist pass on the joints is advised before
   animation.
6. **In-game export.** Archive the rig, export the sprite, list it, rebuild the manifest (from the repo root):
   ```
   python Tooling/ArtLab/scripts/archive_rig.py $O/rig/golem content/art/source/golem
   python Tooling/ArtLab/scripts/export_ingame.py content/art/source/golem/character.png content/art/beasts/golem/golem.png --line-px 6 --preview $O/preview
   ```
   Add or update the entry in `Tooling/PixelArt/illustrated.json` (the printed pivot, a WorldHeight), run
   `python Tooling/PixelArt/build.py`, point the species' `ArtKey` in `content/data/Creatures/beast-roster.json` at
   `beast/<id>/illustrated`, and check it in the viewer (`BeastCraft.Desktop --screenshot`).

## In-game export
- **Size.** The viewer's closest zoom is 5.5 canvas px per board px (`CameraSettings.MaxScale`), and one world unit is
  32 board px, so a unit is at most 176 px on the 1080-wide canvas. The tallest beast is 1.25 units, about **220 px**
  on a 1080p phone at full zoom (about 290 px on a 1440-wide screen). Twice that is about 440-580 px, so the sprites
  are **504 px on their longer side in a 512x512 frame** (the golem is wide, so it is 504 wide and 385 tall; at
  WorldHeight 1.0 it still has about 2x its largest on-screen size).
- **Filter and alpha.** Straight-alpha PNG, downscaled with Lanczos in premultiplied space; the manifest says
  `Filter: "linear"`, `Premultiplied: false`, and the viewer premultiplies on load and builds a mip chain, so the art
  stays clean at fit-all (about 70-140 px tall).
- **Lines.** At fit-all the master's own outline (about 3 px at 504) thins out, so the in-game copy gets a
  contour-only line pass, `--line-px 6` (the masters are unchanged).
- **Frame.** 4 px transparent padding and a power-of-two square frame (mipmaps on GLES 2; equal turn-order portraits).
  The feet pivot is (256, 508).
- **Size in game** is `WorldHeight` in `Tooling/PixelArt/illustrated.json` (world units from the feet to the top of
  the art); see `Tooling/PixelArt/README.md`.

## Known issues
- **XPU NaN/black images.** About 20-30% of calls on the Arc iGPU come back NaN, which decodes black. Every generator
  retries the same seed in-process (4 tries; detail tiles 5; inpaints 6). If an output stays black, delete it with
  `python rmblack.py DIR` and rerun in a fresh process (existing outputs are skipped). Batch > 1, StyleAligned and
  reference-only attention NaN'd every time on this GPU and are not used.
- **The 77-token prompt limit.** The SDXL pipeline silently truncates prompts past CLIP's 77 tokens (the first
  round-2 draft lost all its style and quality tags this way). `python tokcheck.py` checks every house prompt;
  pass extra prompts as `name="..."`. The golem's final prompt is exactly 77.
- **The `dtype=` gotcha.** diffusers 0.40 takes `dtype=`; the deprecated `torch_dtype=` is ignored without an error
  and loads fp32 (twice the memory, much slower). `load_pipe` asserts the UNet's dtype.
- **Scheduler lookup on XPU.** `StableDiffusionXLControlNetPipeline` intermittently failed with "Storage size
  calculation overflowed" in `(timesteps == t).nonzero()`; `load_pipe` does that lookup on the CPU.
- **Style bleed.** The style references are all fire birds: without `fire, flames` in the negatives, non-fire beasts
  grow flames, and plain txt2img never drew a hill-creature (hence the layouts).
- **Word traps.** `glowing` painted sun discs behind the golem; `storybook` painted open books; `slightly chibi` gave
  spindly realism.
- **Machine-cut parts.** Straight polygon cuts at the leg and neck joints, and SDXL-inpainted fill under the front
  parts (palette-guarded). Fine for small moves; an artist pass is advised before real animation.
