# Provenance: Meshy image-to-3D task `01a113e5-56e1-73fa-adfe-b8c009dd74ec`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, image-to-3D (`POST /openapi/v1/image-to-3d`) |
| Input | Session scratchpad `swarmling_meshy_input.png` (the Meshy input image prepared from the approved sprite `content/art/source/enemies/swarmling/hollow/character.png`) -- SHA-256 `f8468fe3110affc9e65ee00d77c14943edba534a69478c634d71b76556cf2b51` |
| ai_model | `meshy-7.1` |
| Settings | `{"ai_model": "meshy-7.1", "should_texture": true, "enable_pbr": false, "texture_resolution": "2k", "target_polycount": 1500, "should_remesh": true}` |
| Task id | `01a113e5-56e1-73fa-adfe-b8c009dd74ec` |
| Status | `SUCCEEDED` |
| Credits spent | `30` |
| Generated | 2026-10-07 |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md`), no attribution requirement applies |
| Outcome | Used -- the Swarmling (Hollow enemy) rig/animation base mesh: prepped with `prep_mesh.py --no-ground-sheet --target-tris 1500` (1499 tris), both eyes present (no eye repair); the `blob` rig in its 6-joint swarm configuration and all 7 clips, also drawn through Live3D's merged swarm batches, see `Tooling/Animation/README.md` v21. The animated GLB is a scratch deliverable, not shipped. |

## Notes

- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with the game unless separately approved as a final, and nothing here changes the licence position of the ten beasts already in the game (made by the local `Tooling/ArtLab` diffusion pipeline, unrelated to Meshy).
- `expires_at` on the task (ms since epoch): `1791594450902` -- the docs describe this as when the task result expires; no separate fixed retention period for the download links is documented in plain words, so outputs were downloaded immediately rather than relied on being re-fetchable later.
