# Provenance: Griffin (Air, personality **Bold, brave**)

**Status:** approved by the producer (art director) as the Griffin's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/griffin/griffin.png` (manifest `beast_griffin_illustrated`, ArtKey `beast/griffin/illustrated`) |
| Master character, rig parts | `content/art/source/griffin/character.png`, `parts/*.png`, `parts.json` (after the streak fix) |
| Design source | `content/art/source/griffin/design_pick.png` (candidate #1, `griffin_L8`, after the V2 wing fix and the tail rotation) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Scripts: `../scripts/seven/` (as run for the seven finals; see its `common.py` for the
paths: one beast's folder in `ARTLAB_OUT`, the trio finals in `ARTLAB_FINALS`). Shared: `gen4.py` (prep),
`lock2.py`, `facemask.py`, `chain.sh` (`detail_pass.py`, `finish.py`), `offpal.py`, `rigparts.py` with
`griffin_parts.json`, `parts_sheet.py`; the fixes `wingfix.py` (round 1, rejected), `wingfix2.py`, `tailfix.py` and
`streakfix.py`. The candidate-sheet scripts (`cand.py`, `layouts5.py`, `sheet5.py`) are not in the repo; their
settings are recorded below.

## 1. Candidate sheet (remaining-seven round)
- **Generated:** 21 candidates: colour-mass layouts L0-L13 and text seeds 1-3, 11-14 and 15-16 (L12/L13, t15/t16 at
  20 steps, the rest 26); 12 shown on `griffin_candidates.png`. The layouts won on structure (a single creature,
  eagle front and lion back); text tended to paint model sheets.
- **Chosen candidate: #1 = `griffin_L8`**: img2img at 0.84 from colour-mass layout 8, IP 0.45 (finals refs), 26 steps,
  CFG 5, seed 508. The heraldic proud griffin: both wings raised high, chest out, hooked beak raised, lion tail tuft.
- **Prompt:** `no humans, solo, chibi, mythical creature, bold, brave, proud chest, griffin, eagle head, lion body,
  feathered wings raised, eagle talons, lion hind legs, tufted tail, golden feathers, tawny fur, heroic pose, bold
  clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple,
  magenta, cyan, red, pokemon, braviary, hippogriff, horse, wolf, fanart, fire, flames, multiple views, dark
  background, lowres, bad anatomy, text, worst quality, blurry`
- **Swatch:** c98a3a 3, e0b877 3, fff3de 2, 7a4a2a 2, f2b84a 1, 9cc7d9 1. The sky-blue accent was remapped to cream in
  the block (it had been matched to pale feathers).
- **IP check:** no Braviary palette or crest plume, no horse body (not a hippogriff); the eagle-lion griffin is a
  public-domain heraldic creature.

## 2. Producer decisions
1. Candidate **Griffin #1** (L8), with the note: "his wings are positioned as if his body is facing left but he's
   facing right".
2. Wing fix round 1 **rejected**: "the wings appear to be coming out of the griffins' necks".
3. Round 2 variants (`griffin_wingfix_variants.png`): **V2 picked**: the wings rooted on the back, behind the ruff,
   with the note to keep the tail readable (V2's wing touched the tail tuft) without moving the root. So the tail was
   rotated clear.
4. Personality: **Bold, brave**.

## 3. Wing fix (round 2, V2) and tail
1. The body was cut away from both SAM wing masks and put on paper.
2. **Far wing:** re-seated *behind* the body (root (360,640), tilt 6°, scale 0.74), drawn only where the body isn't,
   so only its upper part rises above the back line.
3. **Near wing:** the candidate's own wing, mirrored and re-seated on the back line at the withers behind the ruff:
   root (320,660), tilt 15° back, scale 0.90.
4. SDXL mask-inpaint of the junction (an ellipse at the root plus a back-line band) at 2x, 0.55, seed 11.
5. **Tail readability:** the whole tail SAM-cut and rotated −15° about its haunch root (290,780), so the tuft moves
   about 45 px left and down, clear of the wing. The wing root is unchanged.
6. **Canny guide:** canny 50/130 from the edited pick's own lines, plus a low-threshold canny inside the pale wings
   and both wing contours, plus an anatomy guide (back line (300,560)→(298,770), a scapula bump, a wing-root arc).

## 4. Lock
- **Final-lock prompt change:** `feathered wings raised` → `wings raised from shoulders`, `lion hind legs` →
  `lion hindquarters` (77 tokens).
- `lock2.py griffin pick 0.60 0.45 0.55 ../work/seeds lock`, seeds 11-66: pick init 0.60 (0.70 and the soft block
  regrew ghost wings in round 1), canny 0.55 (end 0.7), IP 0.45, 26 steps, CFG 5, then Reinhard.
- **Consistency (6 seeds):** mask IoU mean 0.777 (min 0.636); colour ΔLab mean 4.72 (max 10.93); palette distance mean
  2.57 (max 3.68); off-palette ≈ 0. The low IoU is a mask artefact, not design drift: every seed has the same wing,
  tail and pose, but SAM's box mask sometimes drops the pale wing mass on cream paper.
- **Lock pick: seed 33** (palette distance 1.69).

## 5. Finish
- **Face mask:** centre (500,415), half-axes 115x85 (eye, beak, brow).
- **Detail:** body at 2x with the face masked (0.55 / tile 0.4 / end 0.6 / seed 8); face at 1x (0.25 / tile 0.6 /
  end 0.8). Detail word: `bold`.
- **Lines:** `finish.py --no-tighten`, bold plum. The first run lost the pale wings and tail from the SAM mask, so the
  re-run uses `--extra griffin_extra_mask.png` (the known wing, tail and body masks, dilated, intersected with
  non-paper pixels, OR'd into the SAM mask).
- **Stray fragment removed (face re-audit):** a pale/tan ghost stroke on the paper above the head crest (2x px about
  850-1040, 650-700), a leftover of the round-1 wing edit, was paper-filled outside the line mask only
  (`streakfix.py`; before/after `griffin_streak_fix.png`). The rig was re-cut from the fixed final.

## 6. Rig and in-game
- `rigparts.py griffin` (spec `griffin_parts.json`): `tail`, `legs_back`, `body` (root), `legs_front`, `wings` (near
  and far together), `head`. The pose test holds.
- **Known limits:** the body's hidden under-fill has flat, unpainted patches (Telea fallback after the inpaint
  validator rejected the SDXL fills; they show only when the wings rotate). The wing tips touch the left canvas edge
  (from the V2 placement); pad the canvas at the art pass if needed. The paper gap between the wing's leading edge
  and the head crest is real negative space, and is outlined.
- **In-game:** `export_ingame.py --line-px 6` (content 394x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.25.

## Producer decision (final)
Approved: candidate #1 with the V2 wing fix (wings rooted on the back behind the ruff, tail rotated clear) and the
streak fix, personality **Bold/brave**, for in-game use with AI disclosure.
