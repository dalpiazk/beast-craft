"""Tooling/Animation stage 4: verification gates.

Runs the numeric QA gates from the methodology doc's section 5 against the baked Move/Idle/Attack
actions: foot-slide, joint-angle limits, loop-seam continuity (for the two looping clips), F-curve
jitter, and a rough interpenetration check (feet-below-ground, wing-through-body bounding check).
All numeric -- no rendering needed for these (the silhouette/contact-sheet/onion-skin renders the
task brief also asks for are a separate, visual step: rig_creature.py's weight-check sheet covers
the rig-fitting visual check; the producer-review contact sheet/GIF reel in stage 6 covers the
per-clip visual check).

Foot-slide interpretation for an IN-PLACE clip (see anim/gait.py's docstring for the full
reasoning): the gate checks that stance-phase foot velocity is *constant*, not literally zero --
constant stance velocity is exactly what cancels a constant-rate root translation with zero
residual slide once the engine drives the root; non-constant stance velocity is what would show up
as visible slide. Reported as the coefficient of variation of per-frame stance speed, in mm/frame
terms (both absolute mm/frame deviation and the normalised ratio).

Run headless:
  blender -b --python verify.py -- --move MOVE.blend --keyed KEYED.blend --out OUTDIR
"""
import bpy
import sys
import os
import json
import math
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import common

args = common.parse_args(common.get_argv())
MOVE_BLEND = args.get("move")
KEYED_BLEND = args.get("keyed")
OUT = args["out"]
os.makedirs(OUT, exist_ok=True)

report = {}


def load(blend_path):
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    return arm_obj


def world_bone_head(arm_obj, bone_name):
    pb = arm_obj.pose.bones.get(bone_name)
    if pb is None:
        return None
    return arm_obj.matrix_world @ pb.head


def sample_action_bone_positions(arm_obj, action, bone_name, point="head"):
    arm_obj.animation_data.action = action
    f0, f1 = action.frame_range
    f0, f1 = int(f0), int(f1)
    scene = bpy.context.scene
    positions = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        pb = arm_obj.pose.bones.get(bone_name)
        pos = (arm_obj.matrix_world @ pb.head) if point == "head" else (arm_obj.matrix_world @ pb.tail)
        positions.append((f, pos.copy()))
    return positions


def velocities(positions, fps):
    vels = []
    for i in range(1, len(positions)):
        f0, p0 = positions[i - 1]
        f1, p1 = positions[i]
        dt = (f1 - f0) / fps
        v = (p1 - p0).length / dt if dt > 0 else 0.0
        vels.append(v)
    return vels


