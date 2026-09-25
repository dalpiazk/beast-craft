# Provenance: Kirin (Light, personality **Mystic**)

**Status:** approved by the producer (art director) as the Kirin's in-game art, 2026-09-25. AI-assisted; disclose
where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/kirin/kirin.png` (manifest `beast_kirin_illustrated`, ArtKey `beast/kirin/illustrated`) |
| Master character, rig parts | `content/art/source/kirin/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/kirin/design_pick.png` (exploration candidate #9) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded. Models and licences: `../README.md`.

## 1. Candidate sheet (round 4)
- **Why layouts:** plain txt2img with the (fire-bird) style references turned the kirin into a fire spirit, even at
  IP 0. So `layouts.py kirin 16` drew randomised colour-mass layouts (numpy seeds 100+i).
- **Candidates:** 14, `generate.py explore_i2i kirin 0..13`: img2img 0.80 from the soft layout + canny of the layout
  at 0.4 + InstantStyle 0.4 (weighted refs) + the house prompt, generator seed 400+i, 26 steps, CFG 5, Reinhard to the
  swatch. (The canny end was 0.6 per the round notes; the log does not record it.)
- **Prompt:** `no humans, chibi, majestic, proud, mythical creature, qilin, deer, single curved golden horn, short
  cream mane, amber scales on back, amber eyes, serene, cream fur, dark hooves, heroic pose, dynamic, elegant, bold
  clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, angry, violet,
  purple, cyan, magenta, blue, fire, flames, pokemon, rapidash, xerneas, unicorn, horse, wolf, lowres, bad anatomy,
  extra legs, text, worst quality, low quality, blurry, 3d`
- **Rejects:** #8 (a second antler pair: a Xerneas/Sawsbuck risk), flame manes, a straight unicorn horn,
  spotted-fawn/baby reads.
- **Chosen candidate: #9** (layout 9, seed 409; drift 0.0%): head held high, one swept amber horn, an amber scaled
  mane, dark hooves, the best value contrast of the 14.

## 2. Lock
- `generate.py prep kirin <#9>`, then `generate.py lock kirin --ipa 0.45 --cn 0.55 --cn-end 0.7 --strength 0.80
  11 22 33 44 55 66` (the phoenix's recipe at IP 0.45, the non-fire scale). Same prompt and negative as above.
- **Consistency (6 seeds):** mask IoU mean 0.873 (min 0.806); colour ΔLab mean 4.2 (max 7.75); palette distance
  mean 7.54 (max 8.9); cool drift 4/6 (max 3.5%: the model paints slate/navy hooves and shadow edges).
- **Lock pick: seed 22** (drift 2.1%, palette distance 7.53, no retries).

## 3. Finish
- **Detail pass:** `--strength 0.65 --tile-cn 0.3 --cn-end 0.6 --seed 7`, same prompt as the phoenix (`majestic`).
- **Lines:** `finish.py --no-tighten` (the glow-rim peel eats pale cream areas and the horn tip).
- **Final QA:** cool drift 0.26%, palette distance 7.2.
- **IP check (by eye):** one horn and no antler crown (not Xerneas or Sawsbuck); no blue and no mane spike (not
  Monster Hunter's Kirin); no flames (not Rapidash). Caveat: the silhouette reads a little ibex/goat (generic, not IP).

## 4. Rig and in-game
- `rigparts.py kirin`: parts tail, legs_back, legs_front, body, mane, head, horn; parent body, horn -> head; inpaint
  fill under tail, both leg pairs and body (the palette guard reverted 2.3k teal/green pixels on the neck).
  Recomposite error 0.21/255.
- **Known limits:** machine-cut joints; the faint horn tip has a thin contour.
- **In-game:** `export_ingame.py --line-px 6` (content 369x504 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.15.

## Producer decision (final)
Approved as is (the round-4 final, unchanged), personality word **Mystic**, for in-game use with AI disclosure.
