# Provenance: Shaman (area special; personality **Gnarled elder**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Shaman's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/shaman/shaman_hollow.png` (manifest `enemy_shaman_hollow`, ArtKey `enemy/shaman/hollow`) |
| Accent overlay | `content/art/enemies/shaman/shaman_hollow_accent.png` (manifest `enemy_shaman_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/shaman/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/shaman/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/shaman/hollow/accent_mask.png` |

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
- **Chosen:** Shaman sheet (`shaman_hollow_candidates.png`) **#11 = `shaman_L1`**: enemy colour-mass folk layout 1 img2img at 0.84, seed 501, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, old, gnarled, stern, small elder forest folk, mossy beard, hunched, gnarled root staff with mushrooms, bark cloak, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, plush, round blob, headless, red, blue, cyan, yellow, human, wizard, native, moogle, pokemon, yoda, gore, scary, fire, multiple views, dark background, lowres, bad anatomy, extra eyes, text`

## 2. Producer decisions
1. **Shaman = #11 (`shaman_L1`)**, chosen because "it's the only one with arms".
2. The candidate's staff floated in pieces: to be fixed. This pick set the armed-folk rule: wild folk (Archer, Shaman) must show arms and hands holding the weapon or staff.

## 3. Fixes
- `staffix.py` (before the lock): the broken zig-zag staff removed to paper; rebuilt as **one continuous gnarled root staff** just left of the body (clear of the face) with bark knots and a highlight; the near hand extended into a fist wrapping the staff, with finger creases; the top a knotted root fork plus a mushroom cap, the element accent (8fb85a). The candidate's own two arms are kept (the far one rests across the belly).

## 4. Lock and finish
- **Lock:** `lock2.py shaman pick 0.60 0.45 0.55`, seeds 11-66, 26 steps. **Pick: seed 33.** Consistency: IoU 0.960 (min 0.955), colour delta 3.48, palette distance 1.33, cool drift 0/6.
- **Lock prompt (subject):** the candidate prompt (the Verdant Hollow definition reads `gnarled root staff with mushrooms`).
- **Swatch:** 766850 3, 4a4032 2, a4947a 2, 6f8a4e 2 (moss), 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass (face ellipse (380,600), 105x80; body 0.55 / tile 0.4, face 0.25 / tile 0.6, word `gnarled`), then the bold plum lines.
- **Anatomy check** (`shaman_anatomy_check.png` in the finals folder): 1 face, 2 gloam-glow eyes, the mouth hidden under the beard, 2 arms and 2 hands (the near one grips the staff), 2 legs, 1 continuous staff.
- **IP check:** Not a Moogle, Yoda, Trevenant or Phantump; the antlered moss spirit is folklore Leshy (public domain); none has the friendly Treant's bark face or leaf crown.

## 5. Rig and in-game
- **Rig:** `legs`, `body` (root), `head` (antlers and face), `staff` (a hand polygon with the fist; `shaman_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **1.32**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the mushroom cap on the staff. Its own colour (`AccentNative`, measured from the region) is **#d5e286**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- **A lower branch of the viewer-left antler landed in the `body` part.**
- The violet-grey haze wisps are faint on this pick (the lock softened them): the shared in-game Gloam haze adds them.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
