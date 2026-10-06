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
CREATURE = args.get("creature", "griffin")

# Per-creature gait "character" (v18, wingless quadrupeds): every number here was already a named
# tunable constant (round 5-13's own tuning knobs) EXCEPT the tail/neck/head/spine_02 sinusoid
# amplitudes a few hundred lines down, which were bare literals -- those are now scaled by
# tail_amp/head_amp instead of rewritten per-creature, to keep this a small, auditable diff rather
# than a second copy of the per-frame loop. "griffin" reproduces every one of v17's hardcoded
# values EXACTLY (verified byte-identical: re-ran gait.py before/after this change against the same
# rigged .blend and hashed the baked Move action's fcurve keyframes -- see the handback report).
# cycle-seconds (speed) is NOT in here -- that was already a CLI arg (--cycle-seconds), used as-is
# per creature (Golem slow/heavy -> a larger value; Kirin light/elegant -> a smaller one).
GAIT_PARAMS = {
    "griffin":  dict(duty=0.6,  body_bob=0.025, foot_curl=20.0, toe_curl=28.0, toe_fan_curl=8.0,
                      raw_stride=0.23, crouch_min=0.06, crouch_max=0.14, scapula_swing=12.0,
                      pelvis_roll=4.0, pelvis_yaw=3.0, tail_amp=1.0, head_amp=1.0),
    # Golem: slow, heavy, deliberate -- short stride, minimal head bob, more side-to-side body sway
    # (pelvis roll) than fore-aft, barely any tail motion (it's a short stub anyway).
    "golem":    dict(duty=0.70, body_bob=0.012, foot_curl=3.0, toe_curl=3.0, toe_fan_curl=8.0,
                      raw_stride=0.14, crouch_min=0.03, crouch_max=0.07, scapula_swing=6.0,
                      pelvis_roll=7.0, pelvis_yaw=2.0, tail_amp=0.3, head_amp=0.25),
    # Kirin: elegant and light -- longer stride, a visible head bob on the long neck, a flicking
    # tail, less side-to-side sway (a light-footed gait, not a lumbering one).
    "kirin":    dict(duty=0.52, body_bob=0.032, foot_curl=22.0, toe_curl=30.0, toe_fan_curl=8.0,
                      raw_stride=0.30, crouch_min=0.05, crouch_max=0.10, scapula_swing=14.0,
                      pelvis_roll=3.0, pelvis_yaw=3.0, tail_amp=2.2, head_amp=2.5),
    # Tarasque: short-legged waddle -- short stride, shell rocks side to side (big pelvis roll/yaw),
    # grumpy head-low (small head amplitude, no proud bob), short stub tail barely moves.
    "tarasque": dict(duty=0.68, body_bob=0.016, foot_curl=12.0, toe_curl=14.0, toe_fan_curl=8.0,
                      raw_stride=0.15, crouch_min=0.03, crouch_max=0.06, scapula_swing=7.0,
                      pelvis_roll=8.0, pelvis_yaw=5.0, tail_amp=0.3, head_amp=0.35),
    # Basilisk: low sprawling lizard -- lateral-sequence gait (DUTY close to Griffin's), continuous
    # exaggerated tail sway (its own long tail is the character's signature motion) and a yaw-heavy
    # body S-curve (pelvis_yaw >> pelvis_roll -- a lizard's sprawl sways side to side in the ground
    # plane, not up/down).
    "basilisk": dict(duty=0.58, body_bob=0.018, foot_curl=18.0, toe_curl=22.0, toe_fan_curl=8.0,
                      raw_stride=0.22, crouch_min=0.08, crouch_max=0.15, scapula_swing=10.0,
                      pelvis_roll=4.0, pelvis_yaw=9.0, tail_amp=3.5, head_amp=1.2),
}
# v18 round 4 (lead review of contact_sheet_final: Golem/Tarasque Move tore the chest/forelegs,
# Kirin's Move lurched, Basilisk's tail swung below ground). Root causes, measured directly (a
# per-bone world-space swing/twist decomposition of every baked Move frame, scratchpad
# work5/diag_wtwist.py), not guessed:
#   1. aim_matrix(..., up_hint) rebuilds scapula/thigh/shin orientation from a world up_hint, which
#      ignores the bone's REST ROLL. The Griffin's rest roll happens to sit a constant -90 deg from
#      that construction on every frame (a rigid offset its 17 tuning rounds were built on); the
#      quadruped template's explicit align_roll_leg roll does not, so these rigs' scapula/thigh/
#      shin were twisted by a frame-varying 40-180 deg about their own axes -- a candy-wrapper twist
#      of the leg/shoulder mesh = the torn chest. Fix: `fk_anchored` creatures aim those bones with
#      MINIMUM TWIST from their FK-neutral orientation (aim_min_twist below).
#   2. The IK hip anchors were fixed points (rest hip minus a per-leg crouch) independent of the
#      body's own bob/roll/yaw, and the scapula head stayed at its REST position while spine_02 (its
#      parent) dropped by the crouch -- so shoulder/hip skin sheared every frame. Fix: anchors are
#      read from the actually-posed pelvis/spine_02 frame (FK-consistent).
#   3. Per-leg crouch was linearly mapped across the four legs' rest-reach ratios; for near-straight
#      stumpy legs (ratios 0.996-1.000) that maps NOISE onto the full [min,max] band (Golem FL 0.06 vs
#      BL 0.14) and the stride was an absolute 0.14-0.22*H regardless of leg length (Golem: a 0.28
#      half-stride on 0.70 legs). Fix: uniform crouch = crouch_frac * mean leg length, half-stride =
#      stride_frac * each leg's own length.
#   4. Near-straight legs' landmark-derived knee pole is noise (knee 0.5-2% of leg length off the
#      hip-foot line: Golem FR bowed sideways). Fix: legs straighter than `straight_leg_thresh` bow
#      backward (-FORWARD), the Griffin/Kirin convention.
#   5. Tail sway was pitch (local X) for every tail bone; on the quadruped template's Z-up roll that
#      swings the tail up/down (Basilisk's tail went below ground). Fix: tail_yaw/tail_pitch params.
#   6. Pelvis roll/yaw were local Euler on a bone that runs from root (near the ground) UP to the
#      pelvis landmark, so "roll" (local Y) was really yaw about a near-vertical axis. Fix: world-axis
#      roll about FORWARD and yaw about world Z, conjugated into the pelvis's local frame.
# Griffin has no `fk_anchored` key, so every Griffin code path below is untouched (byte-identical
# baked Move action re-verified).
_FK_COMMON = dict(fk_anchored=True, straight_leg_thresh=0.06, foot_curl=6.0, toe_curl=10.0)
GAIT_PARAMS["golem"].update(_FK_COMMON, cycle_seconds=1.3, crouch_frac=0.075, stride_frac=0.12,
                            lift_frac=0.10, scapula_swing=3.0, pelvis_roll=3.0, pelvis_yaw=2.0,
                            body_bob=0.006, tail_yaw=3.0, tail_pitch=0.0, head_amp=0.3)
GAIT_PARAMS["tarasque"].update(_FK_COMMON, cycle_seconds=1.2, crouch_frac=0.075, stride_frac=0.13,
                               lift_frac=0.10, scapula_swing=3.0, pelvis_roll=3.5, pelvis_yaw=3.0,
                               body_bob=0.006, tail_yaw=4.0, tail_pitch=0.0, head_amp=0.35)
GAIT_PARAMS["kirin"].update(_FK_COMMON, cycle_seconds=1.0, crouch_frac=0.05, stride_frac=0.17,
                            lift_frac=0.16, scapula_swing=6.0, pelvis_roll=2.0, pelvis_yaw=2.0,
                            body_bob=0.008, tail_yaw=6.0, tail_pitch=2.0, head_amp=1.2,
                            duty=0.6)
GAIT_PARAMS["basilisk"].update(_FK_COMMON, cycle_seconds=1.1, crouch_frac=0.04, stride_frac=0.16,
                               lift_frac=0.10, scapula_swing=5.0, pelvis_roll=1.5, pelvis_yaw=5.0,
                               body_bob=0.004, tail_yaw=7.0, tail_pitch=0.0, head_amp=0.8)
# v20 (anim-last): Frost Wyrm -- quadruped template, BODY forward axis stored on the armature (head
# turned ~20 deg). Short stocky near-vertical legs (~0.5 normalised): Golem/Tarasque-class stride and
# lift fractions (NOT Griffin amplitudes), a calm deliberate 1.25 s cycle, and the long 4-bone tail
# carrying the visible secondary motion (yaw sway, a little pitch).
GAIT_PARAMS["frost_wyrm"] = dict(GAIT_PARAMS["tarasque"])
GAIT_PARAMS["frost_wyrm"].update(_FK_COMMON, cycle_seconds=1.25, duty=0.6, crouch_frac=0.07,
                                 stride_frac=0.13, lift_frac=0.11, scapula_swing=4.0,
                                 pelvis_roll=2.5, pelvis_yaw=3.0, body_bob=0.006, tail_yaw=5.0,
                                 tail_pitch=1.0, head_amp=0.8)
