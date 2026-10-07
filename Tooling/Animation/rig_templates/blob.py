"""The "blob" deform-rig template (v21, enemies batch 1): a round body that moves by squash and
stretch -- the legless Brute (a furry ball with ram horns; its paws are only painted on) and the
crowd-unit Swarmling (a small blob on four stubby legs, drawn in a merged-batch swarm, so it has a
HARD 6-bone budget).

Data-driven like serpent.py: hand-placed landmarks in rig_templates/landmarks/<beast>.json
(native prepped-mesh coords; head at -Y). Bones:
  root         on the ground under the body (hop / lean / KO root motion),
  body         ground -> top of the ball, child of root. The squash/stretch bone: clips key its
               SCALE (local Y = up = height, X/Z = width/depth). Its head sits ON the floor, so a
               squash never pushes the base through the ground. Never rotated by the clips (leans
               go on root), so its children only ever inherit an axis-aligned scale.
  parts        the landmark file's "parts" list, in order: {name, parent, head, tail, role,
               follow (bool), rigid (bool), feet ([x, y] native, leg-pair regions)}. A part whose
               parent is root and has "follow": true is a SQUASH FOLLOWER: unscaled itself (rigid
               face, horns), but its location is keyed every frame so its head rides on the
               squashing ball (anim/blob_pose.follow_squash) -- the face and horns stay rigid
               while the fur squashes. Parts parented to body (tuft, crest) inherit the squash.
Every roll is explicit, the serpent/winged_biped convention: local X = the body's lateral axis
LAT = FORWARD x UP (projected perpendicular to the bone); a bone along LAT falls back to local
Z = UP. Clips use anim/bird_pose.py's semantic channels; bird_pose.verify_axes is the axis dump.

Weighting: heat weights (common.auto_weight_with_fallbacks), then
  * fix_blob_weights: a "rigid" part's dominantly-owned vertices go 100% to it (horns), and a part
    with "feet" (a leg pair) takes every vertex within `foot_radius` (XY) of one of its feet and
    below `z_top` -- 100% below `z_full`, ramped up to `z_top` -- and nothing else (heat weights
    from one centreline bone would otherwise give the paws to the body);
  * rig_creature.py's generic rigid_regions (horn coils without a bone, a flower on a horn).
The armature carries: forward, locomotion "hop", template "blob", squash_bone "body",
loc_keyed (the follower bones, whose location bird_pose.bake_frames keys), follow_squash and,
when the file gives one, max_joints (asserted by glb_gate on the exported skin).
"""
import json
import os

import bpy
import mathutils

from winged_quadruped import _native_to_normalized_fn, _snap_if_outside  # noqa: F401
from winged_biped import fill_unweighted, dump_axes  # noqa: F401 -- re-exported
from quadruped import force_rigid_to_bone, rigid_regions_normalized  # noqa: F401 -- re-exported

LANDMARKS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "landmarks")


