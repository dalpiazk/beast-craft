"""Tooling/Animation stage 3a: procedural locomotion ("Move" clip).

Builds a parametric walk cycle for the rigged Griffin (4 legs, all four walking -- see
rig_templates/winged_quadruped.py's docstring for the horizontal-slicing landmark detection that
makes this possible: real hip/knee/ankle/foot joints for every leg, not a guessed foreleg fold)
and bakes it to a clean in-place loop. Implements the methodology doc's section 3b recommendation
directly: velocity-based foot placement + closed-form 2-bone IK + spline-ish foot arcs + lateral-
sequence phase offsets per leg, with wings folded (slight idle-ish motion) and body bob/sway
driven by the leg phase.

IK is closed-form analytic (law of cosines for the knee angle, "aim" direction for the hip), NOT a
Blender IK constraint -- see rig_creature.py's docstring for why. Each leg bone's desired pose is
computed as a full world-space matrix (bone head at the solved joint position, bone Y-axis aimed
at the next joint) and assigned directly to `pose_bone.matrix`, which lets Blender back-solve
loc/rot/scale against the bone's own rest orientation -- this sidesteps needing to hand-derive each
bone's local axis convention (every bone built by rig_templates.build_bones has its own roll,
computed by Blender's edit-bone creation, which is not worth re-deriving by hand).

In-place walk-cycle convention (why the foot "slides" during stance, and why that's correct here):
this is a baked **in-place** clip (per the methodology doc section 4: "prefer in-place move clips
driven by code-side root translation... avoids the classic root-motion-vs-grid-snapping mismatch").
With no root translation baked in, a foot that's rigidly fixed in world space for the whole stance
window would mean the leg never visibly drives anything -- instead, the standard convention (used
by essentially every procedural in-place gait, including the Overgrowth/GDC talk this pipeline
cites) is: during stance the foot sweeps backward *relative to the hip* at a constant rate (from
+stride/2 to -stride/2), and during swing it arcs forward back to +stride/2. When the engine later
translates the root forward at the matching rate over the clip's duration, the constant backward
sweep exactly cancels the root's forward translation and the foot is truly stationary in world
space during stance -- "no foot slide by construction," but only if the stance-phase velocity is
*constant* (any deviation from constant velocity is exactly what would show up as slide once
combined with constant-rate root motion). verify.py's foot-slide gate checks constancy, not
literal zero velocity, for this reason -- see its docstring.

Run headless:
  blender -b --python gait.py -- --blend RIGGED.blend --out OUTDIR [--fps 24] [--cycle-seconds 1.0]
"""
import bpy
import sys
import os
import json
import math
import mathutils

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common

args = common.parse_args(common.get_argv())
BLEND = args["blend"]
OUT = args["out"]
FPS = int(args.get("fps", 24))
CYCLE_SECONDS = float(args.get("cycle-seconds", 1.0))
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = FPS

arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
arm_data = arm_obj.data
H = 2.0  # this pipeline's shared normalisation convention (common.normalise_transform default)

# Bone roles (role -> [bone names]) were not persisted in the .blend's custom data by
# rig_creature.py, so re-derive leg/wing/tail bone groupings from the naming convention
# rig_templates.build_bones uses (leg_<side>_{thigh,shin,foot,toe}, wing_{L,R}_{01,02,03},
# tail_{01..04}) -- simpler and just as reliable as round-tripping the role dict through a file.
all_bones = [b.name for b in arm_data.bones]
leg_sides = sorted({n.split("_")[1] for n in all_bones if n.startswith("leg_")})
print(f"LEG SIDES: {leg_sides}")

# Per-leg bend direction (and is_front), read from rig_creature.py's rig_report.json (same OUT
# directory as the rigged .blend) -- this is the horizontal-slicing landmark detection's own read
# of which way EACH leg's knee/hock actually bows (rig_templates/winged_quadruped.py's
# detect_landmarks: a foreleg's elbow and a hind leg's hock bend oppositely on this mesh), so the
# IK pole below matches the real geometry per leg instead of a single hardcoded "+Y" guess that
# would be correct for at most one of the two bend families.
bend_dir_by_side = {}
is_front_by_side = {}
report_path = os.path.join(os.path.dirname(os.path.abspath(BLEND)), "rig_report.json")
if os.path.isfile(report_path):
    with open(report_path) as f:
        rig_report = json.load(f)
    for leg in rig_report.get("landmarks", {}).get("legs", []):
        side = leg["side"]
        bd = leg.get("bend_dir")
        if bd:
            bend_dir_by_side[side] = mathutils.Vector(bd)
        is_front_by_side[side] = leg.get("is_front", side.startswith("F"))
    print(f"BEND DIRS from rig_report.json: "
          f"{ {s: tuple(round(c,3) for c in v) for s,v in bend_dir_by_side.items()} }")
