"""The "quadruped" deform-rig template: a wingless quadruped, generalised from
`rig_templates/winged_quadruped.py` for creatures without wings or an eagle-style splayed-toe
forefoot -- Golem, Kirin, Tarasque, Basilisk (all hand-placed-landmark creatures; this template has
no horizontal-slicing mesh-geometry detection path, unlike `winged_quadruped.detect_landmarks`).

Reuses winged_quadruped's generic (not Griffin-specific) pieces directly rather than duplicating
them: the native(pre-normalisation)->normalised coordinate conversion (`_native_to_normalized_fn`),
the outside-the-mesh snap correction (`_snap_if_outside`), the per-leg vertex-mask builder
(`build_leg_masks`) and the cross-leg/belly-centre weight-conflict fix (`fix_hip_weight_gradient`)
-- all four already operate on a generic `legs` list (side/hip/knee/ankle/foot/is_front) and a
`bone_roles` dict keyed by `leg_<side>`, with nothing Griffin-specific in their logic. There is no
wing-root-bleed or toe-fan-weight fix here (this family has neither wings nor a toe fan), and no
reused `detect_landmarks` (slicing-based) path -- every one of these four meshes is rigged purely
from hand-placed landmarks (see `detect_landmarks_handplaced` below).

Deform bone list this template builds, per creature (4 legs):
  root, pelvis, spine_01, spine_02, neck_01, neck_02, head, snout, jaw,
  tail_01..tail_NN (NN = len(this beast's "tail" landmark points) - 1 -- the chain length comes
    directly from however many points the lead hand-placed for that creature's tail, so a
    short/stub tail (Golem, Tarasque) gets fewer bones than a long one (Basilisk)),
  scapula_<FL/FR> + leg_<side>_thigh/shin/foot/toe per leg (ONE toe bone per foot -- hooves/paws/
    stone feet, never the Griffin foreleg's 3-bone splayed-talon toe fan).
No extra bones for rigid head-attached mesh detail that doesn't articulate independently (Kirin's
two mirrored horns + ears) -- see `force_rigid_to_bone` below, called from rig_creature.py after
weighting to force that detail 100% onto the existing `head` bone's vertex group instead.

Bone names deliberately match winged_quadruped.py's convention everywhere a role matches (root,
pelvis, spine_01/spine_02, neck_01/neck_02, head, jaw, tail_NN, scapula_<side>,
leg_<side>_thigh/shin/foot/toe) so anim/gait.py and anim/keyed.py -- which look bones up BY NAME,
almost always via `.get()`/membership checks that silently no-op when a name is absent -- can drive
this rig unchanged in a later animation pass. Checked directly against both files before finalising
this template: gait.py's per-leg 2-bone IK solve unconditionally expects a `scapula_<side>` bone to
exist for any leg whose side starts with "F" (`if side.startswith("F"): rest_head_tail(f"scapula_
{side}")`, no `.get`/presence guard there), which is why forelegs here still get a scapula bone even
though there is no wing attached to it; it also branches on `f"leg_{side}_toe_in" in arm_data.bones`
to detect a toe fan (absent here, so it falls through to the single `leg_{side}_toe` bone path
unchanged). keyed.py's hardcoded per-clip poses reference "beak" nowhere (safe to rename that role
to "snout" here) and reference `tail_01`..`tail_04` by literal name in a few clips, but every
lookup goes through `arm_obj.pose.bones.get(name)` (checked: `set_rot_local`'s own definition),
which no-ops for a name this rig doesn't have -- so a 2- or 3-bone tail here (vs. the Griffin's 4)
is safe, it just won't receive whatever portion of that clip's tail motion targets the missing
higher-numbered bones, which is exactly the right behaviour for a shorter tail (nothing to drop the
motion ON).

The "snout" bone plays the Griffin's "beak" role (a rigid nose/snout tip, child of head) under a
more general name -- "beak" is bird-specific and these four creatures aren't birds; the landmark
JSON's own "snout_tip" key (see rig_templates/landmarks/<beast>.json) documents this explicitly
("plays the Griffin's 'beak_tip' role").
"""
import bpy
import math
import mathutils
import json
import os

from winged_quadruped import (
    _native_to_normalized_fn,  # noqa: F401 -- re-exported; rig_creature.py imports this off
                                # whichever template module it selects, generically.
    _snap_if_outside,
    build_leg_masks,            # noqa: F401 -- re-exported for rig_creature.py's generic call
    fix_hip_weight_gradient,    # noqa: F401 -- re-exported for rig_creature.py's generic call
)

LANDMARKS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "landmarks")


def load_hand_landmarks_native(beast_name):
    """Loads rig_templates/landmarks/<beast_name>.json -- the same structure as
    winged_quadruped.HAND_LANDMARKS_NATIVE's per-beast entries (legs/spine/tail/notes, plus an
    optional "horn" for Kirin), in the prepped mesh's own NATIVE (pre-normalisation) coordinates,
    copied verbatim (nothing stripped) from the lead's landmarks_lead.json."""
    path = os.path.join(LANDMARKS_DIR, f"{beast_name}.json")
    with open(path) as f:
        return json.load(f)


