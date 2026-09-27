# Provenance: Archer (ranged physical; personality **Watchful**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Archer's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/archer/archer_hollow.png` (manifest `enemy_archer_hollow`, ArtKey `enemy/archer/hollow`) |
| Accent overlay | `content/art/enemies/archer/archer_hollow_accent.png` (manifest `enemy_archer_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/archer/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/archer/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/archer/hollow/accent_mask.png` |

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
- **Chosen:** Archer sheet (`archer_hollow_candidates.png`) **#1 = `archer_L10`**: enemy folk layout 10 (2 arms and hands, bow, hip quiver) img2img at 0.84, seed 510, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, watchful, alert, masked, small furry forest folk, bark mask, leaf hood, two arms, hands gripping a twig bow, quiver, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, plush, round blob, headless, red, blue, cyan, yellow, human, elf, bokoblin, korok, decidueye, pokemon, beard, gore, scary, fire, ...`

## 2. Producer decisions
1. **Archer = sheet #1 (`archer_L10`)**: the carved-bark mask under a leaf hood.
2. Brief: ONE intact curved twig bow with a taut string; the bow hand grips it and the draw hand is on the string; the fletching is the element accent. (No candidate painted a strung, curved bow.)
3. The armed-folk rule: wild folk show visible arms and hands holding the weapon.

## 3. Fixes
- `bowfix.py` (before the lock): the straight stick removed (Telea); one curved twig bow painted (grip (318,800), tips (338,630) and (338,970) at 1x) with a bark highlight and plum outline; a taut string from both tips to the draw hand; a nocked arrow with fletching in the accent colour 8fb85a; the near arm extended into a tan fist gripping the bow. The fix contours went into the canny and the painted colours were kept unblurred in the init (`fixguide.py`).

## 4. Lock and finish
- **Lock:** `lock2.py archer pick 0.60 0.45 0.55`, seeds 11-66, 26 steps. **Pick: seed 11.** Consistency: IoU 0.961 (min 0.939), colour delta 2.68, palette distance 2.40; the cool-hue drift (up to 3.4%) is the violet Gloam wisp, as intended.
- **Lock prompt (subject):** `watchful, alert, masked, small furry forest folk, carved bark mask, leaf hood, two arms, curved twig bow, arrow, quiver, violet haze`; IP negatives `human, elf, bokoblin, korok, decidueye, pokemon, beard, gore`.
- **Swatch:** 7a6a52 3, 4e4234 2, a8967a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass (face (440,500), 110x95), then the bold plum lines.
- **Anatomy check** (`archer_anatomy_check.png` in the finals folder): 1 face (carved bark mask), 2 dim glow-slit eyes, the mouth hidden under the mask, 2 arms and 2 hands, 2 legs, 1 intact strung bow.
- **IP check:** Not a Bokoblin, Moblin, Link, Legolas or Robin Hood; no Decidueye or Rowlet; not a Korok (a carved mask, not a leaf face).

## 5. Rig and in-game
- **Rig:** `legs`, `body`, `head`, `bow` (with the fist; hand-set pivots; `archer_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **0.94**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the arrow fletching (as painted before the lock). Its own colour (`AccentNative`, measured from the region) is **#fcda98**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- The lock re-grew part of the candidate's arrow through the hands; it reads as a long nocked arrow.
- **The bow string is thin**: it reads at full size, faintly at board size.
- The accent mask marks the fletching as painted before the lock, so the element tint covers only a few pixels and barely reads in game: re-cut it on the final, or pick a larger accent.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
