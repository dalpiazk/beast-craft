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
import json

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

# Round 10: this mesh's bind pose has the beak open (a jaw bone was added specifically so this
# clip set could close it -- see HAND_LANDMARKS_NATIVE's jaw_tip comment in rig_templates/
# winged_quadruped.py for why its tip position is an estimate, not a hand-measured landmark).
# Tuned by rendering (not guessed and left untested): the jaw bone's local Y axis points from the
# skull down to the estimated lower-mandible tip, so a NEGATIVE local-X (pitch) rotation swings
# that tip up toward the fixed upper beak, closing the gap.
JAW_CLOSED_DEG = -32.0
JAW_WIDE_OPEN_DEG = 14.0  # extra beyond the open bind pose, for Attack's anticipation


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
    # Round 10: this mesh's bind pose has the beak open (see HAND_LANDMARKS_NATIVE's jaw_tip
    # comment) -- the lead asked for Idle to close it. JAW_CLOSED_DEG (defined below, near the
    # Attack section, since both clips share it) rotates the jaw bone from its open rest pose up to
    # a closed one; a small settle-synced wobble on top reads as a faint idle mouth/jaw movement
    # rather than a perfectly rigid closed beak.
    p["jaw"] = (JAW_CLOSED_DEG + 1.5 * settle, 0, 0)
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
    # Round 10: "beak snap" -- the beak starts closed (matching Idle's rest state, for a clean
    # loop in/out of Idle), opens wide on the anticipation wind-up, then SNAPS shut fast on the
    # strike frame (paired with the foreleg talon rake) and stays shut through follow-through/
    # recover.
    if label == "neutral":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "anticipation":
        # Counter-rotation opposite the strike direction: pull head/neck BACK and UP, compress
        # chest, wings pulled in tight, tail coils back -- the codeable anticipation rule. The
        # raking foreleg (FR) lifts and draws back, winding up for the rake -- hind legs (BL/BR)
        # and the other foreleg (FL) stay at rest (0,0,0, i.e. braced) throughout the whole clip.
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
        p["leg_FR_thigh"] = (-16, 0, 6)
        p["leg_FR_shin"] = (10, 0, 0)
        p["jaw"] = (JAW_WIDE_OPEN_DEG, 0, 0)
    elif label == "strike":
        # Fast snap forward/down -- the beak strike extreme. Wings flare for balance. Tuned down
        # from a first pass (spine+neck+head summed to ~98 degrees of forward pitch, which curled
        # the head entirely behind the wing/body silhouette in the render -- a first-fix-round
        # correction per the task's silhouette-readability check, not just a numbers tweak.)
        # Foreleg rake: FR swings forward and down past the wind-up, as if raking a talon across --
        # kept to a modest angle (this leg's weighting is fragile at larger articulation, see
        # anim/gait.py's docstring on why Move keeps forelegs static) rather than a dramatic swipe.
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
        p["leg_FR_thigh"] = (22, 0, -8)
        p["leg_FR_shin"] = (-14, 0, 0)
        p["jaw"] = (JAW_CLOSED_DEG - 4, 0, 0)  # slight overshoot past fully-closed -- the "snap"
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
        p["leg_FR_thigh"] = (14, 0, -4)
        p["leg_FR_shin"] = (-8, 0, 0)
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "recover":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)  # back to neutral (foreleg lowered back to braced rest,
        # beak shut), ready to loop into Idle
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

# ---------------------------------------------------------------------------
# Round 15 (producer-approved v14; next round -- four new battle clips: Cast, Hit, KO, Victory).
# Same principles as Idle/Attack above: sparse hand-keyed poses, Bezier EASE_IN_OUT, anticipation/
# follow-through/arcs, legs stay planted (no leg-bone rotation) unless the clip specifically needs
# otherwise (Cast/Victory rear up, KO collapses -- all three touch leg bones deliberately, same FK-
# only approach Attack's foreleg rake already established: no ground-contact verification, since
# this file has never driven locomotion IK -- see its own docstring). Toe bones are never touched
# directly in any clip here, in any of the 6 hand-keyed clips old or new -- they stay at local
# rest/identity, which IS "following the foot in local space," the same correct-by-construction
# behaviour round 14 established for gait.py's FK-driven toe fan.
#
# EVENT MARKERS: per the task brief, exported alongside the GLB (a sidecar JSON here, not glTF
# extras -- both are explicitly allowed; the sidecar is simpler and needs no custom exporter
# plumbing) and logged by Live3D when the pilot sequence crosses that frame during playback.
EVENT_MARKERS = {}  # clip_name -> [{"name": str, "frame": int, "fraction": float}, ...]


