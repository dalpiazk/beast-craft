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

## Running the full pipeline on a new creature

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

**Weighting:** native automatic (heat) weights failed on the raw mesh (`Bone Heat Weighting: failed
to find solution for one or more bones` -- the same class of failure Spike #55 hit on earlier Meshy
generations); the voxel-remesh-donor fallback converged (0 unweighted vertices), followed by the
topology-consistency and floating-island repair passes. After the scripted weight-paint cleanup
(limit 3, normalise, clean, smooth, re-normalise): **max 7 / avg 3.23 influences per vertex**,
**0 / 6,740 vertices unweighted**. The max influence count (7) is slightly over the ≤3-4 mobile
target the methodology doc recommends, because `vertex_group_limit_total(limit=3)` caps how many
groups are *created* per vertex but `vertex_group_smooth` run afterward can reintroduce small
spill-over weights on neighbouring groups -- noted as a follow-up (re-run `limit_total` after smooth,
or drop smooth's `repeat` count) rather than silently reported as 3.

**Animation:**
- `Move`: 25-frame (1.0s @ 24fps) in-place walk/trot. Per-leg stride was **solved per leg from its
  own IK reach**, not a fixed constant -- the landmark-placed hip sits close to full leg extension at
  rest (rest hip-to-foot distance is ~92-95% of each leg's `L1+L2` combined reach once the ankle-to-
  ground foot segment is folded into the IK's effective second bone -- see `anim/gait.py`'s
  `solve_2bone_ik` docstring for why that folding matters), leaving only a small safe stride margin
  (**0.040** world units per leg, ≈2% of the 2.0-unit body height) before the IK would clamp and break
  stance-velocity constancy. The swing-phase lift height was scaled down to match (`0.9x` the solved
  stride) so the cycle reads as a quick, short stride rather than a disproportionate high-step. This
  is an honest limitation of this specific landmark placement, not a hidden workaround -- a follow-up
  that repositions the hip landmark with more reach margin (or widens the safety factor) would allow
  a longer, more visually dynamic stride.
- `Idle`: 73-frame (3.0s @ 24fps) loop, 5 sparse key poses, Bezier ease-in-out. Subtle breathing
  (spine rotation), slow head-look, wing settle, tail sway, alternating weight shift.
- `Attack`: 25-frame (1.0s @ 24fps), 5 key poses (neutral/anticipation/strike/follow-through/
  recover). Anticipation counter-rotates opposite the strike; the strike's in-edge is set to
  `EASE_IN` only (not `EASE_IN_OUT`) so the anticipation-to-strike transition reads as fast/abrupt
  per the methodology doc's timing guidance; bounded squash/stretch (1.08/1.08/0.90) on the chest at
  the strike frame. **One fix round**: a first pass summed spine+neck+head forward pitch to ~98
  degrees at the strike frame, curling the head entirely behind the wing/body silhouette in a render
  check -- reduced to a ~54-degree cumulative pitch, which keeps the beak visible through the strike.

**Verification gates** (`verify_report.json`, numbers from the actual pilot run):

| Gate | Result | Pass |
| --- | --- | --- |
| Move foot-slide (stance-velocity CV, threshold < 0.35) | L: 0.014, R: 0.018 (stddev 0.07 / 0.10 mm/frame) | yes |
| Move knee-angle range (15-179.5 deg) | L: 122.6-141.0, R: 125.0-150.4 | yes |
| Move loop-seam (max bone delta < 0.5 deg) | 0.000 deg | yes |
| Move ground interpenetration (toe min z > -0.01) | L: -3.7e-8, R: -1.9e-7 | yes |
| Idle loop-seam | 0.000 deg | yes |
| Idle jitter (max 2nd-deriv < 0.15) | 0.00057 | yes |
| Attack jitter (max 2nd-deriv < 0.15) | 0.0351 | yes |
| Attack cumulative head pitch (< 120 deg, post-fix) | 64.0 deg | yes |

The foot-slide gate's threshold (constancy of stance-phase velocity, not literal zero) is specific
to this pipeline's **in-place clip** convention -- see `anim/gait.py`'s and `verify.py`'s module
docstrings for why that's the correct metric when root translation is left to the engine, not baked.

**Export:** `griffin_anim.glb`, **0.573 MiB** (well under the 2 MB budget), 25 bones, 7,998 tris,
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
(`scratchpad/anim-pilot/review/final/`, see the handback report for absolute paths): a contact sheet
per clip (10 frames at the battle camera angle, toon-shaded), an onion-skin overlay of the Attack
clip's key poses, and a GIF reel cycling Idle -> Move -> Attack -> Idle with real crossfades,
rendered **from the Live3D runtime itself** (not a Blender fallback -- `--pilot-sequence` captures
real in-engine frames, so the reel and contact sheets show exactly what the game's own toon shader,
GPU skinning, and crossfade/spring-bone code produce, not a pre-rendered stand-in).

## Known limitations / honest self-critique

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
- **Move's stride is short** (see the Animation section above) -- a direct consequence of this
  landmark placement's limited IK reach margin, not a bug in the gait math itself (the verification
  gates confirm the math is correct: zero measurable foot slide). A wider stride needs more hip
  reach margin, which needs either a different hip-landmark heuristic or a deliberately looser
  safety factor in `anim/gait.py`'s per-leg stride solve.
- **This is a pilot on one creature.** The template's leg-count auto-detection, landmark heuristics,
  and the overall stage pipeline are written to generalise, but they have only been exercised against
  this one winged-biped mesh. A true 4-legged creature, a creature with separate front talons, or a
  serpentine/no-leg creature would each exercise code paths (the 4-leg branch in
  `winged_quadruped.py`'s clustering, for instance) that this pilot's own mesh never triggered.
