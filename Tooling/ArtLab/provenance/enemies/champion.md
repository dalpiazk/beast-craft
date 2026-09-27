# Provenance: Champion (mini-boss, Triangle; personality **Swaggering**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Champion's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/champion/champion_hollow.png` (manifest `enemy_champion_hollow`, ArtKey `enemy/champion/hollow`) |
| Accent overlay | `content/art/enemies/champion/champion_hollow_accent.png` (manifest `enemy_champion_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/champion/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/champion/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/champion/hollow/accent_mask.png` |

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
- **Chosen:** Brute sheet (`brute_hollow_candidates.png`) **#9 = `brute_L1`**: colour-mass layout 1 img2img at 0.84, seed 501, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, stubborn, scowl, feral, mossy horned forest beast, thick bark-like hide, heavy quadruped, big curved horns, lichen, ferns, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, red, blue, cyan, yellow, pokemon, tauros, minotaur, monster hunter, tree face, gore, scary, fire, multiple views, dark background, ...` (the full string per candidate is in the candidate log, `ai-art-enemies/work/raw/log.json`)

## 2. Producer decisions
1. **Champion = `brute_L1`** (a Brute-sheet favourite assigned to the Champion role). The finals' notes call it "Brute sheet #1"; on the published sheet `brute_L1` is #9 (#1 is `brute_L7`).
2. Scaled for its three hexes: about 1.75 hexes wide on its triangle (the lineup).
3. **Its two tails are intended** (confirmed by the producer); the forked violet tail reads as the Gloam.

## 3. Fixes
- `scalefix.py` (before the lock): the candidate scaled 1.22x about its feet so it fills its three hexes and reads bigger and swaggering next to the one-hex units.

## 4. Lock and finish
- **Lock:** `lock2.py champion pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 22** (the lowest palette distance). Consistency: IoU 0.974 (min 0.968), colour delta 0.93, palette distance 1.55.
- **Lock prompt (subject):** `swaggering, cocky, big, big mossy horned beast leader, heavy body, moss crown, curled horns, sturdy legs, violet haze`; IP negatives `pokemon, sheep, human, gore`.
- **Swatch:** 7a6450 3, 4e3e32 2, a8927a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the bold plum lines.
- **Anatomy check** (`champion_anatomy_check.png` in the finals folder): 1 face (pale mask, smug half-lids), 2 dim red slit eyes, no mouth drawn (dotted muzzle), 4 legs (3 feet visible, the far hind hidden), 2 curled ram horns, a forked gloam-violet tail.
- **IP check:** No Tauros or Mareep, no Monster Hunter Anjanath: a round moss-crowned ram brute with a pale mask face and a violet gloam tail.

## 5. Rig and in-game
- **Rig:** `body`, `head` (hand polygons; `champion_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **1.7**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the moss crown and mantle. Its own colour (`AccentNative`, measured from the region) is **#c9df68**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- Only 3 feet are visible (the far hind is hidden), fine for the 3/4 stance.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