# v20 (anim-last): Treant -- rig_templates/biped_arms.py. Upright biped walk on the same fk_anchored
# 2-leg machinery (BL/BR alternate at phase 0/0.5) plus `bird`-style semantic DATA layers (the
# extras mechanism is generic: any bone, any bird_pose channel): a slow 1.6 s cycle, gentle steps
# (stride/lift scaled to its ~0.55 legs), a wide side-to-side rock (pelvis roll + spine bank over the
# planted foot), each arm swinging opposite its same-side leg (the out-stretched left arm swings with
# "turn" -- it points along LAT, so a pitch would only twist it -- the bent right arm with "pitch"),
# forearms lagging, and the leaf crown swaying/rustling behind the head. `knee_pole`: "forward" --
# a biped's knee bows forward (the wide stance makes the landmark knees bow sideways in X).
# v20 round 2 (producer review): the Treant is now a HUMANOID rig (biped_arms `humanoid` opt-in:
# relaxed A-pose arms, clavicle/shoulder/elbow/wrist, hip/knee/ankle, 3-segment spine + neck), so the
# walk is authored as a human one, gentle and slow: CONTRALATERAL arm swing (each upper arm's `flex`
# -- shoulder flexion, + = forward -- in antiphase with its same-side leg, so the left arm comes
# forward with the right leg), elbows held softly bent and flexing a little more on the forward
# swing (forearm lags the upper arm), wrists trailing; arms held ~12 deg in from the rest A-pose
# (`abd` hold) so they hang relaxed; shoulders counter-rotate against the hips (spine_03 turn), the
# torso sways over the stance foot (spine_01 bank), the neck keeps the gaze level, and the crown
# follows through behind the head (each lobe lags the next). Heel-to-toe on the root feet: a larger
# foot_curl/toe_curl (heel peels up and the toes roll at push-off), and a higher lift for a clear
# knee bend in swing. Every channel is clamped to bird_pose's human joint limits on the frame it is
# posed (the hinge bones can only flex). gait.py's own spine_02/neck_01/head sinusoids (HEAD_AMP)
# still run; spine_02/head get no semantic layers here because those raw sinusoids overwrite them.
_TREANT_SWAY = [
    ("b", "arm_L_upper", "flex", 16.0, 1, -1.57), ("b", "arm_R_upper", "flex", 16.0, 1, 1.57),
    ("b", "arm_L_fore", "flex", 9.0, 1, -2.07), ("b", "arm_R_fore", "flex", 9.0, 1, 1.07),
    ("b", "arm_L_hand", "flex", 7.0, 1, -2.5), ("b", "arm_R_hand", "flex", 7.0, 1, 0.64),
    ("b", "arm_L_clav", "abd", 2.0, 2, 0.0), ("b", "arm_R_clav", "abd", 2.0, 2, 0.0),
    ("b", "spine_01", "bank", 2.5, 1, 0.3), ("b", "spine_03", "turn", 3.0, 1, -1.57),
    ("b", "neck", "bank", -1.5, 1, 0.3), ("b", "neck", "turn", -2.0, 1, -1.57),
    ("b", "crown_01", "turn", 3.0, 1, -0.6), ("b", "crown_01", "pitch", 2.5, 2, -0.9),
    ("b", "crown_02", "turn", 3.0, 1, -0.9), ("b", "crown_02", "pitch", -3.0, 2, -1.2),
    ("b", "crown_03", "turn", 3.0, 1, -1.2), ("b", "crown_03", "pitch", 2.5, 2, -1.5)]
_TREANT_HOLD = [("b", "arm_L_upper", "abd", -12.0), ("b", "arm_R_upper", "abd", -12.0),
                ("b", "arm_L_fore", "flex", 14.0), ("b", "arm_R_fore", "flex", 14.0),
                ("b", "arm_L_hand", "flex", 6.0), ("b", "arm_R_hand", "flex", 6.0)]
GAIT_PARAMS["treant"] = dict(GAIT_PARAMS["tarasque"])
GAIT_PARAMS["treant"].update(_FK_COMMON, cycle_seconds=1.6, duty=0.6, crouch_frac=0.06,
                             stride_frac=0.14, lift_frac=0.11, pelvis_roll=4.0, pelvis_yaw=4.0,
                             body_bob=0.01, tail_yaw=0.0, tail_pitch=0.0, head_amp=0.6,
                             foot_curl=10.0, toe_curl=16.0,
                             knee_pole="forward", bird=True, bird_layers=_TREANT_SWAY,
                             bird_hold=_TREANT_HOLD)
# v20 (anim-last): Leviathan -- rig_templates/serpent.py, `locomotion: "slither"`. No legs: Move is
# an in-place slither, baked per frame through anim/bird_pose.py's semantic channels: a TRAVELLING
# yaw wave runs down the flat coil from the neck base to the tail tip (each tail bone's "turn" lags
# the previous one by `wave_lag` rad, amplitude growing toward the tip; turn is about world UP, so
# the coil -- the creature's support -- stays flat on the ground), the S-neck sways side to side and
# bobs like a sea serpent (neck turn/pitch layers, head counter-turned to keep the gaze forward),
# side fins flutter, the frill trails. Integer cycles keep the loop seamless; the root never moves.
GAIT_PARAMS["leviathan"] = dict(
    locomotion="slither", cycle_seconds=1.5, n_tail=9, wave_cycles=1, wave_lag=0.75,
    wave_amp=(3.0, 9.0),  # deg on tail_01 .. tail_NN (linear ramp)
    layers=[("b", "body_01", "turn", 2.0, 1, 0.0), ("b", "body_02", "turn", 3.0, 1, -0.5),
            ("b", "body_03", "turn", 3.5, 1, -1.0), ("b", "body_04", "turn", 3.0, 1, -1.5),
            ("b", "body_05", "turn", 2.0, 1, -2.0), ("b", "head", "turn", -5.0, 1, -1.0),
            ("b", "body_02", "pitch", 2.0, 2, 0.0), ("b", "body_03", "pitch", -2.5, 2, -0.6),
            ("b", "body_04", "pitch", 2.5, 2, -1.2), ("b", "head", "pitch", -2.0, 2, -1.8),
            ("b", "fin_L", "pitch", 12.0, 2, 0.0), ("b", "fin_R", "pitch", 12.0, 2, 0.0),
            ("b", "fin_L", "turn", 6.0, 2, 1.2), ("b", "fin_R", "turn", -6.0, 2, 1.2),
            ("b", "frill", "pitch", 4.0, 2, -2.2), ("b", "jaw", "pitch", -2.0, 2, 0.5)],
)
# v19 (birds, rig_templates/winged_biped.py). `bird=True` (the Phoenix's first, walking pass; no
# creature uses it now, the code path is kept): an upright bird's hop-walk on the same
# fk_anchored biped machinery (2 legs alternate at phase 0/0.5 via the existing 2-leg branch) plus
# `bird` extras -- wing balance, tail sway, crest flicker -- authored through anim/bird_pose.py's
# semantic channels as DATA (sine layers below), replacing the Griffin's hard-coded wing-settle
# lines. Thunderbird: locomotion "hover" -- it never stands, so Move is a flap cycle (faster than
# Idle's), forward lean and body bob, no foot contact (see the hover branch below).
# v19 round 2 (producer change): the Phoenix FLIES like the Thunderbird -- the same hover branch
# below, Phoenix-specific values only. Its rest pose has the wings half-folded (wing_*_02 pointing
# up), so a root-only flap would just twitch them: wing_*_02 carries a held -35 deg open offset
# (bird_pose.HOVER_HOLDS, with the leg tuck) plus a 30 deg swing a quarter-cycle AHEAD of
# wing_*_01 (flap lag -pi/2), which spreads the wing flat on the downbeat (wing_02 at -65,
# horizontal) and folds it back toward the rest V on the upstroke. Legs are held tucked with only a
# small dangle; the flame tail is held streaming back (-pitch) and sways as follow-through.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bird_pose as _BPH  # noqa: E402  (HOVER_HOLDS: the tuck/wing-open hold shared with keyed.py)
GAIT_PARAMS["phoenix"] = dict(
    locomotion="hover", cycle_seconds=1.0, lean=-16.0, neck_comp=8.0, head_comp=6.0,
    bob=0.035, bob_base=0.035,
    # 2 flaps per 1.0 s (Idle: 2 per 3.0 s); amps = wing_01 / wing_02 (open-fold) / wing_03.
    flap_cycles=2, flap_amps=(34.0, 30.0, 8.0), flap_lags=(0.0, -1.57, -1.0), flap_sweep=6.0,
    tail_sway=[("b", "tail_01", "pitch", 4.0, 2, -1.2), ("b", "tail_02", "pitch", 5.0, 2, -1.8),
               ("b", "tail_03", "pitch", 6.0, 2, -2.4), ("b", "tail_01", "turn", 5.0, 1, 0.0),
               ("b", "tail_02", "turn", 7.0, 1, -0.6), ("b", "tail_03", "turn", 9.0, 1, -1.2),
               ("b", "leg_BL_thigh", "pitch", 3.0, 2, -0.5),
               ("b", "leg_BR_thigh", "pitch", 3.0, 2, -0.5),
               ("b", "crest_01", "pitch", 4.0, 2, 1.0), ("b", "crest_02", "pitch", 9.0, 2, 1.6),
               ("b", "crest_02", "turn", 6.0, 1, 0.3), ("b", "head", "pitch", -2.0, 2, 0.0)],
    hold=_BPH.HOVER_HOLDS["phoenix"] + [("b", "tail_01", "pitch", -30.0),
                                        ("b", "tail_02", "pitch", -15.0),
                                        ("b", "tail_03", "pitch", -10.0)],
)
GAIT_PARAMS["thunderbird"] = dict(
    locomotion="hover", cycle_seconds=1.0, lean=-14.0, neck_comp=7.0, head_comp=5.0,
    bob=0.035, bob_base=0.035,
    # 2 flaps per 1.0 s Move cycle (Idle, keyed.py, flaps 2 per 3.0 s): a faster flap.
    flap_cycles=2, flap_amps=(26.0, 14.0, 10.0), flap_lags=(0.0, 0.7, 1.4), flap_sweep=6.0,
    tail_sway=[("b", "tail_01", "pitch", 3.0, 2, -1.2), ("b", "tail_02", "pitch", 4.0, 2, -1.8),
               ("b", "tail_03", "pitch", 5.0, 2, -2.4), ("b", "tail_04", "pitch", 6.0, 2, -3.0),
               ("b", "tail_02", "turn", 4.0, 1, 0.0), ("b", "tail_04", "turn", 6.0, 1, -0.8),
               ("b", "leg_BL_thigh", "pitch", 3.0, 2, -0.5),
               ("b", "leg_BR_thigh", "pitch", 3.0, 2, -0.5),
               ("b", "head", "pitch", -2.0, 2, 0.0)],
    hold=[("b", "tail_01", "pitch", -6.0), ("b", "tail_02", "pitch", -4.0)],
)
GP = GAIT_PARAMS.get(CREATURE, GAIT_PARAMS["griffin"])
FK_ANCHORED = GP.get("fk_anchored", False)
if GP.get("bird"):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import bird_pose as BP
if "cycle_seconds" in GP and "cycle-seconds" not in args:
    CYCLE_SECONDS = GP["cycle_seconds"]
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


