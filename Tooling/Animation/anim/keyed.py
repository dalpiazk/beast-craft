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
CREATURE = args.get("creature", "griffin")
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


def build_griffin_clips():
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

# ---------------------------------------------------------------------------
# v18: the four new wingless quadrupeds (Golem, Kirin, Tarasque, Basilisk) -- same six clips
# (Idle, Attack, Cast, Hit, KO, Victory), same sparse-hand-keyed-pose/Bezier-EASE_IN_OUT method as
# build_griffin_clips() above, but each creature's distinctive character (per the lead's brief) is
# authored as its own Attack/Cast/Victory pose function -- Idle/Hit/KO share one generic
# implementation each (parameterised by CREATURE_PARAMS below), since the brief didn't ask for
# unique Idle/Hit/KO character per creature the way it did for Attack/Cast/Victory, and a shared
# implementation keeps this a reviewable amount of code rather than 24 independent clip functions.
#
# No wing bones exist on this template (rig_templates/quadruped.py) -- none of the pose dicts below
# set any wing_* key, rather than relying on apply_pose's silent no-op for a missing bone (clearer
# to read, same effect).
# ---------------------------------------------------------------------------
H_CONST = 2.0  # this pipeline's shared normalisation convention (common.normalise_transform default)

CREATURE_PARAMS = {
    # jaw_closed/jaw_open: every one of these four rigs has its own jaw bone (quadruped.py), but the
    # angle that reads as "closed" depends on that creature's own skull->jaw_tip geometry (same
    # reasoning as Griffin's JAW_CLOSED_DEG comment) -- first-pass values, smaller magnitude than
    # the Griffin beak's -32 since these jaws sit closer to their snout tip at bind. motion_scale
    # scales the generic Idle breathing/look/tail-sway amplitudes (and Hit's recoil) to read as
    # "heavier/slower" (<1) or "lighter/quicker" (>1) per creature, same spirit as gait.py's own
    # per-creature tail_amp/head_amp.
    # ko_mode (v18 round 4): "belly" = sink onto the belly, legs splayed flat (default);
    # "side" = tip over onto the flank (Golem: pelvis-only webbing between its narrow-stance stumps
    # sits 0.125 above the ground and its head ~1.0 up with no neck, so a belly slump can only sink
    # ~12 cm and can never put the head down -- it reads as "barely changes"; a boulder tipping onto
    # its side reads unambiguously as KO).
    "golem":    dict(jaw_closed=-10.0, jaw_open=16.0, motion_scale=0.6, ko_mode="side", ko_roll=-78.0),
    "kirin":    dict(jaw_closed=-14.0, jaw_open=20.0, motion_scale=1.2),
    "tarasque": dict(jaw_closed=-12.0, jaw_open=32.0, motion_scale=0.85),
    "basilisk": dict(jaw_closed=-16.0, jaw_open=28.0, motion_scale=1.15, ko_splay_out=0.8),
}


