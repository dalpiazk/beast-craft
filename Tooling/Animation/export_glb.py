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
import glb_gate
import mathutils

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

# v18 round 5 (producer-caught: in-engine walks showed NO leg motion, Griffin included): capture the
# Blender-evaluated TRUTH pose of every clip BEFORE any rotation-mode change below, so the exported
# GLB can be checked against what the clips actually look like in Blender (glb_gate.py). Move is
# sampled here with gait.py's own rotation modes (QUATERNION legs); the keyed clips are sampled
# after they're appended, under XYZ (their authored mode -- keyed.py sets every bone XYZ).
SCENE = bpy.context.scene
FPS = SCENE.render.fps
if arm_obj.animation_data is None:
    arm_obj.animation_data_create()


def _m(mat):
    return [list(r) for r in mat]


def capture_truth(action, step=2):
    # Bones a clip doesn't key play at REST in glTF/any runtime -- reset the pose first so the truth
    # matches those semantics (a leftover saved pose would otherwise leak into the comparison).
    for pb in arm_obj.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    arm_obj.animation_data.action = action
    f0, f1 = int(round(action.frame_range[0])), int(round(action.frame_range[1]))
    frames = sorted(set(list(range(f0, f1 + 1, step)) + [f1]))
    out = {}
    for f in frames:
        SCENE.frame_set(f)
        out[f] = {pb.name: _m(arm_obj.matrix_world @ pb.matrix) for pb in arm_obj.pose.bones}
    return {"frame_start": f0, "frames": out}


TRUTH = {"rest": {b.name: _m(arm_obj.matrix_world @ b.matrix_local) for b in arm_obj.data.bones},
         "clips": {"Move": capture_truth(move_action)}}
# v19 (birds): per-creature facts stored on the armature by rig_templates/winged_biped.py (absent
# on every other rig, which keeps its exact previous behaviour): the flier gate mode, the engine
# hover offset and extra outline-mask joints, both forwarded to the events sidecar below.
MOVE_MODE = {"hover": "hover", "slither": "slither", "hop": "hop"}.get(arm_obj.data.get("locomotion"), "walk")
# v21: the Swarmling's merged-swarm joint budget (absent on every other rig -> no check)
MAX_JOINTS = int(arm_obj.data["max_joints"]) if "max_joints" in arm_obj.data else None
HOVER_OFFSET = float(arm_obj.data.get("hover_offset", 0.0))
OUTLINE_MASK_ZERO = list(arm_obj.data.get("outline_mask_zero", []))
TRUTH["move_mode"] = MOVE_MODE
if MAX_JOINTS is not None:
    TRUTH["max_joints"] = MAX_JOINTS


def quaternion_fcurves_to_euler(action):
    """v18 round 5 ROOT-CAUSE FIX. The round-16 normalisation below forces every pose bone to
    XYZ-Euler so the keyed clips' Euler leg keys apply -- but anim/gait.py keys the leg/scapula/toe
    bones as rotation_QUATERNION, and in XYZ mode Blender silently ignores quaternion fcurves: every
    GLB since round 16 (Griffin included) exported Move with 2-key CONSTANT leg rotations (measured
    from the GLB itself: 0.0 deg range on every leg bone). Here every bone's quaternion fcurves are
    re-sampled on every integer frame into equivalent rotation_euler (XYZ, continuity-preserving)
    LINEAR keys and the quaternion fcurves removed, so the XYZ normalisation is lossless for every
    clip. Returns the number of bones converted."""
    converted = 0
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if not cb:
                    continue
                by_path = {}
                for fc in list(cb.fcurves):
                    if fc.data_path.endswith(".rotation_quaternion"):
                        by_path.setdefault(fc.data_path, {})[fc.array_index] = fc
                for qpath, fcs in by_path.items():
                    keys = sorted({int(round(k.co[0])) for fc in fcs.values() for k in fc.keyframe_points})
                    if not keys:
                        continue
                    vals, prev = [], None
                    for f in range(keys[0], keys[-1] + 1):
                        q = mathutils.Quaternion([fcs[i].evaluate(f) if i in fcs else (1.0 if i == 0 else 0.0)
                                                  for i in range(4)]).normalized()
                        e = q.to_euler("XYZ", prev) if prev is not None else q.to_euler("XYZ")
                        prev = e
                        vals.append((f, e.copy()))
                    epath = qpath.replace("rotation_quaternion", "rotation_euler")
                    group = next((fc.group.name for fc in fcs.values() if fc.group), "")
                    for fc in list(cb.fcurves):
                        if fc.data_path == epath:
                            cb.fcurves.remove(fc)
                    for i in range(3):
                        nf = cb.fcurves.new(epath, index=i, group_name=group)
                        nf.keyframe_points.add(len(vals))
                        for k, (f, e) in enumerate(vals):
                            kp = nf.keyframe_points[k]
                            kp.co = (f, e[i])
                            kp.interpolation = "LINEAR"
                        nf.update()
                    for fc in fcs.values():
                        cb.fcurves.remove(fc)
                    converted += 1
    return converted


