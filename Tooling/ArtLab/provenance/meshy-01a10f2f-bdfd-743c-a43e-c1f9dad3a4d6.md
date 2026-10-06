# Provenance: Meshy image-to-3D task `01a10f2f-bdfd-743c-a43e-c1f9dad3a4d6`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, image-to-3D (`POST /openapi/v1/image-to-3d`) |
| Input | Session scratchpad `leviathan_meshy_input.png` -- composited on white from `content/art/source/leviathan/character.png` -- SHA-256 `44f5f57f6e37a604bbbdf1ce9b4218f964edfaab5da6f640f6ec5b7d37d2a57e` |
| ai_model | `meshy-7.1` |
| Settings | `{"ai_model": "meshy-7.1", "should_texture": true, "enable_pbr": false, "texture_resolution": "2k", "target_polycount": 8000, "should_remesh": true}` |
| Task id | `01a10f2f-bdfd-743c-a43e-c1f9dad3a4d6` |
| Status | `SUCCEEDED` |
| Credits spent | `30` |
| Generated | 2026-10-06 |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md`), no attribution requirement applies |

## Notes

- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with the game unless separately approved as a final, and nothing here changes the licence position of the ten beasts already in the game (made by the local `Tooling/ArtLab` diffusion pipeline, unrelated to Meshy).
- `expires_at` on the task (ms since epoch): `1791515420758` -- the docs describe this as when the task result expires; no separate fixed retention period for the download links is documented in plain words, so outputs were downloaded immediately rather than relied on being re-fetchable later.

## Outcome

**Used.** Prepped with `Tooling/Animation/prep_mesh.py --creature leviathan --no-ground-sheet` (the
coil's own underside is the ground contact; the default ground-sheet removal would have cut into it)
and carried through the new `serpent` rig template and the `slither` Move gait. See
`Tooling/Animation/README.md`'s "v20" section.