else:
    print(f"WARNING: no rig_report.json next to {BLEND} -- falling back to a uniform +Y knee bow "
          f"for every leg (correct for at most one of the foreleg/hindleg bend families).")


def rest_head_tail(name):
    b = arm_data.bones[name]
    return b.head_local.copy(), b.tail_local.copy()


def solve_2bone_ik(hip, L1, L2, target, bend_dir):
    """Closed-form 2-bone IK in the hip-knee-target plane. Returns (knee_world, target_world) such
    that |hip->knee|==L1, |knee->target|==L2 (clamped to reachable range if `target` is out of
    reach). bend_dir biases which side of the hip-target line the knee bows toward (prevents the
    degenerate "knee could be anywhere" case when target is exactly on the hip-target axis).

    Callers pass L2 = shin_length + foot_length (the ankle-to-ground-contact segment folded into
    the effective second bone) and treat `target` as the ground-contact point directly, not the
    ankle -- a first pass solved hip->knee->ANKLE for a target that was actually the ground point
    (foot_target()'s output), silently placing the ankle ~0.09 units short of where the foot
    segment needed to end up, which meant the true hip-to-ground-target distance (0.60 for this
    rig) regularly exceeded L1+L2 (0.55, ankle-only) and the solve clamped on nearly every frame --
    confirmed as the cause of a failed foot-slide gate (stance-velocity cv=0.406 against a 0.35
    threshold, scratchpad verify run). Folding the foot segment into L2 and deriving the ankle's
    position afterward (set_leg_pose below, by lerping knee->target at the shin/L2 fraction) fixes
    this: the ankle is no longer a second, separately-solved IK target, just a fixed point along
    the already-correct knee-to-ground line."""
    to_target = target - hip
    d = to_target.length
    max_reach = L1 + L2
    min_reach = abs(L1 - L2)
    d_clamped = max(min_reach + 1e-5, min(max_reach - 1e-5, d))
    dirn = to_target.normalized() if d > 1e-6 else (knee_rest - hip).normalized()
    # Law of cosines: angle at hip between (hip->target) and (hip->knee).
    cos_a = (L1 * L1 + d_clamped * d_clamped - L2 * L2) / (2 * L1 * d_clamped)
    cos_a = max(-1.0, min(1.0, cos_a))
    a = math.acos(cos_a)
    # Build an orthonormal basis in the bend plane (dirn, bend_perp) to rotate `dirn` by angle a.
    bend_perp = (bend_dir - bend_dir.dot(dirn) * dirn)
    if bend_perp.length < 1e-6:
        # bend_dir degenerate (parallel to dirn); fall back to any perpendicular.
        arbitrary = mathutils.Vector((0, 0, 1)) if abs(dirn.z) < 0.9 else mathutils.Vector((1, 0, 0))
        bend_perp = arbitrary - arbitrary.dot(dirn) * dirn
    bend_perp.normalize()
    knee_dir = dirn * math.cos(a) + bend_perp * math.sin(a)
    knee_world = hip + knee_dir * L1
    target_clamped = hip + dirn * d_clamped
    ankle_world = target_clamped  # ankle sits at the (possibly clamped) target
    return knee_world, ankle_world