def detect_landmarks_handplaced(obj, H, to_normalized, beast_name):
    from mathutils.bvhtree import BVHTree
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(obj, depsgraph)
    with open(os.path.join(LANDMARKS_DIR, f"{beast_name}.json")) as f:
        data = json.load(f)
    snap_log = []
    o = to_normalized((0.0, 0.0, 0.0))
    k = (to_normalized((0.0, 0.0, 1.0)) - o).z

    def pt(native_xyz, snap=True):
        p = to_normalized(native_xyz)
        if not snap:
            return p
        snapped, moved, exceeded = _snap_if_outside(obj, bvh, p, H)
        if moved > 1e-6:
            snap_log.append((tuple(round(c, 4) for c in p), round(moved, 4), exceeded))
        return snapped

    base = pt(data["body"]["base"], snap=False)
    base.z = 0.0
    top = pt(data["body"]["top"], snap=False)
    parts = []
    for p in data["parts"]:
        q = dict(p)
        q["head"] = pt(p["head"], p.get("snap", True))
        q["tail"] = pt(p["tail"], p.get("snap", True) and p.get("snap_tail", True))
        if "region" in p:  # native -> normalised (same uniform scale); one or several axis limits
            regs = []
            for r in (p["region"] if isinstance(p["region"], list) else [p["region"]]):
                r = dict(r)
                ax = "xyz".index(r["axis"])
                for key in ("lo", "hi"):
                    if r.get(key) is not None:
                        v = [0.0, 0.0, 0.0]
                        v[ax] = r[key]
                        r[key] = to_normalized(v)[ax]
                r["ramp"] = r.get("ramp", 0.1) * k
                regs.append(r)
            q["region"] = regs
        if "feet" in p:
            q["feet"] = [to_normalized((f[0], f[1], 0.0)) for f in p["feet"]]
            q["foot_radius"] = p.get("foot_radius", 0.15) * k
            q["z_full"] = p.get("z_full", 0.1) * k
            q["z_top"] = p.get("z_top", 0.2) * k
        parts.append(q)
    if snap_log:
        print(f"HAND LANDMARKS ({beast_name}): {len(snap_log)} point(s) snapped inside: {snap_log}")
    head = next(p for p in parts if p["name"] == "head")
    forward_sign = -1.0 if head["tail"].y < head["head"].y else 1.0
    return {
        "H": H, "forward_sign": forward_sign, "legs": [], "body_base": base, "body_top": top,
        "parts": parts, "pelvis": base, "chest": top, "head_base": head["head"], "head": head["tail"],
        "head_tip": head["tail"], "tail_tip": top, "notes": data.get("notes", ""),
        "locomotion": data.get("locomotion", "hop"), "max_joints": data.get("max_joints"),
        "rigid_regions": rigid_regions_normalized(data, to_normalized),
    }


def _forward(lm):
    return mathutils.Vector((0.0, lm["forward_sign"], 0.0))


def armature_props(lm):
    follow = [p["name"] for p in lm["parts"] if p.get("follow")]
    props = {"forward": list(_forward(lm)), "locomotion": lm["locomotion"], "hover_offset": 0.0,
             "outline_mask_zero": [], "template": "blob", "squash_bone": "body",
             "follow_squash": follow, "loc_keyed": follow}
    if lm.get("max_joints"):
        props["max_joints"] = int(lm["max_joints"])
    return props


def build_leg_masks(obj, H, legs):
    return {}  # no leg chains (leg pairs are weighted by fix_blob_weights)


def fix_hip_weight_gradient(obj, H, legs, bone_roles, enable_pass1=False):
    return 0, 0


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
        roles[name] = role or name
        return b

    base, top = lm["body_base"], lm["body_top"]
    mk("root", base, base + mathutils.Vector((0, 0, 0.05 * H)), role="root")
    # body: straight up from the floor point, so its local Y is world UP and its scale pivot is on
    # the ground (top only sets the length).
    mk("body", base, mathutils.Vector((base.x, base.y, top.z)), "root", role="body")
    for p in lm["parts"]:
        mk(p["name"], p["head"], p["tail"], p.get("parent", "root"), role=p.get("role", p["name"]))
    for n in names:
        b = eb[n]
        d = (b.tail - b.head).normalized()
        lat_p = LAT - LAT.dot(d) * d
        if lat_p.length < 0.25:
            b.align_roll((UP - UP.dot(d) * d).normalized())
        else:
            b.align_roll(lat_p.normalized().cross(d))
    return names, roles


def _foot_xy_factor(co, p, ramp=0.35):
    """1 inside (1 - ramp) x foot_radius of the nearest foot (XY), fading to 0 at foot_radius -- a
    soft rim, so the leg-pair/body boundary bends over a ring of edges instead of one."""
    r = p["foot_radius"]
    d = min(((co.x - f.x) ** 2 + (co.y - f.y) ** 2) ** 0.5 for f in p["feet"])
    return max(0.0, min(1.0, (r - d) / (ramp * r)))


