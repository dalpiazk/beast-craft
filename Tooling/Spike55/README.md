# Spike #55 tooling: pre-rendered 3D toon vs 2D code bone rig (Griffin)

Scripts behind `docs/spikes/055-3d-mini-spike.md`, the mini-spike gate report comparing:

- **Path A** -- a pre-rendered Blender toon render of a Meshy (paid tier, producer-generated,
  output owned) image-to-3D Griffin mesh, rigged and animated, rendered to transparent sprite
  frames (keeps MonoGame; see `blender_toon_render.py`).
- **Path B** -- a scripted (code) bone rig over the Griffin's *existing* machine-cut parts
  (`content/art/source/griffin/parts/*.png` + `parts.json`), no Spine licence, driven by
  sinusoidal oscillators -- what Spine Essential does (bones, no mesh deform); see `rig2d.py`.

No GLB and no Blender install are committed here. Path A has been run three times (see
`docs/spikes/055-3d-mini-spike.md` sections 2, 2.6 and 2.7): an untextured GLB, producer-supplied
(`Tooling/ArtLab/provenance/spike55-griffin-meshy.md`); a textured `meshy-7.1` + 2K-texture GLB, generated
via the CLI below under a specific spend authorisation
(`Tooling/ArtLab/provenance/meshy-01a0f351-869e-7319-9820-e4b6e8b6b226.md`); and a low-poly (~8k tri)
`remesh` of that textured GLB via Meshy's paid `remesh` endpoint, also under a specific spend authorisation
(`Tooling/ArtLab/provenance/meshy-remesh-01a0f38d-c409-702e-b976-62bff441b88f.md`). No GLB is committed;
rerunning Path A needs your own copy of one (or a fresh Meshy export/remesh) and a Blender install. To
generate a fresh export (or re-download an existing task's outputs) from the command line instead of the
web app, see `Tooling/ArtLab/README.md`'s "Meshy (3D)" section for `Tooling/ArtLab/scripts/meshy.py` -- a
local-only, spend-guarded CLI for Meshy's paid API (`image-to-3d` and `remesh` subcommands).
`blender_toon_render.py` auto-detects whether the GLB it's given has a texture (an untextured GLB gets the
flat-swatch toon material described below; a textured one gets a banded multiplier over the real
base-colour texture) -- no separate flag needed. `blender_lowpoly_render.py` is the same pipeline plus a
vertex-weld step for a `remesh`-task GLB's duplicate-position verts (see below) and a texture downsize.

## Scripts

