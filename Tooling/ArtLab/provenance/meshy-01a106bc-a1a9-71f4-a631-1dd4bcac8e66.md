# Provenance: Meshy text-to-3D refine task `01a106bc-a1a9-71f4-a631-1dd4bcac8e66`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, Text to 3D refine (`POST /openapi/v2/text-to-3d`, mode="refine") |
| Input | refine of preview task `01a106b9-ad41-735e-8052-c18e8dc2c41c` |
| ai_model | `meshy-6` |
| Settings | `{"mode": "refine", "preview_task_id": "01a106b9-ad41-735e-8052-c18e8dc2c41c", "enable_pbr": false, "texture_resolution": "4k", "ai_model": "meshy-6"}` |
| Task id | `01a106bc-a1a9-71f4-a631-1dd4bcac8e66` |
| Status | `SUCCEEDED` |
| Credits spent | `10` |
| Generated | 2026-10-04 |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md`), no attribution requirement applies |

## Notes

- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with the game unless separately approved as a final, and nothing here changes the licence position of the ten beasts already in the game (made by the local `Tooling/ArtLab` diffusion pipeline, unrelated to Meshy).
