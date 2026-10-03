"""Tooling/Animation stage 1: mesh prep.

Imports a Meshy quad-remesh GLB, reports before/after topology stats, welds duplicate-position
vertices (every UV/normal seam leaves coincident duplicates on glTF export/import -- see
Tooling/Spike55/blender_lowpoly_render.py's docstring for the full story), decimates to the
triangle budget if the remesh came in over it, downsizes the base-colour texture to 1K, and
exports a prepped GLB for rig_creature.py to pick up.

What this stage deliberately does NOT do, and why:
  - Scripted joint edge-loop reinforcement (extra loops at shoulders/hips/neck/wing-tail roots):
    skipped for this pilot. The input quad remesh is already a fairly even, moderate-density quad
    field (see the printed before-stats); targeted loop insertion at specific joints needs either
    (a) a per-creature manual loop-cut selection (not scriptable without picking edges by eye) or
    (b) a generic curvature-based heuristic (e.g. insert loops where mean curvature is highest).
    Given the single-pilot time budget, this pipeline instead leans on Decimate (Collapse), which
    preserves relative density gradients from the source remesh, plus the weight-cleanup pass in
    rig_creature.py (vertex_group_smooth) to control any resulting creasing. If the weight-check
    render sheet (rig_creature.py's output) shows candy-wrapping at a specific joint, the fix
    belongs here as a follow-up: a targeted `bpy.ops.mesh.bisect` loop cut at that joint's bone
    plane before decimation.
  - Separating rigid parts (beak/talons) into unweighted attachments: the input GLB is a single
    fused mesh with one material and no existing part boundaries (seams, vertex groups, or
    separate material slots) to split on, so there is no reliable scripted signal for where a
    "rigid part" boundary is on this particular mesh. Noted as a known limitation, not attempted
    rather than faked with a guessed boundary.

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

# Decimate to budget if needed. Only safe to Collapse-decimate a mesh that's (close to) a single
# connected component -- Spike #55 found Collapse shattering a 703-component mesh into shards.
# Guard: if the welded mesh is still badly fragmented, skip decimation and report it loudly rather
# than silently producing a shattered asset.
FRAGMENT_GUARD_RATIO = 0.8  # largest component must hold at least this fraction of all verts
frag_ratio = welded_stats["largest_component"] / max(welded_stats["verts"], 1)
decimated = False
if welded_stats["tri_equivalent"] > TARGET_TRIS:
    if frag_ratio >= FRAGMENT_GUARD_RATIO:
        before_tris, after_tris = common.decimate_to_tris(obj, TARGET_TRIS)
        report["decimate"] = {"before_tris": before_tris, "after_tris": after_tris, "skipped": False}
        print(f"DECIMATE: {before_tris} -> {after_tris} tris")
        decimated = True
    else:
        report["decimate"] = {"skipped": True, "reason": f"fragmented mesh, largest component only "
                               f"{frag_ratio:.1%} of verts -- Collapse decimation would shatter it"}
        print(f"DECIMATE SKIPPED: fragmented mesh ({frag_ratio:.1%} largest component)")
else:
    report["decimate"] = {"skipped": True, "reason": "already under target"}
    print(f"DECIMATE SKIPPED: already under {TARGET_TRIS} tris")

after_stats = common.topology_stats(obj)
report["after"] = after_stats
print("AFTER:", json.dumps(after_stats))

# Non-manifold / inspection summary (printed either way; this is the "Report before/after stats"
# deliverable the task brief asks for).
print(f"NON-MANIFOLD EDGES: {after_stats['non_manifold_edges']} "
      f"(boundary edges: {after_stats['boundary_edges']})")
print(f"COMPONENTS: {after_stats['components']} "
      f"(largest: {after_stats['largest_component']} / {after_stats['verts']} verts)")

# Base-colour texture: downsize to the 1K cap for a hero/mobile budget.
tex_image = common.pick_base_color_image(obj, set())  # obj.data.materials already has it linked
tex_image = common.downsize_image(tex_image, TEXTURE_SIZE, OUT, "base_color_1k.png")
report["texture"] = {"size": list(tex_image.size) if tex_image else None}
print(f"TEXTURE: {tuple(tex_image.size) if tex_image else 'none'}")

# Re-point the material's image node at the (possibly reloaded) downsized image so the export
# below carries the 1K texture, not the original 2048.
for mat in obj.data.materials:
    if mat is None or not mat.use_nodes:
        continue
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE":
            node.image = tex_image

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
