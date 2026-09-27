# Provenance: Caster (ranged special; personality **Spiteful**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Caster's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/caster/caster_hollow.png` (manifest `enemy_caster_hollow`, ArtKey `enemy/caster/hollow`) |
| Accent overlay | `content/art/enemies/caster/caster_hollow_accent.png` (manifest `enemy_caster_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/caster/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/caster/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/caster/hollow/accent_mask.png` |

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
- **Chosen:** Brute sheet (`brute_hollow_candidates.png`) **#6 = `brute_L8`**: colour-mass layout 8 img2img at 0.84, seed 508, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, stubborn, scowl, feral, mossy horned forest beast, thick bark-like hide, heavy quadruped, big curved horns, lichen, ferns, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, red, blue, cyan, yellow, pokemon, tauros, minotaur, monster hunter, tree face, gore, scary, fire, multiple views, dark background, ...` (the full string per candidate is in the candidate log, `ai-art-enemies/work/raw/log.json`)

## 2. Producer decisions
1. **Caster = `brute_L8`**: a **two-legged antlered beast caster**, casting from **between its antlers**, as in the candidate.
2. Producer correction: it has only 2 legs, and that is fine: keep it as drawn, add no legs; no arms (the armed-folk rule does not apply to a beast caster).
3. The spell glow between the antlers is the element accent. The lore changes to match (no longer a hedge-mage; this PR).

## 3. Fixes
- `orbfix.py` (before the lock): a glowing spell orb (accent 8fb85a, a bright core, a soft halo and faint spark threads to the antler tips) painted between the antlers at 1x (500,420), where the candidate had no clear glow.

## 4. Lock and finish
- **Lock:** `lock2.py caster pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 33** (the lowest palette distance). Consistency: IoU 0.952 (min 0.931), colour delta 2.31, palette distance 1.7.
- **Lock prompt (subject):** `spiteful, smug, narrowed eyes, round mossy beast, two short legs, tall wispy antlers, glowing spell orb between the antlers, violet haze`; IP negatives `pokemon, human, rabbit, hamster, gore`.
- **Swatch:** 7a6450 3, 4e3e32 2, a8927a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the bold plum lines.
- **Anatomy check** (`caster_anatomy_check.png` in the finals folder): 1 face (spiteful, smug), 2 dim half-lidded eyes, 1 mouth (smirk), 2 legs (as drawn), no arms, 2 antlers, the orb between them.
- **IP check:** No Pokemon (Shaymin, Sewaddle, Bunnelby), no Moogle, no Monster Hunter read: a round moss beast with tall feather-like antlers.

## 5. Rig and in-game
- **Rig:** `feet`, `body`, `antlers` (the orb is in the antler part; `caster_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **1.25**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the spell orb; the saved mask covered only the orb's inner ring, so it was widened to the whole floating orb (`accent_mask_master.py --whole-islands`). Its own colour (`AccentNative`, measured from the region) is **#ceee90**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- The lock left the orb an open ring with a small teardrop tail and a flat bottom edge.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
