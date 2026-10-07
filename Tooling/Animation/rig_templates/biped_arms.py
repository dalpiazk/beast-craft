"""The "biped with arms" deform-rig template (v20): an upright walker with two legs, two
shoulder/elbow/wrist arms and an optional soft crown fan off the head -- the Treant.

Built the same data-driven way as winged_biped.py (hand-placed landmarks from
rig_templates/landmarks/<beast>.json, native prepped-mesh coords, snapped inside the mesh) with the
same EXPLICIT roll convention on every bone: local X = the body's lateral axis LAT = FORWARD x UP
(projected perpendicular to the bone), local Z = X x Y. So raw `deg_x` is a pitch about LAT on
every bone (+ = "nose up": a forward bone's tip rises, an upright bone's tip goes BACK, a hanging
bone's tip swings FORWARD), and a flat toe's local Z is UP (the stance sole normal gait.py's foot
hold and verify.py's foot_orientation gate read). A bone that runs ALONG LAT (the Treant's
out-stretched left arm) has no perpendicular LAT component; it falls back to local Z = UP. Clips
are authored through anim/bird_pose.py's semantic channels (pitch/turn/bank about LAT/UP/FORWARD,
converted through each bone's rest matrix), so they never depend on these choices; the axis dump
(bird_pose.verify_axes) is the record that they mean what they say.

Deform bones (Treant: 26):
  root (ground, under the pelvis), pelvis, spine_01, spine_02, neck_01, neck_02, head,
  crown_01..NN       (one per "crown" tip, base -> tip, parented to head: soft leaf-mass sway),
  arm_<L|R>_clav     (chest -> shoulder), arm_<L|R>_upper (shoulder -> elbow),
  arm_<L|R>_fore     (elbow -> wrist), arm_<L|R>_hand (wrist -> hand tip),
  leg_<BL|BR>_thigh/shin/foot/toe.
No jaw (the face is painted on the trunk).
"""
import json
import os

import bpy
import mathutils

from winged_quadruped import (
    _native_to_normalized_fn,  # noqa: F401 -- re-exported for rig_creature.py
    _snap_if_outside,
    build_leg_masks,            # noqa: F401 -- re-exported
    fix_hip_weight_gradient,    # noqa: F401 -- re-exported
)
from quadruped import fix_root_leg_bleed  # noqa: F401 -- re-exported
from quadruped import fix_sole_weights    # noqa: F401 -- re-exported (rigid root-feet soles)
from quadruped import force_rigid_to_bone, rigid_regions_normalized, smooth_weights_region  # noqa: F401 -- v22
from winged_biped import fill_unweighted, dump_axes, _seg_dist  # noqa: F401 -- re-exported

LANDMARKS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "landmarks")


