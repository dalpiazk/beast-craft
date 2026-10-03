"""Tooling/Animation stage 2: rig.

Imports the prepped mesh (prep_mesh.py's output), normalises it to the shared 2.0-unit-height
convention (Tooling/Spike55's convention, kept for consistency with the Live3D runtime and hex
board math), detects landmarks from the mesh geometry itself (rig_templates/winged_quadruped.py),
builds a deform-only skeleton from the reusable template, weights it (automatic weights with the
Spike #55 fallback chain: heat weights on raw mesh -> heat weights on a voxel-remeshed duplicate,
data-transferred back -> envelope weights), runs the scripted weight-paint cleanup pass (limit
influences, normalise, clean, smooth), and renders a bone-overlay + a weight-check sheet (several
key poses so creasing/candy-wrapping/collapsing is visible in a static image, since this agent
can't watch a live viewport).

Why a custom scripted rig, not Rigify (the task brief's first-choice option):
Rigify's Wolf/Bird metarig templates are fitted to a target mesh primarily by dragging metarig
bones in the 3D viewport until they line up with the character -- an interactive, visual task with
no scripted equivalent for fitting an *arbitrary* mesh's actual proportions (the metarig itself has
no "fit to this mesh's bounds" operator). Combining a Wolf metarig's legs/spine with a Bird
metarig's wing chain into one creature additionally requires manual metarig surgery (reparenting
bone chains between two separate generated metarig objects) before "Generate Rig" can be run --
again a hand-editing task, not a single scriptable operator. Given this agent cannot see a live
3D viewport to verify an interactive fit, attempting Rigify headless risks silently producing a
misfitted or broken metarig with no way to catch it except by the same rendered-overlay technique
this script already uses for the custom rig -- at which point the custom rig is strictly safer: it
places every bone from numeric landmarks derived directly from the mesh (detect_landmarks), is
fully deterministic, and is verified the same way (rendered overlay) with no extra GUI-shaped step
in between. This matches the task brief's explicitly sanctioned fallback ("equivalent clean custom
deform rig if Rigify isn't usable headless -- document why").

IK is implemented as closed-form analytic 2-bone IK directly in Python (anim/gait.py), not as
Blender bone constraints: the final glTF export must be pure baked FK keyframes regardless (glTF
has no IK solver), so driving the rig with a Blender IK constraint and then baking it to FK would
add a constraint-setup/bake-and-verify step that produces exactly the same shipped result as
computing the joint angles analytically and keyframing them directly. The analytic approach is
what the methodology doc's section 3b itself recommends ("2-bone IK, closed-form, trivial to
implement without a general IK solver").

Run headless:
  blender -b --python rig_creature.py -- --glb PREPPED.glb --out OUTDIR [--target-height 2.0]
"""
import bpy
import bmesh
import sys
import os
import json
import math
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "rig_templates"))
import common
import winged_quadruped as template

args = common.parse_args(common.get_argv())
GLB = args["glb"]
OUT = args["out"]
TARGET_HEIGHT = float(args.get("target-height", 2.0))
os.makedirs(OUT, exist_ok=True)

common.fresh_scene()
obj, new_images = common.import_glb(GLB)
obj.name = "Griffin"
bpy.context.view_layer.objects.active = obj
H = common.normalise_transform(obj, TARGET_HEIGHT)
print(f"NORMALISED to height {H}")

report = {"input": GLB, "target_height": H}

# --- Landmarks + skeleton ---------------------------------------------------
lm = template.detect_landmarks(obj, H)
report["landmarks"] = {
    "forward_sign": lm["forward_sign"],
    "leg_count": len(lm["legs"]),
    "legs": [{"side": l["side"], "foot": list(l["foot"])} for l in lm["legs"]],
    "pelvis": list(lm["pelvis"]),
    "chest": list(lm["chest"]),
    "head_base": list(lm["head_base"]),
    "head_tip": list(lm["head_tip"]),
    "wing_l_tip": list(lm["wing_l_tip"]),
    "wing_r_tip": list(lm["wing_r_tip"]),
    "tail_tip": list(lm["tail_tip"]),
}
print("LANDMARKS:", json.dumps(report["landmarks"]))

arm_data = bpy.data.armatures.new("GriffinRig")
arm_obj = bpy.data.objects.new("GriffinRig", arm_data)
bpy.context.collection.objects.link(arm_obj)
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="EDIT")
bone_names, bone_roles = template.build_bones(arm_data.edit_bones, lm, H)
bpy.ops.object.mode_set(mode="OBJECT")
report["bone_count"] = len(bone_names)
report["bone_names"] = bone_names
report["bone_roles"] = bone_roles
print(f"SKELETON: {len(bone_names)} deform bones")

