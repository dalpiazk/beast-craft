# Provenance: Brute (melee tank; personality **Stubborn**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Brute's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/brute/brute_hollow.png` (manifest `enemy_brute_hollow`, ArtKey `enemy/brute/hollow`) |
| Accent overlay | `content/art/enemies/brute/brute_hollow_accent.png` (manifest `enemy_brute_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/brute/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/brute/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/brute/hollow/accent_mask.png` |

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
- **Chosen:** Brute sheet (`brute_hollow_candidates.png`) **#2 = `brute_L11`**: colour-mass layout 11 img2img at 0.84, seed 511, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, stubborn, scowl, feral, mossy horned forest beast, thick bark-like hide, heavy quadruped, big curved horns, lichen, ferns, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, red, blue, cyan, yellow, pokemon, tauros, minotaur, monster hunter, tree face, gore, scary, fire, multiple views, dark background, ...` (the full string per candidate is in the candidate log, `ai-art-enemies/work/raw/log.json`)

## 2. Producer decisions
1. **Brute = `brute_L11`** (Brute sheet #2: the most stubborn face, a deep scowl under ram horns, on a compact moss-headed tank).

## 3. Fixes
- None: the design is kept as picked.

## 4. Lock and finish
- **Lock:** `lock2.py brute pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 55** (the lowest palette distance). Consistency: IoU 0.984 (min 0.981), colour delta 1.89, palette distance 2.52.
- **Lock prompt (subject):** `stubborn, scowl, feral, mossy horned forest beast, thick hide, heavy quadruped, big curled ram horns, four legs, violet haze`; IP negatives `pokemon, tauros, minotaur, monster hunter, tree face, gore`.
- **Swatch:** 7a6450 3, 4e3e32 2, a8927a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the bold plum lines.
- **Anatomy check** (`brute_anatomy_check.png` in the finals folder): 1 face (stubborn scowl), 2 dim glowing eyes, 1 mouth (clenched), 4 legs (short paws under the body), 2 curled ram horns.
- **IP check:** No Tauros (no three tails), no Monster Hunter Anjanath or Bulldrome, no Mareep: a moss-crowned ram beast with a scowl.

## 5. Rig and in-game
- **Rig:** `body`, `head` (hand polygons; `brute_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **0.88**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the curled ram horns (brown in the art, so a Nature Brute keeps brown horns). Its own colour (`AccentNative`, measured from the region) is **#ddae77**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- A low, round tank: its legs are 4 short paws, so the silhouette reads mainly through the horns.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
