# Provenance: Thunderbird (Lightning, personality **Wild, energetic**)

**Status:** approved by the producer (art director) as the Thunderbird's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/thunderbird/thunderbird.png` (manifest `beast_thunderbird_illustrated`, ArtKey `beast/thunderbird/illustrated`) |
| Master character, rig parts | `content/art/source/thunderbird/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/thunderbird/design_pick.png` (candidate #2, `thunderbird_L7`, paperised and colour-locked, without its lines) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Scripts: `../scripts/seven/` (as run for the seven finals; see its `common.py` for the
paths: one beast's folder in `ARTLAB_OUT`, the trio finals in `ARTLAB_FINALS`). Shared: `gen4.py` (prep),
`lock2.py`, `facemask.py`, `chain.sh` (`detail_pass.py`, `finish.py`), `offpal.py`, `rigparts.py` with
`thunderbird_parts.json`, `parts_sheet.py` (the XPU and recipe changes are in `leviathan.md`). The candidate-sheet
scripts (`cand.py`, `layouts5.py`, `sheet5.py`) are not in the repo; their settings are recorded below.

## 1. Candidate sheet (remaining-seven round)
- **Generated:** 16 candidates: colour-mass layouts L0-L11 and text seeds 11-14 (20 steps except L0/L1 at 26); 12
  shown on `thunderbird_candidates.png`. The layouts won by far (text painted storm scenes with a small bird inside).
- **Chosen candidate: #2 = `thunderbird_L7`**: colour-mass layout 7 (`layouts5.py thunderbird`, numpy seed 219), a
  V-wing layout with lightning streaks; img2img at strength 0.84 (no ControlNet), IP 0.45 with the finals refs,
  seed 507, 20 steps (approved for candidates), CFG 5. The speediest silhouette: a swept V with jagged yellow
  lightning through both wings and a forked tail.
- **Prompt:**
  ```
  no humans, solo, chibi, mythical creature, wild, energetic, excited grin, dynamic, thunderbird, storm bird, swept wings,
  crackling feathers, lightning streaks, slate grey feathers, yellow lightning accents, sleek, heroic pose, bold clean
  lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece
  ```
- **Negative:**
  ```
  flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, red, pink, green, magenta, pokemon,
  zapdos, articuno, staraptor, pikachu, spiky yellow body, fire, flames, multiple views, dark background, lowres,
  bad anatomy, text, worst quality, blurry
  ```
- **Swatch** (hex, weight): 3f4a63 3, 6f8fb8 2, a9bdd6 2, f8f4e8 2, ffd84a 2, f2a93a 1.
- **IP check:** no Zapdos yellow spiky body (slate/blue-grey with yellow accents only), no Staraptor crest mop.

## 2. Producer decision
Candidate **Thunderbird #2** (L7), approved as is. Personality: **Wild** (energetic): lightning-streaked V wings,
a diving silhouette.

## 3. Lock
- **Prep** (`gen4.py prep`): canny from its own lines, swatch block, soft init.
- `lock2.py thunderbird pick 0.55 0.45 0.55 ../work/seeds lock`, seeds 11-66: the leviathan-proven recipe for
  approved-as-is picks (pick init 0.55, canny 0.55 end 0.7, IP 0.45, 26 steps, CFG 5, then Reinhard).
- **Consistency (6 seeds):** mask IoU mean 0.975 (min 0.971); colour ΔLab mean 1.76 (max 3.28); palette distance
  mean 2.04 (max 2.41); off-palette 0.1-0.8% per seed. (The trio's cool drift reads ~48%: it counts the on-palette
  storm-blue body, so it is not used.)
- **Lock pick: seed 66** (palette distance 1.57, 0.28% off-palette). Seed 55 had a faint grey ground haze.

## 4. Finish
- **Face protection:** ellipse at 1x centre (400,600), half-axes 72x66 (eyes, beak, crest base).
- **Detail** (`chain.sh thunderbird wild`): body at 2x 0.55 / tile-CN 0.4 / end 0.6 / seed 8 (627 s); face at 1x
  0.25 / tile-CN 0.6 / end 0.8 (200 s).
- **Lines:** `finish.py --no-tighten`, bold plum `3b1c26`, outer 7-17 px at 2x.
- **Final paint** (before lines): palette distance 1.17, off-palette 0.3%.
- **Note:** the detail pass absorbed the candidate's small grey legs into the body and tail feathers; nothing reads
  as feet now.

## 5. Rig and in-game
- `rigparts.py thunderbird` (spec `thunderbird_parts.json`): parts back to front `tail`, `wing_right`, `wing_left`,
  `body` (root), `head`; parent body. SAM IoU: tail 0.55, wing_right 0.36, wing_left 0.75, body 0.33.
- **Rig fixes this round:** the SAM head split the white face from the eyes and beak, so the head is a hand polygon
  (crest, face and beak; `poly_only`). The XPU inpaint returned a flat grey body fill and colour-bar garbage that
  passed the black check, so `rigparts.inpaint_ok` now rejects flat fills (std < 3), streaks (gradient ratio > 1.35)
  and off-swatch saturated pixels (> 2%); a part whose tries all fail keeps its Telea pre-fill (wing_right did).
- **Flying pose, pivots:** `character.png` keeps the trio convention: its bottom centre is the lowest point, the
  tail tip (canvas (953,2004)). `parts.json` also records `feet_pivot` [920,1510] (canvas px, the talon/body
  underside) for anchoring in flight.
- **Board placement:** the in-game sprite uses the tail-tip pivot, so nothing is drawn below the unit's ground point
  (the depth order against the row in front stays right) and the bird reads as hovering low over its hex. A future
  flight idle can anchor on `feet_pivot` instead.
- **Known limits:** machine cuts and the body under-fill (a soft grey-blue wash) show only when parts rotate; an
  artist pass on the joints is advised before animation.
- **In-game:** `export_ingame.py --line-px 6` (content 448x504 in a 512x512 frame, pivot 256,508);
  WorldHeight 1.25 (wing tip to tail tip).

## Producer decision (final)
Approved as is (candidate #2, L7), personality **Wild/energetic**, for in-game use with AI disclosure.