def detect_landmarks_handplaced(obj, H, to_normalized, beast_name):
    from mathutils.bvhtree import BVHTree
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(obj, depsgraph)
    with open(os.path.join(LANDMARKS_DIR, f"{beast_name}.json")) as f:
        data = json.load(f)
    snap_log = []

    def pt(native_xyz, snap=True):
        p = to_normalized(native_xyz)
        if not snap:
            return p
        snapped, moved, exceeded = _snap_if_outside(obj, bvh, p, H)
        if moved > 1e-6:
            snap_log.append((tuple(round(c, 4) for c in p), round(moved, 4), exceeded))
        return snapped

    spine = data["spine"]
    forward_sign = -1.0 if spine["head"][1] < spine["pelvis"][1] else 1.0
    fwd = mathutils.Vector((0.0, forward_sign, 0.0))
    legs = []
    for leg_spec in data["legs"]:
        hip, knee, ankle, foot = [pt(p) for p in leg_spec["chain"]]
        toe_tip = pt(leg_spec["toe_tip"])
        legs.append({"side": leg_spec["side"], "is_front": False, "foot": foot, "ankle": ankle,
                     "knee": knee, "hip": hip, "toe_tip": toe_tip, "toe_fan": None,
                     # a walking biped's knee bows FORWARD (the lead's knees sit on the hip-ankle
                     # line in Y and a little out in X -- that lateral bow is the wide stance, not a
                     # bend direction)
                     "bend_dir": fwd.copy()})
    arms = []
    for a in data.get("arms", []):
        sh, el, wr = [pt(p) for p in a["chain"]]
        arms.append({"side": a["side"], "shoulder": sh, "elbow": el, "wrist": wr,
                     "hand_tip": pt(a["hand_tip"])})
    crown = None
    if "crown" in data:
        crown = {"base": pt(data["crown"]["base"]), "tips": [pt(t) for t in data["crown"]["tips"]]}
    if snap_log:
        print(f"HAND LANDMARKS ({beast_name}): {len(snap_log)} point(s) snapped inside: {snap_log}")
    head = pt(spine["head"])
    hum = {}
    if data.get("humanoid"):  # v20 round 2: 3-segment spine + single neck
        hum = {"humanoid": True, "spine_02": pt(spine["spine_02"]), "neck_top": pt(spine["neck_top"])}
    return {**hum,
        "H": H, "forward_sign": forward_sign, "legs": legs, "arms": arms, "crown": crown,
        "pelvis": pt(spine["pelvis"]), "chest": pt(spine["chest"]), "spine_01": pt(spine["spine_01"]),
        "neck_base": pt(spine["neck_base" if "neck_base" in spine else "neck_top"]),
        "neck_mid": pt(spine["neck_mid" if "neck_mid" in spine else "neck_top"]),
        "head_base": pt(spine["neck_mid" if "neck_mid" in spine else "neck_top"]), "head": head,
        "head_top": pt(spine["head_top"], snap=False), "head_tip": pt(spine["snout_tip"]),
        "tail_tip": head, "tail_points": [], "notes": data.get("notes", ""),
        "locomotion": data.get("locomotion", "walk"),
        # v22 opt-ins (absent on the Treant, so its rig is unchanged): rig_creature.py's generic
        # rigid_regions / rigid_parts / weight_smooth_regions passes.
        "rigid_regions": rigid_regions_normalized(data, to_normalized),
        "rigid_parts": data.get("rigid_parts") or [],
        "arm_ramp": data.get("arm_ramp"),
        "joint_limit_overrides": data.get("joint_limit_overrides"),
        "arm_roll_closing": data.get("arm_roll_closing", False),
        "arm_hinge_from_upper": data.get("arm_hinge_from_upper", False),
        "extra_bones": [dict(x, head=pt(x["head"], snap=False), tail=pt(x["tail"], snap=False))
                        for x in data.get("extra_bones") or []],
        "bow_rig": (dict(data["bow_rig"], arrow_rest=list(pt(data["bow_rig"]["arrow_rest"], snap=False)))
                    if data.get("bow_rig") else None),
        "prop_clear": data.get("prop_clear"),
        "strip_heat_bones": data.get("strip_heat_bones") or [],
        "keep_shell_heat_weights": data.get("keep_shell_heat_weights", False),
        "heat_unweighted_tolerance": data.get("heat_unweighted_tolerance", 0.05),
        "weight_smooth_regions": rigid_regions_normalized(
            {"rigid_regions": [dict(r, bone="") for r in data.get("weight_smooth_regions") or []]},
            to_normalized, keep=("iterations",)),
    }


def _forward(lm):
    return mathutils.Vector((0.0, lm["forward_sign"], 0.0))


