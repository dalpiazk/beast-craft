# Provenance: Swarmling (swarm, bite; personality **Skittery**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Swarmling's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/swarmling/swarmling_hollow.png` (manifest `enemy_swarmling_hollow`, ArtKey `enemy/swarmling/hollow`) |
| Accent overlay | `content/art/enemies/swarmling/swarmling_hollow_accent.png` (manifest `enemy_swarmling_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/swarmling/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/swarmling/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/swarmling/hollow/accent_mask.png` |

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
- **Chosen:** Brute sheet (`brute_hollow_candidates.png`) **#8 = `brute_L4`**: colour-mass layout 4 img2img at 0.84, seed 504, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, stubborn, scowl, feral, mossy horned forest beast, thick bark-like hide, heavy quadruped, big curved horns, lichen, ferns, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, red, blue, cyan, yellow, pokemon, tauros, minotaur, monster hunter, tree face, gore, scary, fire, multiple views, dark background, ...` (the full string per candidate is in the candidate log, `ai-art-enemies/work/raw/log.json`)

## 2. Producer decisions
1. **Swarmling = `brute_L4`** (a Brute-sheet favourite assigned to the Swarmling). The finals' notes call it "Brute sheet #4"; on the published sheet `brute_L4` is #8.
2. Drawn at about 0.55 of a one-hex unit (confirmed by the producer).

## 3. Fixes
- The line pass re-run heavy for its tiny board size: `finish.py --outer 14 --outer-var 0.2 --inner-strength 1.1 --inner-kernel 11` (the house default is outer 5.5). The in-game export keeps this heavier master line (the 6 px contour pass sits inside it).

## 4. Lock and finish
- **Lock:** `lock2.py swarmling pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 33** (the lowest palette distance). Consistency: IoU 0.969 (min 0.962), colour delta 2.35, palette distance 1.11.
- **Lock prompt (subject):** `skittery, cute creepy, glowing eyes, small round mossy mite beast, ram horns, fern tufts, short legs, glowing eyes, bold outline`; IP negatives `pokemon, sheep, slime, gore`.
- **Swatch:** 7a6450 3, 4e3e32 2, a8927a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the heavy plum lines above.
- **Anatomy check** (`swarmling_anatomy_check.png` in the finals folder): 1 face (skittery glare), 2 violet glow-slit eyes, no mouth drawn (tiny muzzle), 2 stubby front legs (hind hidden), 2 curled rust horns, a spiky crest on the back.
- **IP check:** No Pokemon (Oddish, Bulbasaur, Shroomish, Mareep), no Moogle: a round puff with curled rust horns and a spiky crest.

## 5. Rig and in-game
- **Rig:** `body`, `feet` (hand polygons; `swarmling_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **0.39**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the curled rust horns. Its own colour (`AccentNative`, measured from the region) is **#e98e64**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- The eyes carry the Gloam glow and still read at 64 px; the horns read as two small blobs at that size.
- The extra-heavy line is deliberate for a swarm unit; it looks heavy at full size.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
