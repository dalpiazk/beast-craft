# Provenance: Stingling (swarm, sting; personality **Buzzy**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Stingling's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/stingling/stingling_hollow.png` (manifest `enemy_stingling_hollow`, ArtKey `enemy/stingling/hollow`) |
| Accent overlay | `content/art/enemies/stingling/stingling_hollow_accent.png` (manifest `enemy_stingling_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/stingling/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/stingling/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/stingling/hollow/accent_mask.png` |

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
- **Chosen:** Shaman sheet (`shaman_hollow_candidates.png`) **#6 = `shaman_t14`**: txt2img (no layout), seed 14, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, old, gnarled, stern, small elder forest folk, mossy beard, hunched, gnarled root staff with mushrooms, bark cloak, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, plush, round blob, headless, red, blue, cyan, yellow, human, wizard, native, moogle, pokemon, yoda, gore, scary, fire, multiple views, dark background, lowres, bad anatomy, extra eyes, text`

## 2. Producer decisions
1. **Stingling = `shaman_t14`** (a Shaman-sheet favourite, an owl-like folk figure), "without the stick that's under it". The finals' notes call it "Shaman sheet #14"; on the published sheet it is #6.
2. It had no wings or stinger, so a stinger with a glowing barb (the element accent) was added.
3. Drawn at about 0.55 of a one-hex unit (confirmed by the producer). The lore changes to match (no longer wasp-like; this PR).

## 3. Fixes
- `stingfix.py` (before the lock): the stick (both loops, the crossing under the body and its violet flower) removed to paper; 2 short legs with feet kept; a curved stinger tail added from the lower back.
- `barb2x.py` (after the lock, on the 2x final): the lock softened the barb into a dull knob, so it was repainted as a hooked green thorn with a bright core and a soft glow; the accent mask was re-saved from it.

## 4. Lock and finish
- **Lock:** `lock2.py stingling pick`, seeds 11-66, 26 steps, canny 0.55, IP 0.45. **Pick: seed 22** (the lowest palette distance). Consistency: IoU 0.979 (min 0.974), colour delta 1.93, palette distance 2.19.
- **Lock prompt (subject):** `buzzy, pesky, glowing eyes, fluffy forest pest creature, leafy tufts, glowing eyes, small stinger tail, two legs, violet haze`; IP negatives `pokemon, owl, human, wizard, staff, stick, gore`.
- **Swatch:** 766850 3, 4a4032 2, a4947a 2, 6f8a4e 2, 8fb85a 2 (accent), 8f86a0 2 (Gloam), e8e6a8 1 (glow)
- **Finish:** face-protected detail pass, then the bold plum lines.
- **Anatomy check** (`stingling_anatomy_check.png` in the finals folder): 1 face (buzzy glare), 2 amber glow eyes, the mouth hidden in the ruff, 2 short legs with feet, no arms, no stick, 1 stinger tail with a glowing barb.
- **IP check:** No Pokemon (Cottonee, Whimsicott, Swablu, Beedrill), no Kodama: a fluffy ruffed sprite with leafy ear crests and a barbed tail.

## 5. Rig and in-game
- **Rig:** `legs`, `body`, `head`, `stinger` (hand polygons; `stingling_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **0.73**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the glowing stinger barb. Its own colour (`AccentNative`, measured from the region) is **#b2da7c**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- At board size the barb is a small glint; the amber eyes carry the read. A bigger barb is a quick repaint.
- The barb is a clean hand-painted shape, flatter than the painterly fur.
- No visible wings; if "buzzy" must read as a flyer, the leafy ear crests could become wing leaves.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
