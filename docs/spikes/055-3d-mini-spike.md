# Spike #55 (mini): pre-rendered 3D toon vs 2D code bone rig -- gate report

**Status:** mini-spike only, per the producer's 2026-09-29 decision on issue #55 (1-2 days, one beast, two
paths, not the full 2-3 week engine evaluation). This report is the gate. **Updated 2026-09-30** with a
second pass (section 2.6) that re-runs Path A against a *textured* Meshy generation, and a third pass
(section 2.7) that re-topologises that textured generation down to a low-poly (~8k tri) mesh via Meshy's
paid `remesh` endpoint; see those sections for what changed and what didn't.

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

*Left to right: the approved 2D illustration, Path A pass 1 (untextured Meshy GLB), Path A pass 2
(textured meshy-7.1 GLB, section 2.6), Path B's 2D code bone rig. Updated 2026-09-30 from a 3-column sheet
to this 4-column one; the pass-1-only renders this report originally shipped with are superseded here but
described unchanged below (2.1-2.5) since that evidence still stands for pass 1.*

![Board mock](055/board_mock.png)

*All three (2D reference, Path A pass 1, Path A pass 2) placed on a Verdant Hollow backdrop at the same
scale (a simplified placement, not the full hex/obstacle overlay tooling -- out of scope for a two-day
spike).*

Idle loops: `055/idle_2d.gif` (Path B), `055/idle_3d.gif` (Path A pass 1, untextured),
`055/idle_3d_textured.gif` (Path A pass 2, textured, section 2.6). Move loops: `055/move_2d.gif`,
`055/move_3d.gif`, `055/move_3d_textured.gif`, same pattern.

**Low-poly (pass 3, section 2.7) is kept on a separate sheet, not folded into the 4-column one above**, to
avoid re-touching the already-committed comparison sheet for a pass added after the gate's main call:

![Low-poly sheet](055/lowpoly_sheet.png)

*2D reference, Path A pass 2 (textured, 929,638 tris), Path A pass 3 (low-poly `remesh`, 8,372 tris,
section 2.7).*

![Board mock with low-poly](055/board_mock_lowpoly.png)

*The pass-3 low-poly figure added to a copy of the existing board mock, same backdrop/scale as the other
three.*

Low-poly loops: `055/idle_3d_lowpoly.gif`, `055/move_3d_lowpoly.gif` (Path A pass 3, same 12/8-frame,
512x512 pattern as the other two 3D passes).

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

### 2.6 Second pass: textured model (2026-09-30)

The producer authorised exactly one more paid Meshy call to test whether a **textured** generation closes
2.2 (no texture) and improves 2.3 (rigging) enough to change the recommendation. One call was made and is
fully accounted for below; nothing else spent credits.

