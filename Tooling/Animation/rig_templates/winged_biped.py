"""The "winged biped" deform-rig template: an upright (or hovering) bird -- Phoenix, Thunderbird.

Built from rig_templates/quadruped.py's data-driven, explicit-roll design (hand-placed landmarks
loaded from rig_templates/landmarks/<beast>.json, every bone's roll set explicitly) plus
winged_quadruped.py's 3-bone-per-side wing chain. Two hind legs only (BL/BR, no scapula, no
forelegs), a variable-length tail, the beak/jaw pair off the head, an optional crest chain, and
rigid-attachment support (quadruped.force_rigid_to_bone is re-exported).

Deform bones (count = 9 + crest + tail + 6 wing + 8 leg, <= ~32):
  root, pelvis, spine_01, spine_02, neck_01, neck_02, head, beak, jaw,
  crest_01..crest_NN   (optional; "crest": {"base","tip"} -> 2 bones split at the midpoint, so the
                        flame can flicker/bend, not just swing rigidly),
  tail_01..tail_NN     (NN = len(tail points) - 1),
  wing_<L|R>_01..03    (root->mid as given, mid->tip split into two even bones -- same as the
                        Griffin, and wing_*_03 is what Live3D's wing-tip spring/outline mask key on),
  leg_<BL|BR>_thigh/shin/foot/toe.

ROLL CONVENTION (explicit for EVERY bone; v19). Blender's auto roll is never relied on -- v18 rounds
2-4 showed the same numeric Euler meaning a different world rotation per rig when roll was left to
chance. With FORWARD = (0, forward_sign, 0) (the head's ground-plane direction), UP = +Z and the
body's lateral axis LAT = FORWARD x UP (world -X for a head=-Y mesh):
  * every NON-wing bone (root, pelvis, spine, neck, head, beak, jaw, crest, tail, thigh, shin,
    foot, toe): local X = LAT (projected perpendicular to the bone), local Z = X x Y. So for EVERY
    one of them `deg_x` is the same world rotation -- a pitch about the body's lateral axis, + =
    "nose up" (turns FORWARD toward UP): a horizontal bone's tip rises, a vertical (upright torso)
    bone's tip goes BACK, a hanging bone's (Thunderbird tail) tip swings FORWARD. A first version
    used a "dorsal" reference (normalize(UP - FORWARD) projected) instead; the axis dump showed it
    flipping local X to +X on the hanging Thunderbird tail and going degenerate on the Phoenix's
    down-pointing beak (bone almost antiparallel to the reference), so the same deg_x meant
    opposite things on different bones -- exactly the v18 failure class. Fixed before any clip
    was authored. For a flat toe this also makes local Z = UP (the stance sole normal gait.py's
    foot hold and verify.py's foot_orientation gate rely on).
  * wings: local Z = FORWARD on the right wing and -FORWARD on the left, i.e. mirrored, so
    `+deg_z` RAISES the wing tip on both sides (a flap) and deg_x sweeps it (mirrored sense).
Bird clips author motion through anim/bird_pose.py's semantic channels (pitch/turn/bank,
flap/sweep/twist), converted through each bone's rest matrix, so they never depend on these
choices; the explicit rolls make raw Euler values (rig_creature.py's smoke test, gait.py's generic
spine/neck/tail sinusoids) mean the documented thing too.
`dump_axes` (below) prints every bone's local X/Y/Z in world space; rig_creature.py writes it to
rig_report.json["bone_axes"] for the record.

The armature also carries a few custom properties (see armature_props) that the later stages read
instead of re-deriving: "forward" (the walk/facing axis -- the Phoenix's head is turned ~20 deg
off its body axis, so head-minus-pelvis is NOT a usable forward on this mesh), "locomotion"
("walk" or "hover"), "hover_offset" (engine-side float height, Thunderbird) and
"outline_mask_zero" (extra joints Live3D's outline pass skips).
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
    fix_wing_root_bleed,        # noqa: F401 -- re-exported (generic: wing_l_pts/wing_r_pts + roles)
)
from quadruped import (
    force_rigid_to_bone,        # noqa: F401 -- re-exported (rigid-attachment support)
    fix_root_leg_bleed,         # noqa: F401 -- re-exported
)

LANDMARKS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "landmarks")


def load_hand_landmarks_native(beast_name):
    with open(os.path.join(LANDMARKS_DIR, f"{beast_name}.json")) as f:
        return json.load(f)


def detect_landmarks_handplaced(obj, H, to_normalized, beast_name):
    """Same contract as quadruped.detect_landmarks_handplaced, plus wings/crest and the scale
    factor (native->normalised) needed to convert hover_offset-style lengths."""
    from mathutils.bvhtree import BVHTree
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(obj, depsgraph)
    data = load_hand_landmarks_native(beast_name)
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

    legs = []
    for leg_spec in data["legs"]:
        hip, knee, ankle, foot = [pt(p) for p in leg_spec["chain"]]
        toe_tip = pt(leg_spec["toe_tip"])
        hip_foot = hip - foot
        bend_dir = mathutils.Vector((0, 1, 0))
        if hip_foot.length > 1e-6:
            t_param = max(0.0, min(1.0, (knee - foot).dot(hip_foot) / hip_foot.length_squared))
            bend_dir = knee - foot.lerp(hip, t_param)
        if bend_dir.length < 1e-6:
            bend_dir = mathutils.Vector((0, 1, 0))
        bend_dir.normalize()
        legs.append({"side": leg_spec["side"], "is_front": False, "foot": foot, "ankle": ankle,
                     "knee": knee, "hip": hip, "toe_tip": toe_tip, "toe_fan": None,
                     "bend_dir": bend_dir})

    tail_points = [pt(p) for p in data["tail"]]
    wing_l_pts = [pt(p) for p in data["wing_l"]]
    wing_r_pts = [pt(p) for p in data["wing_r"]]
    crest = None
    if "crest" in data:
        crest = {"base": pt(data["crest"]["base"]), "tip": pt(data["crest"]["tip"])}
    if snap_log:
        print(f"HAND LANDMARKS ({beast_name}): {len(snap_log)} point(s) snapped inside: {snap_log}")
    # native->normalised length scale (to_normalized is affine with a uniform scale)
    scale = (to_normalized((1.0, 0.0, 0.0)) - to_normalized((0.0, 0.0, 0.0))).length
    beak_tip = pt(spine["snout_tip"])
    return {
        "H": H, "forward_sign": forward_sign, "legs": legs,
        "pelvis": pt(spine["pelvis"]), "chest": pt(spine["chest"]), "spine_01": pt(spine["spine_01"]),
        "neck_base": pt(spine["neck_base"]), "neck_mid": pt(spine["neck_mid"]),
        "head_base": pt(spine["neck_mid"]), "head": pt(spine["head"]),
        "head_top": pt(spine["head_top"], snap=False), "beak_tip": beak_tip,
        "jaw_tip": pt(spine["jaw_tip"]), "head_tip": beak_tip,
        "tail_tip": tail_points[-1], "tail_points": tail_points,
        "wing_l_pts": wing_l_pts, "wing_r_pts": wing_r_pts,
        "wing_l_tip": wing_l_pts[-1], "wing_r_tip": wing_r_pts[-1],
        "crest": crest, "notes": data.get("notes", ""),
        "locomotion": data.get("locomotion", "walk"),
        # hover_offset is given in NORMALISED (glTF/engine) units directly -- it is an engine value.
        "hover_offset": float(data.get("hover_offset", 0.0)),
        # where the root bone sits (see build_bones): default ground for a walker, below the pelvis
        # for a hoverer; a landmark file may pin it (the Phoenix became a hoverer after rigging and
        # keeps its ground root, so its bones/weights are unchanged).
        "root_at_ground": bool(data.get("root_at_ground", data.get("locomotion", "walk") != "hover")),
        "outline_mask_zero": list(data.get("outline_mask_zero", [])),
        # opt-in smooth_wing_seam parameters ({"rings": n, "iters": n}); absent -> not run
        "wing_seam_smooth": data.get("wing_seam_smooth"),
        "native_to_norm_scale": scale,
    }


def _forward(lm):
    return mathutils.Vector((0.0, lm["forward_sign"], 0.0))


def armature_props(lm):
    """Custom properties rig_creature.py stores on the armature DATA block (survive in every later
    .blend; read by anim/gait.py, anim/keyed.py, verify.py and export_glb.py)."""
    return {"forward": list(_forward(lm)), "locomotion": lm["locomotion"],
            "hover_offset": lm["hover_offset"], "outline_mask_zero": lm["outline_mask_zero"],
            "template": "winged_biped"}


def build_bones(eb, lm, H):
    """Returns (bone_names_in_build_order, bone_roles); same contract as the other templates."""
    FWD = _forward(lm)
    UP = mathutils.Vector((0, 0, 1))
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

    LAT = FWD.cross(UP).normalized()

    def roll_to(bone, ref):
        """local Z -> component of `ref` perpendicular to the bone."""
        d = (bone.tail - bone.head).normalized()
        perp = ref - ref.dot(d) * d
        bone.align_roll(perp.normalized())

    def dorsal(bone):
        """local X = LAT (perpendicular to the bone) <=> local Z = LAT_perp x Y."""
        d = (bone.tail - bone.head).normalized()
        lat_p = (LAT - LAT.dot(d) * d).normalized()
        bone.align_roll(lat_p.cross(d))

    pelvis_p, chest_p = lm["pelvis"], lm["chest"]
    # Root under the body centre at ground level for a standing bird; for a HOVERING bird (legs
    # tucked, tail hanging to the ground) a ground-level root would run its pelvis bone down the
    # whole hanging tail and pull heat weight off the tail feathers, so it starts just below the
    # pelvis instead. "root_at_ground" in the landmark data overrides this: the Phoenix hovers, but
    # its rig was built and weight-tuned with the ground root, and a hover pose never needs it moved.
    root_z = 0.0 if lm["root_at_ground"] else max(0.0, pelvis_p.z - 0.18 * H)
    root_base = mathutils.Vector((pelvis_p.x, pelvis_p.y, root_z))
    root_b = mk("root", root_base, root_base + mathutils.Vector((0, 0, 0.12 * H)), role="root")
    pelvis_b = mk("pelvis", root_b.tail.copy(), pelvis_p, "root", role="pelvis")
    s1 = mk("spine_01", pelvis_p, lm["spine_01"], "pelvis", role="spine")
    s2 = mk("spine_02", lm["spine_01"], chest_p, "spine_01", role="spine")
    n1 = mk("neck_01", chest_p, lm["neck_base"], "spine_02", role="neck")
    n2 = mk("neck_02", lm["neck_base"], lm["neck_mid"], "neck_01", role="neck")
    hd = mk("head", lm["neck_mid"], lm["head"], "neck_02", role="head")
    bk = mk("beak", lm["head"], lm["beak_tip"], "head", role="head")
    jw = mk("jaw", lm["head"], lm["jaw_tip"], "head", role="jaw")
    for b in (root_b, pelvis_b, s1, s2, n1, n2, hd, bk, jw):
        dorsal(b)

    if lm.get("crest"):
        base, tip = lm["crest"]["base"], lm["crest"]["tip"]
        mid = base.lerp(tip, 0.5)
        c1 = mk("crest_01", base, mid, "head", role="crest")
        c2 = mk("crest_02", mid, tip, "crest_01", role="crest")
        dorsal(c1)
        dorsal(c2)

    tail_pts = lm["tail_points"]
    prev = "pelvis"
    for i in range(1, len(tail_pts)):
        tb = mk(f"tail_{i:02d}", tail_pts[i - 1], tail_pts[i], prev, role="tail")
        dorsal(tb)
        prev = tb.name

    for side in ("L", "R"):
        root_pt, elbow_pt, tip_pt = lm[f"wing_{side.lower()}_pts"]
        chain = [root_pt, elbow_pt, elbow_pt.lerp(tip_pt, 0.5), tip_pt]
        prev = "spine_02"
        for i in range(1, 4):
            wb = mk(f"wing_{side}_{i:02d}", chain[i - 1], chain[i], prev, role=f"wing_{side}")
            roll_to(wb, FWD * (-1.0 if side == "L" else 1.0))
            prev = wb.name

    for leg in lm["legs"]:
        side = leg["side"]
        th = mk(f"leg_{side}_thigh", leg["hip"], leg["knee"], "pelvis", role=f"leg_{side}")
        sh = mk(f"leg_{side}_shin", leg["knee"], leg["ankle"], th.name, role=f"leg_{side}")
        ft = mk(f"leg_{side}_foot", leg["ankle"], leg["foot"], sh.name, role=f"leg_{side}")
        to = mk(f"leg_{side}_toe", leg["foot"], leg["toe_tip"], ft.name, role=f"leg_{side}")
        for lb in (th, sh, ft, to):
            dorsal(lb)

    return names, roles


def dump_axes(arm_obj):
    """{bone: {"x": [..], "y": [..], "z": [..]}} -- each bone's REST local axes in world space."""
    out = {}
    for b in arm_obj.data.bones:
        m = (arm_obj.matrix_world @ b.matrix_local).to_3x3().normalized()
        out[b.name] = {a: [round(c, 3) for c in m.col[i]] for i, a in enumerate("xyz")}
    return out


