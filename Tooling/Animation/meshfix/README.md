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
