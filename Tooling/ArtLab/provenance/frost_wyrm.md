# Provenance: Frost Wyrm (Ice, personality **Wise, wry**)

**Status:** approved by the producer (art director) as the Frost Wyrm's in-game art, 2026-09-26. AI-assisted;
disclose where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/frost_wyrm/frost_wyrm.png` (manifest `beast_frost_wyrm_illustrated`, ArtKey `beast/frost_wyrm/illustrated`) |
| Master character, rig parts | `content/art/source/frost_wyrm/character.png`, `parts/*.png`, `parts.json` (after the round-2 face fix) |
| Design source | `content/art/source/frost_wyrm/design_pick.png` (candidate #11, `frost_wyrm_L1`, after the horn, eye and face edits) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded, no new downloads. Models and
licences: `../README.md`. Scripts: `../scripts/seven/` (as run for the seven finals; see its `common.py` for the
paths: one beast's folder in `ARTLAB_OUT`, the trio finals in `ARTLAB_FINALS`). Shared: `gen4.py` (prep),
`lock2.py`, `facemask.py`, `chain.sh` (`detail_pass.py`, `finish.py`), `offpal.py`, `rigparts.py` with
`frost_wyrm_parts.json`, `parts_sheet.py`; the fix `frostfix.py`. The candidate-sheet scripts (`cand.py`,
`layouts5.py`, `sheet5.py`) are not in the repo; their settings are recorded below.

## 1. Candidate sheet (remaining-seven round)
- **Generated:** 16 candidates: colour-mass layouts L0-L11 and text seeds 11-14, all 20 steps; 12 shown on
  `frost_wyrm_candidates.png`. "Old/wise" was hard to get (most read cute and young); `curled horns` gave ram horns
  on many; pale eyes on a pale body went blank white on several (#10-#12).
- **Chosen candidate: #11 = `frost_wyrm_L1`**: colour-mass layout 1, img2img at 0.84, IP 0.45 (finals refs), 20 steps,
  seed 501.
- **Prompt:** `no humans, solo, chibi, mythical creature, old, wise, wry smile, sleepy eyes, ice dragon, wingless,
  frost whiskers, icicle beard, rime scales, pale blue, curled horns, four legs, heroic pose, bold clean lineart, soft
  cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple,
  magenta, red, orange, pokemon, kyurem, glaceon, articuno, spyro, digimon, fire, flames, multiple views, dark
  background, lowres, bad anatomy, text, worst quality, blurry`
- **Swatch:** c9e4ee 3, f4f8f6 3, 8fb8d0 2, 4f7a99 1, d8c8a8 1, e8b04a 1.
- **IP check:** not Kyurem, Glaceon, Spyro or Articuno; ram horns are generic.

## 2. Producer decisions
1. Candidate **Frost Wyrm #11**, with the notes:
   - "The candidate's eyes were blank pale almonds. Give it proper eyes with irises and pupils, half-lidded and
     knowing, to fit wise/wry."
   - "Swap the ram-curl horns for swept-back dragon horns."
2. Round 2 (face fix): "a small second eye ... and maybe a small second mouth on its face. Only the one eye and one
   mouth should be shown."
3. Personality: **Wise, wry**.

## 3. Design edits (`frostfix.py`, in the lock source)
1. **Horns:** the ram curls SAM-cut, Telea-filled inside the head silhouette and paper-filled outside it.
2. **New horns:** two swept-back dragon horns (bone cream d8c8a8 / e2cea0 tones with a plum outline). Near horn
   (402,366)→(590,268), 44 px base; far horn (334,356)→(490,250), 32 px base.
3. **Eye** (3/4 view): an almond white, a dark-blue iris (2c568a), a pupil and a highlight; the **upper ~45% covered by
   a skin-tone lid** with a heavy lid line and a brow raised at the outer end: the knowing, wry look.
4. **Mouth:** a small wry curl.
5. **Protecting the eye through the lock:** the eye pixels are kept in the colour block and unblurred in the soft init
   (the golem `eyes.py` lesson); the eye and mouth contours go into the canny; after the lock the painted eye is pasted
   back (`frostfix.py restore`) before the face-protected detail pass.
6. **Round 2, one eye and one mouth:** round 1 had deliberately painted a small far eye, and the candidate still had a
   blue far-eye arc at the head edge and a nostril/mini-mouth stroke under the new mouth, which the lock turned into
   features. `EYES` now holds the near eye only, plus an `ERASE` list painted to skin: the far-eye arc (227,452), the
   old far eye and brow (264,425), the nostril/mini-mouth (277,505) and stray cheek marks (455,468). The canny guide
   carries one eye and one mouth. Before/after: `frost_wyrm_face_fix.png`.

## 4. Lock and finish
- **Lock prompt:** `old, wise, wry smile, half-lidded eyes, ice dragon, wingless, frost whiskers, icicle beard, pale
  blue, dark irises, swept back horns` (77 tokens with the house prompt). **IP negatives:** `pokemon, kyurem, glaceon,
  ram horns, blank eyes, wings`.
- `lock2.py` with pick init 0.55, canny 0.55 (end 0.7), IP 0.45, 26 steps, seeds 11-66, re-run after the round-2
  re-prep. **Pick: seed 33** both rounds (lowest palette distance, 2.28 in round 1).
- **Consistency (6 seeds):** mask IoU mean 0.976 (min 0.970); colour ΔLab mean 1.21 (max 2.14); palette distance mean
  3.07 (max 3.55). The trio's cool drift does not apply (it counts every ice-blue pixel).
- **Face mask:** (320,450), half-axes 115x80. **Detail:** body 0.55 / tile 0.4 at 2x; face 0.25 / tile 0.6.
- **Lines:** bold plum.

## 5. Rig and in-game
- `rigparts.py frost_wyrm` (spec `frost_wyrm_parts.json`): `tail`, `fin`, `body` (root), `head` (includes the horns);
  the head/body cut is at the neck.
- **Known limits:** the small blue crystal fin on the back, kept from #11, reads slightly wing-like; remove it at the
  art pass if "wingless" must be strict. Machine-cut joints.
- **In-game:** `export_ingame.py --line-px 6` (content 474x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.2 (a sitting wyrm, its tail curled beside it).

## Producer decision (final)
Approved: candidate #11 with dragon horns replacing the ram curls, knowing half-lidded eyes, and the face fixed to
one eye and one mouth, personality **Wise/wry**, for in-game use with AI disclosure.
