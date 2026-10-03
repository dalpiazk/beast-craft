"""Tooling/Animation stage 3b: hand-keyed pose-to-pose clips ("Idle", "Attack").

Builds on the rigged (bind-pose) Griffin, not gait.py's output -- Idle/Attack are independent,
static-footed clips (no locomotion IK), authored as sparse key poses (3-6 per the task brief) with
Bezier EASE_IN_OUT interpolation, applying the methodology doc's codeable principle rules directly:
anticipation (a counter-rotation pose before the strike), fast strike / slower recover, follow-
through via per-chain settle offsets, and a bounded squash/stretch on the Attack's contact frame.

Run headless:
  blender -b --python keyed.py -- --blend RIGGED.blend --out OUTDIR [--fps 24]
"""
import bpy
import sys
import os
import math

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common

args = common.parse_args(common.get_argv())
BLEND = args["blend"]
OUT = args["out"]
FPS = int(args.get("fps", 24))
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
scene.render.fps = FPS

arm_obj = next(o for o in bpy.data.objects if o.type == "ARMATURE")
all_bones = [b.name for b in arm_obj.data.bones]
leg_sides = sorted({n.split("_")[1] for n in all_bones if n.startswith("leg_")})

bpy.context.view_layer.objects.active = arm_obj
bpy.ops.object.mode_set(mode="POSE")
for pb in arm_obj.pose.bones:
    pb.rotation_mode = "XYZ"  # every bone consistently Euler for these two hand-keyed clips


def apply_pose(pose_dict):
    """pose_dict: {bone_name: (rx,ry,rz)} in degrees, additive offsets from rest. Bones not
    listed are left at whatever they currently are (callers pass a full dict per key pose, built
    from a `rest()` base dict, so every key pose is fully specified -- no implicit carry-over)."""
    for name, rot in pose_dict.items():
        pb = arm_obj.pose.bones.get(name)
        if pb is None:
            continue
        pb.rotation_euler = (math.radians(rot[0]), math.radians(rot[1]), math.radians(rot[2]))


def apply_scale(scale_dict):
    for name, s in scale_dict.items():
        pb = arm_obj.pose.bones.get(name)
        if pb is not None:
            pb.scale = s


def keyframe_pose(frame, bones):
    # IMPORTANT: scene.frame_set() must happen BEFORE apply_pose()/apply_scale(), not after --
    # frame_set() re-evaluates the armature's already-keyed F-curves onto the pose bones, which
    # would silently stomp whatever apply_pose() just set if called afterward (confirmed: a first
    # pass called frame_set() here, after apply_pose(), and every key pose after the first came
    # out identical to the first -- each frame_set() snapped the pose back to the existing keys'
    # extrapolated value right before keyframe_insert() read it). Every call site below already
    # calls scene.frame_set(f) itself, immediately before apply_pose()/apply_scale(), so this
    # function only inserts the keys -- it does not re-set the frame.
    for name in bones:
        pb = arm_obj.pose.bones.get(name)
        if pb is None:
            continue
        pb.keyframe_insert(data_path="rotation_euler", frame=frame)
        pb.keyframe_insert(data_path="scale", frame=frame)


ALL_POSE_BONES = [b for b in all_bones if b not in ("root",)]


def rest_pose():
    return {b: (0, 0, 0) for b in ALL_POSE_BONES}


def reset_all():
    for name in ALL_POSE_BONES:
        pb = arm_obj.pose.bones.get(name)
        if pb:
            pb.rotation_euler = (0, 0, 0)
            pb.scale = (1, 1, 1)


# ---------------------------------------------------------------------------
# Idle: 3s loop, breathing / weight shift / wing settle / head look / tail sway -- all subtle.
# Sparse key poses (5, incl. the loop-closing repeat of pose 0) with Bezier ease.
# ---------------------------------------------------------------------------
IDLE_SECONDS = 3.0
IDLE_FRAMES = int(round(IDLE_SECONDS * FPS))
idle_action = bpy.data.actions.new("Idle")
idle_action.use_fake_user = True  # keeps a user ref after .action is reassigned, so it survives
# the file save below (orphan actions with 0 users get purged on save -- this bit us in a first
# pass: both actions vanished from the saved .blend).
arm_obj.animation_data_create()
arm_obj.animation_data.action = idle_action

idle_key_fractions = [0.0, 0.22, 0.5, 0.72, 1.0]


def idle_pose(frac):
    p = rest_pose()
    settle = math.sin(frac * 2 * math.pi)
    p["spine_02"] = (1.5 * settle, 0, 0)          # breathing: subtle chest rotation
    p["spine_01"] = (0.8 * settle, 0, 0)
    p["neck_01"] = (0, 3 * math.sin(frac * 2 * math.pi + 1.1), 0)   # slow head-look
    p["neck_02"] = (0, 2 * math.sin(frac * 2 * math.pi + 1.1), 0)
    p["head"] = (1.0 * settle, 2 * math.sin(frac * 2 * math.pi + 1.1), 0)
    p["wing_L_01"] = (1.5 * settle, 0, -4 - 1.5 * settle)
    p["wing_R_01"] = (1.5 * settle, 0, 4 + 1.5 * settle)
    p["tail_01"] = (0, 0, 4 * math.sin(frac * 2 * math.pi + 0.4))
    p["tail_02"] = (0, 0, 5 * math.sin(frac * 2 * math.pi + 0.7))
    p["tail_03"] = (0, 0, 4 * math.sin(frac * 2 * math.pi + 1.0))
    # Weight shift: tiny alternating hip-side lean via the thighs (feet stay planted -- a weight
    # shift, not a step).
    if leg_sides:
        p[f"leg_{leg_sides[0]}_thigh"] = (0, 0, 1.2 * settle)
        if len(leg_sides) > 1:
            p[f"leg_{leg_sides[1]}_thigh"] = (0, 0, -1.2 * settle)
    return p