# Lead-review walk-direction fix: FORWARD is derived strictly from the rig's own rest pose --
# normalize(head - pelvis), projected onto the ground plane (Z zeroed) -- instead of a hard-coded
# +Y (or -Y) axis assumption. Every place in this file that moves a foot/joint fore-aft (stride
# sweep, swing arc, scapula fore-aft swing) is built as `rest_point + FORWARD * signed_offset`, so
# the whole file is correct regardless of which way a given mesh's head happens to point in its own
# native axes. This replaces an earlier version that wrote `rest.y + y_off` directly: that silently
# assumed +Y was forward, which was backwards for this mesh (round 10's landmarks put the head at
# -Y from the pelvis -- see rig_templates/winged_quadruped.py's detect_landmarks_handplaced
# forward_sign fix) and produced a Griffin that walked with each foot protracting toward the TAIL
# during swing and retracting toward the HEAD during stance -- exactly backwards (producer review,
# round 11).
_pelvis_head_local, _ = rest_head_tail("pelvis")
_head_bone_head_local, _ = rest_head_tail("head")
FORWARD = (_head_bone_head_local - _pelvis_head_local)
FORWARD.z = 0.0  # ground-plane projection
if FORWARD.length < 1e-6:
    raise RuntimeError(
        "Cannot derive a walk-forward direction: the 'head' and 'pelvis' bones are at the same "
        "ground-plane position in the rest pose. gait.py refuses to fall back to a hard-coded "
        "axis here (that's exactly the bug this fix removes) -- check the rig's landmarks.")
FORWARD.normalize()
if "forward" in arm_data:
    # v19: winged_biped rigs store their forward axis on the armature (the Phoenix's head is turned
    # ~20 deg off its body axis, so head-minus-pelvis would walk it crabwise). Other rigs have no
    # such property and keep the derivation above unchanged.
    FORWARD = mathutils.Vector(tuple(arm_data["forward"]))
    FORWARD.z = 0.0
    FORWARD.normalize()
print(f"FORWARD (ground-plane, head-pelvis, no hard-coded axis): {tuple(round(c, 4) for c in FORWARD)}")

if GP.get("locomotion") == "hover":
    # v19 Thunderbird: a hovering flier's Move. No leg stepping exists to drive, so none of the IK
    # machinery below applies: a flap cycle (tip lagging root), a forward lean carried on the root
    # with neck/head counter-pitched to keep the gaze level, a body bob phased to rise just after
    # each downstroke, tail/leg follow-through. In place (the game moves the root); Blender root
    # height stays >= 0 (the engine adds the sidecar hover offset on top).
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import bird_pose as BP
    _n = int(round(CYCLE_SECONDS * FPS))
    _layers = (BP.wing_layers(GP["flap_cycles"], GP["flap_amps"], GP["flap_lags"], GP["flap_sweep"])
               + list(GP["tail_sway"]))
    _hold = {("b", b, ch): v for _, b, ch, v in GP["hold"]}
    _hold.update({("r", "pitch"): GP["lean"], ("b", "neck_01", "pitch"): GP["neck_comp"],
                  ("b", "head", "pitch"): GP["head_comp"]})

    def _hover_chans(t):
        c = BP.add_channels(dict(_hold), BP.sines(_layers, t))
        # body rises ~a quarter-cycle after the downstroke
        c[("r", "z")] = GP["bob_base"] + GP["bob"] * math.sin(2 * math.pi * GP["flap_cycles"] * t - 1.2)
        return c
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="POSE")
    BP.bake_frames(arm_obj, scene, FORWARD, "Move", _n, _hover_chans)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"MOVE ACTION (hover): {_n + 1} frames @ {FPS}fps, flap cycles {GP['flap_cycles']}")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f"{CREATURE}_move.blend"))
    print("GAIT DONE")
    raise SystemExit(0)


if GP.get("locomotion") == "slither":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import bird_pose as BP
    _n = int(round(CYCLE_SECONDS * FPS))
    _a0, _a1 = GP["wave_amp"]
    _nt = GP["n_tail"]
    _layers = list(GP["layers"])
    for _i in range(1, _nt + 1):
        _amp = _a0 + (_a1 - _a0) * (_i - 1) / max(1, _nt - 1)
        _layers.append(("b", f"tail_{_i:02d}", "turn", _amp, GP["wave_cycles"], -GP["wave_lag"] * _i))
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="POSE")
    BP.bake_frames(arm_obj, scene, FORWARD, "Move", _n, lambda t: BP.sines(_layers, t))
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"MOVE ACTION (slither): {_n + 1} frames @ {FPS}fps, {_nt} tail bones, wave lag "
          f"{GP['wave_lag']} rad/bone, amp {_a0}..{_a1} deg")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f"{CREATURE}_move.blend"))
    print("GAIT DONE")
    raise SystemExit(0)


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


def aim_min_twist(pb, head_pos, tail_pos, parent_world, parent_rest):
    """Round-4 fix 1 (fk_anchored creatures only): world matrix with translation=head_pos and +Y
    aimed head->tail, reached by the MINIMUM rotation from the bone's FK-neutral orientation (its
    rest orientation carried by the parent's actual pose, i.e. identity matrix_basis) -- so the
    bone never twists about its own axis relative to its parent, whatever its rest roll is. Unlike
    aim_matrix(+up_hint), which rebuilds the roll from a world axis and so twisted these rigs'
    scapula/thigh/shin by 40-180 deg per frame."""
    neutral = (parent_world @ parent_rest.inverted() @ pb.bone.matrix_local).to_3x3().normalized()
    y_now = neutral.col[1].normalized()
    y_want = tail_pos - head_pos
    if y_want.length < 1e-7:
        y_want = y_now
    swing = y_now.rotation_difference(y_want.normalized()).to_matrix()
    mat = (swing @ neutral).to_4x4()
    mat.translation = head_pos
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
DUTY = GP["duty"]              # fraction of the cycle each foot spends in stance
BODY_BOB = GP["body_bob"] * H    # vertical bob amplitude, two bob cycles per stride (both feet contribute)
# Round 12 (producer review -- foot orientation fix): small toe-down curl amplitude during swing,
# peaking at mid-swing and easing back to the flat stance direction by touchdown. "Small" per the
# task brief's own wording -- see set_leg_pose's docstring for why this replaces the old
# "aim the foot/toe bone literally at the raw IK target" approach.
FOOT_CURL_MAX_DEG = GP["foot_curl"]
TOE_CURL_MAX_DEG = GP["toe_curl"]  # toes curl a bit more than the pastern/foot segment, same peak timing
# Round 13 (producer review -- toe-fan fix): a foreleg's three splayed toes (toe_in/mid/out) stay
# EXACTLY at their bind-pose fan during stance (per the task brief: "no curl" while planted -- a
# stricter requirement than the single-toe TOE_CURL_MAX_DEG above, which DID allow some stance-
# adjacent easing via the shared foot curl) and each curls up to this amount, around its OWN hinge
# axis, during swing only.
TOE_FAN_CURL_MAX_DEG = GP["toe_fan_curl"]
RAW_STRIDE = GP["raw_stride"] * H   # desired fore-aft HALF-stride excursion (foot sweeps +RAW_STRIDE to
# -RAW_STRIDE), before the per-leg reach clamp. Full peak-to-peak foot travel is 2x this -- the
# lead-review fix round's "readable stride, ~25-40% of body length" target is interpreted as that
# peak-to-peak distance, so this aims for ~30% of H unclamped, clamped down per-leg by actual IK
# reach (see the per-leg safe_stride computation above for the real achieved numbers per leg).

# Lead-review round 5: "the fix is animation, not geometry." The hand-placed rest pose is nearly
# straight-legged (round 4 found FL/FR's rest hip-to-ground distance is 91-99.7% of their own
# total leg length) -- a real walking quadruped crouches slightly, which is exactly what creates
# IK slack: stride ~= 2*sqrt(L^2 - h^2), so even a modest drop in hip height h buys a large gain
# once h is close to L. CROUCH is held constant through the whole cycle (the existing BODY_BOB
# oscillates on TOP of it, per the task brief), applied directly to the hip position fed into the
# IK solve (not just a cosmetic root bob -- see the per-leg precompute below and set_leg_pose's
# docstring for why those are different things in this file's existing architecture).
#
# Round 7: a single flat CROUCH left FR short of the lead's ">=20% of H like BL" stride target
# (FR's rest geometry uses a larger fraction of its own max reach than the other three legs even
# after an 8%-of-H crouch -- confirmed directly in round 6's numbers and unchanged in round 7's
# retopologized-mesh re-measurement). Fixed with a PER-LEG crouch instead of one shared constant:
# each leg's own rest reach-utilization ratio (hip-to-foot rest distance / max_reach, pure rest
# geometry, independent of crouch) is measured in a cheap first pass below, then linearly mapped
# across the four legs' own observed range onto a [CROUCH_MIN, CROUCH_MAX] band -- the leg closest
# to fully extended (highest ratio, FR on this mesh) gets the most crouch, the leg with the most
# natural slack (lowest ratio) gets the least. This widens the original "6-10% of H" band a little
# at the top end specifically to buy FR enough IK slack to clear 20%; legs that already met the
# target keep a crouch close to the original flat 8%.
CROUCH_MIN = GP["crouch_min"] * H
CROUCH_MAX = GP["crouch_max"] * H
_rest_reach_ratio = {}
for _side in leg_sides:
    _hip_rest0, _knee0 = rest_head_tail(f"leg_{_side}_thigh")
    _, _ankle0 = rest_head_tail(f"leg_{_side}_shin")
    _, _foot0 = rest_head_tail(f"leg_{_side}_foot")
    _L1_0 = (_knee0 - _hip_rest0).length
    _L2_0 = (_ankle0 - _knee0).length + (_foot0 - _ankle0).length
    _rest_reach_ratio[_side] = (_foot0 - _hip_rest0).length / max(_L1_0 + _L2_0, 1e-6)
_lo_ratio, _hi_ratio = min(_rest_reach_ratio.values()), max(_rest_reach_ratio.values())
CROUCH_BY_SIDE = {}
for _side in leg_sides:
    if _hi_ratio - _lo_ratio < 1e-6:
        _frac = 0.5
    else:
        _frac = (_rest_reach_ratio[_side] - _lo_ratio) / (_hi_ratio - _lo_ratio)
    CROUCH_BY_SIDE[_side] = CROUCH_MIN + _frac * (CROUCH_MAX - CROUCH_MIN)
print(f"PER-LEG CROUCH: ratios={ {s: round(r,3) for s,r in _rest_reach_ratio.items()} } "
      f"crouch={ {s: round(c,4) for s,c in CROUCH_BY_SIDE.items()} }")