def build_creature_clips(creature):
    import mathutils

    CP = CREATURE_PARAMS[creature]
    JAW_CLOSED = CP["jaw_closed"]
    JAW_OPEN = CP["jaw_open"]
    MS = CP["motion_scale"]
    arm_obj.animation_data_create()

    ALL_POSE_BONES = [b for b in all_bones if b not in ("root",)]
    TAIL_BONES = sorted(n for n in all_bones if n.startswith("tail_"))
    FRONT_SIDES = [s for s in leg_sides if s.startswith("F")]
    BACK_SIDES = [s for s in leg_sides if s.startswith("B")]

    def rest_pose():
        return {b: (0, 0, 0) for b in ALL_POSE_BONES}

    def reset_all():
        for name in ALL_POSE_BONES:
            pb = arm_obj.pose.bones.get(name)
            if pb:
                pb.rotation_euler = (0, 0, 0)
                pb.scale = (1, 1, 1)

    EVENT_MARKERS = {}

    def add_marker(clip_name, name, frame, fraction):
        EVENT_MARKERS.setdefault(clip_name, []).append(
            {"name": name, "frame": frame, "fraction": fraction})

    CLIP_LOOP = {"Idle": True, "Move": True, "Attack": False, "Hit": False,
                 "Cast": False, "KO": False, "Victory": True}

    # -----------------------------------------------------------------------
    # v18 round 4 (lead review: Golem Cast's front legs smeared through the ground, Tarasque Cast/
    # Victory dipped feet below ground). Root cause: every leg in these clips is pure FK hanging off
    # the torso, so ANY spine pitch (a Cast's chest rise, a Hit's recoil) swings the feet with it --
    # Golem Cast's chest pitch drove its forefeet 0.25 below ground (measured). Fix: a planted-foot
    # IK pass after every key pose -- each leg not listed in the pose's "_free" set is re-solved
    # (closed-form 2-bone IK, minimum-twist aim, same construction as anim/gait.py's fk_anchored
    # mode) so its ankle stays at its REST world position with the foot at its rest orientation.
    # Explicit thigh/shin values in a pose only apply to "_free" (deliberately lifted) legs. A pose
    # may also carry "_root_drop" (world-Z offset of the whole body, keyed on `root`), so a crouch/
    # hunker lowers the body and the planted legs bend to absorb it.
    # -----------------------------------------------------------------------
    _pelvis_rest_head = arm_obj.data.bones["pelvis"].tail_local.copy()
    _head_rest = arm_obj.data.bones["head"].head_local.copy()
    FORWARD = _head_rest - _pelvis_rest_head
    FORWARD.z = 0.0
    FORWARD.normalize()

    def fk_world(pb):
        """Armature-space pose matrix of `pb` from the CURRENT python-side matrix_basis values of
        it and its ancestors -- no depsgraph update (which would re-evaluate the assigned action and
        clobber just-set values, see bake_clip)."""
        chain = []
        b = pb
        while b is not None:
            chain.append(b)
            b = b.parent
        m = mathutils.Matrix.Identity(4)
        prev_rest = mathutils.Matrix.Identity(4)
        for b in reversed(chain):
            m = m @ prev_rest.inverted() @ b.bone.matrix_local @ b.matrix_basis
            prev_rest = b.bone.matrix_local
        return m

    LEG_IK = {}
    for _s in leg_sides:
        _th = arm_obj.data.bones[f"leg_{_s}_thigh"]
        _sh = arm_obj.data.bones[f"leg_{_s}_shin"]
        _hip, _knee, _ankle = _th.head_local.copy(), _th.tail_local.copy(), _sh.tail_local.copy()
        _ha = _ankle - _hip
        _t = max(0.0, min(1.0, (_knee - _hip).dot(_ha) / max(_ha.length_squared, 1e-9)))
        _off = _knee - (_hip + _ha * _t)
        _pole = (_off.normalized() if _off.length / max(_th.length + _sh.length, 1e-6) >= 0.06
                 else -FORWARD.copy())
        LEG_IK[_s] = {"L1": _th.length, "L2": _sh.length, "ankle_rest": _ankle, "pole": _pole}

    def aim_min_twist(neutral, head_pos, tail_pos):
        rot = neutral.to_3x3().normalized()
        want = tail_pos - head_pos
        if want.length > 1e-7:
            rot = rot.col[1].rotation_difference(want.normalized()).to_matrix() @ rot
        m = rot.to_4x4()
        m.translation = head_pos
        return m

    def set_basis(pb, desired_world, parent_world):
        rest = pb.bone.matrix_local
        prest = pb.parent.bone.matrix_local
        basis = rest.inverted() @ prest @ parent_world.inverted() @ desired_world
        # Rotation only: every head lands exactly where FK already puts it (knee/ankle are placed at
        # the parent bone's own length), so the basis translation is ~0 and is not keyed.
        pb.rotation_euler = basis.to_euler("XYZ", pb.rotation_euler)

    def plant_leg(side, ankle_target=None):
        """2-bone IK: thigh/shin re-aimed (min twist) so the ankle lands on `ankle_target` (default:
        its rest position), foot held at its rest world orientation, toe at rest relative to foot."""
        d = LEG_IK[side]
        target = d["ankle_rest"] if ankle_target is None else ankle_target
        th = arm_obj.pose.bones[f"leg_{side}_thigh"]
        sh = arm_obj.pose.bones[f"leg_{side}_shin"]
        ft = arm_obj.pose.bones[f"leg_{side}_foot"]
        toe = arm_obj.pose.bones.get(f"leg_{side}_toe")
        parent_w = fk_world(th.parent)
        neutral_th = parent_w @ th.parent.bone.matrix_local.inverted() @ th.bone.matrix_local
        hip = neutral_th.translation.copy()
        L1, L2 = d["L1"], d["L2"]
        to_t = target - hip
        dist = max(abs(L1 - L2) + 1e-4, min(L1 + L2 - 1e-4, to_t.length))
        dirn = to_t.normalized()
        cos_a = max(-1.0, min(1.0, (L1 * L1 + dist * dist - L2 * L2) / (2 * L1 * dist)))
        perp = d["pole"] - d["pole"].dot(dirn) * dirn
        if perp.length < 1e-6:
            perp = mathutils.Vector((0, 0, 1)) - dirn.z * dirn
        perp.normalize()
        a = math.acos(cos_a)
        knee = hip + (dirn * math.cos(a) + perp * math.sin(a)) * L1
        ankle = knee + (hip + dirn * dist - knee).normalized() * L2
        th_w = aim_min_twist(neutral_th, hip, knee)
        set_basis(th, th_w, parent_w)
        sh_w = aim_min_twist(th_w @ th.bone.matrix_local.inverted() @ sh.bone.matrix_local, knee, ankle)
        set_basis(sh, sh_w, th_w)
        ft_w = ft.bone.matrix_local.to_3x3().to_4x4()
        ft_w.translation = ankle
        set_basis(ft, ft_w, sh_w)
        if toe is not None:
            toe.rotation_euler = (0, 0, 0)

    def apply_full_pose(pose):
        """apply_pose + root drop + planted-foot IK for every leg not in pose['_free'] (FK). A leg
        in pose['_lift'] = {side: (up, fwd)} is IK-placed at its rest ankle raised by `up` and moved
        `fwd` along FORWARD, foot kept level -- a clean lifted step/stomp (an FK thigh swing tips
        the foot and dips its toe)."""
        apply_pose(pose)
        arm_obj.pose.bones["root"].location = (0, pose.get("_root_drop", 0.0), 0)
        arm_obj.pose.bones["root"].rotation_euler = (0, 0, 0)
        free = pose.get("_free", ())
        lifts = pose.get("_lift", {})
        for side in leg_sides:
            if side in free:
                continue
            if side in lifts:
                up, fwd = lifts[side]
                plant_leg(side, LEG_IK[side]["ankle_rest"] + mathutils.Vector((0, 0, up)) + FORWARD * fwd)
            else:
                plant_leg(side)

    def bake_clip(action_name, seconds, keys, labels, pose_fn, scale_fn=None, marker=None,
                  sharpen_frame_key=None):
        """Shared bake loop: builds `action_name` from `keys`/`labels` via `pose_fn(label)` (and
        optional `scale_fn(label)`), Bezier EASE_IN_OUT throughout, an optional event marker fired
        at a given label, and an optional sharpened (EASE_IN) keyframe for a fast impact/strike
        beat -- the same pattern every one of build_griffin_clips()'s six clips already uses,
        factored out once here since all four new creatures' clips follow it identically (only the
        pose content differs, which is why that part stays a creature-specific function, not
        factored in)."""
        frames = int(round(seconds * FPS))
        action = bpy.data.actions.new(action_name)
        action.use_fake_user = True
        arm_obj.animation_data.action = action
        marker_label, marker_name = marker if marker else (None, None)
        for frac, label in zip(keys, labels):
            f = 1 + round(frac * frames)
            scene.frame_set(f)
            apply_full_pose(pose_fn(label))
            if scale_fn:
                apply_scale(scale_fn(label))
            keyframe_pose(f, ALL_POSE_BONES)
            arm_obj.pose.bones["root"].keyframe_insert(data_path="location", frame=f)
            # root rotation keyed (0) in every clip: KO's tip-over (Golem) rotates root, and a clip
            # without that channel would inherit whatever rotation the previous clip left behind.
            arm_obj.pose.bones["root"].keyframe_insert(data_path="rotation_euler", frame=f)
            if marker_label is not None and label == marker_label:
                add_marker(action_name, marker_name, f, frac)
        common.set_interpolation(action, "BEZIER", "EASE_IN_OUT")
        arm_obj.pose.bones["root"].location = (0, 0, 0)
        if sharpen_frame_key is not None:
            sharp_frame = 1 + round(keys[sharpen_frame_key] * frames)
            for fcu in common.iter_action_fcurves(action):
                for kp in fcu.keyframe_points:
                    if round(kp.co[0]) == sharp_frame:
                        kp.easing = "EASE_IN"
        print(f"{action_name.upper()} ACTION ({creature}): {len(keys)} key poses "
              f"({', '.join(labels)}) over {frames} frames @ {FPS}fps")
        return action, frames

    # -----------------------------------------------------------------------
    # Idle (3s loop): generic breathing/weight-shift/head-look/tail-sway, scaled by motion_scale --
    # shared across all four (the brief's per-creature character asks were for Attack/Cast/Victory,
    # not Idle).
    # -----------------------------------------------------------------------
    def idle_pose(frac):
        p = rest_pose()
        settle = math.sin(frac * 2 * math.pi)
        p["spine_02"] = (1.3 * MS * settle, 0, 0)
        p["spine_01"] = (0.7 * MS * settle, 0, 0)
        p["neck_01"] = (0, 2.5 * MS * math.sin(frac * 2 * math.pi + 1.1), 0)
        p["neck_02"] = (0, 1.6 * MS * math.sin(frac * 2 * math.pi + 1.1), 0)
        p["head"] = (0.8 * MS * settle, 1.6 * MS * math.sin(frac * 2 * math.pi + 1.1), 0)
        p["jaw"] = (JAW_CLOSED + 1.2 * MS * settle, 0, 0)
        for i, tb in enumerate(TAIL_BONES):
            amp = max(2.0, 5.0 - i)
            p[tb] = (0, 0, MS * amp * math.sin(frac * 2 * math.pi + 0.4 + 0.3 * i))
        if FRONT_SIDES:
            p[f"leg_{FRONT_SIDES[0]}_thigh"] = (0, 0, 1.0 * MS * settle)
        if len(FRONT_SIDES) > 1:
            p[f"leg_{FRONT_SIDES[1]}_thigh"] = (0, 0, -1.0 * MS * settle)
        return p

    idle_keys = [0.0, 0.22, 0.5, 0.72, 1.0]
    bake_clip("Idle", 3.0, idle_keys, [str(k) for k in idle_keys],
              lambda label: idle_pose(float(label)))
    reset_all()

    # -----------------------------------------------------------------------
    # Attack: distinct per-creature lunge/strike character, ~1.0-1.1s.
    # -----------------------------------------------------------------------
    def golem_attack(label):
        # Rear front end up slightly, then a DOUBLE front-foot stomp (not a bite/lunge -- Golem has
        # no reach weapon, its attack is sheer weight).
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "anticipation":
            p["spine_02"] = (-9, 0, 0)
            p["spine_01"] = (-6, 0, 0)
            p["head"] = (-9, 0, 0)
            for s in FRONT_SIDES:
                p[f"leg_{s}_thigh"] = (-20, 0, 0)
            p["jaw"] = (JAW_OPEN * 0.3, 0, 0)
        elif label == "stomp1":
            # v18 round-3 fix (lead review: "make sure the Golem stomp reads clearly at peak") --
            # amplitude increased (thigh 26->38, shin -10->-20, spine_02/head pitch increased to
            # match) so the lifted-then-slammed-down front foot is unambiguous at normal viewing
            # size, not just technically present in the data.
            p["spine_02"] = (16, 0, 0)
            p["head"] = (10, 0, 0)
            # round 4: the stomping foot is IK-lifted (foot level), the other three stay planted
            p["_lift"] = {s: (0.16, 0.04) for s in FRONT_SIDES[:1]}
            p["jaw"] = (JAW_OPEN * 0.15, 0, 0)
        elif label == "stomp2":
            p["spine_02"] = (16, 0, 0)
            p["head"] = (10, 0, 0)
            p["_lift"] = {s: (0.16, 0.04) for s in FRONT_SIDES[1:2]}
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "recover":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def kirin_attack(label):
        # Head-DOWN horn-thrust lunge. v18 round 4 (lead review: frame 3 folded the head/neck
        # BACKWARD over the shoulders). Root cause: on the quadruped template's align_roll_up roll,
        # +deg_x pitches a bone's tip toward its local Z, i.e. UP/BACK; Kirin's neck bones already
        # lean back ~50 deg from horizontal, so the old strike's +22/+26/+30 on neck_01/neck_02/head
        # folded the neck back over the withers. Sign fixed in the data (not a per-clip axis hack):
        # negative = head down/forward. Strike drops the head below the withers so the horns (which
        # sweep up/back at rest) point forward; all four feet stay planted (IK).
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "anticipation":
            p["spine_02"] = (5, 0, 0)
            p["neck_01"] = (8, 0, 0)
            p["neck_02"] = (6, 0, 0)
            p["head"] = (8, 0, 0)
            p["tail_01"] = (10, 0, 0)
            p["_root_drop"] = -0.03
        elif label == "strike":
            p["spine_02"] = (-6, 0, 0)
            p["neck_01"] = (-26, 0, 0)
            p["neck_02"] = (-20, 0, 0)
            p["head"] = (-8, 0, 0)
            p["tail_01"] = (-14, 0, 0)
            p["_root_drop"] = -0.05
        elif label == "follow_through":
            p["spine_02"] = (-7, 0, 0)
            p["neck_01"] = (-28, 0, 0)
            p["neck_02"] = (-21, 0, 0)
            p["head"] = (-9, 0, 0)
            p["tail_01"] = (-10, 0, 0)
            p["_root_drop"] = -0.04
        elif label == "recover":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def tarasque_attack(label):
        # Lunging head bash/bite, jaw wide open.
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "anticipation":
            # round 4: this dip put the snout 14 cm through the floor (dense per-frame check);
            # Tarasque's head rests only ~0.24 off the ground, so the coil is a draw-BACK instead.
            p["spine_01"] = (-2, 0, 0)
            p["spine_02"] = (-3, 0, 0)
            p["neck_01"] = (4, 0, 0)
            p["head"] = (6, 0, 0)
            p["_root_drop"] = -0.03
            p["jaw"] = (JAW_OPEN, 0, 0)
        elif label == "strike":
            # v18 round-3 fix (lead review: "make sure... Tarasque head-bash read clearly at
            # peak") -- amplitude increased (head pitch 18->28, spine_02 14->20) for a more
            # unambiguous lunge-forward silhouette at normal viewing size.
            p["spine_01"] = (12, 0, 0)
            p["spine_02"] = (20, 0, 0)
            p["neck_01"] = (18, 0, 0)
            p["head"] = (28, 0, 0)
            p["jaw"] = (JAW_CLOSED - 6, 0, 0)  # snap shut, slight overshoot
        elif label == "follow_through":
            p["spine_01"] = (8, 0, 0)
            p["spine_02"] = (14, 0, 0)
            p["head"] = (14, 0, 0)
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "recover":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def basilisk_attack(label):
        # Fast forward lunge bite + tail whip (tail snaps the OPPOSITE way from the lunge).
        p = rest_pose()
        tail1 = TAIL_BONES[0] if TAIL_BONES else None
        tail2 = TAIL_BONES[1] if len(TAIL_BONES) > 1 else None
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "anticipation":
            p["spine_01"] = (-5, 0, 0)
            p["spine_02"] = (-8, 0, 0)
            p["neck_01"] = (-6, 0, 0)
            p["head"] = (-8, 0, 0)
            p["jaw"] = (JAW_OPEN * 0.8, 0, 0)
            if tail1:
                p[tail1] = (0, 0, 20)
            if tail2:
                p[tail2] = (0, 0, 14)
        elif label == "strike":
            p["spine_01"] = (10, 0, 0)
            p["spine_02"] = (16, 0, 0)
            p["neck_01"] = (10, 0, 0)
            p["head"] = (14, 0, 0)
            p["jaw"] = (JAW_CLOSED - 8, 0, 0)
            if tail1:
                p[tail1] = (0, 0, -26)
            if tail2:
                p[tail2] = (0, 0, -20)
        elif label == "follow_through":
            p["spine_01"] = (6, 0, 0)
            p["spine_02"] = (8, 0, 0)
            p["jaw"] = (JAW_CLOSED, 0, 0)
            if tail1:
                p[tail1] = (0, 0, -10)
        elif label == "recover":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    ATTACK_FN = {"golem": golem_attack, "kirin": kirin_attack,
                 "tarasque": tarasque_attack, "basilisk": basilisk_attack}
    if creature == "golem":
        attack_keys = [0.0, 0.26, 0.46, 0.68, 1.0]
        attack_labels = ["neutral", "anticipation", "stomp1", "stomp2", "recover"]
        attack_strike_idx = 2
    else:
        attack_keys = [0.0, 0.28, 0.42, 0.64, 1.0]
        attack_labels = ["neutral", "anticipation", "strike", "follow_through", "recover"]
        attack_strike_idx = 2
    bake_clip("Attack", 1.05, attack_keys, attack_labels, ATTACK_FN[creature],
               sharpen_frame_key=attack_strike_idx)
    reset_all()

    # -----------------------------------------------------------------------
    # Cast (~1.2s, non-looping, VFX-driven, marker 'cast_release' at the peak): distinct per-
    # creature "charge" silhouette.
    # -----------------------------------------------------------------------
    def golem_cast(label):
        # Plant feet, slow body rise/settle -- a "charge" pose, not a rear (Golem's weight stays
        # grounded on all four).
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "rise":
            # v18 round 4: these were negative, which on the Z-up roll pitches the chest DOWN (and,
            # with FK legs, drove the forefeet 0.25 below ground -- the lead's "front legs smear").
            # Positive = chest/head up; all four feet stay planted (IK), body sinks into a brace.
            p["spine_01"] = (4, 0, 0)
            p["spine_02"] = (6, 0, 0)
            p["head"] = (6, 0, 0)
            p["jaw"] = (JAW_OPEN * 0.4, 0, 0)
            p["_root_drop"] = -0.03
        elif label in ("peak", "peak_hold"):
            p["spine_01"] = (7, 0, 0)
            p["spine_02"] = (10, 0, 0)
            p["head"] = (10, 0, 0)
            p["jaw"] = (JAW_OPEN, 0, 0)
            p["_root_drop"] = -0.02
        elif label == "settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def kirin_cast(label):
        # Rear up on hind legs, neck upright, head high. v18 round 4: the old peak's +16/+18/+22 on
        # neck_01/neck_02/head bent the already-back-leaning neck further back over the shoulders
        # (see kirin_attack's note on the sign convention). The chest rises (+spine), the neck
        # COUNTER-pitches so it ends near vertical with the head level; forelegs lift and tuck (FK,
        # "_free"); hind feet stay planted (IK) and the hindquarters sink to load the rear.
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "rise":
            p["spine_01"] = (5, 0, 0)
            p["spine_02"] = (10, 0, 0)
            p["neck_01"] = (-4, 0, 0)
            p["head"] = (-4, 0, 0)
            p["_root_drop"] = -0.04
            p["_free"] = set(FRONT_SIDES)
            for s in FRONT_SIDES:
                p[f"leg_{s}_thigh"] = (-12, 0, 0)
                p[f"leg_{s}_shin"] = (20, 0, 0)
        elif label in ("peak", "peak_hold"):
            p["spine_01"] = (14, 0, 0)
            p["spine_02"] = (24, 0, 0)
            p["neck_01"] = (-12, 0, 0)
            p["neck_02"] = (-8, 0, 0)
            p["head"] = (-14, 0, 0)
            p["jaw"] = (JAW_OPEN, 0, 0)
            p["_root_drop"] = -0.08
            p["_free"] = set(FRONT_SIDES)
            for s in FRONT_SIDES:
                p[f"leg_{s}_thigh"] = (-30, 0, 0)
                p[f"leg_{s}_shin"] = (55, 0, 0)
        elif label == "settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def tarasque_cast(label):
        # Hunker down + roar (jaw open, head up) -- the OPPOSITE of rearing; Tarasque's cast
        # lowers its stance, not lifts it.
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "rise":
            p["spine_01"] = (4, 0, 0)
            p["spine_02"] = (6, 0, 0)
            p["head"] = (6, 0, 0)
            p["_root_drop"] = -0.04
            p["jaw"] = (JAW_OPEN * 0.5, 0, 0)
        elif label in ("peak", "peak_hold"):
            p["spine_01"] = (4, 0, 0)
            p["spine_02"] = (6, 0, 0)
            p["neck_01"] = (8, 0, 0)
            p["head"] = (14, 0, 0)  # head up for the roar (round 4: + is up on the Z-up roll)
            p["_root_drop"] = -0.07  # round 4: the hunker is a real body drop, legs bend (IK)
            p["jaw"] = (JAW_OPEN, 0, 0)
        elif label == "settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def basilisk_cast(label):
        # Rise up on the forelegs (a "cobra" push-up), crest/head up, tail curls.
        p = rest_pose()
        tail1 = TAIL_BONES[0] if TAIL_BONES else None
        tail2 = TAIL_BONES[1] if len(TAIL_BONES) > 1 else None
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "rise":
            # v18 round 4: the neck/head no longer pitch +UP on top of the torso rise (on the Z-up
            # roll that folded the already back-leaning neck backward); they counter-pitch so the
            # head stays high and level. Forelegs are planted (IK) and straighten into a real
            # push-up instead of free-swinging FK.
            p["spine_01"] = (4, 0, 0)
            p["spine_02"] = (7, 0, 0)
            p["neck_01"] = (-2, 0, 0)
            p["head"] = (-3, 0, 0)
            if tail1:
                p[tail1] = (0, 0, 10)
        elif label in ("peak", "peak_hold"):
            p["spine_01"] = (10, 0, 0)
            p["spine_02"] = (19, 0, 0)
            p["neck_01"] = (-7, 0, 0)
            p["head"] = (-8, 0, 0)
            p["jaw"] = (JAW_OPEN * 0.6, 0, 0)
            if tail1:
                p[tail1] = (0, 0, 24)
            if tail2:
                p[tail2] = (0, 0, 18)
        elif label == "settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    CAST_FN = {"golem": golem_cast, "kirin": kirin_cast,
               "tarasque": tarasque_cast, "basilisk": basilisk_cast}
    cast_keys = [0.0, 0.30, 0.55, 0.78, 1.0]
    cast_labels = ["neutral", "rise", "peak", "peak_hold", "settle"]
    bake_clip("Cast", 1.2, cast_keys, cast_labels, CAST_FN[creature],
               marker=("peak", "cast_release"))
    reset_all()

    # -----------------------------------------------------------------------
    # Hit (~0.5s, non-looping, marker 'hit_react' at the recoil extreme): generic sharp recoil,
    # scaled by motion_scale -- the brief didn't ask for unique Hit character per creature.
    # -----------------------------------------------------------------------
    def hit_pose(label):
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "recoil":
            # round 4: these were negative = nose DOWN on the quadruped Z-up roll, which drove
            # Tarasque's jaw 11 cm and Basilisk's 7 cm through the floor; a recoil flinches the head
            # UP and BACK, body sinking into the planted legs.
            p["spine_01"] = (3 * MS, 0, 0)
            p["spine_02"] = (6 * MS, 0, 0)
            p["neck_01"] = (6 * MS, 0, 0)
            p["head"] = (10 * MS, 0, 0)
            p["_root_drop"] = -0.03
            if TAIL_BONES:
                p[TAIL_BONES[0]] = (6 * MS, 0, 0)
            p["jaw"] = (JAW_CLOSED - 5, 0, 0)
        elif label == "settle_start":
            p["spine_01"] = (1 * MS, 0, 0)
            p["spine_02"] = (2 * MS, 0, 0)
            p["neck_01"] = (2 * MS, 0, 0)
            p["head"] = (3 * MS, 0, 0)
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "recover":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    hit_keys = [0.0, 0.22, 0.42, 1.0]
    hit_labels = ["neutral", "recoil", "settle_start", "recover"]
    bake_clip("Hit", 0.5, hit_keys, hit_labels, hit_pose,
               marker=("recoil", "hit_react"), sharpen_frame_key=1)
    reset_all()
    # Clear the assigned action before any manual pose-and-measure work below: gait.py's own
    # docstring (set_bone_world_matrix_direct) documents that view_layer.update() re-evaluates
    # whatever action is CURRENTLY assigned onto the pose bones at the scene's current frame,
    # silently clobbering a manually-set pb.rotation_euler before it can be read back -- exactly
    # what the KO measurement below does (pose, update, measure the evaluated mesh). With Hit's action still assigned here,
    # that would make every KO measurement read Hit's own posed values instead of the
    # angle this loop just tried. None keeps the depsgraph from re-driving the pose from any action
    # at all, so a direct `pb.rotation_euler = ...` + view_layer.update() is measured honestly.
    arm_obj.animation_data.action = None

    # -----------------------------------------------------------------------
    # KO (~1.4s, non-looping, held lying-down final pose).
    # v18 round 4 (lead review: Golem KO's legs melted into flat sheets, Basilisk KO "barely
    # changes"). Root cause: the old KO dropped the root by 85% of the shortest leg's reach and then
    # grid-searched thigh/shin PITCH until each toe tip touched z=0 -- for stumpy legs under a low
    # belly that folds the leg forward/back THROUGH the already-sunk body (Golem: whole-mesh min Z
    # -0.45, i.e. 45 cm of mesh below the floor), and the head/neck stayed up. New KO, every number
    # measured from this rig's own evaluated mesh rather than hand-tuned:
    #   * the body drops until its lowest TORSO vertex rests on the ground (iterated, since the legs
    #     splay differently as the hips get lower);
    #   * each leg is splayed out sideways (forelegs forward-out, hind legs back-out) by the same
    #     planted-foot IK as the other clips, the ankle placed on the ground at its rest height --
    #     a sprawled lie-down like the Griffin's, never a fold through the torso;
    #   * neck/head pitch is binary-searched until the head rests on the ground; tail pitch likewise;
    #   * a final whole-mesh check lifts the body if anything still sits more than 8 mm below Z=0.
    # -----------------------------------------------------------------------
    mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
    _vg = {g.index: g.name for g in mesh_obj.vertex_groups}

    def region(pred, thresh=0.5, max_leg=1.0):
        out = []
        for v in mesh_obj.data.vertices:
            legw = sum(g.weight for g in v.groups if _vg.get(g.group, "").startswith("leg_"))
            if legw <= max_leg and                     sum(g.weight for g in v.groups if pred(_vg.get(g.group, ""))) >= thresh:
                out.append(v.index)
        return out

    # Torso = belly/chest skin only: no leg weight at all (scapula-weighted upper-leg skin moves down
    # with the splaying legs and would otherwise cap the drop).
    TORSO_IDS = region(lambda n: n in ("pelvis", "spine_01", "spine_02", "neck_01")
                       or n.startswith("scapula_"), max_leg=0.02)
    HEAD_IDS = region(lambda n: n in ("neck_02", "head", "snout", "jaw"))
    TAIL_IDS = region(lambda n: n.startswith("tail_"))

    def eval_min_z(id_lists):
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        eo = mesh_obj.evaluated_get(dg)
        me = eo.to_mesh()
        mw = eo.matrix_world
        zs = [(mw @ v.co).z for v in me.vertices]
        eo.to_mesh_clear()
        return [min(zs[i] for i in ids) if ids else 0.0 for ids in id_lists], min(zs)

    KO_SPLAY_OUT = CP.get("ko_splay_out", 0.6)  # sideways share of the splay direction

    def splay_target(side, frac):
        d = LEG_IK[side]
        th = arm_obj.pose.bones[f"leg_{side}_thigh"]
        pw = fk_world(th.parent)
        hip = (pw @ th.parent.bone.matrix_local.inverted() @ th.bone.matrix_local).translation
        rest = d["ankle_rest"]
        out = mathutils.Vector((1.0 if rest.x >= 0 else -1.0, 0, 0))
        fb = FORWARD if side.startswith("F") else -FORWARD
        dirn = (out * KO_SPLAY_OUT + fb * (1.0 - KO_SPLAY_OUT)).normalized()
        reach = 0.92 * (d["L1"] + d["L2"])
        h = max(0.0, hip.z - rest.z)
        horiz = math.sqrt(max(reach * reach - h * h, 0.0))
        flat = mathutils.Vector((hip.x, hip.y, rest.z)) + dirn * horiz
        return rest.lerp(flat, frac)

    KO_MODE = CP.get("ko_mode", "belly")
    KO_ROLL = CP.get("ko_roll", 0.0)

    def ko_apply(drop, splay, head_deg, tail_deg, wobble, roll_frac=0.0):
        reset_all()
        p = rest_pose()
        p["jaw"] = (JAW_CLOSED, 0, 0)
        p["spine_01"] = (0, 0, 3 * wobble)
        p["spine_02"] = (0, 0, 4 * wobble)
        p["neck_01"] = (0.4 * head_deg, 0, 2 * wobble)
        p["neck_02"] = (0.3 * head_deg, 0, 0)
        p["head"] = (0.3 * head_deg, 0, 0)
        # Tail: when the base has to LIFT to clear the ground (Basilisk: long tail lying low), the
        # next bone counter-bends so the rest of the tail lies flat instead of sticking up.
        for i, tb in enumerate(TAIL_BONES[:2]):
            k = 1.0 if i == 0 else (-0.85 if tail_deg > 0 else 0.5)
            p[tb] = (tail_deg * k, 0, 4 * wobble)
        apply_pose(p)
        root = arm_obj.pose.bones["root"]
        root.location = (0, drop, 0)
        # root's local Z is the walk axis (root bone points straight up), so this rolls the whole
        # body about FORWARD, pivoting at the ground under the body centre.
        root.rotation_euler = (0, 0, math.radians(KO_ROLL * roll_frac))
        if KO_MODE == "side":
            # Tipped over: legs hang free (FK), stumps relax a little toward the body.
            for side in leg_sides:
                sgn = 1.0 if LEG_IK[side]["ankle_rest"].x >= 0 else -1.0
                arm_obj.pose.bones[f"leg_{side}_thigh"].rotation_euler = (
                    0, 0, math.radians(-8 * sgn * splay))
        else:
            for side in leg_sides:
                plant_leg(side, splay_target(side, splay))

    def ko_settle_on_ground(drop, *args, **kw):
        """Re-apply the pose, lifting/lowering the body until the whole mesh's lowest point is at
        +0.003 (never more than 8 mm below Z=0 -- the lead's hard rule)."""
        for _ in range(6):
            ko_apply(drop, *args, **kw)
            _, lo = eval_min_z([])
            if abs(lo - 0.003) < 0.002:
                break
            drop -= (lo - 0.003)
        return drop, lo

    def bisect(fn, lo, hi, iters=14):
        """Root of increasing fn on [lo, hi] (clamped to an end if no sign change)."""
        flo, fhi = fn(lo), fn(hi)
        if flo >= 0:
            return lo
        if fhi <= 0:
            return hi
        for _ in range(iters):
            mid = 0.5 * (lo + hi)
            if fn(mid) < 0:
                lo = mid
            else:
                hi = mid
        return 0.5 * (lo + hi)

    arm_obj.animation_data.action = None
    KO_DROP = 0.0
    if KO_MODE == "side":
        KO_HEAD, KO_TAIL = -12.0, 0.0
        KO_DROP, all_min = ko_settle_on_ground(0.0, 1.0, KO_HEAD, KO_TAIL, 1.0, roll_frac=1.0)
    else:
        for _ in range(6):
            ko_apply(KO_DROP, 1.0, 0.0, 0.0, 1.0)
            (torso_z,), _ = eval_min_z([TORSO_IDS])
            KO_DROP -= (torso_z - 0.006)
        KO_HEAD = bisect(lambda th: (ko_apply(KO_DROP, 1.0, th, 0.0, 1.0)
                                     or eval_min_z([HEAD_IDS])[0][0]) - 0.008, -70.0, 30.0)
        KO_TAIL = bisect(lambda th: (ko_apply(KO_DROP, 1.0, KO_HEAD, th, 1.0)
                                     or eval_min_z([TAIL_IDS])[0][0]) - 0.006, -15.0, 60.0) \
            if TAIL_IDS else 0.0
        ko_apply(KO_DROP, 1.0, KO_HEAD, KO_TAIL, 1.0)
        _, all_min = eval_min_z([])
        if all_min < -0.006:
            KO_DROP, all_min = ko_settle_on_ground(KO_DROP, 1.0, KO_HEAD, KO_TAIL, 1.0)
    print(f"KO ({creature}): mode={KO_MODE} root_drop={KO_DROP:.3f} head_pitch={KO_HEAD:.1f} "
          f"tail_pitch={KO_TAIL:.1f} final_min_z={all_min:.4f} torso/head/tail verts="
          f"{len(TORSO_IDS)}/{len(HEAD_IDS)}/{len(TAIL_IDS)}")

    # (label, frac, drop_frac, splay_frac, head_frac, tail_frac, wobble)
    KO_KEYS = [("neutral", 0.0, 0.0, 0.0, 0.0, 0.0, 0.0),
               ("stagger", 0.15, 0.0, 0.0, -0.15, 0.0, 1.0),
               ("sink", 0.45, 0.45, 0.4, 0.4, 0.4, 1.0),
               # extra IK-solved key: Bezier between two IK poses is not itself IK-consistent, and
               # with only sink->settle the in-between frames dipped up to 9 cm (Kirin, measured)
               ("sink2", 0.55, 0.62, 0.58, 0.58, 0.58, 1.0),
               ("slump", 0.65, 0.78, 0.75, 0.75, 0.75, 1.0),
               ("settle", 0.85, 1.0, 1.0, 1.0, 1.0, 1.0),
               ("final_hold", 1.0, 1.0, 1.0, 1.0, 1.0, 1.0)]
    ko_frames = int(round(1.4 * FPS))
    # Baked EVERY frame (not 5-7 Bezier keys): the parameters (drop/splay/head/tail/wobble/roll
    # fractions) are smoothstep-interpolated between the KO_KEYS rows and each frame is IK-solved
    # and settled on the ground on its own -- Bezier between two IK poses is not itself IK-
    # consistent and left hoof tips 3-9 cm under the floor between keys (dense check, Kirin).
    ROLL_FRAC = {"neutral": 0.0, "stagger": 0.08, "sink": 0.5, "sink2": 0.65, "slump": 0.8}

    def ko_params(t):
        rows = [(fr, df, sf, hf, tf, wob, ROLL_FRAC.get(lb, 1.0) if KO_MODE == "side" else 0.0)
                for lb, fr, df, sf, hf, tf, wob in KO_KEYS]
        for r0, r1 in zip(rows, rows[1:]):
            if t <= r1[0] + 1e-9:
                u = 0.0 if r1[0] - r0[0] < 1e-9 else (t - r0[0]) / (r1[0] - r0[0])
                u = u * u * (3 - 2 * u)
                return [x0 + (x1 - x0) * u for x0, x1 in zip(r0[1:], r1[1:])]
        return list(rows[-1][1:])

    snaps = []
    for f in range(1, ko_frames + 2):
        df, sf, hf, tf, wob, rf = ko_params((f - 1) / ko_frames)
        d_f = KO_DROP * df
        ko_apply(d_f, sf, KO_HEAD * hf, KO_TAIL * tf, wob, roll_frac=rf)
        _, lo = eval_min_z([])
        if lo < -0.004:
            ko_settle_on_ground(d_f, sf, KO_HEAD * hf, KO_TAIL * tf, wob, roll_frac=rf)
        snaps.append((f, {n: tuple(arm_obj.pose.bones[n].rotation_euler) for n in ALL_POSE_BONES},
                      tuple(arm_obj.pose.bones["root"].location),
                      tuple(arm_obj.pose.bones["root"].rotation_euler)))
    ko_action = bpy.data.actions.new("KO")
    ko_action.use_fake_user = True
    arm_obj.animation_data.action = ko_action
    root_pb = arm_obj.pose.bones["root"]
    root_pb.rotation_mode = "XYZ"
    for f, rots, root_loc, root_rot in snaps:
        scene.frame_set(f)
        for n, r in rots.items():
            arm_obj.pose.bones[n].rotation_euler = r
            arm_obj.pose.bones[n].scale = (1, 1, 1)
        root_pb.location = root_loc
        root_pb.rotation_euler = root_rot
        keyframe_pose(f, ALL_POSE_BONES)
        root_pb.keyframe_insert(data_path="location", frame=f)
        root_pb.keyframe_insert(data_path="rotation_euler", frame=f)
    common.set_interpolation(ko_action, "LINEAR")
    root_pb.location = (0, 0, 0)
    root_pb.rotation_euler = (0, 0, 0)
    print(f"KO ACTION ({creature}): {len(KO_KEYS)} key rows baked per-frame over {ko_frames} frames @ {FPS}fps "
          f"(non-looping, held lying-down final pose)")
    reset_all()

    # -----------------------------------------------------------------------
    # Victory (~2.0s, loop-friendly end): distinct per-creature triumphant beat.
    # -----------------------------------------------------------------------
    def golem_victory(label):
        # Slow, proud, single-foot stomp (twice) -- no rear, no flourish, just weight.
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "beat1":
            p["spine_02"] = (-6, 0, 0)
            p["head"] = (-4, 0, 0)
            p["_lift"] = {s: (0.09, 0.02) for s in FRONT_SIDES[:1]}
        elif label == "hold1":
            p["spine_02"] = (-4, 0, 0)
            p["head"] = (-4, 0, 0)
        elif label == "beat2":
            p["spine_02"] = (-6, 0, 0)
            p["head"] = (-6, 0, 0)
            p["_lift"] = {s: (0.09, 0.02) for s in FRONT_SIDES[1:2]}
        elif label == "hold2":
            p["spine_02"] = (-5, 0, 0)
            p["head"] = (-5, 0, 0)
        elif label == "proud_settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def kirin_victory(label):
        # A small prance (light alternating leg lifts) + head toss.
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "beat1":
            p["spine_02"] = (-6, 0, 0)
            p["neck_01"] = (-4, 0, 0)
            p["head"] = (-6, 0, 0)
            p["_lift"] = {s: (0.12, 0.05) for s in FRONT_SIDES[:1]}  # prance
            p["tail_01"] = (-6, 0, 2)
        elif label == "hold1":
            p["spine_02"] = (-4, 0, 0)
        elif label == "beat2":
            p["spine_02"] = (-6, 0, 0)
            p["neck_01"] = (-4, 0, 0)
            p["head"] = (-6, 0, 0)
            p["_lift"] = {s: (0.12, 0.05) for s in FRONT_SIDES[1:2]}
            p["tail_01"] = (6, 0, -2)
        elif label == "hold2":
            p["spine_02"] = (-4, 0, 0)
        elif label == "proud_settle":
            # v18 round-3 fix (lead review: Kirin's Victory failed verify.py's loop-seam gate at 20
            # degrees). Settle EXACTLY back to neutral/rest here (not holding the head-toss), same
            # pattern as the Griffin's own victory_pose's proud_settle -- see its comment: the loop-
            # seam gate checks the LAST baked frame against frame 0 (<0.5 deg max bone delta
            # tolerance), so the final key pose must actually return to rest, not just read as
            # "close enough" by eye. `p` is already rest_pose() from the top of this function; only
            # jaw needs setting.
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def tarasque_victory(label):
        # A grumpy huff (head shake side to side) + shell shake (pelvis roll wobble).
        p = rest_pose()
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "beat1":
            p["head"] = (0, 0, 10)
            # v18 round-3 fix (lead review: "Tarasque Cast/Victory ground penetration"): pelvis is
            # the root of the WHOLE spine chain (root->pelvis->spine_01->spine_02->...), so rolling
            # IT tilts every leg's effective ground height, not just the hind legs parented directly
            # to it -- Tarasque's own rest-pose toe clearance is unusually thin (3-13mm, confirmed
            # directly) for a short-legged, heavy-shelled creature, so the original 8 degree "shell
            # shake" roll was enough to dip a leg through the ground on the low side of the tilt.
            # Reduced to 3 degrees -- still a visible shell wobble, within the thin clearance budget.
            p["pelvis"] = (0, 3, 0)
        elif label == "hold1":
            p["head"] = (0, 0, 6)
        elif label == "beat2":
            p["head"] = (0, 0, -10)
            p["pelvis"] = (0, -3, 0)
        elif label == "hold2":
            p["head"] = (0, 0, -6)
        elif label == "proud_settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    def basilisk_victory(label):
        # Tail swish + head bob.
        p = rest_pose()
        tail1 = TAIL_BONES[0] if TAIL_BONES else None
        tail2 = TAIL_BONES[1] if len(TAIL_BONES) > 1 else None
        if label == "neutral":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        elif label == "beat1":
            p["head"] = (-8, 0, 0)
            if tail1:
                p[tail1] = (0, 0, 22)
            if tail2:
                p[tail2] = (0, 0, 16)
        elif label == "hold1":
            p["head"] = (-4, 0, 0)
        elif label == "beat2":
            p["head"] = (4, 0, 0)
            if tail1:
                p[tail1] = (0, 0, -22)
            if tail2:
                p[tail2] = (0, 0, -16)
        elif label == "hold2":
            p["head"] = (-2, 0, 0)
        elif label == "proud_settle":
            p["jaw"] = (JAW_CLOSED, 0, 0)
        return p

    VICTORY_FN = {"golem": golem_victory, "kirin": kirin_victory,
                  "tarasque": tarasque_victory, "basilisk": basilisk_victory}
    victory_keys = [0.0, 0.2, 0.36, 0.52, 0.68, 1.0]
    victory_labels = ["neutral", "beat1", "hold1", "beat2", "hold2", "proud_settle"]
    bake_clip("Victory", 2.0, victory_keys, victory_labels, VICTORY_FN[creature])
    reset_all()

    arm_obj.animation_data.action = None
    bpy.ops.object.mode_set(mode="OBJECT")

    with open(os.path.join(OUT, "keyed_event_markers.json"), "w") as f:
        json.dump({"markers": EVENT_MARKERS, "loop": CLIP_LOOP}, f, indent=2)
    print(f"EVENT MARKERS ({creature}): {json.dumps(EVENT_MARKERS)}")

    blend_out = os.path.join(OUT, f"{creature}_keyed.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_out)
    print(f"SAVED {blend_out}")


# ---------------------------------------------------------------------------
# v19: birds (rig_templates/winged_biped.py) -- Phoenix (stands, planted feet) and Thunderbird
# (hovers, never stands). Same six clips. Every clip is DATA below (BIRD_CLIPS): sparse key poses
# in anim/bird_pose.py's semantic channels (pitch/turn/bank, wing flap/sweep), optional extra key
# tracks, and continuous sine layers (a hovering flap, breathing, crest flicker). One builder
# (build_bird_clips) bakes any of them per frame -- per-frame so the planted-foot IK (Phoenix) and
# KO's ground settle are exact on every frame (v18 round 4's lesson: Bezier between IK poses is not
# itself IK-consistent). Root channels: x/y/z world offsets, "f" = distance along FORWARD,
# pitch/turn/bank about the root's head. Pose keys starting "_" are builder directives.
# ---------------------------------------------------------------------------
def _W(flap=0.0, sweep=0.0, f2=0.0, s2=0.0, f3=0.0):
    """Both wings, mirrored: wing_*_01 flap/sweep, wing_*_02 flap/sweep, wing_*_03 flap."""
    out = {}
    for s in ("L", "R"):
        out[f"wing_{s}_01"] = {"flap": flap, "sweep": sweep}
        out[f"wing_{s}_02"] = {"flap": f2, "sweep": s2}
        out[f"wing_{s}_03"] = {"flap": f3}
    return out


def _P(*dicts, **bones):
    """Merge pose dicts; keyword bones given as (pitch, turn, bank) tuples or channel dicts."""
    out = {}
    for d in dicts:
        for k, v in d.items():
            out[k] = v
    for k, v in bones.items():
        if k == "jaw" or k.startswith("_") or isinstance(v, dict):
            out[k] = v
        else:
            out[k] = dict(zip(("pitch", "turn", "bank"), v))
    return out


def _tr_arms(L, R):
    """Treant (humanoid) arm pose: per side (shoulder flex, shoulder abd, elbow flex, wrist flex),
    abd/flex relative to the rest A-pose (abd -12 = the relaxed hang)."""
    out = {}
    for s_, (fl, ab, el, wr) in (("L", L), ("R", R)):
        out[f"arm_{s_}_upper"] = {"flex": fl, "abd": ab}
        out[f"arm_{s_}_fore"] = {"flex": el}
        out[f"arm_{s_}_hand"] = {"flex": wr}
    return out


_TR_REL = _tr_arms((0, -12, 14, 6), (0, -12, 14, 6))  # relaxed hang = gait.py's _TREANT_HOLD


_TB_TAIL_SWAY = [("b", "tail_01", "pitch", 2.5, 2, -1.2), ("b", "tail_02", "pitch", 3.5, 2, -1.8),
                 ("b", "tail_03", "pitch", 4.5, 2, -2.4), ("b", "tail_04", "pitch", 5.5, 2, -3.0),
                 ("b", "tail_03", "turn", 3.0, 1, 0.3), ("b", "tail_04", "turn", 5.0, 1, -0.5),
                 ("b", "leg_BL_thigh", "pitch", 3.0, 2, -0.5), ("b", "leg_BR_thigh", "pitch", 3.0, 2, -0.5)]
_TB_LEGS = ("BL", "BR")
# Phoenix hover follow-through: flame tail trailing and swaying, crest flicker, a small leg dangle.
_PH_SWAY = [("b", "tail_01", "pitch", 3.0, 2, -1.2), ("b", "tail_02", "pitch", 4.0, 2, -1.8),
            ("b", "tail_03", "pitch", 5.0, 2, -2.4), ("b", "tail_01", "turn", 5.0, 1, 0.4),
            ("b", "tail_02", "turn", 7.0, 1, -0.2), ("b", "tail_03", "turn", 9.0, 1, -0.8),
            ("b", "leg_BL_thigh", "pitch", 3.0, 2, -0.5), ("b", "leg_BR_thigh", "pitch", 3.0, 2, -0.5),
            ("b", "crest_01", "pitch", 3.0, 2, 0.0), ("b", "crest_02", "pitch", 7.0, 3, 0.5),
            ("b", "crest_02", "turn", 5.0, 2, 1.0)]

BIRD_PARAMS = {
    # v19 round 2 (producer change): the Phoenix flies like the Thunderbird -- the same hover
    # builder path, Phoenix data. `hold` = bird_pose.HOVER_HOLDS (legs tucked, half-folded wings
    # held more open), added to every clip incl. KO. Flap: wing_*_02 swings a quarter-cycle ahead of
    # wing_*_01 (lag -pi/2) so the wing spreads flat on the downbeat and folds on the upstroke.
    "phoenix": dict(
        hover=True, planted=False, hold="phoenix",
        clips={
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})],
                         flap=dict(cycles=2, amps=(32.0, 30.0, 8.0), lags=(0.0, -1.57, -1.0),
                                   sweep=4.0),
                         bob=(0.03, 0.025, 2), layers=_PH_SWAY + [
                             ("b", "head", "turn", 5.0, 1, 0.8), ("b", "head", "pitch", -1.5, 2, 0.0),
                             ("b", "spine_02", "pitch", 1.5, 2, -1.0)]),
            # Airborne lunge/dive: rear up and back (wings raised and folding, beak open), dive
            # forward-down onto the beak strike with the wings buffeting forward-down, recover to
            # the hover.
            "Attack": dict(seconds=1.05, sharp=2,
                           flap=dict(cycles=2, amps=(8.0, 6.0, 3.0), lags=(0.0, -1.57, -1.0)),
                           layers=_PH_SWAY, keys=[
                (0.0, {}),
                (0.30, _P(_W(32, -15, 28, 0, 8), spine_02=(6, 0, 0), neck_01=(8, 0, 0),
                          head=(8, 0, 0), tail_01=(-10, 0, 0), jaw=-16,
                          _root={"z": 0.09, "f": -0.06, "pitch": 12})),
                (0.48, _P(_W(-32, 35, -25, 10, -8), spine_02=(-6, 0, 0), neck_01=(4, 0, 0),
                          head=(-8, 0, 0), tail_01=(-25, 0, 0), tail_02=(-10, 0, 0), jaw=3,
                          _root={"z": -0.03, "f": 0.24, "pitch": -32})),
                (0.66, _P(_W(-14, 18, -12, 4), neck_01=(4, 0, 0), head=(-4, 0, 0),
                          tail_01=(-15, 0, 0), _root={"z": -0.01, "f": 0.16, "pitch": -20})),
                (1.0, {})]),
            # Wings flare wide and UP, head thrown back, beak open, crest rises; then one
            # flame-burst downbeat (cast_release) that pops the body up with the tail flaring.
            "Cast": dict(seconds=1.2, sharp=2, marker=(2, "cast_release"),
                         flap=dict(cycles=2, amps=(4.0, 3.0, 2.0), lags=(0.0, -1.57, -1.0)),
                         layers=_PH_SWAY + [("b", "crest_02", "pitch", 5.0, 4, 0.0)], keys=[
                (0.0, {}),
                (0.40, _P(_W(48, -6, -15, 0, -6), spine_01=(4, 0, 0), spine_02=(7, 0, 0),
                          neck_01=(6, 0, 0), head=(14, 0, 0), jaw=-18,
                          crest_01={"pitch": -8, "sy": 1.25}, crest_02={"pitch": 6, "sy": 1.2},
                          _root={"z": 0.05})),
                (0.52, _P(_W(-42, 12, -30, 0, -10), spine_02=(-4, 0, 0), head=(-6, 0, 0),
                          jaw=-22, crest_01={"pitch": -4, "sy": 1.3}, crest_02={"sy": 1.25},
                          tail_01=(-20, 0, 0), tail_02=(-12, 0, 0), _root={"z": 0.13})),
                (0.72, _P(_W(-22, 6, -12, 0, -6), head=(-3, 0, 0), jaw=-8,
                          crest_01={"sy": 1.1}, tail_01=(-8, 0, 0), _root={"z": 0.10})),
                (1.0, {})]),
            # Knocked back in the air: shoved backward and up, body pitched back, wings flung up.
            "Hit": dict(seconds=0.5, sharp=1, marker=(1, "hit_react"),
                        flap=dict(cycles=1, amps=(10.0, 8.0, 4.0), lags=(0.0, -1.57, -1.0)),
                        layers=_PH_SWAY, keys=[
                (0.0, {}),
                (0.22, _P(_W(30, -25, 20), head=(-12, 0, 0), neck_01=(-6, 0, 0),
                          crest_02=(14, 0, 0), leg_BL_thigh=(-20, 0, 0), leg_BR_thigh=(-20, 0, 0),
                          tail_01=(12, 0, 0), jaw=-10,
                          _root={"f": -0.15, "z": 0.12, "pitch": 20, "bank": 6})),
                (0.45, _P(_W(12, -10, 8), head=(-6, 0, 0), leg_BL_thigh=(-8, 0, 0),
                          leg_BR_thigh=(-8, 0, 0), _root={"f": -0.10, "z": 0.08, "pitch": 10})),
                (1.0, {})]),
            # Rising SPIRAL (deliberately unlike Cast's stationary wings-up flare): climbs high
            # while turning a full 360 banked into the turn, wings held spread flat and wide, the
            # flame tail fanning side to side; settles back to the start pose (loops).
            "Victory": dict(seconds=2.0,
                            flap=dict(cycles=4, amps=(14.0, 8.0, 4.0), lags=(0.0, -1.57, -1.0)),
                            layers=[("b", "tail_01", "turn", 14.0, 3, 0.0),
                                    ("b", "tail_02", "turn", 18.0, 3, -0.6),
                                    ("b", "tail_03", "turn", 22.0, 3, -1.2),
                                    ("b", "crest_02", "pitch", 8.0, 4, 0.0),
                                    ("b", "crest_02", "turn", 6.0, 2, 0.5)], keys=[
                (0.0, {}),
                (0.2, _P(_W(4, 0, -28, 0, -8), head=(6, 0, 0), jaw=-10, tail_01=(-25, 0, 0),
                         tail_02=(-10, 0, 0), _root={"z": 0.14, "bank": -18, "pitch": -8})),
                (0.5, _P(_W(6, 0, -30, 0, -8), head=(8, 0, 0), jaw=-12, tail_01=(-30, 0, 0),
                         tail_02=(-12, 0, 0), _root={"z": 0.30, "bank": -22, "pitch": -8})),
                (0.8, _P(_W(4, 0, -26, 0, -6), head=(5, 0, 0), jaw=-6, tail_01=(-20, 0, 0),
                         _root={"z": 0.14, "bank": -14, "pitch": -6})),
                (1.0, {})],
                tracks=[[(0.0, {}), (0.12, {}), (0.88, {"_root": {"turn": 360.0}}),
                         (1.0, {"_root": {"turn": 360.0}})]]),
            # Falls out of the hover (starts at the hover height the engine stops adding for KO),
            # flails, pitches nose-down and lies on its belly with the wings splayed out flat.
            "KO": dict(seconds=1.4, ko=True, impact=0.55, keys=[
                (0.0, {"_root": {"z": "hover"}}),
                (0.20, _P(_W(30, -10, 15), head=(10, 0, 0), crest_02=(12, 0, 0),
                          _root={"z": "hover+0.04", "pitch": 12, "bank": 8})),
                (0.55, _P(_W(-15, -15, -25, 0, -10), neck_01=(-6, 0, 0), head=(-10, 0, 0),
                          tail_01=(-25, 0, 0), _root={"pitch": -80})),
                (0.84, _P(_W(-22, -20, -30, 0, -12), neck_01=(-10, 0, 0), head=(-16, 0, 0),
                          tail_01=(-30, 0, 0), jaw=-6, _root={"pitch": -84})),
                (1.0, "same")]),
        }),
    "thunderbird": dict(
        hover=True, planted=False,
        clips={
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})],
                         flap=dict(cycles=2, amps=(18.0, 10.0, 8.0), sweep=4.0),
                         bob=(0.03, 0.025, 2), layers=_TB_TAIL_SWAY + [
                             ("b", "head", "turn", 5.0, 1, 0.8), ("b", "head", "pitch", -1.5, 2, 0.0)]),
            # Dive-strike: rise and rear back (wings up, talons tucked), then dive forward-down
            # with the body pitched nose-down, wings swept back and the talons thrown forward.
            "Attack": dict(seconds=1.05, sharp=2, flap=dict(cycles=2, amps=(9.0, 5.0, 4.0)),
                           layers=_TB_TAIL_SWAY, keys=[
                (0.0, {}),
                (0.30, _P(_W(30, -10, 12), neck_01=(-10, 0, 0), head=(-4, 0, 0),
                          leg_BL_thigh=(-15, 0, 0), leg_BR_thigh=(-15, 0, 0), jaw=-10,
                          _root={"z": 0.10, "f": -0.06, "pitch": 14})),
                (0.48, _P(_W(-15, -35, -10), neck_01=(16, 0, 0), head=(10, 0, 0),
                          leg_BL_thigh=(70, 0, 0), leg_BR_thigh=(70, 0, 0),
                          leg_BL_shin=(-15, 0, 0), leg_BR_shin=(-15, 0, 0), jaw=-18,
                          tail_01=(-10, 0, 0), _root={"z": -0.04, "f": 0.24, "pitch": -35})),
                (0.66, _P(_W(-5, -20, -4), neck_01=(10, 0, 0), head=(6, 0, 0),
                          leg_BL_thigh=(50, 0, 0), leg_BR_thigh=(50, 0, 0),
                          _root={"z": -0.02, "f": 0.20, "pitch": -25})),
                (1.0, {})]),
            # Thunder clap: wings drawn up high, then one sharp downbeat (the cast_release marker)
            # that pops the body upward.
            "Cast": dict(seconds=1.2, sharp=2, marker=(2, "cast_release"),
                         flap=dict(cycles=2, amps=(5.0, 3.0, 2.0)), layers=_TB_TAIL_SWAY, keys=[
                (0.0, {}),
                (0.38, _P(_W(45, -10, 20, 0, 10), head=(10, 0, 0), _root={"z": 0.05})),
                (0.50, _P(_W(-40, 12, -25, 0, -15), head=(-6, 0, 0), jaw=-16,
                          tail_01=(8, 0, 0), tail_02=(6, 0, 0), _root={"z": 0.12})),
                (0.70, _P(_W(-25, 6, -12, 0, -6), head=(-3, 0, 0), jaw=-8, _root={"z": 0.10})),
                (1.0, {})]),
            # Knocked back in the air: shoved backward and up, body pitched back, wings flailing.
            "Hit": dict(seconds=0.5, sharp=1, marker=(1, "hit_react"),
                        flap=dict(cycles=1, amps=(10.0, 6.0, 5.0)), layers=_TB_TAIL_SWAY, keys=[
                (0.0, {}),
                (0.22, _P(_W(30, -25, 15), head=(-12, 0, 0), leg_BL_thigh=(25, 0, 0),
                          leg_BR_thigh=(25, 0, 0), tail_01=(-8, 0, 0), jaw=-10,
                          _root={"f": -0.14, "z": 0.05, "pitch": 22, "bank": 6})),
                (0.45, _P(_W(12, -10, 6), head=(-6, 0, 0), leg_BL_thigh=(10, 0, 0),
                          leg_BR_thigh=(10, 0, 0), _root={"f": -0.10, "z": 0.04, "pitch": 12})),
                (1.0, {})]),
            # Rising spiral flourish: climbs while turning a full 360 with wings flared, settles
            # back to the start pose (loops).
            "Victory": dict(seconds=2.0, flap=dict(cycles=3, amps=(16.0, 10.0, 8.0)),
                            layers=_TB_TAIL_SWAY, keys=[
                (0.0, {}),
                (0.2, _P(_W(30, 0, 15, 0, 6), head=(12, 0, 0), jaw=-12, _root={"z": 0.10})),
                (0.5, _P(_W(36, 0, 18, 0, 8), head=(14, 0, 0), jaw=-14, _root={"z": 0.18})),
                (0.8, _P(_W(28, 0, 12), head=(8, 0, 0), jaw=-6, _root={"z": 0.10})),
                (1.0, {})],
                tracks=[[(0.0, {}), (0.12, {}), (0.88, {"_root": {"turn": 360.0}}),
                         (1.0, {"_root": {"turn": 360.0}})]]),
            # Falls out of the air (starts at the hover height the engine stops adding for KO),
            # tumbles nose-down and lies belly-down with the wings splayed flat.
            "KO": dict(seconds=1.4, ko=True, impact=0.55, keys=[
                (0.0, {"_root": {"z": "hover"}}),
                (0.20, _P(_W(35, -10, 15), head=(-10, 0, 0), leg_BL_thigh=(20, 0, 0),
                          leg_BR_thigh=(20, 0, 0), _root={"z": "hover+0.04", "pitch": 15, "bank": 8})),
                (0.55, _P(_W(-40, -10, -10), neck_01=(20, 0, 0), head=(-5, 0, 0),
                          _root={"pitch": -80})),
                (0.84, _P(_W(-48, -12, -12, 0, -5), neck_01=(14, 0, 0), head=(-10, 0, 0),
                          jaw=-6, _root={"pitch": -84})),
                (1.0, "same")]),
        }),
}