# --- Weighting ---------------------------------------------------------------
method_used, weight_log = common.auto_weight_with_fallbacks(obj, arm_obj, bone_names, H)
report["weighting"] = {"method": method_used, "log": weight_log}
for line in weight_log:
    print("WEIGHT:", line)
if method_used is None:
    raise SystemExit("rig_creature.py: auto-weighting failed on all three methods -- refusing to "
                      "proceed with an unrigged mesh.")

common.cleanup_weights(obj, limit=4)
worst, avg = common.max_influences_per_vertex(obj)
report["weighting"]["max_influences_after_cleanup"] = worst
report["weighting"]["avg_influences_after_cleanup"] = avg
print(f"INFLUENCES AFTER CLEANUP: max={worst} avg={avg:.2f}")
unweighted, total = common.count_unweighted(obj)
report["weighting"]["final_unweighted"] = unweighted
report["weighting"]["final_total"] = total
print(f"FINAL UNWEIGHTED: {unweighted}/{total}")

# ---------------------------------------------------------------------------
# Verification renders: (1) a bone-overlay on the bind-pose mesh (small spheres at each bone head,
# lines approximated by thin cylinders) so the landmark fit can be checked by eye; (2) a weight-
# check sheet across several key poses (legs bent, wings raised, neck turned, tail curled), looking
# for creasing/candy-wrapping/collapsing -- the task brief's explicit ask, since this agent can't
# watch a live viewport.
# ---------------------------------------------------------------------------
def build_overlay_markers():
    markers = []
    for b in arm_data.bones:
        head_ws = arm_obj.matrix_world @ b.head_local
        tail_ws = arm_obj.matrix_world @ b.tail_local
        markers.append((b.name, head_ws, tail_ws))
    return markers


def add_sphere(loc, radius, color, name):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=8, ring_count=5)
    sph = bpy.context.active_object
    sph.name = name
    mat = bpy.data.materials.new(name + "_mat")
    mat.diffuse_color = color
    mat.use_nodes = False
    sph.data.materials.append(mat)
    return sph


def add_cylinder_between(p0, p1, radius, color, name):
    mid = (p0 + p1) / 2.0
    vec = p1 - p0
    length = vec.length
    if length < 1e-6:
        return None
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=length, location=mid, vertices=6)
    cyl = bpy.context.active_object
    cyl.name = name
    z = mathutils.Vector((0, 0, 1))
    axis = z.cross(vec)
    if axis.length > 1e-6:
        angle = z.angle(vec)
        cyl.rotation_mode = "AXIS_ANGLE"
        cyl.rotation_axis_angle = (angle, axis.x, axis.y, axis.z)
    mat = bpy.data.materials.new(name + "_mat")
    mat.diffuse_color = color
    mat.use_nodes = False
    cyl.data.materials.append(mat)
    return cyl


def setup_render(res=600):
    light_data = bpy.data.lights.new("sun", type="SUN")
    light_data.energy = 3.0
    light_obj = bpy.data.objects.new("sun", light_data)
    bpy.context.collection.objects.link(light_obj)
    light_obj.rotation_euler = (0.9, 0.2, 0.6)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "MATCAP"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.film_transparent = True
    return scene


def render_ortho(scene, out_path, loc, rot, ortho_scale=2.6):
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_obj = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam_obj)
    cam_obj.location = loc
    cam_obj.rotation_euler = rot
    scene.camera = cam_obj
    scene.render.filepath = out_path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam_obj, do_unlink=True)


ROLE_COLOR = {
    "root": (1, 1, 1, 1), "pelvis": (1, 0.5, 0, 1), "spine": (1, 0.9, 0, 1),
    "neck": (0, 1, 0.4, 1), "head": (1, 0, 0, 1), "tail": (0.6, 0.2, 1, 1),
}

scene = setup_render(600)

overlay_objs = []
markers = build_overlay_markers()
for name, head_ws, tail_ws in markers:
    role = bone_roles.get(name, "")
    base_role = role.split("_")[0] if role.startswith(("wing", "leg")) else role
    if role.startswith("wing_L") or role == "leg_L":
        color = (0.1, 0.5, 1, 1)
    elif role.startswith("wing_R") or role == "leg_R":
        color = (1, 0.1, 0.6, 1)
    elif role.startswith("leg_"):
        color = (0.1, 0.8, 0.8, 1)
    else:
        color = ROLE_COLOR.get(role, (0.8, 0.8, 0.8, 1))
    s = add_sphere(head_ws, 0.018 * H, color, f"mk_{name}_h")
    overlay_objs.append(s)
    c = add_cylinder_between(head_ws, tail_ws, 0.008 * H, color, f"mk_{name}_c")
    if c:
        overlay_objs.append(c)