def aim_matrix(head_pos, tail_pos, up_hint=mathutils.Vector((0, 0, 1))):
    """World matrix with translation=head_pos and +Y axis pointing from head to tail (matching
    Blender's edit-bone convention: local +Y runs head->tail at rest)."""
    y_axis = (tail_pos - head_pos)
    if y_axis.length < 1e-7:
        y_axis = mathutils.Vector((0, 0, -1))
    y_axis.normalize()
    x_axis = up_hint.cross(y_axis)
    if x_axis.length < 1e-6:
        # Degenerate: y_axis (the bone's own head->tail direction, which varies every frame as the
        # IK solve moves) is parallel to up_hint. BUG, now fixed: every caller in this file passes
        # up_hint=(1,0,0), so the old fallback here ALSO reassigned (1,0,0) -- the exact vector that
        # just failed -- leaving x_axis genuinely zero (a non-invertible matrix) whenever a bone
        # pointed along global X, and producing an ill-conditioned (not just exactly-singular)
        # frame for frames NEAR that direction too, amplifying tiny differences into visible
        # per-frame wobble. Confirmed as the root cause of FL's failed foot-slide gate (cv=0.68):
        # FL's bend_dir is unusually X-dominant (0.925, 0.297, -0.236, from the horizontal-slicing
        # detection -- a real reading of this specific leg's own knee bow, not a bug in the
        # detection), which pulls the shin/foot bones' own head->tail direction close to global X
        # as the IK solve sweeps through FL's stance -- near-exactly the up_hint every leg bone in
        # this file was built with. A genuinely different fallback axis (global Z, or global Y if
        # the bone itself points near Z) fixes this for every leg, not just a special case for FL.
        fallback = mathutils.Vector((0, 0, 1)) if abs(y_axis.z) < 0.9 else mathutils.Vector((0, 1, 0))
        x_axis = fallback.cross(y_axis)
    x_axis.normalize()
    z_axis = x_axis.cross(y_axis)
    # Wait: Blender bone basis is (X, Y, Z) with Y = head->tail; build a right-handed frame.
    z_axis.normalize()
    mat = mathutils.Matrix((
        (x_axis.x, y_axis.x, z_axis.x, head_pos.x),
        (x_axis.y, y_axis.y, z_axis.y, head_pos.y),
        (x_axis.z, y_axis.z, z_axis.z, head_pos.z),
        (0, 0, 0, 1),
    ))
    return mat


def set_bone_world_matrix_direct(pb, desired_world, parent_world, parent_rest):
    """Sets pb's pose directly to `desired_world` (an armature-space 4x4 matrix) by computing
    matrix_basis analytically from Blender's own pose-chain formula:

        pose_matrix[bone] = pose_matrix[parent] @ parent_rest^-1 @ bone_rest @ matrix_basis[bone]
        => matrix_basis[bone] = bone_rest^-1 @ parent_rest @ parent_world^-1 @ desired_world

    This needs no bpy.context.view_layer.update() call and no dependence on Blender having already
    re-evaluated anything -- `parent_world` is a matrix the CALLER already computed in the same
    Python pass (chained frame-by-frame below), not read back from Blender's live pose state.

    Why this exists (replacing an earlier `pb.matrix = aim_matrix(...)` + view_layer.update()
    approach): that approach relied on Blender's depsgraph to make a just-set parent's pose visible
    before computing a child's matrix_basis, via a view_layer.update() call between every bone.
    While authoring (the armature's animation_data.action is live-assigned and growing one
    keyframe at a time), each view_layer.update() call turned out to also re-evaluate the action's
    *existing* F-curves onto bones that already had earlier frames' keyframes, intermittently
    clobbering values set earlier in the *same* frame's processing before they were ever
    keyframed. Symptom: a per-frame probe of the foot's world Y position during what should be a
    perfectly linear stance sweep showed a sawtooth/non-monotonic pattern (scratchpad probe,
    documented in verify.py's run log) instead of a straight line -- confirmed gone after switching
    every leg-chain assignment to this direct, Blender-state-independent computation. Returns
    `desired_world` unchanged, for convenient chaining to the next bone in the hierarchy."""
    rest = pb.bone.matrix_local
    basis = rest.inverted() @ parent_rest @ parent_world.inverted() @ desired_world
    pb.matrix_basis = basis
    return desired_world