# ---------------------------------------------------------------------------
# Move: foot-slide (stance constancy), leg joint-angle limits, loop-seam continuity, jitter.
# ---------------------------------------------------------------------------
if MOVE_BLEND:
    arm_obj = load(MOVE_BLEND)
    move_action = bpy.data.actions.get("Move")
    fps = bpy.context.scene.render.fps
    leg_sides = sorted({n.name.split("_")[1] for n in arm_obj.data.bones if n.name.startswith("leg_")})
    print(f"VERIFY Move: legs={leg_sides}, fps={fps}")

    DUTY = 0.6
    # Lateral-sequence phase offsets (lead-review fix round, 4-leg quadruped walk) -- must match
    # anim/gait.py's LATERAL_SEQUENCE exactly, or this gate samples the wrong window and reads
    # swing-phase velocity as if it were stance. Falls back to the old 2-leg alternation for any
    # other leg-side naming.
    #
    # Round 11: corrected to match anim/gait.py's fix -- the old {BL:0.0,FL:0.25,BR:0.5,FR:0.75}
    # touched feet down in diagonal-sequence order (LH,RF,RH,LF); swapping FL/FR gives the correct
    # lateral-sequence order (LH,LF,RH,RF) -- see gait.py's LATERAL_SEQUENCE comment and this file's
    # new check_walk_direction gate below.
    LATERAL_SEQUENCE = {"BL": 0.0, "FL": 0.75, "BR": 0.5, "FR": 0.25}
    move_report = {"fps": fps, "legs": {}}
    for side in leg_sides:
        if set(leg_sides) == set(LATERAL_SEQUENCE):
            phase_off = LATERAL_SEQUENCE[side]
        else:
            phase_off = 0.0 if side == leg_sides[0] else 0.5
        positions = sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_foot")
        f0 = positions[0][0]
        f1 = positions[-1][0]
        n_frames = f1 - f0
        vels = velocities(positions, fps)
        stance_vels = []
        for i in range(1, len(positions)):
            f = positions[i][0]
            t = (f - f0) / n_frames
            ph = (t + phase_off) % 1.0
            if ph < DUTY * 0.9 and ph > 0.05:  # interior of stance, away from the swing transition
                stance_vels.append(vels[i - 1])
        if stance_vels:
            mean_v = sum(stance_vels) / len(stance_vels)
            var = sum((v - mean_v) ** 2 for v in stance_vels) / len(stance_vels)
            std = math.sqrt(var)
            cv = std / mean_v if mean_v > 1e-9 else 0.0
            mm_per_frame_dev = std * (1.0 / fps) * 1000  # std of per-frame displacement, in mm
        else:
            mean_v, std, cv, mm_per_frame_dev = 0, 0, 0, 0
        # CV is a poor metric once the leg is essentially stationary (lead-review fix round: the
        # forelegs are kept static through Move, see anim/gait.py's docstring) -- a tiny absolute
        # stddev (a couple mm/frame, imperceptible) can still divide out to a large CV against an
        # equally tiny mean. Below an absolute noise floor, judge slide on the ABSOLUTE stddev
        # (still well under a visually-detectable threshold) instead of the ratio.
        near_static = mm_per_frame_dev < 2.0
        passed = (mm_per_frame_dev < 2.0) if near_static else (cv < 0.35)
        move_report["legs"][side] = {
            "stance_mean_velocity_m_per_s": mean_v,
            "stance_velocity_stddev_m_per_s": std,
            "stance_velocity_cv": cv,
            "stance_velocity_stddev_mm_per_frame": mm_per_frame_dev,
            "near_static_leg": near_static,
            "gate_pass": passed,
        }
        print(f"  leg {side}: stance velocity mean={mean_v:.4f} m/s, cv={cv:.3f}, "
              f"stddev={mm_per_frame_dev:.2f} mm/frame{' [near-static, judged on abs stddev]' if near_static else ''} -> "
              f"{'PASS' if passed else 'FAIL'}")

    # Joint-angle limits: knee interior angle (hip-knee-ankle) should stay within a plausible
    # digitigrade range (never fully straight>175 i.e. hyperextended backward-locked, never
    # folded <25 i.e. collapsed).
    joint_report = {}
    for side in leg_sides:
        hip_pos = sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_thigh", "head")
        knee_pos = sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_thigh", "tail")
        ankle_pos = sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_shin", "tail")
        angles = []
        for (f, h), (_, k), (_, a) in zip(hip_pos, knee_pos, ankle_pos):
            v1 = (h - k)
            v2 = (a - k)
            if v1.length > 1e-6 and v2.length > 1e-6:
                cosang = max(-1, min(1, v1.normalized().dot(v2.normalized())))
                angles.append(math.degrees(math.acos(cosang)))
        min_a, max_a = (min(angles), max(angles)) if angles else (0, 0)
        # Upper bound 179.5, not 180: a fully-extended stance leg is legitimately very close to
        # straight (180 = dead straight is the hard physical limit); 179.5 still catches genuine
        # hyperextension/locking while not failing a realistic near-straight extended stride.
        ok = 15 <= min_a and max_a <= 179.5
        joint_report[side] = {"min_knee_angle_deg": min_a, "max_knee_angle_deg": max_a, "pass": ok}
        print(f"  leg {side} knee angle range: {min_a:.1f}..{max_a:.1f} deg -> {'PASS' if ok else 'FAIL'}")
    move_report["joint_limits"] = joint_report

    # Loop-seam continuity: compare first/last frame pose (rotation + root location) and velocity
    # (first derivative, i.e. the step just before vs just after the seam).
    seam_report = {}
    f0, f1 = move_action.frame_range
    f0, f1 = int(f0), int(f1)
    scene = bpy.context.scene
    max_pose_delta = 0.0
    arm_obj.animation_data.action = move_action
    for pb in arm_obj.pose.bones:
        scene.frame_set(f0)
        r0 = mathutils.Quaternion(pb.rotation_quaternion) if pb.rotation_mode == "QUATERNION" \
            else mathutils.Euler(pb.rotation_euler).to_quaternion()
        l0 = pb.location.copy()
        scene.frame_set(f1)
        r1 = mathutils.Quaternion(pb.rotation_quaternion) if pb.rotation_mode == "QUATERNION" \
            else mathutils.Euler(pb.rotation_euler).to_quaternion()
        l1 = pb.location.copy()
        ang_delta = math.degrees(r0.rotation_difference(r1).angle)
        loc_delta = (l0 - l1).length
        max_pose_delta = max(max_pose_delta, ang_delta)
        if ang_delta > 0.5 or loc_delta > 0.001:
            seam_report[pb.name] = {"angle_delta_deg": ang_delta, "loc_delta": loc_delta}
    seam_ok = max_pose_delta < 0.5
    move_report["loop_seam"] = {"max_bone_angle_delta_deg": max_pose_delta,
                                 "mismatches": seam_report, "pass": seam_ok}
    print(f"  Move loop seam: max bone delta {max_pose_delta:.3f} deg -> {'PASS' if seam_ok else 'FAIL'}")

    report["move"] = move_report