def fix_wing_card_weights(obj, bone_roles, lm, H, wing_share=0.2):
    """v19 (birds): wing feather cards must not blend with another LIMB. Heat weighting leaks
    leg/jaw/beak/head/tail weight onto feather cards that hang or reach beside other parts
    (measured: the Phoenix's lower right-wing feathers, ~0.35 lateral of the thigh, came out ~45%
    wing_R_01 / 55% leg_BR_thigh+shin next to 100%-wing card vertices -- 9-10x edge stretch on every
    stepping/lunging frame; its right wing tip near the face carried jaw/beak weight; the
    Thunderbird's chest-side wing attachment carried leg_BR_thigh). A wing and a leg/beak/tail
    move independently, so any blend between them tears; a wing blending with the torso it is
    attached to (spine/neck/pelvis) is the legitimate shoulder blend and is left alone.
    Rule, per vertex carrying both wing and FOREIGN-limb weight: vs legs/tail, wing share >=
    `wing_share` -> the foreign weight is stripped (a feather card hanging beside the leg), otherwise
    the wing weight is stripped (limb skin); vs the head group (head, beak, jaw, crest) the vertex
    goes to whichever bone chain is geometrically NEARER -- and that also applies to a vertex that
    heat weighting made 100% wing: the Thunderbird's head-top feather tuft sits ~0.1 from the
    skull and ~0.15 from the wing root and came out pure wing_R_01 interleaved with pure-head
    vertices (16-40x stretch whenever the wing moved), so any wing-weighted vertex nearer the
    head chain than every wing bone hands its wing weight to `head`. Two earlier versions were measured and
    dropped: a 50% wing majority left the 40-49%-wing feather strip on the leg side; forcing
    wing-majority vertices to PURE wing (stripping torso too) made 100%-wing islands inside the
    Thunderbird's chest skin (12-22x stretch). Returns the number of vertices changed."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    wing_ids = {gi[n] for n, r in bone_roles.items() if r in ("wing_L", "wing_R") and n in gi}
    limb = {gi[n] for n, r in bone_roles.items() if (r.startswith("leg_") or r == "tail") and n in gi}
    headg = {gi[n] for n, r in bone_roles.items() if r in ("head", "jaw", "crest") and n in gi}
    head_idx = gi["head"]
    head_segs = [(lm["neck_mid"], lm["head"]), (lm["head"], lm["beak_tip"]), (lm["head"], lm["jaw_tip"]),
                 (lm["head"], lm["head_top"])]  # head_top = top of skull / head tuft
    if lm.get("crest"):
        head_segs.append((lm["crest"]["base"], lm["crest"]["tip"]))
    wing_segs = []
    for k in ("wing_l_pts", "wing_r_pts"):
        pts = lm[k]
        wing_segs += list(zip(pts, pts[1:]))
    changed = 0
    for vi, v in enumerate(obj.data.vertices):
        tot = sum(g.weight for g in v.groups)
        w = sum(g.weight for g in v.groups if g.group in wing_ids)
        if tot <= 1e-6 or w <= 1e-4:
            continue
        fl = sum(g.weight for g in v.groups if g.group in limb)
        dh = min(_seg_dist(v.co, a, b) for a, b in head_segs)
        dw = min(_seg_dist(v.co, a, b) for a, b in wing_segs)
        if dh < dw:
            # nearer the head than any wing bone: head skin/feathers, even if heat weighting made
            # it 100% wing (the Thunderbird's head tuft) -- wing weight moves to the head bone
            obj.vertex_groups[head_idx].add([vi], w + next(
                (g.weight for g in v.groups if g.group == head_idx), 0.0), 'REPLACE')
            for g in [g.group for g in v.groups if g.group in wing_ids]:
                obj.vertex_groups[g].remove([vi])
            changed += 1
            continue
        drop = set(headg)  # nearer a wing: no head-group weight
        if fl > 1e-4:
            drop |= limb if w / tot >= wing_share else wing_ids
        hit = [g.group for g in v.groups if g.group in drop]
        for g in hit:
            obj.vertex_groups[g].remove([vi])
        if hit:
            changed += 1
    return changed


def fix_beak_bleed(obj, lm, back_frac=0.25):
    """v19 (birds): beak and jaw both start at the skull-centre landmark, so heat weighting spreads
    their weight around the whole skull, including the BACK of the head (measured on the Phoenix:
    a vertex 0.3 behind the skull centre at 77% jaw+beak). Beak/jaw weight on a vertex whose
    projection onto that bone is under `back_frac` of its length (i.e. behind/at the hinge) is
    moved to `head`. Returns the number of vertices changed."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    if "head" not in gi:
        return 0
    head_idx = gi["head"]
    segs = {gi[n]: (lm["head"], lm[t]) for n, t in (("beak", "beak_tip"), ("jaw", "jaw_tip")) if n in gi}
    changed = 0
    for vi, v in enumerate(obj.data.vertices):
        moved = 0.0
        for g in list(v.groups):
            if g.group not in segs or g.weight <= 1e-5:
                continue
            a, b = segs[g.group]
            ab = b - a
            t = (v.co - a).dot(ab) / max(ab.length_squared, 1e-9)
            if t < back_frac:
                moved += g.weight
                obj.vertex_groups[g.group].remove([vi])
        if moved > 0:
            cur = next((g.weight for g in v.groups if g.group == head_idx), 0.0)
            obj.vertex_groups[head_idx].add([vi], cur + moved, 'REPLACE')
            changed += 1
    return changed


