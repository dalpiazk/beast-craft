"""Tooling/Animation stage 5: export.

Merges the three clips (Move from anim/gait.py's output, Idle+Attack from anim/keyed.py's output)
onto one armature+mesh and exports a single skinned, animated GLB with deform-only bones, one glTF
animation per clip. Per the methodology doc's glTF export guidance, every clip is pushed onto its
own NLA track before export (`export_animation_mode='NLA_TRACKS'`) -- the reliable way to get the
Blender glTF exporter to emit more than one named animation from one armature in one file.

Run headless:
  blender -b --python export_glb.py -- --move MOVE.blend --keyed KEYED.blend --out OUTDIR
                                         [--name griffin_anim.glb]
"""
import bpy
import sys
import os
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

args = common.parse_args(common.get_argv())
MOVE_BLEND = args["move"]
KEYED_BLEND = args["keyed"]
OUT = args["out"]
NAME = args.get("name", "griffin_anim.glb")
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=MOVE_BLEND)
arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
move_action = bpy.data.actions.get("Move")
if move_action is None:
    raise SystemExit("export_glb.py: no 'Move' action found in --move blend")

# Append Idle + Attack actions from the keyed .blend (same bone names/armature structure, since
# both files trace back to the same rig_creature.py output -- action data keys purely on bone name
# strings, so it transfers cleanly onto this session's armature).
with bpy.data.libraries.load(KEYED_BLEND, link=False) as (data_from, data_to):
    wanted = [n for n in data_from.actions if n in ("Idle", "Attack")]
    data_to.actions = wanted
idle_action = bpy.data.actions.get("Idle")
attack_action = bpy.data.actions.get("Attack")
missing = [n for n, a in (("Idle", idle_action), ("Attack", attack_action)) if a is None]
if missing:
    raise SystemExit(f"export_glb.py: missing actions from --keyed blend: {missing}")

# Push every clip onto its own NLA track (methodology doc's glTF export guidance -- the Blender
# exporter reliably emits multiple named animations this way; relying on whichever action happens
# to be "active" does not).
arm_obj.animation_data.action = None
for action in (idle_action, move_action, attack_action):
    track = arm_obj.animation_data.nla_tracks.new()
    track.name = action.name
    strip = track.strips.new(action.name, int(action.frame_range[0]), action)
    strip.action_frame_start = action.frame_range[0]
    strip.action_frame_end = action.frame_range[1]
print(f"NLA TRACKS: {[t.name for t in arm_obj.animation_data.nla_tracks]}")

out_path = os.path.join(OUT, NAME)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=out_path,
    export_format="GLB",
    use_selection=False,
    export_animations=True,
    export_animation_mode="NLA_TRACKS",
    export_bake_animation=False,
    export_skins=True,
    export_morph=False,
    export_materials="EXPORT",
    export_image_format="JPEG",
    export_jpeg_quality=90,
    export_apply=False,  # armature modifier must stay un-applied for skinning export
)
size_bytes = os.path.getsize(out_path)
print(f"EXPORTED {out_path}: {size_bytes} bytes ({size_bytes / (1024*1024):.3f} MiB)")

report = {
    "output": out_path,
    "output_bytes": size_bytes,
    "output_mib": size_bytes / (1024 * 1024),
    "under_2mib_budget": size_bytes < 2 * 1024 * 1024,
    "clips": ["Idle", "Move", "Attack"],
    "bone_count": len(arm_obj.data.bones),
    "tri_count": sum(len(p.vertices) - 2 for p in mesh_obj.data.polygons),
    "vert_count": len(mesh_obj.data.vertices),
}
with open(os.path.join(OUT, "export_report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("EXPORT_GLB DONE:", json.dumps(report))
