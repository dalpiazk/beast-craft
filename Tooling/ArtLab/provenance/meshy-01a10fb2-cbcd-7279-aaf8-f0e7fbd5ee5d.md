# Provenance: Meshy image-to-3D task `01a10fb2-cbcd-7279-aaf8-f0e7fbd5ee5d`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, image-to-3D (`POST /openapi/v1/image-to-3d`) |
| Input | Session scratchpad `frost_wyrm_v1_meshy_input.png` -- the round-1 standing-pose collage candidate v1 #1 (seed 704; see `Tooling/ArtLab/provenance/frost_wyrm-standing-candidates.md` section 3) -- SHA-256 `1de8a9af7609cbdae83dbb09131b263dc63e1675ab6342de86f7a31bf151e7c1` |
| ai_model | `meshy-7.1` |
| Settings | `{"ai_model": "meshy-7.1", "should_texture": true, "enable_pbr": false, "texture_resolution": "2k", "target_polycount": 8000, "should_remesh": true}` |
| Task id | `01a10fb2-cbcd-7279-aaf8-f0e7fbd5ee5d` |
| Status | `SUCCEEDED` |
| Credits spent | `30` |
| Generated | 2026-10-06 |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md`), no attribution requirement applies |

## Notes

- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with the game unless separately approved as a final, and nothing here changes the licence position of the ten beasts already in the game (made by the local `Tooling/ArtLab` diffusion pipeline, unrelated to Meshy).
- `expires_at` on the task (ms since epoch): `1791523999967` -- the docs describe this as when the task result expires; no separate fixed retention period for the download links is documented in plain words, so outputs were downloaded immediately rather than relied on being re-fetchable later.

## Outcome

**Used.** Generated from standing candidate v1 #1 (seed 704, see
`Tooling/ArtLab/provenance/frost_wyrm-standing-candidates.md` section 3's round-1 sheet). The
resulting mesh (session scratchpad `frost_wyrm_v1_i23/model.glb`) is the correct Frost Wyrm source
mesh and was carried through `Tooling/Animation/prep_mesh.py --creature frost_wyrm` and
`calib_views.py` for the anim-last prep/calibration round. `meshy-01a10fac-...md`'s output (from the
wrong candidate, v2 #1) was not used -- see that file's own Outcome note.
