"""v19 (birds): semantic posing helpers shared by anim/gait.py (Phoenix walk extras, Thunderbird
hover Move) and anim/keyed.py (both birds' six keyed clips).

Why semantic, not raw local Euler: a bird rig mixes vertical (torso), horizontal (neck/beak),
hanging (Thunderbird tail) and sideways/raised (wings) bones, so "deg_x" cannot mean the same
world motion on all of them whatever the roll convention (rig_templates/winged_biped.py documents
the explicit rolls it does set). Clips here are authored in WORLD-meaningful terms and converted
through each bone's own rest matrix:

  centreline / leg bones:  pitch  -- about the body's lateral axis LAT = FORWARD x UP;
                                     +pitch turns FORWARD toward UP (a vertical bone's tip goes
                                     BACK, a horizontal one's goes UP: head back / lean back /
                                     tail up; -pitch bows forward/down)
                           turn   -- about UP; +turn swings FORWARD toward +X
                           bank   -- about FORWARD; +bank tips UP toward -X
  wings (side-mirrored):   flap   -- +flap raises the wing tip (about -/+FORWARD for L/R)
                           sweep  -- +sweep swings the wing tip FORWARD (about +/-UP for L/R)
                           twist  -- about the bone's own rest axis (+ = leading edge up)

A rotation R given in armature space is written as matrix_basis = Rrest^-1 @ R @ Rrest (rest 3x3),
i.e. it is applied about the bone's head in its parent's (posed) frame, so values accumulate down
a chain like ordinary FK. verify_axes() in this module is the numeric check that these mean what
they say on a given rig (see the v19 README section).
"""
import math

import bpy
import mathutils
from mathutils import Matrix, Vector

UP = Vector((0.0, 0.0, 1.0))


# Per-creature constant channels every HOVER clip carries (anim/gait.py's hover Move and every
# keyed.py clip incl. KO), as (kind, bone, channel, deg). Phoenix (v19 round 2): legs tucked the
# way a flying bird folds them -- thigh only slightly forward (its skin region covers the whole
# lower belly side and abuts the left wing card: a 70 deg thigh tuck measured 16x/25x edge stretch
# and tore in-engine), shin folded back up, foot and talons curled -- and the half-folded wings'
# wing_*_02 held 35 deg more open (so a +-30 swing reaches flat-open on the downbeat). The
# Thunderbird's mesh is already modelled tucked and needs none.
HOVER_HOLDS = {
    "phoenix": [("b", "leg_BL_thigh", "pitch", 20.0), ("b", "leg_BR_thigh", "pitch", 20.0),
                ("b", "leg_BL_shin", "pitch", -105.0), ("b", "leg_BR_shin", "pitch", -105.0),
                ("b", "leg_BL_foot", "pitch", 80.0), ("b", "leg_BR_foot", "pitch", 80.0),
                ("b", "leg_BL_toe", "pitch", -80.0), ("b", "leg_BR_toe", "pitch", -80.0),
                ("b", "wing_L_02", "flap", -35.0), ("b", "wing_R_02", "flap", -35.0),
                ("b", "wing_L_03", "flap", -5.0), ("b", "wing_R_03", "flap", -5.0)],
}


def forward_of(arm_obj):
    f = arm_obj.data.get("forward")
    if f is None:
        raise RuntimeError("bird_pose: armature has no 'forward' property (winged_biped rigs set it)")
    v = Vector(tuple(f))
    v.z = 0.0
    return v.normalized()


def world_rot(fwd, pitch=0.0, turn=0.0, bank=0.0):
    lat = fwd.cross(UP).normalized()
    return (Matrix.Rotation(math.radians(turn), 3, UP)
            @ Matrix.Rotation(math.radians(pitch), 3, lat)
            @ Matrix.Rotation(math.radians(bank), 3, fwd))


def wing_side(name):
    return -1.0 if "_L_" in name or name.endswith("_L") else 1.0