if FK_ANCHORED:
    # Round-4 fix 3 (see GAIT_PARAMS' note): one UNIFORM crouch, scaled to this creature's own mean
    # leg length -- the body is a single rigid parent, so in FK-anchored mode a per-leg crouch would
    # just mean the IK anchors disagree with where the body actually is.
    _leg_lengths = {}
    for _side in leg_sides:
        _h0, _k0 = rest_head_tail(f"leg_{_side}_thigh")
        _, _a0 = rest_head_tail(f"leg_{_side}_shin")
        _, _f0 = rest_head_tail(f"leg_{_side}_foot")
        _leg_lengths[_side] = (_k0 - _h0).length + (_a0 - _k0).length + (_f0 - _a0).length
    _uniform_crouch = GP["crouch_frac"] * sum(_leg_lengths.values()) / len(_leg_lengths)
    # ...but never less than what each leg needs to actually reach its own stride at the bob peak
    # with this side's pelvis-roll lift (splayed/near-straight legs -- Tarasque -- need more).
    for _side in leg_sides:
        # The IK chain in this mode is hip->knee->ANKLE (thigh+shin); the foot keeps its rest
        # orientation, so the ankle target is the foot target minus the rest foot vector.
        _h0, _k0 = rest_head_tail(f"leg_{_side}_thigh")
        _, _a0 = rest_head_tail(f"leg_{_side}_shin")
        _l_ik = (_k0 - _h0).length + (_a0 - _k0).length
        _d = _a0 - _h0
        _fwd = abs(_d.dot(FORWARD))
        _lat_sq = (_d - _d.dot(FORWARD) * FORWARD).xy.length_squared
        _want = (0.98 * _l_ik) ** 2 - _lat_sq - (_fwd + GP["stride_frac"] * _leg_lengths[_side]) ** 2
        if _want > 0:
            _need = ((_h0.z - _a0.z) - math.sqrt(_want) + BODY_BOB
                     + abs(_h0.x) * math.sin(math.radians(GP["pelvis_roll"])))
            _uniform_crouch = max(_uniform_crouch, _need)
    CROUCH_BY_SIDE = {s: _uniform_crouch for s in leg_sides}
    print(f"FK-ANCHORED: leg_lengths={ {s: round(v,3) for s,v in _leg_lengths.items()} } "
          f"uniform crouch={_uniform_crouch:.4f}")
CROUCH = sum(CROUCH_BY_SIDE.values()) / len(CROUCH_BY_SIDE)  # cosmetic root/body bob only (a single
# rigid global transform can't differ per leg) -- the mean of the per-leg values, not a separate
# tuned constant, so the visual torso drop stays representative of what the legs are actually doing.
# Scapula swing (round 5): a real quadruped's foreleg reach comes mostly from the shoulder blade
# itself swinging fore-aft, not the elbow alone. SCAPULA_SWING_DEG is the rotation amplitude;
# the resulting fore-aft shoulder-socket excursion (scapula_len * sin(angle)) is added to each
# foreleg's effective hip position, in phase with that leg's own swing, giving MORE apparent
# stride than knee articulation alone would (see the per-frame loop for how this composes with
# CROUCH's reach-margin gain).
SCAPULA_SWING_DEG = GP["scapula_swing"]  # Griffin: middle of the requested 10-15 degree band
# Pelvis roll/yaw (round 5): a small weight-shift motion in phase with the hind legs' own stride
# frequency (not the 2x-per-cycle body bob) -- subtle secondary motion, not a reach mechanism.
PELVIS_ROLL_DEG = GP["pelvis_roll"]
PELVIS_YAW_DEG = GP["pelvis_yaw"]
TAIL_AMP = GP["tail_amp"]    # scales the tail sinusoid amplitudes a few hundred lines down
HEAD_AMP = GP["head_amp"]    # scales spine_02/neck_01/head's sinusoid amplitudes (both the early
# spine02_local_pitch computation the leg IK is solved against AND the later keyframed value below
# MUST use the identical formula -- see both call sites' own comments)

