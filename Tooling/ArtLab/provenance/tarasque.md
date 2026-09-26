# Provenance: Tarasque (Metal, personality **Grumpy**)

**Status:** approved by the producer (art director) as the Tarasque's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/tarasque/tarasque.png` (manifest `beast_tarasque_illustrated`, ArtKey `beast/tarasque/illustrated`) |
| Master character, rig parts | `content/art/source/tarasque/character.png`, `parts/*.png`, `parts.json` (after the round-3 chin fix and rig re-cut) |
| Design source | `content/art/source/tarasque/design_pick.png` (candidate #2, `tarasque_L2`, with the V3 legs in the 1,3,4,2 order) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Scripts: `../scripts/seven/` (as run for the seven finals; see its `common.py` for the
paths: one beast's folder in `ARTLAB_OUT`, the trio finals in `ARTLAB_FINALS`). Shared: `gen4.py` (prep),
`lock2.py`, `facemask.py`, `chain.sh` (`detail_pass.py`, `finish.py`), `offpal.py`, `rigparts.py` with
`tarasque_parts.json`, `parts_sheet.py`; the fixes `legfix.py`, `legcomp.py`, `legswap.py`, `legcomp4.py` and
`chinfix3.py` (`chinfix.py`, `chinfix2.py`: rejected attempts). The candidate-sheet scripts (`cand.py`,
`layouts5.py`, `sheet5.py`) are not in the repo; their settings are recorded below.

## 1. Candidate sheet (remaining-seven round)
- **Generated:** 16 candidates: colour-mass layouts L0-L11 and text seeds 11-14, all 20 steps; 12 shown on
  `tarasque_candidates.png`. The layouts (domed shell, lion head, stubby legs) gave low, heavy, spiky beasts; the
  model often turned the dome into a mane, and #1-#3 kept a real shell.
- **Chosen candidate: #2 = `tarasque_L2`**: colour-mass layout 2, img2img at 0.84, IP 0.45 (finals refs), 20 steps,
  seed 502. The head in a steel helmet-shell with spikes, gold tusks and a flat grumpy stare.
- **Prompt:** `no humans, solo, chibi, mythical creature, grumpy, scowl, frowning, lovable, tarasque, armored river
  beast, iron plated shell, metal spikes, lion face, stubby legs, steel grey, bronze rust, heroic pose, bold clean
  lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple,
  cyan, blue, pink, pokemon, torterra, blastoise, bowser, koopa, tortoise, fire, flames, multiple views, dark
  background, lowres, bad anatomy, text, worst quality, blurry`
- **Swatch:** 6e7278 3, a8adb2 2, a0643a 2, 5f7f5a 1, d8c8a8 2, 3e4146 1.
- **IP check:** no turtle shell or tortoise body (not Torterra, Blastoise or Bowser/Koopa); the lion-faced armoured
  beast matches the public-domain Tarasque legend.

## 2. Producer decisions
1. Candidate **Tarasque #2**, with the note "appears to have only 2 legs → 4 sturdy legs".
2. Leg variants (`tarasque_legs_variants.png`): **V3, walking stride**, approved with the note "might have 5 legs →
   exactly 4 visible".
3. Round 2: the count is right, but the far legs looked as if they were on the wrong sides (legs angle away from the
   body for balance), and the near-right leg at the extreme right didn't fit the 3/4 view. Wanted: left→right order
   **1, 3, 4, 2**.
4. Round 3: a small gold fang/nub sticking up from the lower jaw, left of the mouth (2x px about 597,1805), a stray
   tusk fragment: remove it.
5. Personality: **Grumpy**.

## 3. Leg fix (V3 walking stride, exactly 4 legs)
1. **Rough edit:** a paper gap under the belly line (y 930) and 4 legs painted in the pick's own dark under-fur tones,
   each with a foot pad and 3 claws; near legs at x 215 and 700, far legs at 390 and 530, all separated by gaps.
2. **Canny guide:** from its own lines plus an anatomy guide (4 leg outlines, foot pads, claws, belly line), with
   stray fur edges removed from the gaps.
3. **Two locks:** 0.60 keeps the head and shell exactly (the legs stay ghost pegs); 0.80 repaints the legs as real
   furred legs, composited in only below the belly (feathered 40 px).
4. **5-leg fix:** the far-right leg had sat against the near-right leg and the near leg's lit/shade stripe split it
   into two; the far legs moved to 390/530, less lean on the near-right leg, the stripe removed, gaps between all four.
5. **Final-lock prompt:** `grumpy, scowl, frowning, lovable, tarasque, armored quadruped, four sturdy legs, iron plated
   shell, spikes, lion face, gold tusks, steel grey`. **IP negatives:** `pokemon, torterra, blastoise, bowser,
   bipedal, two legs`.

## 4. Leg order 1, 3, 4, 2 (round 2)
- Each leg was cut as a whole piece from the approved V3 design source, keeping its own painted shape and angle.
  Leg 1 stays at 215; leg 3 (far) → 395, leg 4 (near) → 560, leg 2 (far) → 725 (1x). Vacated spots: the fur band
  under the shell re-grown (Telea from the surrounding fur), the gap below paper. Far legs pasted first, near legs
  over them. The 4-leg canny guide was redrawn at the new positions.
- Re-locking an already-locked image drifted the head (tusks, pink nose) and grew paper grain, so the composite takes
  the **head and shell unchanged from the approved V3 source** and only the legs from the 0.80 lock (below y 870,
  40 px ramp), on clean paper.
- **Seed check:** `lock2.py tarasque pick 0.55 0.45 0.55`, seeds 11-66, 26 steps: mask IoU mean 0.990 (min 0.987),
  colour ΔLab 3.05, palette distance 0.68 (`seeds_tarasque.png`). The re-lock turned the rendering harsher (jagged
  crystal texture, busier face), so **the final is finished directly from the approved-look composite** (itself a lock
  output). (V3 round 1's lock: seed 66, IoU 0.967, colour Δ 3.13, palette distance 0.80.)
- **Leg count on the final: exactly 4.**

## 5. Finish
- **Face mask:** (300,790), half-axes 175x95. **Detail:** body 0.55 / tile 0.4; face 0.25 / tile 0.6. Detail word:
  `grumpy`. **Lines:** bold plum.
- **Chin fix (round 3, `chinfix3.py`, on the final only):** the nub clone-stamped from the neighbouring chin fur (the
  tip from pale fur to the right, the base from the jaw shade to the left); clone seams blurred inside the patch only;
  the chin outline re-inked continuously; two 1-px specks Telea-filled. Side tusks, eyes, nose and mouth untouched.
  Paint-over and SDXL-inpaint attempts left a flat patch and were rejected. Before/after: `tarasque_chin_fix.png`.

## 6. Rig and in-game
- `rigparts.py tarasque` (spec `tarasque_parts.json`): `leg_far_l`, `leg_far_r`, `body` (root), `leg_near_l`,
  `leg_near_r`, `head` (a hand polygon).
- **Leg pivots (fixed):** set by hand at the hips/shoulders under the shell (`pivot_override`), canvas px far-l
  (818,1800), near-r (1084,1790), near-l (490,1790), far-r (1410,1790); they sit above each part's top edge because the
  hip is hidden under the shell, so the parts rotate about the hip.
- **Tusk tip (fixed):** the `leg_near_r` clip starts at y 945 (1x), so the gold tusk tip belongs to the body/head, not
  the leg. The rig was re-cut from the chin-fixed final.
- **Known limits:** the leg parts are straight polygon cuts at the belly; a faint warm haze was noted at the upper left
  of the paper (outside the character). Machine-cut joints.
- **In-game:** `export_ingame.py --line-px 6` (content 504x406 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.05: about 1.3 units wide, the bulkiest beast on the board.

## Producer decision (final)
Approved: candidate #2 → V3 walking stride, exactly 4 legs reordered left to right 1, 3, 4, 2, stray chin fang
removed, personality **Grumpy**, for in-game use with AI disclosure.