def wing_rot(fwd, rest3, sx, flap=0.0, sweep=0.0, twist=0.0):
    axis_bone = rest3.col[1].normalized()
    return (Matrix.Rotation(math.radians(sweep), 3, UP * (-sx))
            @ Matrix.Rotation(math.radians(flap), 3, fwd * sx)
            @ Matrix.Rotation(math.radians(twist), 3, axis_bone * sx))


def local_euler(pb, Rw, prev=None):
    rest3 = pb.bone.matrix_local.to_3x3().normalized()
    basis = rest3.inverted() @ Rw @ rest3
    return basis.to_euler("XYZ", prev) if prev is not None else basis.to_euler("XYZ")


_LIMITS_CACHE = {}


def joint_limits(arm_obj):
    """v20 round 2 (humanoid Treant): {bone: {channel: [lo, hi]}} from the armature's
    `joint_limits` JSON property (rig_templates/biped_arms.HUMAN_LIMITS); {} on every other rig."""
    raw = arm_obj.data.get("joint_limits")
    if not raw:
        return {}
    if raw not in _LIMITS_CACHE:
        import json
        _LIMITS_CACHE[raw] = json.loads(raw)
    return _LIMITS_CACHE[raw]


def set_semantic(arm_obj, fwd, name, ch):
    """ch: dict with any of pitch/turn/bank (non-wing) or flap/sweep/twist (wing_*), plus
    optional sx/sy/sz scale. Missing bone -> no-op.
    v20 round 2 (humanoid rigs only): non-wing bones also take flex (about the bone's own rest
    local X = its hinge, + = flexion), abd (about FORWARD, + = away from the body on the bone's
    side) and twist (about the bone's own axis); every channel listed in the armature's
    joint_limits is clamped to its human range first (a hinge lists 0..0 for its off-axis
    channels, so it can only flex)."""
    pb = arm_obj.pose.bones.get(name)
    if pb is None:
        return
    pb.rotation_mode = "XYZ"
    lim = joint_limits(arm_obj).get(name)
    if lim:
        ch = dict(ch)
        for k, (lo, hi) in lim.items():
            if k in ch:
                ch[k] = min(hi, max(lo, ch[k]))
    if name.startswith("wing_"):
        rest3 = pb.bone.matrix_local.to_3x3().normalized()
        Rw = wing_rot(fwd, rest3, wing_side(name), ch.get("flap", 0.0), ch.get("sweep", 0.0),
                      ch.get("twist", 0.0))
    else:
        Rw = world_rot(fwd, ch.get("pitch", 0.0), ch.get("turn", 0.0), ch.get("bank", 0.0))
        if any(ch.get(k) for k in ("flex", "abd", "twist")):
            rest3 = pb.bone.matrix_local.to_3x3().normalized()
            Rw = (Rw @ Matrix.Rotation(math.radians(ch.get("abd", 0.0)), 3, fwd * wing_side(name))
                  @ Matrix.Rotation(math.radians(ch.get("flex", 0.0)), 3, rest3.col[0])
                  @ Matrix.Rotation(math.radians(ch.get("twist", 0.0)), 3, rest3.col[1]))
    pb.rotation_euler = local_euler(pb, Rw, pb.rotation_euler.copy())
    pb.scale = (ch.get("sx", 1.0), ch.get("sy", 1.0), ch.get("sz", 1.0))


def set_root(arm_obj, fwd, loc_world=(0.0, 0.0, 0.0), pitch=0.0, turn=0.0, bank=0.0):
    """Root translation given in WORLD axes (converted to the root bone's local frame) and rotation
    in the same pitch/turn/bank terms (about the root's head, on the ground under the body)."""
    pb = arm_obj.pose.bones["root"]
    pb.rotation_mode = "XYZ"
    rest3 = pb.bone.matrix_local.to_3x3().normalized()
    pb.location = rest3.inverted() @ Vector(loc_world)
    pb.rotation_euler = local_euler(pb, world_rot(fwd, pitch, turn, bank), pb.rotation_euler.copy())