legs = {}
scapula_rest = {}  # side -> (head, tail, rest_matrix) for front legs only
for side in leg_sides:
    hip_rest, knee = rest_head_tail(f"leg_{side}_thigh")
    _, ankle = rest_head_tail(f"leg_{side}_shin")
    _, foot = rest_head_tail(f"leg_{side}_foot")
    # Round 13 (producer review): forelegs now have THREE independent toe bones (toe_in/mid/out --
    # a real eagle foot's splayed toes, each with its own talon) instead of one aggregate toe bone.
    # `has_toe_fan` is detected from the rig itself (bone presence), not assumed from `side`, so
    # this stays correct if a future creature's toe-fan legs aren't simply "whichever side starts
    # with F". Hind legs keep the single-toe-bone path entirely unchanged.
    has_toe_fan = f"leg_{side}_toe_in" in arm_data.bones
    if has_toe_fan:
        toe = None  # no single aggregate toe bone for these legs -- see toe-fan precompute below
    else:
        _, toe = rest_head_tail(f"leg_{side}_toe")
    # Per-leg CROUCH applied directly to the hip used for every reach/IK computation below -- this
    # IS the "lower the body" mechanism for leg purposes (see CROUCH_BY_SIDE's docstring above for
    # why a cosmetic root bob alone would not actually buy any reach margin in this file's
    # architecture: the IK solve's hip input has always been independent of root/pelvis's own
    # animated world position, by original design, so CROUCH must shift the hip value fed into the
    # solve directly).
    hip = hip_rest - mathutils.Vector((0, 0, CROUCH_BY_SIDE[side]))
    if side.startswith("F"):
        scapula_head, scapula_tail = rest_head_tail(f"scapula_{side}")
        scapula_pb_tmp = arm_obj.pose.bones.get(f"scapula_{side}")
        scapula_rest[side] = (scapula_head, scapula_tail,
                               scapula_pb_tmp.bone.matrix_local if scapula_pb_tmp else None,
                               (scapula_tail - scapula_head).length)
    # L1 is the thigh bone's own TRUE (intrinsic, rest) length -- computed from hip_rest, not the
    # crouched hip, since crouch only shifts WHERE the 2-bone chain is anchored for IK purposes,
    # not the chain's own physical segment lengths.
    L1 = (knee - hip_rest).length
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
    #
    # Lead-review round 4 (hand-placed, long legs): a flat 0.95 safety factor broke down on this
    # rig -- these legs are long enough that the rest pose itself already sits within ~0.1-0.3% of
    # max_reach (hip height alone very nearly uses up the whole L1+L2 chain; confirmed directly:
    # FR's rest_dist/max_reach = 0.597/0.598), so reach_margin = max_reach*0.95 landed BELOW the
    # rest distance itself, before any stride was even attempted -- every leg's r_sq went negative
    # or near-zero and every leg (bar one) fell straight to the degenerate floor stride. The margin
    # is now the LARGER of the flat 95% budget and "a little past the rest pose's own distance" --
    # never beyond max_reach itself (triangle inequality already guarantees rest_dist <= max_reach
    # for any geometrically valid hand-placed chain, so this can't ask for the impossible) -- so a
    # leg whose bind pose is itself nearly fully extended still gets a small but real usable margin
    # instead of an immediate floor.
    SAFETY = 0.95
    reach_margin = min(max_reach, max(max_reach * SAFETY, (foot - hip).length + 0.02 * H))
    leg_raw_stride = RAW_STRIDE
    if FK_ANCHORED:
        # The hip rides the body in this mode, so size the reach check for the HIGHEST the hip gets
        # (bob peak + pelvis-roll lift on this side), and keep a real 2% knee-bend margin -- a
        # straight stump has no slack otherwise. Stride scales with this leg's own length.
        _roll_lift = abs(hip_rest.x) * math.sin(math.radians(GP["pelvis_roll"]))
        hip = hip_rest - mathutils.Vector((0, 0, CROUCH_BY_SIDE[side] - BODY_BOB - _roll_lift))
        reach_margin = (L1 + shin_len) * 0.98  # IK chain = thigh+shin to the ankle (see below)
        leg_raw_stride = GP["stride_frac"] * max_reach
    # All four feet are grounded in this mesh's own bind pose (producer-confirmed, see
    # rig_templates/winged_quadruped.py's docstring) -- ground_z=0 for every leg, front or back,
    # no "forelegs hover at bind height" special case. The horizontal-slicing landmark detection
    # placed each leg's hip/knee/ankle directly from the mesh's own geometry (not a guessed
    # proportional fold), so there is no longer an artificially extreme bind-pose knee bend to
    # animate across.
    ground_z = 0.0
    if FK_ANCHORED:
        # The foot is held at its bind rotation through stance, so the foot JOINT sits at its own
        # rest height when the toe is grounded -- target that, not z=0 (which a fully-extended
        # stump can't reach, clamping the solve every frame).
        ground_z = foot.z
    # Decompose (foot_rest - hip) relative to FORWARD instead of assuming the fore-aft sweep axis
    # is literally world Y: `fwd_offset` is the signed component already along FORWARD (what the
    # stride sweep moves along), `base_sq` is everything perpendicular to it (sideways ground
    # offset + the hip-to-ground vertical drop) -- the part the stride sweep does NOT change. For a
    # rig whose FORWARD happens to be exactly (0, 1, 0) or (0, -1, 0) this reduces to the original
    # x^2+z^2 / foot.y-hip.y formulas exactly; for any other ground-plane heading it generalizes
    # correctly instead of silently assuming an axis.
    diff = mathutils.Vector((foot.x - hip.x, foot.y - hip.y, ground_z - hip.z))
    if FK_ANCHORED:
        diff = ankle - hip  # the ankle is the IK target in this mode (foot vector held at rest)
    fwd_offset = diff.dot(FORWARD)
    perp = diff - fwd_offset * FORWARD
    base_sq = perp.length_squared
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
        safe_stride = max(0.005 * H, min(r - abs(fwd_offset), leg_raw_stride))
    if FK_ANCHORED:
        hip = hip_rest - mathutils.Vector((0, 0, CROUCH_BY_SIDE[side]))  # nominal (per-frame FK overrides)

    # Lift scales WITH the solved stride (not a fixed absolute height) so a geometrically-
    # constrained short stride (this Griffin's: the landmark-placed hip leaves little slack before
    # hitting max reach -- see safe_stride above) still reads as a proportionate short, quick step
    # rather than "barely moves forward but kicks way up," which a fixed large LIFT would produce.
    lift = max(0.02 * H, 0.9 * safe_stride)
    if FK_ANCHORED:
        lift = GP["lift_frac"] * max_reach

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

    # Round 14 (producer review: round 13's toe-fan still showed "jagged folded geometry at the
    # foot/toe base" and "mangled, overlapping toes" in Move frames). ROOT CAUSE (confirmed via a
    # numeric diagnostic comparing posed-vs-rest matrices, not guessed): round 12/13 built the foot
    # and toe bones' ORIENTATION via `aim_matrix` + a hand-picked `up_hint` world axis -- a
    # DIFFERENT up_hint for the foot (world X) than for the toes (world -Z). aim_matrix's X/Z
    # construction from a given up_hint has NO reason to reproduce the bone's actual REST roll
    # (built by Blender's own default roll-0 convention when the edit bone was created, totally
    # unrelated to any up_hint choice) -- confirmed directly: even during STANCE, with the foot
    # bone's Y-axis exactly matching its bind direction, its full pose-vs-rest matrix differed by
    # 90 degrees (a 44-degree matrix_basis rotation around a non-anatomical compound axis
    # (0.69, 0.19, -0.70) -- NOT a clean single-axis "wrist bends forward," just a roll-convention
    # artefact), and the toe's rotation RELATIVE TO THE FOOT (what the blended mesh at the foot/toe
    # junction actually feels) drifted 160+ degrees from its bind-relative transform. Two
    # independently-mismatched roll conventions meeting at one blended vertex region is exactly what
    # shears/folds triangles there, even though each bone's OWN "shape" (toe-to-toe angle, ground
    # height) still measured fine -- which is why round 13's toe_fan gate (toe-to-toe angle +
    # ground clearance only) passed despite the visible mesh damage.
    #
    # FIX: stop reconstructing orientation from a world-space up_hint entirely for foot/toe bones.
    # The FOOT keeps round 12's real goal -- world orientation stays AT BIND during stance,
    # "with the shin/wrist taking up the difference" -- but now implemented by literally copying
    # the bind rest rotation (foot_rest_rot4) instead of re-deriving a roll via aim_matrix, so there
    # is no possible mismatch from the bind roll at zero curl. The TOE bones are driven with a
    # DIRECT LOCAL matrix_basis (identity at stance, a small local-axis rotation during swing) so
    # they rigidly inherit whatever the foot's ACTUAL pose is via normal FK composition -- by
    # construction this has ZERO relative-to-foot mismatch regardless of the foot's own world
    # orientation, which is what actually matters for the blended mesh at the junction (a toe
    # anatomically just needs to follow its own forefoot rigidly, not independently chase a world
    # direction). Curl axes are precomputed once, transformed from world space into each bone's own
    # REST-LOCAL frame (bone_rest.to_3x3().inverted() @ world_axis) so `Matrix.Rotation(angle, 4,
    # local_axis)` composes correctly as a LOCAL-space rotation on top of the bind-preserving pose.
    foot_rest_mat_local = arm_data.bones[f"leg_{side}_foot"].matrix_local
    foot_rest_rot4 = foot_rest_mat_local.to_3x3().to_4x4()  # rest rotation only, translation zeroed
    foot_local_curl_axis = foot_rest_mat_local.to_3x3().inverted() @ mathutils.Vector((1, 0, 0))
    foot_local_curl_axis = foot_local_curl_axis.normalized() if foot_local_curl_axis.length > 1e-6 \
        else mathutils.Vector((1, 0, 0))

    # Round 14 (second fix, found after the first local-rotation fix above introduced a NEW,
    # measured regression: toe tips sinking 0.02-0.06H below ground, confirmed via verify.py's
    # ground-interpenetration/toe_fan gates). ROOT CAUSE: holding the foot at EXACT bind rotation
    # and simply translating it to ankle_w means the foot+toe assembly is now a RIGID BODY whose
    # ground-contact HEIGHT is ankle_w.z + a FIXED rest offset -- nothing compensates any more for
    # CROUCH (and the rest of the IK chain) placing ankle_w at a different HEIGHT than ankle_rest,
    # which rounds 12/13's old "aim toward a ground-level target" approach absorbed as part of its
    # (buggy-roll) re-aiming, and which this round's bind-preserving fix no longer does by
    # construction. The OLD code's near-zero ground readings were never really earned by the
    # geometry -- they were papered over by an explicit `if tip.z < 0: tip.z = 0` clamp on the
    # toe's WORLD position, which this round's FK-driven toe construction has no equivalent for
    # (there is no longer an explicit toe world position to clamp).
    #
    # FIX: solve for the actual height the ankle needs, directly. The foot+toe assembly's
    # ground-contact HEIGHT, given ankle_w and zero curl, is predictable exactly:
    # `ankle_w.z + contact_offset_rest.z` (contact_offset_rest = the FIXED rest-pose vector from
    # ankle to a representative ground-contact point -- the toe tip for a single-toe leg, the
    # MIDDLE toe's tip for a toe-fan leg, the most anatomically central one). set_leg_pose applies
    # a direct Z correction to ankle_w so that predicted height exactly equals the already-computed
    # ground/lift target -- the same "adjust ankle_w directly, let the shin's own aim_matrix target
    # just follow wherever ankle_w ends up" pattern this file already uses for every other
    # foot-placement adjustment (CROUCH, scapula swing), not a new mechanism.
    if has_toe_fan:
        _, contact_point_rest = rest_head_tail(f"leg_{side}_toe_mid")
    else:
        contact_point_rest = toe
    contact_offset_rest = contact_point_rest - ankle

    toe_fan_data = None
    if has_toe_fan:
        # Round 13: per-toe precompute (tip_dir_rest/tip_len kept for documentation/diagnostics).
        # Round 14 ADDS `hinge_axis_local`: the task brief's hinge axis
        # (normalize(cross(toe_direction, world_up))) computed in world space as before, then
        # transformed into this specific toe bone's own REST-LOCAL frame for direct matrix_basis
        # use (see the fix note above).
        toe_base_rest = rest_head_tail(f"leg_{side}_toe_in")[0]  # all 3 toe bones share this head
        world_up = mathutils.Vector((0, 0, 1))
        toes = {}
        for t in ("in", "mid", "out"):
            _, tip_rest = rest_head_tail(f"leg_{side}_toe_{t}")
            tip_vec = tip_rest - toe_base_rest
            tip_len = tip_vec.length
            tip_dir_rest = tip_vec.normalized() if tip_len > 1e-6 else FORWARD.copy()
            hinge_axis_world = tip_dir_rest.cross(world_up)
            hinge_axis_world = hinge_axis_world.normalized() if hinge_axis_world.length > 1e-6 else \
                mathutils.Vector((1, 0, 0))
            toe_rest_mat_local = arm_data.bones[f"leg_{side}_toe_{t}"].matrix_local
            hinge_axis_local = toe_rest_mat_local.to_3x3().inverted() @ hinge_axis_world
            hinge_axis_local = hinge_axis_local.normalized() if hinge_axis_local.length > 1e-6 else \
                mathutils.Vector((1, 0, 0))
            toes[t] = {"tip_rest": tip_rest, "tip_len": tip_len, "tip_dir_rest": tip_dir_rest,
                       "hinge_axis_local": hinge_axis_local}
        toe_fan_data = {"base_rest": toe_base_rest,
                         "offset_rest": toe_base_rest - ankle, "toes": toes}
        toe_len = 0.0
        toe_stance_dir = FORWARD.copy()
        toe_local_curl_axis = None
    else:
        toe_len = (toe - foot).length
        toe_stance_dir = (toe - foot).normalized() if toe_len > 1e-6 else FORWARD.copy()
        # Round 14: same local-curl-axis fix as the toe fan, for the single aggregate toe bone
        # (hind legs) -- reuses world X as the curl axis (matching the pre-round-14 convention),
        # transformed into this toe's own rest-local frame.
        toe_rest_mat_local = arm_data.bones[f"leg_{side}_toe"].matrix_local
        toe_local_curl_axis = toe_rest_mat_local.to_3x3().inverted() @ mathutils.Vector((1, 0, 0))
        toe_local_curl_axis = toe_local_curl_axis.normalized() if toe_local_curl_axis.length > 1e-6 \
            else mathutils.Vector((1, 0, 0))

    leg_bend_dir = bend_dir_by_side.get(side, mathutils.Vector((0, 1, 0)))
    if FK_ANCHORED:
        # Round-4 fix 4: a near-straight leg's landmark-derived pole is noise -- bow it backward.
        _hf = foot - hip_rest
        _t = max(0.0, min(1.0, (knee - hip_rest).dot(_hf) / max(_hf.length_squared, 1e-9)))
        _knee_off = (knee - (hip_rest + _hf * _t)).length / max(max_reach, 1e-6)
        if _knee_off < GP["straight_leg_thresh"]:
            leg_bend_dir = -FORWARD.copy()
            print(f"LEG {side}: near-straight (knee offset {_knee_off:.3f} of leg length) -> "
                  f"knee pole overridden to -FORWARD")
        if GP.get("knee_pole") == "forward":  # v20 Treant: a biped's knee bows forward
            leg_bend_dir = FORWARD.copy()
    legs[side] = {"hip": hip, "hip_rest": hip_rest, "L1": L1, "L2": L2, "shin_len": shin_len, "foot_rest": foot, "toe_rest": toe,
                  "stride": safe_stride, "lift": lift, "_ankle_frac": ankle_frac, "ground_z": ground_z,
                  "knee_rest": knee, "ankle_rest": ankle,
                  "foot_len": foot_len, "toe_len": toe_len, "min_reach": min_reach,
                  "foot_rest_rot4": foot_rest_rot4, "foot_local_curl_axis": foot_local_curl_axis,
                  "contact_offset_rest": contact_offset_rest,
                  "toe_local_curl_axis": toe_local_curl_axis,
                  "has_toe_fan": has_toe_fan, "toe_fan": toe_fan_data,
                  "bend_dir": leg_bend_dir}
    print(f"LEG {side}: hip={tuple(round(c,3) for c in hip)} L1={L1:.3f} L2={L2:.3f} "
          f"max_reach={max_reach:.3f} rest_dist={(foot-hip).length:.3f} "
          f"safe_stride={safe_stride:.3f} (raw {RAW_STRIDE:.3f})")

