"""Standalone Blender script: opens a rigged .blend and dumps a deterministic fingerprint of
bone head/tail positions (rest pose) and per-vertex weight assignments, for before/after
regression comparison. Run: blender -b <blend> --python hash_rig.py -- --out OUT.json
"""
import bpy, sys, os, json, hashlib

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = None
for i, a in enumerate(argv):
    if a == "--out":
        out_path = argv[i + 1]

arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
arm_data = arm_obj.data

bones = {}
for b in arm_data.bones:
    bones[b.name] = {
        "head": [round(c, 6) for c in b.head_local],
        "tail": [round(c, 6) for c in b.tail_local],
        "parent": b.parent.name if b.parent else None,
    }

me = mesh_obj.data
vgroup_names = [g.name for g in mesh_obj.vertex_groups]
weight_rows = []
for v in me.vertices:
    row = sorted([(vgroup_names[g.group], round(g.weight, 5)) for g in v.groups])
    weight_rows.append(row)

bone_blob = json.dumps(bones, sort_keys=True)
weight_blob = json.dumps(weight_rows, sort_keys=False)
result = {
    "bone_count": len(bones),
    "bone_names": sorted(bones.keys()),
    "bone_hash": hashlib.sha256(bone_blob.encode()).hexdigest(),
    "vertex_count": len(me.vertices),
    "weight_hash": hashlib.sha256(weight_blob.encode()).hexdigest(),
    "bones": bones,
}
with open(out_path, "w") as f:
    json.dump(result, f, indent=2)
print("HASH_RIG DONE", result["bone_hash"], result["weight_hash"])