# ---------------------------------------------------------------------------------------------
# Key-pose interpolation. A pose is a dict:
#   {"bones": {name: {channel: value}}, "root": {"x","y","z" (world offsets), "pitch","turn","bank"},
#    "lift": {side: (up, fwd)}  (IK-raised planted foot), "free": [sides] (legs not planted),
#    "jaw": deg (shorthand for bones["jaw"]["pitch"])}
# Keys: [(fraction, pose, ease)] -- ease is the curve used on the segment ARRIVING at that key:
# "inout" (smoothstep, the Bezier EASE_IN_OUT equivalent), "in" (u^2: accelerates INTO the key --
# a strike/impact frame) or "out" (decelerates).
# ---------------------------------------------------------------------------------------------
def _flatten(pose):
    out = {}
    for b, ch in pose.get("bones", {}).items():
        for k, v in ch.items():
            out[("b", b, k)] = v
    if "jaw" in pose:
        out[("b", "jaw", "pitch")] = pose["jaw"]
    for k, v in pose.get("root", {}).items():
        out[("r", k)] = v
    for side, (up, fw) in pose.get("lift", {}).items():
        out[("lift", side, 0)] = up
        out[("lift", side, 1)] = fw
    return out


_DEFAULT = {"sx": 1.0, "sy": 1.0, "sz": 1.0}


def _ease(u, kind):
    if kind == "in":
        return u * u
    if kind == "out":
        return 1.0 - (1.0 - u) * (1.0 - u)
    if kind == "linear":
        return u
    return u * u * (3.0 - 2.0 * u)


def sample_keys(keys, t):
    """Channel dict at clip fraction t from keys [(frac, pose, ease)]."""
    flats = [(f, _flatten(p), e) for f, p, e in keys]
    allk = set()
    for _, fl, _ in flats:
        allk |= set(fl)

    def val(fl, k):
        return fl.get(k, _DEFAULT.get(k[-1], 0.0) if k[0] == "b" else 0.0)
    if t <= flats[0][0]:
        return {k: val(flats[0][1], k) for k in allk}
    for (f0, a, _), (f1, b, e) in zip(flats, flats[1:]):
        if t <= f1 + 1e-9:
            u = 0.0 if f1 - f0 < 1e-9 else (t - f0) / (f1 - f0)
            u = _ease(max(0.0, min(1.0, u)), e)
            return {k: val(a, k) + (val(b, k) - val(a, k)) * u for k in allk}
    return {k: val(flats[-1][1], k) for k in allk}


def add_channels(base, extra):
    """Additive layering (scale channels default to 1.0, so a +0.03 layer means x1.03)."""
    out = dict(base)
    for k, v in extra.items():
        default = 1.0 if (k[0] == "b" and k[-1] in ("sx", "sy", "sz")) else 0.0
        out[k] = out.get(k, default) + v
    return out


def sample_tracks(tracks, t):
    """Several independent key tracks (e.g. a pose track with ease-in-out plus a linear root-turn
    track), summed channel-wise (scale channels: deltas from 1.0 are summed)."""
    out = {}
    for keys in tracks:
        for k, v in sample_keys(keys, t).items():
            is_scale = k[0] == "b" and k[-1] in ("sx", "sy", "sz")
            if k in out:
                out[k] += (v - 1.0) if is_scale else v
            else:
                out[k] = v
    return out


def sines(layers, t):
    """Continuous sinusoid layers (data): ("b", bone, channel, amp, cycles, phase) or
    ("r", channel, amp, cycles, phase); value = amp * sin(2*pi*cycles*t + phase). Integer cycles
    keep a looping clip seamless."""
    out = {}
    for L in layers:
        if L[0] == "b":
            _, bone, ch, amp, cyc, ph = L
            k = ("b", bone, ch)
        else:
            _, ch, amp, cyc, ph = L
            k = ("r", ch)
        out[k] = out.get(k, 0.0) + amp * math.sin(2 * math.pi * cyc * t + ph)
    return out