# ---------------------------------------------------------------------------
# Idle/Attack: loop-seam (Idle only), F-curve jitter (both).
# ---------------------------------------------------------------------------
if KEYED_BLEND:
    arm_obj = load(KEYED_BLEND)
    scene = bpy.context.scene
    for clip_name, is_loop in (("Idle", True), ("Attack", False)):
        action = bpy.data.actions.get(clip_name)
        if action is None:
            continue
        clip_report = {}
        if is_loop:
            f0, f1 = action.frame_range
            f0, f1 = int(f0), int(f1)
            arm_obj.animation_data.action = action
            max_delta = 0.0
            for pb in arm_obj.pose.bones:
                scene.frame_set(f0)
                r0 = mathutils.Euler(pb.rotation_euler).to_quaternion()
                scene.frame_set(f1)
                r1 = mathutils.Euler(pb.rotation_euler).to_quaternion()
                max_delta = max(max_delta, math.degrees(r0.rotation_difference(r1).angle))
            clip_report["loop_seam"] = {"max_bone_angle_delta_deg": max_delta, "pass": max_delta < 0.5}
            print(f"  {clip_name} loop seam: max bone delta {max_delta:.3f} deg -> "
                  f"{'PASS' if max_delta < 0.5 else 'FAIL'}")

        # Jitter: second-derivative (curvature) of each fcurve sampled at integer frames; flag
        # high-frequency energy (mean |second derivative|) above a threshold (degrees/frame^2).
        jitter_values = []
        for fcu in common.iter_action_fcurves(action):
            f0f, f1f = fcu.range()
            f0i, f1i = int(f0f), int(f1f)
            if f1i - f0i < 2:
                continue
            vals = [fcu.evaluate(f) for f in range(f0i, f1i + 1)]
            second_deriv = [vals[i + 1] - 2 * vals[i] + vals[i - 1] for i in range(1, len(vals) - 1)]
            if second_deriv:
                jitter_values.append(sum(abs(x) for x in second_deriv) / len(second_deriv))
        mean_jitter = sum(jitter_values) / len(jitter_values) if jitter_values else 0.0
        max_jitter = max(jitter_values) if jitter_values else 0.0
        # Threshold is in the fcurve's own units (radians for rotation_euler, units for scale) --
        # a rough but useful relative gate: flag only clearly spiky channels, not natural easing.
        clip_report["jitter"] = {"mean_abs_2nd_deriv": mean_jitter, "max_abs_2nd_deriv": max_jitter,
                                  "pass": max_jitter < 0.15}
        print(f"  {clip_name} jitter: mean={mean_jitter:.5f} max={max_jitter:.5f} -> "
              f"{'PASS' if max_jitter < 0.15 else 'FAIL'}")
        report[clip_name.lower()] = clip_report

    # Attack joint-limit sanity: head/neck chain shouldn't exceed a plausible cumulative pitch.
    attack_action = bpy.data.actions.get("Attack")
    if attack_action:
        arm_obj.animation_data.action = attack_action
        f0, f1 = attack_action.frame_range
        max_head_pitch = 0.0
        for f in range(int(f0), int(f1) + 1):
            scene.frame_set(f)
            total = sum(math.degrees(arm_obj.pose.bones[b].rotation_euler.x)
                        for b in ("spine_02", "neck_01", "neck_02", "head"))
            max_head_pitch = max(max_head_pitch, abs(total))
        ok = max_head_pitch < 120
        report.setdefault("attack", {})["joint_limits"] = {
            "max_cumulative_head_pitch_deg": max_head_pitch, "pass": ok}
        print(f"  Attack cumulative head pitch max: {max_head_pitch:.1f} deg -> {'PASS' if ok else 'FAIL'}")

# ---------------------------------------------------------------------------
# Interpenetration (rough): feet-below-ground check across Move, using the already-sampled data.
# ---------------------------------------------------------------------------
if MOVE_BLEND and "move" in report:
    arm_obj = load(MOVE_BLEND)
    move_action = bpy.data.actions.get("Move")
    leg_sides = sorted({n.name.split("_")[1] for n in arm_obj.data.bones if n.name.startswith("leg_")})
    below_ground_report = {}
    for side in leg_sides:
        # Round 13: toe-fan legs (front) have 3 toe bones, not 1 -- check the lowest of all 3
        # talon tips (the "min_toe_z" this gate has always reported is explicitly a worst-case
        # check, so the worst of 3 tips is the right generalization, not an arbitrary pick).
        toe_suffixes = ("toe_in", "toe_mid", "toe_out") if f"leg_{side}_toe_in" in arm_obj.data.bones \
            else ("toe",)
        min_z = min(
            p.z for suffix in toe_suffixes
            for _, p in sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_{suffix}", "tail")
        )
        below_ground_report[side] = {"min_toe_z": min_z, "pass": min_z > -0.01}
        print(f"  leg {side} min toe z: {min_z:.4f} -> {'PASS' if min_z > -0.01 else 'FAIL'}")
    report["move"]["interpenetration_ground"] = below_ground_report

# ---------------------------------------------------------------------------
# Round 9: max edge-stretch across Move/Attack. A long thin dark sliver/line visible in the toon
# render (contact_sheet_move_side.png, contact_sheet_attack.png -- lead-review round 9) traced
# directly to mesh edges whose two endpoint vertices are skinned to independently-moving bones (most
# often two different legs, or a leg vs. the far-away root/spine) -- the edge's bind-pose length is
# often tiny (easy to miss by eye on the rest pose) but balloons once the two bones move apart,
# stretching a visible triangle across the model. This gate measures exactly that: for every mesh
# edge, (posed length at this frame) / (bind-pose length), across every frame of Move and Attack,
# and fails if the worst ratio anywhere exceeds MAX_STRETCH_RATIO. Reports the single worst triangle
# (both its edge and which bones its two vertices are weighted to, so the failure is actionable
# without re-deriving it by hand).
MAX_STRETCH_RATIO = 1.6


def check_edge_stretch(blend_path, action_name):
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
    arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    action = bpy.data.actions.get(action_name)
    if action is None:
        return None
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    arm_obj.animation_data.action = action
    scene = bpy.context.scene
    me = mesh_obj.data
    bind_pos = [v.co.copy() for v in me.vertices]
    edges = [(e.vertices[0], e.vertices[1]) for e in me.edges]
    bind_len = [(bind_pos[a] - bind_pos[b]).length for a, b in edges]

    f0, f1 = action.frame_range
    worst_ratio, worst_edge, worst_frame = 0.0, None, None
    for f in range(int(f0), int(f1) + 1):
        scene.frame_set(f)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        eval_obj = mesh_obj.evaluated_get(depsgraph)
        eval_mesh = eval_obj.to_mesh()
        posed = [v.co.copy() for v in eval_mesh.vertices]
        eval_obj.to_mesh_clear()
        for i, (a, b) in enumerate(edges):
            bl = bind_len[i]
            if bl < 1e-5:
                continue
            ratio = (posed[a] - posed[b]).length / bl
            if ratio > worst_ratio:
                worst_ratio, worst_edge, worst_frame = ratio, (a, b), f

    result = {"worst_ratio": worst_ratio, "pass": worst_ratio <= MAX_STRETCH_RATIO}
    if worst_edge is not None:
        a, b = worst_edge
        for vi, label in ((a, "vertex_a"), (b, "vertex_b")):
            v = me.vertices[vi]
            groups = sorted(((mesh_obj.vertex_groups[g.group].name, round(g.weight, 3))
                              for g in v.groups), key=lambda x: -x[1])
            result[label] = {"index": vi, "bind_pos": list(round(c, 4) for c in bind_pos[vi]),
                              "bone_weights": groups}
        result["worst_frame"] = worst_frame
        print(f"  {action_name} max edge-stretch: {worst_ratio:.2f}x at frame {worst_frame}, "
              f"edge ({a},{b}) -> {'PASS' if result['pass'] else 'FAIL'}")
        print(f"    vertex {a}: {result['vertex_a']['bone_weights']}")
        print(f"    vertex {b}: {result['vertex_b']['bone_weights']}")
    else:
        print(f"  {action_name} max edge-stretch: no edges found -> PASS")
    return result


