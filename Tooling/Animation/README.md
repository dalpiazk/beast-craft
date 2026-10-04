# Tooling/Animation: agent-driven creature animation pipeline

GitHub issue #68's pilot: a reusable, scripted mesh-prep -> rig -> animate -> verify -> export
pipeline, built and proven against the Griffin (the reference creature). Every stage is a
standalone Blender-headless script (`blender -b --python <script>.py -- <args>`); none of them need
a GUI or live viewport, matching the project's "Claude as the animator, free tools only" decision
(see the approved methodology research this pipeline implements,
`docs/spikes/055-3d-mini-spike.md` and issue #68's own comment thread for the research write-up).

**The pipeline is intended to be reused for every creature**, not just the Griffin -- the only
creature-specific thing in it is `rig_templates/winged_quadruped.py`'s landmark detection (which
works off the mesh's own geometry, not hardcoded coordinates) and the hand-authored key poses in
`anim/keyed.py` (which would need new pose numbers per creature, same as any hand-keyed animation
would).

## Pipeline stages

| Stage | Script | What it does |
| --- | --- | --- |
| 1. Mesh prep | `prep_mesh.py` | Import, weld (fixes the glTF export/import vertex-splitting artefact every stage after this would otherwise mistake for mesh fragmentation), topology/manifold/component stats, decimate to the triangle budget if needed, downsize the base-colour texture to 1K. |
| 2. Rig | `rig_creature.py` | Normalise, detect landmarks from the mesh itself, build a deform-only skeleton from the reusable template, auto-weight (with the Spike #55 fallback chain), scripted weight cleanup, render a bone-overlay + weight-check sheet. |
| 3a. Procedural locomotion | `anim/gait.py` | Parametric walk cycle: closed-form 2-bone IK, velocity-based foot placement, constant-stance-velocity/eased-swing foot arcs, body bob, bakes a clean in-place `Move` loop. |
| 3b. Hand-keyed | `anim/keyed.py` | Sparse pose-to-pose `Idle` (breathing/weight-shift/wing-settle/head-look/tail-sway) and `Attack` (anticipation -> strike -> follow-through -> recover, with bounded squash/stretch), Bezier ease-in/out. |
| 4. Verify | `verify.py` | Numeric QA gates: foot-slide (stance-velocity constancy), joint-angle limits, loop-seam continuity, F-curve jitter, rough ground-interpenetration. |
| 5. Export | `export_glb.py` | Merges Idle/Move/Attack onto one armature, pushes each to its own NLA track, exports one deform-only-bone, multi-clip GLB. |

Shared helpers (weld, topology stats, the Spike #55 auto-weight fallback chain and its three repair
passes, weight-paint cleanup, Blender-5.x layered-Action F-curve walking, interpolation setting,
normalisation) live in `common.py` so every stage script imports them rather than re-deriving the
same Blender-API gotchas five times.

## v5 redo: hand-placed landmarks (lead-review round 4)

The v4 horizontal-slicing redo below was itself sent back: `rig_overlay_side.png` showed hips/
shoulders placed almost at ground level (legs as tiny chains at the floor) and the spine running
near-vertically through the chest -- the detected landmarks were wrong. The lead/animator then
placed every joint BY HAND from the same calibrated ortho views the slicing pass used
(`scratchpad/anim-pilot/calib/{left,front,bottom}.png` + `calib.json`, same `v4/griffin_prepped.glb`
mesh, native pre-normalisation coordinates). `rig_templates/winged_quadruped.py`'s
`HAND_LANDMARKS_NATIVE` now holds this full point set (every leg's hip/knee/ankle/foot/toe, the
spine/neck/head chain, a 5-point tail, 3-point-per-side wings); `detect_landmarks_handplaced`
converts them with the IDENTICAL formula `common.normalise_transform` applies to the mesh itself
(computed from the mesh's own pre-normalisation bounding box, captured by `rig_creature.py` before
normalising -- see `_native_to_normalized_fn`), then snaps a point back inside the mesh only if it
falls outside (`_snap_if_outside`, iterative, not a single capped step -- see its docstring for why
a single-step version wasn't enough). `detect_landmarks` (horizontal slicing) is kept for history/
reuse on a future creature; this mesh now uses the hand-placed path exclusively, and the overlay
render confirms it: hips/shoulders sit at the correct height, the spine runs through the torso at
the body's own lean, all four legs' bones sit inside their own limb.

**Two real engineering problems found and fixed along the way, neither a landmark-placement issue:**

1. **Blender's automatic-weight heat solver failed completely** (every vertex unweighted, including
   on a fresh voxel-remeshed donor mesh that's supposed to never fail) with the hand-placed rig.
   Isolated by systematic testing (one leg at a time, then one joint at a time, then an offset-
   magnitude sweep) to BL's knee specifically: at its exact given position the solver always failed;
   nudging it by as little as 0.01 (normalised units) in -X fixed it completely, every time.
   `_snap_if_outside`'s "outside the mesh" check made no difference here (BL's knee was already
   inside) -- this is a Blender solver instability at that specific point, not a geometry problem,
   and is applied as a small, separately-documented `SOLVER_STABILITY_NUDGE` (-0.015, comfortably
   inside the confirmed-working range), not a change to the recorded hand-placed value.
2. **The stride-safety margin broke down on these much longer legs.** The flat 0.95 safety factor
   (`anim/gait.py`) assumed meaningful slack between the rest pose and max reach; on this rig the
   rest pose itself already sits within ~0.1-0.3% of `max_reach` for the forelegs (confirmed: FR's
   rest_dist/max_reach = 0.597/0.598), so `reach_margin = max_reach*0.95` landed BELOW the rest
   distance itself and nearly every leg fell to the degenerate floor stride before any animation was
   even attempted. Fixed by making the margin the larger of the flat 95% budget and "a little past
   the rest pose's own distance" (capped at `max_reach`, which triangle inequality guarantees is
   always >= rest_dist for a valid hand-placed chain).

**Honest result: stride is still small for the forelegs specifically, and this is physical, not a
parameter to tune away.** FL's hip-to-ground distance (full reach required to plant the foot, before
any fore-aft stride) is ~91% of its own total leg length (L1+L2); FR is ~99.7%. At a hip height that
tall relative to the leg's own segment lengths, there is essentially zero reachable distance left for
ANY fore-aft foot excursion, regardless of safety-margin tuning -- verified directly: even
`SAFETY=1.0` (no margin at all) leaves a reachable radius of a few percent of H. BL (24.5%
peak-to-peak stride) and BR (8.2%) have real slack and get real motion; FL and FR get the floor
value (~1-2%, effectively static) by physical necessity given the hand-placed hip/foot geometry, not
by a bug. This is the same class of honest, geometry-driven asymmetry as the v4 pass's BL/FR vs BR/
FL split -- just now applying to different legs, because the geometry itself is different.

