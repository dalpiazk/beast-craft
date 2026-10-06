# eyefix: camera-projection eye repair

Local-only tooling (not run by CI) for one specific texture-repair problem that showed up twice in
the Meshy image-to-3D pipeline: a generated mesh has a believable eye on the side the source image
showed, and a missing, blank, or malformed eye on the hidden side (the Phoenix's source art is a
3/4 view, so Meshy never saw its far eye; the Golem's v2 remesh produced a stray double-print of
both eyes slightly off the face). Rather than hand-painting the fix, this repairs it by rendering
the *good* eye through a virtual camera, reflecting that camera through the mesh's own fitted
sagittal (left/right symmetry) plane, and re-projecting the render onto the texture wherever a
UV texel's corresponding mesh point lands on the *other* side of that plane. The result is applied
back as a normal edit to the base-colour texture -- no new geometry, no shader tricks, nothing
engine-side.

## How it works (two stages, two processes)

1. **`eyefix_bl.py`** (runs inside Blender -- needs `bpy`): imports the GLB, fits/uses the
   supplied symmetry plane, and for each configured "op" renders the source side through an
   orthographic camera `C`. The target camera `C'` is `C` reflected through the plane (so its
   image is `C`'s mirrored left-right). Every texel on the target side of the plane is projected
   through `C'`, weighted by a feathered mask ellipse x BVH-occlusion visibility x face-angle, and
   recorded (not yet painted) as a sample coordinate + weight in `C`'s source render. Needs
   `eflib.py` (camera/mesh helpers) next to it on `sys.path`, which this script arranges itself.
2. **`compose.py`** (plain Python, needs Pillow + numpy, no Blender): reads the op data
   `eyefix_bl.py` wrote and the *original* texture, blends the weighted projected samples onto a
   copy of it, and writes the repaired texture as a PNG.

Two optional finishing steps:

- **`jpegenc.py`**: if the original texture was a JPEG, re-encodes the repaired PNG with the same
  quantization tables and chroma subsampling as the original (so only the repaired region's
  compression artifacts change, not the whole image's), and reports how much the untouched area
  drifted (should be ~0, modulo JPEG's own re-encode noise).
- **`glbclean.py`** (needs `glbtex.py` next to it): splices the finished texture back into a copy
  of the GLB, rewriting only that one image bufferView -- every other bufferView (geometry,
  skinning, animation) is byte-identical, which `glbclean.py compare A.glb B.glb` verifies. It also
  drops any `normalTexture`/`metallicRoughnessTexture` that merely aliases the base-colour image
  (Live3D's toon shader only reads base colour), which is the "base-colour-only material" cleanup
  used on the Golem's v2 fix.

## Usage

All paths below are examples -- the prepped/pre-rig GLB and its original texture are local pipeline
outputs (`Tooling/Animation/prep_mesh.py` / `rig_creature.py`), not committed to this repo, so you
point these scripts at wherever your own run produced them.

```sh
OUT=/path/to/scratch/eyefix-out   # pick anywhere; must hold cfg.json for compose.py to find it
mkdir -p "$OUT"

# 1. Start from one of the two worked configs here (see "Configs" below) and point it at your GLB.
cp Tooling/Animation/eyefix/phoenix_cfg.json "$OUT/cfg.json"
# edit "$OUT/cfg.json"'s "glb" and "out" fields to your paths, OR leave the template's
# placeholder values and pass them on the command line instead (equivalent):

blender -b --factory-startup -P Tooling/Animation/eyefix/eyefix_bl.py -- \
  "$OUT/cfg.json" --glb /path/to/prepped_or_anim.glb --out "$OUT"

# 2. Composite the projected samples onto the original texture (extract it first if it's packed in
#    the GLB -- glbtex.py extract can do that: `python glbtex.py extract SRC.glb orig_tex.png`).
python Tooling/Animation/eyefix/compose.py /path/to/orig_tex.png "$OUT" "$OUT/fixed_tex.png"

# 3. (JPEG source only) re-encode matching the original's compression.
python Tooling/Animation/eyefix/jpegenc.py /path/to/orig_tex.jpg "$OUT/fixed_tex.png" \
  "$OUT/mask.png" "$OUT/fixed_tex.jpg"

# 4. Splice the repaired texture back into a copy of the GLB, and (optionally) drop any
#    normal/metallicRoughness textures that just aliased the base-colour image.
python Tooling/Animation/eyefix/glbclean.py /path/to/source.glb /path/to/repaired.glb \
  "$OUT/fixed_tex.jpg"

# Verify only the image bufferView changed (geometry/skin/animation untouched):
python Tooling/Animation/eyefix/glbclean.py compare /path/to/source.glb /path/to/repaired.glb
```

`compose.py` writes `mask.png` into `$OUT` as it runs (the union of every op's paint weight, for
the `jpegenc.py` diff step above and for eyeballing coverage).

## Configs

Both are the exact op parameters used for the two producer-approved repairs; adapt the plane/op
geometry for a new mesh rather than starting from scratch, since fitting the sagittal plane and
aiming each op's source ray were the fiddly, iterative part.

- **`phoenix_cfg.json`** -- one op, mirrors the Phoenix's visible (3/4-view) eye onto the hidden
  side that the source art never showed.
- **`golem_v2_cfg.json`** -- four ops (`erase_outer`, `erase_inner`, `stamp_right`, `stamp_left`):
  the Golem v2 remesh's eyes had drifted slightly off the symmetry plane and printed a faint second
  copy nearby; this erases both stray prints and stamps a clean mirrored pair back on, centred and
  symmetric about the fitted plane (producer choice: "re-centre the eyes").

Each config's `"ops"` entries document the per-op knobs in context (source ray, target mask
ellipse, feather, occlusion tolerance, `two_sided` for inconsistent mesh winding, `protect_gr` to
keep saturated red/orange target texels such as a beak or crest out of the paint). `--glb`/`--out`
on the `eyefix_bl.py` command line override the config file's own `"glb"`/`"out"` fields, which are
otherwise local-machine paths you would have to edit by hand.
