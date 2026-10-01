# Provenance: Meshy `remesh` task `01a0f38d-c409-702e-b976-62bff441b88f`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py remesh`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, `remesh` (`POST /openapi/v1/remesh`) |
| Input | `input_task_id=01a0f351-869e-7319-9820-e4b6e8b6b226` -- the pass-2 textured `meshy-7.1` image-to-3D task (929,638 tris, 2K base-colour texture; see `meshy-01a0f351-869e-7319-9820-e4b6e8b6b226.md`) |
| Settings | `{"input_task_id": "01a0f351-869e-7319-9820-e4b6e8b6b226", "target_polycount": 8000, "topology": "triangle", "target_formats": ["glb"]}` |
| Task id | `01a0f38d-c409-702e-b976-62bff441b88f` |
| Status | `SUCCEEDED` (~89s) |
| Credits spent | `5` (flat `remesh` rate, confirmed with `--dry-run` before the real call and by comparing `balance` before/after) |
| Balance before | 1670 credits |
| Balance after | 1665 credits |
| Generated | 2026-09-30, under a specific one-call, 5-credit spend authorisation (producer, 2026-09-30) |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs; no attribution requirement (unlike the free CC BY tier) |

## Notes

- Generated with `python Tooling/ArtLab/scripts/meshy.py remesh --task 01a0f351-869e-7319-9820-e4b6e8b6b226 --target-polycount 8000 --topology triangle --formats glb --yes`. This is the issue #55 mini-spike's **third pass** (see `docs/spikes/055-3d-mini-spike.md`, "Third pass: low-poly (remesh)"), a follow-up to the untextured pass 1 (`spike55-griffin-meshy.md`) and textured pass 2 (`meshy-01a0f351-869e-7319-9820-e4b6e8b6b226.md`).
- Downloaded outputs: `model.glb` (11.35 MB) plus a **full PBR texture set** -- `0_base_color.png` (2048x2048), `0_normal.png` (2048x2048), `0_roughness.png`, `0_metallic.png`, `0_metallic_roughness.png` (4096x4096) -- and a thumbnail. The remesh docs (as of 2026-09-30) do not state whether texture is carried through; this call confirms it is, for this task/settings combination, contrary to the "verify before relying on it" caveat in `meshy.py`'s own docstring.
- `gltf_inspect.py` on the raw downloaded GLB: **8,372 tris, 12,987 verts (as exported, includes UV/normal-seam vertex duplication), 1 material, 3 images** (base_color `texture_0`, `normal`, `texture_0_metallic_roughness`), node named `output_unwrapped` (i.e. this remesh task UV-unwraps as part of its own process -- no separate Smart UV Project or bake was needed for this asset; see the spike doc for what that implies for step 3 of the task brief).
- **A first read of connected-component count was wrong and is recorded here so it isn't repeated:** raw bmesh analysis of the as-imported mesh showed 2,452 connected components (largest 38 verts) -- consistent, at first glance, with a shattered mesh. Checked before accepting that reading: the glTF exporter had split many vertices into coincident, unwelded position duplicates wherever UV/normal differs per face corner (completely normal glTF behaviour, not fragmentation). Blender's "Merge by Distance" at 1e-4 collapses 12,987 raw verts to **4,192 true verts, 1 single connected component**, 28 non-manifold edges and 18 boundary edges out of 12,562 -- a coherent, near-watertight low-poly mesh. This weld step is now the first thing `Tooling/Spike55/blender_lowpoly_render.py` does after import, before weighting.
- Automatic (heat-map) bone weighting converged on the **first attempt**, directly on the welded mesh, with 0 / 4,192 vertices left unweighted -- no voxel-remesh-donor fallback needed (contrast pass 1 and pass 2, both of which needed a voxel-remesh-and-data-transfer workaround; see `docs/spikes/055-3d-mini-spike.md` sections 2.3 and 2.6).
- Texture downsized from 2048x2048 to 1024x1024 (`Image.scale` in Blender) before rendering, per the task brief's 1K cap for a low-poly asset.
- **Committed?** No. `model.glb` and its texture set are large binary working files, kept out of git per the task's instructions; only downsized render outputs (idle/move GIFs, the low-poly comparison sheet, the low-poly board mock) are committed under `docs/spikes/055/`.
- `expires_at` on the task (ms since epoch): see `task.json` in the (uncommitted) working directory; not relied on, output downloaded immediately.
