# Provenance: Phoenix (Fire, personality **Fierce**)

**Status:** approved by the producer (art director) as the Phoenix's in-game art, 2026-09-25. AI-assisted; disclose
where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/phoenix/phoenix.png` (manifest `beast_phoenix_illustrated`, ArtKey `beast/phoenix/illustrated`) |
| Master character, rig parts | `content/art/source/phoenix/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/phoenix/design_pick.png` (= `refs/d_406.png`) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded. Models and licences: `../README.md`.

## 1. Design source: round-2 candidate `d_406`
- **Method:** txt2img, no ControlNet, Animagine XL 4.0 + fp16-fix VAE, 896x1152, DPM++ 2M Karras, 20 steps, CFG 6,
  `torch.Generator("cpu").manual_seed(406)`.
- **Prompt (template v3):**
  `no humans, chibi, cute phoenix, bird, original creature, proud, pointed beak, big head, amber eyes, flame crest,
  curved neck, raised wings, flame-tipped feathers, long flowing flame tail, crimson and gold, bird legs, full body,
  three-quarter view, thick dark outlines, cel shading, simple background, masterpiece, high score, absurdres`
- **Negative:** `pokemon, moltres, ho-oh, talonflame, torchic, digimon, fanart, dragon, snake, cat, mammal,
  egg-shaped body, lowres, bad anatomy, extra wings, text, watermark, worst quality, low quality, low score, bad
  score, blurry, realistic, 3d, human, multiple views`
- **Candidates:** 48 generated in round 2 (46 valid, 2 NaN), over templates v2 (modes a, b, c: txt2img, gesture
  canny, img2img) and v3 (modes d, e).
- **IP-likeness review (by eye, not a legal clearance):** d_404, e_452 and e_454 rejected (the Moltres formula: a
  yellow body, flame wings, long legs); a_107 and a_110 (fire-fox starters); all of mode c (the generic pudgy
  fire-starter look). d_406 checked against Moltres, Ho-Oh and Talonflame: no match (cream body, red crest, no
  rainbow or gold crown, no hawk mask).
- **Chosen candidate:** `d_406`. **Producer decision (round 2):** winner, as the best fit to "proud but cute mythic
  firebird". Round 3 was judged "too soft/babyish"; round 4 returned to d_406's energy.

## 2. Lock (round 4)
- `generate.py prep phoenix refs/d_406.png`: canny 50/130 of d_406's own lines (character only), the swatch block,
  the soft init.
- `generate.py lock phoenix --ipa 0.6 --cn 0.55 --cn-end 0.7 --strength 0.80 11 22 33 44 55 66`
  (StableDiffusionXLControlNetImg2ImgPipeline, 26 steps, CFG 5; InstantStyle `up.block_0=[0,0.6,0]` with the
  weighted refs d_406 0.5 / a_104 0.3 / b_208 0.2; Reinhard chroma 0.7 / light 0.3 with the a\* clamp).
- **Prompt:** `no humans, chibi, majestic, proud, mythical creature, phoenix, bird, pointed beak, swept flame crest,
  curved neck, raised flame wings, long plume tail, cream and orange feathers, bird legs, heroic pose, dynamic,
  elegant, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, angry, violet,
  purple, cyan, magenta, blue, pokemon, moltres, ho-oh, torchic, charmander, fanart, lowres, bad anatomy, extra
  legs, text, worst quality, low quality, blurry, 3d`
- **Scale A/B:** 0.5 / 0.6 / 0.7 on seeds 11/22/33, all kept the design; 0.6 chosen.
- **Consistency (6 seeds):** mask IoU mean 0.916 (min 0.882); seed-to-seed colour ΔLab mean 6.43 (max 11.69);
  palette distance mean 1.82 (max 2.94); cool drift 0/6.
- **Lock pick: seed 55** (drift 0.02%, palette distance 1.21, no retries).

## 3. Finish
- **Detail pass:** `detail_pass.py --strength 0.65 --tile-cn 0.3 --cn-end 0.6 --seed 7` (2x Lanczos to 1792x2304, 6
  tiles of 1024 with 256 overlap, 30 steps, CFG 5, character mask), prompt `no humans, original creature, majestic,
  painterly, visible brush strokes, soft cel shading, warm light, rim light, fine texture, bold clean lineart,
  masterpiece, high score, absurdres`.
- **Lines:** `finish.py` bold preset, tighten on (peels the soft tan glow rim so the plum line sits on the flame edge).
- **Final QA:** cool drift 0.12%, palette distance 0.3.
- **IP check (round 4, by eye):** not Moltres (no yellow body, no long-legged raptor), not Ho-Oh, not Talonflame; the
  round-3 flame-tipped tail (a Charmander echo) is gone.

## 4. Rig and in-game
- `rigparts.py phoenix`: parts back_wing, tail, legs, body, head (with crest), front_wing; parent body; SDXL inpaint
  at 0.55 (seed 7, palette-guarded) under back_wing, tail and body. Recomposite error 0.17/255.
- **Known limits:** machine-cut joints; the lower red wing mass belongs to `body`, not `front_wing`.
- **In-game:** `export_ingame.py --line-px 6` (content 436x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.25.

## Producer decision (final)
Approved as is (the round-4 final, unchanged), personality word **Fierce**, for in-game use with AI disclosure.
