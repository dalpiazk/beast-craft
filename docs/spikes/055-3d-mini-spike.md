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
1.0.7** (MIT), **GPU-skins** each beast instance (a bone-palette vertex shader -- see "Lead-review fix
round" below for why this replaced an initial CPU-skinning implementation), and draws with a custom
toon + inverted-hull-outline effect (`Content/Effects/Toon.fx`, compiled by the MonoGame content
pipeline: a 2-3 band diffuse ramp, cool `#7C7AAE` shadow tint that's never pure black, a warm key
light, plus an ink-plum `#2E2A45` outline pass) over the Verdant Hollow backdrop
(`content/art/backdrops/r01/sun0/medium.png`) and a hex grid sized and laid out to match the real
game's own hex convention (`src/BeastCraft.Presentation/Board/HexLayout.cs`), orthographic camera,
portrait window (540x960 -- a 50% scale-down of the 1080x1920 target, so the window fits a normal
desktop monitor; see the README).

**Lead-review fix round (2026-09-30, same day).** A first pass through this section's numbers and
screenshots was reviewed and sent back with five findings: the 24-beast 1%-low framerate (9 fps) and
CPU skin time (9.4 ms) were the biggest unmeasured-A35 risk and avoidable; the beast faced the camera
head-on with wings straight up instead of the game's 3/4 side view; the natural tint rendered
noticeably more saturated/orange than the source texture and the approved illustration; the 24-beast
board was an oversized, overlapping clump, not beasts on hexes; and every screenshot's on-screen fps
read 0.0. All five are fixed; each is detailed in its own subsection below, and the numbers/screenshots
throughout this section are the **post-fix** ones (the original commit's numbers are quoted inline
where the delta itself is the finding).

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

**Cosmetic placement, two rounds.** The crest attachment first rendered draped in front of the face
like a bib (parented directly at the head bone's world matrix, no adjustment) -- the head bone's
bind-pose orientation doesn't line up with "up" in world space. Fixed with one small fixed local
re-orientation (scale down, tip the blades up and back) in `Game1`'s per-instance pose update. The
lead-review facing fix (below) then rotated every beast 90 degrees, which also rotated the crest's own
fan-spread axis edge-on to the new camera (a thin sliver instead of a plume); fixed with one more fixed
`RotationY` term compensating for the new facing. Both are one fixed correction each, not a full
per-bone rig -- this is a stand-in procedural mesh for the toggle proof, not hand-placed art, so that's
deliberately as far as cosmetic placement went.

**Lead-review fix 1: GPU skinning.** The original CPU-skinning implementation (blend the mesh on the
CPU every instance every frame, rewrite a per-instance `DynamicVertexBuffer`) was the direct cause of
the worst 1%-low numbers (9 fps at 24 beasts) and the dominant CPU cost (9.4 ms skin time at 24
beasts). Moved to GPU skinning: one **static, shared** `VertexBuffer` per model (built once in
`LoadContent`, carrying `BLENDINDICES0`/`BLENDWEIGHT0` per vertex -- see `SkinnedVertex.cs`/
`GpuMesh.cs`), a `Bones[16]` bone-palette array parameter in `Toon.fx`, and per-instance-per-frame CPU
work reduced to computing 11 joint matrices and uploading them as that one small array (no vertex
buffer touched at all). `MAX_BONES = 16` was chosen deliberately small for this 11-joint rig rather
than copying `SkinnedEffect`'s 72-bone budget -- 16 float4x4 is 64 vec4 vertex-uniform registers, safely
inside GLSL ES 2.0's spec-minimum guaranteed 128 vec4, where 72 bones would not fit (see Toon.fx's
header comment). Skin time dropped from 9.4 ms to 1.4 ms at 24 beasts; 1%-low fps rose from 9 to ~86
(see the updated numbers table below).

A second, smaller allocation source turned up while chasing the remaining Gen0 GC count: SharpGLTF's
own `Node.GetWorldMatrix(animation, time)` -- used once per node per instance per frame to evaluate the
pose -- walks the *full* ancestor chain from that node up to the scene root on every call and
measurably allocates doing it (confirmed: disabling the `Bones` effect-parameter upload entirely barely
moved the Gen0 count, ruling that call out). Fixed by having `AnimatedPose` walk the hierarchy itself
(a precomputed `ParentIndex` array, a small stack-allocated `Span<bool>` fixed-point sweep -- glTF node
order is not guaranteed parent-before-child, confirmed on this exact rig, so a naive single forward
pass isn't safe) using each node's own `Node.GetLocalTransform(animation, time)` instead, which doesn't
re-walk ancestors. Gen0 collections at 24 beasts over a 10-second bench: ~2,000 with `GetWorldMatrix`,
~1,230 after this change, ~1,300 in the final build (noise-level difference from the `Bones`-upload
isolation test). This residual is inside SharpGLTF's own per-node keyframe-sampler evaluation -- not
chased further (closing it fully would mean hand-rolling keyframe interpolation directly off the glTF
accessor data, bypassing SharpGLTF's animation API entirely, a bigger change than this fix round's
scope). Gen1 collections stayed at 0-1 and Gen2 at 0 across every beast count tested -- the residual
Gen0 pressure is real but never escalates to the more expensive collection generations.

**Lead-review fix 2: facing and camera.** The beast faced the camera head-on with wings straight up;
the game shows beasts in a 3/4 side view facing right
(`content/art/beasts/griffin/griffin.png`). Fixed two ways together: every beast instance now carries a
fixed 90-degree `FacingYaw` (its glTF-space forward axis, +Z, now points world +X instead), and the
camera -- initially moved, incorrectly, to view mostly along X -- was corrected back to viewing mostly
along Z (elevated, tilted down, the same axis the original frontal camera used) once a 24-beast test
revealed *why* that mattered: a hex board's rows are separated along world Z, and an orthographic
camera looking straight down an axis projects that axis away entirely, so a Z-axis camera is required
for rows to have any screen-space separation at all (see "Lead-review fix 4" below -- the facing and
board-layout fixes are coupled through this same camera axis choice). The net effect: beasts face right
under a camera that still reads the hex board correctly, both fixed by a 90-degree rotation on the
*beast* rather than moving the camera off the board-reading axis.

**Lead-review fix 3: colour.** The natural tint rendered noticeably more saturated/orange than both the
Meshy source texture and the approved illustration -- confirmed by sampling actual pixels from both
(the source texture's own gold, e.g. `(226,163,89)`, is already close to the illustration's
`(218,164,75)`; multiplying by the shader's original `LightColor` of `(1.0, 0.88, 0.68)` alone
reproduces the rendered colour almost exactly, i.e. the shader's lighting -- not a texture
colour-space bug -- was the cause). Checked and ruled out: MonoGame's default `Texture2D.FromStream`
load (`SurfaceFormat.Color`) applies no gamma/sRGB conversion, so this shader's straight multiply is
gamma-space compositing throughout, consistent with the rest of the game (`SpriteBatch`, also
gamma-space) -- not a colour-space mismatch to fix. Fixed by pulling `LightColor` (`(1.0, 0.97,
0.92)`) and `HighlightBoost` (`(1.03, 1.0, 0.96)`) back toward neutral so the texture's own colour
carries through; `ShadowTint` (the brief's required cool `#7C7AAE`, never-pure-black shadow) is
unchanged.

**Lead-review fix 4: board layout and hex scale.** The 24-beast screenshot was an oversized, overlapping
clump, not beasts on hexes -- two compounding bugs. First, hex spacing was picked with no reference to
either the game's own hex convention or the beast's actual size; replaced with a hex grid that mirrors
`HexLayout.cs`'s own pointy-top, "each row shifts half a column per row down" layout and ratio, sized
from the beast's own bind-pose bounding box (`HexBoard.SetScale`) so a standing beast roughly fills,
rather than bursts out of, its own cell. That alone didn't fix it: the 90-degree `FacingYaw` (fix 2)
swaps which bind-pose axis maps to which world axis, and the first version measured the wrong one for
hex-column spacing; fixed by sizing hex spacing from the larger of the beast's two horizontal bind-pose
extents, covering whichever axis ends up where after the yaw. Second, and the one that actually
explained the "clump" look even with correct spacing: the camera was viewing almost straight down the
Z axis (the axis hex rows are separated along), which an orthographic projection collapses entirely --
tilt alone (24 degrees) wasn't enough to give rows real screen separation. Fixed by increasing the
board-camera tilt to 48 degrees. The camera's projection-fitting math was also rewritten to measure the
actual **view-space** extent of every instance (not a world-axis-aligned guess), so it's robust to
camera direction generally, not just retuned for this one fix.

**Lead-review fix 5: screenshot fps stats.** Every screenshot's on-screen `fps avg`/`fps 1% low` read
`0.0`. Not a warm-up timing issue (a warm-up was added regardless, see the README's `--screenshot`
docs) -- a real, standalone bug in `StatsTracker.RecordFrame`'s rolling-window trim: it defaulted to
trimming the *entire* list (`cut = _frameMs.Count`, not `0`) whenever the list's total duration was
still under the 3-second window, which was true on *every single call* (the list was wiped to near-empty
by the end of the previous one), so `SampleCount` was permanently 0 and every fps stat -- on-screen and
in every screenshot taken before this fix, including all four in the original commit -- read 0.0. Fixed
by correcting the default. The bench JSON numbers were never affected (`WriteBenchResult` computes its
own stats directly from the full `_benchFrameMs` list, independent of `StatsTracker`), so the numbers
reported from the original commit's `--bench` runs were accurate; only the on-screen overlay and
screenshots were wrong.

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

**Measured numbers, post-lead-review-fix (this machine: Intel Arc 140V laptop, DesktopGL, Release
build, vsync disabled for true uncapped throughput via `--bench`, GPU skinning):**

| Beasts | fps avg | fps 1% low | Draw calls | Triangles | Skin time (CPU-side) | Managed mem | GPU mem (est.) | Gen0 GC | Gen1 GC | Gen2 GC |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 991 | 182 | 6 | 17,192 | 0.05 ms | 11.1 MB | 7.2 MB | 115 | 1 | 0 |
| 3 | 1,011 | 236 | 14 | 51,576 | 0.20 ms | 7.4 MB | 7.2 MB | 354 | 1 | 1 |
| 12 | 742 | 179 | 50 | 206,304 | 0.74 ms | 11.2 MB | 7.2 MB | 1,036 | 1 | 0 |
| 24 | 471 | 86 | 98 | 412,608 | 1.39 ms | 12.6 MB | 7.2 MB | 1,315 | 1 | 0 |

(10-second `--bench` runs; "draw calls"/"triangles" are per-frame totals -- each beast is 2 draw calls
x 2 passes (toon + outline) for body and crest; Gen0/1/2 GC counts are collections observed *during*
the 10-second bench window, via `GC.CollectionCount`.) For comparison, the **original CPU-skinning**
numbers this fix round replaced: at 24 beasts, 92 fps avg / **9 fps 1% low** / 9.43 ms skin time (see
"Lead-review fix 1" above for the two changes -- GPU skinning, then a SharpGLTF allocation fix -- that
produced this improvement). 1%-low fps at 24 beasts improved **~9.5x** (9 -> 86); skin time dropped
**~6.8x** (9.43 ms -> 1.39 ms); GPU memory is now flat with beast count (a shared static vertex buffer
per model, not one per instance -- see `EstimateGpuBytes`'s comment) instead of scaling linearly.
Every beast count tested stays at or above 60 fps on *both* the average and the 1%-low number on this
desktop GPU, with wide margin even at 24 beasts uncapped. GPU skinning was always an option the task
brief explicitly allowed ("MonoGame's built-in `SkinnedEffect` is acceptable for a first pass, or a
custom toon effect... GPU or CPU skinning"); CPU skinning was chosen for the original commit
specifically so "skinning time" would be a directly measurable, honest CPU-side number -- GPU skinning
keeps that same measurability (the CPU side is now just 11 joint-matrix computations and one small
array upload, still timed and reported) while removing the per-frame vertex-buffer rewrite that caused
the 1%-low regression.

**Galaxy A35 (Exynos 1380 / Mali-G68 MP5) estimate -- unmeasured, reasoned from these numbers:**

- 8,372 tris and one 1024x1024 texture per beast is a trivial vertex/fill-rate budget for a mobile
  GPU in the A35's tier (Mali-G68 MP5 comfortably handles several times this in current mobile titles);
  the real mobile-specific risk is **not** raw triangle/fill throughput.
- **CPU skinning cost is no longer the standout risk it was.** With GPU skinning (the lead-review fix),
  the CPU side per beast per frame is now just 11 joint-matrix computations and one small array upload
  (1.39 ms for 24 beasts on this laptop, down from 9.43 ms with the original CPU-skin implementation --
  see the numbers table above), a much smaller, much more mobile-CPU-tolerant number even after a
  pessimistic 3-6x mobile-CPU slowdown factor.
- The remaining real risks are (a) **per-draw-call driver overhead on a mobile OpenGL ES driver**,
  typically worse than desktop drivers -- at 98 draw calls/frame for 24 beasts, this is now the more
  relevant mobile-optimisation target (instancing or batching would cut it) than CPU skinning was;
  (b) **shader compile/portability for GLSL ES**, now with the added complexity of the `Bones[16]`
  array parameter (`Toon.fx` was only compiled and tested for the OpenGL/DesktopGL profile via MGFX
  here -- chosen deliberately small, 16 bones vs. `SkinnedEffect`'s 72, specifically to stay inside
  GLSL ES 2.0's spec-minimum vertex-uniform guarantee, but the *actual* Android GLSL ES compile is
  still untested); (c) **content pipeline and SharpGLTF behaviour on Android**, entirely untested in
  this pass (MonoGame's Android host uses a different asset-loading path than
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
  engineering (SharpGLTF integration, GPU skinner, custom toon+outline shader, camera/hex board,
  stress/bench/screenshot harness) took the bulk of this session's time -- and a same-day lead-review
  fix round (GPU skinning, facing/camera, colour tuning, hex-board layout, an on-screen-stats bug) added
  a comparable second block of engineering time on top, all still one-time and still not recurring per
  beast. It is real engineering investment a production real-time-3D path would need to have already
  paid before any beast benefits from it -- unlike Path B (section 4), which reuses the game's existing
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

**Screenshots** (retaken after the lead-review fix round above -- facing/camera, colour, board layout
and the on-screen-stats bug all affect what these show; all still at the 540x960 desktop window size,
not device-tested; on-screen stats are real numbers now, not the earlier `0.0`):

![Single Griffin, crest on](055/live3d_close_crest_on.png)
![Single Griffin, crest off](055/live3d_close_crest_off.png)

*Left: crest attachment on (teal plume, head bone-parented). Right: crest off -- same live mesh, same
frame, toggled with no re-render or re-animation.*

![Azure colour-form tint](055/live3d_tint_azure.png)

*The same beast with the `T`-key colour-form tint cycled to the cool azure variant -- a shader
multiply on the sampled texture, no new art, no accent mask.*

![24-beast stress test](055/live3d_board_24.png)

*The stress test's top step: 24 Griffins, each with its own randomised (desynced) animation-clock
offset, toon-shaded with outlines, spread across three rows of the hex board (8x11 arena, matching the
game's own `HexLayout.cs` convention and the Verdant Hollow backdrop's painted size) over the Verdant
Hollow backdrop. Some wingtip overlap between neighbours remains (beasts are drawn larger than their
own hex footprint, same as the 2D sprites in `docs/spikes/055/board_mock_lowpoly.png`), but beasts now
read as individually placed on the board rather than one overlapping clump.*

### 2.9 Mobile budgets (research)

A separate research pass gathered sourced evidence on real-time 3D budgets for mid-range Android
(target: Galaxy A35-class, Exynos 1380 / Mali-G68 MP5), specifically to turn section 2.8's "A35
unmeasured" caveats into numbers with citations rather than guesses. Full research with every source
link, confidence flags (**evidence** vs **[secondary]**/**[speculative]**), and reasoning is not
reproduced here -- this is the load-bearing summary; go to the research itself for the primary-source
detail behind any number below.

**Per-unit tier budget (A35-class target):**

| Unit tier | Triangles | Texture | Bones | Draw calls |
| --- | --- | --- | --- | --- |
| Player beast (<=4 on screen) | 6,000-8,000 | 1K ASTC (ETC2 fallback) | 10-20 (<=30 ceiling) | 2 (toon fill + outline) |
| Giant boss (1 on screen) | 12,000-16,000 | 1-2K ASTC | 20-30 | 2 (fill + outline) |
| Small/swarm enemy (up to 24 on screen) | 800-1,500 | Shared atlas, <=512 each | <=15, or VAT (no bones) | Target <=2-4 **total** for the whole swarm, not per-unit |

Other headline numbers: **1K ASTC primary / ETC2 fallback** texture compression (ASTC on ~75%+ of
active Android devices, ETC2 ~90%+); **30 fps hard cap in battle** (turn-based has no gameplay need for
60, and mid-range Android sustains only 50-65% of peak GPU clock under continuous load -- a 60fps start
can fall to 33-40fps within 5 minutes); **draw calls ~100-150** as a 2026 mid-range ceiling, explicitly
framed across sources as the *bigger* lever than raw triangle count once many small skinned meshes are
on screen -- directly relevant here, since 24 individually-drawn, individually-outlined swarmlings
alone would already be ~96-100+ draw calls before board/UI.

**Evidence, separated from recommendation** (per topic; sources inline):

- **Triangle counts**: no single authoritative number exists. Unity's own official guidance is
  conservative (~300-1,500 tris/mesh "for good results" on mobile,
  https://docs.unity3d.com/560/Documentation/Manual/ModelingOptimizedCharacters.html); mobile-art blog
  consensus runs higher (hero 3,000-8,000+ tris, crowd 800-1,500 -- MessyPoly
  https://www.messypoly.com/learn/polygon-budgets-mobile-games, Creatuneagency
  https://www.creatuneagency.com/en/blog/low-poly-3d-models-for-mobile-games/). The tier table above
  uses the blog consensus as the working target (Unity's figure treated as a conservative floor).
- **Total per-frame triangle budget**: no official Google/Arm/Qualcomm fixed number; Arm frames it as a
  *cycle budget* derived from device clock/target fps/resolution, not a poll count (Arm, "GPU Processing
  Budget Approach to Game Development",
  https://developer.arm.com/community/arm-community-blogs/b/mobile-graphics-and-gaming-blog/posts/gpu-processing-budget-approach-to-game-development).
  100-300K visible tris/frame is a recurring secondary-sourced ceiling (Creatuneagency, as above); our
  worst-case battle geometry (3 beasts/avatar + 24 swarmlings, both passes) stays comfortably under it
  even unoptimised.
- **Texture format/size**: ASTC ~75%+ device support, ETC2 ~90%+ (Android Developers, "Textures",
  https://developer.android.com/games/optimize/textures); the existing Griffin's 1K base-colour texture
  matches that page's own example of a "right-sized" (not oversized) mobile character texture.
- **Bone counts**: Unity community consensus keeps mobile skinning under ~30 bones
  (https://discussions.unity.com/t/skinned-mesh-max-number-of-bones-in-total/936356); Unreal caps
  mobile skeletal-mesh sections at 75 bones
  (https://dev.epicgames.com/documentation/en-us/unreal-engine/skeletal-mesh-rendering-paths-in-unreal-engine);
  the GLES 3.0 spec's guaranteed-minimum 1024 vertex-uniform components is the underlying hard
  constraint a naive 4x4-per-bone uniform array runs into around 64 bones. The Griffin's 11 joints (and
  `Tooling/Spike55/Live3D`'s `MAX_BONES = 16`, chosen for exactly this reason -- see
  `Toon.fx`'s header comment) is comfortably inside every cited ceiling.
- **GPU instancing for skinned meshes**: not supported by Unity's `SkinnedMeshRenderer` at all, a
  platform-level limitation, not Unity-specific
  (https://docs.unity3d.com/Manual/GPUInstancing.html); MonoGame's own GLES/Android backend has an open,
  long-standing gap for instanced geometry drawing generally (not just skinned), tracked since 2018
  (https://github.com/MonoGame/MonoGame/issues/6292) -- directly relevant to how the fifth pass (below)
  approaches swarm rendering.
- **Vertex Animation Textures (VAT)**: the standard industry technique for many animated crowd
  characters, foundational reference NVIDIA GPU Gems 3 Ch.2
  (https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-2-animated-crowd-rendering);
  bakes animation into a texture sampled in the vertex shader, compatible with ordinary (non-skeletal)
  instancing/batched drawing -- which is exactly what sidesteps MonoGame's skinned-instancing gap above.
- **Thermal/battery**: mid-range Android sustains only 50-65% of peak GPU clock under continuous load
  (Kryozon, secondary but widely converged,
  https://www.kryozon.com/blogs/cooling-hub/how-to-cool-down-phone-throttling-on-android); Android's own
  Game Mode FPS-throttling docs confirm the platform expects and rewards fps capping (~50% GPU power
  reduction at a battery-mode cap;
  https://developer.android.com/games/optimize/adpf/gamemode/fps-throttling) and call 30fps "a
  recommended starting target frame rate."

**Recommendation carried forward into the fifth pass (below):** hero-tier GPU skinning (already built,
section 2.8) for player beasts, a much lower-poly VAT path for swarm-tier enemies (no per-unit skinning
or instancing at all), a shared atlas/material across the whole swarm, and a 30fps battle cap as the
default target for on-device numbers -- tested below against an uncapped-throughput bench for
comparison, same as section 2.8's methodology.

### 2.10 Fifth pass: swarm via VAT (2026-09-30)

Per the producer's continued interest in the 3D route, this pass tests the hero-plus-swarm scene
section 2.9's research called for: hero-tier GPU skinning (already built, section 2.8) for the player
beasts, plus a much cheaper, much-lower-draw-call path for many small swarm-tier enemies. Scene: 3
GPU-skinned Griffins + 24 swarm-tier "Swarmling" units on hexes, 11x15 arena (matching the research
pass's "game scale" -- a Large arena per `docs/art/hollow-art-slots.md`), both an uncapped bench and the
research-recommended 30fps battle cap.

**Blocked spend, and what this pass built instead of a Meshy Swarmling.** The producer authorised
exactly two paid Meshy calls (image-to-3d of the approved Swarmling art at
`content/art/enemies/swarmling/swarmling_hollow.png`, 30 credits; a remesh to ~1,200 tris, 5 credits).
A `--dry-run` confirmed the exact 30-credit image-to-3d request. The real `--yes` call was **refused by
this environment's own safety classifier** ("Real-World Transactions"), independent of the task-level
authorisation -- a tool-level restriction this session could not get past, and did not attempt to work
around. **Zero Meshy credits were spent this pass.** Rather than skip the swarm-rendering engineering
entirely, `Tooling/Spike55/blender_export_vat_swarmling.py` builds a small procedural low-poly
"swarmling" body (1,688 tris, 860 verts -- in the authorised ~1,200-tri target's range) textured with
the real approved Swarmling illustration's own sampled palette, so the *technique* is actually built and
measured, honestly labelled as a stand-in body, not a Meshy output. If Meshy spend authorisation reaches
this session by a channel the classifier accepts, this is the one clearly-scoped follow-up: swap the
procedural body for a real Swarmling generation/remesh through the same pipeline -- everything else
(rig, bake, merged-batch rendering) carries over unchanged.

**What "VAT" means in this section's title, and why it isn't literally what got built.** The task brief
asked for Vertex Animation Textures: bake animated position/normal per frame into a texture, sample it
in the vertex shader, so many instances can share one draw call with no per-instance skinning cost. This
was attempted first and **does not compile**: MonoGame 3.8.5's DesktopGL effect compiler has no support
for sampling a texture in the vertex shader stage at all -- isolated to a 6-line minimal repro (a bare
`tex2Dlod` call, nothing else) that fails identically to the full implementation: `"invalid DCL register
type for this shader model"` / `"TEXLD using undeclared sampler"`, inside MonoGame's MojoShader-based
DX9-bytecode-to-GLSL translation step. This is a **harder, more definitive constraint than section 2.9's
own "unverified on some Android GPUs" VTF caveat**: it doesn't build for *desktop* GL at all, so it would
never have reached an Android device to test in the first place, and no shader-side workaround was found
in this pass. Pivoted instead to GPU skinning via a **per-instance-sliced bone-array offset**: the same
`Bones[]`-uniform-array technique already proven working for the Griffin (section 2.8's `SkinPosition
Normal`), just with a bigger shared array (`SwarmBones[72]` = 24 swarmlings x 3 bones each) and a
per-vertex `InstanceId` selecting which instance's slice of that array a given copy reads. Each
swarmling's animation is still pre-baked per frame in Blender (3 bone skin matrices x 20 frames -- tiny,
`swarmling_bones.bin`), matching the task brief's "bake once, sample cheaply at runtime" spirit even
though the runtime-sampling mechanism changed from a texture fetch to a uniform-array index. See
`Content/Effects/Toon.fx`'s "Fifth pass" section and `Tooling/Spike55/blender_export_vat_swarmling.py`'s
module docstring for the full account.

**The merged-batch property survives the pivot, which is the part that actually matters for Android.**
Whichever way each vertex gets its final position, the whole swarm still draws from **one shared static
vertex buffer** (`GpuMesh.BuildSwarmMerged`: N copies of the swarmling's bind-pose vertices, index
buffer offset per copy) in **exactly 2 draw calls total** (toon fill + outline), regardless of swarm
size -- confirmed at 24 instances below. This is the property the research pass (2.9,
`MonoGame/MonoGame#6292`) flagged as load-bearing for Android specifically because MonoGame's GLES
backend has no reliable instancing support; a merged static buffer sidesteps that gap entirely, and nothing
about the VAT-to-bone-array pivot changes that.

**A genuinely new, separately-flagged Android risk the pivot introduces: uniform-array budget.**
`SwarmBones[72]` (24 instances x 3 bones, 4x4 matrices) is 72 x 16 = 1,152 floats = **288 vec4
registers**. GLES 2.0's spec-guaranteed minimum vertex-uniform budget is 128 vec4; **GLES 3.0's is 256
vec4 -- this array does not safely fit inside even the GLES 3.0 guaranteed minimum**, though it compiles
and runs without issue on this desktop GPU (desktop uniform budgets are far larger in practice). This is
exactly the class of problem section 2.9's research flagged in the abstract (Qualcomm's recommendation
to use 3x4 instead of 4x4 bone matrices, or a texture-buffer/UBO-based approach, for higher bone counts)
-- now concretely hit by this pass's own design, not a hypothetical. A production implementation would
need one of: 3x4 matrices (cuts this to 216 vec4, still tight), fewer bones per swarmling (2 instead of
3 -- root+combined-body-head -- cuts to 192 vec4), a smaller simultaneous-swarm cap, or genuinely
texture-based instance data via a mechanism that isn't vertex-stage texture sampling (this pass's other
finding says that path is closed for MonoGame/DesktopGL specifically, but a UBO/uniform-buffer approach
is a different mechanism, untested here).

**Measured numbers (this machine: Intel Arc 140V laptop, DesktopGL, Release build):**

| Scene | fps avg (uncapped) | fps 1% low (uncapped) | fps avg (30fps cap) | fps 1% low (30fps cap) | Draw calls | Triangles | Skin time | GPU mem (est.) | Gen0 GC |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 24 swarmlings alone | 1,700 | 329 | 30.0 | 21.6 | 4 | 81,024 | 0.002 ms | 9.9 MB | 0 |
| 3 Griffins + 24 swarmlings | 960-1,024 | 126-206 | 30.0 | 26.0 | 16 | 132,600 | 0.11-0.28 ms | 9.9 MB | ~60-335/10s |

(10-second `--bench` runs, or 2s for the smallest smoke-test row shown in the range above; GPU memory
estimate is flat across swarm-alone vs. full-battle because the swarm's shared swatch texture and merged
buffer are sized for the fixed 24-instance cap either way.) The swarm's own CPU cost is close to
immeasurable (0.002 ms for 24 instances -- a plain per-instance array-index lookup, no per-vertex work,
no matrix hierarchy walk) and produces **zero Gen0 GC** on its own (confirmed with `--battle --griffins
0`); all Gen0 pressure in the full-battle row comes from the Griffins' own pose evaluation (section
2.8's residual SharpGLTF allocation, unchanged here). Both scenes sustain the research-recommended 30fps
cap solidly (29.99 fps avg in both), with 1%-low cap numbers (21.6-26.0 fps) close enough to the 30fps
target that this desktop GPU is nowhere near the bottleneck -- the uncapped throughput (nearly 1,000-
1,700 fps) shows the actual margin. Draw calls stay at exactly 16 for the full battle regardless of
swarm size changes (griffins: 3 x 4 = 12 [body+crest x toon+outline] + swarm: 2 x [toon+outline] = 2 +
backdrop + hex grid = 2; the swarm's own 2 draw calls would stay 2 even at the 32-instance array cap).

**Galaxy A35 estimate, building on section 2.9's budgets:**

- Triangle/fill-rate cost is trivial either way (81,024-132,600 tris/frame total, well under the
  100-300K/frame secondary-sourced ceiling from section 2.9, and the swarm-tier per-unit budget --
  1,688 tris -- is close to, if a bit over, the 800-1,500 tri-per-swarmling target that section
  recommended).
- **Draw calls are now genuinely small** (16 for the full battle, 4 for the swarm alone) -- comfortably
  under section 2.9's ~100-150 mid-range ceiling, and this is the number the merged-batch design was
  built specifically to keep small.
- **CPU cost for the swarm itself is a non-issue** (0.002 ms measured for 24 instances) -- the
  GPU-skinned Griffins' own per-instance cost (section 2.8, already measured and small) remains the
  larger of the two CPU-side costs, and that's already accounted for in section 2.8's own A35 estimate.
- **The uniform-array budget risk above is the one new, swarm-specific Android unknown** this pass
  surfaces -- not fill rate, not draw calls, not CPU cost, but whether a 288-vec4 `SwarmBones[]` array
  actually compiles and runs correctly on a real Mali-G68 MP5 GLES driver, given it exceeds the GLES
  3.0 spec's own guaranteed minimum. **The exact test needed:** the same Android-build-plus-on-device
  `--bench` test section 2.8 already named, specifically checking whether this shader compiles at all
  on-device (not just whether it runs fast) before trusting any of this pass's other numbers for mobile.

**Per-enemy asset cost, honestly estimated from what was actually done this pass:**

- Meshy generate + remesh: **0 credits spent** (blocked, see above) -- the 30+5 = 35-credit
  authorisation is unused and still available for a follow-up pass with a real Swarmling generation.
- Blender rig/anim/bake authoring (`blender_export_vat_swarmling.py`), *given the Griffin pipeline's
  weighting/repair code already existed to adapt*: roughly 20-30 minutes of active work this session,
  most of it spent on the procedural body construction (new) rather than the rig/weighting/animation
  parts (adapted almost directly from `blender_export_live.py`).
- **Not a per-enemy cost, but a real one-time cost this pass paid for the whole swarm tier:** the
  merged-batch runtime engineering (`GpuMesh.BuildSwarmMerged`, `SwarmVertex`, `SwarmlingSkinnedModel`,
  `Toon.fx`'s `SwarmBones[]` techniques) plus the VAT-to-bone-array pivot (diagnosing the vertex-texture-
  fetch compile failure, redesigning around it) took the bulk of this session's time -- a materially
  larger one-time engineering cost than section 2.8's GPU-skinning work alone, because it included a
  full architecture change mid-pass. It does not recur per swarmling once built.

**Screenshots** (one look-over pass per the task brief; both still at the 540x960 desktop window size,
not device-tested):

![3 Griffins + 24 swarmlings battle](055/live3d_battle_3plus24.png)

*The full battle scene: 3 GPU-skinned Griffins in front, 24 swarm-tier units filling the rows behind
them, on the 11x15 arena. Critical look: the swarm reads as small, numerous, and clearly separate from
the hero-tier beasts in both scale and shading weight (no outline-heavy silhouette competing with the
Griffins') -- matching section 1.7's research recommendation to reserve full outline treatment for
hero/boss-tier units, though this pass gave swarmlings the same outline pass as everyone else rather
than implementing that cheaper-swarm-shading recommendation (a real, not-yet-taken next step, not a
finding). A couple of swarmlings show a thin dark streak artefact (a stray vertex-weight mismatch, the
same general class of issue the Griffin passes hit and documented in section 2.3/2.6 -- not chased
further here, left as a known cosmetic issue on this stand-in placeholder body).*

![Swarmling close-up](055/live3d_swarmling_close.png)

*A single swarmling at close range: toon-shaded olive-gold body (sampled from the real Swarmling
illustration's palette), visible ink-plum outline, legs/antennae mostly foreshortened under the body at
this camera's elevated board-tilt angle. Reads clearly as a small, distinct creature type at this
distance, not just a coloured blob -- reasonable for a stand-in swarm-tier placeholder.*

### 2.11 Fifth-pass fix round: the real Swarmling, register budget, camera framing (2026-09-30)

Section 2.10's swarm-rendering engineering was measured against a procedural placeholder body because
the real Meshy call was blocked by this environment's own safety classifier. This fix round closes that
gap: the producer ran the two authorised Meshy calls directly (outside this session's own tool-level
restriction) and this pass rigs, animates, and integrates the result, fixes the battle camera's framing,
and re-measures.

**Provenance (35 credits total, run by the producer directly, not through the spend-guarded CLI):**

| Call | Task id | Credits | Output |
| --- | --- | ---: | --- |
| `image-to-3d` (approved Swarmling illustration) | `01a0f424-1644-75af-8cdb-e25ef6452845` | 30 | Untextured-shape + 2K base-colour texture mesh |
| `remesh` (target ~1,200 tris) | `01a0f426-b36a-7051-8603-3b77b80b9616` | 5 | 1,234 tris, 2,454 verts as exported (UV-seam-duplicated), base_color + normal + metallic_roughness textures |

Full records: `Tooling/ArtLab/provenance/meshy-01a0f424-1644-75af-8cdb-e25ef6452845.md` and
`Tooling/ArtLab/provenance/meshy-remesh-01a0f426-b36a-7051-8603-3b77b80b9616.md` (the second was written
this pass -- the CLI only writes a provenance record for calls it makes itself, and this remesh was run
directly by the producer).

**Asset pipeline (`Tooling/Spike55/blender_export_live_swarmling.py`, new -- reuses
`blender_export_live.py`'s import/weld/texture-pick/downsize/normalise pipeline verbatim in shape, same
as every prior pass's script did):**

1,234 raw tris weld to **620 true verts, 1 connected component** (same "glTF UV-seam vertex splitting,
not real fragmentation" story as the Griffin's own remesh, section 2.7) -- confirming this remesh task
produces a coherent, non-shattered low-poly mesh for the Swarmling too, not just the one Griffin data
point.

- **Rig: 6 bones** -- `bone_body` (the armature's own root; no separate identity root bone, unlike the
  Griffin's 11-bone rig), `bone_head`, and 4 leg bones (`bone_leg_front_l/r`, `bone_leg_back_l/r`). Bone
  placement was chosen from `inspect_orientation.py`'s probe renders of the raw remesh GLB (front faces
  local -Y, same convention as the Griffin's own remesh, horns splay along +-X near the head), not
  guessed blind, though exact placement is still an approximation (fractions of the mesh's own bounding
  height) rather than derived from a skeletal analysis of the mesh.
- **No horn bones**, by deliberate choice, not an oversight: two more bones would have meant either a
  smaller per-batch swarm cap or a smaller bone budget elsewhere (see the register-budget arithmetic
  below), for motion (horns wagging independently of the head) that would not read at swarm scale/camera
  distance anyway. The horns are weighted rigidly to `bone_head` instead -- they move with the head, just
  not independently.
- **Automatic (heat-map) weighting converged on the first attempt**: 0 / 620 vertices unweighted, no
  voxel-remesh-donor fallback needed (unlike the Griffin's first two Meshy passes, section 2.3/2.6, which
  both needed it) -- consistent with the Griffin's own low-poly `remesh` mesh also converging cleanly
  (section 2.7), suggesting the `remesh` endpoint's output topology, not mesh size or creature type, is
  what makes heat weighting reliable here.
- **Texture**: downsized 2048x2048 -> 512x512 (within the task's 512-or-1K budget; 512 chosen since a
  swarm unit is small and far from camera even at the close-up shot's own zoom level), JPEG-encoded in
  the export (quality 88, same as the Griffin) to keep the GLB small.
- **Idle (48 frames, 2s @24fps) and Move (24 frames, 1s @24fps)** actions, named "Idle"/"Move" exactly
  like the Griffin's clips. Idle is a gentle bob/sway/head-tilt; Move is a four-legged diagonal trot
  (front-left+back-right swing together, opposite front-right+back-left) plus a faster bob.
- **Export: `swarmling_live.glb`, 191,900 bytes (0.183 MiB)** -- well under the <1 MB target, committed at
  `Tooling/Spike55/Live3D/Content/model/swarmling_live.glb`.

**A genuine architecture change, not just a new asset: the runtime now loads the Swarmling exactly like
the Griffin.** The procedural placeholder's custom `swarmling_mesh.bin`/`swarmling_bones.bin`/
`swarmling_meta.json` format (pre-baked bone matrices per frame, nearest-frame-snapped at runtime, no
glTF involved) is retired. `SwarmlingSkinnedModel.cs` is deleted; the runtime loads `swarmling_live.glb`
with `GltfSkinnedModel.Load` (the same class the Griffin uses) and evaluates its pose every frame via
`AnimatedPose` (real interpolated animation sampling at arbitrary time, not a snap to one of a handful of
baked frames) -- what stays swarm-specific is purely the *batching* (see below), not the asset format or
the pose-evaluation method.

**Register-budget recompute, as the task brief asked.** Section 2.10 flagged `SwarmBones[72]` (24
instances x 3 bones, full 4x4 matrices = 288 vec4) as already over GLES 3.0's guaranteed-minimum 256 vec4
vertex-uniform budget, before this pass even doubled the bone count per swarmling. Two changes claw this
back under budget:

1. **3x4 affine matrices** (3 vec4 registers/bone, dropping the `[0,0,0,1]` row every bind-pose skin
   matrix has anyway) instead of a full 4x4 (4 vec4/bone) -- a 25% cut on its own. Implemented as a flat
   `float4 SwarmBoneRows[]` array (3 plain rows per bone) rather than an HLSL `float3x4[]` array, so the
   register count is unambiguous by construction (Game1.cs uploads it via
   `EffectParameter.SetValue(Vector4[])`, one `Vector4` = one register, no reliance on how a host API
   might pack a non-4x4 matrix array).
2. **Fixed-size batches of `SWARM_BATCH_CAPACITY = 12` swarmlings** instead of one flat 24-instance array
   -- `GpuMesh.BuildSwarmMerged` is called once per batch, each with its own merged `VertexBuffer` and its
   own slice of bone data uploaded before that batch's 2 draw calls (toon + outline).

**Final count: `SWARM_BATCH_CAPACITY(12) x SWARM_BONES_PER_INSTANCE(6) x 3 vec4/bone = 216 vec4` --
216 of GLES 3.0's guaranteed-minimum 256 (84%)**, leaving ~40 vec4 of headroom for `ViewProjection` (4)
and the handful of lighting/outline uniforms (`LightDirection`, `LightColor`, `ShadowTint`,
`HighlightBoost`, `TintMultiply`, `OutlineThickness`, `OutlineColor` -- 7 small uniforms, comfortably
under 40 vec4 even if each is packed into its own register). A 24-swarmling battle now draws the swarm in
**2 batches (4 draw calls: 2 batches x toon+outline)** instead of section 2.10's single merged buffer (2
draw calls) -- a real, measured cost of closing the register-budget risk, not a free fix; see the bench
table below.

**Camera framing (task brief item 3): the battle camera now frames the fixed arena, not wherever units
happen to be.** Section 2.10's screenshot (reproduced below for comparison) read as "too far out" --
Griffins tiny, swarm unreadable -- because the camera fit itself to the *actual occupied cells'*
view-space bounding box (`RebuildCamera`'s original design, still used by the single-species stress
test), and a sparse, off-centre placement (3 Griffins in rows 2-5, 24 swarmlings starting row 6) doesn't
by itself tell the camera how wide the *board* is. `Game1.RebuildCameraArena` (new) instead samples every
cell centre across the real 11-column arena (up to 15 rows for the "arena" shot, a narrower column+row
crop for a "front" close-up of the front line -- `--zoom arena|front|close`) and fits to that fixed grid,
independent of how many units are actually on the board. `--screenshot` also now renders at the task
brief's full portrait **1080x1920** (previously the same half-scale 540x960 window used for interactive
play and `--bench`, which this pass keeps at 540x960 for comparability across passes).

*One fix round, as the task brief allows:* the first version of `RebuildCameraArena` only narrowed the
**row** range for `--zoom front`, leaving all 11 columns sampled -- since the ortho fit is column/width-
bound (11 columns alone already decides the frame's width in the "arena" shot), narrowing rows barely
changed anything (confirmed by comparing the two renders side by side). Fixed by narrowing both the
column and row range together for `front`.

**Facing.** The Swarmling and Griffin share an identical bind-pose front convention -- confirmed via
`inspect_orientation.py`'s probe renders of both raw remesh GLBs, both facing local -Y in Blender before
export -- so a Y-axis yaw exactly 180 degrees apart from the Griffins' own `FacingYaw` (`RotationY(-90°)`)
is guaranteed, by construction, to face the swarm the exact opposite screen direction, not just
approximately: `SwarmFacingYaw = RotationY(+90°)`. Griffins keep their existing, already-reviewed facing;
swarmlings now visibly face back across the front line toward them, instead of the placeholder's
un-rotated bind pose (its squat, roughly radially-symmetric shape made that omission hard to notice by
eye -- the real rigged mesh's directional head/legs make facing actually visible, so this is a real fix).

**Measured numbers (same machine as every other pass: Intel Arc 140V laptop, DesktopGL, Release build),
compared against section 2.10's placeholder-body numbers:**

| Scene | Asset | fps avg (uncapped) | fps 1% low (uncapped) | fps avg (30fps cap) | fps 1% low (30fps cap) | Draw calls | Triangles | Skin time (ms) | Managed mem | Gen0 GC/10s |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 24 swarmlings alone | Placeholder (2.10) | 1,700 | 329 | 30.0 | 21.6 | 4 | 81,024 | 0.002 | 9.9 MB | 0 |
| 24 swarmlings alone | **Real, rigged (this pass, after the fix round below)** | **696.2** | **164.7** | **30.0** | **21.1** | **6** | **59,232** | **0.81** | 13.6 MB | 1,069 |
| 3 Griffins + 24 swarmlings | Placeholder (2.10) | 960-1,024 | 126-206 | 30.0 | 26.0 | 16 | 132,600 | 0.11-0.28 | 9.9 MB | ~60-335 |
| 3 Griffins + 24 swarmlings | **Real, rigged (this pass, after the fix round below)** | **632.6** | **139.2** | **30.0** | **24.3** | **18** | **110,808** | **0.87** | 14.3 MB | 1,192 |

(Re-run after the second, lead-reviewed fix round below -- the unit-scale fix changes how large each
triangle draws on screen, not triangle/draw-call/vertex counts, so it was checked for a fill-rate cost.
Numbers land within the same noise band as the pre-fix-round run in every column: draw calls and
triangles are bit-for-bit identical, as expected, and fps/skin-time/GC deltas are consistent with normal
run-to-run variance on this machine, not a real cost from drawing visually larger swarmlings.)

Both scenes still sustain the research-recommended 30fps cap solidly (29.97-30.0 fps avg), with 1%-low
cap numbers (21.1-24.3 fps -- a little lower than this pass's own first-draft numbers, within normal
run-to-run variance, not a regression from the scale/outline/camera fixes below) close to the cap --
comparable margin to section 2.10's placeholder despite real animation sampling now running per
swarmling. Three real, not free, costs of closing the register-budget risk and switching to real
per-instance `AnimatedPose` evaluation, all as expected going in, not surprises found after the fact:
**uncapped fps roughly halved** (696 vs. 1,700 alone; 633 vs. ~1,000 in the battle) -- still enormous
headroom over the 30fps target, so not a practical concern at this instance count; **draw calls up by 2**
(6 vs. 4 alone, 18 vs. 16 in the battle) from the 2-batch swarm split; and **skin time up substantially in
relative terms** (0.81 ms vs. 0.002 ms alone) because the placeholder's cost was a bare array-index lookup
per instance with zero matrix math, while this pass runs a real
`AnimatedPose.ComputeWorldMatrices`/`ComputeSkinMatrices` hierarchy walk per swarmling per frame -- still
small in absolute terms (well under one frame's 33 ms budget at 30fps even added to the Griffins' own pose
cost). Gen0 GC collections are far higher in absolute count at the uncapped frame rate (~1,100 vs. 0 for
swarm-alone) simply because there are ~6,500-7,000 frames in a 10-second uncapped run instead of ~170; at
the 30fps cap (46-57 collections/10s, not shown in the table above) the two passes are comparable.
Triangle counts are lower than the placeholder's (59,232 vs. 81,024 for 24 alone) because the real
Swarmling (1,234 tris) is smaller than the placeholder body (1,688 tris) -- unaffected by the visual-scale
fix below, which changes each instance's world-space size, not its vertex/triangle count.

**Screenshots** (this pass's first look-over; a second, lead-reviewed fix round immediately below found
three more real problems in these first screenshots, since corrected -- the images embedded here are the
*post-second-fix-round* versions, not what this first pass actually produced):

![3 Griffins + 24 swarmlings battle, arena-framed](055/live3d_battle_3plus24.png)

*Replaces section 2.10's "too far out" screenshot. The full 11x15 arena now fills the screen width;
Griffins and swarmlings are both clearly readable as distinct creature silhouettes rather than
indistinct dots, each sized to fill its own hex footprint (see the second fix round below for how this
differs from this pass's first attempt, which was still wrong). Critical look: no skinning tears, holes,
or stray-vertex spikes visible on any of the 24 swarmlings across this shot or the close-up below -- a
cleaner result than section 2.10's placeholder, which showed a thin dark streak artefact on a couple of
instances (attributed there to the placeholder body's own rig, not reproduced here).*

![Front-line mid-zoom](055/live3d_battle_front.png)

*A tighter crop around the Griffins' front line and the first couple of swarm rows behind them
(`--zoom front`). Griffin wing/leg/tail detail and swarmling horn/leg silhouettes are both legible at
this distance; the swarm's facing (toward the Griffins, away from the far back rows) reads correctly.*

![Swarmling close-up](055/live3d_swarmling_close.png)

*A single swarmling at close range (`--zoom close`): mossy olive-gold fur texture (sampled from the real
approved illustration by Meshy's own generation, not a flat swatch colour this time, unlike the
placeholder), visible curled horns and a cream muzzle, ink-plum outline (re-scaled to the mesh's own
size in the second fix round below -- this pass's first version had a large, disconnected ring around the
body instead), viewed front-on so the face actually reads -- consistent with the 2D approved art's own
Skittery "cute creepy, glowing eyes" read (`Tooling/ArtLab/provenance/enemies/swarmling.md`). Legs are
still mostly foreshortened/hidden under the round body, matching that same source art's own "2 stubby
front legs, hind hidden" silhouette -- a shared design trait, not a rendering gap.*

**Lead-review fix round (2026-09-30, second pass).** A lead review of this pass's first screenshots
(commit `abba70f`) sent back four real findings, all fixed; the screenshots and bench table above are
already the *post-fix* versions, this is the account of what was wrong and why.

1. **Unit scale was still wrong, despite the first fix round's camera-framing work**: Griffins read at
   roughly half a hex wide, Swarmlings at roughly a fifth of a hex -- both far smaller than the real 2D
   battle screen's own proportions. Checked directly rather than guessed: `BeastCraft.Desktop
   --screenshot shot.png --turns 0 --team griffin --lineup swarmling:Nature,swarmling:Nature,... --arena
   Large` renders the actual 2D battle-opening board (fit-all camera, no turn played) -- the same game,
   the same screen, ground truth rather than a doc's stated ratio. A direct pixel measurement against
   that render: the 2D Griffin's wingspan visibly overflows its own hex (roughly 1.2-1.3x hex width), and
   a 2D enemy sprite fills roughly two-thirds of its hex (~0.65-0.75x). Two separate bugs, both real:
   - `HexBoard.SetScale`'s clearance factor (`beastFootprint * 1.4`) sized the hex *wider* than the
     Griffin by 40% -- backwards from the 2D reference, where the beast is wider than its hex, not the
     other way round. Changed to `beastFootprint * 0.8` (the Griffin now ~1.25 hexes wide, matching the
     2D reference's overflow).
   - Even after that, the Swarmling's own absolute world size (`TARGET_HEIGHT = 0.55` in
     `blender_export_live_swarmling.py`) was still too small relative to the now-smaller hex: a bind-pose
     bounding-box estimate suggested ~0.59x hex, but the *observed* on-screen ratio was ~0.35x --
     the horn-tip-to-horn-tip bounding box is wider than the visually solid round body actually reads, so
     the bbox math over-estimated the apparent size. Fixed with a runtime `SwarmScale = 1.4` multiplier
     (Game1.cs, folded into each swarm instance's World matrix, not a Blender re-export -- quick to
     re-tune without rebuilding the asset), landing the Swarmling at roughly 0.7x hex by the same pixel-
     measurement method -- matching the 2D reference's own enemy-sprite ratio. **Re-ran both benches after
     this change** (a visually larger swarm instance could plausibly cost more fill rate): draw calls and
     triangle counts are bit-for-bit identical (scale doesn't touch vertex/index data), and fps/skin-time/
     GC numbers land within normal run-to-run variance -- see the bench table above, now showing the
     post-fix numbers directly.
   - **Stated plainly, as asked:** this was tuned by eye against a handful of screenshots and one 2D
     reference render, not derived from the 2D renderer's own sprite-to-hex sizing formula (which lives in
     `BeastCraft.Presentation`, not reverse-engineered here) -- close enough by direct pixel comparison to
     call this fixed, but a future pass should re-check if either asset's proportions change.
2. **The arena still didn't fill the screen width**, even with the first fix round's arena-grid camera
   sampling. Root cause: `ApplyCamera`'s ortho-fit rule, `Math.Max(viewSpanX, viewSpanY / aspect)`, widens
   the frame whenever the sampled content's *vertical* extent needs more room than the portrait aspect
   would otherwise show -- and a 15-row-deep arena's vertical extent (under the board tilt) does, every
   time, which is exactly what was quietly shrinking the *width* below what it could have been. The real
   2D battle screen doesn't do this: it crops rows top/bottom rather than shrinking everything to keep a
   tall board fully visible (confirmed against the same `battle2d.png` reference -- the hex pattern runs
   flush to, and slightly past, the left/right edges). Added `ApplyCamera(..., preferWidth: true)`, used
   only by `RebuildCameraArena`, which makes `orthoWidth = viewSpanX` outright -- width always fills the
   frame, tall row ranges may run off the top of the portrait screen instead. Edge margin also cut from a
   full hex corner radius (`HexBoard.HexSize`) to a tenth of one, matching the 2D board's own edge hexes
   reading as visibly, slightly cropped rather than comfortably inset.
3. **The swarmling close-up had a large, disconnected elliptical ring around the body.** Cause: the
   inverted-hull outline pass pushes every vertex out along its normal by a fixed *world-space* distance
   (`OutlineThickness = 0.012`, tuned for the Griffin's ~2.0-unit scale) -- that absolute distance doesn't
   shrink with a smaller mesh, so on the Swarmling's ~0.55-0.77-unit body it reads as a shell floating well
   outside the silhouette rather than a thin contour hugging it. Fixed with a second, separate
   `SwarmOutlineThickness` uniform in `Toon.fx` (used only by `VS_OutlineSwarm`), set from Game1 in
   proportion to the two assets' own normalisation heights times `SwarmScale` above -- small enough now to
   read as a contour, not a ring.
4. **The swarmling close-up was framed from behind, not the front.** `SwarmFacingYaw` is specifically a
   *battle-formation* choice (turn the Swarmling to face the Griffins across the front line) with no
   reason to apply to an isolated, no-Griffins-in-frame close-up shot -- applying it there pointed the
   Swarmling's face away from the same camera angle that shows a Griffin's front. `--zoom close`'s swarm
   facing is now the identity rotation (no yaw) instead -- confirmed by a side-by-side render check (not
   re-derived from the Blender axis-convention reasoning that got the *battle* facing right, because a
   round, mostly bilaterally-symmetric creature's face doesn't reliably read from the 3/4 *side* profile
   that works for the Griffin's beak/wings silhouette; a front-ish view was empirically the one that
   actually shows the muzzle and horns). A same-settings Griffin close-up (`live3d_griffin_close_check
   .png`, not otherwise referenced in this doc, kept here as the regression check) confirms the Griffin's
   own close-up framing is unchanged.
5. **Screenshots carried the debug stats overlay** (fps/draw-calls/memory text) baked into the image,
   previously hidden only with an explicit `--hide-stats`. `--screenshot` now always hides it (interactive
   play and `--bench` keep it) -- every screenshot in this section was re-taken after this change.

**Honest per-enemy cost estimate, given the Griffin pipeline already existed to adapt from:**

- Meshy generate + remesh: **35 credits** (30 + 5, run by the producer directly).
- Blender script authoring/debugging (`blender_export_live_swarmling.py`, adapting
  `blender_export_live.py`'s weld/weight/repair pipeline to a smaller 6-bone rig and new idle/move
  poses): roughly 45-60 minutes, most of it spent on bone placement/pose tuning and confirming facing via
  `inspect_orientation.py`, not the weighting pipeline itself (reused near-verbatim).
- C# runtime integration (retiring `SwarmlingSkinnedModel`, adapting `GpuMesh.BuildSwarmMerged` to a
  generic `GltfSkinnedModel`, the batched-draw redesign, the 3x4-row shader rewrite, per-instance
  `AnimatedPose` scratch arrays, facing, the arena camera): roughly 90-120 minutes -- this is a one-time
  architecture cost for the *swarm tier as a whole*, not a per-enemy cost; a second real swarm-tier enemy
  through the same pipeline would skip almost all of it (only the Blender authoring step repeats).
  Camera-framing work (task item 3) is a similar one-time, not-per-enemy cost.
- Testing (screenshots, bench runs, one fix round) and this write-up: roughly 45-60 minutes.
- **Second, lead-reviewed fix round** (unit-scale investigation against a real 2D reference render,
  camera `preferWidth` fix, per-asset outline thickness, close-up facing, stats-overlay hiding, re-running
  both benches, re-taking four screenshots, this write-up's own update): roughly 60-75 minutes -- a real
  cost, not folded into the estimate above, because it was a genuine correctness pass on top of the first
  round's work, not part of authoring the asset itself.
- **Total for this one enemy, this pass: roughly 4-4.5 hours** across both fix rounds, dominated by the
  one-time swarm-tier runtime architecture work and camera/scale correctness, not by the Blender asset
  authoring itself -- consistent with section 2.10's own observation that the merged-batch engineering,
  not the per-asset rig, was the larger cost that pass too.

**Caveats, honestly stated:**

- **No on-device Android test still.** Every number above remains desktop-only; the register-budget fix
  narrows a *reasoned-from-spec* risk (closing distance to GLES 3.0's guaranteed minimum) but does not
  replace an actual GLES driver compile test, named as the standing next step since section 2.8.
- **Bone placement is an approximation**, not derived from a skeletal/medial-axis analysis of the mesh --
  heat weighting converged cleanly regardless, but a different remesh of the same source could place
  geometry differently enough that these fractional bone coordinates need re-tuning by eye.
- **No horn bones**, a deliberate register-budget trade-off (above), not a limitation discovered after
  the fact -- flagged here so it isn't mistaken for an oversight if a future pass revisits bone count.
  `SWARM_BONES_PER_INSTANCE` is hardcoded to 6 on both the Blender and shader sides; a differently-rigged
  swarm enemy would need both changed together (`Game1.LoadContent` throws if the loaded GLB's joint
  count doesn't match, rather than silently misrendering).
- **The 3/4-turn "facing the players" choice is a screen-reading convention** (mirrored left/right from
  the Griffins, matching genre convention for side-view battle scenes), not a literal vector pointing at
  the Griffins' world position -- named here since "up/right toward enemies, enemies facing the players"
  in the task brief could be read more literally than what was built.
- **Unit scale (`HexBoard`'s 0.8x factor, `SwarmScale = 1.4`) was tuned by eye against pixel measurements
  of one 2D reference render**, not derived from the 2D renderer's own hex/sprite sizing formula -- close
  enough by direct comparison to call fixed (see the fix round above for the actual before/after ratios),
  but not a guaranteed-exact match, and the reference render itself
  (`BeastCraft.Desktop --screenshot ... --turns 0 --team griffin --lineup swarmling:Nature,...`) is a
  scratch artefact, not committed to the repository -- rerun that exact command against `content/data/Vfx
  /battle-art.json`'s `Large` arena entry to reproduce it.
- **The swarmling close-up's front-facing choice (`--zoom close` -> identity rotation) was found
  empirically** (render, look, compare), not derived from the same Blender-axis reasoning that fixed the
  battle-formation facing -- correct by inspection for this mesh, but if the Swarmling is ever re-rigged
  from a different remesh, re-check by eye rather than assuming the axis math still lines up the same way.

### Licences (this pass)

No new licence implications: the real Swarmling mesh/texture are **Meshy, paid tier** (output ownership
retained, same as the Griffin's Meshy passes -- see "AI provenance" below); everything else this pass
touched (the batching/shader/camera code) is original engineering, not AI-generated content.

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
| Steady 60 fps on a Galaxy A35-class phone | Unmeasured; expected non-issue as baked 2D sprites (same cost class as shipping content) -- see section 3 | **Measured on desktop only** (2.8): 471 fps avg / 86 fps 1% low at 24 beasts, uncapped, wide margin above 60 fps on this laptop's GPU (GPU skinning, after a lead-review fix round); A35 unmeasured, reasoned estimate says per-draw-call driver overhead and GLSL ES shader compile are the real unknowns now, not CPU skinning or fill rate -- see 2.8 | Unmeasured; expected non-issue, same reasoning, and it's strictly less new content than Path A |
| An approvable beast within a set number of hours per asset | Real risk on pass 1's mesh (rigging did not converge); **pass 3's low-poly remesh rigged cleanly on the first attempt with no repairs (2.7)**, but that's one beast, one mesh -- generality across the other 9 is unconfirmed | Same mesh/rig as pass 3, plus ~25-35 min this pass to author Idle/Move clips and export -- but animation is hand-keyed sinusoidal posing, not production quality, and has no Mixamo-equivalent shortcut for a quadruped (2.8) | Looks solid: ~3-5 min per beast once the tooling exists, reusing the existing `rigparts.py` convention |
| Cosmetic swaps without per-beast rework | **Fails as built, all three passes**: needs full re-generation (+ re-render, for pre-rendered output) per combination (section 4) | **Passes, for attachment-style cosmetics and colour form** (2.8): crest toggle and tint cycle both proven live, zero re-render/re-animation -- does *not* yet prove segmented-submesh swaps for a cosmetic baked into the base mesh's own geometry | **Passes**: parts/attachment swap, no rework |
| A trimmed device build that loads content | Out of scope for this mini-spike | Out of scope for this mini-spike (DesktopGL only; Android untested, see 2.8's A35 estimate) | Out of scope for this mini-spike |

*Low-poly (pass 3, section 2.7) row values above are folded into "Path A"; it's the same path, a third
generation. Path A' (pass 4, section 2.8) is real-time rendering of that same low-poly mesh, not a
fourth generation -- a different rendering strategy over the same asset. Likeness (not itself a
pass-criteria row, but see 2.1/2.7) reassessed as passing the producer's current "close, with
appropriate physiology" bar for the low-poly and textured passes, and unchanged (same mesh) for pass 4.*

## 6. Recommendation (revised 2026-09-30 after the fourth and fifth, real-time passes)

**Fifth-pass update.** Section 2.10 closes the last big open technical unknown from the fourth pass's
own recommendation text below: whether a hero-plus-swarm battle scene (the actual shape of combat per
the PvE design direction -- a few player beasts vs. up to ~24 small enemies) can be rendered cheaply.
It can: hero-tier GPU skinning (section 2.8, unchanged) plus a batched swarm path (section 2.10,
pivoted from VAT to a shared bone-array technique after confirming VAT itself doesn't compile under
MonoGame's DesktopGL effect profile) drew a 3-Griffin-plus-24-swarmling battle in 18 total draw calls
(16 with section 2.10's placeholder body's smaller bone count; 2.11's real 6-bone rig needed 2 swarm
batches instead of 1 merged buffer to stay under the GLES register budget) and sustained the
research-recommended 30fps battle cap with wide margin on this desktop GPU. **Fifth-pass fix round
(2.11)** replaced the procedural placeholder with a real, rigged, animated Swarmling (the producer's
own 35-credit Meshy generation) and fixed the battle camera's framing, which read as "too far out" in
2.10's own screenshot. **This is now enough evidence to write a concrete production plan** (below), not
just "real-time 3D is credible in the abstract" -- but the plan still has real, named open risks (an
actual Android device test chief among them) that keep it a plan to execute, not a decision already
proven safe.

**Still continue the 2.5D + Spine path (or the code bone rig this spike demonstrates) as the default for
the beast roster today.** That part of the call is unchanged by either real-time pass. **What the
real-time passes do change:** the single objection that has driven every version of this recommendation
-- pre-rendered 3D fails the cosmetic-swap criterion outright (section 4) -- is **no longer true of
real-time 3D**. Section 2.8 built and measured a real-time MonoGame renderer and it passes the
cosmetic-swap and colour-form criteria live, with no re-render or re-animation step. That means real-time
3D is no longer a theoretical "wrinkle" (as it was described after pass 3) -- it is now an evidence-backed,
*credible*
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
- **The A35 numbers that matter most -- per-draw-call driver overhead and GLSL ES shader compile on
  mobile -- are still unmeasured** (2.8). A lead-review fix round moved skinning from CPU to GPU
  specifically because the CPU-skinning cost *was* the standout unmeasured risk and turned out, once
  measured, to also be the dominant cause of a real desktop performance problem (a 9 fps 1%-low at 24
  beasts); with that fixed, the CPU side is now small even under a pessimistic mobile-CPU slowdown
  factor, but every other A35 unknown (98 draw calls/frame, a `Bones[16]`-array GLSL ES compile,
  SharpGLTF/content-loading on Android) is still a reasoned estimate, not a measurement, pending an
  actual device test.
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
animation authoring, and -- even after a lead-review fix round found and fixed the worst of it, moving
skinning to the GPU -- still-unmeasured mobile driver/shader-compile risk) that pre-rendered 3D never
had to pay.

Reasoning:

1. The cosmetic-swap criterion is the one this whole spike (and issue #38) cares most about. **Pre-rendered**
   3D fails it outright, all three passes (1-3): a single-fused AI mesh -- textured or not, low-poly or
   not -- cannot cheaply take 20 discrete cosmetic options without a full re-render per combination.
   **Real-time** 3D (pass 4, section 2.8) passes it for attachment-style cosmetics and colour form, measured
   not hypothesised -- but adopting it means paying for a new render path, a new animation-authoring
   problem with no Mixamo shortcut, and still-unmeasured mobile driver/shader-compile risk (2.8's CPU
   skinning cost was the worst of the mobile unknowns and is now fixed and measured small, but the
   others -- draw-call overhead, GLSL ES compile, Android content loading -- remain open), costs Path B
   simply doesn't have. That package of new costs, not the combinatorics objection itself, is why
   real-time 3D is not today's default even though it clears the one bar that mattered most.
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
A35's Mali-G68 MP5; GPU skinning (the lead-review fix) means CPU skinning cost is no longer the
standout mobile risk it originally was; the real unmeasured risks now are per-draw-call driver
overhead, `Bones[16]`-array GLSL ES shader compile, and SharpGLTF/content loading on Android, not raw
triangle throughput or CPU skinning. Desktop numbers (2.8) show wide 60 fps margin (both average and
1% low) at up to 24 beasts on this laptop's GPU, but that is not a substitute for an on-device A35 test,
which remains not done.

### Risks if this recommendation is wrong

- If cosmetic scope shrinks a lot (fewer discrete options per species), Path A's combinatorial cost drops
  proportionally and might become tractable -- worth revisiting if `cosmetic-library.json` changes
  significantly.
- If a future AI-to-3D tool or tier reliably outputs clean, riggable, textured, segmented meshes, most of
  Path A's problems here (2.2, 2.3, section 4) could disappear; this spike used one paid-tier Meshy
  generation (plus one paid `remesh` follow-up, 2.7) and does not rule that out for other tools/settings.
- **Now confirmed rather than hypothetical (2.8):** real-time 3D rendering does dodge the
  cosmetic-combinatorics objection, for attachment-style cosmetics and colour form. A lead-review fix
  round already closed the CPU-skinning risk (moved to the GPU, measured small); if the remaining
  animation-authoring problem (no Mixamo-equivalent for this game's beasts) or the remaining unmeasured
  A35 risks (driver overhead, GLSL ES shader compile) turn out to be smaller than feared, this
  recommendation is the one most likely to flip on new evidence -- see "Next steps".

### A concrete production plan, if real-time 3D is ever greenlit (not a recommendation to greenlit it today)

Sections 2.8-2.10 are now enough evidence to write this down concretely rather than leave it as "real-time
3D is credible" -- this is the plan this spike would hand to an engineer starting that work, with its
risks named, not a claim that those risks are already closed:

1. **Hero tier (player beasts, ~≤4 on screen): GPU skinning**, the Griffin's proven `Bones[]`-array
   technique (section 2.8), 6-8K tris/1K texture/10-20 bones per section 2.9's budget.
2. **Swarm tier (up to ~24-32 on screen): batched GPU skinning via a shared, per-instance-sliced bone
   array** (sections 2.10-2.11) -- not literal VAT (confirmed not to compile under MonoGame/DesktopGL),
   but the same "very few draw calls for the whole swarm" outcome the task brief wanted (4 draw calls at
   24 instances, not literally 2 any more once a real 6-bone rig made the uniform-array-budget risk real
   rather than hypothetical). The register-budget fix named in 2.10 is now closed (2.11: 3x4 matrices +
   fixed 12-instance batches, 216 of GLES 3.0's guaranteed 256 vec4) for *this* rig's bone count; a
   different swarm enemy with more bones would need the same arithmetic re-run, not a new technique.
3. **30fps hard cap in battle** (section 2.9's research, confirmed compatible with both scene shapes in
   2.10's numbers) -- register with Android's Game Mode/frame-pacing APIs per 2.9, not just a MonoGame
   frame-rate throttle.
4. **Selective outline-pass cost-cutting** (section 1.7/2.9's own recommendation, not yet implemented in
   this spike's own swarm rendering -- section 2.10's battle screenshot still gives every swarmling a
   full outline pass): drop or cheapen the inverted-hull outline for swarm-tier units specifically, since
   it currently doubles their already-multiplied draw-call/fill cost for a readability benefit that
   matters most for player-targeted units, not background swarm.

**Named risks that keep this a plan, not a proven-safe decision** -- every one of these is an actual
open unknown after five passes, not a formality:

- **An Android device test is still not done.** Every fps/draw-call/triangle number in this spike is
  desktop-only (Intel Arc 140V laptop). The single most important next step before trusting any of this
  for production is building the Android variant and running the same `--bench` harness on a physical
  Galaxy A35.
- **Quadruped/winged animation authoring has no Mixamo-equivalent shortcut.** Every clip in this spike
  (Griffin and swarmling both) is hand-authored sinusoidal posing, not production-quality animation, and
  not a workflow that scales cheaply to 10 beasts x several clips each.
- **GLES shader compile is unverified beyond "the vertex-texture-fetch path is confirmed closed."**
  Section 2.10 found one hard MonoGame/MGFX limitation (no VTF on the OpenGL profile at all) by testing
  it; the uniform-array-budget question is a second, different shader-compile risk -- 2.11 closes the
  *arithmetic* (216 of 256 guaranteed vec4) but that is still reasoned from the GLES spec, not tested
  against a real driver. Both need resolving on-device before the swarm path above can be trusted for
  Android.
- **Likeness and cosmetic-segmentation caveats from earlier passes are unchanged**: the Griffin mesh is
  still a plausible griffin, not a reconstruction of the specific approved illustration (2.1, 2.7), and
  this spike still only proves attachment-style cosmetics (a crest parented to a bone), not
  segmented-submesh swaps for a cosmetic baked into a beast's own base geometry (section 4).

### Next steps

- No further 3D work recommended as the *default* roster path off this spike -- Path B remains it. But
  unlike after pass 3, this is no longer "no further 3D work, full stop": if 2D cosmetic authoring later
  proves unsustainable at full scale (10 beasts x ~2-3 categories x several options each), or a
  free-orbiting camera becomes a real requirement, real-time 3D (not another pre-rendered-3D attempt) is
  now a credible re-open path, with a working starting point (`Tooling/Spike55/Live3D`) and a specific,
  named list of what to close first: an Android build and an actual Galaxy A35 `--bench` run (still the
  single biggest open unknown across sections 2.8-2.11), and a real animation-authoring plan for a
  quadruped/winged skeleton with no Mixamo shortcut. The `SwarmBones[]` uniform-budget fix (2.10) is now
  closed for this rig's bone count (2.11) -- re-run the same arithmetic for any differently-rigged swarm
  enemy, rather than treating it as solved once for all future swarm assets.
- **Closed this pass:** the 35-credit Meshy spend authorisation for a real Swarmling generation (section
  2.10) is now used (2.11) -- a real, rigged, animated Swarmling (`swarmling_live.glb`) replaced the
  procedural placeholder body, with the merged-batch/register-budget engineering carrying over largely
  unchanged in shape (batched rather than single-merged, per the register-budget fix). The next enemy
  through this pipeline (if any) would be a much smaller marginal cost -- see 2.11's honest per-enemy
  estimate.
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

**The Swarmling 3D model (section 2.10's procedural placeholder, replaced in section 2.11's fix round)**
was generated by **Meshy (paid tier)**, from the already-approved 2D Swarmling illustration
(`content/art/enemies/swarmling/swarmling_hollow.png`, itself a separately AI-disclosed asset --
`Tooling/ArtLab/provenance/enemies/swarmling.md`, unrelated Meshy involvement); output ownership retained
(paid tier).

- **Pass 5 fix round** (2.11): `image-to-3d` (30 credits) + `remesh` to ~1,200 tris (5 credits), **run by
  the producer directly**, not via `Tooling/ArtLab/scripts/meshy.py`. Full records:
  `Tooling/ArtLab/provenance/meshy-01a0f424-1644-75af-8cdb-e25ef6452845.md` and
  `Tooling/ArtLab/provenance/meshy-remesh-01a0f426-b36a-7051-8603-3b77b80b9616.md`. Re-exported as a
  skinned, animated GLB (`swarmling_live.glb`, committed at `Tooling/Spike55/Live3D/Content/model/`, 0.18
  MiB -- small enough to commit directly per the spike's size budget, same convention as
  `griffin_live.glb`), same as pass 4 did for the Griffin.
- **Section 2.10's procedural placeholder body** (superseded, no longer used at runtime, but its script
  and a description remain in the repo for reference) was **not** AI-generated -- a small procedural
  mesh (icospheres + cones) built directly in `blender_export_vat_swarmling.py`, textured with a sampled
  swatch from the approved 2D illustration's own palette, not the illustration itself.
