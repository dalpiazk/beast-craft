# meshfix: hand-authored mesh surgery

Local-only tooling (not run by CI), same convention as `eyefix/`: a one-off geometry fix for a
specific producer-rejected mesh, not a general tool. So far this holds `treant_surgery.py`, the
fix for the Treant's first rig/animation pass (right arm fused into the chest, flat-backed leaf
crown, non-humanoid limb bend -- see `Tooling/Animation/README.md`'s "v20 round 2" section for the
full before/after mesh stats).

## treant_surgery.py

Runs inside Blender (needs `bpy`/`bmesh`):

```
blender -b --python meshfix/treant_surgery.py -- TREANT_PREPPED.glb OUT_DIR [clean,arms,crown]
```

- `TREANT_PREPPED.glb` -- the output of `prep_mesh.py --creature treant --no-ground-sheet`.
- `OUT_DIR` -- written with the repaired `treant_prepped.glb` (the new mesh `rig_creature.py` takes
  as is), the working `.blend`, and `surgery_report.json` (vertex/triangle/boundary-edge/
  non-manifold-edge/component counts after every stage).
- The optional third argument selects which stages run (default `clean,arms,crown`): `clean`
  (watertight repair of the decimated input's open/non-manifold edges), `arms` (right forearm/hand
  removed and replaced with a mirrored copy of the free left arm; left arm lowered into a relaxed
  A-pose), `crown` (the front-only leaf crown mirrored to fill in a full back).

All paths are arguments -- nothing in the script is machine- or session-specific. The script is not
bit-reproducible (a re-run gives identical stage stats but a handful of crown-back triangles can
differ); the shipped asset is a specific run's output, not something `rig_creature.py` regenerates.

## prop_surgery.py (v22, enemies batch 2)

Config-driven surgery for held props and missing limbs (Blender, `bmesh`):

```
blender -b --python meshfix/prop_surgery.py -- meshfix/<name>_cfg.json PREPPED.glb OUT_DIR
```

Stages, in config order (the docstring has the details): `copy` (faces in a region, optionally
colour-gated, read from the ORIGINAL mesh, capped and placed by a similarity transform -- a new
limb), `delete`, `rip` (flood a prop from a seed through its own region/colours and delete only the
band of faces mixing prop and body vertices), `drop_small`, `fill` (ring-UV patches; `poke: false`
ear-clips long strips, `smooth` subdivides, relaxes, inflates and colours them from the nearest
original vertex of the same piece), `tube` and `mushroom` (new closed shells). New parts stay
separate closed shells; the rig's `rigid_parts` weights them. Deterministic.

- `shaman_cfg.json` -- the staff (shaft and mushroom cap) was one surface with the beard and hood; it
  is ripped free (`rip` + `fill`), nothing regenerated: 8394 tris, 0 open edges, 2 shells.
- `archer_cfg.json` -- one modelled arm lay across the chest with its fist on the arrow and the bow
  grip had no arm: two new arms (copies of that forearm and fist) go shoulder-to-grip and
  shoulder-to-nock, the old arm is cut away, the bow ripped free and the arrow gap bridged:
  8734 tris, 0 open edges, 6 shells. Round 3 (producer rejected the Meshy bow): the ripped bow
  tangle (stave, string, both flame sticks, arrow) is removed whole with the new `drop_component`
  op (726 vertices) instead of kept; the quiver stays. Body 6936 tris, 0 open edges.
- `archer_bow.py` + `archer_bow_cfg.json` (round 3) -- a scripted, deterministic bow and arrow added
  to that body as separate closed shells: a 1.52-unit recurve (~0.8 of the Archer's height) with
  tapered 6-sided limbs, a leather-wrapped grip, one thin 4-sided string and three curled violet
  flame tongues at each tip (568 tris, 0 open / 0 non-manifold edges); a separate arrow with a
  6-sided shaft, leaf head, three vanes and a violet flame at the head only (132 tris, 0 open
  edges). No new texture: every new vertex takes its UV from the matching painted surface of the
  ORIGINAL Meshy mesh (bow wood, violet flames, string, grip leather, hood leaf, quiver fletching),
  so the props share the Archer's single material and painted style. Writes `bow_report.json` (tris,
  edges, rest placement, one seed vertex per shell for the rig's `rigid_parts`).
  `blender -b --python meshfix/archer_bow.py -- meshfix/archer_bow_cfg.json BODY.glb ORIGINAL.glb OUT_DIR`.
  Round 4 (the approved art): a short pale-wood D-shaped self bow (0.95 chord = 0.5 of the height,
  sagitta 29 % of the limb, thick limbs, grip wrap, no flames; 268 tris, 0 open edges) and an arrow
  with a flaring violet flame at the head plus a smoke-trail wisp (228 tris, 0 open edges); the bow
  rests yawed -57 deg (rig `rest_yaw`). `archer_cfg.json`'s bow arm is now the exact mirror image of
  the draw arm (`copy` op's `post_mirror_x`, mirror across the trunk mid-plane x = 0.02).
- `archer_arms.py` + `archer_arms_cfg.json` (round 5) -- the producer found the arm joined the torso
  in the wrong place: the mesh had NO shoulders or upper arms, only the copied fists on short forearm
  stubs poking out of the chest under the leaf mantle. The stubs are cut at the wrist (the fists are
  kept as hands), a socket is cut in the torso wall just under the mantle on each side, and a 12-sided
  arm tube (shoulder cap, upper arm, bent elbow, forearm; 3 loops at the cap, 3 round the elbow) is
  zipped to the socket rim and to the fist's wrist rim, so body and arms are ONE watertight surface.
  The left arm is built from the mirror image of the right's parameters across x = 0.02. Every new
  vertex samples one cream-skin square of the existing atlas (found automatically, nearest the
  torso's colour; a triangle wave around the arm so there is no wrap seam). Input = prop_surgery's
  output: 6936 tris (3 shells); output 7522 tris, 1 shell, 0 open / 0 non-manifold edges; ~487 tris
  per arm. Writes `arms_report.json` (socket, cap, elbow, wrist and fist-tip points for the landmarks).
  `blender -b --python meshfix/archer_arms.py -- meshfix/archer_arms_cfg.json SURGERY.glb OUT_DIR`,
  then `archer_bow.py` on its output.
