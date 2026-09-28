# Provenance: the skill icons (Verdant Hollow, 2026-09-27)

The 91 painted skill icons in `content/art/icons/skills/` (76 beast skills, avatar actives and passives, as
`<id>.png`) and `content/art/icons/skills/enemy/` (15 enemy skills, as `<enemy>_<skill>.png`), plus the icon frame
and the five rarity rings in `content/art/ui/skill_icon/` (`frame.png`, `ring_{common,rare,epic,legendary,gloam}.png`).
All are 256x256 RGBA with straight alpha, transparent outside the circle. The layer sizes follow the slot spec in
`docs/art/hollow-art-slots.md`: ring 1.00, frame 0.90 and icon 0.76 of the icon box.

The scripts as run are in `scripts/icons/`. The record of the run is in `provenance/icons/`:
- `picks.json`: the chosen seed for each skill, with every candidate's readability score, the motif, and reroll and
  override flags and notes.
- `overrides.json`: the producer's manual seed choices.
- `log.json`: every generation's prompt, negative prompt, seed and settings.

## Producer decisions
- **Treatment B** ("bold": high contrast and glow, like a mobile RPG) was chosen at the style test (`icons.py` and
  `icon_sheet.py`). A (soft painterly) and C (flat plus texture) were not picked.
- **Frame 1** was chosen: a bronze frame with coloured rarity rings, drawn procedurally by `review.py frames` (no
  model).
- The producer reviewed one sheet per group (`review.py group`) and called for rerolls:
  - Firestorm: a fire tornado.
  - Flame Wave: a fire circle.
  - Granite Bulwark: a slab ring.
  - All of Lightning redone.
  - Thorn Lash: thorns.
  - Spore Cloud: a green cloud.
  - The others listed in `batch.REROLL` and `REROLL_B`: 34 skills in all.
- The producer stopped after round 3: "not aiming for perfect".
- Borderline icons, accepted as they are: Thorn Lash, Sunder and Great Cleave (see `docs/art/touch-ups.md`).

## Method
1. **Motif inits** (`briefs.py`, `motifs.py`): each skill has a one-line motif brief and a palette for its element
   or group. A parametric colour-mass primitive draws that single clear shape on a 1024 canvas, inside the
   inscribed circle. This is the img2img init.
2. **Generation** (`batch.py gen`, run by `run_all.sh`). Settings:
   - Model: Animagine XL 4.0 with the fp16-fix VAE, through `seven/common.py`'s pipeline.
   - Sampler: DPM++ 2M Karras, 20 steps, CFG 5, 1024x1024.
   - img2img strength 0.72.
   - IP-Adapter InstantStyle at 0.35 (style only), using the Phoenix, Golem and Kirin finals equally as
     references.
   - Prompt: `no humans, game skill icon, centered, <motif>, bold, high contrast, vivid colors, strong glow, rim
     light, dark background, painterly, masterpiece`.
   - Base negative prompt as logged, with per-skill extras (`XNEG`).
   - Seeds: two per skill (5000+i and 6000+i), and three for the style test's weak spots.
   - The process is recycled every two images, and NaN or black outputs are retried.
3. **Post-pass and cut** (`icon_sheet.icon256_img`), deterministic with no model:
   - Treatment B post-pass: saturation 1.2, S-curve contrast, a darker rim and bloom on the brights.
   - Then the circle cut with a soft edge, down to 256.
4. **Auto-pick** (`batch.py pick`): each seed is scored at 64 px as 0.6 × motif fidelity plus 0.4 × figure/ground
   contrast. Fidelity is the correlation of the icon's luminance with the layout's inside the circle. The best seed
   is kept unless `overrides.json` names one.
5. **Rerolls** (`reroll.py`, run by `run_reroll*.sh`), after each motif fix (`fix*.py`):
   - Round 2: seeds 8000/9000/10000+i for the `REROLL` list.
   - Round 3: seeds 11000/12000/13000+i for `REROLL_B`, where no seed of the first reroll showed the asked-for motif.
   - A final pass generated the seeds still missing.
   - The before/after sheets went to the producer.

`fix1.py`–`fix21.py` and `add_shell.py` are the in-place edit history of `motifs.py`, `briefs.py` and `batch.py`.
They are already applied: do not re-run them.

## Paths
The scripts take their paths from environment variables:
- `ARTLAB_HOLLOW`: the Hollow art work folder (the style test's `work/icons`).
- `ARTLAB_ICONS`: the icon batch folder, with `work/`, the outputs and the sheets. It defaults to
  `$ARTLAB_HOLLOW/icons`.
- `BEASTCRAFT_REPO`: defaults to this checkout. The skill ids and ArtKeys are read from
  `content/data/Skills/skill-library.json`.
- `BEASTCRAFT_ARTLAB`: the venv and models, as in the README.
- `ARTLAB_FINALS`: the style references.

`briefs.py` reads the enemy skills from `$ARTLAB_ICONS/work/enemy-library.branch.json`. That file is a copy of
this branch's `content/data/Encounters/enemy-library.json`, taken before the batch.