render_ortho(scene, os.path.join(OUT, "rig_overlay_front.png"), (0, -4, 1.0),
             (math.radians(90), 0, 0))
render_ortho(scene, os.path.join(OUT, "rig_overlay_side.png"), (4, 0, 1.0),
             (math.radians(90), 0, math.radians(90)))
print("RIG OVERLAY RENDERED")

for o in overlay_objs:
    bpy.data.objects.remove(o, do_unlink=True)

# --- Weight-check sheet: pose the armature into a few extreme test poses and render the skinned
# mesh (not an overlay) so creasing/candy-wrapping/collapsing is visible. ---------------------
scene.display.shading.color_type = "TEXTURE"


def set_pose_euler(bone, deg_x=0, deg_y=0, deg_z=0):
    pb = arm_obj.pose.bones.get(bone)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg_x), math.radians(deg_y), math.radians(deg_z))


def reset_pose():
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)


bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")

leg_sides = [l["side"] for l in lm["legs"]]
poses = {}
reset_pose()
poses["bind"] = "bind (rest) pose"

reset_pose()
for side in leg_sides:
    # Angles halved from an earlier pass: the rig template now gives legs a genuinely bent-knee
    # REST pose (lead-review fix round, see winged_quadruped.py's build_bones docstring), so the
    # same additional test-pose rotation on top of that already-bent rest reads as over-rotated/
    # stretched -- this is a smaller *additional* bend on top of the rest bend, not the total angle.
    set_pose_euler(f"leg_{side}_thigh", deg_x=-18)
    set_pose_euler(f"leg_{side}_shin", deg_x=28)
bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode="OBJECT")
render_ortho(scene, os.path.join(OUT, "weightcheck_legs_bent.png"), (3, -3, 1.0),
             (math.radians(75), 0, math.radians(40)), ortho_scale=2.8)
bpy.ops.object.mode_set(mode="POSE")

reset_pose()
set_pose_euler("wing_L_01", deg_z=-60)
set_pose_euler("wing_L_02", deg_z=-25)
set_pose_euler("wing_R_01", deg_z=60)
set_pose_euler("wing_R_02", deg_z=25)
bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode="OBJECT")
render_ortho(scene, os.path.join(OUT, "weightcheck_wings_raised.png"), (0, -4, 1.0),
             (math.radians(90), 0, 0), ortho_scale=2.8)
bpy.ops.object.mode_set(mode="POSE")

reset_pose()
set_pose_euler("neck_01", deg_z=30)
set_pose_euler("neck_02", deg_z=25)
set_pose_euler("head", deg_z=20)
bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode="OBJECT")
render_ortho(scene, os.path.join(OUT, "weightcheck_neck_turned.png"), (0, -4, 1.3),
             (math.radians(90), 0, 0), ortho_scale=1.6)
bpy.ops.object.mode_set(mode="POSE")

reset_pose()
set_pose_euler("tail_01", deg_x=30)
set_pose_euler("tail_02", deg_x=40)
set_pose_euler("tail_03", deg_x=45)
set_pose_euler("tail_04", deg_x=35)
bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode="OBJECT")
render_ortho(scene, os.path.join(OUT, "weightcheck_tail_curled.png"), (4, 0, 0.6),
             (math.radians(90), 0, math.radians(90)), ortho_scale=1.8)
bpy.ops.object.mode_set(mode="POSE")

reset_pose()
bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode="OBJECT")
print("WEIGHT-CHECK SHEET RENDERED (4 poses + bind)")

# --- Export the rigged (bind-pose) mesh+armature for the anim stage ---------------------------
out_glb = os.path.join(OUT, "griffin_rigged.glb")
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(
    filepath=out_glb,
    export_format="GLB",
    use_selection=False,
    export_animations=False,
    export_skins=True,
    export_materials="EXPORT",
    export_apply=False,
)
report["output"] = out_glb
report["output_bytes"] = os.path.getsize(out_glb)
print(f"EXPORTED {out_glb}: {report['output_bytes']} bytes")

# Keep a .blend for the next stages to build on directly (richer than re-importing the GLB: avoids
# re-running weighting), not committed -- large binary working file, scratchpad only.
blend_path = os.path.join(OUT, "griffin_rigged.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
report["blend"] = blend_path

with open(os.path.join(OUT, "rig_report.json"), "w") as f:
    json.dump(report, f, indent=2)

print("RIG_CREATURE DONE")