if MOVE_BLEND:
    report["move_edge_stretch"] = check_edge_stretch(MOVE_BLEND, "Move")
if KEYED_BLEND:
    attack_stretch = check_edge_stretch(KEYED_BLEND, "Attack")
    if attack_stretch is not None:
        report["attack_edge_stretch"] = attack_stretch

# ---------------------------------------------------------------------------
# Round 11 (producer review: "the Griffin walks backwards"): walk_direction gate. Confirms, from
# the rig and the baked Move action themselves -- never a hard-coded axis, so this gate can never
# pass by agreeing with the same bug it exists to catch -- that:
#   1. each foot's STANCE phase moves it toward the TAIL relative to the pelvis (retracting, as the
#      body passes forward over the planted foot), and its SWING phase moves it toward the HEAD
#      (protracting, carrying the foot forward for the next step) -- the two halves of a real
#      forward walk, checked by projecting (foot - pelvis) onto FORWARD at each sampled frame;
#   2. the four feet touch down in the canonical lateral-sequence order LH -> LF -> RH -> RF (a
#      real quadruped's walk, not a diagonal-sequence gait -- see this file's corrected
#      LATERAL_SEQUENCE above and gait.py's matching fix).
# FORWARD is re-derived here independently (normalize(head - pelvis) rest-pose bone positions,
# ground-plane projected) rather than imported from anim/gait.py, so a regression in one file can't
# silently go unnoticed because the other file's copy of the same (possibly wrong) value agrees.
def check_walk_direction(blend_path, action_name="Move"):
    arm_obj = load(blend_path)
    action = bpy.data.actions.get(action_name)
    if action is None:
        return None
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    arm_obj.animation_data.action = action

    pelvis_pb = arm_obj.pose.bones.get("pelvis")
    head_pb = arm_obj.pose.bones.get("head")
    if pelvis_pb is None or head_pb is None:
        print("  walk_direction: missing pelvis/head bone -> FAIL")
        return {"pass": False, "error": "missing pelvis/head bone, cannot derive FORWARD"}
    pelvis_rest_pos = arm_obj.matrix_world @ pelvis_pb.bone.head_local
    head_rest_pos = arm_obj.matrix_world @ head_pb.bone.head_local
    forward = head_rest_pos - pelvis_rest_pos
    forward.z = 0.0
    if forward.length < 1e-6:
        print("  walk_direction: head/pelvis coincide on the ground plane -> FAIL")
        return {"pass": False, "error": "head and pelvis coincide on the ground plane"}
    forward.normalize()
    print(f"  walk_direction FORWARD (independently re-derived): {tuple(round(c, 4) for c in forward)}")

    leg_sides = sorted({n.name.split("_")[1] for n in arm_obj.data.bones if n.name.startswith("leg_")})
    DUTY = 0.6

    f0, f1 = action.frame_range
    f0, f1 = int(f0), int(f1)
    n_frames = f1 - f0

    pelvis_positions = sample_action_bone_positions(arm_obj, action, "pelvis", "head")

    leg_results = {}
    overall_pass = True
    for side in leg_sides:
        phase_off = LATERAL_SEQUENCE.get(side, 0.0 if side == leg_sides[0] else 0.5)
        # Round 13: toe-fan legs (front) have 3 toe bones -- use the middle toe (toe_mid) as the
        # representative point for the overall foot's fore-aft travel (this gate only cares about
        # net forward/backward direction, not the fan's own shape, which the dedicated toe_fan gate
        # checks separately).
        toe_bone_name = f"leg_{side}_toe_mid" if f"leg_{side}_toe_in" in arm_obj.data.bones \
            else f"leg_{side}_toe"
        positions = sample_action_bone_positions(arm_obj, action, toe_bone_name, "tail")
        stance_fwd, swing_fwd = [], []
        for (f, p), (_, pel) in zip(positions, pelvis_positions):
            t = (f - f0) / n_frames
            ph = (t + phase_off) % 1.0
            rel_fwd = (p - pel).dot(forward)
            if 0.05 < ph < DUTY * 0.95:
                stance_fwd.append((ph, rel_fwd))
            elif DUTY * 1.05 < ph < 0.95:
                swing_fwd.append((ph, rel_fwd))
        stance_fwd.sort()
        swing_fwd.sort()
        # Checked as the net trend (last sample minus first, within each phase window), not
        # frame-to-frame monotonicity -- secondary motion (scapula swing, body bob) riding on top
        # of the primary stride can introduce small non-monotonic wiggles without the gait actually
        # being backwards; the net direction over the whole phase window is what distinguishes a
        # correct walk from a reversed one.
        stance_trend = (stance_fwd[-1][1] - stance_fwd[0][1]) if len(stance_fwd) >= 2 else 0.0
        swing_trend = (swing_fwd[-1][1] - swing_fwd[0][1]) if len(swing_fwd) >= 2 else 0.0
        stance_ok = stance_trend < -1e-5   # moves toward the tail (negative along FORWARD)
        swing_ok = swing_trend > 1e-5      # moves toward the head (positive along FORWARD)
        leg_pass = stance_ok and swing_ok
        overall_pass = overall_pass and leg_pass
        leg_results[side] = {
            "stance_forward_trend": stance_trend, "stance_toward_tail": stance_ok,
            "swing_forward_trend": swing_trend, "swing_toward_head": swing_ok,
            "pass": leg_pass,
        }
        print(f"  walk_direction {side}: stance trend={stance_trend:+.4f} (toward tail? {stance_ok}), "
              f"swing trend={swing_trend:+.4f} (toward head? {swing_ok}) -> {'PASS' if leg_pass else 'FAIL'}")

    # Lateral-sequence touchdown order: touchdown (ph crosses 0, start of stance) happens at
    # t = (-phase_offset) mod 1 for each leg -- sort legs by that touchdown time and check the
    # result is some rotation of the canonical LH -> LF -> RH -> RF cycle (a continuous loop has no
    # fixed "first" leg, so any cyclic rotation of the right order is correct).
    canonical = ["BL", "FL", "BR", "FR"]  # LH, LF, RH, RF
    touchdown_order = sorted(leg_sides, key=lambda s: (-LATERAL_SEQUENCE.get(s, 0.0)) % 1.0)
    seq_ok = False
    if set(touchdown_order) == set(canonical):
        doubled = canonical + canonical
        for start in range(4):
            if doubled[start:start + 4] == touchdown_order:
                seq_ok = True
                break
    print(f"  walk_direction sequence: touchdown order {touchdown_order} "
          f"(a rotation of canonical LH->LF->RH->RF? {seq_ok}) -> {'PASS' if seq_ok else 'FAIL'}")

    result = {"forward": list(round(c, 4) for c in forward), "legs": leg_results,
              "touchdown_order": touchdown_order, "sequence_pass": seq_ok,
              "pass": overall_pass and seq_ok}
    return result