# ---------------------------------------------------------------------------
# v20 (anim-last): Frost Wyrm (quadruped template), Treant (biped_arms template) and Leviathan
# (serpent template) reuse the bird builder below: every clip is DATA (sparse semantic key poses +
# sine layers in anim/bird_pose.py channels, converted through each bone's rest matrix, so they never
# depend on bone roll), baked per frame with planted-foot IK. Extra spec keys used only by these
# entries are documented where build_bird_clips reads them ("ko_settle").
# ---------------------------------------------------------------------------
def _legs(pitch_f, pitch_b, bank=0.0, sh_f=0.0, sh_b=0.0):
    """Four-leg semantic splay: front thighs pitch `pitch_f` (+ = tip forward for a hanging leg),
    hind thighs `pitch_b`, left legs bank outward (-bank), right legs outward (+bank)."""
    out = {}
    for side in ("FL", "FR", "BL", "BR"):
        front = side.startswith("F")
        b = -bank if side.endswith("L") else bank
        out[f"leg_{side}_thigh"] = {"pitch": pitch_f if front else pitch_b, "bank": b}
        out[f"leg_{side}_shin"] = {"pitch": sh_f if front else sh_b}
    return out


_FW_TAIL_IDLE = [("b", "tail_01", "turn", 3.0, 1, 0.0), ("b", "tail_02", "turn", 4.0, 1, -0.5),
                 ("b", "tail_03", "turn", 5.0, 1, -1.0), ("b", "tail_04", "turn", 7.0, 1, -1.5),
                 ("b", "tail_04", "pitch", -3.0, 2, -0.8)]
