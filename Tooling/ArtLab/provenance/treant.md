# Provenance: Treant (Nature, personality **Gentle**)

**Status:** approved by the producer (art director) as the Treant's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/treant/treant.png` (manifest `beast_treant_illustrated`, ArtKey `beast/treant/illustrated`) |
| Master character, rig parts | `content/art/source/treant/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/treant/design_pick.png` (candidate #8, `treant_L7`, paperised and colour-locked, without its lines) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. The scripts are the adapted set described in `leviathan.md`.

## 1. Candidate sheet (remaining-seven round)
- **Generated:** 16 candidates: colour-mass layouts L0-L11 and text seeds 11-14, all 20 steps; 12 shown on
  `treant_candidates.png`. A quadruped "tree-on-back" layout was dropped before generation (a Torterra risk). Both
  methods gave upright chibi trunk-guardians with leaf crowns, root feet and branch arms.
- **Chosen candidate: #8 = `treant_L7`**: colour-mass layout 7, img2img at 0.84, IP 0.45 (finals refs), 20 steps,
  seed 507.
- **Prompt:** `no humans, solo, chibi, mythical creature, gentle, kind eyes, soft smile, protective, treant, walking
  tree creature, mossy bark body, leaf crown, root feet, branch arms, blossoms, green leaves, brown bark, heroic pose,
  bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple,
  cyan, blue, red, pokemon, torterra, trevenant, sudowoodo, groot, human face, fire, flames, multiple views, dark
  background, lowres, bad anatomy, text, worst quality, blurry`
- **Swatch:** 7a5a3e 3, 4e3a2a 1, 6f8f4a 2, 9fbf6a 2, c7d97a 1, f2b8b0 1, f2c96a 1.
- **IP check:** no shell (not Torterra), no humanoid bark-skinned adult (not Groot), no pale spiky face (not
  Trevenant); no round leaf "hands" (Sudowoodo).

## 2. Producer decision
Candidate **Treant #8** (L7), approved as is. Personality: **Gentle**.

## 3. Lock
- **Prep:** canny from its own lines, swatch block, soft init. The block's blossom-pink swatch had been matched onto
  the pale face and chest (21.5k px), which would have painted a pink torso; it was remapped to the amber accent
  f2c96a.
- `lock2.py treant pick 0.55 0.45 0.55`, seeds 11-66, 26 steps.
- **Consistency (6 seeds):** mask IoU mean 0.959 (min 0.949); colour ΔLab mean 1.51 (max 3.19); palette distance mean
  1.84 (max 2.11); cool drift 0/6.
- **Lock pick: seed 11** (the lowest palette distance).

## 4. Finish
- **Face mask:** (415,500), half-axes 75x55.
- **Detail:** body 0.55 / tile 0.4 at 2x; face 0.25 / tile 0.6. Detail word: `gentle`.
- **Lines:** bold plum.

## 5. Rig and in-game
- `rigparts.py treant` (spec `treant_parts.json`): `leg_left`, `leg_right`, `body` (root), `arm_right`, `arm_left`,
  `head` (leaf crown and face). The pose test holds.
- **Known limits:** the crown is a big asymmetric leaf mass (as in the candidate); the blossom accent is not present in
  this design. Machine-cut joints.
- **In-game:** `export_ingame.py --line-px 6` (content 407x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.4, the tallest beast on the board.

## Producer decision (final)
Approved as is (candidate #8, L7), personality **Gentle**, for in-game use with AI disclosure.