for frac in idle_key_fractions:
    f = 1 + round(frac * IDLE_FRAMES)
    scene.frame_set(f)
    apply_pose(idle_pose(frac))
    keyframe_pose(f, ALL_POSE_BONES)

n = common.set_interpolation(idle_action, "BEZIER", "EASE_IN_OUT")
print(f"IDLE ACTION: {len(idle_key_fractions)} key poses over {IDLE_FRAMES} frames @ {FPS}fps, "
      f"{n} fcurves")

# ---------------------------------------------------------------------------
# Attack: anticipation -> strike -> follow-through -> recover, ~1.0s, fast strike / slower recover
# per the methodology doc's timing table. Squash/stretch on the contact (strike) frame.
# ---------------------------------------------------------------------------
reset_all()
ATTACK_SECONDS = 1.0
ATTACK_FRAMES = int(round(ATTACK_SECONDS * FPS))
attack_action = bpy.data.actions.new("Attack")
attack_action.use_fake_user = True
arm_obj.animation_data.action = attack_action

# (frame_fraction, label) -- strike is a short, fast gap from anticipation (principle: impact
# frames read best when abrupt), recover is the longest phase.
ATTACK_KEYS = [0.0, 0.28, 0.40, 0.62, 1.0]
ATTACK_LABELS = ["neutral", "anticipation", "strike", "follow_through", "recover"]


def attack_pose(label):
    p = rest_pose()
    if label == "neutral":
        pass
    elif label == "anticipation":
        # Counter-rotation opposite the strike direction: pull head/neck BACK and UP, compress
        # chest, wings pulled in tight, tail coils back -- the codeable anticipation rule.
        p["spine_02"] = (-6, 0, 0)
        p["neck_01"] = (-8, 0, 0)
        p["neck_02"] = (-10, 0, 0)
        p["head"] = (-14, 0, 0)
        p["wing_L_01"] = (-4, 0, -14)
        p["wing_L_02"] = (0, 0, -8)
        p["wing_R_01"] = (-4, 0, 14)
        p["wing_R_02"] = (0, 0, 8)
        p["tail_01"] = (10, 0, 0)
        p["tail_02"] = (14, 0, 0)
    elif label == "strike":
        # Fast snap forward/down -- the beak strike extreme. Wings flare for balance. Tuned down
        # from a first pass (spine+neck+head summed to ~98 degrees of forward pitch, which curled
        # the head entirely behind the wing/body silhouette in the render -- a first-fix-round
        # correction per the task's silhouette-readability check, not just a numbers tweak.)
        p["spine_02"] = (8, 0, 0)
        p["neck_01"] = (12, 0, 0)
        p["neck_02"] = (16, 0, 0)
        p["head"] = (18, 0, 0)
        p["wing_L_01"] = (10, 0, 22)
        p["wing_L_02"] = (0, 0, 14)
        p["wing_R_01"] = (10, 0, -22)
        p["wing_R_02"] = (0, 0, -14)
        p["tail_01"] = (-14, 0, 0)
        p["tail_02"] = (-18, 0, 0)
    elif label == "follow_through":
        # Slight overshoot past the strike extreme, wings pushing back for balance recovery.
        p["spine_02"] = (10, 0, 0)
        p["neck_01"] = (16, 0, 0)
        p["neck_02"] = (18, 0, 0)
        p["head"] = (20, 0, 0)
        p["wing_L_01"] = (6, 0, 10)
        p["wing_R_01"] = (6, 0, -10)
        p["tail_01"] = (-8, 0, 0)
        p["tail_02"] = (-10, 0, 0)
    elif label == "recover":
        pass  # back to neutral, ready to loop into Idle
    return p


def attack_scale(label):
    s = {}
    if label == "strike":
        # Bounded squash/stretch on the contact frame (methodology doc's numeric clamp: ~0.85-0.95
        # vertical / 1.05-1.15 horizontal), applied to the chest, reverting by follow-through.
        s["spine_02"] = (1.08, 1.08, 0.90)
    return s


for frac, label in zip(ATTACK_KEYS, ATTACK_LABELS):
    f = 1 + round(frac * ATTACK_FRAMES)
    scene.frame_set(f)
    apply_pose(attack_pose(label))
    apply_scale(attack_scale(label))
    keyframe_pose(f, ALL_POSE_BONES)

n2 = common.set_interpolation(attack_action, "BEZIER", "EASE_IN_OUT")
# Sharpen the strike's IN edge specifically (anticipation -> strike should read as fast/abrupt,
# not eased both ways like the rest of the clip) by setting that one keyframe's left handle to
# EASE_IN only (right handle stays EASE_IN_OUT for the strike->follow_through release).
strike_frame = 1 + round(ATTACK_KEYS[2] * ATTACK_FRAMES)
for fcu in common.iter_action_fcurves(attack_action):
    for kp in fcu.keyframe_points:
        if round(kp.co[0]) == strike_frame:
            kp.easing = "EASE_IN"
print(f"ATTACK ACTION: {len(ATTACK_KEYS)} key poses ({', '.join(ATTACK_LABELS)}) over "
      f"{ATTACK_FRAMES} frames @ {FPS}fps, {n2} fcurves, strike frame={strike_frame}")

reset_all()
arm_obj.animation_data.action = None
bpy.ops.object.mode_set(mode="OBJECT")

blend_out = os.path.join(OUT, "griffin_keyed.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print(f"SAVED {blend_out}")
print("KEYED DONE")