def add_marker(clip_name, name, frame, fraction):
    EVENT_MARKERS.setdefault(clip_name, []).append(
        {"name": name, "frame": frame, "fraction": fraction})


# CLIP LOOP FLAGS (round 16, producer review): whether each clip is meant to be sampled with time
# WRAPPING (a seamless loop -- Idle/Move/Victory, each explicitly authored and loop-seam-gated to
# return to their own start) or CLAMPED to its last keyframe (a one-shot action -- Attack/Hit/Cast/KO,
# none of them loop-seam-gated, each meant to hold its final pose, not snap back to frame 0). Exported
# into the events sidecar below so Live3D's sampler (AnimatedPose.ComputeWorldMatricesBlended) can
# tell the two apart instead of always wrapping -- root cause of the lead's "KO's last contact-sheet
# frame snaps back upright" finding: sampling a non-looping clip at exactly t=duration previously hit
# Wrap(duration, duration) == 0 (floating-point modulo), silently re-evaluating the BIND/neutral pose
# instead of holding the authored final key.
CLIP_LOOP = {
    "Idle": True,
    "Move": True,
    "Attack": False,
    "Hit": False,
    "Cast": False,
    "KO": False,
    "Victory": True,
}


# ---------------------------------------------------------------------------
# Cast (~1.2s, non-looping into itself -- plays once, VFX-driven): rear back onto the hind legs
# slightly, wings spread and raise, head up, beak opens, a HELD peak (the 'cast_release' marker
# fires here, for VFX to key off), then settle back toward neutral.
# ---------------------------------------------------------------------------
CAST_SECONDS = 1.2
CAST_FRAMES = int(round(CAST_SECONDS * FPS))
cast_action = bpy.data.actions.new("Cast")
cast_action.use_fake_user = True
arm_obj.animation_data.action = cast_action

CAST_KEYS = [0.0, 0.30, 0.55, 0.78, 1.0]
CAST_LABELS = ["neutral", "rise", "peak", "peak_hold", "settle"]


def cast_pose(label):
    p = rest_pose()
    if label == "neutral":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "rise":
        # Wind-up: weight shifts back onto the hind legs (hind thighs compress slightly, forelegs
        # lighten/lift a touch), chest/head start rising, wings begin spreading, beak cracking
        # open -- the codeable anticipation rule (counter-motion before the main action, here
        # "down and back" before "up and forward").
        p["spine_01"] = (-4, 0, 0)
        p["spine_02"] = (-8, 0, 0)
        p["neck_01"] = (-4, 0, 0)
        p["head"] = (-6, 0, 0)
        p["wing_L_01"] = (-2, 0, -10)
        p["wing_R_01"] = (-2, 0, 10)
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (10, 0, 0)  # hind legs compress, bearing more weight
            else:
                p[f"leg_{side}_thigh"] = (-6, 0, 0)  # forelegs lighten
        p["jaw"] = (JAW_CLOSED_DEG + 8, 0, 0)
        p["tail_01"] = (-6, 0, 0)
    elif label in ("peak", "peak_hold"):
        # Full rear: hind legs braced and bent, forelegs lifted/tucked, chest and head thrown up,
        # wings fully spread and raised, beak wide open -- the "cast" silhouette. peak_hold repeats
        # this pose exactly (a genuine hold, not just a slow approach) so the VFX marker has a
        # real window to read from, not an instantaneous passing pose.
        p["spine_01"] = (-10, 0, 0)
        p["spine_02"] = (-22, 0, 0)
        p["neck_01"] = (-14, 0, 0)
        p["neck_02"] = (-16, 0, 0)
        p["head"] = (-20, 0, 0)
        p["wing_L_01"] = (-10, 0, -32)
        p["wing_L_02"] = (-4, 0, -16)
        p["wing_R_01"] = (-10, 0, 32)
        p["wing_R_02"] = (-4, 0, 16)
        # Round 16 (producer review): both forelegs swinging forward/up by the SAME amount read as
        # sliding forward together, not a rear -- a real rear keeps (at most) one forepaw light,
        # the other still grounded for balance. FL lifts slightly (a reduced version of the old
        # uniform lift); FR stays close to planted/neutral instead of matching it.
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (20, 0, 0)
                p[f"leg_{side}_shin"] = (-10, 0, 0)
            elif side == "FL":
                p[f"leg_{side}_thigh"] = (-9, 0, 0)
                p[f"leg_{side}_shin"] = (5, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (-2, 0, 0)
                p[f"leg_{side}_shin"] = (1, 0, 0)
        p["jaw"] = (JAW_WIDE_OPEN_DEG + 6, 0, 0)
        p["tail_01"] = (-12, 0, 0)
        p["tail_02"] = (-14, 0, 0)
    elif label == "settle":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)  # back to a closed-beak neutral, ready to loop into Idle
    return p


