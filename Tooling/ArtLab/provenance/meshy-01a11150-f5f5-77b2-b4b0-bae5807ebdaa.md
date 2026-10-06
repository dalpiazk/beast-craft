# Provenance: Meshy text-to-3D preview task `01a11150-f5f5-77b2-b4b0-bae5807ebdaa`

**Status:** local-tooling output of `Tooling/ArtLab/scripts/meshy.py`, not shipped in the game unless a producer later approves it as in-game art (see `docs/art/art-brief.md`'s AI-disclosure rule).

| | |
| --- | --- |
| Tool | Meshy, **paid tier**, Text to 3D preview (`POST /openapi/v2/text-to-3d`, mode="preview") |
| Input | prompt: 'A stylized chibi treant character, a gentle humanoid tree spirit standing upright in an A-pose: two arms held out and down away from the body with clear shoulders, elbows and wrist-like branch hands with twig fingers, two legs with clear knees and root-like feet, a smooth pale bark face with a small cute smile on a slender trunk body, brown bark limbs with leaf clusters on the shoulders, and a large round full crown of bright green leaves covering the whole head front, sides and back. Single character, no ground, no base, hand-painted warm style.' |
| ai_model | `meshy-6` |
| Settings | `{"mode": "preview", "ai_model": "meshy-6", "target_polycount": 8000, "should_remesh": true, "topology": "quad", "pose_mode": "a-pose"}` |
| Task id | `01a11150-f5f5-77b2-b4b0-bae5807ebdaa` |
| Status | `SUCCEEDED` |
| Credits spent | `20` |
| Generated | 2026-10-06 |
| Output ownership | Meshy's paid tier grants the generating account ownership of outputs (see Meshy's terms at generation time); unlike the free tier (CC BY, per `docs/design/content-bible.md`), no attribution requirement applies |

## Notes

- Generated with `Tooling/ArtLab/scripts/meshy.py` (local-only CLI; CI never runs it). The model is a tool only, per `docs/art/art-brief.md` section 0: it does not ship with the game unless separately approved as a final, and nothing here changes the licence position of the ten beasts already in the game (made by the local `Tooling/ArtLab` diffusion pipeline, unrelated to Meshy).

## Outcome

**Rejected: crown swallowed the face; mesh fixed in Blender instead.** This text-to-3D A-pose preview
read as a child in a leaf skirt with the crown overhanging and obscuring the face. The approved
Treant source mesh instead came from the image-to-3D route (`meshy-01a10f2e-...md`, the re-posed
collage), then `Tooling/Animation/meshfix/treant_surgery.py`'s hand-authored mesh surgery in Blender
for the right arm and crown back -- see that record and `Tooling/Animation/README.md`'s "v20 round 2".