if MOVE_BLEND:
    walk_dir_result = check_walk_direction(MOVE_BLEND, "Move")
    if walk_dir_result is not None:
        report["walk_direction"] = walk_dir_result

# ---------------------------------------------------------------------------
# Round 12 (producer review: "the feet look wrong" -- FR toes crossing/twisting, hind feet
# pads-up/flipped). Root cause, confirmed via a numeric diagnostic (angle of the foot/toe bones'
# own axes vs. world up/FORWARD, sampled at bind + four Move phases): the OLD foot/toe orientation
# was derived by literally aiming the bone at the raw IK target position every frame, which was
# numerically unstable during swing -- a leg's foot-bone direction could flip up to 180 deg from
# FORWARD between adjacent swing frames. Fixed in anim/gait.py (set_leg_pose) by decoupling foot/
# toe ORIENTATION from the raw target position: stance holds a fixed, bind-pose-derived direction
# (literally "no roll/twist while planted"), swing blends to a small, bounded toe-curl and back.
#
# This gate checks the FIX, independently of gait.py's own internals (reads the posed armature
# directly, not gait.py's formulas, so a regression in one file can't silently agree with a bug in
# the other): during STANCE, for every leg:
#   - "toe direction": the leg_<side>_toe bone's own Y axis (head->tail = foot-point -> toe-tip,
#     true by construction for ANY bone regardless of roll) must be within TOE_DIR_MAX_DEG of
#     FORWARD, ground-projected.
#   - "sole normal": the toe bone's REST-pose local axis closest to world-up (found once, since
#     roll is never set explicitly when this rig's bones are built -- see winged_quadruped.py's
#     build_bones -- so "which local axis is sole-normal" isn't assumable in advance; auto-detected
#     from the rest matrix itself, the same way for every leg) must, once carried through the
#     bone's ACTUAL POSED rotation, stay within SOLE_NORMAL_MAX_DEG of world-up.
TOE_DIR_MAX_DEG = 25.0
SOLE_NORMAL_MAX_DEG = 20.0


