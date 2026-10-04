"""Tooling/Animation stage 1: mesh prep.

Imports a Meshy quad-remesh GLB, reports before/after topology stats, welds duplicate-position
vertices (every UV/normal seam leaves coincident duplicates on glTF export/import -- see
Tooling/Spike55/blender_lowpoly_render.py's docstring for the full story), **retopologizes to a
single connected, manifold, clean-quad-flow shell** (round-7 lead review -- see below for why the
old weld-then-Collapse-decimate approach wasn't enough), bakes the base colour onto the new mesh's
own low-island UVs at 1K, and exports a prepped GLB for rig_creature.py to pick up.

## Mesh-prep lesson (round 7): Meshy remesh output is fragmented -- always retopologize to one
## shell before rigging, not just weld-and-decimate.

Rounds 4-6 of this pilot chased a hip/belly skinning tear through several weight-logic fixes (hard
per-leg masks, then a cross-leg weight-gradient pass) without success. The lead's round-7 review
correctly identified the actual root cause as upstream of rigging entirely: the raw Meshy GLB
(`griffin_quad/model.glb`) reports **1018 connected components** by vertex-index adjacency before
any processing -- `weld_mesh` (below) collapses literal duplicate-position vertices from UV/normal
seams and gets that down to a reassuring-looking 4 components, 99%+ in the largest. But that
internal Blender vertex count is NOT what rig_creature.py ever actually rigs -- every stage in this
pipeline round-trips through a glTF EXPORT, and **glTF export re-splits a single welded vertex into
several wherever it carries more than one UV/normal value across its surrounding faces** (a format
requirement: one glTF vertex = one UV). The OLD prep_mesh.py welded, Collapse-decimated, and
exported -- but never re-wove those export-time seam splits back together, so the mesh
rig_creature.py actually imported and rigged was fragmented again by every UV seam. Confirmed
directly: the round-6 pilot's rigged mesh's own automatic-weighting log reported "366 floating-
island components" on a mesh whose pre-export Blender-internal component count was only 4 -- the
weighting fallback chain's nearest-neighbour "floating island" repair pass (`common.
reweight_floating_mesh_islands`) was then filling in ~57% of the mesh's vertices with inconsistent,
non-heat-diffused weights, which is what a flat-shaded before/after close-up (round 6's and this
round's) shows as visible self-intersecting tears at the hip: spatially-coincident UV-seam-split
patches landing on opposite sides of a weight discontinuity, invisible at bind pose (they're still
coincident at rest) but visibly torn the instant the joint actually rotates. **No amount of weight-
gradient tuning downstream can fix geometry that is not actually one connected piece.**

The fix is this stage: `common.retopologize_to_single_shell` (voxel remesh, tuned to land the
pre-quad-retopo mesh in a ~10-14k-tri band while auto-tuning voxel size per this mesh's own
bounding-box scale, so it isn't a hardcoded absolute size), `common.smooth_relax` (softens voxel
stair-stepping before quad retopology), `common.quadriflow_retopo` (QuadriFlow to ~4k quads / ~8k
tris, clean edge flow), `common.shrinkwrap_onto` (projects back onto the original surface to
recover true surface position/detail lost to voxel reconstruction), then `common.bake_base_color`
(Smart UV Project + a Cycles selected-to-active Diffuse bake from the original textured mesh onto
the new mesh's own 1K UVs -- far fewer UV islands than Meshy's original layout, so even residual
glTF export-time seam-splitting has much less surface area left to fragment). The old weld-then-
Collapse-decimate path is removed entirely, not kept as a fallback: Collapse decimation is UNSAFE
on this mesh's thin/complex features regardless of how clean the pre-decimate component count looks
(Spike #55's own prior finding -- Collapse shattering a 703-component mesh into shards -- turns out
to generalise to "looks clean at 4 components, still shatters thin features during the Collapse
operation itself," which is a stronger and more dangerous failure mode than the FRAGMENT_GUARD_RATIO
check this file used to gate on was ever able to catch).

What this stage deliberately does NOT do, and why:
  - Splitting wings into separate shrink-wrapped shells parented to wing bones: tried first with a
    single voxel size for the whole mesh (see `retopologize_to_single_shell`'s docstring on why a
    coarser voxel size doesn't monotonically reduce component count -- thin features pinch into
    tiny stray islands rather than vanishing cleanly), which worked well enough once tuned (verified
    below: 1 component, 0 non-manifold edges, wing silhouette/feather read clearly in the before/
    after turnaround) that the added pipeline complexity of multi-shell export + per-shell armature
    parenting wasn't worth it for this mesh. Worth revisiting per-creature if a future mesh's wings
    don't resolve as cleanly.
  - Scripted joint edge-loop reinforcement: still skipped, same reasoning as before (no scripted
    per-joint loop-cut signal without manual picking or a curvature heuristic this pilot's time
    budget didn't build) -- QuadriFlow's own edge flow is a meaningfully better substitute than the
    old Collapse-decimate's density-gradient-preservation-only approach, since QuadriFlow actively
    tries to align quads with surface curvature.
  - Separating rigid parts (beak/talons): still not attempted, same reasoning as before (no
    existing part-boundary signal on this mesh).

Run headless:
  blender -b --python prep_mesh.py -- --glb INPUT.glb --out OUTDIR [--target-tris 8000]
                                        [--texture-size 1024]
"""
import bpy
import sys
import os
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

