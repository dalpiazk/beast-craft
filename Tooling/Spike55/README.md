# Spike #55 tooling: pre-rendered 3D toon vs 2D code bone rig (Griffin)

Scripts behind `docs/spikes/055-3d-mini-spike.md`, the mini-spike gate report comparing:

- **Path A** -- a pre-rendered Blender toon render of a Meshy (paid tier, producer-generated,
  output owned) image-to-3D Griffin mesh, rigged and animated, rendered to transparent sprite
  frames (keeps MonoGame; see `blender_toon_render.py`).
- **Path B** -- a scripted (code) bone rig over the Griffin's *existing* machine-cut parts
  (`content/art/source/griffin/parts/*.png` + `parts.json`), no Spine licence, driven by
  sinusoidal oscillators -- what Spine Essential does (bones, no mesh deform); see `rig2d.py`.

No GLB and no Blender install are committed here. The Meshy GLB used for Path A is a private,
producer-supplied input (see `Tooling/ArtLab/provenance/spike55-griffin-meshy.md`); rerunning
Path A needs your own copy of it (or a fresh Meshy export) and a Blender install. To generate a fresh
export (or re-download an existing task's outputs) from the command line instead of the web app, see
`Tooling/ArtLab/README.md`'s "Meshy (3D)" section for `Tooling/ArtLab/scripts/meshy.py` -- a local-only,
spend-guarded CLI for Meshy's paid API.

## Scripts

| Script | What it does |
| --- | --- |
| `gltf_inspect.py` | Pure-stdlib GLB inspector (tri count, vertex count, embedded image sizes, node/skin/animation counts) -- no 3D library needed. |
| `inspect_orientation.py` | Blender headless: imports a GLB and renders quick orthographic front/side/top probes so you can read off which local axis the mesh faces, before writing any camera/rig code against it. |
| `blender_toon_render.py` | Blender headless: imports the GLB, normalises it (feet on ground, scaled to a fixed height), builds a 2-3 band toon material (Diffuse -> Shader to RGB -> ColorRamp -> Emission) plus an inverted-hull ink-plum outline (Solidify), places an orthographic hex-board camera, builds a simple armature, attempts automatic (heat-map) weights with a voxel-remesh fallback if that fails, and renders idle (12 frame) and move (8 frame) loops as transparent PNGs. |
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
```

`blender_toon_render.py` writes `idle/idle_NN.png`, `move/move_NN.png`, `hero.png` (neutral
pose) and, for reference only, `griffin_rig.blend` (not meant to be committed -- it's a large
binary scratch file).

Blender itself was installed as a portable zip (not the winget/MSI package -- that download was
blocked by a Cloudflare bot challenge in this environment) from
`https://download.blender.org/release/Blender5.2/` (or any of its mirrors), extracted to a
short, non-project path (long paths inside the zip, e.g. under
`python/lib/site-packages/pkg_resources/tests/...`, exceed Windows' `MAX_PATH` if extracted deep
inside a nested temp directory).

## Known issues found while building this (see the gate report for the full write-up)

- The Meshy "generate" GLB download has geometry + UVs but **no material or embedded texture**.
  Path A's toon shading uses a flat colour sampled from the approved Griffin palette
  (`Tooling/ArtLab/provenance/griffin.md`), not the source image.
- Blender's automatic (heat-map) bone weighting **did not converge** on this mesh, even after a
  voxel remesh (`blender_toon_render.py` tries the remesh fallback automatically and logs both
  attempts). `blender_toon_render.py` falls back to animating the whole Griffin object rigidly
  (bob/sway/lean) rather than true per-part skeletal deformation -- the armature and bone
  hierarchy are still built and left in the scene as documentation of the intended rig.
- In Blender 5.2's headless/background EEVEE, moving/rotating the *mesh* object between renders
  in the same session was silently ignored by the render operator (reproduced with a pixel
  diff: identical output despite different `object.rotation_euler`), even after forcing a
  depsgraph update. The fix was to drive an unencumbered parent `Empty` instead of the mesh
  object directly (the mesh has an Armature modifier + armature-parenting from the automatic
  weights attempts, which is suspected but not confirmed to be related) -- see the comments in
  `blender_toon_render.py` around `anim_root`.