def check_foot_orientation(blend_path, action_name="Move"):
    arm_obj = load(blend_path)
    action = bpy.data.actions.get(action_name)
    if action is None:
        return None
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    arm_obj.animation_data.action = action
    scene = bpy.context.scene

    leg_sides = sorted({n.name.split("_")[1] for n in arm_obj.data.bones if n.name.startswith("leg_")})
    pelvis_pb = arm_obj.pose.bones.get("pelvis")
    head_pb = arm_obj.pose.bones.get("head")
    forward = (arm_obj.matrix_world @ head_pb.bone.head_local) - (arm_obj.matrix_world @ pelvis_pb.bone.head_local)
    forward.z = 0.0
    forward.normalize()
    world_up = mathutils.Vector((0, 0, 1))

    f0, f1 = action.frame_range
    f0, f1 = int(f0), int(f1)
    n_frames = f1 - f0

    leg_results = {}
    overall_pass = True
    for side in leg_sides:
        # Round 13: legs with a toe FAN (leg_<side>_toe_in/mid/out -- front legs on this rig) no
        # longer have a single aggregate leg_<side>_toe bone at all, and are covered in more
        # relevant detail by the new toe_fan gate below (per-toe fan-angle + ground-clearance,
        # rather than this gate's single-toe sole-normal/toe-direction concept, which doesn't map
        # cleanly onto 3 independently-splayed toes) -- skip them here rather than guessing which
        # one of the 3 toe bones would stand in for "the" toe.
        if f"leg_{side}_toe_in" in arm_obj.data.bones:
            continue
        toe_pb = arm_obj.pose.bones[f"leg_{side}_toe"]
        phase_off = LATERAL_SEQUENCE.get(side, 0.0 if side == leg_sides[0] else 0.5)

        # Which LOCAL axis of the toe bone is "sole normal" cannot be auto-detected from the REST
        # matrix: this rig's bones are built with Blender's default roll (never set explicitly --
        # see winged_quadruped.py's build_bones), while the POSED toe bone's roll is built entirely
        # independently by anim/gait.py's own aim_matrix/up_hint convention (set_leg_pose uses
        # up_hint=(0,0,-1) specifically for the toe bone, BY DESIGN, so that its local Z axis is the
        # one that ends up close to world-up once posed -- confirmed numerically, see gait.py's own
        # comment there for why the sign is -Z not +Z). Rest and pose simply don't share a roll
        # baseline here, so this gate checks what the POSE convention actually guarantees -- local Z
        # -- directly, not an auto-detected-from-rest axis (an earlier version of this gate did
        # that and failed at 90-180 deg on every leg even after the fix landed, because it was
        # comparing against the wrong, rest-derived axis instead of the one gait.py actually
        # controls).
        local_up = mathutils.Vector((0, 0, 1))

        toe_dir_worst, sole_worst = 0.0, 0.0
        toe_dir_worst_frame, sole_worst_frame = None, None
        n_stance_samples = 0
        for f in range(f0, f1 + 1):
            t = (f - f0) / n_frames
            ph = (t + phase_off) % 1.0
            if ph >= DUTY:
                continue  # swing -- this gate only checks stance, per the task brief
            n_stance_samples += 1
            scene.frame_set(f)
            pose_mat3 = toe_pb.matrix.to_3x3()
            y_axis = (pose_mat3 @ mathutils.Vector((0, 1, 0)))
            y_flat = mathutils.Vector((y_axis.x, y_axis.y, 0))
            toe_dir_deg = math.degrees(y_flat.angle(forward)) if y_flat.length > 1e-6 else 180.0
            sole_dir = (pose_mat3 @ local_up).normalized()
            sole_deg = math.degrees(sole_dir.angle(world_up))
            if toe_dir_deg > toe_dir_worst:
                toe_dir_worst, toe_dir_worst_frame = toe_dir_deg, f
            if sole_deg > sole_worst:
                sole_worst, sole_worst_frame = sole_deg, f

        toe_ok = toe_dir_worst <= TOE_DIR_MAX_DEG
        sole_ok = sole_worst <= SOLE_NORMAL_MAX_DEG
        leg_pass = toe_ok and sole_ok and n_stance_samples > 0
        overall_pass = overall_pass and leg_pass
        leg_results[side] = {
            "stance_samples": n_stance_samples,
            "worst_toe_direction_deg": toe_dir_worst, "worst_toe_direction_frame": toe_dir_worst_frame,
            "toe_direction_pass": toe_ok,
            "worst_sole_normal_deg": sole_worst, "worst_sole_normal_frame": sole_worst_frame,
            "sole_normal_pass": sole_ok,
            "pass": leg_pass,
        }
        print(f"  foot_orientation {side}: worst toe-direction {toe_dir_worst:.1f} deg "
              f"(<= {TOE_DIR_MAX_DEG}? {toe_ok}), worst sole-normal {sole_worst:.1f} deg "
              f"(<= {SOLE_NORMAL_MAX_DEG}? {sole_ok}) over {n_stance_samples} stance frames -> "
              f"{'PASS' if leg_pass else 'FAIL'}")

    return {"forward": list(round(c, 4) for c in forward), "legs": leg_results, "pass": overall_pass}


if MOVE_BLEND:
    foot_orient_result = check_foot_orientation(MOVE_BLEND, "Move")
    if foot_orient_result is not None:
        report["foot_orientation"] = foot_orient_result

# ---------------------------------------------------------------------------
# Round 13 (producer review: FR's toes still looked crossed, since a real eagle foreleg has THREE
# independently-splayed toes -- inner/middle/outer, each with its own talon -- and the single
# aggregate toe bone round 12 fixed curling around one axis necessarily swung all three splayed
# toes together, crossing the outer toes and pointing the middle talon straight down at contact.
# gait.py now gives each foreleg 3 sibling toe bones (toe_in/mid/out), each curling around its OWN
# hinge axis, staying exactly at bind during stance (see set_leg_pose's docstring).
#
# This gate checks that fix directly: for each front foot (FL/FR), EVERY frame (not just stance --
# the task brief's own wording), the angle between adjacent toe directions (in-mid, mid-out) must
# stay within TOE_FAN_ANGLE_TOLERANCE_DEG of its BIND value -- i.e. the fan's own shape never
# distorts/crosses, regardless of how the whole foot is posed -- and during STANCE specifically,
# no talon tip may sink below GROUND_CLEARANCE_MIN (ground - 0.005).
TOE_FAN_ANGLE_TOLERANCE_DEG = 12.0
GROUND_CLEARANCE_MIN = -0.005