def wing_layers(cycles, amps=(18.0, 10.0, 8.0), lags=(0.0, 0.6, 1.2), sweep=0.0, phase=0.0):
    """Symmetric flap layer for both wings: wing_*_01/02/03 flap with increasing phase LAG down the
    chain (follow-through: the tip trails the root), optional sweep (forward on the downstroke)."""
    out = []
    for side in ("L", "R"):
        for i, (amp, lag) in enumerate(zip(amps, lags), start=1):
            out.append(("b", f"wing_{side}_{i:02d}", "flap", amp, cycles, phase - lag))
        if sweep:
            out.append(("b", f"wing_{side}_01", "sweep", sweep, cycles, phase + math.pi / 2))
    return out


def euler_near(r, prev):
    """v22 round 4: of an XYZ Euler's two equivalent solutions (x, y, z) and (x+pi, pi-y, z+pi),
    each made 2pi-compatible with `prev`, the one closest to `prev` (make_compatible alone keeps
    the solution it is given, so a bone sweeping past y = +-90 deg flips by 180 on two axes)."""
    a = r.copy(); a.make_compatible(prev)
    b = mathutils.Euler((r.x + math.pi, math.pi - r.y, r.z + math.pi), "XYZ"); b.make_compatible(prev)
    da = sum(abs(x - y) for x, y in zip(a, prev)); db = sum(abs(x - y) for x, y in zip(b, prev))
    return a if da <= db else b


def bake_frames(arm_obj, scene, fwd, name, n_frames, chan_fn, post_fn=None, interp_fn=None):
    """Two-pass per-frame bake. Pass 1 (no action assigned, so a depsgraph update can never
    re-drive the pose from F-curves -- see gait.py's set_bone_world_matrix_direct note): for each
    frame, chan_fn(t) -> channel dict, apply it, then post_fn(chans, t) (planted-foot IK, ground
    settle, ...), and snapshot every bone's rotation/scale + root location. Pass 2: write the
    snapshots as keys on a new action, LINEAR (already eased in Python). Returns the action."""
    names = [pb.name for pb in arm_obj.pose.bones]
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.matrix_basis = Matrix.Identity(4)
    arm_obj.animation_data_create()
    arm_obj.animation_data.action = None
    snaps = []
    # v22 round 3 (the Archer's bow / string / arrow bones, placed by anim/bow_rig.py from a world
    # matrix): keep their Euler keys continuous (no +-180 deg flips between frames). No other rig
    # carries "bow_rig", so every other bake is unchanged.
    compat = set()
    if arm_obj.data.get("bow_rig"):
        import json as _json
        _br = _json.loads(arm_obj.data["bow_rig"])
        compat = {_br["bow"], _br["string"], _br["arrow"]}
    for i in range(n_frames + 1):
        t = i / n_frames
        chans = chan_fn(t)
        apply_channels(arm_obj, fwd, chans, names)
        if post_fn:
            post_fn(chans, t)
        snap = {}
        for pb in arm_obj.pose.bones:
            r = pb.rotation_euler.copy()
            if pb.name in compat and snaps:
                r = euler_near(r, mathutils.Euler(snaps[-1][pb.name][0], "XYZ"))
            snap[pb.name] = (tuple(r), tuple(pb.scale), tuple(pb.location))
        snaps.append(snap)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm_obj.animation_data.action = act
    # v21 (blob rigs): squash-follower bones also get location keys (anim/blob_pose.follow_squash);
    # every other rig has no `loc_keyed` property, so only root's location is keyed, as before.
    loc_keyed = {"root"} | set(arm_obj.data.get("loc_keyed", []))
    for i, snap in enumerate(snaps):
        f = i + 1
        scene.frame_set(f)
        for pb in arm_obj.pose.bones:
            r, sc, loc = snap[pb.name]
            pb.rotation_euler = r
            pb.scale = sc
            pb.keyframe_insert(data_path="rotation_euler", frame=f)
            pb.keyframe_insert(data_path="scale", frame=f)
            if pb.name in loc_keyed:
                pb.location = loc
                pb.keyframe_insert(data_path="location", frame=f)
    for layer in act.layers:
        for strip in layer.strips:
            for slot in act.slots:
                cb = strip.channelbag(slot)
                if cb:
                    for fc in cb.fcurves:
                        for kp in fc.keyframe_points:
                            kp.interpolation = "LINEAR"
    return act


