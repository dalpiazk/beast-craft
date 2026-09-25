# Provenance: Golem (Earth, personality **Cute**)

**Status:** approved by the producer (art director) as the Golem's in-game art, 2026-09-25. AI-assisted; disclose
where a store requires it (see `docs/art/art-brief.md`).

| Asset | Path |
| --- | --- |
| In-game sprite | `content/art/beasts/golem/golem.png` (manifest `beast_golem_illustrated`, ArtKey `beast/golem/illustrated`) |
| Master character, rig parts | `content/art/source/golem/character.png`, `parts/*.png`, `parts.json` |
| Design source | `content/art/source/golem/design_pick.png` (exploration candidate #10) |

Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing uploaded. Models and licences: `../README.md`.

## 1. Candidate sheet (round 4)
- **Why layouts:** txt2img never drew "a living hillside" (always a furry wolf or lion on a rock), so
  `layouts.py golem 16` drew randomised colour-mass layouts (hill body, standing stones, flower patch, four thick
  legs, head at 1:3.5; numpy seeds 100+i).
- **Candidates:** 14, `generate.py explore_i2i golem 0..13`: img2img 0.80 + canny of the layout 0.4 + InstantStyle
  0.4 + the house prompt, generator seed 400+i, 26 steps, CFG 5, Reinhard to the swatch.
- **Prompt (round 4):** `no humans, chibi, majestic, proud, mythical creature, rock golem, quadruped, mossy hill back,
  standing stones, wildflowers, thick stone legs, glowing amber eyes, determined, grey stone, heroic pose, dynamic,
  elegant, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece`
- **Negative:** `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, angry, violet,
  purple, cyan, magenta, blue, fire, flames, pokemon, torterra, turtwig, tortoise, sprout, wolf, lowres, bad anatomy,
  extra legs, text, worst quality, low quality, blurry, 3d`
- **Rejects:** block/table bodies, a humanoid ape, a bear (#12), a turtle-like one (#13, a Torterra risk), a cat face.
- **Round 4 first used #5** (a determined, heavy hill-creature). Its face was lost in the detail pass.
- **Chosen candidate: #10** (layout 10, seed 410; drift 0.03%), re-picked by the producer in the final round.

## 2. Producer notes (final round)
Re-lock from **#10** with the personality word **Cute** (still Earth, still a sturdy hill-creature, not a round
blob). Keep #10's small round eyes and friendly face.

## 3. Lock
- `generate.py prep golem <#10>` (canny 50/130 of #10's own lines; SAM kept the standing stones this time).
- `face.py init golem <#10>`: the soft init's blur erased #10's small eyes, and the first A/B repainted them as hollow
  pale rings, so #10's eyes were pasted into the init (circles at (125,609) and (181,607), r 26, feather 1.5).
- **Prompt (final):** the subject is `rock golem, quadruped, mossy hill back, standing stones, wildflowers, thick
  stone legs, round face, big amber eyes, curious, grey stone` (replacing `glowing amber eyes, determined`); the rest
  of the house prompt and the negative are unchanged. 77 tokens.
  - A/B v1 used the round-4 subject (eyes lost). A/B v2 used `big glowing amber eyes`, which painted sun discs and
    halos behind the golem (IP 0.45 seed 22, IP 0.55 seed 11), so `glowing` was dropped.
- **Scale A/B:** IP 0.35 / 0.45 / 0.55 x seeds 11 / 22 (canny 0.55, end 0.7, strength 0.80). #10's silhouette and face
  held at every scale; 0.35 flatter, 0.55 heavier and yellower with halos. **Chosen 0.45.**
- `generate.py lock golem --ipa 0.45 --cn 0.55 --cn-end 0.7 --strength 0.80 11 22 33 44 55 66`
- **Consistency (6 seeds):** mask IoU mean 0.973 (min 0.962); colour ΔLab mean 5.37 (max 11.81); palette distance
  mean 0.73 (max 1.85); cool drift 0/6 (max 0.15%). (Round 4's #5 lock: IoU 0.87, min 0.67.)
- **Lock pick: seed 11** (drift 0.0%, palette distance 0.64; warm eyes, red wildflowers, the clearest grey stone),
  then `face.py restore`: #10's eyes pasted back, template-aligned (shifts 0/0 and 0/+1 px), r 23, feather 1.5.

## 4. Finish (`golem_chain.sh`)
- **Body detail:** `detail_pass.py --strength 0.55 --tile-cn 0.4 --cn-end 0.6 --seed 8`, mask = the character minus
  the face ellipse (centre (158,622), half-axes 84x66 at 1x, plus a 12 px band). 190 s.
- **Face detail:** `--scale 1 --strength 0.25 --tile-cn 0.6 --cn-end 0.8 --seed 8`, face-only mask. 40 s.
- **Detail prompt:** `no humans, original creature, cute, painterly, visible brush strokes, soft cel shading, warm
  light, rim light, fine texture, bold clean lineart, masterpiece, high score, absurdres`
- **Lines:** `finish.py --no-tighten --peel-paper --ground-green 900 --points "426,450;500,430;625,440;697,462"`: SAM
  point masks give the 4 standing stones the plum contour, the grass tufts by the feet are dropped from the mask, and
  bare paper is peeled from the mask edge (face and stones protected).
- **Final QA:** cool drift 0.05%, palette distance 2.94 (#10 itself: 3.05).
- **IP check (by eye):** a mossy hill on four stone legs with standing stones; no shell or tree (not Torterra), no
  sprout (not Turtwig or Bulbasaur), no ring-boulder humanoid (not Graveler or Golem).

## 5. Rig and in-game
- `rigparts.py golem` (points and polygons in `scripts/parts/golem.json`; the character alpha is the finish line
  mask): parts legs_back, body, legs_front, stones, head; parent body; inpaint fill under legs_back and body.
  Recomposite error 0.23/255. The paper gap between the back legs stays transparent.
- **Known limits:** the 0.55 body pass smoothed #10's cobbled stone plates into broader painterly facets (rerun the
  body pass at 0.45 / tile 0.5 to bring them back); the head part includes the front moss tuft; a tiny detached
  flower-sprig outline sits left of the head (from #10); machine-cut joints.
- **In-game:** `export_ingame.py --line-px 6` (content 504x385 in a 512x512 frame, feet pivot 256,508);
  WorldHeight 1.0, which makes it the bulkiest of the trio on the board (about 1.14 units wide).

## Producer decision (final)
Approved: the re-lock from exploration candidate #10, personality word **Cute**, for in-game use with AI disclosure.