def detect_landmarks_handplaced(obj, H, to_normalized, beast_name):
    """Builds the landmark dict build_bones() expects, from this beast's hand-placed native-space
    JSON (see load_hand_landmarks_native). Same pt()/snap-if-outside approach as
    winged_quadruped.detect_landmarks_handplaced, minus the wing/toe-fan-specific pieces this
    family of creatures doesn't have. `obj` must already be normalised (common.normalise_transform
    already run); `to_normalized` is `_native_to_normalized_fn`'s conversion built from the mesh's
    PRE-normalisation bounding box, captured by the caller before normalising."""
    from mathutils.bvhtree import BVHTree
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(obj, depsgraph)
    data = load_hand_landmarks_native(beast_name)

    snap_log = []

    def pt(native_xyz):
        p = to_normalized(native_xyz)
        snapped, moved, exceeded = _snap_if_outside(obj, bvh, p, H)
        if moved > 1e-6:
            snap_log.append((tuple(round(c, 4) for c in p), moved, exceeded))
        return snapped

    spine = data["spine"]
    pelvis = pt(spine["pelvis"])
    chest = pt(spine["chest"])

    # Forward axis: same approach as winged_quadruped.detect_landmarks_handplaced -- derived
    # directly from the hand-placed landmarks themselves (head further from pelvis along -Y or +Y),
    # not guessed from mesh geometry.
    _pelvis_native_y = spine["pelvis"][1]
    _head_native_y = spine["head"][1]
    forward_sign = -1.0 if _head_native_y < _pelvis_native_y else 1.0

    legs = []
    for leg_spec in data["legs"]:
        hip, knee, ankle, foot = [pt(p) for p in leg_spec["chain"]]
        toe_tip = pt(leg_spec["toe_tip"])
        hip_foot = hip - foot
        if hip_foot.length > 1e-6:
            t_param = max(0.0, min(1.0, (knee - foot).dot(hip_foot) / hip_foot.length_squared))
            on_line = foot.lerp(hip, t_param)
            bend_dir = knee - on_line
        else:
            bend_dir = mathutils.Vector((0, 1, 0))
        if bend_dir.length < 1e-6:
            bend_dir = mathutils.Vector((0, 1, 0))
        bend_dir.normalize()
        legs.append({"side": leg_spec["side"], "is_front": leg_spec["is_front"],
                     "foot": foot, "ankle": ankle, "knee": knee, "hip": hip,
                     "toe_tip": toe_tip, "toe_fan": None, "bend_dir": bend_dir})

    tail_points = [pt(p) for p in data["tail"]]

    horn = None
    if "horn" in data:
        horn = {"base": pt(data["horn"]["base"]), "tip": pt(data["horn"]["tip"])}

    if snap_log:
        exceeded_pts = [s for s in snap_log if s[2]]
        print(f"HAND LANDMARKS ({beast_name}): {len(snap_log)} point(s) snapped back inside the "
              f"mesh (max {max(m for _, m, _ in snap_log):.4f}): {snap_log}")
        if exceeded_pts:
            print(f"HAND LANDMARKS WARNING ({beast_name}): {len(exceeded_pts)} point(s) needed "
                  f"MORE than the instructed 0.04 budget to fully resolve outside the mesh -- see "
                  f"_snap_if_outside's docstring: {exceeded_pts}")
    else:
        print(f"HAND LANDMARKS ({beast_name}): all points already inside the mesh, no snapping needed")

    return {
        "H": H,
        "forward_sign": forward_sign,
        "legs": legs,
        "pelvis": pelvis,
        "chest": chest,
        "spine_01": pt(spine["spine_01"]),
        "neck_base": pt(spine["neck_base"]),
        "neck_mid": pt(spine["neck_mid"]),
        "head_base": pt(spine["neck_mid"]),  # kept for callers that still read head_base directly
        "head": pt(spine["head"]),
        "head_top": pt(spine["head_top"]),
        "snout_tip": pt(spine["snout_tip"]),
        "jaw_tip": pt(spine["jaw_tip"]),
        "head_tip": pt(spine["snout_tip"]),  # kept for callers that still read head_tip directly
        "tail_tip": tail_points[-1],
        "tail_points": tail_points,
        "horn": horn,
        "notes": data.get("notes", ""),
        # v20 (Frost Wyrm), opt-in per landmark file -- absent for every v18 creature, whose rigs
        # are therefore unchanged: "forward_mode": "body" stores the pelvis->chest walk axis on the
        # armature (the head is turned ~20 deg, so head-minus-pelvis is crabwise); "roll_policy":
        # "lateral" makes every bone's roll explicit as local X = the body's lateral axis.
        "forward_mode": data.get("forward_mode"),
        "roll_policy": data.get("roll_policy"),
        "jaw_split": data.get("jaw_split"),
        # optional explicit mouth geometry for split_jaw_weights (native points, not snapped)
        "jaw_hinge": (to_normalized(data["jaw_split"]["hinge"]) if isinstance(data.get("jaw_split"), dict)
                      and "hinge" in data["jaw_split"] else None),
        "jaw_mouth_tip": (to_normalized(data["jaw_split"]["mouth_tip"]) if isinstance(data.get("jaw_split"), dict)
                          and "mouth_tip" in data["jaw_split"] else None),
        # v21 (opt-in, Stalker): rigid detail with no bone of its own (antlers) -- see
        # rigid_regions_normalized and rig_creature.py's generic rigid_regions pass.
        "rigid_regions": rigid_regions_normalized(data, to_normalized),
        "weight_smooth_regions": rigid_regions_normalized(
            {"rigid_regions": [dict(r, bone="") for r in data.get("weight_smooth_regions") or []]},
            to_normalized, keep=("iterations",)),
    }