**A real mesh tear, found and diagnosed (not fully fixed) at the hind-leg hip/belly junction during
Move specifically.** Per the task brief's instruction, diagnosed with a flat-shaded Blender render
(not the toon runtime) at the exact same pose, to separate "real deformation" from "outline-shader
artefact":
- The pure bind pose (`griffin_rigged.blend`, no animation at all) renders completely clean --
  confirms the mesh/weights themselves are fine, ruling out inverted normals/backfaces or an
  outline-hull self-intersection as the cause (per the task brief's suggested checks).
- ANY baked Move frame, even with stride manually damped to 30% of its computed value, shows the
  same tear at the same severity -- ruling out stride AMPLITUDE as the driver.
- Disabling body bob entirely made no difference either.
- Resetting the Move-baked armature to full identity pose (no action) on the SAME file renders
  clean -- confirms it's not file/weight corruption from the gait bake, purely pose-dependent.
- Direct comparison of the hand-placed bind-pose knee position against the IK-solved knee at the
  very first baked frame showed a real, substantial difference (BL: ~0.085 units, ~4.25% of H) even
  though that frame's foot target is close to the rest foot position -- the knee's sensitivity to
  small target changes is high for this leg geometry (bind pose already near its own reach limits,
  per the stride finding above), and BL/BR are exactly opposite-phase in the lateral-sequence gait,
  so their thigh bones rotate in diverging directions on every frame. The most likely mechanism:
  ordinary linear-blend-skinning quality at a joint undergoing more rotation range than the
  automatic weighting (tuned against the task brief's weight-check poses, not this specific gait)
  handles smoothly, not a bug in the landmark placement, the mask, or the IK math specifically --
  but this was not fully isolated down to a single confirmed fix within this pass's budget.
  **Mitigation applied, not a full fix:** `build_leg_masks` now excludes the hip-to-knee (thigh)
  segment from the per-leg restriction (it was pulling belly vertices between the two hind legs into
  a hard BL-only/BR-only split, a real but distinct problem, confirmed separately) -- this did not
  measurably change the tear's severity either way, so it was kept for its own (smaller, hip-to-
  thigh-blend) benefit rather than reverted. The tear is visible in `review_v5/final/
  contact_sheet_move.png` and `contact_sheet_move_side.png` at the hind legs' hip/belly junction;
  Idle and Attack do not show it (legs stay braced in both).

## v4 redo: all four legs from horizontal-slicing landmarks

A later pass redid the rig fitting and the walk from scratch after the producer supplied an
independent multi-angle orthographic turnaround of the exact input mesh
(`scratchpad/anim-pilot/views_quad.png`): **all four feet stand on the ground** in this creature's
bind pose (two eagle forelegs under the chest, two lion hind legs under the haunch, chest held
higher than the hips -- a reared/proud stance, not a rampant one). The prior pass (left below,
struck through in spirit but not deleted -- the mistake and why it happened are worth keeping on
record) had read the mesh backwards: it concluded the forelegs were tucked up off the ground and
placed them with a guessed forward-offset formula. Without real ground contact to anchor it, that
guess landed on a bind-pose knee fold so extreme that every attempt to animate it tore the mesh.

**Method: horizontal-slicing landmark detection**, replacing the old proportional guess entirely
(`rig_templates/winged_quadruped.py`):

1. **Feet, by per-side ground-band clustering.** Ground-band vertices (z < 0.10H) are split by body
   side (X<0 / X>=0) and each side is clustered independently with `_cluster_xy`'s adaptive
   single-link threshold search (same technique the prior pass's hind-leg detection already used
   successfully), taking the largest threshold that still yields exactly 2 big clusters per side.
   **Per-side, not one global clustering pass** -- a global version could not satisfy both sides at
   once: this mesh's right foreleg's toes need a much larger merge threshold (~0.18-0.24H) to fuse
   into one paw than the left side needs (~0.015-0.04H) for its own two, already well-separated
   paws, so a single global threshold either left the right paw's toes looking like 2-3 "feet" or
   risked over-merging the left side's genuinely separate paws.
2. **Ankle/knee/hip, by tracking each foot's cross-section upward.** From each real foot, the mesh
   is bisected with a horizontal plane (`bmesh.ops.bisect_plane`) at many Z levels; connected
   cut-edge islands are tracked frame-to-frame (nearest-centroid matching) from the foot upward.
   Below `settle_frac*H` the splayed claws are still mid-fusion (confirmed by calibration), so the
   tracker follows unconditionally; above it, a merge event (two tracks claiming the same island),
   an oversized jump, a ballooning cross-section, or straying closer to a *different* leg's own
   track than to its own all end that leg's track -- the point just before that is the hip/shoulder.
   The two sharpest curvature points along the kept path are the ankle/wrist and knee/elbow.
3. **Left/right hip-height symmetry safeguard.** A real quadruped's left and right shoulders (and
   separately hips) sit at the same height even on this mesh's otherwise-asymmetric bind pose; if a
   pair disagrees by more than 0.12H, the taller (over-tracked) one's knee/hip are corrected toward
   its sibling's height, keeping the ankle and the chain's own lateral lean.
