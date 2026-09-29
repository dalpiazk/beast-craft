# Spike #55 (mini): pre-rendered 3D toon vs 2D code bone rig -- gate report

**Status:** mini-spike only, per the producer's 2026-09-29 decision on issue #55 (1-2 days, one beast, two
paths, not the full 2-3 week engine evaluation). This report is the gate.

## 0. Goal

Issue #55 asks whether 3D (or a different engine) beats the current 2.5D + Spine path. The producer scoped
this mini-spike to just the highest-uncertainty question: **does a pre-rendered Blender toon render of an
AI-generated 3D mesh look and cost better than animating the Griffin's existing, already-approved 2D
artwork with a code bone rig** -- the same joints Spine Essential would give us (bones, no mesh deform),
without taking on a Spine licence.

Both paths keep MonoGame; neither tests real-time 3D rendering or another engine (that remains in the
full, not-yet-scheduled spike per issue #55's larger scope).

## 1. Method

One beast (Griffin, the approved illustration and its existing machine-cut rig parts), one AI-to-3D
generation, one Blender toon pipeline, one code-driven 2D bone rig, built and rendered in this session.
Scripts: `Tooling/Spike55/` (README there explains how to rerun each path). Full detail, including two
genuine tooling bugs hit and fixed along the way, is in that README; this report only carries what's
needed to make the call.

- **Input:** a Meshy (paid tier, producer-generated, output owned) image-to-3D model of the approved
  Griffin illustration. Provenance: `Tooling/ArtLab/provenance/spike55-griffin-meshy.md`.
- **Path A (3D):** `Tooling/Spike55/blender_toon_render.py`, run headless in Blender 5.2.1 LTS (portable
  install, see the README -- the winget package's download was blocked by a Cloudflare bot challenge in
  this environment, so it was installed from Blender's own mirror network instead). Imports the GLB,
  normalises scale/ground/facing, builds a 2-3 band toon material (warm gold base, shadows tinted toward
  cool shadow `#7C7AAE`, no pure black) with an inverted-hull ink-plum outline, sets an orthographic
  hex-board camera, builds a 10-bone armature (root/body/neck/head/wing_l/wing_r/4 legs/tail) by hand from
  the mesh's bounding box, and renders idle (12 frames) and move (8 frames) loops at 512x512.
- **Path B (2D):** `Tooling/Spike55/rig2d.py`, pure Python + Pillow (no new dependency; matches
  `Tooling/PixelArt`'s pinned Pillow). Reads `content/art/source/griffin/parts.json` (already-cut parts:
  `tail`, `legs_back`, `body`, `legs_front`, `wings`, `head`, each with a pivot) and rotates each part about
  its own pivot with sinusoidal oscillators, compositing back-to-front. Same idle/move frame counts and
  output size as Path A, for a fair side-by-side.
- **Review pass:** every rendered frame (idle x12, move x8, hero) was looked at for both paths; see
  section 4 for what the one fix round changed and what's left as a known issue.

## 2. Results

![Comparison sheet](055/comparison_sheet.png)

*Left to right: the approved 2D illustration, Path A's Blender toon render, Path B's 2D code bone rig.*

![Board mock](055/board_mock.png)

*Both placed on a Verdant Hollow backdrop at the same scale (a simplified placement, not the full
hex/obstacle overlay tooling -- out of scope for a two-day spike).*

Idle loops: `055/idle_2d.gif`, `055/idle_3d.gif`. Move loops: `055/move_2d.gif`, `055/move_3d.gif`.

### 2.1 Likeness and fit with the art v2 pillars

- **Path B** reads as the same Griffin, because it *is* the same Griffin -- the same painted linework,
  the same warm daylight / cool shadow palette, the same likeness the producer already approved. It fits
  the art v2 pillars (warm daylight with cool saturated shadows, no pure black, painted not plastic) by
  construction, since nothing about the source art changed.
- **Path A** is recognisably a griffin, in a similar heraldic pose, but it is **not** the same design: the
  Meshy generation reinterprets proportions, crest shape and feather detail, and (see 2.2) ships with no
  colour information at all, so the toon shader had to fabricate a flat palette rather than carry the
  approved illustration's actual painted texture. It's a plausible *new* Griffin, not a 3D version of the
  *approved* one. Getting a 3D asset that reads as the same design, same as a rig would preserve, is a real
  open problem this mini-spike does not solve (it would need either a much more controlled AI-to-3D
  pipeline, e.g. multi-view-consistent generation plus a full retexture/paint-over pass, or manual 3D
  modelling from the illustration as reference -- both well beyond a two-day spike).

### 2.2 A concrete finding: the Meshy GLB has no texture

`Tooling/Spike55/gltf_inspect.py` (a from-scratch GLB parser, no 3D library needed) on the delivered
`.glb`: **51,488 triangles, 39,446 vertices, UVs present, but `images: 0` and `materials: 0`.** The
"generate" download is geometry + UVs only. Path A's toon material therefore uses a flat colour sampled
from the Griffin's own approved swatch (`Tooling/ArtLab/provenance/griffin.md`), not a projection of the
source illustration -- a real gap for any production use of this path, and not something this spike's
time box could close (it needs either a different Meshy export/tier setting, or a manual texture pass, or
re-lighting from vertex colour if the tool can bake one; none of that was tried here).

### 2.3 A concrete finding: automatic bone weighting failed, even after a voxel remesh

Per the task's own fallback instruction, `blender_toon_render.py` tries Blender's automatic (heat-map)
bone weighting on the raw Meshy mesh, and if that leaves most vertices with zero deform weight, retries
once after a voxel remesh (`REMESH` modifier, `VOXEL` mode). **Both attempts failed** here -- Blender logs
`Bone Heat Weighting: failed to find solution for one or more bones` both times, and every one of the
39,446 (raw) / 58,780 (remeshed) vertices ends up with no deform weight. This is very likely the Meshy
mesh being non-manifold or having disjoint shells under the wings/body (common for single-image-to-3D
output), though the exact cause wasn't isolated further (out of scope for a mini-spike).

**Fallback in effect:** the script still builds the 10-bone armature (left in the scene as documentation
of the intended hierarchy), but the idle/move loops you see in Path A's GIFs are the whole Griffin object
animated rigidly (a bob/sway/lean), **not** true per-part skeletal deformation. This is a materially weaker
result than Path B, which does bend each part independently at its own pivot.

### 2.4 One fix round

- **Path A:** the first render leaned almost entirely into the cool-shadow band (a lighting/ramp-threshold
  problem, not a modelling one) -- fixed by moving the ColorRamp's gold stop from 0.55 to 0.32 and
  brightening the key light. A handful of small bright specks remain at thin, sharp geometry (feather tips,
  claws) where the inverted-hull outline shell self-intersects; left as a known issue rather than chased
  further (see 4.1).
- **Path B:** no fixes needed to the joints/pivots (they came pre-placed and correct from
  `content/art/source/griffin/parts.json`, cut by the existing `Tooling/ArtLab/rigparts.py`); a very small
  seam is visible between the head and neck at the largest head-turn angles in the idle loop, left as a
  known issue (see 4.1).
- Two **tooling bugs**, not art bugs, ate most of Path A's build time and are fixed in the committed
  script (not just worked around): (a) Blender's render operator silently ignored `object.location` /
  `object.rotation_euler` changes to the *mesh* object between headless renders in this Blender 5.2
  session (reproduced with a pixel diff: identical output despite different rotation values) -- fixed by
  driving an unencumbered parent `Empty` instead; (b) the winget/MSI Blender install was blocked by a
  Cloudflare bot challenge on `download.blender.org` in this sandboxed environment -- worked around with a
  portable zip from one of Blender's mirrors, extracted to a short path (the zip's own deeply nested paths,
  e.g. under `pkg_resources/tests/...`, exceed Windows' `MAX_PATH` if extracted somewhere already deep).

### 2.5 Known issues left as-is (honesty check, not chased further)

| Path | Issue | Judgment |
| --- | --- | --- |
| A | No texture from the Meshy GLB; flat palette colour instead | Real gap, not fixable in this spike's time box |
| A | Automatic rigging failed; whole-object rigid fallback only | Real gap -- see 2.3 |
| A | A few bright speck artefacts at thin geometry (outline shell) | Minor, cosmetic |
| A | Design drifts from the approved illustration (see 2.1) | Real gap for reuse of existing approved art |
| B | Small seam at the head/neck joint at extreme rotation | Minor, cosmetic |
| B | `legs_front`/`legs_back` are pair-combined parts, not per-leg, so the move cycle is a stylised 2-group lope, not a true 4-leg alternating gait | Known limitation of the *existing* rig-parts cut, not something this spike introduced |

## 3. Measurements

**Time** (this session, one person, one beast, building the tooling from scratch -- see the breakdown for
what's one-time vs repeatable):

| | Path A (3D) | Path B (2D) |
| --- | --- | --- |
| Environment/tooling setup (one-time: Blender install workaround, orientation probing, material/lighting tuning, the two tooling bugs in 2.4) | ~90-110 min | ~20 min (script worked close to first-try against the existing parts convention) |
| Per-asset repeatable work, *once the tooling exists* (run script, eyeball frames, adjust) | est. 10-20 min **if** automatic rigging succeeds; unknown/mesh-dependent additional time when it doesn't (as here) | est. 3-5 min (fully automatic; parts.json already carries correct pivots from the existing `rigparts.py` cut) |

Path B's per-asset number should generalise reasonably well across the other 9 beasts, since they were all
cut by the same `rigparts.py` convention (same six-part hierarchy, `order_back_to_front`, pivot fields).
Path A's per-asset number is much less certain: the rigging failure here is plausibly mesh-specific
(topology-dependent), so "10-20 minutes once tooling exists" is optimistic and the real number could
regress to the same order as this first attempt (over an hour) on a mesh with worse topology, or improve
if a future Meshy export is cleaner. Labelled as an estimate.

**Output sizes** (idle 12 frames + move 8 frames = 20 frames, 512x512 RGBA, both paths, for a fair
comparison):

| | Path A | Path B |
| --- | --- | --- |
| Mesh | 51,488 tris, 39,446 verts (Meshy GLB) | n/a (2D parts) |
| Source texture | none (see 2.2) | 6 PNGs, ~2.05 MB total on disk (already existed, `content/art/source/griffin/parts/*.png`) |
| Idle frames on disk | 12 x 512x512 PNG, ~2.2 MB total | 12 x 512x512 PNG, ~2.2 MB total |
| Move frames on disk | 8 x 512x512 PNG, ~1.5 MB total | 8 x 512x512 PNG, ~1.5 MB total |
| Idle/move sprite strip | n/a in this spike (frames only) | 6144x512 (1.43 MB PNG) / 4096x512 (0.71 MB PNG) |

The two paths' *baked frame* disk cost is nearly identical (expected -- both are 512x512 RGBA PNGs of
similar visual complexity). The difference that matters is what has to be re-baked when something changes
(next section).

**RGBA texture memory, 2048x2048 atlas budget (per `docs/art/art-brief.md`):**

- One 2048x2048 RGBA atlas page = 16 MiB uncompressed (`2048*2048*4`); it holds sixteen untrimmed 512x512
  frames.
- Idle (12) + move (8) = 20 frames already **overflows one atlas page** for either path if baked
  untrimmed at 512x512 (20 > 16 slots) -- before the other five in-data clips (attack, cast, hit, KO,
  victory) or any cosmetic variant are counted. Trimming to content bounds (the Griffin's illustrated
  export uses ~394x504 inside the 512x512 frame) buys back some room but doesn't change the order of
  magnitude.
- **Path B's real per-beast cost, if kept as a live rig (not baked) as intended:** the six part textures,
  at their existing 2x-final master resolution, sum to `1154x1428 + 609x1081 + 580x463 + 609x531 +
  311x407 + 496x425` px = ~12.9 MiB RGBA. Downscaled to game resolution the same way `character.png` ->
  `griffin.png` already is (~0.268x, from `griffin.md`'s provenance), that's **~0.9 MiB total**, shared
  across *every* animation clip and *every* cosmetic combination, because the rig recomposites the same
  parts every frame instead of baking. This is the whole point of not pre-rendering.

**Engine draw cost (A35, unmeasured on device):** both paths, *as built in this spike*, bake to ordinary
2D sprite frames drawn the same way the game already draws every illustrated beast (`SpriteBatch`, alpha
blend, premultiplied PNG) -- so the runtime draw cost of Path A's output is the same cost class as
anything already shipping, not a new risk. (This spike does not test real-time 3D rendering in MonoGame,
which is a separate, larger item in issue #55's full scope and would have a genuinely different, unmeasured
GPU cost profile.) The real device-dependent unknowns are: (a) whether the *content pipeline* (atlas
packing, storage, load time) copes with Path A's much larger frame count at full scale, and (b) nothing
new for Path B, which stays inside the same budget the game already ships. A real Galaxy A35 test is
needed to confirm either way; not done here (out of scope for a mini-spike, and no device was available in
this environment).

## 4. Cosmetics (`docs/art/art-brief.md`, `cosmetic-library.json`, issue #38)

The Griffin has two discrete cosmetic categories plus a colour-form set (from
`content/data/Cosmetics/cosmetic-library.json`): `griffin_crest` (9 options), `griffin_wings` (8 options),
`griffin_colour_form` (3 options), plus `griffin_tint` (a continuous `ColorPicker`, not a discrete
multiplier).

- **Path B (parts/attachment swap):** each option is one more part texture, swapped into the same rig at
  the same slot -- **9 + 8 + 3 = 20 additional art assets**, combinable freely, zero additional animation
  work. This is exactly the "cosmetic swaps without per-beast rework" pass criterion, and it's true today,
  because it's the same mechanism the existing Spine plan (section 2 of the art brief) already assumes --
  this spike just proves it also works without Spine, over plain PNG parts.
- **Path A (pre-rendered, single fused mesh):** the Meshy mesh has no separable crest/wing sub-meshes, so
  as built here, a different crest or wing means a different generation and a full re-render. Crest x
  wings alone is 9 x 8 = 72 combinations; with the 3 colour forms, 216; at 20 frames (idle+move only) each,
  that's **4,320 frames for one beast's idle+move set alone**, before the other five clips are counted (a
  full 7-clip set would scale this well past 10,000 frames). A more sophisticated pipeline could in
  principle treat crest/wings as swappable sub-meshes/textures on a *segmented* 3D model and cut this down
  -- but that's a materially bigger engineering lift than this spike demonstrates, and the current
  single-fused-mesh Meshy output doesn't support it without extra retopology/segmentation work per asset.
  Labelled as an estimate (frame count is exact arithmetic; the "how much of this could be engineered away"
  part is not).

## 5. Pass criteria (from issue #55)

| Criterion | Path A | Path B |
| --- | --- | --- |
| Steady 60 fps on a Galaxy A35-class phone | Unmeasured; expected non-issue as baked 2D sprites (same cost class as shipping content) -- see section 3 | Unmeasured; expected non-issue, same reasoning, and it's strictly less new content than Path A |
| An approvable beast within a set number of hours per asset | Real risk: rigging did not converge this attempt; per-asset time is uncertain/mesh-dependent | Looks solid: ~3-5 min per beast once the tooling exists, reusing the existing `rigparts.py` convention |
| Cosmetic swaps without per-beast rework | **Fails as built**: needs full re-generation + re-render per combination (section 4) | **Passes**: parts/attachment swap, no rework |
| A trimmed device build that loads content | Out of scope for this mini-spike | Out of scope for this mini-spike |

## 6. Recommendation

**Continue the 2.5D + Spine path** (or, short of taking on the Spine licence, the code bone rig this spike
demonstrates over the existing parts). Do not pursue pre-rendered 3D for the beast roster on the evidence
here.

Reasoning:

1. The cosmetic-swap criterion is the one this whole spike (and issue #38) cares most about, and Path A
   fails it outright with the tooling available in a two-day window: a single-fused, textureless,
   non-riggable-by-default AI mesh cannot cheaply take 20 discrete cosmetic options without a full
   re-render per combination.
2. Path B costs almost nothing beyond what's already built: it reuses the *already-approved* Griffin art,
   the *already-cut* rig parts, and produces a visibly correct, independently-articulated idle/move loop
   with about 20 minutes of new tooling. It is the lower-risk, lower-cost path by a wide margin.
3. Path A's likeness gap (2.1) and missing texture (2.2) mean even a "best case" render isn't a 3D version
   of the approved Griffin -- it's a new, different-looking griffin, which the producer would need to
   re-review and potentially re-approve per beast, on top of the rigging and cosmetic problems above.
4. The one thing Path A visibly wins on: it's a real skeleton in principle (once rigging works), giving a
   camera more freedom (e.g., a free-orbiting Grove camera, noted as one of the two reasons to reconsider
   3D in issue #55's notes). If a free Grove camera becomes a real requirement, that's worth a fresh,
   narrower spike specifically on rigging AI-generated meshes reliably (voxel remesh already tried and
   failed here; manual retopology or a different generation tool might do better) -- not a reason to adopt
   3D for the whole roster today.

### Risks if this recommendation is wrong

- If cosmetic scope shrinks a lot (fewer discrete options per species), Path A's combinatorial cost drops
  proportionally and might become tractable -- worth revisiting if `cosmetic-library.json` changes
  significantly.
- If a future AI-to-3D tool or tier reliably outputs clean, riggable, textured, segmented meshes, most of
  Path A's problems here (2.2, 2.3, section 4) could disappear; this spike used one paid-tier Meshy
  generation and does not rule that out for other tools/settings.

### Next steps

- No further 3D work recommended off this spike. If 2D cosmetic authoring later proves unsustainable at
  full scale (10 beasts x ~2-3 categories x several options each), re-open with a wider spike per issue
  #55's original scope (real-time MonoGame 3D, Unity, Godot), not another pre-rendered-3D attempt.
- Carry the code-bone-rig approach (Path B) forward as a live option if the Spine licence is ever judged
  not worth it: `Tooling/Spike55/rig2d.py` is a working starting point, though production use would want it
  ported to run inside the game (not as an offline PNG baker) so it gets the "no rework" benefit at
  runtime rather than needing pre-baked frames.

## AI provenance

The Griffin 3D model used for Path A was generated by **Meshy (paid tier)**, by the producer, from the
already-approved 2D Griffin illustration; output ownership is retained (paid tier, not the CC BY free
tier). Disclosed per `docs/art/art-brief.md`'s AI-disclosure rule. Full record:
`Tooling/ArtLab/provenance/spike55-griffin-meshy.md`. The model is a spike input only and is not committed
to the repository or shipped with the game (`Tooling/Spike55/README.md` explains how to get your own copy
to rerun Path A).