# Per-leg rest geometry + a per-leg SAFE stride, solved from each leg's own reach. L2 here is the
# COMBINED shin+foot length (ankle-to-ground), not just the shin -- see solve_2bone_ik's docstring
# for why: the IK target is the ground-contact point, so the second "effective bone" for the 2-bone
# solve must span all the way from the knee to the ground, not stop at the ankle.
DUTY = 0.6              # fraction of the cycle each foot spends in stance
BODY_BOB = 0.025 * H    # vertical bob amplitude, two bob cycles per stride (both feet contribute)
RAW_STRIDE = 0.23 * H   # desired fore-aft HALF-stride excursion (foot sweeps +RAW_STRIDE to
# -RAW_STRIDE), before the per-leg reach clamp. Full peak-to-peak foot travel is 2x this -- the
# lead-review fix round's "readable stride, ~25-40% of body length" target is interpreted as that
# peak-to-peak distance, so this aims for ~30% of H unclamped, clamped down per-leg by actual IK
# reach (see the per-leg safe_stride computation above for the real achieved numbers per leg).

legs = {}
for side in leg_sides:
    hip, knee = rest_head_tail(f"leg_{side}_thigh")
    _, ankle = rest_head_tail(f"leg_{side}_shin")
    _, foot = rest_head_tail(f"leg_{side}_foot")
    _, toe = rest_head_tail(f"leg_{side}_toe")
    L1 = (knee - hip).length
    shin_len = (ankle - knee).length
    foot_len = (foot - ankle).length
    L2 = shin_len + foot_len  # combined effective 2nd bone
    max_reach = L1 + L2
    ankle_frac = shin_len / L2 if L2 > 1e-6 else 0.5

    # Solve the largest |y_off| (fore-aft sweep from foot_rest, the IK target's only moving axis)
    # that keeps hip-to-target distance within a safety margin of max_reach, so the stance sweep
    # never clamps (a first pass used a fixed STRIDE for every leg and clamped on nearly every
    # frame -- see solve_2bone_ik's docstring -- which produced wildly non-constant stance
    # velocity and failed verify.py's foot-slide gate at cv=0.406 against a 0.35 threshold).
    SAFETY = 0.95
    reach_margin = max_reach * SAFETY
    # All four feet are grounded in this mesh's own bind pose (producer-confirmed, see
    # rig_templates/winged_quadruped.py's docstring) -- ground_z=0 for every leg, front or back,
    # no "forelegs hover at bind height" special case. The horizontal-slicing landmark detection
    # placed each leg's hip/knee/ankle directly from the mesh's own geometry (not a guessed
    # proportional fold), so there is no longer an artificially extreme bind-pose knee bend to
    # animate across.
    ground_z = 0.0
    base_sq = (foot.x - hip.x) ** 2 + (ground_z - hip.z) ** 2
    margin_y = foot.y - hip.y
    r_sq = reach_margin ** 2 - base_sq
    if r_sq <= 0:
        safe_stride = 0.005 * H  # truly degenerate rig geometry fallback -- shouldn't trigger here
    else:
        r = math.sqrt(r_sq)
        # Lower bound 0.005H (a near-imperceptible but non-zero sweep), NOT the 0.02H floor an
        # earlier pass used unconditionally -- forcing stride UP to a fixed floor regardless of
        # the actual computed safe value re-introduces exactly the clamping this computation exists
        # to avoid whenever a leg's true safe excursion is smaller than that floor (confirmed on
        # this mesh: FL's computed safe value was ~0.037H, the floor forced it to 0.04H, and the
        # resulting tiny-but-unsafe excursion kept the IK solve clamped through most of stance --
        # non-constant stance velocity, failing verify.py's foot-slide gate at cv=0.68 against a
        # 0.35 threshold even though the stride itself already read as "barely moves"). Respecting
        # the computed safe value even when it's smaller than the floor fixes this at the root.
        safe_stride = max(0.005 * H, min(r - abs(margin_y), RAW_STRIDE))

    # Lift scales WITH the solved stride (not a fixed absolute height) so a geometrically-
    # constrained short stride (this Griffin's: the landmark-placed hip leaves little slack before
    # hitting max reach -- see safe_stride above) still reads as a proportionate short, quick step
    # rather than "barely moves forward but kicks way up," which a fixed large LIFT would produce.
    lift = max(0.02 * H, 0.9 * safe_stride)

    # Lift SAFETY cap against the MINIMUM-reach clamp (not just the max-reach one safe_stride
    # already guards): as the swing arc lifts the foot toward z=lift, the vertical hip-to-target
    # gap shrinks -- for a leg whose hip sits only a little above min_reach (=|L1-L2|), a large
    # enough lift brings hip-to-target distance down near min_reach, folding the knee almost flat
    # (confirmed directly: verify.py's knee-angle gate failed at ~1 degree for exactly this
    # mechanism on this mesh before this cap existed). Capping lift so the worst-case (straight
    # down from the hip) distance stays a safety margin above min_reach prevents that regardless of
    # stride.
    min_reach = abs(L1 - L2)
    max_safe_lift = max(0.01 * H, hip.z - min_reach * 1.15)
    lift = min(lift, max_safe_lift)

    legs[side] = {"hip": hip, "L1": L1, "L2": L2, "foot_rest": foot, "toe_rest": toe,
                  "stride": safe_stride, "lift": lift, "_ankle_frac": ankle_frac, "ground_z": ground_z,
                  "knee_rest": knee, "ankle_rest": ankle,
                  "bend_dir": bend_dir_by_side.get(side, mathutils.Vector((0, 1, 0)))}
    print(f"LEG {side}: hip={tuple(round(c,3) for c in hip)} L1={L1:.3f} L2={L2:.3f} "
          f"max_reach={max_reach:.3f} rest_dist={(foot-hip).length:.3f} "
          f"safe_stride={safe_stride:.3f} (raw {RAW_STRIDE:.3f})")