| Script | What it does |
| --- | --- |
| `gltf_inspect.py` | Pure-stdlib GLB inspector (tri count, vertex count, embedded image sizes, node/skin/animation counts) -- no 3D library needed. |
| `inspect_orientation.py` | Blender headless: imports a GLB and renders quick orthographic front/side/top probes so you can read off which local axis the mesh faces, before writing any camera/rig code against it. |
| `blender_toon_render.py` | Blender headless: imports the GLB, normalises it (feet on ground, scaled to a fixed height), builds a 2-3 band toon material (Diffuse -> Shader to RGB -> ColorRamp -> Emission, multiplied onto the GLB's base-colour texture if it has one) plus a thin inverted-hull ink-plum outline (Solidify), places an orthographic hex-board camera, builds a simple armature, and tries several weighting strategies in order (heat weights on the raw mesh; heat weights on a disposable voxel-remeshed duplicate, data-transferred back onto the original UV-intact mesh; envelope weights) before falling back to a rigid whole-object animation. Three small repair passes clean up strays from the data-transfer path (unweighted vertices, vertices that disagree with their mesh-connected neighbours, and small disconnected mesh islands mapped to the wrong bone). Renders idle (12 frame) and move (8 frame) loops as transparent PNGs. |
| `blender_lowpoly_render.py` | Same pipeline as `blender_toon_render.py`, for a Meshy `remesh`-task GLB. Adds two steps right after import: (1) "Merge by Distance" at a 1e-4 threshold to weld the duplicate-position vertices the glTF exporter leaves behind at every UV/normal seam (without this, a `remesh` GLB's raw vertex-edge connectivity reads as thousands of tiny disconnected "components" that look shattered but aren't -- see the gate report section 2.7 for the full story); (2) picks the base-colour image by walking the imported material's node graph to whatever feeds the Principled BSDF's Base Color (a `remesh` GLB can carry base_color + normal + metallic_roughness, so "the first image" is not a safe guess), then downsizes it to 1024x1024 (`--texture-size`, default 1024). On the one low-poly Griffin mesh tested here, automatic weighting converged on the *first* attempt (no voxel-remesh-donor fallback needed) with 0 unweighted vertices. |
| `rig2d.py` | Pure Pillow: loads `parts.json` + the six part PNGs, rotates each part about its own pivot (padding the image so rotation doesn't clip, per-part), composites back-to-front per `order_back_to_front`, and renders the same idle/move loops at the game's sprite size, plus sprite-strip PNGs and preview GIFs. |

## Rerunning

Both scripts take paths as arguments/environment -- nothing personal or absolute is hardcoded.

**Path B (no external dependencies beyond Pillow, matches `Tooling/PixelArt`'s pinned version):**

```
python Tooling/Spike55/rig2d.py --repo <path to your beast-craft checkout> \
    --beast griffin --out <output dir> [--frame-size 512]
```

Run from anywhere; `--repo` defaults to the current working directory, so running it from the
repo root with no `--repo` also works.

**Path A (needs Blender 5.x on PATH or a full path to `blender.exe`, and your own Griffin
GLB):**

```
# Optional: sanity-check the GLB and its orientation first
python Tooling/Spike55/gltf_inspect.py <path to griffin.glb>
blender -b --python Tooling/Spike55/inspect_orientation.py -- --glb <griffin.glb> --out <probe dir>

# Full render
blender -b --python Tooling/Spike55/blender_toon_render.py -- \
    --glb <griffin.glb> --out <output dir> --frame-size 512

# Low-poly variant (a Meshy `remesh`-task GLB)
blender -b --python Tooling/Spike55/blender_lowpoly_render.py -- \
    --glb <griffin_remesh.glb> --out <output dir> --frame-size 512 --texture-size 1024
```

Both scripts write `idle/idle_NN.png`, `move/move_NN.png`, `hero.png` (neutral pose) and, for
reference only, `griffin_rig.blend` (not meant to be committed -- it's a large binary scratch
file). `blender_lowpoly_render.py` also writes `texture_1k.png`, the downsized base-colour
texture actually used for the render.

Blender itself was installed as a portable zip (not the winget/MSI package -- that download was
blocked by a Cloudflare bot challenge in this environment) from
`https://download.blender.org/release/Blender5.2/` (or any of its mirrors), extracted to a
short, non-project path (long paths inside the zip, e.g. under
`python/lib/site-packages/pkg_resources/tests/...`, exceed Windows' `MAX_PATH` if extracted deep
inside a nested temp directory).

## Known issues found while building this (see the gate report for the full write-up)

**Pass 1 (untextured GLB):**

- The Meshy "generate" GLB download (as produced for pass 1) had geometry + UVs but **no material
  or embedded texture**. Path A's toon shading used a flat colour sampled from the approved
  Griffin palette (`Tooling/ArtLab/provenance/griffin.md`), not the source image. Closed in pass
  2 -- `--texture` on the CLI produces a GLB with a real baked base-colour texture, which
  `blender_toon_render.py` now detects and uses automatically.
- Blender's automatic (heat-map) bone weighting **did not converge** on this mesh, even after a
  voxel remesh applied directly to the render mesh. Fell back to animating the whole Griffin
  object rigidly (bob/sway/lean) rather than true per-part skeletal deformation. Closed in pass 2
  for that generation (a real per-part rig converged -- see below) -- but the underlying cause
  (this class of AI mesh not always being heat-weightable) is mesh-dependent and could recur on a
  different beast/generation.
- In Blender 5.2's headless/background EEVEE, moving/rotating the *mesh* object between renders
  in the same session was silently ignored by the render operator (reproduced with a pixel
  diff: identical output despite different `object.rotation_euler`), even after forcing a
  depsgraph update. The fix was to drive an unencumbered parent `Empty` instead of the mesh
  object directly (the mesh has an Armature modifier + armature-parenting from the automatic
  weights attempts, which is suspected but not confirmed to be related) -- see the comments in
  `blender_toon_render.py` around `anim_root`.

**Pass 2 (textured `meshy-7.1` GLB, 2026-09-30):**

- Heat weighting on the *raw* mesh still didn't converge (same failure class as pass 1). This
  time, remeshing directly would have destroyed the new texture's UV mapping, so the remesh runs
  on a disposable duplicate and the resulting weights are data-transferred back onto the
  original, UV-intact mesh -- see `blender_toon_render.py`'s comments around
  `Griffin_weight_donor_temp` and the `DATA_TRANSFER` modifier. That data-transfer path
  introduced its own strays (unweighted vertices, vertices disagreeing with their mesh-connected
  neighbours, small disconnected mesh islands mapped to the wrong bone) -- each has a targeted
  repair function in the script; see their docstrings.
- This Meshy mesh is fragmented into **703 disconnected edge-connected components** (separate
  feather/fur shells, not "one body plus a few floaters"). Any repair that treats "not the single
  largest component" as "a floater" is wrong here -- it touched 489,925 of 496,815 vertices in an
  early version of `reweight_floating_mesh_islands` and was confirmed overcorrecting. The shipped
  version only touches components at or below 300 vertices.
- One thin spike artefact survived every weight repair on certain move-loop frames (large leg
  rotation angles); disabling the outline modifier and re-rendering showed the same spike in the
  base mesh colour, confirming it's **real geometry from the Meshy reconstruction** (a thin spur
  near a claw), not a rig/weight bug. Left as a known, cosmetic issue -- see the gate report's
  section 2.6.

**Pass 3 (low-poly `remesh`, 2026-09-30):**

- A **free**, local-only attempt preceded this: Blender's Decimate (Collapse) modifier run
  directly on pass 2's 929,638-tri mesh. It failed visually -- shattered into disconnected shards
  with holes and floating debris, texture not carried over (flat grey-lilac). Root cause: pass 2's
  mesh is fragmented into 703 disconnected components (see above), and Collapse decimation has no
  awareness of component/UV-seam boundaries. That attempt's output (`decimate_lowpoly.py` in the
  work dir, not committed here) is superseded and was not reused for anything below.
- The **paid** `remesh` endpoint (5 credits flat) avoided that failure mode entirely: a single,
  near-watertight low-poly mesh (once its glTF-export vertex-splitting is welded -- see
  `blender_lowpoly_render.py`'s docstring) that rigs cleanly with automatic weights on the first
  attempt, no voxel-remesh-donor fallback or repair passes needed.
- A first raw connected-component reading on the un-welded import (2,452 components, largest 38
  verts) looked like the same shattering the free Decimate attempt produced. It wasn't: the glTF
  exporter splits a vertex into coincident, unwelded duplicates wherever UV/normal differs per
  face corner (normal behaviour, not fragmentation) -- confirmed, and fixed, by a "Merge by
  Distance" weld step (1e-4 threshold) that collapses this to 4,192 true verts and a single
  connected component. See the gate report section 2.7 for the full account; don't conflate this
  finding with the free attempt's genuine shattering above.
- No fix round was needed for the low-poly render -- the first pass at idle/move frames was
  already clean (no holes, shards, floating debris, texture seams, or broken silhouette), a first
  among this spike's three 3D passes.

## Fourth pass: real-time 3D in MonoGame (`Tooling/Spike55/Live3D`)

Per the producer's 2026-09-30 decision, the fourth pass tests **real-time** 3D rendering in MonoGame
(not pre-rendered frames) -- a standalone spike app, not added to `BeastCraft.slnx` and not touching
`src/`, so CI is unaffected. Full write-up (method, measured numbers, screenshots, cost estimate,
risks, revised recommendation): `docs/spikes/055-3d-mini-spike.md`'s "Fourth pass" section. This
section is the tooling reference -- how to rebuild the asset and how to run the app.

### Asset: `blender_export_live.py`

Blender headless, reuses `blender_lowpoly_render.py`'s import/weld/texture-downsize/armature/
auto-weight pipeline verbatim (same weld threshold, bone hierarchy, heat-weights -> voxel-remesh-donor
-> envelope-weights fallback chain, same three weight-repair passes -- see that script's docstring),
but instead of rendering PNG frames it authors real keyframed `Idle` (2s loop) and `Move` (1s loop)
Actions on the armature and exports a single skinned, animated GLB, plus one small separate procedural
cosmetic attachment mesh (no Meshy call -- none authorised for this pass):

```
blender -b --python Tooling/Spike55/blender_export_live.py -- \
    --glb <griffin_remesh.glb> --out <output dir> [--texture-size 1024] [--fps 24]
```

Writes `griffin_live.glb` (skinned mesh + armature + both animation clips, base-colour texture only --
no normal/metallic-roughness, JPEG-encoded to stay well under the 2 MB budget) and `crest_alt.glb` (a
procedural low-poly plume, ~224 tris, no skin, parented at runtime to the head bone's world matrix).
Both are small enough to commit directly (`Tooling/Spike55/Live3D/Content/model/`) rather than needing
a path argument: `griffin_live.glb` is 0.88 MiB, `crest_alt.glb` is 13 KiB.

One Blender API gotcha hit while writing this script: Blender 5.x's layered-Action data model (the
4.4+ "Animation 2.0" redesign) removed the top-level `Action.fcurves` -- fcurves now live under
`action.layers[].strips[].channelbag(slot).fcurves`; see `set_linear_interpolation()`'s docstring for
the walk. A second, more consequential one: the glTF exporter's base-colour-texture extraction only
recognises a **Principled** BSDF's Base Color input -- a first export using a plain Diffuse BSDF
produced a GLB with a material but zero images/textures, silently untextured (caught by inspecting the
exported JSON chunk, not by eye). A third: `Image.scale()` only touches the in-memory pixel buffer: the
glTF exporter reads from the image's packed/on-disk source, so a first downsize-and-export shipped the
original 2048x2048 texture despite the resize; fixed by saving the scaled image to a real file and
reloading it as a fresh image datablock before export.

### Runtime: `Tooling/Spike55/Live3D`

A standalone MonoGame DesktopGL project, `net10.0`, mirroring `src/BeastCraft.Desktop`'s target
framework and `MonoGame.Framework.DesktopGL` package version (`3.8.5.1`) for an apples-to-apples
comparison. Loads the GLBs at runtime with **SharpGLTF.Core 1.0.7** (MIT licence,
https://github.com/vpenades/SharpGLTF -- see `THIRD-PARTY-NOTICES.md`-style attribution below; only
`SharpGLTF.Core` is referenced, not `SharpGLTF.Runtime`, because this project hand-rolls animation
sampling directly against the glTF schema (`Node.GetLocalTransform(animation, time)`, walked through
the hierarchy by `AnimatedPose.cs` itself -- see its doc comment for why not `Node.GetWorldMatrix`)
rather than using Runtime's scene-graph helper.

**GPU skinning** (`Content/Effects/Toon.fx`'s `Bones[16]` bone-palette vertex shader, fed by one shared
*static* `VertexBuffer` per model, built once -- see `SkinnedVertex.cs`/`GpuMesh.cs`): each beast
instance's CPU-side work per frame is just computing 11 joint matrices and uploading them as that one
small array, not touching a vertex. This replaced an initial CPU-skinning implementation (blend the
mesh on the CPU every instance every frame into a per-instance `DynamicVertexBuffer`) after a
lead-review pass found it was the direct cause of the worst frame-time spikes at 12+ beasts -- see
"Lead-review fix round" below and `docs/spikes/055-3d-mini-spike.md`'s fourth-pass section for the full
before/after numbers. The custom toon + inverted-hull-outline effect itself is compiled by the
MonoGame content pipeline via `MonoGame.Content.Builder.Task` -- the only MGCB-built content; the GLBs
and backdrop PNG are read as plain files at runtime, same convention as `BeastCraft.Desktop`.

Build/run (first time, restore the local `dotnet-mgcb` tool the content pipeline needs):

```
dotnet tool restore --manifest Tooling/Spike55/Live3D/dotnet-tools.json   # or just: cd Tooling/Spike55/Live3D && dotnet tool restore
dotnet build Tooling/Spike55/Live3D -c Release
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll             # interactive window
```

Controls (interactive mode only): `Tab` cycles the stress test 1 -> 3 -> 12 -> 24 beasts (each with
its own randomised animation-clock offset -- desynced on purpose, to prove instances don't need to
share a phase); `C` toggles the crest attachment on/off; `T` cycles 3 colour-form tints (a shader
multiply on the sampled texture); `M` toggles the Idle/Move clip; `Esc` quits. On-screen stats: average
fps, 1%-low fps (over a rolling 3-second window), draw calls, triangles, CPU-side pose/skin-upload
time, managed + estimated GPU memory.

Headless modes for reproducible numbers/images:

```
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --bench 24 --seconds 10 --out result.json
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --screenshot shot.png --beasts 12 --crest on --tint 1
```

`--bench N --seconds S --out path.json` disables vsync (uncapped throughput, not monitor-capped),
spawns N beasts, runs for S seconds of real wall-clock time, and writes `{beasts, seconds, frameCount,
fpsAvg, fps1PercentLow, frameMsAvg, drawCalls, triangles, skinningMsAvg, managedMemoryBytes,
gpuMemoryEstimateBytes, gcGen0Collections, gcGen1Collections, gcGen2Collections}` (the three GC counts
are `GC.CollectionCount(0/1/2)` deltas over the bench window -- added in the lead-review fix round to
make GC-driven frame-time jitter directly measurable rather than inferred). `--screenshot path.png
[--beasts N] [--crest on|off] [--tint 0|1|2] [--hide-stats]` opens the window, waits until the
on-screen stats have real data (elapsed wall-clock time, not a fixed frame count -- see "Lead-review
fix round" below), writes one PNG, then exits.

### Two real bugs this uncovered (worth knowing before touching the shader)

- **MGFX does not honour a `.fx` file's HLSL default-value initialisers.** `Toon.fx` declares e.g.
  `float3 LightDirection = normalize(float3(0.45, 0.65, 0.60));`, but MonoGame's effect compiler
  strips that default and the parameter comes back zero at runtime unless explicitly set from C#. A
  first render (only `ViewProjection`/`BaseTexture`/`TintMultiply` set from `Game1`) came out
  **solid black**: `LightDirection` was `(0,0,0)`, `normalize` of a zero vector is undefined/NaN, every
  band comparison fell through to `HighlightBoost` -- itself also unset/zero -- multiplying the
  (correctly bound, correctly textured) beast by black. Fixed by explicitly setting every constant
  parameter once in `LoadContent` (see `Game1.cs`'s comment at that call site).
- **glTF's winding convention is the opposite of MonoGame/XNA's default `RasterizerState`.**
  `griffin_live.glb`'s index data was loaded as-is (no re-winding); MonoGame's default
  `CullCounterClockwise` treats *clockwise* as front-facing, which is backwards for a right-handed,
  CCW-front glTF mesh. Fixed by using `RasterizerState.CullClockwise` for the main toon pass and
  `CullCounterClockwise` for the inverted-hull outline pass (the two are swapped from what an
  XNA-only codebase would default to).

### Lead-review fix round (2026-09-30, same day)

A first commit's screenshots and numbers were reviewed and sent back with five findings, all fixed;
full detail (and the before/after numbers/screenshots) is in
`docs/spikes/055-3d-mini-spike.md`'s fourth-pass section -- this is the short tooling-facing version:

1. **GPU skinning**, replacing CPU skinning into a per-instance `DynamicVertexBuffer` -- the direct
   cause of the worst 1%-low framerate (9 fps at 24 beasts) and dominant CPU cost (9.4 ms). Also found
   along the way: `Node.GetWorldMatrix(animation, time)`, called once per node per instance per frame,
   walks the full ancestor chain from scratch every call and measurably allocates doing it; replaced
   with `AnimatedPose` walking the hierarchy itself via a precomputed `ParentIndex` array and
   `Node.GetLocalTransform(animation, time)` (one node, no ancestor walk).
2. **Facing and camera**: beasts now carry a fixed 90-degree yaw so they read as a 3/4 side profile
   (matching `content/art/beasts/griffin/griffin.png`) instead of head-on with wings straight up.
3. **Colour**: `LightColor`/`HighlightBoost` pulled back toward neutral -- the original tuning rendered
   noticeably more saturated/orange than both the Meshy source texture and the approved illustration
   (confirmed by sampling actual pixels from both, not a texture colour-space bug).
4. **Board layout and hex scale**: the hex grid now mirrors the real game's own
   `src/BeastCraft.Presentation/Board/HexLayout.cs` convention and ratio, sized from the beast's own
   bind-pose bounding box; the board camera's tilt was increased (24 -> 48 degrees) once a 24-beast test
   showed *why* the original tilt collapsed multiple hex rows onto the same screen position (an
   orthographic camera looking straight down an axis projects that axis away entirely -- rows are
   separated along world Z, so a shallow tilt gave them almost no screen-space separation).
5. **Screenshot fps stats reading `0.0`**: a real, standalone bug in `StatsTracker.RecordFrame`'s
   rolling-window trim (it defaulted to wiping the *entire* history every call, not just the part
   outside the window) -- `--bench`'s own numbers were never affected (computed independently), only
   the on-screen overlay and every screenshot taken before this fix.

## Fifth pass: swarm via merged-batch GPU skinning (`--battle`)

`docs/spikes/055-3d-mini-spike.md`'s section 2.10 has the full account; this is the tooling-facing
summary. **No Meshy credits spent this pass** -- the real-money `image-to-3d` call was refused by this
environment's own safety classifier independent of the task's spend authorisation (a `--dry-run`
confirmed the exact 30-credit request first); `Tooling/Spike55/blender_export_vat_swarmling.py` builds a
procedural placeholder "swarmling" body instead (1,688 tris), textured with the real approved
Swarmling illustration's own sampled palette.

**VAT (vertex animation textures, sampled in the vertex shader) does not compile** under MonoGame
3.8.5's DesktopGL effect profile at all (`"invalid DCL register type for this shader model"` /
`"TEXLD using undeclared sampler"`, isolated to a 6-line minimal repro). Pivoted to GPU skinning via a
per-instance-sliced bone-array offset instead -- `SwarmBones[]` in `Content/Effects/Toon.fx`'s "Fifth
pass" section, the same `Bones[]`-array technique already proven for the Griffin, just bigger (24
swarmlings x 3 bones = 72 slots). The merged-batch property survives the pivot either way: the whole
swarm draws from one shared static vertex buffer (`GpuMesh.BuildSwarmMerged`) in exactly 2 draw calls,
regardless of swarm size -- confirmed at 24 instances: 4 draw calls total for "swarm alone" (backdrop +
hex grid + 2 swarm passes), 16 for a full 3-Griffin + 24-swarmling battle.

Run the battle scene:

```
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --battle --griffins 3 --swarm 24
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --bench 1 --battle --griffins 3 --swarm 24 --seconds 10 --fps-cap 30 --out result.json
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --screenshot shot.png --battle --griffins 3 --swarm 24 --zoom arena
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --screenshot shot.png --battle --griffins 3 --swarm 24 --zoom front
dotnet Tooling/Spike55/Live3D/bin/Release/net10.0/Live3D.dll --screenshot shot.png --battle --griffins 0 --swarm 1 --zoom close
```

`--zoom arena|front|close` (fix round, task item 3): `arena` (the default) frames the whole fixed 11x15
board; `front` crops tighter around the Griffins' front line and the first couple of swarm rows; `close`
is a tight instance-fit close-up (for a single-unit shot, e.g. the swarmling close-up above) -- see
`Game1.RebuildCameraArena`/`RebuildCameraFitInstances`. `--screenshot` renders at the task brief's full
portrait 1080x1920 regardless of zoom level; interactive play and `--bench` stay at the existing
540x960 half-scale window.

`--battle` switches the scene to `BattleGriffins` GPU-skinned beasts + `BattleSwarm` swarmlings on an
11x15 arena instead of the fourth pass's single-species Tab-cycling stress test; `--griffins 0` gives
"swarm alone". `--fps-cap N` (bench only) throttles via MonoGame's own fixed-time-step, measuring
whether the scene sustains the 30fps battle cap section 2.9's research recommends, alongside (not
instead of) the uncapped throughput number -- both are always reported in `--bench`'s JSON.

**Superseded (kept for reference only, not used by the runtime any more):** the original procedural
placeholder body was rebuilt with
`blender -b --python Tooling/Spike55/blender_export_vat_swarmling.py -- --out OUTDIR`, writing
`swarmling_mesh.bin`/`swarmling_bones.bin`/`swarmling_meta.json` plus a separately PIL-built
`swarmling_texture.png`. The fix round below replaces all of that with a single real GLB -- see its own
section for the current rebuild command.

A genuinely new Android risk this pivot introduces: `SwarmBones[72]` is 288 vec4 registers, which
**does not fit inside GLES 3.0's own spec-guaranteed minimum (256 vec4)** even though it runs fine on
this desktop GPU's much larger uniform budget -- see the gate report for what a production fix looks
like (3x4 matrices, fewer bones/swarmling, or a smaller simultaneous-swarm cap). **Closed in the fix
round below** for the real Swarmling's actual bone count.

## Fifth-pass fix round: the real Swarmling (`swarmling_live.glb`)

Per the producer's own 35-credit Meshy spend (image-to-3D of the approved Swarmling illustration, 30
credits, plus a remesh to ~1,200 tris, 5 credits -- run directly, not through `meshy.py`; see
`Tooling/ArtLab/provenance/meshy-01a0f424-1644-75af-8cdb-e25ef6452845.md` and
`Tooling/ArtLab/provenance/meshy-remesh-01a0f426-b36a-7051-8603-3b77b80b9616.md`), this pass replaces the
procedural placeholder above with a real, rigged, animated Swarmling. Full account (asset stats, register
budget, camera-framing fix, bench numbers, caveats, and a lead-reviewed second fix round for unit scale,
board-width fill, the swarm outline, close-up facing, and hiding the debug overlay in `--screenshot`
mode): `docs/spikes/055-3d-mini-spike.md` section 2.11.

### Asset: `blender_export_live_swarmling.py`

Reuses `blender_export_live.py`'s (Griffin) import/weld/texture-pick/downsize/normalise/auto-weight
pipeline in shape, for a much smaller **6-bone** rig (`bone_body` as the armature root, `bone_head`, 4 leg
bones -- no separate horn bones, weighted rigidly to `bone_head` instead; see the script's module
docstring for the register-budget reasoning) and exports a real skinned, animated GLB exactly like the
Griffin's, not the old custom binary format below:

```
blender -b --python Tooling/Spike55/blender_export_live_swarmling.py -- \
    --glb <swarmling_remesh.glb> --out <output dir> [--texture-size 512] [--fps 24]
```

Writes `swarmling_live.glb` (skinned mesh + armature + Idle/Move clips, JPEG base-colour texture --
0.18 MiB for the committed asset at `Tooling/Spike55/Live3D/Content/model/swarmling_live.glb`, well
under the 1 MB target).

### Runtime changes this pass

`SwarmlingSkinnedModel.cs` (the custom `swarmling_mesh.bin`/`swarmling_bones.bin`/`swarmling_meta.json`
loader below) is retired: the runtime now loads `swarmling_live.glb` with `GltfSkinnedModel.Load` --
the same class the Griffin uses -- and evaluates its pose every frame via `AnimatedPose`, exactly like a
Griffin. What stays swarm-specific is purely the *batching*: `Content/Effects/Toon.fx`'s `SwarmBoneRows[]`
(a flat `float4[]`, 3 rows/bone, not a `float4x4[]`) and `Game1.cs`'s `_swarmBatches` draw the swarm in
fixed-size batches of `SwarmBatchCapacity = 12` swarmlings (24 total = 2 batches, 4 draw calls) instead of
one merged buffer -- **216 of GLES 3.0's guaranteed-minimum 256 vec4 vertex uniform registers** at this
rig's 6-bones/swarmling x 12-per-batch x 3-vec4-per-bone layout; see the shader's header comment for the
full arithmetic.

The battle camera also changed this pass (`Game1.RebuildCameraArena`, task item 3): it now frames the
fixed 11-wide arena (sampling every cell centre, not wherever units happen to be placed) instead of
fitting to the actual occupied cells, which read as "too far out" in the previous pass's own screenshot.
`--screenshot` renders at the task brief's full portrait 1080x1920 (interactive play and `--bench` stay
at the existing 540x960 for comparability across passes). New CLI flag: `--zoom arena|front|close`
(`arena` = full board, the default; `front` = a tighter crop around the front line; `close` = a
tight instance-fit close-up, for the swarmling/beast close-up shots).

### Licences

- **SharpGLTF** (`SharpGLTF.Core`, v1.0.7) -- MIT licence. https://github.com/vpenades/SharpGLTF
- **MonoGame.Framework.DesktopGL** / **MonoGame.Content.Builder.Task** (v3.8.5.1) -- same licence
  already covered for the rest of the game (see the repo's `THIRD-PARTY-NOTICES.md`).
- The Verdant Hollow backdrop (`content/art/backdrops/r01/sun0/medium.png`), the griffin's approved
  illustration/palette, and the Swarmling's approved illustration/palette
  (`content/art/enemies/swarmling/swarmling_hollow.png`, sampled only for a flat swatch colour, not
  used as a texture) are existing, already-approved game assets -- no new licence implications.
- `crest_alt.glb`'s geometry is procedurally generated in `blender_export_live.py` (a simple fanned
  quad-strip "plume"), and the fifth pass's swarmling body is procedurally generated in
  `blender_export_vat_swarmling.py` (icospheres + cones) -- neither is AI-generated or derived from
  Meshy output.
