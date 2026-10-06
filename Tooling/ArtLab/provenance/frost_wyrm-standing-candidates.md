# Provenance: Frost Wyrm standing-pose candidates (for Meshy image-to-3D)

**Status:** candidates only, not yet producer-picked. The approved sitting design
(`content/art/source/frost_wyrm/character.png`, `Tooling/ArtLab/provenance/frost_wyrm.md`) gives a sitting mesh
that can't be rigged cleanly; this is a re-pose pass (sitting -> standing, four legs down) so the next step
(Meshy image-to-3D) gets a standing-mesh source. Tools: local only (Intel Arc 140V, XPU, bf16); no cloud
service, nothing uploaded, no Meshy credits spent (candidate generation only).

**Round 1 (below) was rejected by the producer:** correct pose, but design drift -- building the pose guide
from the Basilisk's 3D silhouette pulled the result toward a long, low lizard body, losing the approved
chibi proportions (big head, stocky short body), the crystalline ice-shard scale texture, the small ice
wings/fin, and (on most candidates) the two distinct swept-back horns. **Round 2 (section 5) fixes this** by
re-composing the approved art's own rig parts instead of borrowing another beast's body plan, and is the
recommended set to pick from.

## 1. Method (round 1, rejected for design drift)

Reused the house SDXL pipeline (`Tooling/ArtLab/scripts/seven/common.py`, `gen4.py`'s lock/explore_i2i
machinery, the Reinhard colour lock in `colour.py`) via a new ad hoc script,
`Tooling/ArtLab/scripts/seven/wyrm_stand.py` (not committed -- a one-off tool, kept alongside the other `seven/`
fix scripts for the pattern). Three approaches were tried, in the order the task favoured:

1. **ControlNet canny + img2img from a colour-mass "standing" layout (used for the kept candidates).**
   Rather than img2img off the approved *sitting* art (whose canny lines would re-lock the sitting silhouette),
   a fresh colour-mass layout was built the way `layouts.py` builds golem/kirin's exploration inits: flat
   swatch-coloured regions + a Canny edge map of the flat block + the round-3 `soften()` blur/light/grain init
   -- except the *shape* for this layout came from a real standing quadruped instead of hand-drawn ellipses.
   - A Blender headless script (ad hoc, not committed) imported `Tooling/Spike55/Live3D/Content/model/basilisk_anim.glb`
     (a standing quadruped rig from the 3D animation pilot, chosen over `kirin_anim.glb` for its low
     reptilian body plan), set it to the `Idle` action's first frame, flat-shaded it as a silhouette (no
     texture), and rendered an orthographic side-on probe (EEVEE, 896x1152).
   - A second ad hoc script thresholded that render to a mask, cropped/rescaled it to fill most of the house
     canvas, painted the frost_wyrm swatch into the silhouette by position (pale body, white head/belly, a
     deep-blue dorsal-ridge/tail accent, a small gold eye dot), then produced the usual three prep outputs:
     `sketch_frost_wyrm_block.png`, a layout-canny edge map (`cv2.Canny` of the flat block, dilated 2x2,
     matching `gen4.layout_canny`), and the soft img2img init (blur sigma 10, light gradient, grain -- grain
     was masked down outside the silhouette; see "what failed" below).
   - Generation: `StableDiffusionXLControlNetImg2ImgPipeline`, Animagine XL 4.0 + fp16-fix VAE, 896x1152,
     DPM++ 2M Karras, 26 steps, CFG 5, **img2img strength 0.74, canny conditioning 0.5, canny end 0.6**,
     InstantStyle IP-Adapter 0.45 on `up.block_0` only (the house's three approved finals --
     phoenix/golem/kirin's archived `content/art/source/<beast>/character.png`, composited onto the house
     cream BG, standing in for the old `ARTLAB_FINALS/*_final.png` style refs, which no longer exist on
     disk), then the usual Reinhard colour lock toward the frost_wyrm swatch.
   - **Prompt** (new subject, 77 tokens with the house head/personality/tail): `no humans, solo, chibi,
     mythical creature, old, wise, wry smile, half-lidded eyes, ice dragon, four legs on the ground, standing,
     tail extended, pale blue, dark irises, swept back horns, heroic pose, bold clean lineart, soft cel
     shading, painterly, warm light, full body, simple background, masterpiece`
   - **Negative** (house negatives for frost_wyrm + `sitting`; the 77-token budget only fit that one extra
     word): `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet,
     purple, magenta, red, orange, pokemon, kyurem, glaceon, ram horns, blank eyes, wings, fire, flames,
     multiple views, dark background, lowres, bad anatomy, text, worst quality, blurry, sitting`
   - Seeds 702-706 (5 renders); seed 701 used the same setup before strength/cn were tuned down (see below).