phase_offset = {}
# Lead-review fix round: real quadruped lateral-sequence walk timing (LH -> LF -> RH -> RF, each a
# quarter-cycle out of phase from the previous) for the confirmed 4-leg case, replacing the earlier
# 2-beat fallback. BL/BR/FL/FR are this template's side codes for back-left/back-right/front-left/
# front-right (see rig_templates/winged_quadruped.py) -- i.e. BL=LH, FL=LF, BR=RH, FR=RF.
LATERAL_SEQUENCE = {"BL": 0.0, "FL": 0.25, "BR": 0.5, "FR": 0.75}
if set(leg_sides) == set(LATERAL_SEQUENCE):
    phase_offset = dict(LATERAL_SEQUENCE)
elif len(leg_sides) == 2:
    s0, s1 = leg_sides
    phase_offset = {s0: 0.0, s1: 0.5}
else:
    for i, s in enumerate(leg_sides):
        phase_offset[s] = (i % 2) * 0.5  # 2-beat gait fallback for any other leg-count/naming


def foot_target(side, t):
    """t in [0,1). Returns (world_pos, is_stance). world_pos is the GROUND-CONTACT point (not the
    ankle) -- see solve_2bone_ik's docstring.

    Z is always GROUND-RELATIVE (0 = stance, lift-arc above 0 during swing), not rest.z + offset:
    all four feet are already grounded in this mesh's own bind pose (producer-confirmed, see
    rig_templates/winged_quadruped.py's docstring -- a reared/proud stance with the chest held
    higher than the hips, NOT a rampant stance with the forelegs lifted off the ground), so this
    applies uniformly to every leg. Grounding every leg at z=0 during its own stance is what "zero
    foot slide" / a real quadruped gait needs regardless.
    """
    rest = legs[side]["foot_rest"]
    stride = legs[side]["stride"]
    lift = legs[side]["lift"]
    ground_z = legs[side]["ground_z"]

    ph = (t + phase_offset[side]) % 1.0
    if ph < DUTY:
        u = ph / DUTY
        y_off = stride * (1.0 - 2.0 * u)  # +stride -> -stride, LINEAR (constant velocity -- see
        # module docstring: constant stance velocity is what cancels a constant-rate root
        # translation with zero residual slide).
        z = ground_z
        stance = True
    else:
        u = (ph - DUTY) / (1.0 - DUTY)
        ease = u * u * (3 - 2 * u)  # smootherstep: eased arc, non-linear (principle of arcs)
        y_off = stride * (-1.0 + 2.0 * ease)
        z = ground_z + lift * math.sin(math.pi * u)
        stance = False
    pos = mathutils.Vector((rest.x, rest.y + y_off, z))
    return pos, stance