def smooth_weights_region(obj, center, radius, iterations=6, max_influences=4, **_):
    """v21 (opt-in "weight_smooth_regions", Stalker chest underside): Laplacian relaxation of the
    vertex weights of every vertex within `radius` of `center` (mesh-edge neighbours, half own /
    half neighbour mean per pass; vertices outside the sphere act as fixed boundary values), then
    the top `max_influences` kept and renormalised. For a patch where heat weighting left HARD
    neighbouring ownerships of independently moving bones (one vertex 100% scapula_FL beside one
    on leg_FR_thigh/neck_01/jaw under the Stalker's chin): a gradient over several edge rings
    instead of one 5-8x stretched edge. Returns the number of vertices relaxed."""
    import bmesh
    me = obj.data
    r2 = radius * radius
    region = [v.index for v in me.vertices if (v.co - center).length_squared <= r2]
    if not region:
        return 0
    nbr = {i: set() for i in range(len(me.vertices))}
    for e in me.edges:
        a, b = e.vertices
        nbr[a].add(b)
        nbr[b].add(a)
    W = [{g.group: g.weight for g in v.groups} for v in me.vertices]
    rs = set(region)
    for _ in range(iterations):
        new = {}
        for i in region:
            acc = {}
            nb = nbr[i]
            for j in nb:
                for g, w in W[j].items():
                    acc[g] = acc.get(g, 0.0) + w / len(nb)
            mixed = {g: 0.5 * W[i].get(g, 0.0) + 0.5 * acc.get(g, 0.0) for g in set(W[i]) | set(acc)}
            top = sorted(mixed.items(), key=lambda kv: -kv[1])[:max_influences]
            tot = sum(w for _, w in top) or 1.0
            new[i] = {g: w / tot for g, w in top if w / tot > 1e-4}
        for i in region:
            W[i] = new[i]
    for i in rs:
        v = me.vertices[i]
        for g in list(v.groups):
            if g.group not in W[i]:
                obj.vertex_groups[g.group].remove([i])
        for g, w in W[i].items():
            obj.vertex_groups[g].add([i], w, "REPLACE")
    return len(region)


def rigid_regions_normalized(data, to_normalized, keep=()):
    """v21: the landmark file's optional "rigid_regions" list ({bone, center, radius, min_z?, min_y?,
    protect_roles?}, native coords) converted to the normalised frame (radius/min_* scaled by the
    same uniform factor as every point). [] when absent, so older rigs are unchanged."""
    regs = data.get("rigid_regions") or []
    if not regs:
        return []
    o = to_normalized((0.0, 0.0, 0.0))
    k = (to_normalized((0.0, 0.0, 1.0)) - o).z
    out = []
    for r in regs:
        c = to_normalized(r["center"])
        out.append({"bone": r["bone"], "center": c, "radius": r["radius"] * k,
                    "min_z": to_normalized((0, 0, r["min_z"])).z if "min_z" in r else None,
                    "min_y": to_normalized((0, r["min_y"], 0)).y if "min_y" in r else None,
                    "protect_roles": tuple(r.get("protect_roles", ())),
                    "protect_threshold": r.get("protect_threshold", 0.1),
                    **{k: r[k] for k in keep if k in r}})
    return out


def _body_forward(lm):
    f = lm["chest"] - lm["pelvis"]
    f.z = 0.0
    return f.normalized()


def armature_props(lm):
    """v20: armature custom properties (same keys as winged_biped.armature_props) -- ONLY for a
    landmark file that opts in with "forward_mode": "body"; every v18 quadruped gets {} (no
    properties, so gait.py/verify.py keep deriving FORWARD from head - pelvis exactly as before)."""
    if lm.get("forward_mode") != "body":
        return {}
    return {"forward": list(_body_forward(lm)), "locomotion": "walk", "hover_offset": 0.0,
            "outline_mask_zero": [], "template": "quadruped"}


