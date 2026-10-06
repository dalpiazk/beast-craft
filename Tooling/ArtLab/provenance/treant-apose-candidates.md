# Provenance: Treant A-pose candidates (for Meshy image-to-3D)

**Status:** candidates only, not yet producer-picked. The current Treant mesh came from
`content/art/source/treant/character.png` (front view, right arm bent against the chest), which fused that
arm to the body in 3D; a Meshy A-pose text attempt separately failed (crown swallowed the face, body read
as a child in a leaf skirt). This pass re-poses the *approved* 2D design into an A-pose instead, for a
fresh image-to-3D source. Tools: local only (Intel Arc 140V, XPU, bf16); no cloud service, nothing
uploaded, no Meshy credits spent (candidate generation only).

## 1. Method

Same two-step method as the Frost Wyrm standing-pose v2 retry (`Tooling/ArtLab/provenance/frost_wyrm-standing-candidates.md`
section 5): re-compose the approved art's own rig parts into the target pose, then a low/moderate-strength
ControlNet img2img blend pass to fuse seams only, so the design (bark colour, face, crown) is inherited
pixel-for-pixel rather than re-imagined.

1. **The collage** (ad hoc script `compose_treant.py`, not committed). The Treant's rig
   (`content/art/source/treant/parts/{leg_left,leg_right,body,arm_right,arm_left,head}.png`, `parts.json`)
   has a separate part per limb (unlike the Frost Wyrm's rig), so both arms could be rotated about their own
   shoulder pivots instead of being redrawn:
   - `arm_right` (bent across the chest in the approved art -- the one that fused to the body) is rotated
     ~25-55 degrees about its shoulder pivot and translated outward (`dx` +170 to +320 world px at the
     rig's native 2x-finals scale) so the forearm and hand clear the torso silhouette with visible background
     between them. **Why the translate, not just rotation:** a pure rotation about the shoulder pivot swept
     the whole rigid arm (it has its elbow bend baked into one image, not a separate joint) down the *front*
     of the trunk rather than out to the side, since the shoulder pivot sits close to the body centreline --
     caught by zooming into early test renders and confirmed by eye, not by the thumbnail. Translating the
     rotated arm outward after rotation fixed this; the upper arm/shoulder still brushes the torso's edge
     (normal for a shoulder), but the forearm and hand clear it with a real background gap.
   - `arm_left` (already reaching outward in the approved art) is rotated -15 to -35 degrees about its own
     shoulder pivot, down from its original near-horizontal reach toward the brief's "~30-45 degrees from the
     body" A-pose angle.
   - `leg_left`, `leg_right`, `body` and `head` (face + full leaf crown -- this rig has no separate crown part)
     keep their approved pixels and offsets unchanged; the legs were already straight and apart in the
     approved art, so left alone.
   - A reusable `rotate_about_pivot()` helper rotates a part image about an arbitrary pivot by first
     recentring it on a padded canvas (padded by the true corner-to-pivot distance, not separate x/y maxima --
     an early version under-padded and clipped the rotated `arm_left` at some angles, caught by a zero-rotation
     sanity render that reproduces the source exactly, then a rotation sweep that showed the clipping) and
     rotating about that canvas's centre, which stays fixed under rotation.
   - Two stance variants were built (different arm angles/translations) for genuine candidate variety.
2. **Prep + generation** (`Tooling/ArtLab/scripts/seven/treant_apose.py`, not committed, same house pipeline
   as `wyrm_stand_v2.py`): the collage is cropped/fit to the house 896x1152 canvas on the house cream BG,
   with an own-lines Canny guide and a lightly blurred (not heavily abstracted) img2img init
   (`v2_prep.py`, generalised with a beast-name argument). `StableDiffusionXLControlNetImg2ImgPipeline`,
   Animagine XL 4.0 + fp16-fix VAE, 896x1152, DPM++ 2M Karras, 26 steps, CFG 5, **img2img strength 0.48,
   canny conditioning 0.6 (end 0.8)**, InstantStyle IP-Adapter 0.4 on the house refs. No Reinhard colour
   lock (the collage's colours are already the approved art's own pixels).
   - **Prompt** (75 tokens): `no humans, solo, chibi, mythical creature, gentle, kind eyes, soft smile,
     protective, treant, arms spread down and out, bent elbows, twig hands, feet apart, bark body, round leaf
     crown, heroic pose, bold clean lineart, soft cel shading, painterly, warm light, full body, simple
     background, masterpiece`
   - **Negative** (75 tokens, the house negative for treant, unchanged -- no room left for extras):
     `flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, violet, purple,
     cyan, blue, red, pokemon, torterra, trevenant, sudowoodo, groot, human face, fire, flames, multiple
     views, dark background, lowres, bad anatomy, text, worst quality, blurry`
   - Seeds 902, 903 (stance a: arm_right +55 deg / dx 170 / dy 50, arm_left -25 deg); seed 905 (stance b:
     arm_right +25 deg / dx 320 / dy 90, arm_left -35 deg, a lower/more "down and out" arm angle).

## 2. The 3/4 view: tried, not successful enough to keep

The brief allowed reporting this as too much of a stretch, and that is the honest result. Two things were
tried:
- **Prompt + weakened canny** (seed 904: canny 0.35/end 0.5, strength 0.62, prompt adds "three quarter view"):
  the result is still essentially a front view -- looser canny let the model vary colour/texture and fingers
  slightly more, but it did not turn the head or body. A flat 2D collage carries no actual depth cue for the
  model to reinterpret as a turn; the prompt words alone were not enough to move the camera.
- **A cheap geometric "keystone" pre-warp** (`warp_34.py`, not committed): horizontally squeezing one side of
  the collage before the diffusion pass, meant to at least give the canny guide a turn-shaped silhouette to
  work from. At a visually-safe compression (16% max, smooth falloff) the effect was too subtle to read as a
  turn at all; pushing it further risks an obviously warped result rather than a convincing 3/4 angle, and
  was not pursued further under the session's time budget.
- **Why this matters less than it sounds for this beast specifically:** unlike the Frost Wyrm (whose approved
  head design deliberately shows only one eye), the Treant's approved face already shows both eyes face-on, so
  the "both eyes visible" half of the brief's view requirement is already satisfied by the plain front-view
  candidates below. The remaining concern -- the crown reading as flat-backed in the mesh -- is a genuine
  single-image-to-3D limitation (no image can show what a flat 2D front view doesn't depict), not something a
  2D pose-reference pass can fully solve; it most likely needs either a true side/back reference or whatever
  multi-view input Meshy's own pipeline supports, both outside this task's tools.

## 3. The sheet

`treant_apose_candidates.png` (session scratchpad): the approved (chest-arm) art on the left, three numbered
A-pose candidates beside it. Individual cutouts (background removed to transparent) at
`treant_apose_cand_1.png` through `treant_apose_cand_3.png`, numbered to match.

| # | Seed/stance | Arms free of body? | Elbows | Crown | Design fidelity | Notes |
| - | ----------- | ------------------- | ------ | ----- | ---------------- | ----- |
| 1 | 902, stance a | both arms clear, real background gap at both hands and at the left shoulder; the right upper arm still brushes the torso edge near the shoulder (normal/expected, not a fusion-width overlap) | both visibly bent | full, round, sits on top, doesn't cover the face | bark colour, blossom/leaf clusters on the shoulders, trunk texture, face all match the approved art closely | cleanest of the three |
| 2 | 905, stance b | same as #1, right arm hangs lower -- a clearer "30-45 degrees down and out" read | both visibly bent | same as #1 | same as #1 | best match to the brief's exact arm-angle ask |
| 3 | 903, stance a | same as #1 | both visibly bent | same as #1 | very close to approved, but a faint nose-like shape appears on the face that the approved flat/cute face doesn't have -- a small design drift | still usable; flagged for a quick check if picked |

All three: single character, plain house cream background, no ground line, front view (not 3/4 -- see
section 2), both eyes visible (inherent to this beast's face design), slender bark trunk, pale face area
with the small smile, brown bark limbs with leaf clusters on the shoulders, full round leaf crown on top of
the head, same palette and painted style as the approved art.

**Not on the sheet:** seed 904 (the 3/4-view attempt -- not rejected for drift exactly, but it didn't
achieve a turn and its looser conditioning drifted the colour grading warmer/flatter than the approved
palette, so candidates 1-3 are the stronger picks).

## 4. Next step

Producer picks one candidate (or asks for the 3/4 view to be pursued a different way, or a quick retouch of
#3's face). The pick becomes the Meshy image-to-3D source per `Tooling/ArtLab/README.md`'s "Meshy (3D)"
section. No Meshy credits were spent in this pass (candidate generation only, per the task's constraints).