2. **Plain img2img (no ControlNet), same soft init, strength 0.78** -- tried once (seed 707) for a looser,
   possibly more front-turned head. Produced a second, malformed creature in the blank lower canvas (see
   below); rejected.

3. **Prompt-only txt2img fallback, no image conditioning** -- tried once (seed 708), IP-Adapter style only.
   Rejected: wrong (mauve/tan) background, a curled-up tail, and a crouching rather than standing read -- the
   prompt-only route that worked for the menu-backdrop landscapes did not reproduce a controlled quadruped
   pose here; the image-conditioned approaches above were clearly stronger for this task.

## 2. What failed

- **Seed 701 (first attempt, strength 0.82, cn 0.4/0.6):** a second, smaller, wrongly-coloured creature
  appeared in the blank lower third of the canvas -- the colour-mass layout only filled about a third of the
  896x1152 frame (a side-on quadruped is much wider than tall), and at high img2img strength the mostly-blank
  remaining canvas gave the model room to invent a second subject. Two mitigations: (a) grain in the soft
  init was masked down to near-zero outside the silhouette (full grain invites the model to "see" something
  there), and (b) strength was dropped 0.82 -> 0.74 and canny conditioning raised 0.4 -> 0.5 (end 0.6) so the
  whole canvas, not just the character, stays closer to the (quiet) init. That combination (seed 702 on)
  produced single-creature results reliably; the plain-img2img seed 707 (no canny, strength 0.78) reproduced
  the duplicate-creature failure, confirming the canny guide -- not just lower strength -- is doing real work
  here.
- **Candidate 706 (not on the sheet):** the hindquarters blended smoothly into the tail with no hind-leg
  shapes at all (only the two front legs read clearly) -- caught on a zoomed-in recheck of the leg region,
  not visible at thumbnail size. Dropped.
- **Candidates 703/705 (on the sheet as #4/#3):** a faint tan horizontal line sits across the canvas right at
  the feet -- a trace of the colour-mass layout's hard mask edge (the swatch block's bottom silhouette
  boundary) that survived generation as a ground-line-like mark. Cosmetic only (doesn't read as an actual
  ground plane at a glance, but is there under inspection); worth a quick paint-out if either is picked.
- **IP-Adapter / style refs:** the three house style refs (phoenix/golem/kirin) no longer exist on disk as
  `*_final.png` (`ARTLAB_FINALS` was empty) -- recreated for this session from the beasts' own archived
  masters (`content/art/source/{phoenix,golem,kirin}/character.png`, composited onto the house cream BG).
  Same images, same role (InstantStyle on `up.block_0` only, weighted equally), so this should reproduce the
  house finals' style conditioning; no leak/crash issue was hit with this house-refs-only setup (the
  known IP-Adapter leak/crash was specific to menu-backdrops' *secondary, content-heavy* reference image --
  not applicable here, since the frost wyrm's own sitting art was deliberately *not* used as an IP-Adapter
  image reference, only as the design-fidelity target via the swatch/prompt).
- **Eye colour drift:** several candidates came out amber/gold-eyed rather than the approved dark-blue iris
  (`2c568a` per the original face fix) -- likely the shared IP-Adapter style pull from golem/phoenix, which
  both have amber eyes. Seeds 703, 705 and 704 (partially) kept a blue iris; 702 did not. Flagged per
  candidate below.

## 3. The sheet

`frost_wyrm_candidates.png` (in the session scratchpad): the approved sitting art on the left, four numbered
standing candidates beside it (all from the ControlNet canny + colour-mass-layout method above, seeds
704/702/705/703). Individual cutouts (background removed to transparent) at `frost_wyrm_cand_1.png`
through `frost_wyrm_cand_4.png` in the same folder, numbered to match the sheet.