args = common.parse_args(common.get_argv())
GLB = args["glb"]
OUT = args["out"]
TARGET_TRIS = int(args.get("target-tris", 8000))
TEXTURE_SIZE = int(args.get("texture-size", 1024))
os.makedirs(OUT, exist_ok=True)

common.fresh_scene()
obj, new_images = common.import_glb(GLB)
obj.name = "Griffin"
bpy.context.view_layer.objects.active = obj

report = {"input": GLB}

before_stats = common.topology_stats(obj)
report["before"] = before_stats
print("BEFORE:", json.dumps(before_stats))

# Weld duplicate-position verts left by the glTF export/import round-trip at every UV/normal seam.
v0, v1 = common.weld_mesh(obj, threshold=1e-4)
report["weld"] = {"verts_before": v0, "verts_after": v1}
print(f"WELD: {v0} -> {v1} verts")

welded_stats = common.topology_stats(obj)
report["after_weld"] = welded_stats
print("AFTER WELD:", json.dumps(welded_stats))

# Keep an untouched copy of the welded mesh around: it's both the shrinkwrap TARGET (true surface
# position/detail to project the retopologized mesh back onto) and the bake SOURCE (still has the
# original UVs + textured material) for the rest of this stage.
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.object.duplicate()
retopo_obj = bpy.context.view_layer.objects.active
retopo_obj.name = "GriffinRetopo"

# --- Retopology: voxel remesh (auto-tuned, single shell) -> smooth -> QuadriFlow -> shrinkwrap ---
retopo_obj, retopo_diag = common.retopologize_to_single_shell(
    retopo_obj, target_tri_band=(10000, 14000))
report["retopo_voxel"] = retopo_diag
print(f"RETOPO VOXEL: converged={retopo_diag['converged']}")
for t in retopo_diag["tries"]:
    print(f"  try: {t}")

common.smooth_relax(retopo_obj)
common.quadriflow_retopo(retopo_obj, target_faces=4000)
post_quadriflow_stats = common.topology_stats(retopo_obj)
report["after_quadriflow"] = post_quadriflow_stats
print("AFTER QUADRIFLOW:", json.dumps(post_quadriflow_stats))

common.shrinkwrap_onto(retopo_obj, obj)
post_shrinkwrap_stats = common.topology_stats(retopo_obj)
report["after_shrinkwrap"] = post_shrinkwrap_stats
print("AFTER SHRINKWRAP:", json.dumps(post_shrinkwrap_stats))

# Final budget trim if QuadriFlow's target_faces overshot TARGET_TRIS -- safe here (unlike the old
# pre-retopology Collapse call this replaces) because retopo_obj is, by construction, a single
# connected, manifold, evenly-quadded mesh at this point.
decimated = False
if post_shrinkwrap_stats["tri_equivalent"] > TARGET_TRIS:
    before_tris, after_tris = common.decimate_to_tris(retopo_obj, TARGET_TRIS)
    report["decimate"] = {"before_tris": before_tris, "after_tris": after_tris, "skipped": False}
    print(f"DECIMATE: {before_tris} -> {after_tris} tris")
    decimated = True
else:
    report["decimate"] = {"skipped": True, "reason": "already under target"}
    print(f"DECIMATE SKIPPED: already under {TARGET_TRIS} tris")

after_stats = common.topology_stats(retopo_obj)
report["after"] = after_stats
print("AFTER:", json.dumps(after_stats))

# Non-manifold / inspection summary (printed either way; this is the "Report before/after stats"
# deliverable the task brief asks for).
print(f"NON-MANIFOLD EDGES: {after_stats['non_manifold_edges']} "
      f"(boundary edges: {after_stats['boundary_edges']})")
print(f"COMPONENTS: {after_stats['components']} "
      f"(largest: {after_stats['largest_component']} / {after_stats['verts']} verts)")

# Base-colour texture: Smart-UV-Project the new mesh, then Cycles-bake the ORIGINAL textured mesh's
# diffuse colour onto it (selected-to-active) at 1K, replacing the old "downsize the existing
# texture" step entirely -- the new mesh has its own new UV layout, so the old texture's UVs no
# longer apply.
bake_img = common.bake_base_color(retopo_obj, obj, image_size=TEXTURE_SIZE)
tex_path = os.path.join(OUT, "base_color_1k.png")
bake_img.filepath_raw = tex_path
bake_img.file_format = "PNG"
bake_img.save()
tex_image = bpy.data.images.load(tex_path)
for mat in retopo_obj.data.materials:
    if mat is None or not mat.use_nodes:
        continue
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE":
            node.image = tex_image
report["texture"] = {"size": list(tex_image.size), "baked": True}
print(f"TEXTURE (baked): {tuple(tex_image.size)}")

# Only export the retopologized mesh -- the original (now just a shrinkwrap target / bake source)
# is cleaned up first so it doesn't end up in the output GLB alongside it.
bpy.data.objects.remove(obj, do_unlink=True)
retopo_obj.name = "Griffin"

out_glb = os.path.join(OUT, "griffin_prepped.glb")
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=out_glb,
    export_format="GLB",
    use_selection=False,
    export_animations=False,
    export_skins=False,
    export_materials="EXPORT",
    export_image_format="AUTO",
)
report["output"] = out_glb
report["output_bytes"] = os.path.getsize(out_glb)
print(f"EXPORTED {out_glb}: {report['output_bytes']} bytes")

with open(os.path.join(OUT, "prep_mesh_report.json"), "w") as f:
    json.dump(report, f, indent=2)

print("PREP_MESH DONE")