phase_offset = {}
# Lead-review fix round: real quadruped lateral-sequence walk timing (LH -> LF -> RH -> RF, each a
# quarter-cycle out of phase from the previous) for the confirmed 4-leg case, replacing the earlier
# 2-beat fallback. BL/BR/FL/FR are this template's side codes for back-left/back-right/front-left/
# front-right (see rig_templates/winged_quadruped.py) -- i.e. BL=LH, FL=LF, BR=RH, FR=RF.
#
# Round 11 (producer review via the new verify.py walk_direction gate): the ORIGINAL {BL:0.0,
# FL:0.25, BR:0.5, FR:0.75} mapping here was mislabeled -- touchdown (the moment a leg's own phase
# `ph=(t+phase_offset)%1` crosses 0) happens at t = (-phase_offset) mod 1, which for those values
# actually touches feet down in the order BL, FR, BR, FL (LH, RF, RH, LF) -- a DIAGONAL-sequence
# gait (each hindfoot followed by the OPPOSITE-side forefoot), not the lateral-sequence gait
# (hindfoot followed by the SAME-side forefoot) the comment above claims and a real walking
# quadruped uses. Swapping FL and FR's offsets fixes the actual touchdown order to BL, FL, BR, FR
# (LH, LF, RH, RF) -- confirmed against the new gate's touchdown-order check, which verifies this
# independent of any hard-coded axis.
LATERAL_SEQUENCE = {"BL": 0.0, "FL": 0.75, "BR": 0.5, "FR": 0.25}
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
        # fwd_off is a signed coordinate along FORWARD (not a raw world axis): +stride means "this
        # foot's most-forward/head-ward point" (touchdown), -stride means "most-backward/tail-ward"
        # (toe-off). Stance sweeps +stride -> -stride, LINEAR (constant velocity -- see module
        # docstring: constant stance velocity is what cancels a constant-rate root translation with
        # zero residual slide) -- i.e. the planted foot retracts toward the tail as the body passes
        # over it, which is correct gait regardless of which way FORWARD actually points in this
        # mesh's own native axes (see this file's FORWARD derivation above for why that distinction
        # matters: an earlier version wrote `rest.y + y_off` directly, hard-coding +Y as forward,
        # which was backwards for this mesh and made the Griffin walk with each foot protracting
        # toward the tail and retracting toward the head -- exactly reversed).
        fwd_off = stride * (1.0 - 2.0 * u)
        z = ground_z
        stance = True
    else:
        u = (ph - DUTY) / (1.0 - DUTY)
        ease = u * u * (3 - 2 * u)  # smootherstep: eased arc, non-linear (principle of arcs)
        # Swing protracts the foot back toward the head: -stride (toe-off) -> +stride (next
        # touchdown).
        fwd_off = stride * (-1.0 + 2.0 * ease)
        z = ground_z + lift * math.sin(math.pi * u)
        stance = False
    pos = rest + FORWARD * fwd_off
    pos.z = z
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

    # Swing-phase envelope: computed ONCE, up front, since it now feeds BOTH the ground-height IK
    # correction below and the foot/toe curl further down (round 12/13's original single use).
    ph = (t + phase_offset[side]) % 1.0
    u_swing = 0.0 if stance else (ph - DUTY) / (1.0 - DUTY)
    curl_s = 0.0 if stance else math.sin(math.pi * u_swing)  # 0 at both swing boundaries, peak mid
    foot_curl_angle = math.radians(FOOT_CURL_MAX_DEG) * curl_s

    # Round 14 (second fix -- see the per-leg precompute's `contact_offset_rest` comment for the
    # full root cause): with the foot held at exact bind rotation (this round's main fix, below),
    # the foot+toe assembly's ground-contact HEIGHT is fully predictable from ankle_w alone --
    # ankle_w.z + a fixed (curl-rotated) rest offset to a representative contact point -- so the IK
    # TARGET's Z is adjusted, via a few fixed-point iterations of the EXACT closed-form 2-bone solve
    # (not a post-hoc position hack), until that predicted contact height exactly matches the
    # intended `ground_target.z` (0 during stance, the lift arc during swing). Iterating the real
    # solve (instead of shifting knee_w/ankle_w AFTER the fact) keeps the shin's true rest length
    # exactly honoured -- an earlier version of this fix shifted knee_w and ankle_w together
    # post-hoc, which was simpler but collapsed FL/FR's knee angle toward its floor (confirmed via
    # verify.py: min angle dropped to 6-8 deg, close to the 15 deg gate threshold) by perturbing the
    # knee's true hip-relative solve. A handful of iterations converges quickly since the
    # relationship is smooth for the small corrections this mesh needs (a fixed point, not a
    # literal Newton solve, but well-behaved here).
    if FK_ANCHORED:
        # Round-4: the foot is held at its rest orientation (plus swing curl), so its vector is
        # known exactly -- solve thigh+shin to the ANKLE = foot-joint target minus that vector.
        # (The inherited solve below folds the foot into L2 and puts the ankle on the knee->ground
        # line, then only corrects Z; with a long foot -- Kirin's hind cannon is 0.46 tall -- that
        # left the stance sweep visibly non-linear: foot-slide cv 0.39.)
        _foot_rot = (L["foot_rest_rot4"].to_3x3()
                     @ mathutils.Matrix.Rotation(foot_curl_angle, 3, L["foot_local_curl_axis"]))
        _ankle_target = ground_target - _foot_rot @ mathutils.Vector((0, L["foot_len"], 0))
        knee_w, ankle_w = solve_2bone_ik(L["hip"], L["L1"], L["shin_len"], _ankle_target, bend_dir)
        return _place_leg_bones(side, L, knee_w, ankle_w, stance, curl_s, foot_curl_angle,
                                parent_world, parent_rest)
    contact_offset_now = mathutils.Matrix.Rotation(foot_curl_angle, 3, "X") @ L["contact_offset_rest"]
    knee_w, reached = solve_2bone_ik(L["hip"], L["L1"], L["L2"], ground_target, bend_dir)
    ankle_w = knee_w.lerp(reached, L["_ankle_frac"])
    predicted_contact_z = ankle_w.z + contact_offset_now.z
    desired_correction = ground_target.z - predicted_contact_z
    # Safety clamp, same principle/margin (1.15x) as this leg's own lift-vs-min_reach cap above:
    # pulling the ankle UP to compensate for a rigid foot's own reach deficit shrinks hip-to-ankle
    # distance toward min_reach (=|L1-L2|) exactly like a large swing-phase lift does, and an
    # UNCLAMPED correction was confirmed (empirically, via verify.py's knee-angle gate) to collapse
    # FL/FR's knee to near-zero degrees for this mesh's large foreleg CROUCH. Clamping the applied
    # correction so hip-to-ankle distance never drops below that same safe floor keeps the knee
    # angle gate honest; any residual ground-height error this leaves is reported honestly (not
    # silently hidden) by verify.py's toe_fan/ground gates, same as every other imperfect-but-
    # bounded tradeoff already documented in this file.
    safe_floor = max(0.01 * H, L["min_reach"] * 1.15)
    ankle_candidate = ankle_w.copy()
    ankle_candidate.z += desired_correction
    dist = (ankle_candidate - L["hip"]).length
    if dist < safe_floor and desired_correction != 0:
        # Scale back the Z delta (not just clamp the raw distance) so the correction direction is
        # preserved, just reduced to whatever this leg's geometry can safely absorb this frame.
        lo, hi = 0.0, 1.0
        for _ in range(12):  # binary search the safe fraction -- cheap, exact enough
            mid = (lo + hi) / 2
            cand = ankle_w.copy()
            cand.z += desired_correction * mid
            if (cand - L["hip"]).length >= safe_floor:
                lo = mid
            else:
                hi = mid
        desired_correction *= lo
    ankle_w.z += desired_correction

    # Round 12 (producer review: "the feet look wrong"). Original root cause: the OLD code aimed
    # the foot/toe bones at the raw IK target every frame, unstable during swing (see the git
    # history for the full writeup) -- fixed by decoupling orientation from the raw target,
    # holding a fixed stance direction. That part of the fix stands.
    #
    # Round 14 (producer review: round 13's toe fan still showed "jagged folded geometry at the
    # foot/toe base" / "mangled, overlapping toes" in Move frames, even though round 13's own
    # toe_fan gate passed). ROOT CAUSE (confirmed via a numeric diagnostic comparing posed-vs-rest
    # matrices directly, not guessed): rounds 12-13 reconstructed the foot/toe bones' ORIENTATION
    # via `aim_matrix` + a hand-picked world-space `up_hint` -- a DIFFERENT up_hint for the foot
    # (world X) than for the toes (world -Z). That construction has NO reason to reproduce either
    # bone's actual REST roll (Blender's own default roll-0 convention, set when the edit bone was
    # created, entirely unrelated to any up_hint choice): measured directly, even during STANCE
    # with the foot bone's Y-axis exactly matching its bind direction, its full pose-vs-rest matrix
    # differed by 90 degrees of rotation (a 44-degree matrix_basis rotation around a non-anatomical
    # compound axis (0.69, 0.19, -0.70) -- not a clean single-axis "wrist bends forward," just a
    # roll-convention artefact), and the toe's rotation RELATIVE TO THE FOOT (what the blended mesh
    # at the foot/toe junction actually feels) drifted 160+ degrees from its bind-relative
    # transform. Two independently-mismatched roll conventions meeting at one blended vertex region
    # is exactly what shears/folds triangles there, even though each bone's OWN shape (toe-to-toe
    # angle, ground height) still measured fine -- which is why round 13's toe_fan gate (angle +
    # ground clearance only, never mesh deformation) passed despite the visible mesh damage.
    #
    # FIX: stop reconstructing orientation from a world-space up_hint for the foot and toe bones.
    # FOOT: keeps round 12's real goal -- world orientation stays AT BIND during stance, "with the
    # shin/wrist taking up the difference" (i.e. the shin/thigh above it absorb whatever rotation
    # is needed to still deliver the ankle to its IK-solved position while the foot itself holds
    # still) -- now implemented by literally copying the bind REST rotation (foot_rest_rot4,
    # precomputed once) instead of re-deriving a roll via aim_matrix, so there is no possible
    # mismatch from the bind roll at zero curl: `foot_world = Translation(ankle_w) @
    # foot_rest_rot4 @ Rotation(foot_curl_angle, local_axis)`. TOES: driven with a DIRECT LOCAL
    # matrix_basis (identity at stance -- exactly bind-relative to the foot, zero rotation -- a
    # small local-axis rotation during swing), so they rigidly inherit whatever the foot's ACTUAL
    # pose is via normal FK composition. By construction this has ZERO relative-to-foot mismatch
    # regardless of the foot's own world orientation, which is what actually matters for the
    # blended mesh at the junction -- a toe anatomically just needs to follow its own forefoot
    # rigidly, not independently chase a world direction. Blender's own FK composition places the
    # toe correctly as a rigid extension of the (now correctly bind-preserving) foot.
    return _place_leg_bones(side, L, knee_w, ankle_w, stance, curl_s, foot_curl_angle,
                            parent_world, parent_rest)