LAST_PARAMS = {
    # Wise, wry: calm, deliberate, a little smug. Feet planted (IK) except KO.
    "frost_wyrm": dict(
        hover=False, planted=True,
        clips={
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})],
                         bob=(-0.012, 0.006, 2), layers=_FW_TAIL_IDLE + [
                             ("b", "spine_02", "pitch", 1.2, 2, 0.0), ("b", "neck_01", "pitch", -1.0, 2, 0.3),
                             ("b", "head", "turn", 5.0, 1, 0.8), ("b", "head", "pitch", 2.0, 2, 1.0),
                             ("b", "jaw", "pitch", -1.5, 2, 0.5)]),
            # Lunge + frost-breath head thrust: draw back (head up, jaw parting), then a sharp
            # forward lunge with the neck thrust out and the jaw wide (the breath), held, recover.
            "Attack": dict(seconds=1.15, sharp=2, layers=_FW_TAIL_IDLE, keys=[
                (0.0, {}),
                (0.30, _P(spine_02=(5, 0, 0), neck_01=(12, 0, 0), neck_02=(8, 0, 0), head=(10, 0, 0),
                          tail_01=(0, -6, 0), tail_02=(0, -8, 0), jaw=-10,
                          _root={"f": -0.05, "z": -0.02})),
                (0.46, _P(spine_02=(-4, 0, 0), neck_01=(-8, 0, 0), neck_02=(-2, 0, 0), head=(8, 0, 0),
                          tail_01=(-4, 10, 0), tail_02=(0, 14, 0), tail_03=(0, 10, 0), jaw=-30,
                          _root={"f": 0.10, "z": -0.04})),
                (0.70, _P(spine_02=(-3, 0, 0), neck_01=(-6, 0, 0), neck_02=(-2, 0, 0), head=(6, 0, 0),
                          tail_01=(-4, 6, 0), tail_02=(0, 8, 0), jaw=-26,
                          _root={"f": 0.08, "z": -0.035})),
                (1.0, {})]),
            # Rears up a little on the hind legs, forepaws raised, neck and head lifted high, jaw
            # parted; held, then the forepaws come back down (cast_release at the peak).
            "Cast": dict(seconds=1.35, sharp=2, marker=(2, "cast_release"), layers=_FW_TAIL_IDLE, keys=[
                (0.0, {}),
                (0.30, _P(spine_02=(-3, 0, 0), neck_01=(-4, 0, 0), head=(-4, 0, 0),
                          _root={"z": -0.03, "f": -0.02})),
                (0.52, _P(neck_01=(14, 0, 0), neck_02=(8, 0, 0), head=(6, 0, 0), jaw=-14,
                          tail_01=(-8, 0, 0), tail_02=(-2, 0, 0),
                          _lift={"FL": (0.12, 0.03), "FR": (0.12, 0.03)},
                          _root={"pitch": 8, "z": -0.03, "f": -0.03})),
                (0.70, _P(neck_01=(14, 0, 0), neck_02=(8, 0, 0), head=(8, 0, 0), jaw=-12,
                          tail_01=(-8, 0, 0), tail_02=(-2, 0, 0),
                          _lift={"FL": (0.12, 0.03), "FR": (0.12, 0.03)},
                          _root={"pitch": 8, "z": -0.03, "f": -0.03})),
                (1.0, {})]),
            # Struck: a sharp flinch back and up, head snapped aside, then settle.
            "Hit": dict(seconds=0.55, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, {}),
                (0.24, _P(spine_02=(4, 0, 0), neck_01=(10, 8, 0), head=(12, 10, 6), jaw=-12,
                          tail_01=(-3, -10, 0), tail_02=(0, -12, 0),
                          _root={"f": -0.06, "z": -0.025, "bank": -4})),
                (0.50, _P(neck_01=(4, 3, 0), head=(5, 4, 2), jaw=-4, tail_01=(0, -4, 0),
                          _root={"f": -0.03, "z": -0.015})),
                (1.0, {})]),
            # Smug: a slow head tilt and nod with a sly jaw twitch while the tail flicks (three
            # crisp flicks travelling down the tail); returns exactly to rest (loops).
            "Victory": dict(seconds=2.0,
                            layers=[("b", "tail_02", "turn", 8.0, 3, 0.0), ("b", "tail_03", "turn", 14.0, 3, -0.7),
                                    ("b", "tail_04", "turn", 20.0, 3, -1.4), ("b", "tail_04", "pitch", -6.0, 3, -1.0)],
                            keys=[(0.0, {}),
                                  (0.25, _P(neck_01=(6, 0, 0), head=(6, 6, -14), jaw=-6, _root={"z": -0.01})),
                                  (0.55, _P(neck_01=(8, 0, 0), head=(2, 10, -18), jaw=-3, _root={"z": -0.01})),
                                  (0.80, _P(neck_01=(3, 0, 0), head=(4, 3, -6), _root={"z": -0.005})),
                                  (1.0, {})]),
            # Slumps flat on its belly, legs splayed out on the ground, head down on the floor.
            "KO": dict(seconds=1.6, ko=True, impact=0.55, keys=[
                (0.0, {}),
                (0.20, _P(neck_01=(-6, 0, 0), head=(-8, 6, 8), jaw=-6,
                          _root={"bank": 6, "z": -0.04})),
                (0.55, _P(_legs(60, -60, 5), spine_02=(-3, 0, 0), neck_01=(-16, 0, 0), neck_02=(-10, 0, 0),
                          head=(-14, 4, 6), jaw=-4, tail_01=(-10, 6, 0), tail_02=(4, 8, 0),
                          _root={"z": -0.25})),
                (0.84, _P(_legs(78, -78, 6), spine_02=(-4, 0, 0), neck_01=(-22, 0, 0), neck_02=(-17, 0, 0),
                          head=(-13, 4, 6), jaw=-3, tail_01=(-16, 8, 0), tail_02=(7, 10, 0),
                          tail_03=(3, 6, 0), _root={"z": -0.30})),
                (1.0, "same")]),
        }),
    # v20 round 2 (producer review): HUMANOID Treant (biped_arms `humanoid` opt-in). Gentle: slow,
    # swaying, kindly. Arms are authored in the humanoid channels (bird_pose.set_semantic): upper
    # `flex` = shoulder flexion (+ forward/up), `abd` = abduction (+ out/up from the side), fore/hand
    # `flex` = elbow/wrist flexion (+ = bend forward); every value is clamped to the human ranges in
    # the armature's joint_limits (hinges can only flex). Every clip starts/ends in _TR_REL (the
    # relaxed arm hang the walk also holds) so clip-to-clip blends match. Feet planted (IK) except KO.
    "treant": dict(
        hover=False, planted=True,
        clips={
            # Breathing (chest rises, shoulders lift), arms drifting a little at shoulder, elbow
            # and wrist (left and right out of step), head looking about, crown rustling.
            "Idle": dict(seconds=3.2, keys=[(0.0, _TR_REL), (1.0, _TR_REL)], bob=(-0.008, 0.005, 2), layers=[
                ("b", "spine_02", "pitch", 1.2, 2, 0.4), ("b", "spine_03", "pitch", 1.5, 2, 0.1),
                ("b", "arm_L_clav", "abd", 2.0, 2, 0.1), ("b", "arm_R_clav", "abd", 2.0, 2, 0.1),
                ("b", "spine_01", "bank", 1.5, 1, 0.0),
                ("b", "head", "turn", 4.0, 1, 0.8), ("b", "head", "bank", 2.0, 1, 1.6),
                ("b", "arm_L_upper", "flex", 5.0, 1, 0.5), ("b", "arm_R_upper", "flex", 5.0, 1, 2.3),
                ("b", "arm_L_upper", "abd", 3.0, 1, 1.4), ("b", "arm_R_upper", "abd", 3.0, 1, -0.4),
                ("b", "arm_L_fore", "flex", 6.0, 1, 0.0), ("b", "arm_R_fore", "flex", 6.0, 1, 1.8),
                ("b", "arm_L_hand", "flex", 6.0, 2, 0.3), ("b", "arm_R_hand", "flex", 6.0, 2, 1.9),
                ("b", "crown_01", "pitch", 3.0, 3, 0.0), ("b", "crown_01", "turn", 2.0, 2, 0.7),
                ("b", "crown_02", "turn", 3.0, 2, 1.1), ("b", "crown_02", "pitch", 2.0, 3, 0.4),
                ("b", "crown_03", "bank", 3.0, 3, 2.0), ("b", "crown_03", "turn", 2.0, 2, 0.2)]),
            # Two-armed branch swing: both arms rise up and back over the right shoulder (club held
            # high, trunk wound round and leaning back), then sweep down and across in front as the
            # trunk unwinds and leans in (arms extended, elbows nearly straight), follow through,
            # recover.
            "Attack": dict(seconds=1.4, sharp=2, keys=[
                (0.0, _TR_REL),
                (0.32, _P(_tr_arms((5, 110, 85, -15), (5, 105, 85, -15)),
                          spine_01=(5, 12, 0), spine_02=(6, 6, 0), spine_03=(6, 6, 0), head=(4, -10, 0),
                          crown_01=(-4, 0, 0), crown_03=(4, 0, 0), _root={"z": -0.02})),
                (0.50, _P(_tr_arms((80, -15, 8, -25), (85, -10, 8, -25)),
                          spine_01=(-6, -12, 0), spine_02=(-6, -8, 0), spine_03=(-8, -6, 0), head=(-4, 6, 0),
                          crown_01=(6, -6, 0), crown_02=(-6, 4, 0), crown_03=(-6, 4, 0),
                          _root={"f": 0.05, "z": -0.04})),
                (0.70, _P(_tr_arms((55, -10, 20, -15), (60, -8, 20, -15)),
                          spine_01=(-4, -8, 0), spine_02=(-4, -5, 0), spine_03=(-4, -4, 0), head=(-2, 4, 0),
                          crown_01=(-3, 4, 0), crown_02=(4, -3, 0), crown_03=(4, -3, 0),
                          _root={"f": 0.04, "z": -0.03})),
                (1.0, _TR_REL)]),
            # Gather (hands drawn in to the chest, head bowed), then both arms rise wide and high,
            # palms up, head lifted, crown flared (bones stretch a little), held; gentle settle
            # (cast_release at the peak).
            "Cast": dict(seconds=1.5, sharp=2, marker=(2, "cast_release"), layers=[
                ("b", "crown_01", "pitch", 2.0, 4, 0.0), ("b", "crown_03", "bank", 2.0, 4, 1.0)], keys=[
                (0.0, _TR_REL),
                (0.30, _P(_tr_arms((35, -25, 85, 10), (35, -25, 85, 10)),
                          spine_02=(-4, 0, 0), spine_03=(-3, 0, 0), head=(-8, 0, 0), _root={"z": -0.035})),
                (0.55, _P(_tr_arms((15, 110, 25, -35), (15, 110, 25, -35)),
                          spine_02=(4, 0, 0), spine_03=(5, 0, 0), head=(14, 0, 0),
                          crown_01={"pitch": -10, "sy": 1.1}, crown_02={"pitch": 6, "sy": 1.08},
                          crown_03={"bank": 10, "sy": 1.1}, _root={"z": -0.01})),
                (0.78, _P(_tr_arms((14, 106, 28, -30), (14, 106, 28, -30)),
                          spine_02=(4, 0, 0), spine_03=(4, 0, 0), head=(12, 0, 0),
                          crown_01={"pitch": -8, "sy": 1.08}, crown_02={"pitch": 5, "sy": 1.06},
                          crown_03={"bank": 8, "sy": 1.08}, _root={"z": -0.01})),
                (1.0, _TR_REL)]),
            # Struck: the trunk rocks back, head and crown whip, both arms fly up and out (elbows
            # bending), then settle.
            "Hit": dict(seconds=0.6, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, _TR_REL),
                (0.25, _P(_tr_arms((-20, 35, 50, 20), (-20, 35, 50, 20)),
                          spine_01=(4, 0, 0), spine_02=(8, 0, -4), spine_03=(6, 0, 0), head=(10, 0, 8),
                          crown_01=(8, 0, 0), crown_02=(10, 0, 0), crown_03=(-8, 0, 0),
                          _root={"f": -0.05, "z": -0.02})),
                (0.55, _P(_tr_arms((-5, 10, 30, 8), (-5, 10, 30, 8)),
                          spine_02=(3, 0, -2), head=(4, 0, 3),
                          crown_01=(-3, 0, 0), crown_02=(-4, 0, 0), crown_03=(3, 0, 0),
                          _root={"f": -0.02, "z": -0.012})),
                (1.0, _TR_REL)]),
            # Happy: BOTH arms raised high and waving, swaying side to side with a little knee
            # bounce, head tilting, crown bouncing; returns exactly to rest (loops).
            "Victory": dict(seconds=2.4, bob=(-0.02, 0.012, 4), layers=[
                ("b", "spine_01", "bank", 5.0, 2, 0.0), ("b", "head", "bank", 6.0, 2, 0.4),
                ("b", "arm_L_upper", "abd", 9.0, 4, 0.0), ("b", "arm_R_upper", "abd", 9.0, 4, 3.14),
                ("b", "arm_L_fore", "flex", 14.0, 4, -0.6), ("b", "arm_R_fore", "flex", 14.0, 4, 2.5),
                ("b", "arm_L_hand", "flex", 12.0, 4, -1.2), ("b", "arm_R_hand", "flex", 12.0, 4, 1.9),
                ("b", "crown_01", "pitch", 4.0, 4, 0.0), ("b", "crown_02", "pitch", -4.0, 4, 0.4),
                ("b", "crown_03", "bank", 4.0, 4, 0.8)], keys=[
                (0.0, _TR_REL),
                (0.16, _P(_tr_arms((10, 100, 35, -10), (10, 100, 35, -10)), head=(8, 0, 0))),
                (0.84, _P(_tr_arms((10, 100, 35, -10), (10, 100, 35, -10)), head=(8, 0, 0))),
                (1.0, _TR_REL)]),
            # Topples backward like a felled tree and lies on its back, arms flung out to the sides,
            # crown on the ground (root pivots at the feet; the ground settle rests the lowest point
            # on z=0). The feet slide forward under it ("f") so the body lands on its own tile.
            "KO": dict(seconds=1.8, ko=True, impact=0.6, keys=[
                (0.0, _TR_REL),
                (0.22, _P(_tr_arms((20, 35, 40, 0), (20, 35, 40, 0)),
                          spine_02=(-8, 0, 0), spine_03=(-4, 0, 0), head=(-10, 0, 0),
                          _root={"bank": 4, "z": -0.03})),
                (0.60, _P(_tr_arms((-15, 60, 25, 0), (-15, 55, 25, 0)),
                          spine_02=(2, 0, 0), head=(-6, 0, 0), crown_01=(6, 0, 0), crown_03=(-6, 0, 0),
                          _root={"pitch": 80, "bank": 8, "f": 0.45})),
                (0.85, _P(_tr_arms((-10, 70, 15, 10), (-10, 65, 15, 10)),
                          spine_02=(4, 0, 0), head=(4, 0, 0), crown_01=(4, 0, 0), crown_03=(-4, 0, 0),
                          _root={"pitch": 86, "bank": 6, "f": 0.55})),
                (1.0, "same")]),
        }),
    # Legless sea serpent: the coil is the support and never leaves the ground (no root motion);
    # every clip lives in the S-neck, head, jaw, frill, fins and a coil ripple. Semantic signs
    # (serpent.py axis dump): +pitch on an upright neck bone tips it BACK, -pitch bows it
    # forward/down; "turn" (about world UP) is the safe coil/neck sway channel.
    "leviathan": dict(
        hover=False, planted=True,
        clips={
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})], layers=[
                ("b", "body_02", "turn", 2.0, 1, 0.0), ("b", "body_03", "turn", 3.0, 1, -0.6),
                ("b", "body_04", "turn", 3.0, 1, -1.2), ("b", "head", "turn", -3.0, 1, -1.0),
                ("b", "body_03", "pitch", 1.5, 2, 0.0), ("b", "body_04", "pitch", -2.0, 2, -0.6),
                ("b", "head", "pitch", 2.0, 2, -1.2), ("b", "jaw", "pitch", -2.0, 2, 0.3),
                ("b", "fin_L", "pitch", 8.0, 2, 0.0), ("b", "fin_R", "pitch", 8.0, 2, 0.0),
                ("b", "frill", "pitch", 3.0, 2, -1.5),
                ("b", "tail_05", "turn", 2.0, 1, 0.0), ("b", "tail_07", "turn", 3.0, 1, -1.0),
                ("b", "tail_09", "turn", 5.0, 1, -2.0)]),
            # Rears back (neck coils up, head lifted, jaw parting), then a fast forward strike --
            # the neck uncoils, head drives forward-down, jaw wide (the bite) -- and recovers.
            "Attack": dict(seconds=1.2, sharp=2, keys=[
                (0.0, {}),
                (0.32, _P(body_02=(6, 0, 0), body_03=(10, 0, 0), body_04=(12, 0, 0), body_05=(8, 0, 0),
                          head=(10, 0, 0), jaw=-12, frill=(-6, 0, 0), fin_L=(-15, 0, 0), fin_R=(-15, 0, 0),
                          tail_07=(0, 6, 0), tail_09=(0, 10, 0))),
                (0.48, _P(body_01=(-4, 0, 0), body_02=(-10, 0, 0), body_03=(-16, 0, 0),
                          body_04=(-14, 0, 0), body_05=(-6, 0, 0), head=(-6, 0, 0), jaw=-32,
                          frill=(8, 0, 0), fin_L=(12, 0, 0), fin_R=(12, 0, 0),
                          tail_07=(0, -8, 0), tail_09=(0, -14, 0))),
                (0.68, _P(body_02=(-7, 0, 0), body_03=(-11, 0, 0), body_04=(-10, 0, 0),
                          body_05=(-4, 0, 0), head=(-4, 0, 0), jaw=-20, frill=(4, 0, 0),
                          tail_09=(0, -6, 0))),
                (1.0, {})]),
            # Rises tall (the S straightens up), head tipped up, jaw open, frill flared wide and
            # raised, fins spread; held, then settles (cast_release at the peak).
            "Cast": dict(seconds=1.4, sharp=2, marker=(2, "cast_release"), layers=[
                ("b", "frill", "pitch", 3.0, 4, 0.0), ("b", "fin_L", "pitch", 6.0, 4, 0.0),
                ("b", "fin_R", "pitch", 6.0, 4, 0.0)], keys=[
                (0.0, {}),
                (0.30, _P(body_03=(-4, 0, 0), body_04=(-6, 0, 0), head=(-6, 0, 0), jaw=-4,
                          frill={"pitch": 6, "sy": 0.95})),
                (0.55, _P(body_01=(4, 0, 0), body_02=(5, 0, 0), body_03=(-8, 0, 0), body_04=(-10, 0, 0),
                          body_05=(-6, 0, 0), head=(16, 0, 0), jaw=-18,
                          frill={"pitch": -14, "sx": 1.2, "sy": 1.25, "sz": 1.2},
                          fin_L=(-25, 0, 0), fin_R=(-25, 0, 0))),
                (0.78, _P(body_01=(4, 0, 0), body_02=(5, 0, 0), body_03=(-8, 0, 0), body_04=(-10, 0, 0),
                          body_05=(-6, 0, 0), head=(14, 0, 0), jaw=-14,
                          frill={"pitch": -12, "sx": 1.18, "sy": 1.22, "sz": 1.18},
                          fin_L=(-22, 0, 0), fin_R=(-22, 0, 0))),
                (1.0, {})]),
            # Struck: the head snaps back and aside, neck recoils, coil flinches.
            "Hit": dict(seconds=0.55, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, {}),
                (0.25, _P(body_03=(6, 6, 0), body_04=(10, 6, 0), body_05=(10, 0, 0), head=(12, 8, 10),
                          jaw=-12, frill=(10, 0, 0), fin_L=(-10, 0, 0), fin_R=(-10, 0, 0),
                          tail_06=(0, -6, 0), tail_09=(0, -10, 0))),
                (0.55, _P(body_04=(4, 2, 0), body_05=(4, 0, 0), head=(4, 3, 4), jaw=-4,
                          frill=(3, 0, 0), tail_09=(0, -3, 0))),
                (1.0, {})]),
            # Proud: head held high, a big slow neck sway and a fast happy coil ripple, frill and
            # fins fluttering; returns exactly to rest (loops).
            "Victory": dict(seconds=2.2, layers=[
                ("b", "body_02", "turn", 5.0, 2, 0.0), ("b", "body_03", "turn", 6.0, 2, -0.6),
                ("b", "body_04", "turn", 5.0, 2, -1.2), ("b", "head", "turn", -6.0, 2, -1.0),
                ("b", "frill", "pitch", 6.0, 4, 0.0), ("b", "fin_L", "pitch", 14.0, 4, 0.0),
                ("b", "fin_R", "pitch", 14.0, 4, 0.0)] + [
                ("b", f"tail_{i:02d}", "turn", 3.0 + 0.8 * i, 2, -0.75 * i) for i in range(1, 10)], keys=[
                (0.0, {}),
                (0.2, _P(body_04=(-6, 0, 0), head=(14, 0, 0), jaw=-10)),
                (0.8, _P(body_04=(-6, 0, 0), head=(14, 0, 0), jaw=-10)),
                (1.0, {})]),
            # The neck sways, then collapses back and to the side down ONTO its own coil, the upper
            # neck lying on the floor inside the coil ring and the head resting on the coil (a
            # forward collapse that put the head on the floor in front measured ~1.3 units past
            # the coil and left the battle camera's framing in-engine) (ko_settle: the authored collapse overshoots; per frame the
            # fraction of it applied is bisected so the head/neck rest exactly on the floor -- the
            # coil already is the ground contact, so the root never moves).
            "KO": dict(seconds=1.8, ko=True, impact=0.62,
                       ko_settle={"bones": ["body_01", "body_02", "body_03", "body_04", "body_05", "head"],
                                  "region": ["head", "snout", "jaw", "frill", "body_04", "body_05"]},
                       keys=[
                (0.0, {}),
                (0.25, _P(body_03=(-4, 10, 0), body_04=(-6, 8, 0), head=(6, 0, 10), jaw=-8)),
                (0.62, _P(body_01=(4, 8, 0), body_02=(14, 6, 0), body_03=(30, 4, 0),
                          body_04=(44, 0, 0), body_05=(18, 0, 0), head=(-80, 0, 20), jaw=-6,
                          frill=(10, 0, 0), fin_L=(20, 0, 0), fin_R=(20, 0, 0))),
                (0.85, _P(body_01=(6, 10, 0), body_02=(18, 8, 0), body_03=(36, 4, 0),
                          body_04=(52, 0, 0), body_05=(20, 0, 0), head=(-95, 0, 24), jaw=-4,
                          frill=(12, 0, 0), fin_L=(22, 0, 0), fin_R=(22, 0, 0))),
                (1.0, "same")]),
        }),
}