def set_leg_pose(side, t, parent_world, parent_rest):
    """parent_world/parent_rest: the world/rest matrix of THIS leg's actual Blender parent bone --
    pelvis for back legs, spine_02 for front legs (see rig_templates/winged_quadruped.py's
    build_bones: forelegs are parented to spine_02, the shoulder girdle, not the pelvis). Passing
    the wrong one here would place the leg offset by the rest-pose gap between pelvis and spine_02
    (a large vertical gap in this rig) even though set_bone_world_matrix_direct still *looks* like
    it succeeds -- it silently computes a matrix_basis that's only correct for the parent it was
    told about, not the bone's real one, and Blender's own evaluation uses the real parent chain."""
    L = legs[side]
    ground_target, stance = foot_target(side, t)

    # Per-leg IK pole: `bend_dir` comes from the horizontal-slicing landmark detection's own read
    # of which way THIS leg's knee/hock bows (rig_templates/winged_quadruped.py's
    # detect_landmarks) -- a foreleg's elbow and a hind leg's hock bend oppositely on this mesh, so
    # a single hardcoded "+Y" pole (an earlier pass's approach, before all four legs were walking)
    # would only be correct for one of the two families. Falls back to +Y if rig_report.json wasn't
    # found (see this file's top-level load).
    bend_dir = L["bend_dir"]
    knee_w, reached = solve_2bone_ik(L["hip"], L["L1"], L["L2"], ground_target, bend_dir)
    # Ankle: a fixed point along the knee->ground line, at the shin's share of the combined L2
    # (shin_len / (shin_len+foot_len)) -- see solve_2bone_ik's docstring for why the ankle is
    # derived here rather than itself being a separate IK target.
    ankle_w = knee_w.lerp(reached, L["_ankle_frac"])
    toe_w = reached + (L["toe_rest"] - L["foot_rest"])  # toe follows foot rigidly (no separate
    # roll target this pass -- foot roll is a nice-to-have the task brief lists; given the time
    # budget this pilot keeps the foot a single rigid segment through stance/swing, which already
    # avoids foot slide by construction (the main quality driver per the methodology doc) -- noted
    # as a follow-up in the README rather than silently skipped.
    target = reached

    thigh_pb = arm_obj.pose.bones[f"leg_{side}_thigh"]
    shin_pb = arm_obj.pose.bones[f"leg_{side}_shin"]
    foot_pb = arm_obj.pose.bones[f"leg_{side}_foot"]
    toe_pb = arm_obj.pose.bones[f"leg_{side}_toe"]
    thigh_rest = thigh_pb.bone.matrix_local
    shin_rest = shin_pb.bone.matrix_local
    foot_rest_mat = foot_pb.bone.matrix_local
    toe_rest_mat = toe_pb.bone.matrix_local

    # up_hint picked PER LEG, as the world axis least aligned with this leg's own detected bend
    # direction -- a single hardcoded (1,0,0) (every leg, every frame, an earlier pass's approach)
    # put aim_matrix's cross-product into the ill-conditioned near-parallel regime whenever a leg's
    # bones swing close to that axis, which on this mesh happens for real for FL/BR specifically
    # (their horizontal-slicing-detected bend_dir is X-dominant -- confirmed as the root cause of
    # FL's failed foot-slide gate: aim_matrix's degenerate-direction fallback also had its own bug,
    # fixed separately above, but avoiding the near-singular regime in the first place is the more
    # robust fix). bend_dir is a stable per-leg constant (not frame-dependent), so this choice is
    # made once per leg, not per frame, and stays consistent across the whole clip.
    bd = L["bend_dir"]
    axis_candidates = [mathutils.Vector((1, 0, 0)), mathutils.Vector((0, 1, 0)), mathutils.Vector((0, 0, 1))]
    up_hint = min(axis_candidates, key=lambda ax: abs(ax.dot(bd)))
    thigh_world = set_bone_world_matrix_direct(
        thigh_pb, aim_matrix(L["hip"], knee_w, up_hint), parent_world, parent_rest)
    shin_world = set_bone_world_matrix_direct(
        shin_pb, aim_matrix(knee_w, ankle_w, up_hint), thigh_world, thigh_rest)
    foot_world = set_bone_world_matrix_direct(
        foot_pb, aim_matrix(ankle_w, target, up_hint), shin_world, shin_rest)
    set_bone_world_matrix_direct(
        toe_pb, aim_matrix(target, toe_w, up_hint), foot_world, foot_rest_mat)
    return stance