# v20 round 2 (humanoid Treant): human joint ranges, in anim/bird_pose.py semantic channels, clamped
# on every posed frame by bird_pose.set_semantic (Move and every keyed clip). Hinges (elbow, knee)
# allow ONLY their flexion channel: a hinge cannot abduct, turn or bank. "flex" is a rotation about
# the bone's own local X, which build_bones sets to the joint's hinge axis with + = flexion (arm
# bones: forearm/hand toward FORWARD; the legs keep local X = LAT, so knee flexion is -pitch and is
# driven by the planted-foot IK -- verify.py's joint_limits gate measures knees/elbows on the
# deformed result, not the channels). "abd" = about FORWARD, + = away from the body on either side.
HUMAN_LIMITS = {
    "arm_{s}_clav": {"flex": (-12, 12), "abd": (-8, 20), "pitch": (0, 0), "turn": (0, 0), "bank": (0, 0)},
    "arm_{s}_upper": {"flex": (-45, 125), "abd": (-35, 110), "twist": (-45, 45),
                      "pitch": (0, 0), "turn": (0, 0), "bank": (0, 0)},
    "arm_{s}_fore": {"flex": (0, 140), "twist": (-60, 60), "abd": (0, 0),
                     "pitch": (0, 0), "turn": (0, 0), "bank": (0, 0)},
    "arm_{s}_hand": {"flex": (-60, 70), "abd": (-25, 25), "twist": (0, 0),
                     "pitch": (0, 0), "turn": (0, 0), "bank": (0, 0)},
    "spine_01": {"pitch": (-30, 20), "turn": (-20, 20), "bank": (-15, 15)},
    "spine_02": {"pitch": (-30, 20), "turn": (-20, 20), "bank": (-15, 15)},
    "spine_03": {"pitch": (-25, 20), "turn": (-20, 20), "bank": (-15, 15)},
    "neck": {"pitch": (-40, 40), "turn": (-50, 50), "bank": (-30, 30)},
    "head": {"pitch": (-40, 40), "turn": (-50, 50), "bank": (-30, 30)},
}
# hinge spec for verify.py's joint_limits gate: (parent bone, child bone, flexion sign about the
# parent's posed local X, allowed flexion range deg relative to straight, max off-hinge deg)
HINGES = {"elbow_{s}": ("arm_{s}_upper", "arm_{s}_fore", 1.0, (-5.0, 150.0), 12.0),
          "knee_{s}": ("leg_{s}_thigh", "leg_{s}_shin", -1.0, (-5.0, 150.0), 15.0)}


def armature_props(lm):
    props = {"forward": list(_forward(lm)), "locomotion": lm["locomotion"], "hover_offset": 0.0,
             "outline_mask_zero": [], "template": "biped_arms"}
    if lm.get("humanoid"):
        lim = {}
        for k, v in HUMAN_LIMITS.items():
            for s in ([a["side"] for a in lm["arms"]] if "{s}" in k else [None]):
                lim[k.format(s=s) if s else k] = {c: list(r) for c, r in v.items()}
        # v22 opt-in "joint_limit_overrides" ({bone: {channel: [lo, hi]}}): the channels are relative
        # to the REST pose, and a rig whose rest elbow is already bent ~90 deg (the Shaman's staff
        # arm, the Archer's bow arms) needs negative elbow flex (extension) to straighten it --
        # still inside the anatomical range verify.py's joint_limits gate measures on the mesh.
        for k, v in (lm.get("joint_limit_overrides") or {}).items():
            lim.setdefault(k, {}).update({c: list(r) for c, r in v.items()})
        hin = {}
        for k, (pa, ch, sg, rng, off) in HINGES.items():
            sides = [a["side"] for a in lm["arms"]] if k.startswith("elbow") else [lg["side"] for lg in lm["legs"]]
            for s in sides:
                hin[k.format(s=s)] = [pa.format(s=s), ch.format(s=s), sg, list(rng), off]
        props.update(humanoid=True, joint_limits=json.dumps(lim), hinges=json.dumps(hin))
    if lm.get("prop_clear"):  # v22 round 2: anim/prop_clear.py's correction direction
        props["prop_clear"] = json.dumps(lm["prop_clear"])
    if lm.get("bow_rig"):  # v22 round 3 (Archer): anim/bow_rig.py's keyed bow / string / arrow bones
        br = lm["bow_rig"]
        props.update(bow_rig=json.dumps(br), loc_keyed=[br["bow"], br["string"], br["arrow"]])
    return props