| # | Seed | Legs | Pose | Eyes | Design fidelity |
| - | ---- | ---- | ---- | ---- | ---------------- |
| 1 | 704 | 4, clearly apart (near front + near hind reach the ground; far pair occluded as normal for a side view) | standing, level back, tail extended straight behind, not curled | one eye visible (side profile); amber, not the approved dark blue | pale blue/white, crystalline scales, two horns (read as a single swept crest more than two distinct horns) -- good but the horns are softer than the approved design |
| 2 | 702 | 4, same as #1 | standing, level back, tail extended behind | one eye visible (profile); amber, not approved blue | pale blue/white, crystalline scales, two swept horns reasonably distinct; closest in temperament ("wry smile") to the approved art |
| 3 | 705 | 4, same as #1 | standing, level back; tail extends behind but curls decoratively at the very tip (not wrapped around the body) | one eye visible (profile); correct dark blue iris | **best horn match** -- two clearly separate swept-back horns, close to the approved shape; faint ground-line artifact at the feet |
| 4 | 703 | 4, same as #1 | standing, level back, tail extended behind | one eye visible (profile); correct dark blue iris | clean face/eye, pale crystalline scales match well; faint ground-line artifact at the feet (same cause as #3) |

None of the four achieve a 3/4 or near-front view with both eyes visible -- the pose guide was built from a
side-on Blender render, so the head came out in profile on every kept candidate (this matches the task's
warning about Phoenix/Golem's hidden eye on 3/4 views, just from the opposite direction: here the guide never
offered a turned head at all). A worthwhile follow-up if the producer wants eye visibility: rebuild the pose
guide from a 3/4-angle Blender render (tried once here; the orthographic 3/4 probe filled far less of the
frame and was not pursued further under the session's pacing constraints) or erase the head portion of the
canny guide so the prompt can turn the head freely while the body/leg structure stays controlled.

**Rejected, not on the sheet:** seed 701 (duplicate second creature), seed 707 (plain img2img, duplicate
second creature), seed 708 (txt2img prompt-only: wrong background colour, curled tail, crouching read),
candidate 706 (missing hind legs -- reads as a legless wyrm from the waist back).

## 4. Round 1 outcome

Rejected by the producer: all four stood correctly, but the Basilisk-silhouette pose guide drifted the
design toward a long, low lizard, losing the chibi proportions, the ice shards, the wings, and (on most
candidates) the distinct horns. See section 5 for the retry.

## 5. Method (round 2, kept)

Per the producer's retry brief, this round re-composes the approved art's **own rig parts**
(`content/art/source/frost_wyrm/parts/{body,head,fin,tail}.png`, `parts.json`) into a standing pose instead
of borrowing a different beast's 3D silhouette, so the design is inherited pixel-for-pixel rather than
re-imagined.

- **The collage** (ad hoc script, not committed): `head.png` (face, eye, both horns), `fin.png` (the small
  ice-wing/crystal fin) and `body.png` (the torso -- this beast's rig has no separate leg part; the sitting
  body bakes the tucked front legs into a rounded haunch/belly silhouette, so there is nothing to
  duplicate/mirror) are pasted at their documented `parts.json` offsets, unchanged. `tail.png` is rotated
  ~8-18 degrees and moved to the haunch so it extends behind the body instead of its original curled-forward
  placement. **Four new short, stocky legs** (same icy palette, warm-plum outline) are drawn under the torso
  piece, since no leg cutout exists to repurpose -- the one deliberate deviation from "re-compose the art's
  own parts" in the brief, called out here for that reason. Two stance variants were built: **a** (legs
  roughly under the chest/haunch, narrower) and **b** (legs spread wider, a visibly different candidate).
- **Prep:** the collage is cropped to its content, fit into the house 896x1152 canvas on the house cream BG,
  then (a) a `gen4.prep`-style own-lines Canny edge map (not a synthetic colour-mass block this time -- the
  collage already *is* the finished design) and (b) a very lightly blurred (sigma 2, not the heavy
  `soften()` blur/grain used for colour-mass layouts) version of the same image as the img2img init, with
  the background re-flattened to pure quiet BG outside the character mask (the round-1 duplicate-creature
  lesson: a noisy/blank background invites a second subject).