def _seg_dist(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def fix_tail_leg_bleed(obj, bone_roles, lm):
    """v19 (birds): both birds carry their tail right behind/between tucked or short legs
    (Thunderbird: legs folded against the tail base; Phoenix: the flame tail starts at the hips),
    and heat weighting handed tail-feather vertices to the nearest THIGH -- measured on the
    Thunderbird: a 100% leg_BR_thigh vertex 0.10 behind the hip, inside the tail, next to a
    tail_02-dominant one (17.7x stretch the moment Attack throws the talons forward). Any vertex
    carrying leg weight that is geometrically nearer the tail polyline than its leg's chain
    (hip-knee-ankle-foot-toe, with a 0.8 margin in the leg's favour) has that leg weight moved to
    its nearest tail bone. Returns the number of vertices changed."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    tail_pts = lm["tail_points"]
    tail_names = [f"tail_{i:02d}" for i in range(1, len(tail_pts))]
    legs = {l["side"]: [l["hip"], l["knee"], l["ankle"], l["foot"], l["toe_tip"]] for l in lm["legs"]}
    leg_ids = {gi[n]: r.split("_", 1)[1] for n, r in bone_roles.items() if r.startswith("leg_") and n in gi}
    changed = 0
    for vi, v in enumerate(obj.data.vertices):
        lw = [(g.group, g.weight) for g in v.groups if g.group in leg_ids and g.weight > 1e-4]
        if not lw:
            continue
        p = v.co
        dists = [_seg_dist(p, a, b) for a, b in zip(tail_pts, tail_pts[1:])]
        k = min(range(len(dists)), key=lambda i: dists[i])
        side = leg_ids[max(lw, key=lambda t: t[1])[0]]
        chain = legs[side]
        d_leg = min(_seg_dist(p, a, b) for a, b in zip(chain, chain[1:]))
        if dists[k] >= 0.8 * d_leg or tail_names[k] not in gi:
            continue
        moved = sum(w for _, w in lw)
        for g, _ in lw:
            obj.vertex_groups[g].remove([vi])
        tidx = gi[tail_names[k]]
        cur = next((g.weight for g in v.groups if g.group == tidx), 0.0)
        obj.vertex_groups[tidx].add([vi], cur + moved, 'REPLACE')
        changed += 1
    return changed


def fix_sole_weights(obj, legs, bone_roles, **kw):
    """quadruped.fix_sole_weights ramps thigh/shin weight onto the foot for every vertex below
    ankle height -- right for a bird that stands (Phoenix), wrong for one that hovers with its legs
    tucked mid-air (Thunderbird: "below the ankle" is the whole hanging tail). Skipped when the
    lowest foot is clearly off the ground."""
    from quadruped import fix_sole_weights as _q
    if min(l["foot"].z for l in legs) > 0.05 * max(l["hip"].z for l in legs) + 0.05:
        return 0
    return _q(obj, legs, bone_roles, **kw)


def fill_unweighted(obj, max_passes=8):
    """After rig_creature.py's generic root-weight strip: a hovering bird's root bone sits at
    ground level under the body, i.e. at its TAIL TIP, so a few tail-tip vertices were weighted to
    root alone and end up with no group at all. Each such vertex copies the weights of its nearest
    weighted edge-neighbour (repeated so a small cluster fills from its edge). Returns the count."""
    me = obj.data
    nbrs = {i: set() for i in range(len(me.vertices))}
    for e in me.edges:
        a, b = e.vertices
        nbrs[a].add(b)
        nbrs[b].add(a)
    filled = 0
    for _ in range(max_passes):
        todo = [v.index for v in me.vertices if not any(g.weight > 1e-6 for g in v.groups)]
        if not todo:
            break
        for vi in todo:
            cands = [n for n in nbrs[vi] if any(g.weight > 1e-6 for g in me.vertices[n].groups)]
            if not cands:
                continue
            src = min(cands, key=lambda n: (me.vertices[n].co - me.vertices[vi].co).length)
            for g in me.vertices[src].groups:
                obj.vertex_groups[g.group].add([vi], g.weight, 'REPLACE')
            filled += 1
    return filled


def smooth_wing_seam(obj, bone_roles, lm, rings=3, iters=6, max_influences=4, whole=False):
    """v19 round 2 (Phoenix, opt-in via the landmark file's "wing_seam_smooth"): a flapping wing
    whose LOWER card edge is modelled fused to the belly/thigh skin. fix_wing_card_weights makes
    that edge a hard 100%-wing / 100%-body step (right for a walking bird whose wing barely moves --
    a wing/leg blend there tore on every step), but once the wing really flaps the single edge row
    across the step stretches 17-25x (verify.py edge-stretch on edge 3779-3785 of the Phoenix) and
    shows in-engine as pale stretched wedges along the wing's lower edge. Here the step is turned
    into a ramp: every wing/non-wing boundary edge below the wing root seeds a zone `rings`
    edge-rings wide, and the zone's weights are Laplacian-smoothed `iters` times with the zone's
    outer ring held fixed, so the wing's motion fades over several edges instead of one. `whole`
    also takes the boundaries above the root: the Phoenix's right wing card touches the back of its
    head (edge 1019-1021, head vs wing_R_01: 7-12x once the wing flaps). Measured on the Phoenix's
    flying clips, worst edge over all 7: lower seam only 25x -> 12.4x, whole 6.2x (the next worst
    is tail vs tucked shin). Each vertex keeps its `max_influences` largest weights, renormalised
    (glTF skins carry 4). Returns the number of zone vertices."""
    me = obj.data
    gi = {g.name: g.index for g in obj.vertex_groups}
    wing_ids = {gi[n] for n, r in bone_roles.items() if r in ("wing_L", "wing_R") and n in gi}
    root_z = min(lm["wing_l_pts"][0].z, lm["wing_r_pts"][0].z) if not whole else float("inf")
    W = [{g.group: g.weight for g in v.groups if g.weight > 1e-6} for v in me.vertices]

    def is_wing(i):
        tot = sum(W[i].values())
        return tot > 1e-6 and sum(w for g, w in W[i].items() if g in wing_ids) / tot > 0.5

    nbrs = [set() for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nbrs[a].add(b)
        nbrs[b].add(a)
    seed = set()
    for e in me.edges:
        a, b = e.vertices
        if is_wing(a) != is_wing(b) and max(me.vertices[a].co.z, me.vertices[b].co.z) < root_z:
            seed |= {a, b}
    zone, front = set(seed), set(seed)
    for _ in range(rings):
        front = {n for v in front for n in nbrs[v] if me.vertices[n].co.z < root_z} - zone
        zone |= front
    inner = zone - front  # the outermost ring stays fixed (anchors the ramp)
    for _ in range(iters):
        new = {}
        for v in inner:
            acc = dict(W[v])
            for n in nbrs[v]:
                for g, w in W[n].items():
                    acc[g] = acc.get(g, 0.0) + w
            tot = sum(acc.values())
            new[v] = {g: w / tot for g, w in acc.items()} if tot > 1e-9 else W[v]
        for v, d in new.items():
            W[v] = d
    for v in inner:
        top = sorted(W[v].items(), key=lambda kv: -kv[1])[:max_influences]
        tot = sum(w for _, w in top)
        for g in [g.group for g in me.vertices[v].groups]:
            obj.vertex_groups[g].remove([v])
        for g, w in top:
            obj.vertex_groups[g].add([v], w / tot, 'REPLACE')
    return len(inner)
