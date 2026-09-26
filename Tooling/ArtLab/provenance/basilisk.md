# Provenance: Basilisk (Dark, personality **Sly**)

**Status:** approved by the producer (art director) as the Basilisk's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/basilisk/basilisk.png` (manifest `beast_basilisk_illustrated`, ArtKey `beast/basilisk/illustrated`) |
| Master character, rig parts | `content/art/source/basilisk/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/basilisk/design_pick.png` (round-2 candidate #7, `basilisk_v2_L2`, after the far-front leg recolour) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Scripts: `../scripts/seven/` (as run for the seven finals; see its `common.py` for the
paths: one beast's folder in `ARTLAB_OUT`, the trio finals in `ARTLAB_FINALS`). Shared: `gen4.py` (prep),
`lock2.py`, `facemask.py`, `chain.sh` (`detail_pass.py`, `finish.py`), `offpal.py`, `rigparts.py` with
`basilisk_parts.json`, `parts_sheet.py`; the fixes `basfix.py` and `farhind.py`. The candidate-sheet scripts
(`cand.py`, `layouts5.py`, `sheet5.py`) are not in the repo; their settings are recorded below.

## 1. Candidate sheets
- **Round 1** (`basilisk_candidates.png`, reference only): 16 serpent-king candidates (layouts L0-L11, text seeds
  11-14), subject `sly, smirk, narrowed eyes, basilisk, serpent king, small crown crest, glowing gold eyes, two small
  forelegs, long tail, deep plum scales, cream belly`. **Rejected by the producer:** "The bodies or tails aren't right.
  AI tends to struggle with snake bodies. Try again and make its body more lizard like instead of snake, no coiled
  body."
- **Round 2** (`basilisk_candidates_v2.png`): 16 candidates, new lizard layouts L0-L11 (`layouts5.basilisk_v2`: a low
  four-legged crested lizard, sprawled legs, a raised chibi head about 1:3.5 with a small gold crown crest, sly
  narrowed eyes, a dorsal crest and a long tapering tail; no coil, no S-body) and text seeds 11-14, all 20 steps, IP
  0.45 with the finals refs. All 12 layouts gave four-legged lizards; all 4 text candidates were rejected (sitting,
  bipedal, curled tail).
- **Chosen candidate: round-2 #7 = `basilisk_v2_L2`**: colour-mass lizard layout 2, img2img at 0.84, IP 0.45 (finals
  refs), 20 steps, seed 502.
- **Prompt (candidate and lock):** `no humans, solo, chibi, mythical creature, sly, smirk, narrowed eyes, basilisk,
  crested lizard, four legs, low body, long tapering tail, small crown crest, glowing gold eyes, deep plum scales,
  heroic pose, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, red, cyan, green,
  blue, pink, pokemon, snake, coiled, serpent, hood, fangs, wyvern, wings, fire, flames, multiple views, dark
  background, lowres, bad anatomy, text, worst quality, blurry`
- **Swatch** (deepened plum/violet, cream reduced to one accent): 2e1f3a 3, 4a2f5e 3, 6e4a82 2, d8c8b8 1, e8b04a 1,
  1c1224 1.
- **IP check:** no cobra hood, no banded purple-with-fangs, no blade tail (not Arbok, Seviper or Ekans); not the
  Harry Potter basilisk (no giant green snake).

## 2. Producer decisions
1. Round 1 (serpent bodies) rejected: "make its body more lizard like instead of snake, no coiled body".
2. Round 2 **#7** approved: a four-legged lizard body, because snake bodies were rejected.
3. The check asked for: 4 legs, one visible eye per side, one mouth, and a tapering, uncoiled tail.
4. Personality: **Sly**.

## 3. Pre-lock check and fixes
- **Checked on the candidate:** one glowing gold eye visible; one mouth line plus a nostril; a long, tapering,
  uncoiled tail; 4 legs (near hind, dark far hind, far front, near front).
- **Fix 1** (`basfix.py`): the far-front leg was painted in off-palette peach, which made it read as a separate odd
  limb and shared a visual foot with the far-hind leg; it was recoloured to shaded plum, luminance-preserving, only
  inside the body.
- **Lock:** `lock2.py basilisk pick 0.55 0.45 0.55`, seeds 11-66, 26 steps. **Pick: seed 55.** Consistency over 6
  seeds: mask IoU 0.981, colour ΔLab 1.93, palette distance 2.61.
- **Fix 2** (`farhind.py`, after the lock): the far-hind leg had lost its foot and merged into the belly, so the first
  finish read as 3 legs. The near-hind lower leg and foot were copied (darker, far side) behind the others, so the
  far-hind leg reaches the ground again; then the detail and line chain and the rig were re-run. The anatomy check is
  `basilisk_anatomy_check.png` (legs numbered, eye and mouth).

## 4. Finish
- **Face mask:** (780,715), half-axes 75x50.
- **Detail:** body 0.55 / tile 0.4; face 0.25 / tile 0.6. Detail word: `sly`.
- **Lines:** bold plum.

## 5. Rig and in-game
- `rigparts.py basilisk` (spec `basilisk_parts.json`): `tail`, `leg_far_b` (a hand polygon), `leg_far_f`, `body`
  (root), `leg_near_b`, `leg_near_f`, `head`.
- **Known limits:** the far-hind leg (2) is only partly visible: a dark leg whose foot peeks out behind the far-front
  leg, reading as a shadowed fourth leg rather than a clear one (an artist paint-over or a wider stance is the fix if
  it must be unmistakable). The body sits right of centre on the canvas, as in the candidate. Machine-cut joints.
- **In-game:** `export_ingame.py --line-px 6` (content 504x199 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 0.65, the lowest beast on the board (about 1.2 units long). Its turn-order portrait is small, because
  portraits fit the whole square frame and this long, low beast fills it only in width.

## Producer decision (final)
Approved: round-2 candidate #7, a four-legged lizard body (snake bodies were rejected), personality **Sly**, for
in-game use with AI disclosure.
