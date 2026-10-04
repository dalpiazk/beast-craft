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

# Round-7 mesh-prep lesson: glTF EXPORT re-splits a single welded vertex into several wherever it
# carries more than one UV/normal value across its surrounding faces (a format requirement -- see
# prep_mesh.py's docstring for the full story). That means even prep_mesh.py's new single-shell
# retopology round-trips back to a multi-component mesh the instant it's exported to GLB and
# re-imported here -- confirmed directly: the retopologized, single-component (1 component, 0
# non-manifold edges) mesh prep_mesh.py produces re-fragments to 152 components once exported and
# re-imported, the exact glTF-export-seam-splitting mechanism that produced round 6's 366-island
# weighting-fallback problem in the first place, just on a much cleaner starting mesh with far fewer
# UV islands (152 vs. 366). Welding immediately on import here -- before anything else, including
# the native-coordinate capture just below -- collapses those re-split-but-coincident vertices back
# to the true single-component topology for every downstream step (landmark snapping, weighting),
# the same threshold prep_mesh.py itself uses. This does not affect the landmark snapping below:
# HAND_LANDMARKS_NATIVE positions are independent of vertex count/indices, and welding merges
# coincident vertices without moving any geometry, so the mesh's bounding box (what
# _native_to_normalized_fn reads) is unchanged either way.
wv0, wv1 = common.weld_mesh(obj, threshold=1e-4)
print(f"WELD ON IMPORT: {wv0} -> {wv1} verts")
welded_rig_stats = common.topology_stats(obj)
print(f"WELD ON IMPORT COMPONENTS: {welded_rig_stats['components']} "
      f"(largest: {welded_rig_stats['largest_component']} / {welded_rig_stats['verts']} verts), "
      f"non-manifold edges: {welded_rig_stats['non_manifold_edges']}")

# Lead-review round 4: the hand-calibrated landmark path needs the native (pre-normalisation) ->
# normalised conversion captured BEFORE common.normalise_transform runs (it reads the mesh's own
# current bounding box; afterwards that box is already the normalised one). See
# rig_templates.winged_quadruped._native_to_normalized_fn's docstring.
to_normalized = template._native_to_normalized_fn(obj, TARGET_HEIGHT)

H = common.normalise_transform(obj, TARGET_HEIGHT)
print(f"NORMALISED to height {H}")

report = {"input": GLB, "target_height": H}