**Generation.** `python Tooling/ArtLab/scripts/meshy.py image-to-3d --image scratchpad/spike55/griffin_meshy_input.png
--model meshy-7.1 --texture --texture-resolution 2k --yes` (the same source illustration as pass 1,
re-submitted through the new local CLI rather than the producer's manual first-pass generation). Balance
before: **1700 credits**; after: **1670 credits** -- **30 credits spent**, exactly matching the CLI's printed
cost for `meshy-7.1` mesh + 2K texture, confirmed with a `--dry-run` before the real call. Task
`01a0f351-869e-7319-9820-e4b6e8b6b226`, `SUCCEEDED` in ~104s. Full record:
`Tooling/ArtLab/provenance/meshy-01a0f351-869e-7319-9820-e4b6e8b6b226.md`.

**Mesh/texture stats** (`Tooling/Spike55/gltf_inspect.py` on the downloaded GLB, not committed -- see
"Committed?" in the provenance file): **929,638 tris, 496,815 verts, 1 material, 1 image** (`base_color`,
JPEG, 2048x2048). Unlike pass 1 (51,488 tris, 39,446 verts, `images: 0`, `materials: 0`), this generation
carries a real baked base-colour texture -- 2.2's finding is closed for this generation/tier/settings
combination. File size ~29 MB (not committed; large binary working file, same policy as pass 1's GLB).

**Rig result: a real per-part rig, after three targeted repairs.** `blender_toon_render.py` was extended to
(a) build the toon material as a banded multiplier over the Meshy base-colour texture instead of a flat
swatch (cool `#7C7AAE`-tinted shadow band, never pure black, neutral midtone, slight warm highlight boost,
warmed key light, thinner ink-plum outline), and (b) retry rigging per the task brief. What actually
converged, in order:

1. **Attempt 1** (heat weights, raw mesh): failed, same as pass 1 (`Bone Heat Weighting: failed to find
   solution for one or more bones`; 496,815 / 496,815 vertices unweighted).
2. **Attempt 2** (heat weights on a voxel-remeshed duplicate): **succeeded** (16 / 51,398 unweighted at the
   first voxel size tried). Pass 1 stopped here and applied the remesh directly to the render mesh, which
   would have destroyed this pass's UVs/texture mapping. Instead, the remesh ran on a disposable
   *duplicate*; the computed weights were copied back onto the original, UV-intact mesh with a Data
   Transfer modifier (`POLYINTERP_NEAREST`, nearest-surface), and the duplicate was discarded. This is the
   "voxel remesh + data-transfer of weights" option the task brief named, chosen over envelope weights
   because it produces real heat-map-quality weights rather than a coarser geometric approximation, and it
   worked on the first try.
3. **Method that shipped:** heat weights computed on a voxel-remeshed duplicate (finished at
   `voxel_size = 0.003 x TARGET_HEIGHT`, finer than the `0.006` first tried -- see below), data-transferred
   onto the original mesh. Envelope weights (the brief's other named fallback) were never needed.

**One fix round, three causes found.** The first textured render showed thin ink-plum spikes stretching
from the legs during the move loop's largest rotations. Investigating turned up three distinct failure
modes in the data-transfer path, each with a targeted repair (all in `blender_toon_render.py`, run
automatically, not manual cleanup):

- **554 fully unweighted vertices** (at the voxel size first tried) -- nearest-*weighted*-vertex copy
  (`repair_stray_unweighted_vertices`). Finer remeshing (`0.006` -> `0.003`) independently brought this to
  0 before the repair even ran.
- **70 vertices weighted, but disagreeing with every mesh-connected neighbour's dominant bone** -- a
  topology-walking repair (`repair_topologically_inconsistent_weights`) that only ever copies from a
  vertex the bad one is actually edge-connected to, so it can't jump the same empty-space gap that caused
  the original mismatch.
- **Small disconnected mesh islands** (this Meshy mesh is fragmented into 703 edge-connected components,
  not one blob with a few floaters -- typical of single-image-to-3D output): islands at or below 300
  vertices (276 of the 703) were snapped wholesale to their nearest larger-component vertex
  (`reweight_floating_mesh_islands`). An earlier, untargeted version of this repair (touching every
  non-largest component) was tried and rejected after it altered 489,925 of 496,815 vertices -- confirmed
  overcorrecting -- before landing on the 300-vertex threshold.

After all three, **0 / 496,815 vertices are unweighted**, all previously-mismatched vertices agree with
their neighbours, and idle/move show genuine per-part skeletal deformation, not the whole-object rigid
fallback pass 1 shipped. One thin spike remained visible on certain move frames after all three repairs;
disabling the outline (Solidify) modifier and re-rendering showed the exact same spike in the base mesh
colour, confirming it is **real geometry from the Meshy reconstruction** (a thin spur near a claw, not a
rig/weight bug) -- left as a known issue, the same class as pass 1's outline-shell specks (2.5), not chased
further under the one-fix-round budget.

**Renders.** Idle (12) and move (8) frames at 512x512, same camera/framing/board-mock placement as pass 1
for a fair comparison: `055/idle_3d_textured.gif`, `055/move_3d_textured.gif`, and the fourth column of
`055/comparison_sheet.png` / third figure of `055/board_mock.png`.

**Likeness assessment vs. the approved illustration and the art v2 pillars.** Closer, but the verdict from
2.1 is **unchanged**: this is still a plausible *new* griffin, not a 3D version of the *approved* one. The
texture is real (unlike pass 1) and reads as warm gold/cream fur and white feathering with visible
strokework -- tonally in the art v2 family (warm daylight, cool saturated shadow band, no pure black,
painted rather than plastic) -- but it's Meshy's own fabricated texture, not a projection of the actual
approved artwork, and the underlying mesh still reinterprets proportions, crest shape and feather
silhouette the same way pass 1's did (2.1). A producer comparing `055/comparison_sheet.png` column 3
against column 1 would still recognise "a griffin, by the same tool, now with fur and feather detail," not
"our griffin, in 3D."

**Does this change the recommendation?** **No.** The cosmetics combinatorics argument (section 4) that
drives the recommendation is untouched by texturing -- if anything it gets slightly worse, since a
different crest/wings/colour-form combination now means a different *textured* generation and re-render
(same 30-credit, ~2-minute cost each, at minimum, for a single-fused mesh with no separable cosmetic
sub-meshes), not just a different untextured one. The rigging picture genuinely improved (a real per-part
rig now works, given the extra weight-transfer engineering above), which **does** partially address 2.3 and
would matter if a free-orbiting camera became a real requirement (section 6, point 4) -- but it doesn't
touch the cosmetic-swap failure that is the recommendation's primary driver. **Recommendation unchanged:
continue the 2.5D + Spine path (or the code bone rig this spike demonstrates).**

### 2.7 Third pass: low-poly (remesh) (2026-09-30)

The producer authorised exactly **one** more paid call -- Meshy's `remesh` endpoint, 5 credits flat,
`input_task_id=01a0f351-869e-7319-9820-e4b6e8b6b226` (the pass-2 textured task), `target_polycount=8000`,
`topology=triangle` -- to test whether a paid, purpose-built retopology closes the gap a **free** local
attempt had just failed to close. One call was made and is fully accounted for below; nothing else spent
credits.

**The free attempt that came before this, and why it doesn't count.** Before this authorisation, a free
local pass ran Blender's Decimate (Collapse) modifier directly on the pass-2 mesh (929,638 tris -> ~8,000,
no Meshy call, no credits). It failed visually: the result (`lowpoly_render/hero.png`,
`lowpoly_render/move/move_02.png` in the work dir, not committed) shattered into disconnected shards with
visible holes and floating debris, and the texture did not carry over (a flat grey-lilac surface, not the
Griffin's gold/cream palette). Root cause: the pass-2 mesh is itself fragmented into 703 disconnected
components (2.6), and Collapse decimation has no awareness of component or UV-seam boundaries -- it merges
edges within each shard independently, tearing the silhouette apart at the seams between shards. **That
result is not reused anywhere in this section** and should not be described as clean; it's superseded by
the paid remesh below, which explicitly avoids this failure mode.

**The remesh call.** `python Tooling/ArtLab/scripts/meshy.py remesh --task 01a0f351-869e-7319-9820-e4b6e8b6b226
--target-polycount 8000 --topology triangle --formats glb --yes`, confirmed against a `--dry-run` printing
the same 5-credit cost first. Balance before: **1670 credits**; after: **1665 credits** -- exactly 5 spent.
Task `01a0f38d-c409-702e-b976-62bff441b88f`, `SUCCEEDED` in ~89s. Full record:
`Tooling/ArtLab/provenance/meshy-remesh-01a0f38d-c409-702e-b976-62bff441b88f.md`.

**Mesh stats, and a finding that looked worse than it was.** `gltf_inspect.py` on the downloaded GLB:
**8,372 tris**, 12,987 verts as exported (includes glTF's per-loop UV/normal-seam vertex duplication -- see
below), 1 material, **3 images** (`texture_0` base colour 2048x2048, `normal` 2048x2048,
`texture_0_metallic_roughness` 4096x4096; a full PBR export, not just base colour -- texture handling on
`remesh` isn't documented either way as of this writing, and this call confirms it's carried through, at
least for this task/settings combination). A first raw connected-component check (same method as 2.6's
703-component finding on the pass-2 mesh) read as **2,452 components, largest only 38 verts** -- at first
glance indistinguishable from a shattered mesh, and alarming given the free attempt's failure right before
it. Checked rather than accepted: the glTF exporter (`Khronos glTF Blender I/O`) splits a vertex into
several coincident, unwelded position duplicates wherever its UV or normal differs per face corner --
normal glTF/Blender-import behaviour, not fragmentation. Running Blender's "Merge by Distance" at a 1e-4
threshold (small relative to this mesh's ~1.2-unit bounding-box range) collapses those 12,987 verts to
**4,192 true verts and a single connected component**, with only 28 non-manifold edges and 18 boundary
edges out of 12,562 -- a genuinely coherent, near-watertight low-poly mesh. This weld is now the first
step `Tooling/Spike55/blender_lowpoly_render.py` runs after import, before weighting or materials.

**Texture route: Meshy-kept, downsized.** The remesh output already carried a real base-colour texture
(unlike pass 1, and matching pass 2), so no UV-unwrap-and-bake pass was needed (the task brief's fallback
for an untextured result). The base-colour image (2048x2048) was downsized to **1024x1024** in Blender
(`Image.scale`) before rendering, per the task's 1K cap for a low-poly asset; the normal and
metallic/roughness maps were not used (this spike's toon shader, both here and in pass 2, only consumes
base colour -- see `blender_toon_render.py`'s banded-multiplier material).

**Rig result: converged on the first attempt, no repairs needed.** Automatic (heat-map) bone weighting ran
directly on the welded 4,192-vert mesh and converged immediately: **0 / 4,192 vertices unweighted**, single
mesh component. None of pass 2's three weight repairs (stray-unweighted, topologically-inconsistent,
floating-island) were needed, and the voxel-remesh-donor workaround both earlier passes required never
triggered. This mesh is the first of the three passes where Blender's own automatic weighting worked
cleanly on the mesh as delivered -- consistent with the low-poly mesh being genuinely more coherent
(single shell, few non-manifold edges) than either the 39,446-vert pass-1 mesh or the 496,815-vert,
703-component pass-2 mesh.

**Render and honest visual verdict.** Idle (12) and move (8) frames at 512x512, plus a hero frame and a
board-mock placement, same camera/lighting/palette/outline settings as `blender_toon_render.py`'s second
pass (`Tooling/Spike55/blender_lowpoly_render.py`, which is that script plus the weld and texture-downsize
steps above). Every idle and move frame was looked at individually, specifically for holes, shards,
floating pieces, texture seams/smears, and broken silhouette (wings, four legs, tail, beak). **The result
is clean**: no holes, no shards, no floating debris, no visible texture seams or smears, and the silhouette
reads correctly (both wings, all four legs, the tail, and the beak are intact and legible) across every
frame checked, including the move loop's largest leg-rotation frames where pass 2 had shown a residual
geometry spike. **No fix round was needed** -- the first render was already usable, a first for this
spike's three 3D passes. This is a materially different outcome from the failed free Decimate attempt
above; the two should not be conflated.

**Renders:** `055/idle_3d_lowpoly.gif`, `055/move_3d_lowpoly.gif`, and `055/lowpoly_sheet.png` (2D
reference / pass-2 textured hi-poly / pass-3 low-poly remesh, three columns) / `055/board_mock_lowpoly.png`
(the low-poly figure added to a copy of the existing board mock, same backdrop and scale as the earlier
three).

**Likeness, reassessed against the producer's relaxed bar.** The producer's criterion as of this pass is
"close, with appropriate physiology," not an exact match to the approved illustration (a change from how
2.1/2.6 read the bar). Under that relaxed standard, the low-poly remesh **passes**: it is unambiguously a
griffin with the right physiology -- eagle head and beak, feathered wings, lion hindquarters, four legs, a
tail -- proportioned plausibly, and carrying real fur/feather texture in the approved warm-gold/cream
family. It is still not a 3D reconstruction of the *specific* approved illustration (the underlying mesh
still reinterprets crest shape, proportions and feather silhouette, same as 2.1/2.6 found), so it would not
pass a strict same-design test -- but that stricter test is no longer the bar the producer is applying.

**Does this change the recommendation?** Partially reassessed, not reversed. Closing the likeness gap
under the relaxed bar removes one objection to Path A, but it doesn't touch the cosmetics-combinatorics
finding (section 4), which is about **pre-rendering a fused, non-segmented mesh** -- crest/wings/colour-form
swaps still mean a different generation and a full re-render per combination, regardless of whether the
per-combination render is a hi-poly or a low-poly pass. **One relevant, untested wrinkle the low-poly
result surfaces:** the combinatorics problem is specifically a *pre-rendering* problem -- baking fixed 2D
frames means every cosmetic combination needs its own bake. A real-time 3D renderer (not pre-rendered
frames) would not have this constraint in the same way: cosmetics could in principle be swapped as
textures or toggled submeshes on a live mesh at runtime, the same way Path B swaps 2D parts today, without
a re-render step. **This spike does not test that** -- no real-time MonoGame 3D rendering was built or
measured here, and issue #55's full-scope real-time-3D evaluation remains the place to test it, not this
mini-spike. See section 6 for the updated recommendation text.

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

### 2.8 Fourth pass: real-time 3D in MonoGame (2026-09-30)

Per the producer's 2026-09-30 decision, this pass tests the question the first three passes explicitly
did **not**: does **real-time** 3D rendering (not pre-rendered frames) change the cosmetic-combinatorics
verdict that has driven every recommendation so far? Built as a standalone app,
`Tooling/Spike55/Live3D` (not added to `BeastCraft.slnx`, does not touch `src/`), full tooling reference
in `Tooling/Spike55/README.md`'s "Fourth pass" section.

**Method.** The pass-3 low-poly `remesh` GLB (8,372 tris, section 2.7 -- no new Meshy call this pass,
per this pass's spend authorisation) was re-run through the same weld/texture-downsize/armature/
auto-weight pipeline as `blender_lowpoly_render.py`, but instead of baking PNG frames,
`Tooling/Spike55/blender_export_live.py` authors real keyframed `Idle` (2s loop) and `Move` (1s loop)
Actions and exports a single skinned, animated GLB (`griffin_live.glb`, 0.88 MiB, base-colour texture
only, JPEG-encoded, well under the 2 MB budget) plus one small procedural cosmetic attachment mesh
(`crest_alt.glb`, ~224 tris, 13 KiB, no Meshy call -- a from-scratch fanned-quad "plume", not an AI
generation) meant to be parented to the head bone at runtime. Both are small enough to commit directly.

The runtime (`Tooling/Spike55/Live3D`, MonoGame DesktopGL, `net10.0`, mirroring
`BeastCraft.Desktop`'s `MonoGame.Framework.DesktopGL` version) loads both GLBs with **SharpGLTF.Core
1.0.7** (MIT), CPU-skins each beast instance per frame (`Skinner.cs`, linear blend skinning against
`Node.GetWorldMatrix(animation, time)`-evaluated joint matrices), and draws with a custom toon +
inverted-hull-outline effect (`Content/Effects/Toon.fx`, compiled by the MonoGame content pipeline: a
2-3 band diffuse ramp, cool `#7C7AAE` shadow tint that's never pure black, a warm key light, plus an
ink-plum `#2E2A45` outline pass) over the Verdant Hollow backdrop
(`content/art/backdrops/r01/sun0/medium.png`) and a simple hex grid, orthographic camera, portrait
window (540x960 -- a 50% scale-down of the 1080x1920 target, so the window fits a normal desktop
monitor; see the README).

**Two real bugs found and fixed** (both are non-obvious MonoGame/glTF integration gotchas worth
recording, not spike-specific one-offs):

1. **MGFX (MonoGame's effect compiler) does not honour a `.fx` file's HLSL default-value
   initialisers.** A first render came out **solid black** -- `LightDirection`'s HLSL default
   (`normalize(float3(0.45, 0.65, 0.60))`) was silently discarded at compile time, came back as
   `(0,0,0)` at runtime, `normalize` of a zero vector is undefined/NaN, and every toon-band comparison
   fell through to `HighlightBoost` -- itself also unset/zero -- multiplying the correctly bound,
   correctly textured beast by black. Fixed by explicitly setting every constant shader parameter from
   C# in `LoadContent` instead of relying on the shader's own defaults.
2. **glTF's winding convention is the opposite of MonoGame/XNA's default `RasterizerState`.**
   `griffin_live.glb`'s index data was loaded as-is (no re-winding); MonoGame's default
   `CullCounterClockwise` treats *clockwise* as front-facing, backwards for a right-handed, CCW-front
   glTF mesh. Symptom: the toon pass lit the mesh's inside-out backfaces and the outline pass (also
   reversed) fully overdrew the model instead of just its fringe -- both explained by, and fixed by,
   swapping which `RasterizerState` each pass uses.

**One fix round, cosmetic placement.** The crest attachment first rendered draped in front of the
face like a bib (parented directly at the head bone's world matrix, no adjustment) -- the head bone's
bind-pose orientation doesn't line up with "up" in world space. Fixed with one small fixed local
re-orientation (scale down, tip the blades up and back) in `Game1.SkinInstance`; this is a stand-in
procedural mesh for the toggle proof, not hand-placed art, so a single fixed correction (not a full
per-bone rig) is deliberately as far as cosmetic placement went.

**Cosmetics and colour form: proven, no re-render or re-animation needed.** Pressing `C` toggles the
crest attachment on/off and `T` cycles 3 colour-form tints (a shader multiply on the sampled base-colour
texture) -- both change instantly, every frame, on the live skinned mesh, with **zero new art
generation and zero new Blender work**. This is the thing passes 1-3 could not test (section 4's
combinatorics finding was specifically about *pre-rendered* frames): a live 3D mesh really does sidestep
the per-combination re-bake cost that ruled out pre-rendered 3D. It does **not** by itself prove
segmented-submesh cosmetics (crest/wings as swappable parts of the *same* mesh) -- this pass only
demonstrates one *additional* small mesh parented to a bone, which is enough for an attachment-style
cosmetic (a crest, a saddle, a weapon) but not yet a body-colour-form-only proof for a part that's
baked into the base mesh's own geometry (handled here instead by the tint shader parameter, which is a
real and simpler mechanism for that case).

**Measured numbers (this machine: Intel Arc 140V laptop, DesktopGL, Release build, vsync disabled for
true uncapped throughput via `--bench`):**

| Beasts | fps avg | fps 1% low | Draw calls | Triangles | CPU skin time | Managed mem | GPU mem (est.) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1,006 | 154 | 6 | 17,192 | 0.37 ms | 12.8 MB | 6.4 MB |
| 3 | 644 | 69 | 14 | 51,576 | 1.24 ms | 8.4 MB | 7.6 MB |
| 12 | 183 | 20 | 50 | 206,304 | 4.11 ms | 9.9 MB | 11.5 MB |
| 24 | 92 | 9 | 98 | 412,608 | 9.43 ms | 16.9 MB | 16.6 MB |

(10-second `--bench` runs; "draw calls"/"triangles" are per-frame totals -- each beast is 2 draw calls
x 2 passes (toon + outline) for body and crest.) Even at 24 beasts, uncapped average throughput (92
fps) stays comfortably above the 60 fps pass bar on this desktop GPU, with wide margin. The 1%-low
numbers are notably worse relative to average, especially at 12/24 beasts (20 fps, 9 fps) -- this looks
like GC/driver-pressure jitter from `DynamicVertexBuffer.SetData` discard-writing large buffers for
every instance every frame (CPU skinning re-uploads the full vertex buffer per beast per frame; an
earlier version that allocated fresh `Matrix4x4[]` arrays per instance per frame made this materially
worse, fixed by reusing per-instance scratch arrays -- see `AnimatedPose.cs`'s comments), not something
chased further here. A production implementation would very likely move skinning to the GPU (a bone
matrix palette in the vertex shader, no per-frame CPU vertex buffer rewrite) specifically to remove this
class of stall; this spike deliberately used CPU skinning (explicitly acceptable per the task brief) so
"skinning time" would be a directly measurable, honest number rather than hidden GPU cost.

**Galaxy A35 (Exynos 1380 / Mali-G68 MP5) estimate -- unmeasured, reasoned from these numbers:**

- 8,372 tris and one 1024x1024 texture per beast is a trivial vertex/fill-rate budget for a mobile
  GPU in the A35's tier (Mali-G68 MP5 comfortably handles several times this in current mobile titles);
  the real mobile-specific risk is **not** raw triangle/fill throughput.
- The real risks are (a) **CPU skinning cost on a much slower mobile CPU core** -- this laptop's CPU
  skinned 24 beasts in 9.4 ms; a phone-class Cortex-A55/A78-tier core (Exynos 1380's CPU) could plausibly
  run this 3-6x slower with no other optimisation, which would meaningfully eat into a 16.6 ms (60 fps)
  frame budget at high beast counts -- this is the single biggest unmeasured unknown; (b) **driver
  overhead per draw call on a mobile OpenGL ES driver**, typically worse than desktop drivers -- at 98
  draw calls/frame for 24 beasts, this is a real (if standard) mobile-optimisation target (instancing or
  batching would cut it); (c) **shader compile/portability for GLSL ES** (`Toon.fx` was only compiled
  and tested for the OpenGL/DesktopGL profile via MGFX here -- Android's MonoGame target uses a
  different GLSL ES profile, untested); (d) **content pipeline and SharpGLTF behaviour on Android**,
  entirely untested in this pass (MonoGame's Android host uses a different asset-loading path than
  `File.ReadAllBytes`/`Texture2D.FromStream` off the raw filesystem, which is what this spike relies
  on). **The exact test needed:** build the Android variant of this same app (out of scope for this
  pass per the task brief) and run the same `--bench` harness on a physical Galaxy A35 at the beast
  counts the PvE design actually needs (per the user's PvE-direction note: small teams vs. up to ~24
  small enemies) -- no number in this section should be treated as a substitute for that.

**Per-beast asset cost, honestly estimated from what was actually done this pass:**

- Meshy generate (pass 2, reused): 30 credits -- **not spent again this pass**.
- Meshy remesh (pass 3, reused): 5 credits -- **not spent again this pass**. This pass made **zero**
  Meshy API calls (none were authorised for it); it consumed the pass-3 GLB already on disk.
- Blender rig/anim authoring (`blender_export_live.py`), *given the pass-3 rig/weighting code already
  existed to build on*: roughly 25-35 minutes of active work this session, including two Blender-API
  debugging detours (the layered-Action `fcurves` API change in Blender 5.x, and the glTF exporter's
  Principled-BSDF-only base-colour extraction, both documented in the README). This is the "tooling
  exists" case -- a first attempt on a species with no prior rig work would likely cost closer to pass
  1/2's 60-110 minute range (section 2.7's time table).
- **Not a per-beast cost, but a real one-time cost this pass paid for the whole game:** the runtime
  engineering (SharpGLTF integration, CPU skinner, custom toon+outline shader, camera/hex board,
  stress/bench/screenshot harness) took the bulk of this session's time. It does not recur per beast,
  but it is real engineering investment a production real-time-3D path would need to have already paid
  before any beast benefits from it -- unlike Path B (section 4), which reuses the game's existing
  `SpriteBatch` draw path with no new engine code at all.

**Risks, specific to this pass:**

- **Quadruped animation authoring without Mixamo.** This pass's idle/move clips are hand-authored
  sinusoidal pose keyframes (ported directly from `blender_lowpoly_render.py`'s per-frame posing code),
  not motion-captured or hand-animated by a rigger -- a crude, proof-of-concept gait, not
  production-quality animation. Mixamo (a common free source of pre-made animation clips) targets
  bipedal humanoid rigs and does not support an arbitrary quadruped-plus-wings skeleton like this
  Griffin's; a production pipeline would need either a real animator hand-keying every clip per beast
  (expensive, does not scale to 10 beasts x 7 clips) or a from-scratch procedural/physics-assisted gait
  system (unbuilt, unproven here).
- **Meshy inventing designs, still applies.** This pass's mesh is the same pass-3 remesh as before
  (section 2.7); its likeness caveats (2.1, 2.7 -- a plausible griffin, not a reconstruction of the
  *specific* approved illustration) are unchanged and unre-tested here.
- **SharpGLTF/skinning on Android, entirely unverified.** See the A35 estimate above -- this pass only
  built and ran the DesktopGL host.
- **Shader compile for Android.** `Toon.fx` compiled cleanly for the OpenGL/DesktopGL MGFX profile; an
  Android build would need it to also compile for MonoGame's Android GLSL ES target, untested here.

**Screenshots** (one fix round applied to the crest-placement and rendering bugs above before these
were taken; all still at the 540x960 desktop window size, not device-tested):

![Single Griffin, crest on](055/live3d_close_crest_on.png)
![Single Griffin, crest off](055/live3d_close_crest_off.png)

*Left: crest attachment on (teal plume, head bone-parented). Right: crest off -- same live mesh, same
frame, toggled with no re-render or re-animation.*

![Azure colour-form tint](055/live3d_tint_azure.png)

*The same beast with the `T`-key colour-form tint cycled to the cool azure variant -- a shader
multiply on the sampled texture, no new art, no accent mask.*

![24-beast stress test](055/live3d_board_24.png)

*The stress test's top step: 24 Griffins, each with its own randomised (desynced) animation-clock
offset, toon-shaded with outlines, on the hex board over the Verdant Hollow backdrop.*

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
- **Low-poly (pass 3, section 2.7) changes none of this arithmetic.** The `remesh` task re-topologises one
  already-generated, already-fused mesh; it doesn't segment it into swappable crest/wing sub-meshes, so a
  different crest or wing still means a different upstream generation (and, if a low-poly version is
  wanted, a second paid `remesh` call) per combination. If it were built, real-time 3D rendering is the one
  path that could avoid this by swapping textures/submeshes on a live mesh instead of pre-rendering frames
  (see section 6) -- not tested in this spike.

## 5. Pass criteria (from issue #55)

| Criterion | Path A (pre-rendered, passes 1-3) | Path A' (real-time, pass 4) | Path B |
| --- | --- | --- | --- |
| Steady 60 fps on a Galaxy A35-class phone | Unmeasured; expected non-issue as baked 2D sprites (same cost class as shipping content) -- see section 3 | **Measured on desktop only** (2.8): 92 fps avg at 24 beasts, uncapped, wide margin above 60 fps on this laptop's GPU; A35 unmeasured, reasoned estimate says CPU skinning cost is the real unknown, not fill rate -- see 2.8 | Unmeasured; expected non-issue, same reasoning, and it's strictly less new content than Path A |
| An approvable beast within a set number of hours per asset | Real risk on pass 1's mesh (rigging did not converge); **pass 3's low-poly remesh rigged cleanly on the first attempt with no repairs (2.7)**, but that's one beast, one mesh -- generality across the other 9 is unconfirmed | Same mesh/rig as pass 3, plus ~25-35 min this pass to author Idle/Move clips and export -- but animation is hand-keyed sinusoidal posing, not production quality, and has no Mixamo-equivalent shortcut for a quadruped (2.8) | Looks solid: ~3-5 min per beast once the tooling exists, reusing the existing `rigparts.py` convention |
| Cosmetic swaps without per-beast rework | **Fails as built, all three passes**: needs full re-generation (+ re-render, for pre-rendered output) per combination (section 4) | **Passes, for attachment-style cosmetics and colour form** (2.8): crest toggle and tint cycle both proven live, zero re-render/re-animation -- does *not* yet prove segmented-submesh swaps for a cosmetic baked into the base mesh's own geometry | **Passes**: parts/attachment swap, no rework |
| A trimmed device build that loads content | Out of scope for this mini-spike | Out of scope for this mini-spike (DesktopGL only; Android untested, see 2.8's A35 estimate) | Out of scope for this mini-spike |

*Low-poly (pass 3, section 2.7) row values above are folded into "Path A"; it's the same path, a third
generation. Path A' (pass 4, section 2.8) is real-time rendering of that same low-poly mesh, not a
fourth generation -- a different rendering strategy over the same asset. Likeness (not itself a
pass-criteria row, but see 2.1/2.7) reassessed as passing the producer's current "close, with
appropriate physiology" bar for the low-poly and textured passes, and unchanged (same mesh) for pass 4.*

## 6. Recommendation (revised 2026-09-30 after the fourth, real-time pass)

**Still continue the 2.5D + Spine path (or the code bone rig this spike demonstrates) as the default for
the beast roster.** That part of the call is unchanged by the fourth pass. **What the fourth pass does
change:** the single objection that has driven every version of this recommendation -- pre-rendered 3D
fails the cosmetic-swap criterion outright (section 4) -- is **no longer true of real-time 3D**. Section
2.8 built and measured a real-time MonoGame renderer and it passes the cosmetic-swap and colour-form
criteria live, with no re-render or re-animation step. That means real-time 3D is no longer a
theoretical "wrinkle" (as it was described after pass 3) -- it is now an evidence-backed, *credible*
alternative architecture, not just a rejected one. It is not being recommended as the default today
because it loses on cost and risk, not because it's structurally blocked the way pre-rendered 3D is:

- **New engineering cost pre-rendered 3D never had.** Path B reuses the game's existing `SpriteBatch`
  draw path with zero new engine code. Real-time 3D needed a new glTF loader/skinner, a new custom
  shader, and a new render path (section 2.8) -- real, one-time engineering investment a production
  adoption would have to pay up front, on top of per-beast asset cost.
- **Animation authoring has no Mixamo-equivalent shortcut for this game's quadruped/winged beasts**
  (2.8's risks) -- every clip, for every beast, would need either expensive hand-keying or an unbuilt
  procedural gait system. Path B's per-beast animation cost (a few minutes, section 2.7's time table)
  stays dramatically cheaper.
- **The A35 number that matters most (CPU skinning cost on mobile) is still unmeasured** (2.8) -- this
  pass used CPU skinning deliberately so the cost would be visible and honest, but that same choice
  means the one most mobile-relevant number in this whole spike is a reasoned estimate, not a
  measurement, pending an actual device test.
- **Likeness is unchanged** (same pass-3 mesh): still a plausible griffin, not a reconstruction of the
  specific approved illustration (2.1, 2.7).

**Unchanged after the second, textured pass (section 2.6) and the third, low-poly pass (section 2.7):**
a real texture, a real per-part rig, and a clean, well-behaved low-poly mesh all improved Path A's
*engineering* quality without touching the pre-rendering-specific cosmetic-swap combinatorics that drove
the original call (point 1 below, and section 4) -- that combinatorics objection specifically, not
real-time 3D in general, is what those two passes left standing.

**Likeness, reassessed (2026-09-30):** the producer's own bar for this criterion has moved to "close, with
appropriate physiology," not an exact match to the approved illustration. Re-read against that relaxed
bar (2.7), Path A's likeness gap -- point 3 below in its original, stricter form -- **softens**: the
low-poly (and textured) results are unambiguously griffins with correct physiology (beak, wings, four
legs, tail), just not reconstructions of the *specific* approved illustration. That's real progress on one
of the three original objections. It does not reach the cosmetic-swap combinatorics (section 4), which is
the objection that actually decides this call, and which likeness has no bearing on.

**The wrinkle the low-poly pass surfaced but couldn't test is now tested, and confirmed (2.8):** the
combinatorics problem is a *pre-rendering* problem specifically -- baking fixed 2D frames means every
cosmetic combination needs its own render. Section 2.8 built a real-time MonoGame renderer and proved
cosmetics (a head-bone-attached mesh toggle) and colour form (a shader tint) both work live, with zero
re-render or re-animation, the same "no rework" property Path B already has. That is real, measured
evidence, not a hypothesis -- but see the reasoning below (point 1) for why it still doesn't change
*today's* default recommendation: the combinatorics fix comes bundled with new costs (engineering,
animation authoring, unmeasured mobile CPU-skinning risk) that pre-rendered 3D never had to pay.

Reasoning:

1. The cosmetic-swap criterion is the one this whole spike (and issue #38) cares most about. **Pre-rendered**
   3D fails it outright, all three passes (1-3): a single-fused AI mesh -- textured or not, low-poly or
   not -- cannot cheaply take 20 discrete cosmetic options without a full re-render per combination.
   **Real-time** 3D (pass 4, section 2.8) passes it for attachment-style cosmetics and colour form, measured
   not hypothesised -- but adopting it means paying for a new render path, a new animation-authoring
   problem with no Mixamo shortcut, and an unmeasured mobile CPU-skinning risk (2.8), costs Path B simply
   doesn't have. That package of new costs, not the combinatorics objection itself, is why real-time 3D is
   not today's default even though it clears the one bar that mattered most.
2. Path B costs almost nothing beyond what's already built: it reuses the *already-approved* Griffin art,
   the *already-cut* rig parts, and produces a visibly correct, independently-articulated idle/move loop
   with about 20 minutes of new tooling. It is the lower-risk, lower-cost path by a wide margin.
3. Path A's likeness gap (2.1, reassessed 2.7) means a "best case" render still isn't a 3D reconstruction
   of the *specific* approved Griffin illustration -- it's a new, different-looking griffin that happens to
   read as the right species under the producer's current, relaxed bar. Depending on how strictly future
   art review reads "close, with appropriate physiology," the producer may still want to re-review and
   potentially re-approve per beast, on top of the cosmetic problem above.
4. The one thing Path A visibly wins on: it's a real skeleton in principle (rigging now works cleanly, on
   the first attempt, for the low-poly mesh -- 2.7), giving a camera more freedom (e.g., a free-orbiting
   Grove camera, noted as one of the two reasons to reconsider 3D in issue #55's notes). If a free Grove
   camera becomes a real requirement, that's worth a fresh, narrower spike specifically on rigging
   AI-generated meshes reliably -- which, per 2.7, may now be a materially smaller problem than 2.3 first
   found, at least for a remeshed low-poly mesh -- not a reason to adopt 3D for the whole roster today.

**Real-time 3D on a Galaxy A35-class phone:** see section 2.8's full estimate and reasoning. Short
version: 8,372 tris and a 1024x1024 texture per beast is a trivial fill-rate/vertex budget for the
A35's Mali-G68 MP5; the real unmeasured risk is CPU skinning cost on a much slower mobile CPU core and
per-draw-call driver overhead, not raw triangle throughput. Desktop numbers (2.8) show wide 60 fps
margin at up to 24 beasts on this laptop's GPU, but that is not a substitute for an on-device A35 test,
which remains not done.

### Risks if this recommendation is wrong

- If cosmetic scope shrinks a lot (fewer discrete options per species), Path A's combinatorial cost drops
  proportionally and might become tractable -- worth revisiting if `cosmetic-library.json` changes
  significantly.
- If a future AI-to-3D tool or tier reliably outputs clean, riggable, textured, segmented meshes, most of
  Path A's problems here (2.2, 2.3, section 4) could disappear; this spike used one paid-tier Meshy
  generation (plus one paid `remesh` follow-up, 2.7) and does not rule that out for other tools/settings.
- **Now confirmed rather than hypothetical (2.8):** real-time 3D rendering does dodge the
  cosmetic-combinatorics objection, for attachment-style cosmetics and colour form. If the animation-
  authoring problem (no Mixamo-equivalent for this game's beasts) or the unmeasured A35 CPU-skinning
  risk turn out to be smaller than feared, this recommendation is the one most likely to flip on new
  evidence -- see "Next steps".

### Next steps

- No further 3D work recommended as the *default* roster path off this spike -- Path B remains it. But
  unlike after pass 3, this is no longer "no further 3D work, full stop": if 2D cosmetic authoring later
  proves unsustainable at full scale (10 beasts x ~2-3 categories x several options each), or a
  free-orbiting camera becomes a real requirement, real-time 3D (not another pre-rendered-3D attempt) is
  now a credible re-open path, with a working starting point (`Tooling/Spike55/Live3D`) and a specific,
  named list of what to close first: an Android build and an actual Galaxy A35 `--bench` run (2.8's
  single biggest open unknown), and a real animation-authoring plan for a quadruped/winged skeleton with
  no Mixamo shortcut.
- Carry the code-bone-rig approach (Path B) forward as a live option if the Spine licence is ever judged
  not worth it: `Tooling/Spike55/rig2d.py` is a working starting point, though production use would want it
  ported to run inside the game (not as an offline PNG baker) so it gets the "no rework" benefit at
  runtime rather than needing pre-baked frames.
- If a free-orbiting camera or real-time 3D is ever prioritised, start from `Tooling/Spike55/Live3D`
  (2.8) and the low-poly `remesh` pipeline (2.7, `Tooling/Spike55/blender_lowpoly_render.py` /
  `blender_export_live.py`), not a fresh hi-poly generation or a from-scratch renderer -- both already
  exist and are known to work end-to-end on desktop.

## AI provenance

The Griffin 3D models used for Path A were generated by **Meshy (paid tier)**, from the already-approved 2D
Griffin illustration; output ownership is retained in all cases (paid tier, not the CC BY free tier).
Disclosed per `docs/art/art-brief.md`'s AI-disclosure rule.

- **Pass 1** (untextured, section 2.2): generated by the producer directly. Full record:
  `Tooling/ArtLab/provenance/spike55-griffin-meshy.md`.
- **Pass 2** (textured, `meshy-7.1`, section 2.6): generated 2026-09-30 via `Tooling/ArtLab/scripts/meshy.py`
  under a specific one-call, 30-credit spend authorisation. Full record:
  `Tooling/ArtLab/provenance/meshy-01a0f351-869e-7319-9820-e4b6e8b6b226.md`.
- **Pass 3** (low-poly `remesh` of pass 2, section 2.7): generated 2026-09-30 via
  `Tooling/ArtLab/scripts/meshy.py remesh`, under a specific one-call, 5-credit spend authorisation. Full
  record: `Tooling/ArtLab/provenance/meshy-remesh-01a0f38d-c409-702e-b976-62bff441b88f.md`. (A free local
  Blender Decimate attempt preceded this and failed -- shattered mesh, no texture -- see 2.7; it spent no
  credits and is not part of this AI-provenance list since it produced no usable output.)
- **Pass 4** (real-time 3D, section 2.8): **made zero Meshy API calls** (none were authorised for this
  pass). It re-used the pass-3 GLB already generated above, re-exporting it as a skinned, animated GLB
  (`griffin_live.glb`, committed at `Tooling/Spike55/Live3D/Content/model/`, 0.88 MiB -- small enough to
  commit directly per this pass's size budget, unlike passes 1-3's uncommitted GLBs). The cosmetic
  attachment mesh built for this pass (`crest_alt.glb`, also committed) is **not** AI-generated -- it's a
  small procedural mesh built directly in `blender_export_live.py` (a fanned quad-strip "plume"), with no
  Meshy involvement and no AI-disclosure implication.

None of passes 1-3's models are committed to the repository or shipped with the game
(`Tooling/Spike55/README.md` explains how to get your own copy to rerun Path A). Pass 4's two small GLBs
(`griffin_live.glb`, `crest_alt.glb`) are the one exception, committed under `Tooling/Spike55/Live3D/`
because they're both well under the spike's size budget.
