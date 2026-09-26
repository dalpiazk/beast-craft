# Provenance: Leviathan (Water, personality **Serene, regal**)

**Status:** approved by the producer (art director) as the Leviathan's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/leviathan/leviathan.png` (manifest `beast_leviathan_illustrated`, ArtKey `beast/leviathan/illustrated`) |
| Master character, rig parts | `content/art/source/leviathan/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/leviathan/design_pick.png` (candidate #1, `leviathan_t15`, paperised and colour-locked, without its lines) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Script names below are the art-lab round's working scripts (the trio's scripts, adapted:
`lock2.py`, `facemask.py`, `chain.sh`, `offpal.py`); see "Changes vs the trio recipe".

## 1. Candidate sheet (remaining-seven round)
- **Style refs:** the three approved finals (phoenix, golem, kirin), equally weighted, InstantStyle
  `up.block_0=[0,0.45,0]` (no longer the round-2 fire birds, which removes the fire bias).
- **Generated:** 19 candidates: colour-mass layouts L0-L9 and text seeds 1-3, 11-14 and 15-16; 12 shown on
  `leviathan_candidates.png`. Text worked best (fin-crest serpents with calm, lidded faces).
- **Chosen candidate: #1 = `leviathan_t15`**: txt2img, Animagine XL 4.0 with the fp16-fix VAE, DPM++ 2M Karras,
  26 steps, CFG 5, 896x1152, seed 15 (CPU generator). The most serene face of the set: a closed-mouth smile, lidded
  eyes, a golden scale-mane frill and a clean chibi head-to-coil ratio.
- **Prompt:**
  ```
  no humans, solo, chibi, mythical creature, calm, serene, regal, gentle eyes, sea serpent, long coiled body, flowing fin
  frills, protective scales, teal scales, cream belly, small horns, closed mouth, heroic pose, bold clean lineart, soft cel
  shading, painterly, warm light, full body, simple background, masterpiece
  ```
- **Negative:**
  ```
  flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple, magenta, red, orange,
  pokemon, gyarados, milotic, dragonair, lugia, water, waves, fire, flames, multiple views, dark background, lowres,
  bad anatomy, text, worst quality, blurry
  ```
- **Swatch** (hex, weight): 2f6f7a 3, 4f9a9a 3, 9fd3c7 2, f3e6cc 2, e8c170 1, 23485a 1.
- **Candidate finishing:** paperise, then the swatch lock (Reinhard: chroma 0.7, light 0.3, gain cap 0.5-1.5), then
  the bold plum line pass.
- **Rejects (IP):** L0 (Dratini-like), L6 (Dragonair-like); others for unreadable heads or an eel read.

## 2. Producer decision
Candidate **Leviathan #1**, approved as is. Personality: **Serene** (calm and regal).

## 3. Lock
- **Prep** (`gen4.py prep`): SAM box mask, canny 50/130 from the pick's own lines, swatch block and soft init.
- **Style A/B:** IP 0.35 / 0.45 / 0.55 on the soft-block lock (strength 0.80, canny 0.55, end 0.7): all held the
  design and differed little.
- **Init A/B** (IP 0.45, seed 11): the trio's soft block at 0.80 (and at 0.70) **paled the design** (the deep teal
  coil went periwinkle, the golden scale-mane teal). **Pick-init at 0.55** keeps the approved design, colours and
  lidded smile and repaints it in the house finish. Chosen, because the pick was approved as is.
- `lock2.py leviathan pick 0.55 0.45 0.55 ../work/seeds lock`, seeds 11-66: 26 steps, CFG 5, canny 0.55 (end 0.7),
  IP 0.45, the prompt and negative above, Reinhard toward the swatch block.
- **Consistency (6 seeds):** mask IoU mean 0.992 (min 0.990); colour ΔLab mean 1.72 (max 4.12); palette distance
  mean 1.58 (max 2.03); off-palette (hue > 30° from every chromatic swatch hue, `offpal.py`) 2.2-6.9% per seed. The
  trio's cool-drift metric does not apply (teal is this beast's palette).
- **Lock pick: seed 33** (palette distance 0.99, 3.2% off-palette).

## 4. Finish
- **Face protection:** ellipse at 1x centre (560,360), half-axes 125x90 (eyes, snout, smile).
- **Body detail** at 2x: strength 0.55, tile-CN 0.4, end 0.6, seed 8, face masked out. **Face detail** at 1x:
  strength 0.25, tile-CN 0.6, end 0.8. Detail prompt: the round-4 detail prompt with `serene`; its negative drops
  `cyan` (teal is on-palette).
- **Lines:** `finish.py --no-tighten`: SAM box mask, halo clean, bold plum line `3b1c26` (outer 7-16 px at 2x).
- **Retouch:** two dark detail-pass specks on the neck Telea-filled.
- **Final paint** (before lines): palette distance 1.7, off-palette 3.3%.

## Changes vs the trio recipe (and why)
- Style refs are the approved finals; the lock inits from the pick at 0.55, not the soft block at 0.80.
- XPU this session: math SDPA and VAE tiling off (the default SDPA kernel NaN'd; the tiled VAE decode corrupted the
  bottom tiles). About 2.5x slower: lock ~95 s per seed, body detail 643 s, face 194 s.
- Reinhard fidelity: the block-pixel threshold `mean > 90` became `mean > 45`, so the dark teal 23485a counts.
- The rig's cool-hue palette guard is off for Water (teal is legitimate).

## 5. Rig and in-game
- `rigparts.py leviathan` (spec `leviathan_parts.json`): parts back to front `coil`, `fin`, `body` (root, the S-neck
  and front coil), `head` (with the scale-mane frill); parent body. Character alpha: SAM box mask plus the plum
  contour ring; per-part SAM points clipped to polygons. SAM IoU: coil 0.72, fin 0.89, body 0.51, head 0.48;
  20% auto-fixed (orphans to the nearest part).
- **Root pivot:** the lowest coil contact (bottom centre of `character.png`); child pivots at the centre of each
  part's contact band with its parent.
- **Occlusion fill:** re-run with the inpaint validator (`rigparts.inpaint_ok`): all 6 SDXL tries for the coil were
  rejected, so the coil keeps its Telea pre-fill (smooth teal, hidden under the body and fin).
- **Known limits:** the coil/body cut is a straight polygon edge and the head/neck boundary is ragged; an artist pass
  on the joints is advised before animation.
- **In-game:** `export_ingame.py --line-px 6` (content 320x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.3 (the upright coil stands a little taller than the phoenix).

## Producer decision (final)
Approved as is (candidate #1), personality **Serene/regal**, for in-game use with AI disclosure.