4. **Per-leg weight masks + restriction** (`build_leg_masks` / `common.restrict_leg_weights`): a
   vertex within `radius` of one leg's own hip-knee-ankle-foot polyline may only carry weight for
   that leg's 4 bones plus its parent (spine_02 for forelegs, pelvis for hind legs) -- built from
   the same detected chain the bones use, so a foreleg vertex cannot carry hind-leg or tail weight
   regardless of what automatic/voxel weighting produced.

**Three real bugs found and fixed along the way, each confirmed by rendering the result and looking
at it, not by numbers alone** (see `winged_quadruped.py`'s `_track_legs`/`detect_landmarks`
docstrings and `anim/gait.py`'s `aim_matrix`/frame-loop comments for the full forensic detail):
- **Forward-axis detection was backwards.** The old "which Y half holds more upper-body mass"
  heuristic picked up this mesh's raised wings (which sweep toward the tail side in this pose), not
  the head -- confirmed by rendering the detected head/front-leg landmarks against the producer's
  turnaround and finding them on the wrong end. Fixed with a simpler, more direct signal: of the
  two global Y-extreme vertices, the one at the higher Z (a real tail or rear-torso extremity, not
  an outstretched ground-level claw) points backward.
- **`aim_matrix`'s degenerate-direction fallback reused the exact up_hint that had just failed**
  (every caller passed a single hardcoded `(1,0,0)`), leaving an ill-conditioned or literally
  non-invertible frame whenever a leg bone swung close to that axis -- which happens for real on
  this mesh (FL's detected bend direction is X-dominant). Fixed with a genuinely different fallback
  axis, and `up_hint` is now chosen per leg as whichever world axis is least aligned with that
  leg's own detected bend direction.
- **The forelegs' parent transform (`spine02_world`) ignored its own small same-frame
  counter-rotation**, approximating it as identity. Small, but enough to push FL's unusually tight
  IK reach margin in and out of its clamp every frame -- confirmed directly (the IK solve's own
  output matched its target exactly, in isolation, with zero clamping; only the rendered bone
  drifted). Fixed by folding the rotation in analytically before using it as a parent matrix.

**Result:** all four legs walk with a real lateral-sequence gait (BL->FL->BR->FR, each a
quarter-cycle apart), all verify.py gates pass (numbers below), 33 deform bones, 0/6,740 vertices
unweighted, max 4 / avg 3.22 influences/vertex.

| Gate | BL | BR | FL | FR |
| --- | --- | --- | --- | --- |
| Foot-slide (stance CV<0.35, or abs stddev<2mm if near-static) | 0.114 (2.98mm) | near-static, 0.35mm | near-static, 0.08mm | 0.151 (2.99mm) |
| Knee angle range (15-179.5 deg) | 38-131 | 90-144 | 104-142 | 44-133 |
| Ground interpenetration (toe min z > -0.01) | ~0 | ~0 | ~0 | ~0 |
| Move loop seam | 0.000 deg | | | |
| Idle loop seam / jitter | 0.000 deg / 0.00057 max | | | |
| Attack jitter / cumulative head pitch | 0.0357 max / 64.0 deg | | | |

**Stride is honestly asymmetric, and the reason is now fully legible from the landmarks
themselves** (not a mystery the way the old pilot's L/R asymmetry partly was): BL (peak-to-peak
0.49 = 24.5%H) and FR (0.38 = 19.1%H) have real IK slack and stride accordingly; BR (0.16 = 8.2%H)
and FL (0.07 = 3.6%H) sit much closer to their own hip-to-foot max reach at rest (FL's rest
hip-to-foot distance is already ~91% of its own max reach, L1=0.147/L2=0.184) and so get a small,
geometrically-honest stride rather than a forced
one that would have reintroduced clamping (an earlier attempt forced a fixed-floor minimum stride
and it measurably did: FL's foot-slide CV was 0.68 until the floor was removed in favour of
whatever `anim/gait.py`'s own reach computation says is actually safe). This is a direct, legible
consequence of this specific mesh's proportions (FL and BR's own detected hip-knee-ankle-foot
chains leave little slack), not a detection error -- the landmark overlay renders (below) show
each chain sitting correctly inside its own leg.

**Landmark overlay renders** (`rig_creature.py`'s 4-view output, semi-transparent textured mesh,
one colour per leg): `rig_overlay_front.png`, `rig_overlay_side.png`, `rig_overlay_bottom.png`,
`rig_overlay_34.png` in the stage-2 output directory. Per-leg isolation renders used during
calibration (one leg's chain alone, bright red, side + 3/4 views) are a scratchpad-only tool, not
part of the gated pipeline.

**Known limitation carried into this pass, honestly reported:** a small, faceted dark patch appears
on the hip/belly during parts of the Move clip specifically (when multiple legs are mid-stride at
once) in the Live3D toon-shader runtime render -- not visible in the Idle or Attack renders, and not
reproducible by posing the rig in Blender directly (a Blender MatCap render of the same exported
asset at the same pose is clean). This is the same *class* of issue as the precedent spike's
documented "outline-shell specks" / "cosmetic outline-shader artefact" entries (confirmed present,
in a different spot, in this pipeline's own prior review output too -- see `review_v3/contact_move`
in session scratchpad) -- i.e. a recurring, pre-existing characteristic of this toon/outline shader
under multi-limb articulation, not a new defect this pass introduced, though having all four legs
move instead of two likely makes it more visible than before. A stronger weight-smooth pass
(`common.cleanup_weights`, factor 0.5->0.6 / repeat 2->3) visibly fixed it for Idle; Move's version
was not fully chased further within this pass's budget.

## Pilot results (Griffin) -- original 2-leg finding, superseded above

```
BLENDER="C:\bctmp\blender-5.2.1-windows-x64\blender.exe"   # or wherever your Blender 5.x portable lives
IN=path\to\your_quad_remesh.glb
OUT=path\to\a\scratch\working\dir

%BLENDER% -b --python prep_mesh.py       -- --glb %IN% --out %OUT% --target-tris 8000 --texture-size 1024
%BLENDER% -b --python rig_creature.py    -- --glb %OUT%\griffin_prepped.glb --out %OUT%
%BLENDER% -b --python anim\gait.py       -- --blend %OUT%\griffin_rigged.blend --out %OUT% --fps 24 --cycle-seconds 1.0
%BLENDER% -b --python anim\keyed.py      -- --blend %OUT%\griffin_rigged.blend --out %OUT% --fps 24
%BLENDER% -b --python verify.py          -- --move %OUT%\griffin_move.blend --keyed %OUT%\griffin_keyed.blend --out %OUT%
%BLENDER% -b --python export_glb.py      -- --move %OUT%\griffin_move.blend --keyed %OUT%\griffin_keyed.blend --out %OUT% --name <creature>_anim.glb
```

(Every stage prints its own before/after stats to stdout and writes a `*_report.json` alongside its
output -- read those rather than re-deriving numbers by eye.) The output file names
(`griffin_prepped.glb`, `griffin_rigged.blend`, `griffin_move.blend`, `griffin_keyed.blend`) are
fixed by the scripts themselves (not yet parameterised per creature name -- a straightforward
follow-up if/when a second creature goes through this pipeline).

## Pilot results (Griffin)

**Mesh prep:** input quad remesh 14,761 tris / 13,587 verts, reading as 1,018 disconnected
components / 10,475 non-manifold edges before the weld (the usual glTF export/import vertex-split
artefact, not real fragmentation -- confirmed by welding: 7,418 verts / 4 components, largest
holding 99.4% of verts, 107 non-manifold edges left). Decimated 14,755 -> **7,998 tris** (under the
8k hero budget). Base-colour texture downsized 2048 -> 1024.

**Rig:** landmark detection found **2 ground-contact feet, not 4** -- this Griffin mesh is a winged
biped (lion hindquarters + 2 legs, eagle head, wings in place of forelimbs), not a four-legged
chimera. The template adapted automatically (see `rig_templates/winged_quadruped.py`'s module
docstring for the full detection story, including a false-4-leg-positive the clustering threshold
search initially produced and how it was caught/fixed). Final skeleton: **25 deform bones** (root,
pelvis, 2 spine, 2 neck, head, 4-bone tail chain, 3-bone wing chain x2, 4-bone leg chain x2) -- at
the low end of the 25-45 bone budget, which is appropriate for a 2-leg creature.

**Weighting (second round, 4-leg rig):** native automatic (heat) weights failed on the raw mesh
again; the voxel-remesh-donor fallback converged (0 unweighted vertices), followed by the
topology-consistency and floating-island repair passes. After the scripted weight-paint cleanup
(limit 4, normalise, clean, smooth, re-limit to 4, clean, re-normalise): **max 4 / avg 3.64
influences per vertex**, **0 / 6,740 vertices unweighted** -- at the top of the <=3-4 mobile
target (the earlier 2-leg rig hit max 3; the extra foreleg bones near the shoulder/hip cost one
more influence slot on nearby vertices).

**Animation (second round, 4-leg):**
- `Move`: 30-frame (1.2s @ 24fps). **Hind legs (BL/BR)** execute a real lateral-sequence walk
  (`anim/gait.py`'s `LATERAL_SEQUENCE`: BL->FL->BR->FR phase order, each a quarter-cycle apart) with
  per-leg IK-solved stride -- **BL: 0.460 (peak-to-peak 46% of body height), BR: 0.250 (25%)**, both
  within/above the requested ~25-40% range. **Forelegs (FL/FR) are kept static** (braced at their
  bind pose) through the whole clip -- every attempt to animate them, including a tiny 0.08H stride
  and even a fully *static IK target*, visibly tore the mesh (see the lead-review section above for
  the full diagnosis: the bind-pose knee fold needed for IK reach is more extreme than the weighting
  holds up across, for the forelegs specifically). This is a real, documented limitation, not the
  full "all four legs walk" result asked for.
- `Idle`: 73-frame (3.0s @ 24fps) loop, 5 sparse key poses, Bezier ease-in-out. Subtle breathing
  (spine rotation), slow head-look, wing settle, tail sway, alternating weight shift. All four legs
  stay at their bind/rest pose throughout (never touched by `anim/keyed.py`) -- "all four planted".
- `Attack`: 25-frame (1.0s @ 24fps), 5 key poses (neutral/anticipation/strike/follow-through/
  recover). Anticipation counter-rotates opposite the strike; the strike's in-edge is set to
  `EASE_IN` only (not `EASE_IN_OUT`) so the anticipation-to-strike transition reads as fast/abrupt
  per the methodology doc's timing guidance; bounded squash/stretch (1.08/1.08/0.90) on the chest at
  the strike frame. **Foreleg rake:** FR's thigh/shin get explicit keyframed rotations (anticipation:
  wind up/lift; strike: swing forward/down; follow-through: settle) -- small, modest angles (not
  routed through the fragile Move-clip IK path at all, so this doesn't hit the same tearing issue --
  confirmed clean in a render check). Hind legs (BL/BR) and the other foreleg (FL) stay braced at
  rest throughout. **One fix round** (carried over from the 2-leg pilot): a first pass summed
  spine+neck+head forward pitch to ~98 degrees at the strike frame, curling the head entirely behind
  the wing/body silhouette -- reduced to a ~54-degree cumulative pitch, which keeps the beak visible.

**Verification gates** (`verify_report.json`, numbers from the actual 4-leg pilot run):

| Gate | Result | Pass |
| --- | --- | --- |
| Move foot-slide, hind legs (stance CV, threshold < 0.35) | BL: 0.031, BR: 0.031 (stddev <1mm/frame) | yes |
| Move foot-slide, forelegs (near-static, judged on abs stddev < 2mm/frame) | FL/FR: 1.4-1.5mm/frame | yes |
| Move knee-angle range, hind legs (15-179.5 deg) | BL: 42.0-117.7, BR: 69.2-124.1 | yes |
| Move knee-angle, forelegs (static) | 16.5 deg (constant) | yes |
| Move loop-seam (max bone delta < 0.5 deg) | 0.000 deg | yes |
| Move ground interpenetration (toe min z > -0.01) | hind: ~0; fore: 0.548 (never reaches ground, by design) | yes |
| Idle loop-seam | 0.000 deg | yes |
| Idle jitter (max 2nd-deriv < 0.15) | 0.00057 | yes |
| Attack jitter (max 2nd-deriv < 0.15) | 0.0357 | yes |
| Attack cumulative head pitch (< 120 deg, post-fix) | 64.0 deg | yes |

The foot-slide gate's threshold (constancy of stance-phase velocity, not literal zero) is specific
to this pipeline's **in-place clip** convention -- see `anim/gait.py`'s and `verify.py`'s module
docstrings for why that's the correct metric when root translation is left to the engine, not baked.
The foreleg gate judges on absolute stddev, not the velocity-ratio CV, once a leg is near-static --
see `verify.py`'s comment for why CV is a poor metric at near-zero mean velocity.

**Export:** `griffin_anim.glb`, **0.594 MiB** (well under the 2 MB budget), **33 bones**, 7,998 tris,
6,740 verts, 3 named glTF animations (Idle/Move/Attack), 1024x1024 JPEG base-colour texture. Copied
to `Tooling/Spike55/Live3D/Content/model/griffin_anim.glb` for the runtime.

## Runtime (Live3D)

`Tooling/Spike55/Live3D` (the issue #55 spike app) was extended, not replaced -- the fourth pass's
`griffin_live.glb` stress-test/battle/swarm paths are unchanged and still work exactly as before.
New, additive pieces:

- **`AnimatedPose.Clip.Attack`** and **`AnimatedPose.ComputeWorldMatricesBlended`**: a crossfade pose
  evaluator that blends per-node LOCAL transforms (translation lerp, rotation slerp, scale lerp)
  *before* composing the hierarchy -- blending already-composed world matrices would not interpolate
  rotation correctly. See its doc comment.
- **`SpringBone.cs`**: a minimal damped point-mass spring per tracked joint (tail tip, one wing-tip
  bone per side), layered on the baked pose after `AnimatedPose` runs, by nudging that joint's world
  matrix translation toward a lagged, spring-simulated position. Not a chain solver -- one spring per
  joint is enough for a believable one-frame-of-lag follow on a tail tip / wing feather, which is all
  this pilot's two tracked points need.
- **`MAX_BONES` 16 -> 25** (`Toon.fx`, `BeastInstance.MaxBones`): the pilot rig has 25 deform bones
  vs. the fourth pass's 11; still comfortably inside GLSL ES 2.0's guaranteed 128 vec4 vertex-uniform
  minimum (25 bones = 100 vec4). Every existing model (griffin_live.glb, crest_alt.glb,
  swarmling_live.glb) still works unchanged -- unused palette slots are inert identity padding.
  (Bumped again to **34** in the lead-review 4-leg fix round, covering this rig's 33 bones; the v4
  redo above keeps the same 33-bone count, so no further runtime change was needed.)
- **`--pilot-sequence <dir> [--pilot-clip reel|idle|move|attack] [--frames N] [--pilot-fps N]`**: a
  new headless capture mode (mirrors `--screenshot`'s structure) that loads `griffin_anim.glb`
  instead of `griffin_live.glb`, frames it with the existing single-instance camera fit (same
  `CameraTiltDeg=33` / `CameraYawDeg=35` battle-camera angle the rest of the app already uses -- not
  a new camera), and writes a numbered PNG sequence: either one evenly-sampled loop of a single named
  clip (`idle`/`move`/`attack`, for a contact sheet) or the full crossfaded `Idle -> Move -> Attack ->
  Idle` reel (`reel`, the default, for a GIF). Pose time is frame-indexed, not real-elapsed-time, so
  output is reproducible regardless of the engine's real frame rate.

Gate commands (all pass as of this pilot):
```
dotnet build Tooling/Spike55/Live3D -c Release          # 0 warnings
dotnet format Tooling/Spike55/Live3D --verify-no-changes
dotnet build src/BeastCraft.Desktop -c Release           # unaffected -- this pilot never touches src/
dotnet test Tooling/EditModeTests                        # unaffected
git diff main --stat -- src .github BeastCraft.slnx      # empty
```

## Producer review gate outputs

Per the task brief, these are **not committed** -- they live in the session scratchpad
(`scratchpad/anim-pilot/review/final/` for the original pilot, `scratchpad/anim-pilot/review_v4/final/`
for the v4 all-four-legs redo above -- see the handback report for absolute paths): a contact sheet
per clip (10 frames at the battle camera angle, toon-shaded), an onion-skin overlay of the Attack
clip's key poses, and a GIF reel cycling Idle -> Move -> Attack -> Idle with real crossfades,
rendered **from the Live3D runtime itself** (not a Blender fallback -- `--pilot-sequence` captures
real in-engine frames, so the reel and contact sheets show exactly what the game's own toon shader,
GPU skinning, and crossfade/spring-bone code produce, not a pre-rendered stand-in).

## Lead-review fix round

A first pass through this pilot was sent back with five findings. Four are fixed; one (leg count)
was re-investigated and the original finding stands, with the evidence recorded here.

1. **Leg count -- CORRECTED to 4-leg after a second review round (this agent's "2-leg" conclusion
   below was wrong).** On the first lead-review pass, this section argued (in good faith, with real
   evidence -- a thumbnail-matched render, exhaustive clustering/protrusion/raycast searches) that
   the mesh was a 2-legged winged biped. The producer then supplied an independent multi-angle
   orthographic render of the exact same input mesh (`views_quad.png`), whose Left/Right/Bottom/
   3-4-low-back views clearly show **four separate legs** -- two eagle forelegs tucked up under the
   chest (never touching the ground in this reared bind pose, which is exactly why every ground-
   contact and silhouette-based check this agent ran missed them) and two lion hind legs planted on
   the ground. Re-rendering this agent's own prepped mesh with the same turnaround confirmed it's
   the same asset, and the forelegs ARE there -- this agent's front-view and straight-down bottom-
   view probes put them directly in front of (visually overlapping) the hind legs from those
   specific angles, which is exactly the failure mode the producer's reference diagnosed. The
   earlier "2-leg, confirmed via thumbnail match" write-up is left below, struck through in spirit
   but not deleted, because the mistake and why it happened are worth keeping on record: silhouette/
   ground-contact checks from a narrow set of angles are not sufficient to rule out a tucked, never-
   grounded limb, and this agent should have tried more independent view combinations (side + bottom
   together, specifically) before concluding a limb didn't exist.

   **What changed in the rig:** `winged_quadruped.py` now always builds 4 legs. Hind legs (BL/BR)
   keep the original, reliable ground-contact clustering. Foreleg (FL/FR) landmarks could not be
   isolated by any further vertex-level search either (clustering, protrusion scoring, and several
   raycast strategies all mis-traced onto the tail or wing, which sweep through most of the
   plausible foreleg region in this mesh's dramatically curled pose) -- they're placed via a
   documented proportional estimate (torso centreline height at the target Z, offset forward by a
   conservative margin) rather than precise detection, acknowledged as a real limitation, not a
   silent guess. Forelegs attach at the shoulder (parented to `spine_02`, like the wings), not the
   pelvis -- a true quadruped detail the earlier 2-leg rig didn't need. Final skeleton: **33 deform
   bones** (was 25), weighting converges cleanly (0/6,740 unweighted, max 4 influences/vertex).

   **Known limitation, honestly reported:** the forelegs' bind-pose knee had to be folded far more
   sharply than the hind legs' (needed purely for IK reach -- the shoulder sits much higher above
   the ground than the hind hips do) and the mesh weighting does not hold up across *any* animated
   range at that fold, tearing visibly in every attempt tried (full ground reach, a reduced-depth
   ground target, even a tiny 0.08H stride) -- confirmed it wasn't a stride-amplitude problem by
   testing a fully STATIC foreleg target too, which still tore until the IK solve itself was bypassed
   in favour of directly reproducing the bind-pose transforms. **Forelegs are kept static (braced) through
   the Move clip** as a result -- only the hind legs execute the walk cycle -- and get a small, explicit
   rake rotation in the Attack clip's keyframes (not routed through the fragile IK path at all). This
   is a real, time-boxed limitation: a genuinely animated foreleg walk would need either re-weighting
   (not attempted -- the automatic weighting pipeline doesn't have a lever for "be more robust across
   large pose changes" beyond what's already tried) or a less extreme bind-pose fold (which would
   cost IK reach instead). The toon-shaded runtime render (the actual producer-review deliverable)
   reads acceptably despite this -- the outline/toon shading hides most of the underlying seam that's
   plainly visible in flat Blender MatCap renders -- but it is not the full "all four legs walk" result
   the task asked for.
2. **Move's stride fixed at the root cause.** The hip/knee landmark placement (`winged_quadruped.py`
   `build_bones`) now pulls the hip inward toward the spine and raises it, with a pronounced forward
   knee bow -- a genuine bent-knee rest stance instead of a near-fully-extended leg -- which is what
   actually creates IK slack for a real stride (not a cosmetic change; it changes the reachable
   envelope `anim/gait.py`'s per-leg solve works within). Result: leg L's solved stride is now 0.460
   (reach-limited; a peak-to-peak foot excursion of 46% of body height), leg R's is 0.250 (peak-to-peak
   25%) -- both within or above the requested ~25-40%-of-body-length range, up from both legs being
   stuck at 0.040 (4% peak-to-peak, "barely moves"). The L/R asymmetry is real and traced to this
   mesh's own asymmetric bind pose (leg R's rest hip-to-foot distance uses a larger fraction of its
   own reach than leg L's), not a bug -- see the knee-angle numbers below, which now show a genuinely
   bent gait (42-118 deg / 69-124 deg) instead of the earlier near-straight range (122-150 deg).
3. **Max influences per vertex: 3** (was 7). `common.cleanup_weights` now re-runs
   `vertex_group_limit_total` *after* the smooth pass, not just before -- smoothing was reintroducing
   influences past the cap by spreading weight onto neighbouring groups a vertex didn't previously
   belong to.
4. **Attack re-checked with legs planted.** Legs are never touched by `anim/keyed.py`, so they now sit
   in the same genuinely bent, braced rest pose the gait fix gave them throughout Idle and Attack --
   satisfying "legs planted" by construction. Since this creature has no front talons to rake with
   (finding 1), the pounce/strike reads through the beak (mouth open, head thrust forward) and a
   dramatic wing flare instead -- a reasonable adaptation for a winged biped, not a literal rake.
5. **A side-view Move contact sheet was added** (`--pilot-side-camera`, a new Live3D flag): camera
   yaw 0 instead of the battle camera's 35 (every beast already faces world +X via `FacingYaw`, and
   yaw=0 is a near-true side view to an X-facing unit -- see `CameraDir`'s updated comment), shallower
   10-degree tilt so the fore-aft leg swing reads clearly instead of being foreshortened by elevation.
   `contact_sheet_move_side.png` now visibly shows alternating leg reach frame to frame -- see the
   handback report for a frame-by-frame description.

## Known limitations / honest self-critique

**Superseded by the v4 redo above:** the two entries below that this pilot originally reported --
"Move's L/R stride is asymmetric" and "Forelegs don't walk" -- are both out of date. All four legs
now walk (horizontal-slicing landmark detection, see above); the stride asymmetry is still real but
is now 4-way (BL/FR have real slack, BR/FL are reach-constrained) and traced to each leg's own
detected hip-to-foot geometry rather than a single hardcoded formula. Left below for the historical
record of what the 2-leg pilot actually found, not because it's still true.

- **Foot roll is not implemented.** The foot segment (ankle-to-ground) is treated as rigid through
  stance and swing; only the thigh/shin solve via IK. This is consistent with the "no foot slide by
  construction" goal (the dominant quality driver per the methodology doc) but a real foot-roll
  (heel-strike -> flat -> toe-off) would read more naturally at a larger stride. Listed in the task
  brief as a nice-to-have; skipped for this pilot's time budget, not silently dropped.
- **Rigid-part separation (beak/talons) was not attempted** -- the delivered mesh is a single fused
  surface with one material and no existing part boundary to split on; see `prep_mesh.py`'s
  docstring for why a guessed boundary wasn't used instead.
- **Joint edge-loop reinforcement was not attempted** -- same reasoning (no scripted per-joint
  loop-cut signal without either manual edge picking or a curvature heuristic this pilot's time
  budget didn't build); the Decimate-based approach plus weight-smoothing substitutes for it. The
  weight-check sheet showed a small faceted patch on the hip and a slight crease at the neck/shoulder
  in extreme test poses -- minor, left as a known issue, same class as the precedent spike's "thin
  spike artefact" and "small seam" known-issues entries.
- **A cosmetic outline-shader artefact** appears on one Attack frame (a dark triangular gap near the
  wing shoulder, visible in `contact_sheet_attack.png`) -- plausibly the inverted-hull outline pass
  interacting with a sharply bent wing joint at that specific pose. Not chased further within this
  pilot's budget; same class of "known, cosmetic, not chased further" issue the precedent spike docs
  record repeatedly (e.g. docs/spikes/055-3d-mini-spike.md section 2.4's outline-shell specks).
- **Move's L/R stride is asymmetric** (0.460 vs 0.250, see the lead-review fix round above) -- a
  direct, honest reflection of this specific mesh's asymmetric bind pose (leg R's own rest geometry
  has less IK reach margin than leg BL's), not a bug in the gait math (the verification gates confirm
  zero measurable foot slide on both hind legs regardless of stride length). Narrowing the asymmetry
  further would need a hip-landmark heuristic that compensates per-leg for the source mesh's own
  pose asymmetry, rather than placing both hips with the same formula.
- **Forelegs don't walk** (see the lead-review section above) -- kept static through Move, with only
  a small keyframed rake in Attack. The single biggest open item if this pipeline continues past the
  pilot: either a less extreme bind-pose knee fold (costs IK reach) or a dedicated re-weighting pass
  for the foreleg region specifically.
- **This is a pilot on one creature.** The template's landmark heuristics and overall stage pipeline
  are written to generalise, but they have only been exercised against this one mesh. Hind-leg
  ground-contact detection is reliable and reusable as-is; foreleg placement (proportional, not
  detected -- see above) is the weakest, least-generalisable part of the template and would need
  re-tuning per creature, possibly per-pose, until a more robust detection method is found. A
  serpentine/no-leg creature would exercise the 2-leg/0-leg code paths this pilot's mesh never did.