# ---------------------------------------------------------------------------
# v21 (enemies batch 1, Hollow region): Stalker (quadruped template, planted-foot IK like the Frost
# Wyrm), Brute and Swarmling (rig_templates/blob.py: squash/stretch body + follower bones, see
# anim/blob_pose.py). Same data-driven builder; blob-only spec keys: "_sq" in a pose = body squash
# factor s (body sy = s, sx = sz = 1/sqrt(s), volume kept), "ground_lift" (lift the root only when
# the deformed mesh would dip below the floor -- rocking a round body about its centre).
# ---------------------------------------------------------------------------
def _SQ(sq, **extra):
    """Blob body squash channels (volume kept): height x sq, width/depth x 1/sqrt(sq)."""
    w = (1.0 / sq) ** 0.5
    return dict({"sx": w, "sy": sq, "sz": w}, **extra)


_BR_IDLE = [("b", "body", "sy", 0.025, 2, 0.0), ("b", "body", "sx", -0.0125, 2, 0.0),
            ("b", "body", "sz", -0.0125, 2, 0.0), ("b", "tuft", "turn", 5.0, 1, -0.6),
            ("b", "tuft", "pitch", 3.0, 2, -1.0)]
_SW_JIT = [("b", "body", "sy", 0.03, 4, 0.0), ("b", "body", "sx", -0.015, 4, 0.0),
           ("b", "body", "sz", -0.015, 4, 0.0), ("b", "legs_F", "pitch", 6.0, 4, 0.0),
           ("b", "legs_B", "pitch", -6.0, 4, 0.9), ("b", "crest", "pitch", 5.0, 4, -1.0),
           ("b", "crest", "turn", 4.0, 2, 0.3)]