for frac, label in zip(CAST_KEYS, CAST_LABELS):
    f = 1 + round(frac * CAST_FRAMES)
    scene.frame_set(f)
    apply_pose(cast_pose(label))
    keyframe_pose(f, ALL_POSE_BONES)
    if label == "peak":
        add_marker("Cast", "cast_release", f, frac)

common.set_interpolation(cast_action, "BEZIER", "EASE_IN_OUT")
print(f"CAST ACTION: {len(CAST_KEYS)} key poses ({', '.join(CAST_LABELS)}) over {CAST_FRAMES} "
      f"frames @ {FPS}fps, marker 'cast_release' at frame "
      f"{EVENT_MARKERS['Cast'][0]['frame']}")

# ---------------------------------------------------------------------------
# Hit (~0.5s, non-looping): sharp recoil away from an impact -- head and chest snap back, wings
# flinch -- then a quick recovery. Marker 'hit_react' at the recoil extreme (the impact beat).
# ---------------------------------------------------------------------------
reset_all()
HIT_SECONDS = 0.5
HIT_FRAMES = int(round(HIT_SECONDS * FPS))
hit_action = bpy.data.actions.new("Hit")
hit_action.use_fake_user = True
arm_obj.animation_data.action = hit_action

HIT_KEYS = [0.0, 0.22, 0.42, 1.0]
HIT_LABELS = ["neutral", "recoil", "settle_start", "recover"]


def hit_pose(label):
    p = rest_pose()
    if label == "neutral":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "recoil":
        # Sharp snap-back: head/neck/chest pitch away (opposite sense from Attack's forward
        # strike-snap), wings flinch into a tight tucked flare, legs brace (a small compress, not
        # a full crouch) -- all bones reach their extreme on this ONE frame, reached fast (the
        # strike-sharpening EASE_IN trick below), which is what reads as an "impact," not a slow
        # lean.
        p["spine_01"] = (-6, 0, 0)
        p["spine_02"] = (-14, 0, 0)
        p["neck_01"] = (-10, 0, 0)
        p["neck_02"] = (-14, 0, 0)
        p["head"] = (-18, 0, 0)
        p["wing_L_01"] = (-8, 0, -20)
        p["wing_R_01"] = (-8, 0, 20)
        for side in leg_sides:
            p[f"leg_{side}_thigh"] = (6, 0, 0)
        p["tail_01"] = (8, 0, 0)
        p["jaw"] = (JAW_CLOSED_DEG - 6, 0, 0)
    elif label == "settle_start":
        # Partway back toward neutral -- the quick recovery's first step, not an instant snap-back
        # (a real recoil eases out, even a fast one).
        p["spine_01"] = (-2, 0, 0)
        p["spine_02"] = (-5, 0, 0)
        p["neck_01"] = (-3, 0, 0)
        p["head"] = (-5, 0, 0)
        p["wing_L_01"] = (-2, 0, -6)
        p["wing_R_01"] = (-2, 0, 6)
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "recover":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    return p


for frac, label in zip(HIT_KEYS, HIT_LABELS):
    f = 1 + round(frac * HIT_FRAMES)
    scene.frame_set(f)
    apply_pose(hit_pose(label))
    keyframe_pose(f, ALL_POSE_BONES)
    if label == "recoil":
        add_marker("Hit", "hit_react", f, frac)