# Round 16 (producer review -- a Cast leg-pose edit that was correctly authored in keyed.py never
# showed up in the exported GLB, root-caused here, not assumed): gait.py's own leg-posing code
# (direct world-matrix/IK work -- see anim/gait.py) leaves this armature's LEG pose bones in
# QUATERNION rotation_mode, while keyed.py's hand-keyed clips (Idle/Attack/Cast/Hit/KO/Victory) are
# ALL authored by setting `rotation_euler` under XYZ mode (keyed.py sets `rotation_mode = "XYZ"` on
# ITS OWN armature at its own script start -- but that's a property of the POSE BONE on a SPECIFIC
# ARMATURE OBJECT, not something that travels with an Action when the action alone is appended into
# a DIFFERENT file's armature, as export_glb.py does below). A pose bone's `rotation_euler` property
# always stores whatever value was last written to it (confirmed directly: reading it back after
# assigning the appended Cast action gave the exact edited values) -- but Blender only uses that
# property to compute the bone's actual transform when `rotation_mode` is an Euler order; in
# QUATERNION mode the (untouched, still-identity) `rotation_quaternion` property drives the pose
# instead, silently discarding every Euler keyframe. Confirmed via a direct check: THIS armature's
# leg_FL_thigh/leg_FR_thigh/leg_BR_thigh pose bones (loaded fresh from --move, before any append)
# were QUATERNION while spine_02/head/wing_L_01 were XYZ -- exactly matching which bones' keyed-clip
# pose changes silently failed to export (leg bones) versus which ones worked (everything else, which
# is why Attack/Hit/KO/Victory's spine/head/wing motion has always read correctly on screen despite
# this). Normalise every pose bone to XYZ here, matching keyed.py's own convention, before any keyed
# action is appended or pushed onto an NLA strip, so every clip's leg-bone keyframes actually apply.
for pb in arm_obj.pose.bones:
    pb.rotation_mode = "XYZ"
_n_conv = quaternion_fcurves_to_euler(move_action)
print(f"QUATERNION->EULER: {_n_conv} bones' Move rotation fcurves converted (round-5 fix)")

# Append Idle/Attack/Cast/Hit/KO/Victory actions from the keyed .blend (same bone names/armature
# structure, since both files trace back to the same rig_creature.py output -- action data keys
# purely on bone name strings, so it transfers cleanly onto this session's armature). Round 15:
# four new battle clips (Cast/Hit/KO/Victory) joined Idle/Attack in anim/keyed.py's single output
# blend -- see that script's own docstring for why they're all built the same FK-pose-to-pose way.
KEYED_CLIP_NAMES = ("Idle", "Attack", "Cast", "Hit", "KO", "Victory")
with bpy.data.libraries.load(KEYED_BLEND, link=False) as (data_from, data_to):
    wanted = [n for n in data_from.actions if n in KEYED_CLIP_NAMES]
    data_to.actions = wanted
keyed_actions = {name: bpy.data.actions.get(name) for name in KEYED_CLIP_NAMES}
missing = [n for n, a in keyed_actions.items() if a is None]
if missing:
    raise SystemExit(f"export_glb.py: missing actions from --keyed blend: {missing}")
for _name, _act in keyed_actions.items():
    quaternion_fcurves_to_euler(_act)  # no-op for keyed.py's all-Euler clips; guards a future one
    TRUTH["clips"][_name] = capture_truth(_act)

# Push every clip onto its own NLA track (methodology doc's glTF export guidance -- the Blender
# exporter reliably emits multiple named animations this way; relying on whichever action happens
# to be "active" does not).
arm_obj.animation_data.action = None
all_actions = [keyed_actions["Idle"], move_action, keyed_actions["Attack"],
               keyed_actions["Cast"], keyed_actions["Hit"], keyed_actions["KO"],
               keyed_actions["Victory"]]
for action in all_actions:
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