_ST_TAIL = [("b", "tail_01", "turn", 5.0, 1, 0.0), ("b", "tail_02", "turn", 8.0, 1, -0.6),
            ("b", "tail_03", "turn", 11.0, 1, -1.2)]
ENEMY_PARAMS = {
    # Sneaky: low, coiled and watchful. Feet planted (IK) except where lifted; KO lies flat.
    "stalker": dict(
        hover=False, planted=True,
        clips={
            # Crouched and watching: slow breathing, head low and scanning side to side, the tail
            # swishing, a little weight shift.
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})], bob=(-0.03, 0.006, 2), layers=_ST_TAIL + [
                ("b", "spine_02", "pitch", 1.0, 2, 0.0), ("b", "neck_02", "turn", 9.0, 1, 0.4),
                ("b", "head", "turn", 5.0, 1, 0.9), ("b", "neck_02", "pitch", 2.0, 2, 0.3),
                ("r", "bank", 1.0, 1, 0.0)]),
            # Pounce: sink low and draw back (head down, tail lashing), then spring forward and up
            # with the forepaws thrown out ahead and the head stretched at the prey, land, recover.
            "Attack": dict(seconds=1.15, sharp=2, keys=[
                (0.0, {}),
                (0.32, _P(spine_02=(-3, 0, 0), neck_01=(-6, 0, 0), neck_02=(-12, 0, 0), head=(-6, 0, 0),
                          tail_01=(-6, 10, 0), tail_02=(0, 12, 0), tail_03=(0, 14, 0),
                          _root={"z": -0.13, "f": -0.09, "pitch": -5})),
                (0.50, _P(spine_02=(4, 0, 0), neck_01=(-4, 0, 0), neck_02=(-10, 0, 0), head=(-4, 0, 0),
                          tail_01=(8, -6, 0), tail_02=(6, -8, 0), tail_03=(4, -8, 0),
                          _lift={"FL": (0.24, 0.46), "FR": (0.24, 0.46)},
                          _root={"z": 0.12, "f": 0.34, "pitch": 10})),
                (0.66, _P(neck_02=(-14, 0, 0), head=(-8, 0, 0), tail_01=(2, -4, 0),
                          _lift={"FL": (0.0, 0.30), "FR": (0.0, 0.30)},
                          _root={"z": -0.07, "f": 0.22, "pitch": -5})),
                (1.0, {})]),
            # Sly charm: rises a little on the hind legs, head and antlers lifted high, forepaws
            # off the ground, held (cast_release), then settles.
            "Cast": dict(seconds=1.35, sharp=2, marker=(2, "cast_release"), layers=_ST_TAIL, keys=[
                (0.0, {}),
                (0.30, _P(neck_02=(-6, 0, 0), head=(-6, 0, 0), _root={"z": -0.05, "f": -0.02})),
                (0.52, _P(spine_02=(6, 0, 0), neck_01=(10, 0, 0), neck_02=(10, 0, 0), head=(6, 0, 0),
                          tail_01=(-8, 0, 0), _lift={"FL": (0.10, 0.02), "FR": (0.10, 0.02)},
                          _root={"pitch": 9, "z": -0.03, "f": -0.04})),
                (0.72, _P(spine_02=(6, 0, 0), neck_01=(10, 0, 0), neck_02=(10, 0, 0), head=(8, 0, 0),
                          tail_01=(-8, 0, 0), _lift={"FL": (0.10, 0.02), "FR": (0.10, 0.02)},
                          _root={"pitch": 9, "z": -0.03, "f": -0.04})),
                (1.0, {})]),
            # Struck: flinches back and low, head snapped aside, then settles.
            "Hit": dict(seconds=0.55, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, {}),
                (0.24, _P(spine_02=(3, 0, 0), neck_02=(8, 12, 0), head=(10, 14, 8),
                          tail_01=(0, -12, 0), tail_02=(0, -12, 0),
                          _root={"f": -0.08, "z": -0.05, "bank": -5})),
                (0.50, _P(neck_02=(3, 4, 0), head=(4, 5, 3), tail_01=(0, -4, 0),
                          _root={"f": -0.03, "z": -0.02})),
                (1.0, {})]),
            # Sly and pleased: a slow head tilt with a smug nod, the tail swishing in big slow arcs
            # and the body rocking in a low crouch; returns exactly to rest (loops).
            "Victory": dict(seconds=2.0, bob=(-0.025, 0.012, 2),
                            layers=[("b", "tail_01", "turn", 10.0, 2, 0.0), ("b", "tail_02", "turn", 14.0, 2, -0.6),
                                    ("b", "tail_03", "turn", 18.0, 2, -1.2), ("b", "tail_03", "pitch", -6.0, 2, -1.0)],
                            keys=[(0.0, {}),
                                  (0.25, _P(neck_02=(6, 8, 0), head=(6, 10, -16), _root={"bank": -3})),
                                  (0.55, _P(neck_02=(8, -6, 0), head=(2, -8, 14), _root={"bank": 3})),
                                  (0.80, _P(neck_02=(3, 0, 0), head=(4, 2, -4))),
                                  (1.0, {})]),
            # Collapses onto its belly, legs splayed out on the ground, head down on the floor.
            "KO": dict(seconds=1.6, ko=True, impact=0.55, keys=[
                (0.0, {}),
                (0.20, _P(neck_02=(-6, 0, 0), head=(-8, 6, 8), _root={"bank": 6, "z": -0.04})),
                (0.55, _P(_legs(60, -60, 5), spine_02=(-3, 0, 0), neck_01=(-10, 0, 0), neck_02=(-14, 0, 0),
                          head=(-12, 6, 10), tail_01=(-10, 8, 0), tail_02=(4, 10, 0),
                          _root={"z": -0.25})),
                (0.84, _P(_legs(78, -78, 6), spine_02=(-4, 0, 0), neck_01=(-14, 0, 0), neck_02=(-18, 0, 0),
                          head=(-12, 6, 10), tail_01=(-14, 10, 0), tail_02=(6, 12, 0), tail_03=(3, 8, 0),
                          _root={"z": -0.30})),
                (1.0, "same")]),
        }),
    # Stubborn, grumpy, HEAVY: everything starts with a slow wind-up squash and lands hard. No legs:
    # body squash/stretch (_SQ), root lean/hop/charge, the rigid face+horns riding on the ball, the
    # tuft whipping as follow-through. Not planted; every non-KO frame is ground-lifted (a lean
    # about the root would swing the round base into the floor).
    "brute": dict(
        hover=False, planted=False, ground_lift=True,
        clips={
            # Grumpy breathing (slow heave of the ball), a sullen look side to side, a huff.
            "Idle": dict(seconds=3.0, keys=[(0.0, {}), (1.0, {})], layers=_BR_IDLE + [
                ("b", "head", "turn", 6.0, 1, 0.4), ("b", "head", "pitch", -2.5, 2, 0.8),
                ("r", "bank", 1.2, 1, 1.0)]),
            # Horn charge / headbutt: rocks back and squashes (gathering), then bulls forward with
            # the whole ball stretched and pitched down horns-first, a hard impact squash at the end
            # of the charge, rebound, back to its spot.
            "Attack": dict(seconds=1.35, sharp=2, keys=[
                (0.0, {}),
                (0.30, _P(body=_SQ(0.84), head=(8, 0, 0), tuft=(10, 0, 0),
                          _root={"pitch": 10, "f": -0.08})),
                (0.48, _P(body=_SQ(1.10), head=(-12, 0, 0), tuft=(-10, 0, 0),
                          _root={"pitch": -12, "f": 0.18, "z": 0.05})),
                (0.56, _P(body=_SQ(0.80), head=(-16, 0, 0), tuft=(-16, 0, 0),
                          _root={"pitch": -14, "f": 0.22})),
                (0.74, _P(body=_SQ(1.04), head=(-6, 0, 0), tuft=(8, 0, 0),
                          _root={"pitch": -4, "f": 0.12})),
                (1.0, {})]),
            # Stubborn bellow: squats right down (gathering), then heaves up tall, face to the sky,
            # tuft flared, a little off the ground (cast_release), drops back with a squash.
            "Cast": dict(seconds=1.45, sharp=2, marker=(2, "cast_release"), keys=[
                (0.0, {}),
                (0.30, _P(body=_SQ(0.80), head=(-8, 0, 0), tuft=(8, 0, 0))),
                (0.50, _P(body=_SQ(1.22), head=(14, 0, 0), tuft={"pitch": -6, "sy": 1.2},
                          _root={"z": 0.08, "pitch": 4})),
                (0.70, _P(body=_SQ(1.16), head=(12, 0, 0), tuft={"pitch": -4, "sy": 1.15},
                          _root={"z": 0.05, "pitch": 3})),
                (0.84, _P(body=_SQ(0.84), head=(-4, 0, 0), tuft=(-10, 0, 0))),
                (1.0, {})]),
            # Struck: shoved back on its heels, squashed, face screwed up and turned away.
            "Hit": dict(seconds=0.6, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, {}),
                (0.24, _P(body=_SQ(0.80), head=(12, 12, 6), tuft=(18, 8, 0),
                          _root={"f": -0.12, "pitch": 12})),
                (0.55, _P(body=_SQ(1.05), head=(4, 4, 2), tuft=(-6, 0, 0), _root={"f": -0.05, "pitch": 3})),
                (1.0, {})]),
            # Grumpy-proud: two heavy stomping bounces in place, chin up and a smug head tilt;
            # returns exactly to rest (loops).
            "Victory": dict(seconds=2.2, layers=[("b", "tuft", "turn", 8.0, 2, 0.0)], keys=[
                (0.0, {}),
                (0.14, _P(body=_SQ(0.84), head=(4, 0, 0))),
                (0.24, _P(body=_SQ(1.10), head=(10, 0, -8), _root={"z": 0.07})),
                (0.34, _P(body=_SQ(1.0), head=(10, 0, -10), _root={"z": 0.10})),
                (0.44, _P(body=_SQ(0.82), head=(6, 0, -10))),
                (0.58, _P(body=_SQ(0.84), head=(6, 0, 10))),
                (0.68, _P(body=_SQ(1.10), head=(12, 0, 10), _root={"z": 0.07})),
                (0.78, _P(body=_SQ(1.0), head=(12, 0, 8), _root={"z": 0.10})),
                (0.88, _P(body=_SQ(0.84), head=(4, 0, 0))),
                (1.0, {})]),
            # Dazed wobble, then topples over onto its side and lies there, a little flattened, one
            # horn on the floor and the grumpy face in profile (on its back, both the battle and the
            # head-on camera saw only the flat underside). Root bank pivots about the base centre,
            # so "x" slides the root sideways by about the ball's radius: it rolls over in place
            # instead of landing beside its tile (the ko ground settle rests the lowest point on
            # the floor).
            "KO": dict(seconds=1.6, ko=True, impact=0.6, keys=[
                (0.0, {}),
                (0.22, _P(body=_SQ(0.9), head=(-6, 10, 8), tuft=(10, 0, 0), _root={"bank": -10})),
                (0.60, _P(body=_SQ(0.88), head=(4, 0, -6), tuft=(0, 0, 14), _root={"bank": 84, "x": 0.66})),
                (0.84, _P(body=_SQ(0.9), head=(6, 0, -8), tuft=(0, 0, 18), _root={"bank": 80, "x": 0.66})),
                (1.0, "same")]),
        }),
    # Skittery and scrappy: quick, jittery, never still; tiny squash pulses, scrabbling leg pairs.
    "swarmling": dict(
        hover=False, planted=False, ground_lift=True,
        clips={
            # Jittery: quick squash pulses, legs fidgeting, head darting about, crest twitching.
            "Idle": dict(seconds=2.0, keys=[(0.0, {}), (1.0, {})], layers=_SW_JIT + [
                ("b", "head", "turn", 9.0, 3, 0.0), ("b", "head", "pitch", 3.0, 4, 0.5),
                ("r", "bank", 2.0, 2, 0.3)]),
            # Scrappy bite-lunge: crouch back, spring forward horns-first with the front legs
            # reaching and the hind legs kicking, chomp (head snaps down), land squashed, scurry back.
            "Attack": dict(seconds=0.9, sharp=2, keys=[
                (0.0, {}),
                (0.28, _P(body=_SQ(0.82), head=(8, 0, 0), legs_F=(-12, 0, 0), legs_B=(12, 0, 0),
                          _root={"pitch": 6, "f": -0.05})),
                (0.46, _P(body=_SQ(1.15), head=(-14, 0, 0), legs_F=(30, 0, 0), legs_B=(-28, 0, 0),
                          _root={"pitch": -15, "f": 0.28, "z": 0.08})),
                (0.60, _P(body=_SQ(0.84), head=(-8, 0, 0), legs_F=(10, 0, 0), legs_B=(-10, 0, 0),
                          _root={"pitch": -4, "f": 0.30})),
                (0.80, _P(body=_SQ(1.04), legs_F=(-8, 0, 0), legs_B=(8, 0, 0), _root={"f": 0.10})),
                (1.0, {})]),
            # Puffs up: crouch, then a fluffed-up stretch with the crest flared and head thrown back,
            # hopping off the ground (cast_release), drop.
            "Cast": dict(seconds=1.0, sharp=2, marker=(2, "cast_release"), layers=_SW_JIT, keys=[
                (0.0, {}),
                (0.28, _P(body=_SQ(0.80), head=(-6, 0, 0), crest=(6, 0, 0))),
                (0.50, _P(body=_SQ(1.18), head=(12, 0, 0), crest={"pitch": -10, "sy": 1.3},
                          legs_F=(-14, 0, 0), legs_B=(14, 0, 0), _root={"z": 0.10})),
                (0.70, _P(body=_SQ(1.10), head=(10, 0, 0), crest={"pitch": -6, "sy": 1.2},
                          _root={"z": 0.05})),
                (0.84, _P(body=_SQ(0.86))),
                (1.0, {})]),
            # Struck: bowled back and squashed flat, legs flailing.
            "Hit": dict(seconds=0.45, sharp=1, marker=(1, "hit_react"), keys=[
                (0.0, {}),
                (0.25, _P(body=_SQ(0.80), head=(12, 10, 0), legs_F=(26, 0, 0), legs_B=(-20, 0, 0),
                          crest=(14, 0, 0), _root={"f": -0.12, "pitch": 14})),
                (0.6, _P(body=_SQ(1.05), head=(4, 3, 0), legs_F=(6, 0, 0), _root={"f": -0.04, "pitch": 3})),
                (1.0, {})]),
            # Delighted: four bouncy hops in place, spinning a full turn over the middle two (loops).
            "Victory": dict(seconds=1.6, layers=[("b", "legs_F", "pitch", 16.0, 4, 0.0),
                                                 ("b", "legs_B", "pitch", -16.0, 4, 0.0),
                                                 ("b", "crest", "pitch", 8.0, 4, -1.0)], keys=[
                (0.0, {}),
                (0.06, _P(body=_SQ(0.84))), (0.13, _P(body=_SQ(1.12), _root={"z": 0.10})),
                (0.20, _P(body=_SQ(0.86))), (0.31, _P(body=_SQ(1.12), _root={"z": 0.12})),
                (0.45, _P(body=_SQ(0.86))), (0.56, _P(body=_SQ(1.12), _root={"z": 0.12})),
                (0.70, _P(body=_SQ(0.86))), (0.81, _P(body=_SQ(1.12), _root={"z": 0.10})),
                (0.94, _P(body=_SQ(0.90))), (1.0, {})],
                tracks=[[(0.0, {}), (0.22, {}), (0.66, {"_root": {"turn": 360.0}}),
                         (1.0, {"_root": {"turn": 360.0}})]]),
            # Flips over onto its back, legs in the air, a last twitch, still.
            "KO": dict(seconds=1.2, ko=True, impact=0.55, keys=[
                (0.0, {}),
                (0.20, _P(body=_SQ(0.85), head=(8, 0, 0), _root={"pitch": -6})),
                (0.55, _P(body=_SQ(0.92), head=(10, 0, 0), legs_F=(-20, 0, 0), legs_B=(20, 0, 0),
                          _root={"pitch": 105, "bank": 6, "f": 0.8})),
                (0.84, _P(body=_SQ(0.94), head=(10, 0, 0), legs_F=(-14, 0, 0), legs_B=(14, 0, 0),
                          _root={"pitch": 100, "bank": 6, "f": 0.8})),
                (1.0, "same")]),
        }),
}