def apply_channels(arm_obj, fwd, chans, bone_names):
    per_bone = {}
    for k, v in chans.items():
        if k[0] == "b":
            per_bone.setdefault(k[1], {})[k[2]] = v
    for name in bone_names:
        if name == "root":
            continue
        set_semantic(arm_obj, fwd, name, per_bone.get(name, {}))
    set_root(arm_obj, fwd, (chans.get(("r", "x"), 0.0), chans.get(("r", "y"), 0.0),
                            chans.get(("r", "z"), 0.0)),
             chans.get(("r", "pitch"), 0.0), chans.get(("r", "turn"), 0.0),
             chans.get(("r", "bank"), 0.0))


def lifts_of(chans):
    out = {}
    for k, v in chans.items():
        if k[0] == "lift":
            out.setdefault(k[1], [0.0, 0.0])[k[2]] = v
    return out


# ---------------------------------------------------------------------------------------------
# Python-side FK + planted-foot IK (same construction as anim/keyed.py's quadruped builder and
# anim/gait.py's fk_anchored mode: closed-form 2-bone IK, minimum-twist aim, foot held at its rest
# WORLD orientation, toe at rest relative to the foot). No depsgraph update is needed.
# ---------------------------------------------------------------------------------------------
def fk_world(pb):
    chain = []
    b = pb
    while b is not None:
        chain.append(b)
        b = b.parent
    m = Matrix.Identity(4)
    prev_rest = Matrix.Identity(4)
    for b in reversed(chain):
        m = m @ prev_rest.inverted() @ b.bone.matrix_local @ b.matrix_basis
        prev_rest = b.bone.matrix_local
    return m


def _aim_min_twist(neutral, head_pos, tail_pos):
    rot = neutral.to_3x3().normalized()
    want = tail_pos - head_pos
    if want.length > 1e-7:
        rot = rot.col[1].rotation_difference(want.normalized()).to_matrix() @ rot
    m = rot.to_4x4()
    m.translation = head_pos
    return m


def _set_basis(pb, desired_world, parent_world):
    rest = pb.bone.matrix_local
    prest = pb.parent.bone.matrix_local
    basis = rest.inverted() @ prest @ parent_world.inverted() @ desired_world
    pb.rotation_euler = basis.to_euler("XYZ", pb.rotation_euler)


def leg_ik_data(arm_obj, side, fwd):
    th = arm_obj.data.bones[f"leg_{side}_thigh"]
    sh = arm_obj.data.bones[f"leg_{side}_shin"]
    hip, knee, ankle = th.head_local.copy(), th.tail_local.copy(), sh.tail_local.copy()
    ha = ankle - hip
    t = max(0.0, min(1.0, (knee - hip).dot(ha) / max(ha.length_squared, 1e-9)))
    off = knee - (hip + ha * t)
    pole = off.normalized() if off.length / max(th.length + sh.length, 1e-6) >= 0.06 else fwd.copy()
    if arm_obj.data.get("humanoid"):  # v20 round 2: a human knee only ever bends forward
        pole = fwd.copy()
    return {"L1": th.length, "L2": sh.length, "ankle_rest": ankle, "pole": pole}


