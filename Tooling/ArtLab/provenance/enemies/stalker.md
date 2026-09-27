# Provenance: Stalker (fast melee hunter; personality **Sneaky**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Stalker's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/stalker/stalker_hollow.png` (manifest `enemy_stalker_hollow`, ArtKey `enemy/stalker/hollow`) |
| Accent overlay | `content/art/enemies/stalker/stalker_hollow_accent.png` (manifest `enemy_stalker_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/stalker/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/stalker/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/stalker/hollow/accent_mask.png` |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../../README.md`. Scripts: `../../scripts/enemies/` (as run; paths from `ARTLAB_OUT` = this enemy's folder,
`ARTLAB_ENEMIES` = the folder of all nine, `ARTLAB_FINALS` = the trio finals) and the shared chain in
`../../scripts/seven/` (`gen4.py`, `lock2.py`, `chain.sh`, `finish.py`, `rigparts.py`). The candidate-sheet scripts
(`cand.py`, `layouts_en.py`, `sheet5.py`) are not in the repo; their settings are recorded below and per candidate in the
candidate log.

## 1. Candidate
- **Common rules (all nine):** themed to r01 Verdant Hollow (mossy woodland, old stones, fireflies); the Gloam marker
  on every enemy (`violet grey haze wisps, dim glowing eyes` in the prompt, violet-grey wisps and dim pale eyes in the
  layouts; the Gloam is not evil and not the Dark element, so `gore, scary` are negatives); one element-accent region
  per type painted in the accent swatch (8fb85a, Nature green) so the game can recolour it; every shown candidate
  counted for faces, eyes, mouths and limbs; a human IP-likeness review. 12 layout img2img (0.84, seeds 500+i) plus 4
  text (seeds 11-14) per sheet, 20 steps; finish: paperise, swatch lock, bold plum lines.
- **Chosen:** Brute sheet (`brute_hollow_candidates.png`) **#10 = `brute_L3`**: colour-mass layout 3 img2img at 0.84, seed 503, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, stubborn, scowl, feral, mossy horned forest beast, thick bark-like hide, heavy quadruped, big curved horns, lichen, ferns, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, red, blue, cyan, yellow, pokemon, tauros, minotaur, monster hunter, tree face, gore, scary, fire, multiple views, dark background, ...` (the full string per candidate is in the candidate log, `ai-art-enemies/work/raw/log.json`)

## 2. Producer decisions
1. **Stalker = `brute_L3`** (Brute sheet #10, lanky and deer-like with antlers, which read less as a brute and suit a lean hunter).
2. The far hind leg is to be added (the candidate showed 3 legs).

## 3. Fixes
- `legfix.py` (before the lock): a far hind leg (darker, far side, with a paw) painted between the near hind and near front legs, so the count reads 4.
- `leg4.py` (after the lock, on the 2x final): the lock rendered that leg ghost-pale, so it was repainted as a solid darker leg with a paw and plum outline, behind the belly line.

## 4. Lock and finish
- **Lock:** `lock2.py stalker pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 11** (the lowest palette distance). Consistency: IoU 0.978 (min 0.971), colour delta 1.91, palette distance 1.67.
- **Lock prompt (subject):** `sneaky, sly, narrowed eyes, lean hunter, lean mossy forest hunter beast, four slender legs, tall antlers, bushy tail, pale fur, violet haze`; IP negatives `pokemon, wolf, fox, deer, monster hunter, gore`.
- **Swatch:** 7a6450 3, 4e3e32 2, a8927a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the bold plum lines.
- **Anatomy check** (`stalker_anatomy_check.png` in the finals folder): 1 face (sly, narrowed eyes), 2 dim glowing eyes, 1 mouth (small smirk), 4 legs (2 near, the far hind darker between), 1 bushy mossy tail, 2 antlers.
- **IP check:** No Absol or Umbreon, no Sawsbuck or Xerneas, no Monster Hunter Kirin: a mossy lean hunter with a bushy tail, pale face and antlers.

## 5. Rig and in-game
- **Rig:** `legs`, `body`, `head` (antlers in the head; `stalker_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **1.03**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the tall antlers. Its own colour (`AccentNative`, measured from the region) is **#d7a685**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- **The far hind leg is thin and flat-painted** beside the rendered legs: an artist touch-up is advised.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