def build_bones(eb, lm, H):
    FWD = _forward(lm)
    UP = mathutils.Vector((0, 0, 1))
    LAT = FWD.cross(UP).normalized()
    roles, names = {}, []

    def mk(name, head, tail, parent=None, role=None):
        b = eb.new(name)
        b.head = head
        b.tail = tail
        if parent:
            b.parent = eb[parent]
            b.use_connect = False
        names.append(name)
        if role:
            roles[name] = role
        return b

    pelvis_p = lm["pelvis"]
    root = mk("root", mathutils.Vector((pelvis_p.x, pelvis_p.y, 0.0)),
              mathutils.Vector((pelvis_p.x, pelvis_p.y, 0.12 * H)), role="root")
    mk("pelvis", root.tail.copy(), pelvis_p, "root", role="pelvis")
    mk("spine_01", pelvis_p, lm["spine_01"], "pelvis", role="spine")
    if lm.get("humanoid"):
        # v20 round 2: pelvis -> spine_01 -> spine_02 -> spine_03 (chest) -> neck -> head
        mk("spine_02", lm["spine_01"], lm["spine_02"], "spine_01", role="spine")
        mk("spine_03", lm["spine_02"], lm["chest"], "spine_02", role="spine")
        mk("neck", lm["chest"], lm["neck_top"], "spine_03", role="neck")
        mk("head", lm["neck_top"], lm["head"], "neck", role="head")
        chest_bone = "spine_03"
    else:
        mk("spine_02", lm["spine_01"], lm["chest"], "spine_01", role="spine")
        mk("neck_01", lm["chest"], lm["neck_base"], "spine_02", role="neck")
        mk("neck_02", lm["neck_base"], lm["neck_mid"], "neck_01", role="neck")
        mk("head", lm["neck_mid"], lm["head"], "neck_02", role="head")
        chest_bone = "spine_02"
    if lm.get("crown"):
        for i, tip in enumerate(lm["crown"]["tips"], start=1):
            mk(f"crown_{i:02d}", lm["crown"]["base"], tip, "head", role="crown")
    for a in lm["arms"]:
        s = a["side"]
        mk(f"arm_{s}_clav", lm["chest"], a["shoulder"], chest_bone, role=f"arm_{s}")
        mk(f"arm_{s}_upper", a["shoulder"], a["elbow"], f"arm_{s}_clav", role=f"arm_{s}")
        mk(f"arm_{s}_fore", a["elbow"], a["wrist"], f"arm_{s}_upper", role=f"arm_{s}")
        mk(f"arm_{s}_hand", a["wrist"], a["hand_tip"], f"arm_{s}_fore", role=f"arm_{s}")
    for xb in lm.get("extra_bones") or []:
        # v22 opt-in (Archer): a prop bone, e.g. the bow string's nock, child of a hand
        mk(xb["name"], xb["head"], xb["tail"], xb["parent"], role=xb.get("role", "prop"))
    for leg in lm["legs"]:
        s = leg["side"]
        mk(f"leg_{s}_thigh", leg["hip"], leg["knee"], "pelvis", role=f"leg_{s}")
        mk(f"leg_{s}_shin", leg["knee"], leg["ankle"], f"leg_{s}_thigh", role=f"leg_{s}")
        mk(f"leg_{s}_foot", leg["ankle"], leg["foot"], f"leg_{s}_shin", role=f"leg_{s}")
        mk(f"leg_{s}_toe", leg["foot"], leg["toe_tip"], f"leg_{s}_foot", role=f"leg_{s}")
    # EXPLICIT roll on every bone: local X = LAT (projected), local Z = X x Y; a bone along LAT
    # (the out-stretched arm) falls back to local Z = UP.
    for n in names:
        b = eb[n]
        d = (b.tail - b.head).normalized()
        if lm.get("humanoid") and n.startswith("arm_"):
            # humanoid arms: local Z = FORWARD (projected) so local X is the elbow/wrist/shoulder
            # flexion hinge and +X rotation swings the bone's tip FORWARD (= flexion).
            f_p = FWD - FWD.dot(d) * d
            if lm.get("arm_hinge_from_upper") and n.endswith(("_fore", "_hand")):
                # v22 opt-in (Archer: straight template arms, where the elbow-closing direction is
                # undefined): the forearm and hand share the upper arm's hinge -- local X = the
                # upper arm's local X (projected perpendicular to this bone), so an elbow "flex" is
                # a pure hinge bend and verify.py's joint_limits gate reads no off-hinge twist.
                xu = eb[f"arm_{n.split('_')[1]}_upper"].x_axis
                x_p = xu - xu.dot(d) * d
                if x_p.length > 1e-6:
                    b.align_roll(x_p.normalized().cross(d))
                    continue
            if lm.get("arm_roll_closing") and n.endswith(("_fore", "_hand")) and f_p.length < 0.5:
                # v22 opt-in (Shaman, Archer): a forearm held FORWARD (staff, bow) runs along
                # FORWARD, so the projection above is degenerate (the Shaman's elbow hinge came out
                # vertical: "flexion" swung the fist sideways). local Z = the direction that CLOSES
                # the elbow (shoulder - elbow, perpendicular to the forearm), so +flex bends the
                # forearm toward the upper arm about the true hinge; the hand shares its forearm's
                # plane (its wrist flexes the same way).
                s_ = n.split("_")[1]
                arm_ = next(x for x in lm["arms"] if x["side"] == s_)
                df = (arm_["wrist"] - arm_["elbow"]).normalized()
                c_ = arm_["shoulder"] - arm_["elbow"]
                c_ = c_ - c_.dot(df) * df
                c_ = c_ - c_.dot(d) * d
                if c_.length > 1e-6:
                    b.align_roll(c_.normalized())
                    continue
            b.align_roll(f_p.normalized())
            continue
        lat_p = LAT - LAT.dot(d) * d
        if lat_p.length < 0.25:
            up_p = UP - UP.dot(d) * d
            b.align_roll(up_p.normalized())
        else:
            b.align_roll(lat_p.normalized().cross(d))
    return names, roles