def plant_leg(arm_obj, side, d, ankle_target=None):
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
        perp = Vector((0, 0, 1)) - dirn.z * dirn
    perp.normalize()
    a = math.acos(cos_a)
    knee = hip + (dirn * math.cos(a) + perp * math.sin(a)) * L1
    ankle = knee + (hip + dirn * dist - knee).normalized() * L2
    th_w = _aim_min_twist(neutral_th, hip, knee)
    _set_basis(th, th_w, parent_w)
    sh_w = _aim_min_twist(th_w @ th.bone.matrix_local.inverted() @ sh.bone.matrix_local, knee, ankle)
    _set_basis(sh, sh_w, th_w)
    ft_w = ft.bone.matrix_local.to_3x3().to_4x4()
    ft_w.translation = ankle
    _set_basis(ft, ft_w, sh_w)
    if toe is not None:
        toe.rotation_euler = (0, 0, 0)


# ---------------------------------------------------------------------------------------------
# Axis check (the "confirm the rotations mean what they assume" step). For every bone: its rest
# local axes in world space, what +20 deg on raw local X/Y/Z does to the bone's tip, and the same
# for the semantic channels. Returns a dict; callers dump it to JSON.
# ---------------------------------------------------------------------------------------------
def verify_axes(arm_obj, deg=20.0):
    fwd = forward_of(arm_obj)
    lat = fwd.cross(UP).normalized()
    out = {}

    def tip_move(pb, R3):
        rest = pb.bone.matrix_local
        v = rest.to_3x3() @ Vector((0, pb.bone.length, 0))
        rest3 = rest.to_3x3().normalized()
        moved = (rest3 @ R3 @ rest3.inverted()) @ v
        d = moved - v
        return d.normalized() if d.length > 1e-9 else d

    def name_dir(d):
        cands = {"forward": fwd, "back": -fwd, "up": UP, "down": -UP, "+X": Vector((1, 0, 0)),
                 "-X": Vector((-1, 0, 0))}
        best = max(cands, key=lambda k: cands[k].dot(d))
        return f"{best}({cands[best].dot(d):.2f})"

    for pb in arm_obj.pose.bones:
        rest3 = pb.bone.matrix_local.to_3x3().normalized()
        rec = {"axes": {a: [round(c, 3) for c in rest3.col[i]] for i, a in enumerate("xyz")}}
        for i, a in enumerate("xyz"):
            e = [0.0, 0.0, 0.0]
            e[i] = math.radians(deg)
            rec[f"+{a}_tip_moves"] = name_dir(tip_move(pb, mathutils.Euler(e, "XYZ").to_matrix()))
        if pb.name.startswith("wing_"):
            sx = wing_side(pb.name)
            for k in ("flap", "sweep"):
                Rw = wing_rot(fwd, rest3, sx, **{k: deg})
                rec[f"+{k}_tip_moves"] = name_dir(tip_move(pb, rest3.inverted() @ Rw @ rest3))
        else:
            for k in ("pitch", "turn", "bank"):
                Rw = world_rot(fwd, **{k: deg})
                rec[f"+{k}_tip_moves"] = name_dir(tip_move(pb, rest3.inverted() @ Rw @ rest3))
            rec["local_x_dot_lateral"] = round(rest3.col[0].dot(lat), 3)
            if joint_limits(arm_obj) and pb.name.startswith("arm_"):  # v20 round 2 humanoid arms
                Rf = Matrix.Rotation(math.radians(deg), 3, rest3.col[0])
                Ra = Matrix.Rotation(math.radians(deg), 3, fwd * wing_side(pb.name))
                rec["+flex_tip_moves"] = name_dir(tip_move(pb, rest3.inverted() @ Rf @ rest3))
                rec["+abd_tip_moves"] = name_dir(tip_move(pb, rest3.inverted() @ Ra @ rest3))
        out[pb.name] = rec
    return out