# --- Landmarks + skeleton ---------------------------------------------------
# Lead-review round 4: hand-placed landmarks (calibrated against scratchpad/anim-pilot/calib/
# {left,front,bottom}.png + calib.json by the animator) replace the horizontal-slicing detection
# for this mesh -- the slicing-detected rig placed hips/shoulders near ground level with the spine
# running near-vertically through the chest, producing a walk that barely moved and dark belly
# patches. `detect_landmarks` (horizontal slicing) is kept in winged_quadruped.py for history/
# reuse on a future creature; this mesh now uses `detect_landmarks_handplaced`.
lm = template.detect_landmarks_handplaced(obj, H, to_normalized)
report["landmarks"] = {
    "forward_sign": lm["forward_sign"],
    "leg_count": len(lm["legs"]),
    "legs": [{"side": l["side"], "is_front": l["is_front"], "foot": list(l["foot"]),
              "ankle": list(l["ankle"]), "knee": list(l["knee"]), "hip": list(l["hip"]),
              "bend_dir": list(l["bend_dir"])} for l in lm["legs"]],
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

# --- Leg masks (from the slicing-derived joint chains) -----------------------
# Built BEFORE weighting so the restriction pass below has something to restrict to. See
# rig_templates/winged_quadruped.py's build_leg_masks docstring: a vertex is assigned to the
# nearest leg's hip-knee-ankle-foot polyline if within `radius` of it, else left unassigned
# (body/tail/wing/neck -- normal weighting applies there, unrestricted).
leg_masks = template.build_leg_masks(obj, H, lm["legs"])
report["leg_masks"] = {side: len(vids) for side, vids in leg_masks.items()}
print("LEG MASKS:", json.dumps(report["leg_masks"]))

# --- Weighting ---------------------------------------------------------------
method_used, weight_log = common.auto_weight_with_fallbacks(obj, arm_obj, bone_names, H)
report["weighting"] = {"method": method_used, "log": weight_log}
for line in weight_log:
    print("WEIGHT:", line)
if method_used is None:
    raise SystemExit("rig_creature.py: auto-weighting failed on all three methods -- refusing to "
                      "proceed with an unrigged mesh.")

# Per-leg weight restriction: a vertex inside one leg's mask may only carry weight for that leg's
# own 4 bones or its parent (spine_02 for forelegs, pelvis for hind legs) -- so a foreleg vertex
# literally cannot carry hind-leg or tail weight regardless of what automatic/voxel weighting
# produced, closing off the specific failure mode (cross-limb weight bleed at the point two limbs
# pass close to each other) that contributed to the previous pass's tearing. Runs BEFORE
# cleanup_weights' limit/normalise/smooth pass, which re-normalises whatever this leaves behind.
removed = common.restrict_leg_weights(obj, leg_masks, bone_roles)
report["weighting"]["leg_restriction_groups_removed"] = removed
print(f"LEG WEIGHT RESTRICTION: removed {removed} out-of-chain group memberships")

# Lead-review round 5: fixes the Move-clip hip/belly tear at its diagnosed mechanism -- a vertex
# blending TWO different legs' (opposite-phase) thigh bones, not a hard-mask boundary (round 4's
# masks already excluded the thigh segment, which made no difference to the tear). See
# fix_hip_weight_gradient's docstring for the two passes (single-leg ownership near any thigh,
# belly-centre vertices forced off every thigh). Runs on top of restrict_leg_weights (which only
# ever touched the knee-ankle-foot region, not the thigh/hip area this targets) and still before
# cleanup_weights' renormalise.
cross_leg_removed, belly_forced = template.fix_hip_weight_gradient(obj, H, lm["legs"], bone_roles, enable_pass1=False)
report["weighting"]["hip_gradient_cross_leg_removed"] = cross_leg_removed
report["weighting"]["hip_gradient_belly_forced"] = belly_forced
print(f"HIP WEIGHT GRADIENT: {cross_leg_removed} vertices had a cross-leg thigh weight removed, "
      f"{belly_forced} belly-centre vertices forced off every thigh")

# Round 9: the same class of fix as fix_hip_weight_gradient, for the wing root -- see
# fix_wing_root_bleed's docstring.
wing_bleed_fixed = template.fix_wing_root_bleed(obj, H, lm, bone_roles)
report["weighting"]["wing_root_bleed_fixed"] = wing_bleed_fixed
print(f"WING ROOT BLEED: {wing_bleed_fixed} vertices had an out-of-range spine_02/scapula weight removed")

common.cleanup_weights(obj, limit=4)

# Round 9 lead review: a flat-shaded close-up and a new verify.py max-edge-stretch gate both found
# long stretched slivers in Move/Attack, traced to belly vertices carrying weight on TWO different
# legs (confirmed: e.g. leg_BL_thigh=0.43/leg_FL_thigh=0.32 on the same vertex -- BL and FL are
# never in phase in the gait, so that vertex gets pulled two directions every frame, stretching a
# triangle clear across the body). restrict_leg_weights/fix_hip_weight_gradient above already
# target exactly this, and DID remove it -- but cleanup_weights' own vertex_group_smooth call (just
# above) unintentionally spread the removed weight back in from neighbouring vertices that still
# legitimately carry it, since smoothing has no notion that a boundary it's softening was put there
# on purpose. Re-running both restriction passes here, AFTER smoothing, makes the restriction the
# true final word; common.normalize_weights (not a second full cleanup_weights call, which would
# just reintroduce the same contamination again) renormalises what's left without smoothing it back.
removed_post_smooth = common.restrict_leg_weights(obj, leg_masks, bone_roles)
cross_leg_removed_post_smooth, belly_forced_post_smooth = template.fix_hip_weight_gradient(
    obj, H, lm["legs"], bone_roles, enable_pass1=False)
wing_bleed_fixed_post_smooth = template.fix_wing_root_bleed(obj, H, lm, bone_roles)
common.normalize_weights(obj)
report["weighting"]["post_smooth_leg_restriction_removed"] = removed_post_smooth
report["weighting"]["post_smooth_hip_gradient_cross_leg_removed"] = cross_leg_removed_post_smooth
report["weighting"]["post_smooth_hip_gradient_belly_forced"] = belly_forced_post_smooth
report["weighting"]["post_smooth_wing_root_bleed_fixed"] = wing_bleed_fixed_post_smooth
print(f"POST-SMOOTH RE-RESTRICTION: leg={removed_post_smooth} "
      f"hip_gradient_cross_leg={cross_leg_removed_post_smooth} "
      f"hip_gradient_belly={belly_forced_post_smooth} "
      f"wing_root_bleed={wing_bleed_fixed_post_smooth}")

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


# All 12 role colors chosen to be mutually distinguishable by eye -- an earlier pass reused the
# same blue for wing_L and leg_FL (and a near-identical cyan for neck and leg_FL), which made an
# overlay render look like a leg's chain ran up the wing/neck when it was actually just a
# different bone drawn in a too-similar color (confirmed by checking the raw landmark numbers
# against the render, not by eye alone). leg_FL/FR/BL/BR vs wing_L/wing_R are the pair most likely
# to be confused (both can run tall near the shoulder/chest) so those six got the most separated
# hues; tail/neck/head/spine/pelvis/root are short or spatially unambiguous (head is always the
# terminus ball at the very top) so some hue reuse there is fine.
ROLE_COLOR = {
    "root": (1, 1, 1, 1), "pelvis": (1, 0.5, 0, 1), "spine": (1, 0.9, 0, 1),
    "neck": (0, 1, 0.2, 1), "head": (1, 0, 0, 1), "tail": (0.6, 0.2, 1, 1),
    "wing_L": (0.05, 0.05, 0.85, 1), "wing_R": (0.85, 0.3, 0.75, 1),
}

scene = setup_render(700)
# Semi-transparent mesh for the overlay renders (task brief: "textured mesh semi-transparent") so
# bones that sit INSIDE the leg are still visible through the surface, not hidden behind it --
# X-ray mode on top of the existing TEXTURE/MATERIAL shading, not a separate material swap.
scene.display.shading.show_xray = True
scene.display.shading.xray_alpha = 0.45

overlay_objs = []
markers = build_overlay_markers()
LEG_SIDE_COLOR = {
    "FL": (0.0, 0.9, 0.9, 1), "FR": (1.0, 0.0, 1.0, 1),
    "BL": (0.55, 0.35, 0.1, 1), "BR": (1.0, 0.85, 0.0, 1),
}
for name, head_ws, tail_ws in markers:
    role = bone_roles.get(name, "")
    if role.startswith("leg_"):
        color = LEG_SIDE_COLOR.get(role.split("_", 1)[1], (0.1, 0.8, 0.8, 1))
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
render_ortho(scene, os.path.join(OUT, "rig_overlay_bottom.png"), (0, -0.3, -4),
             (math.radians(180), 0, 0), ortho_scale=2.2)
render_ortho(scene, os.path.join(OUT, "rig_overlay_34.png"), (2.6, -3.0, 1.6),
             (math.radians(65), 0, math.radians(40)), ortho_scale=2.8)
print("RIG OVERLAY RENDERED (front/side/bottom/3-4)")
scene.display.shading.show_xray = False

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

# Per-leg lifted-and-extended: each leg on its own, swung well past its rest bend while the other
# three stay planted at rest -- isolates shoulder/hip tearing that a combined all-legs-bent pose
# (above) can hide (one leg's crease can be masked by another leg's silhouette from a single
# camera angle). Task brief's explicit ask: "each leg lifted and extended."
for side in leg_sides:
    reset_pose()
    set_pose_euler(f"leg_{side}_thigh", deg_x=-38, deg_z=(10 if side.endswith("L") else -10))
    set_pose_euler(f"leg_{side}_shin", deg_x=46)
    bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    render_ortho(scene, os.path.join(OUT, f"weightcheck_leg_{side}_extended.png"), (3, -3, 1.0),
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
