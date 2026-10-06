"""Hashes every Action's baked fcurve keyframe data in a .blend (deterministic fingerprint), for
before/after comparison. Run: blender -b FILE.blend --python hash_actions.py -- --out OUT.json
"""
import bpy, sys, os, json, hashlib

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = None
for i, a in enumerate(argv):
    if a == "--out":
        out_path = argv[i + 1]


def iter_action_fcurves(action):
    # Blender 5.x layered-Action redesign: fcurves live under layers[].strips[].channelbag(slot).
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if not cb:
                    continue
                for fcu in cb.fcurves:
                    yield fcu


result = {}
for action in bpy.data.actions:
    curves = []
    for fc in iter_action_fcurves(action):
        pts = [(round(kp.co[0], 5), round(kp.co[1], 6)) for kp in fc.keyframe_points]
        curves.append({"data_path": fc.data_path, "array_index": fc.array_index, "points": pts})
    curves.sort(key=lambda c: (c["data_path"], c["array_index"]))
    blob = json.dumps(curves)
    result[action.name] = {
        "fcurve_count": len(curves),
        "keyframe_total": sum(len(c["points"]) for c in curves),
        "hash": hashlib.sha256(blob.encode()).hexdigest(),
    }

with open(out_path, "w") as f:
    json.dump(result, f, indent=2)
print("HASH_ACTIONS DONE", json.dumps({k: v["hash"][:12] for k, v in result.items()}))