- **Generation** (new script `Tooling/ArtLab/scripts/seven/wyrm_stand_v2.py`, not committed):
  `StableDiffusionXLControlNetImg2ImgPipeline`, same base/VAE/scheduler/steps/CFG as round 1, but **much
  lower img2img strength (0.45-0.5) and a stronger, longer-held canny (conditioning 0.55-0.6, end 0.75-0.8)**
  -- low strength is what makes this a seam-blend pass rather than a re-render; InstantStyle IP-Adapter 0.4
  on the same house refs. **Prompt dropped 'wingless'** (round 1's subject) and the negative dropped `wings`
  from the IP_NEG list -- the producer wants the fin kept as a visible small wing this time, so fighting it
  in the negative would just invite the model to paint over the pasted-in fin. No Reinhard colour lock this
  round: the collage's colours are already the approved art's own pixels, truer than any swatch
  approximation.
  - **Prompt** (77 tokens): `no humans, solo, chibi, mythical creature, old, wise, wry smile, half-lidded
    eyes, ice dragon, four legs, standing, tail extended, crystal wings, pale blue, dark irises, swept back
    horns, heroic pose, bold clean lineart, soft cel shading, painterly, warm light, full body, simple
    background, masterpiece`
  - **Negative** (73 tokens): `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob,
    pastel, violet, purple, magenta, red, orange, pokemon, kyurem, glaceon, ram horns, blank eyes, fire,
    flames, multiple views, dark background, lowres, bad anatomy, text, worst quality, blurry, sitting`
  - Seeds 801-803 on stance **a** (cn 0.6, end 0.8, strength 0.45, seed 803 at strength 0.5/cn 0.55); seed
    804 on stance **b** (cn 0.6, end 0.8, strength 0.45).

## 6. Round 2: what failed / limits

- **First collage draft:** stance **b**'s longer legs ran past the bottom of the working canvas and were
  clipped. Fixed by shortening the leg length back to stance **a**'s and nudging the hip/shoulder anchor
  points up slightly.
- **Legs read visually plainer than the rest of the body.** The synthetic legs are flat-shaded capsules;
  at 0.45-0.5 strength the diffusion pass blends their silhouette into the torso cleanly but doesn't add the
  body's ice-shard faceting to them, so on close zoom the legs are a visible texture mismatch (structurally
  correct -- four, clearly apart, reaching the ground -- but less detailed). Worth a higher-strength or
  masked-only pass over just the leg region if the producer wants to polish before Meshy.
- **One eye only, by design.** Every candidate again shows a single eye in a 3/4-ish profile: `head.png` is
  the already-approved, already-locked design (`Tooling/ArtLab/provenance/frost_wyrm.md` section 3.6
  explicitly repainted a stray second eye back to skin, "only the one eye and one mouth should be shown").
  Pasting that head verbatim (as the brief asked) inherits that choice; nothing short of repainting the head
  would add a second eye, and that would reopen exactly the design-drift risk this round was trying to
  close. Prioritised design fidelity over eye-count here, since that was the producer's stated top complaint.
- No duplicate-creature or ground-line artifacts this round (both round-1 failure modes): the background
  reflattening in prep and the much lower strength avoid both.

## 7. The sheet (round 2)

`frost_wyrm_candidates_v2.png` (session scratchpad): approved sitting art + four numbered standing
candidates (seeds 801/802/803 on stance **a**, 804 on stance **b**). Individual cutouts (background removed
to transparent) at `frost_wyrm_v2_cand_1.png` through `frost_wyrm_v2_cand_4.png`, numbered to match.

| # | Seed/stance | Legs | Pose | Horns | Wings/shards | Eye |
| - | ----------- | ---- | ---- | ----- | ------------ | --- |
| 1 | 801, stance a | 4, clearly apart, reach the ground | standing, level back, tail extended behind | two distinct swept-back horns, matches the approved art (same pixels) | fin/wing and ice-shard scales preserved intact | one eye, dark blue/violet iris, matches approved colour |
| 2 | 802, stance a | 4, clearly apart | standing, level back, tail extended behind | same as #1 | same as #1 | one eye, dark blue |
| 3 | 803, stance a (strength 0.5) | 4, clearly apart | standing, level back, tail extended behind | same as #1 | same as #1 | one eye, slightly more green-blue (marginal drift from the extra 0.05 strength) |
| 4 | 804, stance b (wider) | 4, clearly apart, wider/taller stance -- the most visually distinct of the four | standing, level back, tail extended behind | same as #1 | same as #1 | one eye, dark blue |

All four: single creature, plain house cream background, no ground line, chibi proportions matching the
approved art (same head/torso pixels), two horns, ice shards, small wing/fin all present. Legs are the one
soft spot (plainer texture than the rest of the body, noted above) but are structurally correct on all four.

## 8. Next step

Producer picks one candidate (or asks for a leg-texture polish pass, or a repainted head for true two-eye
visibility, understanding the design-fidelity trade-off that implies). The pick becomes the Meshy
image-to-3D source per `Tooling/ArtLab/README.md`'s "Meshy (3D)" section. No Meshy credits were spent in
either round (candidate generation only, per the task's constraints).