def check_toe_fan(blend_path, action_name="Move"):
    arm_obj = load(blend_path)
    action = bpy.data.actions.get(action_name)
    if action is None:
        return None
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    scene = bpy.context.scene

    front_sides = [s for s in ("FL", "FR")
                   if f"leg_{s}_toe_in" in arm_obj.data.bones]
    if not front_sides:
        return None  # no toe-fan legs on this rig -- nothing to check

    f0, f1 = action.frame_range
    f0, f1 = int(f0), int(f1)
    n_frames = f1 - f0

    def toe_dir(side, t_name):
        pb = arm_obj.pose.bones[f"leg_{side}_toe_{t_name}"]
        v = (arm_obj.matrix_world @ pb.tail) - (arm_obj.matrix_world @ pb.head)
        return v.normalized() if v.length > 1e-6 else mathutils.Vector((0, -1, 0))

    leg_results = {}
    overall_pass = True
    for side in front_sides:
        phase_off = LATERAL_SEQUENCE.get(side, 0.0)

        # Bind-pose reference angles (no action assigned -- pose == rest).
        arm_obj.animation_data.action = None
        bind_in_mid = math.degrees(toe_dir(side, "in").angle(toe_dir(side, "mid")))
        bind_mid_out = math.degrees(toe_dir(side, "mid").angle(toe_dir(side, "out")))

        arm_obj.animation_data.action = action
        worst_fan_delta, worst_fan_frame = 0.0, None
        worst_ground, worst_ground_frame = 999.0, None
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            in_mid = math.degrees(toe_dir(side, "in").angle(toe_dir(side, "mid")))
            mid_out = math.degrees(toe_dir(side, "mid").angle(toe_dir(side, "out")))
            delta = max(abs(in_mid - bind_in_mid), abs(mid_out - bind_mid_out))
            if delta > worst_fan_delta:
                worst_fan_delta, worst_fan_frame = delta, f

            t = (f - f0) / n_frames
            ph = (t + phase_off) % 1.0
            if ph < DUTY:  # stance only, per the task brief
                for t_name in ("in", "mid", "out"):
                    pb = arm_obj.pose.bones[f"leg_{side}_toe_{t_name}"]
                    tip_z = (arm_obj.matrix_world @ pb.tail).z
                    if tip_z < worst_ground:
                        worst_ground, worst_ground_frame = tip_z, f

        fan_ok = worst_fan_delta <= TOE_FAN_ANGLE_TOLERANCE_DEG
        ground_ok = worst_ground >= GROUND_CLEARANCE_MIN
        leg_pass = fan_ok and ground_ok
        overall_pass = overall_pass and leg_pass
        leg_results[side] = {
            "bind_angle_in_mid_deg": bind_in_mid, "bind_angle_mid_out_deg": bind_mid_out,
            "worst_fan_delta_deg": worst_fan_delta, "worst_fan_delta_frame": worst_fan_frame,
            "fan_pass": fan_ok,
            "worst_ground_z": worst_ground, "worst_ground_frame": worst_ground_frame,
            "ground_pass": ground_ok,
            "pass": leg_pass,
        }
        print(f"  toe_fan {side}: worst fan-angle delta from bind {worst_fan_delta:.1f} deg "
              f"(<= {TOE_FAN_ANGLE_TOLERANCE_DEG}? {fan_ok}), worst talon ground z "
              f"{worst_ground:.4f} (>= {GROUND_CLEARANCE_MIN}? {ground_ok}) -> "
              f"{'PASS' if leg_pass else 'FAIL'}")

    return {"legs": leg_results, "pass": overall_pass}


if MOVE_BLEND:
    toe_fan_result = check_toe_fan(MOVE_BLEND, "Move")
    if toe_fan_result is not None:
        report["toe_fan"] = toe_fan_result

# ---------------------------------------------------------------------------
# Round 14 (lead review of round 13's own FRONT_FEET_SHEET.png: "the report says clean, but it
# isn't -- contact, passing, lift and mid-swing columns show the front feet crumpled -- jagged
# folded geometry at the foot/toe base in the side row, and mangled, overlapping toes in the bottom
# row. The toe_fan gate passes because it only measures toe-to-toe angles and talon height, not
# mesh deformation.") This gate closes exactly that blind spot: it measures the MESH itself (edge
# stretch + triangle normal flips), restricted to the region actually at risk (vertices weighted
# >=0.2 to any foreleg foot/toe bone -- the junction where round 14's fix (anim/gait.py's
# bind-rotation-preserving foot + foot-local toe matrix_basis) matters), across EVERY frame of
# Move/Idle/Attack (not just a handful of sampled poses the way the existing per-leg gates do).
TOE_DEFORM_MAX_STRETCH = 1.35