def _place_leg_bones(side, L, knee_w, ankle_w, stance, curl_s, foot_curl_angle, parent_world,
                     parent_rest):
    """Shared by both set_leg_pose paths: places thigh/shin/foot/toe(s) from the solved knee and
    ankle (unchanged code, moved here verbatim from set_leg_pose so the fk_anchored path reuses
    it)."""
    thigh_pb = arm_obj.pose.bones[f"leg_{side}_thigh"]
    shin_pb = arm_obj.pose.bones[f"leg_{side}_shin"]
    foot_pb = arm_obj.pose.bones[f"leg_{side}_foot"]
    thigh_rest = thigh_pb.bone.matrix_local
    shin_rest = shin_pb.bone.matrix_local
    foot_rest_mat = foot_pb.bone.matrix_local

    # up_hint for thigh/shin only now (picked PER LEG, as the world axis least aligned with this
    # leg's own detected bend direction -- see the longer-standing comment history for why this
    # avoids aim_matrix's ill-conditioned near-parallel cross-product regime for those two bones;
    # this reasoning still applies to thigh/shin, which are driven by genuine IK targets, unlike
    # the foot/toe bones above which no longer use aim_matrix at all).
    bd = L["bend_dir"]
    axis_candidates = [mathutils.Vector((1, 0, 0)), mathutils.Vector((0, 1, 0)), mathutils.Vector((0, 0, 1))]
    up_hint = min(axis_candidates, key=lambda ax: abs(ax.dot(bd)))
    if FK_ANCHORED:
        thigh_world = set_bone_world_matrix_direct(
            thigh_pb, aim_min_twist(thigh_pb, L["hip"], knee_w, parent_world, parent_rest),
            parent_world, parent_rest)
        shin_world = set_bone_world_matrix_direct(
            shin_pb, aim_min_twist(shin_pb, knee_w, ankle_w, thigh_world, thigh_rest),
            thigh_world, thigh_rest)
    else:
        thigh_world = set_bone_world_matrix_direct(
            thigh_pb, aim_matrix(L["hip"], knee_w, up_hint), parent_world, parent_rest)
        shin_world = set_bone_world_matrix_direct(
            shin_pb, aim_matrix(knee_w, ankle_w, up_hint), thigh_world, thigh_rest)

    foot_desired_world = (mathutils.Matrix.Translation(ankle_w) @ L["foot_rest_rot4"]
                           @ mathutils.Matrix.Rotation(foot_curl_angle, 4, L["foot_local_curl_axis"]))
    foot_world = set_bone_world_matrix_direct(foot_pb, foot_desired_world, shin_world, shin_rest)

    if L["has_toe_fan"]:
        # Round 13's toe fan, round 14's local-matrix_basis fix: three SIBLING bones (toe_in/mid/
        # out, not chained to each other), each parented directly to leg_<side>_foot. During
        # STANCE, matrix_basis is IDENTITY -- i.e. the fan sits EXACTLY at its bind-relative-to-foot
        # pose, "stays flat and fanned exactly as at bind, no curl," literally (not approximately).
        # During SWING, each toe gets its OWN small local-axis curl (hinge_axis_local, precomputed
        # per toe from normalize(cross(toe_direction, world_up)) transformed into that toe's own
        # rest-local frame -- see the per-leg precompute) on top of that identity baseline, so the
        # fan closes slightly and reopens without ever rotating about a SHARED axis (which is what
        # swung the splayed toes across each other in round 13's bug) and without ever drifting
        # from the foot's own actual roll (which is what caused round 14's mesh shearing).
        fan = L["toe_fan"]
        for t_name in ("in", "mid", "out"):
            toe_data = fan["toes"][t_name]
            toe_curl_angle = math.radians(TOE_FAN_CURL_MAX_DEG) * curl_s
            toe_pb = arm_obj.pose.bones[f"leg_{side}_toe_{t_name}"]
            toe_pb.matrix_basis = mathutils.Matrix.Rotation(
                toe_curl_angle, 4, toe_data["hinge_axis_local"])
    else:
        toe_pb = arm_obj.pose.bones[f"leg_{side}_toe"]
        toe_curl_angle = 0.0 if stance else math.radians(TOE_CURL_MAX_DEG) * curl_s
        toe_pb.matrix_basis = mathutils.Matrix.Rotation(toe_curl_angle, 4, L["toe_local_curl_axis"])
    return stance


def set_scapula_pose(side, hip_dynamic, parent_world, parent_rest):
    """Lead-review round 5: points the scapula bone from its rest head (near the chest) to
    `hip_dynamic` -- this frame's fore-aft-swung shoulder-socket position (crouch Z-offset +
    swing Y-offset, computed by the caller) -- and returns its own world matrix, for use as the
    PARENT when this frame's leg_<side>_thigh is placed (set_leg_pose). The scapula's rest
    head/tail/length were captured once in `scapula_rest` during the per-leg precompute above."""
    head, tail, rest_mat, _length = scapula_rest[side]
    pb = arm_obj.pose.bones[f"scapula_{side}"]
    if FK_ANCHORED:
        # Round-4 fix 2: the scapula head rides spine_02's ACTUAL pose (not its rest position).
        fk_head = (parent_world @ parent_rest.inverted() @ pb.bone.matrix_local).translation
        return set_bone_world_matrix_direct(
            pb, aim_min_twist(pb, fk_head, hip_dynamic, parent_world, parent_rest),
            parent_world, parent_rest)
    bd = legs[side]["bend_dir"]
    axis_candidates = [mathutils.Vector((1, 0, 0)), mathutils.Vector((0, 1, 0)), mathutils.Vector((0, 0, 1))]
    up_hint = min(axis_candidates, key=lambda ax: abs(ax.dot(bd)))
    return set_bone_world_matrix_direct(pb, aim_matrix(head, hip_dynamic, up_hint), parent_world, parent_rest)


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
    for suffix in ("thigh", "shin", "foot"):
        arm_obj.pose.bones[f"leg_{side}_{suffix}"].rotation_mode = "QUATERNION"
    # Round 13: toe-fan legs have 3 toe bones (toe_in/mid/out), not 1 ("toe") -- set whichever
    # actually exist on this leg.
    toe_suffixes = ("toe_in", "toe_mid", "toe_out") if legs[side]["has_toe_fan"] else ("toe",)
    for suffix in toe_suffixes:
        arm_obj.pose.bones[f"leg_{side}_{suffix}"].rotation_mode = "QUATERNION"
    if side in scapula_rest:
        arm_obj.pose.bones[f"scapula_{side}"].rotation_mode = "QUATERNION"