def set_rot_local(bone_name, deg_x=0, deg_y=0, deg_z=0):
    pb = arm_obj.pose.bones.get(bone_name)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (math.radians(deg_x), math.radians(deg_y), math.radians(deg_z))


# Set every pose bone to QUATERNION rotation mode up front for leg bones (matrix-driven), XYZ for
# the rest (simple sinusoidal driving below).
bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
for side in leg_sides:
    for suffix in ("thigh", "shin", "foot", "toe"):
        arm_obj.pose.bones[f"leg_{side}_{suffix}"].rotation_mode = "QUATERNION"

FRAMES = int(round(CYCLE_SECONDS * FPS))
move_action = bpy.data.actions.new("Move")
move_action.use_fake_user = True  # survives a save even if .action gets reassigned later
arm_obj.animation_data_create()
arm_obj.animation_data.action = move_action

root_pb = arm_obj.pose.bones.get("root")
pelvis_pb = arm_obj.pose.bones.get("pelvis")
spine02_pb = arm_obj.pose.bones.get("spine_02")
if root_pb:
    root_pb.rotation_mode = "XYZ"
root_rest = root_pb.bone.matrix_local
pelvis_rest = pelvis_pb.bone.matrix_local
spine02_rest = spine02_pb.bone.matrix_local if spine02_pb else pelvis_rest
identity4 = mathutils.Matrix.Identity(4)

