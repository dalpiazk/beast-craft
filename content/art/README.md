# content/art

| Folder | What | Shipped |
| --- | --- | --- |
| `pixel/` | The code-generated pixel placeholders (`Tooling/PixelArt`) and **the art manifest**, `pixel-art-manifest.json`, which indexes every sprite the game draws, including the illustrated ones below. | yes |
| `beasts/<id>/<id>.png` | Illustrated in-game beast sprites: 512x512 frames (504 px on the longer side), straight alpha, feet pivot, linear filter. Exported from the masters by `Tooling/ArtLab/scripts/export_ingame.py`. Listed in the manifest through `Tooling/PixelArt/illustrated.json`. | yes |
| `source/<id>/` | Archival masters of the illustrated beasts. Not loaded by the game and not copied into builds. | no |

All PNGs are stored in Git LFS (see `.gitattributes`).

## `source/<id>/`
| File | What |
| --- | --- |
| `character.png` | The full-resolution transparent character, from the 2x painted final (1792x2304 canvas). It is cropped so the **feet pivot is its bottom centre**. |
| `parts/*.png` + `parts.json` | The rig split: each part with its offset on the 1792x2304 canvas, its parent, its pivot (canvas and local px), and the back-to-front draw order. The parts are machine-cut; see the provenance record for known limits. |
| `design_pick.png` | The design source the producer picked, which the lock step reproduces (canny from its lines, colours from its swatch block). The phoenix's is `d_406`, which is also a style reference. |

- The PNGs were re-saved losslessly by `Tooling/ArtLab/scripts/archive_rig.py`: the colour under fully transparent pixels is cleared and every visible pixel is unchanged.
- `parts.json` names `character_full.png`, which is not archived: it is `character.png` placed on the 1792x2304 canvas with its feet at `character.pivot`.
- How each asset was made (prompts, seeds, settings, the chosen candidate and the producer's decision) is in `Tooling/ArtLab/provenance/<id>.md`. These are AI-assisted assets: see `docs/art/art-brief.md` for the approval and disclosure policy.
