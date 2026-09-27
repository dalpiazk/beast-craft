# Provenance: Giant (boss, Hex7; personality **Cross, sleepy-grumpy**), Verdant Hollow variant

**Status:** approved by the producer (art director) as the Giant's Verdant Hollow (r01) art, 2026-09-27; the first
of its region variants. AI-assisted; disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/enemies/giant/giant_hollow.png` (manifest `enemy_giant_hollow`, ArtKey `enemy/giant/hollow`) |
| Accent overlay | `content/art/enemies/giant/giant_hollow_accent.png` (manifest `enemy_giant_hollow_accent`) |
| Master character, rig parts | `content/art/source/enemies/giant/hollow/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/enemies/giant/hollow/design_pick.png` (the lock's pick, after the fixes below) |
| Accent mask | `content/art/source/enemies/giant/hollow/accent_mask.png` |

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
- **Chosen:** Giant sheet (`giant_hollow_candidates.png`) **#11 = `giant_t11`**: txt2img (no layout), seed 11, IP 0.45 (trio finals), 20 steps.
- **Candidate prompt:** `no humans, solo, chibi, fantasy creature, cross, sleepy grumpy, ancient, huge wide forest giant beast, mossy stone and root body, four thick legs, crystal outcrops, small face, violet grey haze wisps, dim glowing eyes, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Candidate negative:** `flat colors, vector art, sticker, glossy, pastel, plush, round blob, headless, red, blue, cyan, yellow, pokemon, torterra, golurk, gogoat, ent, tree face, colossus, totoro, human, gore, scary, fire, multiple views, dark background, ...`

## 2. Producer decisions
1. **Giant = sheet #11 = `giant_t11`** (the big white shaggy fur creature). The producer's pick was first misread as layout L11 (sheet #3); that final is archived (`archive_L11/`) and **not used**; the producer corrected it to #11.
2. Notes (verbatim): "Add some greens that fade to blacks or purples to bring in a bit more nature." It must read as a Verdant Hollow elder, not a snow yeti; readable limbs with 1 face, 2 eyes and 1 mouth; the eyes keep the magenta only if it reads as Gloam glow, otherwise a dim hazy violet.
3. Round 2: "Lose the purple horns on his head": the crown crystals (the first accent) were removed; the crystal version (`work/giant_final_crystals.png`) is **not used**.
4. Size: about 2.5 hexes wide on its Hex7 flower (the lineup).

## 3. Fixes
- `giantfix2.py` (before the lock): scaled to 0.86 and re-centred (the candidate was cut off at the right); moss and leaf greens worked into the white fur (noise patches plus a vertical gradient), the fur tips pale lichen; shadows and the base fading to black-green 22301f and Gloam purple 4a3a5a; the off-palette blue rim to dusk purple; a lighter face; the magenta eyes pulled to a dim hazy violet (c496e8), protected through the lock.
- `pawfix.py` (after the finish): toe splits and claws on the three visible feet and an ink split between the front paws.
- `nocrystal.py` (round 2, on the final): the crown span rebuilt (paper above a new crown line, moss fur re-grown below by Telea, a horizontal smooth and fur grain), the crown outline re-inked as one shaggy line; the face, eyes, mouth, colours and limbs unchanged. The rig was re-cut with the head clip lowered.

## 4. Lock and finish
- **Lock:** `lock2.py giant pick 0.60 0.45 0.55`, seeds 11-66, 26 steps. **Final seed 22** (not 55, the lowest palette distance: seed 55's face lost its mouth line and read smiling). Consistency: IoU 0.984, colour delta 0.89, palette distance 0.46.
- **Lock prompt (subject):** `cross, sleepy grumpy, ancient, huge shaggy mossy fur beast, lichen, crystal shards in the crown, four sturdy legs, green to purple shadows, violet haze`; IP negatives `pokemon, yeti, snow, white fur, snorlax, totoro, human, cat, tree`.
- **Swatch:** b0be82 2, 6f8a4e 3, 9fbf6a 2, 4e6440 2, 22301f 2, 4a3a5a 2, b8a8d8 1, c496e8 1
- **Finish:** face-protected detail pass (face ellipse (302,409), 170x90), then the bold plum lines.
- **Anatomy check** (`giant_anatomy_check.png` in the finals folder): 1 face (3/4, looking up), 2 dim violet eyes, 1 mouth (cross frown), 4 limbs (2 front paws, the near hind foot, the far hind leg in shadow); a seated quadruped, no arms.
- **IP check:** Not a Snorlax or Totoro (no belly patch or ears), and no longer a yeti read (green moss fur).

## 5. Rig and in-game
- **Rig:** `hind_foot`, `body` (root), `front_paws`, `head` (mostly hand polygons; `giant_parts.json`).
- **In-game:** `export_ingame.py --line-px 6 --accent-mask` (512x512, feet pivot (256, 508)); `WorldHeight` **2.37**
  (the lineup rule: `docs/design/presentation-and-vfx.md`, "Enemy sizes").
- **Element accent:** the mossy/lichen green fur patches (saturated green fur inside the body, the face excluded), from `nocrystal.py`; about 11.5% of the frame. Its own colour (`AccentNative`, measured from the region) is **#c6d294**; drawn in it,
  the base plus the overlay matches the approved art to **0/255** per channel (offline 8-bit check over paper).

## 6. Look at
- The front paws are short stubs under the fur mass (they read through their claws); the far hind leg reads mainly as a dusk-purple fold.
- The accent is patchy moss, so a non-Nature tint shows as patches over the fur.

**Usage:** approved game art (Verdant Hollow variant); the provenance above is the AI-disclosure record.