def fix_arm_body_bleed(obj, bone_roles, lm, ratio=0.8):
    """v20 (Treant): heat weighting blends the arms into whatever they touch -- the right hand rests
    on the chest and the shoulder leaf clumps sit on the upper arms. A vertex carrying both ARM and
    LEG/CROWN weight is given to whichever chain it is geometrically nearer (arm vs leg/crown
    polylines, `ratio` margin in the arm's favour): an arm and a leg or the crown move independently,
    so any blend between them tears. Arm vs torso blends (shoulders, the hand on the chest) are left
    to the smooth heat ramp. Returns the number of vertices changed."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    arm_ids = {gi[n] for n, r in bone_roles.items() if r.startswith("arm_") and n in gi}
    other = {gi[n] for n, r in bone_roles.items() if (r.startswith("leg_") or r == "crown") and n in gi}
    arm_segs = []
    for a in lm["arms"]:
        pts = [lm["chest"], a["shoulder"], a["elbow"], a["wrist"], a["hand_tip"]]
        arm_segs += list(zip(pts, pts[1:]))
    oth_segs = []
    for leg in lm["legs"]:
        pts = [leg["hip"], leg["knee"], leg["ankle"], leg["foot"], leg["toe_tip"]]
        oth_segs += list(zip(pts, pts[1:]))
    if lm.get("crown"):
        oth_segs += [(lm["crown"]["base"], t) for t in lm["crown"]["tips"]]
    changed = 0
    for vi, v in enumerate(obj.data.vertices):
        wa = sum(g.weight for g in v.groups if g.group in arm_ids)
        wo = sum(g.weight for g in v.groups if g.group in other)
        if wa <= 1e-4 or wo <= 1e-4:
            continue
        da = min(_seg_dist(v.co, a, b) for a, b in arm_segs)
        do = min(_seg_dist(v.co, a, b) for a, b in oth_segs)
        drop = other if da < do / ratio else arm_ids
        for g in [g.group for g in v.groups if g.group in drop]:
            obj.vertex_groups[g].remove([vi])
        changed += 1
    return changed


def fix_humanoid_weights(obj, bone_roles, lm):
    """v20 round 2 (humanoid Treant): the rounded back of the leaf crown is a set of separate closed
    leaf shells (Tooling/Animation/meshfix/treant_surgery.py) seated on the old flat crown slab.
    Heat weighting treats each shell on its own (a shell can come out pinned to whatever bone is
    nearest its own centre), so every vertex outside the main body component takes the weights of
    the nearest main-body vertex that is part of the crown/head (dominant group a crown_* or head
    bone): the shells ride exactly with the slab they sit on. Returns the number of vertices set."""
    import mathutils
    from common import connected_components
    if not lm.get("humanoid"):
        return 0
    me = obj.data
    # (1) the relaxed A-pose arms hang a few cm off the trunk, and heat weighting bleeds arm weight
    # onto the flank/hip skin beside them (a hip vertex carried 12 % arm_R_upper: it stretched with
    # every arm raise). A vertex's arm (upper/fore/hand) weight is dropped when it is nearer the
    # trunk than the arm in radius-normalised terms (distance to the arm chain / arm radius vs
    # distance to the spine axis / trunk radius).
    gi = {g.name: g.index for g in obj.vertex_groups}
    # v22 opt-in "arm_ramp" (Shaman: the arms come out from under a fat leaf cloak, and heat weighting
    # gave each clavicle ~1400 cloak vertices): {"include_clav": bool, "radii": [arm, trunk]} --
    # the clavicle joins the ramped set and the arm/trunk radii (x H/2) fit the creature. Absent on the
    # Treant (unchanged).
    _ar = lm.get("arm_ramp") or {}
    limb = {gi[n] for n in gi if n.startswith("arm_") and (_ar.get("include_clav") or not n.endswith("_clav"))}
    spine_pts = [lm["pelvis"], lm["spine_01"], lm["spine_02"], lm["chest"], lm["neck_top"]]
    spine_names = ["spine_01", "spine_02", "spine_03", "neck"]
    arm_segs = []
    for a in lm["arms"]:
        pts = [a["shoulder"], a["elbow"], a["wrist"], a["hand_tip"]]
        arm_segs += list(zip(pts, pts[1:]))
    _ra, _rt = _ar.get("radii", (0.06, 0.17))
    R_ARM, R_TRUNK = _ra * lm["H"] / 2.0, _rt * lm["H"] / 2.0
    stripped = 0
    for vi, v in enumerate(me.vertices):
        if not any(g.group in limb and g.weight > 1e-4 for g in v.groups):
            continue
        da = min(_seg_dist(v.co, a_, b_) for a_, b_ in arm_segs) / R_ARM
        dt = min(_seg_dist(v.co, a_, b_) for a_, b_ in zip(spine_pts, spine_pts[1:])) / R_TRUNK
        q = da / max(dt, 1e-6)
        if q > 1.0:
            # ramp, not a cut: q 1.0 -> 1.8 scales the arm weight 1 -> 0 (a hard strip tore the
            # skin where the stripped region met the arm); the weight taken off goes to the
            # nearest spine bone (so a chest vertex heat-weighted 100 % to the arm is handed back)
            k = max(0.0, (1.8 - q) / 0.8)
            moved = 0.0
            for g in [g for g in v.groups if g.group in limb]:
                moved += g.weight * (1.0 - k)
                if k <= 0.0:
                    obj.vertex_groups[g.group].remove([vi])
                else:
                    obj.vertex_groups[g.group].add([vi], g.weight * k, "REPLACE")
            j = min(range(len(spine_names)), key=lambda i: _seg_dist(v.co, spine_pts[i], spine_pts[i + 1]))
            if spine_names[j] in gi and moved > 0:
                obj.vertex_groups[gi[spine_names[j]]].add([vi], moved, "ADD")
            gs = sorted(v.groups, key=lambda g: -g.weight)
            for g in gs[4:]:  # keep the glTF 4-influence budget (the exporter would drop it anyway)
                obj.vertex_groups[g.group].remove([vi])
            stripped += 1
    print(f"HUMANOID ARM/TRUNK: {stripped} trunk vertices had arm weight ramped down/removed")
    if lm.get("keep_shell_heat_weights"):
        # v22 opt-in (Archer): the separate shells are new ARMS (meshfix/prop_surgery.py copies) whose
        # own heat weights are right; the crown-shell copy below would give them the head's weights
        return 0
    comps = sorted(connected_components(obj), key=len, reverse=True)
    if len(comps) <= 1:
        return 0
    body = comps[0]
    names = [g.name for g in obj.vertex_groups]
    crownish = {n for n, r in bone_roles.items() if r in ("crown", "head")}
    donors = []
    for vi in body:
        gs = me.vertices[vi].groups
        if gs and names[max(gs, key=lambda g: g.weight).group] in crownish:
            donors.append(vi)
    kd = mathutils.kdtree.KDTree(len(donors))
    for vi in donors:
        kd.insert(me.vertices[vi].co, vi)
    kd.balance()
    n = 0
    for comp in comps[1:]:
        for vi in comp:
            _, d, _ = kd.find(me.vertices[vi].co)
            for g in list(me.vertices[vi].groups):
                obj.vertex_groups[g.group].remove([vi])
            for g in me.vertices[d].groups:
                obj.vertex_groups[g.group].add([vi], g.weight, "REPLACE")
            n += 1
    return n