for i in range(FRAMES + 1):  # +1: bake the loop-closing frame identical to frame 0
    t = (i % FRAMES) / FRAMES
    a = 2 * math.pi * t
    scene.frame_set(i + 1)

    # Body bob: lowest when a foot is mid-stance (weight-bearing, both legs roughly under body),
    # highest at the cross-over point between steps (classic double "M"-shaped bob over one cycle).
    # Computed and applied to root BEFORE the legs, both because the legs' world-space IK targets
    # must be solved against root's bob (so feet stay correctly grounded despite the body bobbing)
    # and because set_leg_pose needs pelvis_world, which is derived from root_world here.
    bob = BODY_BOB * abs(math.sin(2 * a))
    root_world = root_rest @ mathutils.Matrix.Translation((0, 0, bob))
    if root_pb:
        root_pb.location = (0, 0, bob)
        root_pb.keyframe_insert(data_path="location", frame=i + 1)
    # pelvis is never itself animated (matrix_basis stays identity) -- its world matrix is purely
    # root's current world matrix composed with the fixed rest offset between root and pelvis.
    pelvis_world = root_world @ root_rest.inverted() @ pelvis_rest
    # spine_02 (the forelegs' real parent -- see set_leg_pose's docstring) DOES get a small
    # animated counter-rotation later this same frame (set_rot_local("spine_02", ...) below), but
    # that happens after legs are placed; approximating spine_02_world as if it were identity-basis
    # (same formula shape as pelvis_world) ignores that few-degree rotation. The resulting
    # foreleg-position error from this approximation is small relative to the rest-pose gap between
    # pelvis and spine_02 that using pelvis_world outright would have caused (33-45% of body height
    # in Z alone) -- documented simplification, not an oversight.
    # spine_02's own small counter-rotation (set below via set_rot_local) is folded in HERE,
    # analytically, before it's used as the forelegs' parent transform -- an earlier version used
    # spine02_world = root_world @ root_rest.inverted() @ spine02_rest (identity local basis),
    # approximating away this same few-degree rotation, "documented" as a small simplification.
    # It was not negligible: FL's own IK reach margin (hip-to-foot distance vs. L1+L2) is tight
    # enough on this mesh that even this few-degree parent error was enough to push the solve in
    # and out of its max-reach clamp every frame -- confirmed directly (a debug probe showed
    # solve_2bone_ik's OWN output, in hip-local space, exactly matching its target every frame with
    # zero clamping, while the bone actually rendered into the saved action drifted off that target
    # by up to several mm -- the discrepancy could only be coming from the parent chain, which this
    # fixes). Computed directly from the same sinusoid set_rot_local("spine_02", ...) below applies,
    # so the two stay in sync by construction, not by re-deriving/duplicating the formula.
    spine02_local_pitch = math.radians(2 * math.sin(2 * a))
    spine02_world = (root_world @ root_rest.inverted() @ spine02_rest
                      @ mathutils.Matrix.Rotation(spine02_local_pitch, 4, "X"))

    stances = []
    for side in leg_sides:
        if side.startswith("F"):
            stances.append(set_leg_pose(side, t, spine02_world, spine02_rest))
        else:
            stances.append(set_leg_pose(side, t, pelvis_world, pelvis_rest))

    # Tail: counter-sways opposite the leg phase for a touch of weight-shift readability.
    set_rot_local("tail_01", deg_x=6 * math.sin(a), deg_z=4 * math.sin(a + 0.3))
    set_rot_local("tail_02", deg_x=8 * math.sin(a + 0.4), deg_z=5 * math.sin(a + 0.6))
    set_rot_local("tail_03", deg_x=6 * math.sin(a + 0.8))
    set_rot_local("tail_04", deg_x=4 * math.sin(a + 1.1))

    # Wings: folded, slight settle motion (not flapping -- this is a ground-locomotion clip).
    set_rot_local("wing_L_01", deg_z=-6 - 2 * math.sin(a * 2), deg_x=2 * math.sin(a))
    set_rot_local("wing_L_02", deg_z=-4 * math.sin(a * 2 + 0.5))
    set_rot_local("wing_R_01", deg_z=6 + 2 * math.sin(a * 2), deg_x=2 * math.sin(a))
    set_rot_local("wing_R_02", deg_z=4 * math.sin(a * 2 + 0.5))

    # Head/neck/spine: a light counter-rotation to the body bob reads as weight/balance. spine_02's
    # rotation here MUST match spine02_local_pitch above exactly (same formula) -- it's the pose
    # this frame's leg placement was actually computed against.
    set_rot_local("spine_02", deg_x=2 * math.sin(2 * a))
    set_rot_local("neck_01", deg_x=-2 * math.sin(2 * a))
    set_rot_local("head", deg_x=-1.5 * math.sin(2 * a + 0.2))

    for side in leg_sides:
        for suffix in ("thigh", "shin", "foot", "toe"):
            pb = arm_obj.pose.bones[f"leg_{side}_{suffix}"]
            pb.keyframe_insert(data_path="rotation_quaternion", frame=i + 1)
            pb.keyframe_insert(data_path="location", frame=i + 1)
    for name in ("tail_01", "tail_02", "tail_03", "tail_04", "wing_L_01", "wing_L_02",
                 "wing_R_01", "wing_R_02", "spine_02", "neck_01", "head"):
        pb = arm_obj.pose.bones.get(name)
        if pb:
            pb.keyframe_insert(data_path="rotation_euler", frame=i + 1)

bpy.ops.object.mode_set(mode="OBJECT")
# LINEAR, not Bezier: this action is baked every frame from already-eased Python math (smootherstep
# on the swing arc, deliberately constant/linear during stance -- see module docstring on why
# constant stance velocity matters for a slide-free result once combined with root motion).
# Re-interpolating a dense per-frame bake with Bezier/EASE_IN_OUT re-eases between already-close
# samples and measurably hurt stance-velocity constancy (confirmed: an earlier pass with Bezier
# here failed verify.py's foot-slide gate at cv=0.406 against a 0.35 threshold; switching to LINEAR
# fixed it -- see verify.py's docstring for the gate's definition). The methodology doc's "always
# Bezier EASE_IN_OUT, never LINEAR" rule is about *sparse hand-keyed* clips (anim/keyed.py uses it
# correctly); a densely pre-eased procedural bake is the documented exception, matching
# Tooling/Spike55/blender_export_live.py's own precedent (LINEAR for its per-frame baked actions).
n_curves = common.set_interpolation(move_action, "LINEAR")
stride_summary = ", ".join(f"{s}={legs[s]['stride']:.3f}" for s in leg_sides)
print(f"MOVE ACTION: {FRAMES + 1} frames @ {FPS}fps, {n_curves} fcurves, duty={DUTY}, "
      f"stride=[{stride_summary}]")

blend_out = os.path.join(OUT, "griffin_move.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print(f"SAVED {blend_out}")
print("GAIT DONE")