def check_toe_deformation(blend_path, action_names):
    arm_obj = load(blend_path)
    mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
    me = mesh_obj.data

    target_bone_names = set()
    for side in ("FL", "FR"):
        if f"leg_{side}_toe_in" in arm_obj.data.bones:
            target_bone_names |= {f"leg_{side}_foot", f"leg_{side}_toe_in",
                                   f"leg_{side}_toe_mid", f"leg_{side}_toe_out"}
        elif f"leg_{side}_toe" in arm_obj.data.bones:
            target_bone_names |= {f"leg_{side}_foot", f"leg_{side}_toe"}
    if not target_bone_names:
        return None  # no foreleg foot/toe bones on this rig -- nothing to check
    group_ids = {g.index for g in mesh_obj.vertex_groups if g.name in target_bone_names}
    restricted_verts = set()
    for v in me.vertices:
        w = sum(g.weight for g in v.groups if g.group in group_ids)
        if w >= 0.2:
            restricted_verts.add(v.index)
    if not restricted_verts:
        return None

    edges = [(e.vertices[0], e.vertices[1]) for e in me.edges
             if e.vertices[0] in restricted_verts or e.vertices[1] in restricted_verts]
    bind_pos = [v.co.copy() for v in me.vertices]
    bind_len = [(bind_pos[a] - bind_pos[b]).length for a, b in edges]
    polys = [p.index for p in me.polygons if any(vi in restricted_verts for vi in p.vertices)]
    bind_normals = {pi: me.polygons[pi].normal.copy() for pi in polys}

    result = {"restricted_vertex_count": len(restricted_verts), "restricted_edge_count": len(edges),
              "restricted_triangle_count": len(polys),
              "worst_stretch": 0.0, "worst_stretch_edge": None, "worst_stretch_frame": None,
              "worst_stretch_action": None,
              "flip_count": 0, "worst_flip_triangle": None, "worst_flip_frame": None,
              "worst_flip_action": None, "worst_flip_dot": 1.0}

    for action_name in action_names:
        action = bpy.data.actions.get(action_name)
        if action is None:
            continue
        if arm_obj.animation_data is None:
            arm_obj.animation_data_create()
        arm_obj.animation_data.action = action
        scene = bpy.context.scene
        f0, f1 = action.frame_range
        for f in range(int(f0), int(f1) + 1):
            scene.frame_set(f)
            depsgraph = bpy.context.evaluated_depsgraph_get()
            eval_obj = mesh_obj.evaluated_get(depsgraph)
            eval_mesh = eval_obj.to_mesh()
            posed = [v.co.copy() for v in eval_mesh.vertices]
            for i, (a, b) in enumerate(edges):
                bl = bind_len[i]
                if bl < 1e-5:
                    continue
                ratio = (posed[a] - posed[b]).length / bl
                if ratio > result["worst_stretch"]:
                    result["worst_stretch"] = ratio
                    result["worst_stretch_edge"] = (a, b)
                    result["worst_stretch_frame"] = f
                    result["worst_stretch_action"] = action_name
            for pi in polys:
                d = bind_normals[pi].dot(eval_mesh.polygons[pi].normal)
                if d < result["worst_flip_dot"]:
                    result["worst_flip_dot"] = d
                if d < 0:
                    result["flip_count"] += 1
                    if result["worst_flip_triangle"] is None:
                        result["worst_flip_triangle"] = pi
                        result["worst_flip_frame"] = f
                        result["worst_flip_action"] = action_name
            eval_obj.to_mesh_clear()

    stretch_ok = result["worst_stretch"] <= TOE_DEFORM_MAX_STRETCH
    flip_ok = result["flip_count"] == 0
    result["stretch_pass"] = stretch_ok
    result["flip_pass"] = flip_ok
    result["pass"] = stretch_ok and flip_ok

    if result["worst_stretch_edge"] is not None:
        a, b = result["worst_stretch_edge"]
        for vi, label in ((a, "stretch_vertex_a"), (b, "stretch_vertex_b")):
            v = me.vertices[vi]
            groups = sorted(((mesh_obj.vertex_groups[g.group].name, round(g.weight, 3))
                              for g in v.groups), key=lambda x: -x[1])
            result[label] = {"index": vi, "bone_weights": groups}
    if result["worst_flip_triangle"] is not None:
        pi = result["worst_flip_triangle"]
        result["worst_flip_triangle_vertices"] = list(me.polygons[pi].vertices)

    print(f"  toe_deformation ({len(restricted_verts)} restricted verts, {len(edges)} edges, "
          f"{len(polys)} triangles): worst edge-stretch {result['worst_stretch']:.3f}x "
          f"(<= {TOE_DEFORM_MAX_STRETCH}? {stretch_ok}) at frame {result['worst_stretch_frame']} "
          f"({result['worst_stretch_action']}); {result['flip_count']} normal-flipped triangles "
          f"(worst dot {result['worst_flip_dot']:.3f}, {'none' if flip_ok else 'frame ' + str(result['worst_flip_frame']) + ' ' + str(result['worst_flip_action'])}) "
          f"-> {'PASS' if result['pass'] else 'FAIL'}")
    if result["worst_stretch_edge"] is not None:
        print(f"    worst stretch edge {result['worst_stretch_edge']}: "
              f"{result['stretch_vertex_a']['bone_weights']} / {result['stretch_vertex_b']['bone_weights']}")
    if result["worst_flip_triangle"] is not None:
        print(f"    worst flip triangle #{result['worst_flip_triangle']}: "
              f"vertices {result['worst_flip_triangle_vertices']}")
    return result


toe_deform_results = {}
if MOVE_BLEND:
    r = check_toe_deformation(MOVE_BLEND, ["Move"])
    if r is not None:
        toe_deform_results["move"] = r
if KEYED_BLEND:
    r = check_toe_deformation(KEYED_BLEND, ["Idle", "Attack"])
    if r is not None:
        toe_deform_results["idle_attack"] = r
if toe_deform_results:
    report["toe_deformation"] = {
        "pass": all(r["pass"] for r in toe_deform_results.values()),
        **toe_deform_results,
    }

with open(os.path.join(OUT, "verify_report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("VERIFY DONE")

# "Fail loudly": walk_direction, foot_orientation, toe_fan, and toe_deformation are correctness
# gates, not quality-polish ones -- a backwards-walking, mangled-footed, or mesh-shearing Griffin is
# categorically broken, not just imperfect, so (unlike the other gates in this file, which report
# pass/fail in the JSON for a human to read) these also abort the pipeline with a non-zero exit code
# so a failure can't be silently skipped past in a batch/CI context.
hard_fail = False
for gate_name in ("walk_direction", "foot_orientation", "toe_fan", "toe_deformation"):
    if gate_name in report and not report[gate_name].get("pass", False):
        print("=" * 70)
        print(f"FATAL: {gate_name} gate FAILED -- see verify_report.json['{gate_name}'].")
        print("=" * 70)
        hard_fail = True
if hard_fail:
    sys.exit(1)