# Round 15: event markers. keyed.py writes `keyed_event_markers.json` next to its own
# griffin_keyed.blend output -- copy/re-key it into a sidecar next to the exported GLB (glTF extras
# would need custom exporter plumbing this pipeline doesn't have; a sidecar is the simpler of the two
# options the task brief explicitly allows). Live3D reads this file at startup and logs a line
# whenever its pilot-sequence/live playback crosses a marker's frame for the matching clip -- see
# BeastInstance.cs/Game1.cs.
#
# Round 16 (producer review -- KO's last contact-sheet frame snapping back to a neutral pose, root-
# caused to the sampler unconditionally wrapping t==duration back to 0): keyed.py's own
# `keyed_event_markers.json` now carries a sibling "loop" map (clip name -> bool) alongside its
# existing "markers" map -- {"markers": {...}, "loop": {"Idle": true, ..., "KO": false, ...}} -- so
# this sidecar can forward BOTH, and Live3D's sampler (AnimatedPose.ComputeWorldMatricesBlended) can
# tell a seamless loop (Idle/Move/Victory, wrap) from a one-shot action (Attack/Hit/Cast/KO, clamp to
# the last key) per clip instead of always wrapping.
markers_path = os.path.join(os.path.dirname(KEYED_BLEND), "keyed_event_markers.json")
event_markers = {}
clip_loop = {}
if os.path.isfile(markers_path):
    with open(markers_path) as f:
        markers_sidecar_src = json.load(f)
    event_markers = markers_sidecar_src.get("markers", {})
    clip_loop = markers_sidecar_src.get("loop", {})
    sidecar_path = os.path.splitext(out_path)[0] + "_events.json"
    sidecar = {"fps": 24, "markers": event_markers, "loop": clip_loop}
    if HOVER_OFFSET > 0:
        # v19: Live3D lifts the model by `offset` (glTF units) in every clip except those listed
        # (KO bakes its own fall from that height to the ground), blended across crossfades.
        sidecar["hover"] = {"offset": HOVER_OFFSET, "exempt": ["KO"]}
    if OUTLINE_MASK_ZERO:
        sidecar["outline_mask_zero"] = OUTLINE_MASK_ZERO
    with open(sidecar_path, "w") as f:
        json.dump(sidecar, f, indent=2)
    print(f"EVENT MARKERS SIDECAR: {sidecar_path} -- markers={json.dumps(event_markers)} "
          f"loop={json.dumps(clip_loop)}")
else:
    print(f"EVENT MARKERS: no {markers_path} found, skipping sidecar (no markers this export)")

# v18 round 5: hard gate on the GLB itself -- see glb_gate.py. Fails the export loudly.
with open(os.path.join(OUT, "glb_gate_truth.json"), "w") as f:
    json.dump({"fps": FPS, **TRUTH}, f)  # lets `python glb_gate.py GLB TRUTH` re-run the gate standalone
gate_pass, gate_report = glb_gate.run_gate(out_path, TRUTH, FPS, move_mode=MOVE_MODE, max_joints=MAX_JOINTS)
if "joints" in gate_report:
    print(f"GLB GATE JOINTS: {gate_report['joints']['count']} (max {gate_report['joints']['max']})")
for _clip, _h in gate_report.get("hop", {}).items():
    print(f"GLB GATE HOP {_clip}: {_h}")
for _clip, _h in gate_report.get("string", {}).items():  # v22 round 3 (the Archer's bow string)
    print(f"GLB GATE STRING {_clip}: nock travel {_h}")
with open(os.path.join(OUT, "glb_gate_report.json"), "w") as f:
    json.dump(gate_report, f, indent=2)
for _clip, _r in gate_report["clips"].items():
    print(f"GLB GATE {_clip}: max_rot_err={_r['max_rot_err_deg']} deg max_pos_err={_r['max_pos_err']} "
          f"leg ranges glb/blender={_r['leg_range_glb_vs_blender_deg']}")
if not gate_pass:
    print("GLB GATE FAILED:\n  " + "\n  ".join(gate_report["failures"]))
    raise SystemExit("export_glb.py: GLB gate FAILED -- the exported GLB does not reproduce the "
                     "Blender-evaluated pose (see glb_gate_report.json)")
print("GLB GATE PASSED")

report = {
    "glb_gate": {"pass": gate_pass, "failures": gate_report["failures"]},
    "output": out_path,
    "output_bytes": size_bytes,
    "output_mib": size_bytes / (1024 * 1024),
    "under_2mib_budget": size_bytes < 2 * 1024 * 1024,
    "clips": ["Idle", "Move", "Attack", "Cast", "Hit", "KO", "Victory"],
    "bone_count": len(arm_obj.data.bones),
    "tri_count": sum(len(p.vertices) - 2 for p in mesh_obj.data.polygons),
    "vert_count": len(mesh_obj.data.vertices),
    "event_markers": event_markers,
    "clip_loop": clip_loop,
}
with open(os.path.join(OUT, "export_report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("EXPORT_GLB DONE:", json.dumps(report))