def build_bird_clips(creature):
    import mathutils
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import bird_pose as BP

    BPAR = (BIRD_PARAMS[creature] if creature in BIRD_PARAMS else LAST_PARAMS[creature]  # v20
            if creature in LAST_PARAMS else ENEMY_PARAMS[creature])  # v21
    FWD = BP.forward_of(arm_obj)
    BLOB = arm_obj.data.get("template") == "blob"  # v21
    if BLOB:
        import blob_pose as BLP
    HOVER = float(arm_obj.data.get("hover_offset", 0.0))
    mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
    LEG_IK = {s: BP.leg_ik_data(arm_obj, s, FWD) for s in leg_sides}
    # constant per-creature channels on every clip (bird_pose.HOVER_HOLDS; none for the Thunderbird)
    HOLD = {("b", b, ch): v for _, b, ch, v in BP.HOVER_HOLDS.get(BPAR.get("hold"), [])}
    EVENT_MARKERS = {}
    CLIP_LOOP = {"Idle": True, "Move": True, "Attack": False, "Hit": False,
                 "Cast": False, "KO": False, "Victory": True}

    def conv_pose(p):
        """BIRD_PARAMS pose -> bird_pose pose (resolves _root f/hover, _lift, jaw)."""
        out = {"bones": {}, "root": {}, "lift": {}}
        for k, v in p.items():
            if k == "_root":
                for rk, rv in v.items():
                    if isinstance(rv, str):  # "hover" / "hover+0.04"
                        rv = HOVER + (float(rv.split("+")[1]) if "+" in rv else 0.0)
                    if rk == "f":
                        out["root"]["x"] = out["root"].get("x", 0.0) + FWD.x * rv
                        out["root"]["y"] = out["root"].get("y", 0.0) + FWD.y * rv
                    else:
                        out["root"][rk] = rv
            elif k == "_lift":
                out["lift"] = dict(v)
            elif k == "_free":
                continue
            elif k == "jaw":
                out["jaw"] = v
            else:
                out["bones"][k] = dict(v)
        return out

    def keys_of(raw):
        res, prev = [], None
        for i, (f, p) in enumerate(raw):
            if p == "same":
                p = prev
            res.append((f, conv_pose(p), "inout"))
            prev = p
        return res

    def _bone_chans(c):
        per = {}
        for k, v in c.items():
            if k[0] == "b":
                per.setdefault(k[1], {})[k[2]] = v
        return per

    _region_cache = {}

    def region_min(bones):
        """min world Z of the vertices whose dominant vertex group is one of `bones`."""
        key = tuple(bones)
        if key not in _region_cache:
            names = [g.name for g in mesh_obj.vertex_groups]
            _region_cache[key] = [v.index for v in mesh_obj.data.vertices if len(v.groups) and
                                  names[max(v.groups, key=lambda g: g.weight).group] in bones]
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        eo = mesh_obj.evaluated_get(dg)
        me = eo.to_mesh()
        mz = min((eo.matrix_world @ me.vertices[i].co).z for i in _region_cache[key])
        eo.to_mesh_clear()
        return mz

    def eval_min_z():
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        eo = mesh_obj.evaluated_get(dg)
        me = eo.to_mesh()
        mz = min((eo.matrix_world @ v.co).z for v in me.vertices)
        eo.to_mesh_clear()
        return mz

    for clip in ("Idle", "Attack", "Cast", "Hit", "KO", "Victory"):
        spec = BPAR["clips"][clip]
        raw = spec["keys"]
        keys = keys_of(raw)
        if "sharp" in spec:  # accelerate INTO the strike/impact key (Attack/Hit's sharpening)
            i = spec["sharp"]
            keys[i] = (keys[i][0], keys[i][1], "in")
        if spec.get("ko"):
            j = min(range(len(keys)), key=lambda k: abs(keys[k][0] - spec["impact"]))
            keys[j] = (keys[j][0], keys[j][1], "in")
        tracks = [keys] + [keys_of(t) for t in spec.get("tracks", [])]
        layers = list(spec.get("layers", []))
        if "flap" in spec:
            fl = spec["flap"]
            layers += BP.wing_layers(fl["cycles"], fl.get("amps", (18.0, 10.0, 8.0)),
                                     fl.get("lags", (0.0, 0.6, 1.2)), fl.get("sweep", 0.0))

        def chans(t, layers=layers, tracks=tracks, spec=spec):
            c = BP.add_channels(BP.sample_tracks(tracks, t), BP.sines(layers, t))
            if HOLD:
                c = BP.add_channels(c, HOLD)
            if "bob" in spec:
                base, amp, cyc = spec["bob"]
                c[("r", "z")] = c.get(("r", "z"), 0.0) + base + amp * math.sin(2 * math.pi * cyc * t - 1.2)
            elif "flap" in spec and not spec.get("ko"):
                # every hovering clip keeps the same baseline lift + a bob phased with its flap
                fl = spec["flap"]
                c[("r", "z")] = c.get(("r", "z"), 0.0) + 0.03 + 0.02 * math.sin(
                    2 * math.pi * fl["cycles"] * t - 1.2)
            return c

        def post(c, t, spec=spec):
            if BLOB:  # v21: face/horn followers ride on the squashing ball
                BLP.follow_squash(arm_obj)
            if BLOB and BPAR.get("ground_lift") and not spec.get("ko"):
                BLP.ground_lift(arm_obj, mesh_obj, lambda z: BP.set_root(
                    arm_obj, FWD, (c.get(("r", "x"), 0.0), c.get(("r", "y"), 0.0), z),
                    c.get(("r", "pitch"), 0.0), c.get(("r", "turn"), 0.0), c.get(("r", "bank"), 0.0)),
                    c.get(("r", "z"), 0.0))
            if BPAR["planted"] and not spec.get("ko"):
                lifts = BP.lifts_of(c)
                for side in leg_sides:
                    up, fw = lifts.get(side, (0.0, 0.0))
                    tgt = LEG_IK[side]["ankle_rest"] + mathutils.Vector((0, 0, up)) + FWD * fw
                    BP.plant_leg(arm_obj, side, LEG_IK[side], tgt)
            if spec.get("ko") and spec.get("ko_settle"):
                # v20 (Leviathan): bisect the fraction of the listed bones' authored KO pose that
                # is applied so the region (head/upper neck) rests on the floor (+4 mm) -- before
                # impact only if it would go through; the root is never moved.
                ks = spec["ko_settle"]
                full = {b: dict(ch) for b, ch in _bone_chans(c).items() if b in ks["bones"]}
                if region_min(ks["region"]) < 0.004 or t >= spec["impact"]:
                    if region_min(ks["region"]) < 0.004:
                        lo, hi = 0.0, 1.0
                        for _ in range(14):
                            mid = (lo + hi) / 2
                            for b, ch in full.items():
                                BP.set_semantic(arm_obj, FWD, b, {k: v * mid if k in ("pitch", "turn", "bank")
                                                                  else v for k, v in ch.items()})
                            if region_min(ks["region"]) >= 0.004:
                                lo = mid
                            else:
                                hi = mid
                        for b, ch in full.items():
                            BP.set_semantic(arm_obj, FWD, b, {k: v * lo if k in ("pitch", "turn", "bank")
                                                              else v for k, v in ch.items()})
                return
            if spec.get("ko"):
                # ground settle: before impact only lift out of the floor; from impact on, rest the
                # lowest vertex exactly on it (+4 mm).
                z = c.get(("r", "z"), 0.0)
                for _ in range(6):
                    mz = eval_min_z()
                    if t < spec["impact"] and mz >= 0.004:
                        break
                    if abs(mz - 0.004) < 0.001:
                        break
                    z -= (mz - 0.004)
                    BP.set_root(arm_obj, FWD, (c.get(("r", "x"), 0.0), c.get(("r", "y"), 0.0), z),
                                c.get(("r", "pitch"), 0.0), c.get(("r", "turn"), 0.0),
                                c.get(("r", "bank"), 0.0))

        n = int(round(spec["seconds"] * FPS))
        BP.bake_frames(arm_obj, scene, FWD, clip, n, chans, post)
        if "marker" in spec:
            ki, mname = spec["marker"]
            frac = raw[ki][0]
            EVENT_MARKERS.setdefault(clip, []).append(
                {"name": mname, "frame": 1 + round(frac * n), "fraction": frac})
        print(f"{clip.upper()} ACTION ({creature}, bird): {n + 1} frames @ {FPS}fps, "
              f"{len(raw)} key poses, {len(layers)} sine layers")

    arm_obj.animation_data.action = None
    bpy.ops.object.mode_set(mode="OBJECT")
    with open(os.path.join(OUT, "keyed_event_markers.json"), "w") as f:
        json.dump({"markers": EVENT_MARKERS, "loop": CLIP_LOOP}, f, indent=2)
    print(f"EVENT MARKERS ({creature}): {json.dumps(EVENT_MARKERS)}")
    blend_out = os.path.join(OUT, f"{creature}_keyed.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_out)
    print(f"SAVED {blend_out}")


if CREATURE == "griffin":
    build_griffin_clips()
elif CREATURE in BIRD_PARAMS or CREATURE in LAST_PARAMS or CREATURE in ENEMY_PARAMS:  # v20/v21: same builder
    build_bird_clips(CREATURE)
else:
    build_creature_clips(CREATURE)