def fix_blob_weights(obj, bone_roles, lm):
    """See the module docstring. Returns {"rigid": {bone: n}, "leg_pairs": {bone: n}}."""
    me = obj.data
    gi = {g.name: g.index for g in obj.vertex_groups}
    out = {"rigid": {}, "leg_pairs": {}}
    names = {g.index: g.name for g in obj.vertex_groups}
    for p in lm["parts"]:
        if p.get("rigid") and p["name"] in gi:
            idx = gi[p["name"]]
            n = 0
            for vi, v in enumerate(me.vertices):
                if not len(v.groups):
                    continue
                dom = max(v.groups, key=lambda g: g.weight)
                if dom.group != idx:
                    continue
                for g in list(v.groups):
                    if g.group != idx:
                        obj.vertex_groups[g.group].remove([vi])
                obj.vertex_groups[idx].add([vi], 1.0, "REPLACE")
                n += 1
            out["rigid"][p["name"]] = n
    for p in lm["parts"]:
        if "feet" not in p or p["name"] not in gi:
            continue
        idx = gi[p["name"]]
        z0, z1 = p["z_full"], p["z_top"]
        n = 0
        for vi, v in enumerate(me.vertices):
            if v.co.z >= z1:
                continue
            xy = _foot_xy_factor(v.co, p)
            if xy <= 0.0:
                continue
            t = (1.0 if v.co.z <= z0 else (z1 - v.co.z) / max(z1 - z0, 1e-6)) * xy
            cur = {g.group: g.weight for g in v.groups if names[g.group] != p["name"]}
            tot = sum(cur.values())
            for g, w in cur.items():
                nw = w * (1.0 - t) / tot if tot > 0 else 0.0
                if nw < 1e-4:
                    obj.vertex_groups[g].remove([vi])
                else:
                    obj.vertex_groups[g].add([vi], nw, "REPLACE")
            obj.vertex_groups[idx].add([vi], t if tot > 0 else 1.0, "REPLACE")
            n += 1
        out["leg_pairs"][p["name"]] = n
    # Region limits: a part's weight is faded to zero outside its region (heat weighting spreads a
    # face/tuft/crest/leg-pair bone thinly over the whole ball, so the ball would not squash as one
    # piece); the removed weight goes to body. Leg pairs: only their foot regions.
    body = gi.get("body")
    out["region_stripped"] = {}
    for p in lm["parts"]:
        if p["name"] not in gi or ("region" not in p and "feet" not in p):
            continue
        idx = gi[p["name"]]
        n = 0
        for vi, v in enumerate(me.vertices):
            w = next((g.weight for g in v.groups if g.group == idx), 0.0)
            if w <= 0.0:
                continue
            f = 1.0
            for r in p.get("region", []):
                c = v.co["xyz".index(r["axis"])]
                if r.get("lo") is not None:
                    f = min(f, max(0.0, min(1.0, (c - r["lo"]) / max(r["ramp"], 1e-6))))
                if r.get("hi") is not None:
                    f = min(f, max(0.0, min(1.0, (r["hi"] - c) / max(r["ramp"], 1e-6))))
            if "feet" in p:
                f = min(f, _foot_xy_factor(v.co, p) if v.co.z < p["z_top"] else 0.0)
            if f >= 1.0:
                continue
            moved = w * (1.0 - f)
            if w * f < 1e-4:
                obj.vertex_groups[idx].remove([vi])
            else:
                obj.vertex_groups[idx].add([vi], w * f, "REPLACE")
            if body is not None and moved > 0:
                cur = next((g.weight for g in v.groups if g.group == body), 0.0)
                obj.vertex_groups[body].add([vi], cur + moved, "REPLACE")
            n += 1
        out["region_stripped"][p["name"]] = n
    return out