common.set_interpolation(hit_action, "BEZIER", "EASE_IN_OUT")
# Sharpen the recoil's IN edge, same principle as Attack's strike frame -- neutral->recoil should
# read as abrupt/fast (an impact), not eased in.
recoil_frame = 1 + round(HIT_KEYS[1] * HIT_FRAMES)
for fcu in common.iter_action_fcurves(hit_action):
    for kp in fcu.keyframe_points:
        if round(kp.co[0]) == recoil_frame:
            kp.easing = "EASE_IN"
print(f"HIT ACTION: {len(HIT_KEYS)} key poses ({', '.join(HIT_LABELS)}) over {HIT_FRAMES} "
      f"frames @ {FPS}fps, marker 'hit_react' at frame {EVENT_MARKERS['Hit'][0]['frame']}")

# ---------------------------------------------------------------------------
# KO (~1.4s, NON-LOOPING -- ends on a held, LYING-DOWN final pose, does not return to neutral):
# stagger, the body sinks straight down, settles onto belly/side with legs extended loosely and
# wings open flat on the ground. Producer direction (2026-10-04), replacing round 16's curled-up
# collapse: "it should lie down, not curl up... favour extending limbs over folding them (that's
# what keeps the skin clean)." Round 16's own investigation already found, the hard way, that
# heavy joint FOLDING at this foot/toe junction reliably makes toe_deformation worse -- this redo
# leans into that lesson instead of fighting it: every limb below is rotated toward EXTENSION
# (straightened out along the ground), not folded underneath the body.
#
# The root bone -- the sole, unparented top-level bone (see rig_templates/winged_quadruped.py;
# pelvis/spine/legs/tail all descend from it) -- gets a LOCATION keyframe here, not just rotation,
# so the whole skeleton actually translates down to ground height instead of staying planted at
# standing hip height while only rotating. Nothing else in this file uses bone location
# (apply_pose/keyframe_pose, and rest_pose()'s dict, are rotation/scale-only, and "root" is
# deliberately excluded from ALL_POSE_BONES) so it's handled as its own small block below, kept
# scoped to KO and explicitly reset afterward so it can't leak into Victory's action, which comes
# right after this in the script and never touches root itself.
# ---------------------------------------------------------------------------
reset_all()
arm_obj.pose.bones["root"].location = (0, 0, 0)
KO_SECONDS = 1.4
KO_FRAMES = int(round(KO_SECONDS * FPS))
ko_action = bpy.data.actions.new("KO")
ko_action.use_fake_user = True
arm_obj.animation_data.action = ko_action

KO_KEYS = [0.0, 0.15, 0.45, 0.85, 1.0]
KO_LABELS = ["neutral", "stagger", "sink", "settle", "final_hold"]

# How far the root bone translates at full settle, along its OWN local Y axis -- this specific
# bone points straight up at bind (head (0,0,0) to tail (0,0,0.24*H)), so local Y is the axis
# that runs along its length, and NEGATIVE local Y is world -Z (down); confirmed directly, not
# assumed (a quick standalone check: root.location=(0,-0.7,0) moved the pelvis to world z=-0.46,
# exactly matching 0.24-0.70 -- the OTHER two components move it sideways/forward instead).
# Native units at this rig's target_height=2.0. -0.72 is not a guess: iterated by sampling actual
# pose-bone world positions (same method as gait.py's own ground-contact checks) until every
# foot/wingtip/tail-tip landed within a few mm of ground together with the leg/wing angles below.
KO_ROOT_DROP = -0.72


def ko_root_location(frac):
    return (0, KO_ROOT_DROP * frac, 0)


