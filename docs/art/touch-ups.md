# Beast art: known touch-ups before the animation pass

All ten beasts' art is final and approved for the game (`docs/art/art-brief.md`), as single illustrated sprites.
The rig parts in `content/art/source/<id>/` are machine-cut, so this list is for the **animation pass** (Spine or
a part-based idle), where parts rotate and show what the still sprite hides. None of it is visible in the game
today. Details and coordinates are in each beast's `Tooling/ArtLab/provenance/<id>.md`.

## To fix
| Beast | Touch-up | Where it shows |
| --- | --- | --- |
| Griffin | The body's hidden under-wing fill has **flat, unpainted patches** (Telea fallback after the inpaint validator rejected the SDXL fills). Repaint under the `wings` part. | when the wings rotate |
| Griffin | The wing tips touch the left canvas edge (from the V2 wing placement). Pad the canvas if a pose needs more room. | wide wing poses |
| Basilisk | The **far-hind leg (2) is only partly visible**: a dark leg whose foot peeks out behind the far-front leg, reading as a shadowed fourth leg. An artist paint-over, or a wider stance, if it must be unmistakable. | the still sprite, at close zoom |
| Frost Wyrm | The small blue **crystal back-fin reads slightly wing-like** (the design is wingless). Remove or reshape it if "wingless" must be strict. | the still sprite |
| Leviathan | The coil/body cut is a straight polygon edge and the head/neck boundary is ragged; the coil's under-fill is a smooth Telea teal. | when the coil or head moves |
| Thunderbird | The body under-fill behind the wings is a soft grey-blue wash; `wing_right` keeps its Telea pre-fill. | when the wings rotate |
| Tarasque | The leg parts are straight polygon cuts at the belly. | when the legs swing |
| Golem | The head part includes the front moss tuft; a tiny detached flower-sprig outline sits left of the head. | head turns |
| All | Machine-cut joints: an artist pass on every part's joint edges is advised before animating. | any rotation |

## Already fixed (for the record)
| Beast | Fix |
| --- | --- |
| Tarasque | **Leg pivots** set by hand at the hips/shoulders under the shell (they had sat mid-leg), so the legs rotate about the hip. |
| Tarasque | **Tusk tip**: the `leg_near_r` part no longer catches the tip of the gold tusk (its clip starts below it); the tip belongs to the body/head. |
| Tarasque | The stray gold chin fang was removed (round 3). |
| Griffin | The stray ghost stroke above the head crest was removed. |
| Frost Wyrm | The face shows one eye and one mouth (round 2). |