if FK_ANCHORED:
    # v18 round 5: rig_creature.py's smoke-test "TestPose" leaves neck/head/legs posed in the saved
    # rigged .blend; any bone this clip never keys (neck_02) kept that pose in Blender (a 14 deg neck
    # turn in every Move preview/contact sheet) while the GLB/runtime shows rest. Clear it first.
    for _pb in arm_obj.pose.bones:
        _pb.matrix_basis = mathutils.Matrix.Identity(4)
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

    # Body bob + CROUCH (round 5): CROUCH is a constant base offset held through the whole cycle
    # (the task brief's "pelvis and chest ~6-10% of H below bind height, held through the cycle
    # with the existing bob on top"); BODY_BOB still oscillates upward from that lowered base,
    # lowest when a foot is mid-stance, highest at the cross-over point between steps. This moves
    # the VISUAL pelvis/chest down (root's children), independent of the separate CROUCH shift
    # already folded directly into each leg's own "hip" IK anchor during the per-leg precompute
    # above -- see that section's comment for why those are two different mechanisms in this
    # file's architecture (the IK solve's hip input was never coupled to root's animated world
    # position to begin with).
    bob = -CROUCH + BODY_BOB * abs(math.sin(2 * a))
    root_world = root_rest @ mathutils.Matrix.Translation((0, 0, bob))
    if root_pb:
        root_pb.location = (0, 0, bob)
        root_pb.keyframe_insert(data_path="location", frame=i + 1)
        if FK_ANCHORED:
            # keyed (identity) so a clip played after a root-rotating KO can't inherit its roll
            root_pb.keyframe_insert(data_path="rotation_euler", frame=i + 1)
    # Pelvis roll/yaw (round 5): a small weight-shift motion in phase with the hind legs' own
    # stride frequency (sin(a), one full cycle per full gait cycle -- not sin(2a) like the 2x-per-
    # cycle body bob). Folded in analytically here for the same reason spine_02's pitch is below:
    # BL/BR are this rotation's children (parented to pelvis), so their own IK solve needs the
    # ACTUAL pelvis_world this frame was computed against, not an identity-basis approximation.
    pelvis_local_roll = math.radians(PELVIS_ROLL_DEG * math.sin(a))
    pelvis_local_yaw = math.radians(PELVIS_YAW_DEG * math.sin(a + 0.5))
    pelvis_world = (root_world @ root_rest.inverted() @ pelvis_rest
                     @ mathutils.Matrix.Rotation(pelvis_local_roll, 4, "Y")
                     @ mathutils.Matrix.Rotation(pelvis_local_yaw, 4, "Z"))
    pelvis_fk_basis = None
    if FK_ANCHORED:
        # Round-4 fix 6: roll about the walk axis, yaw about world up, conjugated into the pelvis
        # bone's own rest frame (its local axes are not body axes -- it runs root -> pelvis landmark).
        _rp = pelvis_rest.to_3x3()
        _rw = (mathutils.Matrix.Rotation(pelvis_local_yaw, 3, "Z")
               @ mathutils.Matrix.Rotation(pelvis_local_roll, 3, FORWARD))
        pelvis_fk_basis = _rp.inverted() @ _rw @ _rp
        pelvis_world = root_world @ root_rest.inverted() @ pelvis_rest @ pelvis_fk_basis.to_4x4()
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
    #
    # ROUND 3 BUG FOUND AND FIXED: the formula above (root_world @ root_rest.inverted() @
    # spine02_rest @ pitch) treats spine_02 as if ROOT were its direct parent. That was a valid
    # shortcut ONLY as long as every bone between root and spine_02 in the real chain (root ->
    # pelvis -> spine_01 -> spine_02) had an identity matrix_basis, which was true before this round
    # (pelvis never rotated) -- bone.matrix_local is armature-space, so skipping identity
    # intermediate bones' own rest matrices is mathematically exact in that case. Round 3 added
    # PELVIS roll/yaw (pelvis_local_roll/pelvis_local_yaw just above), which is a REAL, non-identity
    # rotation now -- so the shortcut silently went stale and spine02_world (hence scapula_world and
    # every FL/FR leg bone hung off it) was computed against the wrong parent pose, off by a lever-
    # arm-scaled multiple of the pelvis rotation (confirmed directly: a same-session probe comparing
    # the old formula's spine02_world.translation, (0.159,-0.390,0.860), against Blender's own
    # evaluated spine_02 pose at the same frame, (0.164,-0.018,0.680), showed a ~0.37-unit Y and
    # ~0.18-unit Z discrepancy -- this is what dragged the FL/FR ground targets down through the
    # floor, not the CROUCH/lift math itself, which three earlier fix attempts at those parameters
    # correctly found had no effect). Fixed by routing through pelvis_world (which DOES already
    # include the round-3 roll/yaw) instead of root_world directly; spine_01 is still skipped since
    # it genuinely has no pose rotation of its own (only pelvis's new rotation needed restoring).
    spine02_local_pitch = math.radians(HEAD_AMP * 2 * math.sin(2 * a))
    spine02_world = (pelvis_world @ pelvis_rest.inverted() @ spine02_rest
                      @ mathutils.Matrix.Rotation(spine02_local_pitch, 4, "X"))

    stances = []
    for side in leg_sides:
        if side in scapula_rest:
            # Scapula swing (round 5): fore-aft shoulder-socket excursion, same phase as this
            # leg's own swing (peaks forward at the start of this leg's stance, matching the
            # foot's own most-forward point, per the per-leg precompute's amplitude comment).
            # A smooth cosine, not foot_target()'s piecewise stance/swing shape -- the scapula is
            # secondary motion, not the thing verify.py's foot-slide gate measures (that gate only
            # cares about the FOOT's own trajectory, which stays exactly as solve_2bone_ik places
            # it regardless of how the hip anchor it's solved from moves -- see solve_2bone_ik's
            # own docstring: IK always places the foot at the given target when reachable).
            swing_phase = (t + phase_offset[side]) % 1.0
            scapula_amp = scapula_rest[side][3] * math.sin(math.radians(SCAPULA_SWING_DEG))
            scapula_fwd = scapula_amp * math.cos(2 * math.pi * swing_phase)
            # FORWARD (not a hard-coded (0, y, 0)) -- see this file's FORWARD derivation: the
            # shoulder socket's fore-aft excursion must swing toward the head/tail exactly like the
            # foot's own stride does, for the same reason.
            hip_dynamic = legs[side]["hip"] + FORWARD * scapula_fwd
            if FK_ANCHORED:
                # Round-4 fix 2: shoulder socket carried by spine_02's actual pose this frame.
                hip_dynamic = (spine02_world @ spine02_rest.inverted() @ legs[side]["hip_rest"]
                               + FORWARD * scapula_fwd)
            scapula_world = set_scapula_pose(side, hip_dynamic, spine02_world, spine02_rest)
            scapula_pb = arm_obj.pose.bones[f"scapula_{side}"]
            scapula_pb.keyframe_insert(data_path="rotation_quaternion", frame=i + 1)
            scapula_pb.keyframe_insert(data_path="location", frame=i + 1)
            # set_leg_pose reads L["hip"] fresh each call -- temporarily override it with this
            # frame's swung position so the thigh's own IK solve (and its rendered position, via
            # aim_matrix(L["hip"], ...) in set_leg_pose) matches where the scapula just placed the
            # shoulder socket, then restore the precomputed (crouch-only) value for next frame's
            # swing calculation above (which always starts from the same crouched base).
            hip_static = legs[side]["hip"]
            legs[side]["hip"] = hip_dynamic
            stances.append(set_leg_pose(side, t, scapula_world, scapula_rest[side][2]))
            legs[side]["hip"] = hip_static
        elif FK_ANCHORED:
            # Round-4 fix 2: hip socket carried by the pelvis's actual pose this frame.
            hip_static = legs[side]["hip"]
            legs[side]["hip"] = pelvis_world @ pelvis_rest.inverted() @ legs[side]["hip_rest"]
            stances.append(set_leg_pose(side, t, pelvis_world, pelvis_rest))
            legs[side]["hip"] = hip_static
        else:
            stances.append(set_leg_pose(side, t, pelvis_world, pelvis_rest))

    # Tail: counter-sways opposite the leg phase for a touch of weight-shift readability.
    if FK_ANCHORED:
        # Round-4 fix 5: on the quadruped template's Z-up roll, local Z is yaw -- sway sideways at
        # constant height (a small optional pitch), each bone lagging the one before it.
        for _ti, _tb in enumerate(sorted(n for n in all_bones if n.startswith("tail_"))):
            set_rot_local(_tb, deg_x=GP["tail_pitch"] * math.sin(2 * a + 0.4 * _ti),
                          deg_z=GP["tail_yaw"] * (1.0 + 0.25 * _ti) * math.sin(a + 0.35 * (_ti + 1)))
    else:
        set_rot_local("tail_01", deg_x=TAIL_AMP * 6 * math.sin(a), deg_z=TAIL_AMP * 4 * math.sin(a + 0.3))
        set_rot_local("tail_02", deg_x=TAIL_AMP * 8 * math.sin(a + 0.4), deg_z=TAIL_AMP * 5 * math.sin(a + 0.6))
        set_rot_local("tail_03", deg_x=TAIL_AMP * 6 * math.sin(a + 0.8))
        set_rot_local("tail_04", deg_x=TAIL_AMP * 4 * math.sin(a + 1.1))
        set_rot_local("tail_05", deg_x=TAIL_AMP * 3 * math.sin(a + 1.4))  # Basilisk's 5-bone tail

    if GP.get("bird"):
        # v19 Phoenix: wing balance / tail sway / crest flicker as semantic data layers (see
        # GAIT_PARAMS["phoenix"]); bones the leg IK above depends on (pelvis, spine_02) are not
        # touched here, so the solve stays consistent.
        _bc = BP.add_channels({("b", b, ch): v for _, b, ch, v in GP["bird_hold"]},
                              BP.sines(GP["bird_layers"], t))
        _per = {}
        for _k, _v in _bc.items():
            _per.setdefault(_k[1], {})[_k[2]] = _v
        for _bn, _ch in _per.items():
            BP.set_semantic(arm_obj, FORWARD, _bn, _ch)
    else:
        # Wings: folded, slight settle motion (not flapping -- this is a ground-locomotion clip).
        set_rot_local("wing_L_01", deg_z=-6 - 2 * math.sin(a * 2), deg_x=2 * math.sin(a))
        set_rot_local("wing_L_02", deg_z=-4 * math.sin(a * 2 + 0.5))
        set_rot_local("wing_R_01", deg_z=6 + 2 * math.sin(a * 2), deg_x=2 * math.sin(a))
        set_rot_local("wing_R_02", deg_z=4 * math.sin(a * 2 + 0.5))

    # Head/neck/spine: a light counter-rotation to the body bob reads as weight/balance. spine_02's
    # rotation here MUST match spine02_local_pitch above exactly (same formula) -- it's the pose
    # this frame's leg placement was actually computed against. Same for pelvis's roll/yaw below
    # (must match pelvis_local_roll/pelvis_local_yaw exactly -- that's the pose BL/BR's own IK
    # solve was computed against this frame).
    if pelvis_fk_basis is not None:
        pelvis_pb.rotation_mode = "XYZ"
        pelvis_pb.rotation_euler = pelvis_fk_basis.to_euler("XYZ")
    else:
        set_rot_local("pelvis", deg_x=0, deg_y=PELVIS_ROLL_DEG * math.sin(a), deg_z=PELVIS_YAW_DEG * math.sin(a + 0.5))
    set_rot_local("spine_02", deg_x=HEAD_AMP * 2 * math.sin(2 * a))
    set_rot_local("neck_01", deg_x=HEAD_AMP * -2 * math.sin(2 * a))
    set_rot_local("head", deg_x=HEAD_AMP * -1.5 * math.sin(2 * a + 0.2))

    for side in leg_sides:
        toe_suffixes = ("toe_in", "toe_mid", "toe_out") if legs[side]["has_toe_fan"] else ("toe",)
        for suffix in ("thigh", "shin", "foot") + toe_suffixes:
            pb = arm_obj.pose.bones[f"leg_{side}_{suffix}"]
            pb.keyframe_insert(data_path="rotation_quaternion", frame=i + 1)
            pb.keyframe_insert(data_path="location", frame=i + 1)
    _keyed_names = ("tail_01", "tail_02", "tail_03", "tail_04", "tail_05", "wing_L_01", "wing_L_02",
                    "wing_R_01", "wing_R_02", "spine_02", "neck_01", "head", "pelvis")
    if GP.get("bird"):
        _keyed_names = _keyed_names + ("wing_L_03", "wing_R_03", "neck_02", "crest_01", "crest_02")
        # v20: every bone the semantic layers drive (the Treant's arms/crown) is keyed too
        _keyed_names = _keyed_names + tuple(sorted({L[1] for L in GP["bird_layers"]} - set(_keyed_names)))
        # v20 round 2: ... and every bone a constant hold drives (the Treant's held arm pose)
        _keyed_names = _keyed_names + tuple(sorted({L[1] for L in GP["bird_hold"]} - set(_keyed_names)))
    for name in _keyed_names:
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

blend_out = os.path.join(OUT, f"{CREATURE}_move.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_out)
print(f"SAVED {blend_out}")
print("GAIT DONE")