def ko_pose(label):
    p = rest_pose()
    if label == "neutral":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "stagger":
        # Off-balance wobble before the legs actually give way -- unchanged from before, except a
        # small foreleg head-start (see below).
        p["spine_01"] = (4, 0, 3)
        p["spine_02"] = (8, 0, 4)
        p["neck_01"] = (6, 0, 2)
        p["head"] = (10, 0, 2)
        p["wing_L_01"] = (6, 0, 8)
        p["wing_R_01"] = (6, 0, -8)
        p["jaw"] = (JAW_CLOSED_DEG + 4, 0, 0)
        p["tail_01"] = (4, 0, 2)
        # A small foreleg head-start toward extension: without this, the forelegs sit at bind
        # (0 deg) all through stagger while the root starts dropping on the very next segment
        # (stagger->sink), so the interpolated frames between them briefly had the body lower
        # than the still-near-bind legs could support -- confirmed by scanning every frame of the
        # baked action, not just the keyframes (worst case was -0.039 at frame 7, just past this
        # key). A small head-start here closes that gap smoothly instead of chasing it with a
        # bigger root/leg mismatch fix elsewhere.
        for side in leg_sides:
            if side == "FL":
                p[f"leg_{side}_thigh"] = (-26, 0, 0)  # matches settle's FL/FR asymmetry, see there
            elif side == "FR":
                p[f"leg_{side}_thigh"] = (-22, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (18, 0, 0)
    elif label == "sink":
        # The body lowers straight down (the brief's other offered option, besides "hind legs
        # fold under first") -- root only 25% of the way down here (see ROOT_LOCATION_FRAC
        # below), legs ALREADY close to their final extended angle. The leg-angle-to-foot-height
        # relationship here is sharply non-linear near the fully-extended zone (confirmed by
        # direct measurement: thigh -73 deg left the foot -0.143 below ground, -78 gave -0.075,
        # -83 gave -0.006 -- small angle changes near full extension move the foot a lot more
        # than they do earlier in the swing), so a "halfway" sink pose using a "halfway" leg angle
        # actually left the foot well below ground through the sink-to-settle transition (worst
        # measured: -0.185 at frame 14) -- legs extend FAST early and the root drops LATE instead,
        # confirmed by re-scanning every frame of the baked action afterward, not just the keys.
        for side in leg_sides:
            if side.startswith("F"):
                p[f"leg_{side}_thigh"] = (-70, 0, 0)
                p[f"leg_{side}_shin"] = (-5, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (60, 0, 0)
                p[f"leg_{side}_shin"] = (5, 0, 0)
                p[f"leg_{side}_toe"] = (-90, 0, 0)  # proportional head-start, see settle's -110
        p["spine_01"] = (3, 0, 4)
        p["spine_02"] = (3, 0, 5)
        p["neck_01"] = (16, 0, 3)
        p["neck_02"] = (4, 0, 2)
        p["head"] = (6, 0, 2)
        p["wing_L_01"] = (-18, 0, -10)
        p["wing_L_02"] = (-8, 0, 0)
        p["wing_R_01"] = (-18, 0, 10)
        p["wing_R_02"] = (-8, 0, 0)
        p["jaw"] = (JAW_CLOSED_DEG + 4, 0, 0)
        p["tail_01"] = (6, 0, 5)
        p["tail_02"] = (5, 0, 4)
    elif label in ("settle", "final_hold"):
        # Lying flat on the ground, belly/side down. Forelegs extended forward, hind legs
        # extended back (both nearly straight -- a small shin counter-rotation only, not a fold),
        # wings OPEN and lying flat beside the body (not tucked), neck extended forward along the
        # ground with the head resting on the ground, beak CLOSED (not round 16's unconscious
        # gape -- this producer brief doesn't ask for that), tail limp and extended, not curled.
        # Magnitudes here are the result of direct numeric iteration -- sampling each foot/
        # wingtip/tail-tip's actual world-space position against ground and adjusting until every
        # one landed within a few mm, the same way ground-contact was tuned in gait.py -- not
        # picked by eye: an extended-but-wrong-angle limb can clip the ground exactly like a
        # folded one can tear the mesh, so "favour extension" still needed the angles measured,
        # not just the general direction guessed.
        for side in leg_sides:
            if side == "FL":
                # A tiny asymmetric nudge vs FR (-85 vs -83): the two sides aren't perfectly
                # mirror-symmetric in this mesh/rig (confirmed by measurement -- FL's foot sat
                # noticeably lower than FR's at the identical -83 angle, -0.024 vs -0.004), so a
                # shared angle alone can't zero both; this closes the gap directly instead of
                # chasing it through an unrelated shared parameter.
                p[f"leg_{side}_thigh"] = (-85, 0, 0)
                p[f"leg_{side}_shin"] = (-5, 0, 0)
            elif side == "FR":
                p[f"leg_{side}_thigh"] = (-83, 0, 0)
                p[f"leg_{side}_shin"] = (-5, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (72, 0, 0)
                p[f"leg_{side}_shin"] = (5, 0, 0)
                # The hind feet use a single aggregate toe bone (not the forelegs' 3-bone fan --
                # see rig_templates/winged_quadruped.py), left at its bind-relative angle by
                # every other clip in this file. That's fine when the foot itself stays close to
                # its own bind world-orientation (Idle/Move/Attack/Cast/Hit/Victory), but this
                # pose rotates the WHOLE hind leg ~70 deg to lie flat, and the toe's bind-relative
                # angle carried along for the ride pointed it steeply into the ground (measured
                # directly: -0.163 at the toe tip, vs the foot's own -- correctly grounded --
                # 0.005). A counter-rotation straightens it back out flat; -110 deg was the value
                # that actually cleared ground with margin (tested -40 through -115 directly, not
                # guessed -- the toe-to-ground relationship here is as non-linear as the leg's own,
                # see settle's comment above).
                p[f"leg_{side}_toe"] = (-110, 0, 0)
        p["spine_01"] = (5, 0, 6)
        p["spine_02"] = (5, 0, 7)
        p["neck_01"] = (32, 0, 4)
        p["neck_02"] = (8, 0, 3)
        p["head"] = (10, 0, 3)
        p["wing_L_01"] = (-33, 0, -10)
        p["wing_L_02"] = (-15, 0, 0)
        p["wing_R_01"] = (-33, 0, 10)
        p["wing_R_02"] = (-15, 0, 0)
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
        p["tail_01"] = (10, 0, 8)
        p["tail_02"] = (10, 0, 6)
    return p


ROOT_LOCATION_FRAC = {"neutral": 0.0, "stagger": 0.0, "sink": 0.25, "settle": 1.0, "final_hold": 1.0}
for frac, label in zip(KO_KEYS, KO_LABELS):
    f = 1 + round(frac * KO_FRAMES)
    scene.frame_set(f)
    apply_pose(ko_pose(label))
    keyframe_pose(f, ALL_POSE_BONES)
    root_pb = arm_obj.pose.bones["root"]
    root_pb.location = ko_root_location(ROOT_LOCATION_FRAC[label])
    root_pb.keyframe_insert(data_path="location", frame=f)

common.set_interpolation(ko_action, "BEZIER", "EASE_IN_OUT")
arm_obj.pose.bones["root"].location = (0, 0, 0)  # reset live scene state before Victory, next
print(f"KO ACTION: {len(KO_KEYS)} key poses ({', '.join(KO_LABELS)}) over {KO_FRAMES} frames "
      f"@ {FPS}fps (non-looping, held lying-down final pose)")

# ---------------------------------------------------------------------------
# Victory (~2.0s, LOOP-FRIENDLY END -- the final pose is close to neutral/rest so it transitions
# smoothly back into Idle): rear up, wings fully spread with a flap (out then a downbeat), head
# tossed back with beak open, settle into a proud stance.
# ---------------------------------------------------------------------------
reset_all()
VICTORY_SECONDS = 2.0
VICTORY_FRAMES = int(round(VICTORY_SECONDS * FPS))
victory_action = bpy.data.actions.new("Victory")
victory_action.use_fake_user = True
arm_obj.animation_data.action = victory_action

VICTORY_KEYS = [0.0, 0.22, 0.38, 0.52, 0.72, 1.0]
VICTORY_LABELS = ["neutral", "rear_up", "flap_out", "flap_down", "toss_head", "proud_settle"]


def victory_pose(label):
    p = rest_pose()
    if label == "neutral":
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    elif label == "rear_up":
        # Rear up onto the hind legs (same "weight back" mechanism as Cast's wind-up, pushed
        # further -- Victory is the bigger, more triumphant version of that same silhouette).
        p["spine_01"] = (-8, 0, 0)
        p["spine_02"] = (-16, 0, 0)
        p["neck_01"] = (-8, 0, 0)
        p["head"] = (-10, 0, 0)
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (16, 0, 0)
                p[f"leg_{side}_shin"] = (-8, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (-10, 0, 0)
        p["tail_01"] = (-8, 0, 0)
    elif label == "flap_out":
        # Wings sweep fully out/up -- the flap's up-stroke.
        p["spine_01"] = (-8, 0, 0)
        p["spine_02"] = (-18, 0, 0)
        p["neck_01"] = (-8, 0, 0)
        p["head"] = (-8, 0, 0)
        p["wing_L_01"] = (-14, 0, -40)
        p["wing_L_02"] = (-6, 0, -22)
        p["wing_R_01"] = (-14, 0, 40)
        p["wing_R_02"] = (-6, 0, 22)
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (16, 0, 0)
                p[f"leg_{side}_shin"] = (-8, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (-10, 0, 0)
        p["tail_01"] = (-10, 0, 0)
    elif label == "flap_down":
        # The flap's down-stroke -- wings sweep down/forward past neutral, a real flap cycle (not
        # just a static spread-and-hold), chest puffs with the effort.
        p["spine_01"] = (-6, 0, 0)
        p["spine_02"] = (-10, 0, 0)
        p["neck_01"] = (-4, 0, 0)
        p["head"] = (-4, 0, 0)
        p["wing_L_01"] = (14, 0, -10)
        p["wing_L_02"] = (8, 0, -4)
        p["wing_R_01"] = (14, 0, 10)
        p["wing_R_02"] = (8, 0, 4)
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (10, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (-6, 0, 0)
        p["tail_01"] = (-4, 0, 0)
    elif label == "toss_head":
        # Head tossed back, beak open -- the triumphant "call" beat, wings holding a proud spread.
        p["spine_01"] = (-6, 0, 0)
        p["spine_02"] = (-14, 0, 0)
        p["neck_01"] = (-14, 0, 0)
        p["neck_02"] = (-18, 0, 0)
        p["head"] = (-24, 0, 0)
        p["wing_L_01"] = (-6, 0, -24)
        p["wing_R_01"] = (-6, 0, 24)
        p["jaw"] = (JAW_WIDE_OPEN_DEG + 4, 0, 0)
        for side in leg_sides:
            if side.startswith("B"):
                p[f"leg_{side}_thigh"] = (12, 0, 0)
            else:
                p[f"leg_{side}_thigh"] = (-8, 0, 0)
        p["tail_01"] = (-6, 0, 0)
    elif label == "proud_settle":
        # Settle EXACTLY back to neutral/rest (not just "close") -- verify.py's loop-seam gate
        # checks this precisely (<0.5 deg max bone delta between frame 0 and the final frame); an
        # earlier version left a small +/-2 deg spine/head offset here, which read fine by eye but
        # failed that gate at 2.0 deg. Beak closed, matching neutral and Idle's own rest pose, so
        # looping Victory -> Idle is a true seamless continuation, not just a visually-close one.
        p["jaw"] = (JAW_CLOSED_DEG, 0, 0)
    return p


for frac, label in zip(VICTORY_KEYS, VICTORY_LABELS):
    f = 1 + round(frac * VICTORY_FRAMES)
    scene.frame_set(f)
    apply_pose(victory_pose(label))
    keyframe_pose(f, ALL_POSE_BONES)

common.set_interpolation(victory_action, "BEZIER", "EASE_IN_OUT")
print(f"VICTORY ACTION: {len(VICTORY_KEYS)} key poses ({', '.join(VICTORY_LABELS)}) over "
      f"{VICTORY_FRAMES} frames @ {FPS}fps (loop-friendly end)")

reset_all()
arm_obj.animation_data.action = None
bpy.ops.object.mode_set(mode="OBJECT")

with open(os.path.join(OUT, "keyed_event_markers.json"), "w") as f:
    json.dump({"markers": EVENT_MARKERS, "loop": CLIP_LOOP}, f, indent=2)
print(f"EVENT MARKERS: {json.dumps(EVENT_MARKERS)}")
print(f"CLIP LOOP: {json.dumps(CLIP_LOOP)}")

blend_out = os.path.join(OUT, "griffin_keyed.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print(f"SAVED {blend_out}")
print("KEYED DONE")
