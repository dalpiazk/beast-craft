# Provenance: Griffin Meshy image-to-3D model (spike #55 input)

**Status:** a private, producer-supplied input to the issue #55 mini-spike (docs/spikes/055-3d-mini-spike.md,
2026-09-29), not shipped in the game. Disclosed here per `docs/art/art-brief.md`'s AI-disclosure rule, the same as
every other AI-assisted asset in this project.

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, image-to-3D ("generate" pipeline) |
| Input | `scratchpad/spike55/griffin_meshy_input.png` -- the approved 2D Griffin illustration (`content/art/beasts/griffin/griffin.png`, provenance `griffin.md`), fed in as the source image |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md` / issue #55's notes), no attribution requirement applies |
| Generated | 2026-09-29, by the producer |
| Output file | a single `.glb`: geometry + UVs only -- **no material, no embedded texture, no rig, no animation** (confirmed with `Tooling/Spike55/gltf_inspect.py`: 51,488 tris, 39,446 verts, `images: 0`, `materials: 0`, `skins: 0`, `animations: 0`) |
| Used for | Path A of the spike: a Blender toon render (`Tooling/Spike55/blender_toon_render.py`) compared against a 2D code bone rig over the existing machine-cut parts (Path B, `Tooling/Spike55/rig2d.py`) |
| Committed? | **No.** The GLB is a large binary working file, kept out of git per the task's instructions; it is not needed to read or rerun `rig2d.py` (Path B), and Path A needs a fresh GLB (or the producer's original) to rerun regardless. |

## Notes

- Since the GLB has no baked texture, Path A's toon material uses a **flat colour sampled from the
  Griffin's approved palette** (`Tooling/ArtLab/provenance/griffin.md`'s swatch), not a projection of the
  source image. A production pipeline would need Meshy's textured export (a different download/tier
  setting) or a manual texture pass -- cost not measured in this mini-spike.
- The model is a tool only, per `docs/art/art-brief.md` section 0 and `Tooling/ArtLab/README.md`: it does
  not ship with the game, and nothing here changes the licence position for the ten beasts already in the
  game (those are made by the local `Tooling/ArtLab` pipeline, unrelated to Meshy).