def build_bones(eb, lm, H):
    """Builds the wingless-quadruped deform skeleton into armature edit_bones `eb` from landmarks
    `lm` (as returned by detect_landmarks_handplaced above). Returns (bone_names_in_build_order,
    bone_roles) -- same contract as winged_quadruped.build_bones, so rig_creature.py's generic
    driver code works unchanged against either template module."""
    pelvis_p = lm["pelvis"]
    chest_p = lm["chest"]
    roles = {}
    names = []

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

    def align_roll_up(bone):
        """Sets bone.roll so local Z points as close to world-up as the bone's own head->tail
        direction allows (falls back to world +Y/forward when the bone is itself close to
        vertical, where (0,0,1) would be a near-degenerate roll target -- same "pick whichever
        candidate axis is least parallel" principle anim/gait.py already uses for its own per-leg
        up_hint choice). Establishes local-X=lateral / local-Y=head-tail / local-Z=up for every
        bone anim/gait.py and anim/keyed.py drive with plain local XYZ-Euler rotations (gait.py:
        deg_x=pitch around the lateral axis, deg_y=roll around the spine axis, deg_z=yaw around
        the vertical axis; keyed.py: apply_pose's rotation_euler tuples assume the same).

        v18 round-2 finding (lead review): Blender's un-set, auto-computed default roll has NO
        reason to already match this convention, and empirically doesn't for these new creatures'
        bone geometry (very different head/tail positions from the Griffin's own bones, which
        gait.py's pitch/roll/yaw constants were tuned against for 17 rounds) -- confirmed as the
        actual mechanism behind Move being broken on Golem/Tarasque/Basilisk/Kirin (body pitched/
        rolled/twisted far more than the small tuned amplitudes should produce, tail swinging on
        the wrong axis): the SAME numeric `deg_y` roll amount rotates around a completely different
        effective world axis depending on each bone's own (otherwise arbitrary) default roll.
        Applied to every bone anim/gait.py or anim/keyed.py poses via plain local Euler rotation
        (pelvis/spine/neck/head/tail plus, for anim/keyed.py's hand-keyed clips, scapula/thigh/
        shin -- gait.py's Move drives those four via full IK world-matrix placement instead, which
        ignores rest roll entirely, but keyed.py's apply_pose does not). NOT applied to
        winged_quadruped.py's Griffin build_bones -- keeps the Griffin rig byte-for-byte
        unchanged; its own default roll is what every round of gait.py/keyed.py tuning was already
        verified against."""
        direction = bone.tail - bone.head
        if direction.length < 1e-7:
            return
        direction = direction.normalized()
        target = mathutils.Vector((0, 0, 1))
        if abs(direction.dot(target)) > 0.9:
            target = mathutils.Vector((0, 1, 0))
        bone.align_roll(target)

    def align_roll_leg(bone):
        """v18 round-3 fix (lead review: Basilisk's Cast rose the wrong way even after
        align_roll_up fixed Move). Diagnosed directly (a roll-axes dump of leg_FL_thigh for all
        four creatures, not guessed): align_roll_up's single 0.9 dot-product threshold for "is this
        bone close enough to vertical to need the Y fallback" put different creatures' thigh bones
        on OPPOSITE sides of that threshold -- Golem/Tarasque/Kirin's thighs point almost straight
        down (|dot with (0,0,1)| = 0.99), comfortably past 0.9, so they got the Y-fallback roll
        reference; Basilisk's thigh, per its own notes ("legs fold forward-down... crouched"),
        points measurably less vertically (|dot| = 0.853) -- just under 0.9 -- so it kept the
        (0,0,1) primary reference instead. Two different roll bases for the "same" bone role means
        the SAME numeric pose angle (anim/keyed.py's basilisk_cast reused the other creatures'
        already-working front-thigh/spine/neck/head values) rotates around a genuinely different
        effective axis for Basilisk than for the other three, which is what actually produced the
        wrong-direction rise (not a sign typo in the pose data itself -- confirmed by first checking
        whether flipping basilisk_cast's signs fixed it in isolation, which it did not, before
        finding this root cause).

        Fix: scapula/thigh/shin always use the Y (forward) roll reference unconditionally --
        matches what Golem/Tarasque/Kirin's own near-vertical thighs already got from align_roll_up
        (so their rigs are unaffected -- same final roll), and brings Basilisk's more horizontal
        thigh onto the SAME convention instead of a threshold accident. Spine/neck/head/tail keep
        align_roll_up's Z-primary logic unchanged (their dot-with-Z stayed consistently low, under
        0.3, for all four creatures -- confirmed in the same diagnostic -- so that threshold never
        actually flips sides for those bones; this fix is specific to the leg-bone geometry that
        does straddle it)."""
        direction = bone.tail - bone.head
        if direction.length < 1e-7:
            return
        bone.align_roll(mathutils.Vector((0, 1, 0)))

    root_b = mk("root", (0, 0, 0), (0, 0, 0.12 * H), role="root")
    pelvis_b = mk("pelvis", (0, 0, 0.12 * H), pelvis_p, "root", role="pelvis")
    spine1_tail = lm.get("spine_01", pelvis_p.lerp(chest_p, 0.5))
    spine01_b = mk("spine_01", pelvis_p, spine1_tail, "pelvis", role="spine")
    spine02_b = mk("spine_02", spine1_tail, chest_p, "spine_01", role="spine")

    neck_base = lm.get("neck_base", chest_p.lerp(lm["head"], 1.0 / 3.0))
    neck_mid = lm.get("neck_mid", lm["head"])
    neck01_b = mk("neck_01", chest_p, neck_base, "spine_02", role="neck")
    neck02_b = mk("neck_02", neck_base, neck_mid, "neck_01", role="neck")
    head_b = mk("head", neck_mid, lm["head"], "neck_02", role="head")
    mk("snout", lm["head"], lm["snout_tip"], "head", role="head")
    mk("jaw", lm["head"], lm["jaw_tip"], "head", role="jaw")

    # v18 round-2 fix (see align_roll_up's own docstring): every bone anim/gait.py or anim/keyed.py
    # poses via plain local XYZ-Euler rotation needs a predictable local-axis convention -- Blender's
    # un-set default roll doesn't provide one for these new creatures' bone geometry.
    for b in (root_b, pelvis_b, spine01_b, spine02_b, neck01_b, neck02_b, head_b):
        align_roll_up(b)

    # Round-1 lead review (Kirin): the mesh has TWO symmetric mirrored V horns, not one centred
    # horn -- a single centreline "horn" bone (the original design) necessarily points into the
    # empty space between them, visibly wrong in the overlay. No bone is built from "horn" any
    # more; the landmark is still loaded (see detect_landmarks_handplaced) and reused purely as a
    # RADIUS HINT for force_rigid_to_bone's region (called from rig_creature.py after weighting) --
    # both horns (and the ears) are instead weighted 100% rigid to the existing "head" bone there,
    # which needs no extra bone at all since none of them articulate independently.

    # Tail chain: variable length, driven entirely by how many points this beast's landmark JSON
    # supplies (N points -> N-1 bones) -- a short/stub tail (Golem, Tarasque: 3 points -> 2 bones)
    # gets fewer bones than a long one (Basilisk: 6 points -> 5 bones), matching the lead's notes
    # ("no visible tail: stub tail inside the body", "tail is ~half the total length: needs 5+ tail
    # bones"). Same bone-1-head-is-tail_points[0] construction as winged_quadruped.build_bones
    # (not the pelvis position itself).
    tail_pts = lm["tail_points"]
    n_tail_bones = len(tail_pts) - 1
    prev_name = "pelvis"
    for i in range(1, n_tail_bones + 1):
        name = f"tail_{i:02d}"
        tail_b = mk(name, tail_pts[i - 1], tail_pts[i], prev_name, role="tail")
        align_roll_up(tail_b)  # v18 round-2 fix -- see align_roll_up's docstring
        prev_name = name

    # Legs: 4-bone chain (thigh/shin/foot/toe) per leg, ONE toe bone (no toe fan). Front legs
    # attach via a scapula bone (same shoulder-blade-swing mechanism as the Griffin's forelegs,
    # independent of wings -- and required by anim/gait.py's unconditional scapula_<side> lookup
    # for any front leg, see this module's docstring); hind legs attach directly to the pelvis.
    for leg in lm["legs"]:
        side = leg["side"]
        foot = leg["foot"]
        hip = leg["hip"]
        knee = leg["knee"]
        ankle = leg["ankle"]
        is_front = leg["is_front"]
        toe_tip = leg.get("toe_tip") or mathutils.Vector(
            (foot.x, foot.y + lm["forward_sign"] * 0.12 * H, foot.z))
        if is_front:
            scapula_b = mk(f"scapula_{side}", chest_p, hip, "spine_02", role=f"scapula_{side}")
            align_roll_leg(scapula_b)  # v18 round-3 fix -- see align_roll_leg's docstring (anim/
            # keyed.py's hand-keyed clips pose scapula via plain local Euler rotation; anim/gait.py's
            # Move does not -- it places scapula via full IK world-matrix, ignoring rest roll -- but
            # this still needs to be consistent for keyed.py's benefit)
            parent_bone = f"scapula_{side}"
        else:
            parent_bone = "pelvis"
        thigh_b = mk(f"leg_{side}_thigh", hip, knee, parent_bone, role=f"leg_{side}")
        shin_b = mk(f"leg_{side}_shin", knee, ankle, f"leg_{side}_thigh", role=f"leg_{side}")
        align_roll_leg(thigh_b)  # v18 round-3 fix -- same reasoning as scapula above: anim/gait.py's
        align_roll_leg(shin_b)   # Move ignores thigh/shin rest roll (full IK matrix placement), but
        # anim/keyed.py's hand-keyed clips pose them via plain local Euler rotation and do need it.
        foot_b = mk(f"leg_{side}_foot", ankle, foot, f"leg_{side}_shin", role=f"leg_{side}")
        toe_b = mk(f"leg_{side}_toe", foot, toe_tip, f"leg_{side}_foot", role=f"leg_{side}")
        # v18 finding (verify.py's foot_orientation gate): anim/gait.py holds the foot/toe at their
        # BIND rotation during stance (round 14's fix, see gait.py's own comment -- it copies the
        # rest rotation directly rather than reconstructing one via aim_matrix+up_hint), so whatever
        # local Z axis each bone's REST roll happens to have IS its stance-time "sole normal" --
        # Blender's un-set, auto-computed default roll has no reason to point that local Z anywhere
        # near world-up, and empirically doesn't for three of these four creatures' shorter/more
        # crouched legs (confirmed: Kirin's longer, more Griffin-like leg proportions happened to
        # pass this gate with the default roll; Golem/Tarasque/Basilisk's shorter, more acutely-
        # angled rest legs did not, by over 100 degrees). `align_roll` explicitly sets each bone's
        # roll so its local Z points as close to world-up as its own head-tail direction allows --
        # the fix belongs here, at rig-build time, not in gait.py (which correctly just preserves
        # whatever rest roll it's given). Not applied to winged_quadruped.py's Griffin build_bones
        # (keeps the Griffin rig byte-for-byte unchanged; its own default roll already works).
        foot_b.align_roll(mathutils.Vector((0, 0, 1)))
        toe_b.align_roll(mathutils.Vector((0, 0, 1)))

    if lm.get("roll_policy") == "lateral":
        # v20 (opt-in, Frost Wyrm): EVERY bone's roll set explicitly to local X = the body's
        # lateral axis LAT = FORWARD x UP (projected perpendicular to the bone), local Z = X x Y --
        # the winged_biped convention. Needed because align_roll((0,0,1)) is DEGENERATE on a
        # vertical bone: the Frost Wyrm's foot bones (ankle -> foot) point straight down, so their
        # "explicit" roll would be whatever Blender picks for a zero-length reference. With this
        # policy raw deg_x is a pitch about LAT on every bone (+ = tip toward UP for a forward bone),
        # and a flat toe's local Z is UP (the stance sole normal verify.py's foot_orientation reads).
        # FORWARD here is the BODY axis (pelvis -> chest), not head - pelvis (the head is turned).
        fwd = _body_forward(lm) if lm.get("forward_mode") == "body" else             mathutils.Vector((0.0, lm["forward_sign"], 0.0))
        lat = fwd.cross(mathutils.Vector((0, 0, 1))).normalized()
        for n in names:
            b = eb[n]
            d = (b.tail - b.head).normalized()
            lat_p = lat - lat.dot(d) * d
            if lat_p.length < 1e-4:  # a bone along LAT itself: fall back to UP as local Z
                b.align_roll(mathutils.Vector((0, 0, 1)))
                continue
            b.align_roll(lat_p.normalized().cross(d))

    return names, roles


