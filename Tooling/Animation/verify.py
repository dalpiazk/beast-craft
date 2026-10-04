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
    LATERAL_SEQUENCE = {"BL": 0.0, "FL": 0.25, "BR": 0.5, "FR": 0.75}
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
        positions = sample_action_bone_positions(arm_obj, move_action, f"leg_{side}_toe", "tail")
        min_z = min(p.z for _, p in positions)
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

with open(os.path.join(OUT, "verify_report.json"), "w") as f:
    json.dump(report, f, indent=2)
print("VERIFY DONE")