def force_rigid_to_bone(obj, bone_roles, center, radius, target_bone, min_z=None, min_y=None,
                         protect_roles=(), protect_threshold=0.1):
    """Forces every vertex within `radius` of world-space `center` (and, if given, at or above
    `min_z`/`min_y`) to be weighted 100% to `target_bone`, stripping every other group membership --
    for mesh detail that has no bone of its own but must move completely rigidly with one existing
    bone (Kirin's two mirrored horns + ears, rigid to `head`; see build_bones' docstring on why
    there's no separate horn bone). A vertex already carrying more than `protect_threshold` combined
    weight on any role in `protect_roles` is left alone (protects an adjacent, independently-
    animated part -- e.g. the neck -- from being swallowed by an otherwise-generous rigid region).

    Round-2 Kirin fix, worth recording: the first attempt also put "jaw" in `protect_roles`, meant
    to keep this forcing pass off the mouth -- but it had the OPPOSITE effect for Kirin's actual
    mesh. Confirmed by inspecting the real horn-tip vertices directly: Blender's heat-weight solver
    gave them substantial (~30%) spurious `jaw`/`snout` weight (those vertices are nowhere near the
    jaw -- `jaw` and `snout` both originate at the same point as `head`, and a thin, nearly-isolated
    protrusion like a horn tip is exactly the shape that defeats clean surface-aware heat diffusion,
    falling back to something closer to plain distance-to-bone-origin, where head/jaw/snout's shared
    origin makes them all look similarly "close") -- so protecting "jaw" left every horn-tip vertex
    untouched (0 forced) instead of fixing it. `min_y` is the actual fix for keeping this pass off
    the snout/jaw: Kirin's horns point UP/BACK (native horn-tip Y is at-or-behind the skull's own Y
    in this mesh's head=-Y convention), while the snout/jaw/muzzle point distinctly FORWARD (more
    negative Y) -- requiring `v.co.y >= min_y` (e.g. the skull's own Y) cleanly separates "horn/ear
    territory" from "muzzle territory" by actual geometry, which turned out to be the reliable
    signal here, not vertex-group membership.

    Must run AFTER common.cleanup_weights/normalize_weights (the true final word, like the other
    per-creature weighting fixes in this pipeline) -- nothing after this should re-touch these
    vertices' groups. Returns the number of vertices forced."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    if target_bone not in group_index:
        obj.vertex_groups.new(name=target_bone)
        group_index = {g.name: g.index for g in obj.vertex_groups}
    target_idx = group_index[target_bone]
    protect_ids = {group_index[n] for n, role in bone_roles.items()
                   if role in protect_roles and n in group_index}
    r2 = radius * radius
    forced = 0
    for vi, v in enumerate(me.vertices):
        if (v.co - center).length_squared > r2:
            continue
        if min_z is not None and v.co.z < min_z:
            continue
        if min_y is not None and v.co.y < min_y:
            continue
        if protect_ids:
            protect_w = sum(g.weight for g in v.groups if g.group in protect_ids)
            if protect_w > protect_threshold:
                continue
        for g in list(v.groups):
            if g.group != target_idx:
                obj.vertex_groups[g.group].remove([vi])
        obj.vertex_groups[target_idx].add([vi], 1.0, 'REPLACE')
        forced += 1
    return forced


def fix_root_leg_bleed(obj, bone_roles):
    """Animation-stage finding (v18 gate run): `root` never moves relative to the legs during any
    clip in this pipeline except KO's lying-down translation (gait.py/keyed.py never rotate it),
    so a vertex that blends substantial `root` weight with substantial weight on any `leg_<side>_*`
    bone gets pulled two independent directions every frame the SAME way fix_hip_weight_gradient's
    leg-pair stripping targets for two different legs -- confirmed as the live mechanism on Golem:
    verify.py's Move/Attack/KO edge-stretch and toe_deformation gates all flagged the SAME edge,
    (root=0.77/pelvis=0.23) next to (leg_FL_shin=0.42/leg_FL_foot=0.37/leg_FL_toe=0.22), an 8-18x
    stretch and (accumulated across the Move cycle's frames) thousands of flipped triangles at
    exactly that boundary. Neither `restrict_leg_weights` (its allowed-bones set for a leg mask
    vertex is that leg's own chain + spine_02/pelvis/scapula -- never includes `root`, so a vertex
    OUTSIDE the knee-to-foot mask, e.g. near the hip/thigh transition where this bled in on Golem's
    short legs, isn't touched) nor `fix_hip_weight_gradient`'s pair-stripping (it only iterates
    LEG-vs-LEG pairs, never root) previously covered this specific conflict. Same conservative
    "strip only the conflicting pair, leave everything else alone" approach as those two. Must run
    BEFORE common.cleanup_weights (like the other weighting fixes in this pipeline) and again after
    it (cleanup_weights' own smoothing re-spreads weight across group boundaries the same way).
    Returns the number of vertices fixed."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    root_idx = group_index.get("root")
    if root_idx is None:
        return 0
    leg_ids = {group_index[n] for n, role in bone_roles.items()
               if role.startswith("leg_") and n in group_index}
    if not leg_ids:
        return 0
    fixed = 0
    for vi, v in enumerate(me.vertices):
        root_w = sum(g.weight for g in v.groups if g.group == root_idx)
        leg_w = sum(g.weight for g in v.groups if g.group in leg_ids)
        if root_w > 0.08 and leg_w > 0.08:
            for g in list(v.groups):
                if g.group == root_idx:
                    obj.vertex_groups[root_idx].remove([vi])
            fixed += 1
    return fixed


def fix_scapula_cross_leg_bleed(obj, bone_roles):
    """v18 round-2 finding (lead review: "Move is broken... edge-stretch 7-32x"). After fixing the
    bone-roll bug that caused the GROSS pose breakage (see rig_templates/quadruped.py's
    align_roll_up), Golem's Move edge-stretch gate still flagged a real, separate weight conflict:
    a vertex near the chest centreline blending `scapula_FR` (100%) with its NEIGHBOUR blending
    `leg_FL_shin`/`leg_FL_thigh` -- scapula_FR and leg_FL (opposite forelegs, always out of phase
    in a lateral-sequence gait) move independently every frame, exactly the class of conflict
    `fix_hip_weight_gradient`'s leg-pair stripping already targets for two different LEGS -- but
    that function's leg groups are built from the `leg_<side>` role tag only, which never includes
    `scapula_<side>` (a separate role tag, `scapula_FL`/`scapula_FR`), so a scapula-vs-opposite-leg
    conflict was invisible to it. `fix_hip_weight_gradient` is shared with the Griffin (reused
    directly, not duplicated -- see this module's docstring) and must not be changed to add
    scapula handling there (would change Griffin's own weighting). This is a separate, narrow
    function instead: for each front leg's `scapula_<side>`, strips its weight from any vertex
    that also carries substantial weight on any OTHER side's `leg_<other>_*` bones (both directions
    -- strips whichever is the minority influence is wrong in spirit; strips scapula specifically,
    since a vertex this far out on a leg's own chain has a clearer "home" there than on a shoulder-
    blade bone it's only brushing past). Same conservative "strip only the conflicting pair" style
    as fix_root_leg_bleed. Must run BEFORE common.cleanup_weights (and again after it, like the
    other weighting fixes here). Returns the number of vertices fixed."""
    me = obj.data
    group_index = {g.name: g.index for g in obj.vertex_groups}
    scapula_sides = {role.split("_", 1)[1] for role in bone_roles.values() if role.startswith("scapula_")}
    fixed = 0
    for scap_side in scapula_sides:
        scap_name = f"scapula_{scap_side}"
        if scap_name not in group_index:
            continue
        scap_idx = group_index[scap_name]
        other_leg_ids = {group_index[n] for n, role in bone_roles.items()
                          if role.startswith("leg_") and role != f"leg_{scap_side}" and n in group_index}
        if not other_leg_ids:
            continue
        for vi, v in enumerate(me.vertices):
            scap_w = sum(g.weight for g in v.groups if g.group == scap_idx)
            other_w = sum(g.weight for g in v.groups if g.group in other_leg_ids)
            if scap_w > 0.08 and other_w > 0.08:
                for g in list(v.groups):
                    if g.group == scap_idx:
                        obj.vertex_groups[scap_idx].remove([vi])
                fixed += 1
    return fixed


def fix_sole_weights(obj, legs, bone_roles, ramp_frac=0.6, max_influences=4):
    """v18 round-4 finding (lead review: Golem Attack/Hit/Victory feet dipped 5-9 cm through the
    floor even with the feet IK-planted). Measured directly: the lowest vertices in those frames
    were SOLE vertices weighted 60-73% to `leg_<side>_shin` -- heat weighting gives a stumpy foot's
    sole mostly to the shin (the foot bone is a short ankle->ground segment), so whenever the knee
    flexes, the shin swings and drags the sole through the ground even though the foot BONE is held
    level at its rest orientation (anim/gait.py and anim/keyed.py both hold it so). Fix: below each
    leg's own ankle height, that leg's thigh+shin weight is ramped onto `leg_<side>_foot` (fully at
    `(1 - ramp_frac) * ankle_z` and below, linearly up to none at the ankle), so the sole is rigid
    with the level foot. Quadruped template only (Griffin untouched). Run after the final weight
    restriction, before the final normalise. Returns the number of vertices changed."""
    me = obj.data
    gi = {g.name: g.index for g in obj.vertex_groups}
    changed = 0
    leg_by_side = {l["side"]: l for l in legs}
    side_groups = {}
    for side in leg_by_side:
        upper = {gi[n] for n in (f"leg_{side}_thigh", f"leg_{side}_shin") if n in gi}
        allg = {gi[n] for n, r in bone_roles.items() if r == f"leg_{side}" and n in gi}
        side_groups[side] = (upper, allg, gi.get(f"leg_{side}_foot"))
    for vi, v in enumerate(me.vertices):
        best, best_w = None, 0.0
        for side, (_, allg, _) in side_groups.items():
            w = sum(g.weight for g in v.groups if g.group in allg)
            if w > best_w:
                best, best_w = side, w
        if best is None or best_w < 0.05:
            continue
        upper, _, foot_idx = side_groups[best]
        if foot_idx is None:
            continue
        ankle_z = leg_by_side[best]["ankle"].z
        if v.co.z >= ankle_z or ankle_z <= 1e-6:
            continue
        t = max(0.0, min(1.0, (ankle_z - v.co.z) / (ramp_frac * ankle_z)))
        moved = 0.0
        for g in list(v.groups):
            if g.group in upper and g.weight > 0:
                take = g.weight * t
                moved += take
                if g.weight - take < 1e-4:
                    obj.vertex_groups[g.group].remove([vi])
                else:
                    obj.vertex_groups[g.group].add([vi], g.weight - take, 'REPLACE')
        if moved > 1e-4:
            cur = next((g.weight for g in v.groups if g.group == foot_idx), 0.0)
            obj.vertex_groups[foot_idx].add([vi], cur + moved, 'REPLACE')
            groups = sorted(v.groups, key=lambda g: g.weight)
            while len(groups) > max_influences:
                obj.vertex_groups[groups[0].group].remove([vi])
                groups = groups[1:]
            changed += 1
    return changed


def split_jaw_weights(obj, lm, band=0.05, hinge_ramp=0.3, min_head=0.6, max_depth=None, **_):
    """v20 (opt-in via the landmark file's "jaw_split": true; Frost Wyrm). Heat weighting smears the
    `jaw` bone's weight thinly over the whole skull (measured on the Frost Wyrm: 863 vertices carry
    some jaw weight, only 18 more than 0.5), so a jaw rotation tilts the whole head a little and never
    opens the mouth. The mouth is a FUSED (closed, manifold) seam, so here the head-group weight
    (head + snout + jaw) of every skull vertex is re-split by geometry: the mouth plane runs through
    the hinge (the `head` landmark) along hinge -> mouth tip (midway between snout_tip and jaw_tip)
    and the body's lateral axis; a vertex below that plane and forward of the hinge goes to `jaw`,
    above it to snout/head, with a `band`-wide ramp across the plane and a ramp from the hinge
    (`hinge_ramp` x mouth length) so the cheeks bend instead of tearing. Only vertices whose head
    group already holds >= `min_head` of their weight are touched. The landmark file may give the
    mouth corner ("hinge") and the front of the mouth seam ("mouth_tip") explicitly (native coords,
    read off a section along the head axis) -- the skull-centre `head` landmark sits at eye height,
    well above the mouth -- and `max_depth` (normalised units below the mouth plane) keeps the throat
    off the jaw. Returns the count changed."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    if not all(n in gi for n in ("head", "snout", "jaw")):
        return 0
    hinge = lm["jaw_hinge"] if lm.get("jaw_hinge") is not None else lm["head"]
    tip = lm["jaw_mouth_tip"] if lm.get("jaw_mouth_tip") is not None else (lm["snout_tip"] + lm["jaw_tip"]) / 2
    d = tip - hinge
    L = d.length
    d.normalize()
    fwd = _body_forward(lm) if lm.get("forward_mode") == "body" else mathutils.Vector((0, lm["forward_sign"], 0))
    lat = fwd.cross(mathutils.Vector((0, 0, 1))).normalized()
    lat = (lat - lat.dot(d) * d).normalized()
    n = lat.cross(d).normalized()
    if n.z < 0:
        n = -n
    ih, isn, ij = gi["head"], gi["snout"], gi["jaw"]
    hg = {ih, isn, ij}
    changed = 0

    def sstep(x):
        x = max(0.0, min(1.0, x))
        return x * x * (3 - 2 * x)
    for vi, v in enumerate(obj.data.vertices):
        w = {g.group: g.weight for g in v.groups}
        tot = sum(w.values())
        H = sum(w.get(g, 0.0) for g in hg)
        if tot <= 1e-6 or H / tot < min_head:
            continue
        rel = v.co - hinge
        t = rel.dot(d) / L
        if t < -0.15:
            continue
        s = sstep(0.5 - rel.dot(n) / band) * sstep(t / hinge_ramp)
        if max_depth is not None:  # throat/chest far below the mouth stay off the jaw
            s *= 1.0 - sstep((-rel.dot(n) - max_depth) / band)
        up = w.get(ih, 0.0) + w.get(isn, 0.0)
        frac_sn = w.get(isn, 0.0) / up if up > 1e-6 else (0.5 if t > 0.5 else 0.0)
        new = {ij: H * s, isn: H * (1 - s) * frac_sn, ih: H * (1 - s) * (1 - frac_sn)}
        for g, val in new.items():
            if val > 1e-4:
                obj.vertex_groups[g].add([vi], val, 'REPLACE')
            elif g in w:
                obj.vertex_groups[g].remove([vi])
        changed += 1
    return changed
