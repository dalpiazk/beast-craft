"""The "serpent" deform-rig template (v20): a legless sea serpent -- the Leviathan -- with an upright
S-curve neck rising from a tail coiled flat on the ground.

Data-driven like the other v19/v20 templates (hand-placed landmarks in
rig_templates/landmarks/<beast>.json, native prepped-mesh coords, snapped inside the mesh) with the
same EXPLICIT roll on every bone: local X = the body's lateral axis LAT = FORWARD x UP (projected
perpendicular to the bone), local Z = X x Y; a bone running along LAT (coil segments on the sides of
the coil) falls back to local Z = UP. Clips use anim/bird_pose.py's semantic channels (pitch about
LAT, turn about UP, bank about FORWARD -- converted through each bone's rest matrix) so they do not
depend on the roll; bird_pose.verify_axes is the axis dump.

Deform bones (Leviathan: 22):
  root          on the ground under the coil base (the support -- never lifted by Move),
  pelvis        root -> coil base (body_chain[0]): the shared parent of the neck and the coil,
  body_01..NN   up the S-neck (body_chain points; NN = len - 1),
  head          top of the neck -> skull centre; snout (rigid upper jaw) and jaw off it,
  frill         optional soft crest bone (head -> crest top),
  tail_01..NN   around the flat coil (tail points; tail[0] == the coil base),
  fin_<L|R>     one bone per side fin, parented to the body bone nearest the fin root.
The armature carries "locomotion": "slither" (gait.py's slither Move; verify.py/glb_gate.py's
body-wave gates and skipped foot gates).
"""
import json
import os

import bpy
import mathutils

from winged_quadruped import _native_to_normalized_fn, _snap_if_outside  # noqa: F401
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
    body = [pt(p) for p in data["body_chain"]]
    tail = [pt(p) for p in data["tail"]]
    head = pt(spine["head"])
    forward_sign = -1.0 if spine["snout_tip"][1] < spine["head"][1] else 1.0
    fins = [{"side": f["side"], "root": pt(f["root"]), "tip": pt(f["tip"])} for f in data.get("fins", [])]
    frill = None
    if "frill" in data:
        frill = {"base": pt(data["frill"]["base"]), "tip": pt(data["frill"]["tip"])}
    if snap_log:
        print(f"HAND LANDMARKS ({beast_name}): {len(snap_log)} point(s) snapped inside: {snap_log}")
    snout = pt(spine["snout_tip"])
    return {
        "H": H, "forward_sign": forward_sign, "legs": [], "body_points": body, "tail_points": tail,
        "fins": fins, "frill": frill, "pelvis": body[0], "chest": body[len(body) // 2],
        "head_base": body[-1], "head": head, "snout_tip": snout, "jaw_tip": pt(spine["jaw_tip"]),
        "head_tip": snout, "tail_tip": tail[-1], "notes": data.get("notes", ""),
        "locomotion": data.get("locomotion", "slither"),
    }


def _forward(lm):
    return mathutils.Vector((0.0, lm["forward_sign"], 0.0))


def armature_props(lm):
    return {"forward": list(_forward(lm)), "locomotion": lm["locomotion"], "hover_offset": 0.0,
            "outline_mask_zero": [], "template": "serpent"}


def build_leg_masks(obj, H, legs):
    return {}  # legless


def fix_hip_weight_gradient(obj, H, legs, bone_roles, enable_pass1=False):
    return 0, 0  # legless: no leg pairs to separate


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

    body = lm["body_points"]
    base = body[0]
    root = mk("root", mathutils.Vector((base.x, base.y, 0.0)),
              mathutils.Vector((base.x, base.y, 0.5 * base.z)), role="root")
    mk("pelvis", root.tail.copy(), base, "root", role="pelvis")
    prev = "pelvis"
    for i in range(1, len(body)):
        mk(f"body_{i:02d}", body[i - 1], body[i], prev, role="body")
        prev = f"body_{i:02d}"
    mk("head", body[-1], lm["head"], prev, role="head")
    mk("snout", lm["head"], lm["snout_tip"], "head", role="head")
    mk("jaw", lm["head"], lm["jaw_tip"], "head", role="jaw")
    if lm.get("frill"):
        mk("frill", lm["frill"]["base"], lm["frill"]["tip"], "head", role="frill")
    tail = lm["tail_points"]
    prev = "pelvis"
    for i in range(1, len(tail)):
        mk(f"tail_{i:02d}", tail[i - 1], tail[i], prev, role="tail")
        prev = f"tail_{i:02d}"
    for f in lm["fins"]:
        # parent: the body bone whose segment is nearest the fin root
        best = min(range(1, len(body)), key=lambda i: _seg_dist(f["root"], body[i - 1], body[i]))
        mk(f"fin_{f['side']}", f["root"], f["tip"], f"body_{best:02d}", role=f"fin_{f['side']}")
    for n in names:
        b = eb[n]
        d = (b.tail - b.head).normalized()
        lat_p = LAT - LAT.dot(d) * d
        if lat_p.length < 0.25:
            b.align_roll((UP - UP.dot(d) * d).normalized())
        else:
            b.align_roll(lat_p.normalized().cross(d))
    return names, roles


def fix_serpent_weights(obj, bone_roles, lm, reach=2):
    """v20 (Leviathan). Heat weighting bleeds across things that touch but move independently:
    the neck base sits right beside the first coil loop and the coil's own loops lie next to each
    other, so vertices end up blending bones that are far apart ALONG the body (e.g. tail_02 with
    body_03, or tail_01 with tail_06) -- the slither wave rotates those differently every frame
    and the skin between them tears. The main chain is ordered tail_NN..tail_01, pelvis,
    body_01..NN, head; each vertex keeps only the chain bones within `reach` links of its own
    dominant chain bone (fins/jaw/snout/frill untouched); the removed weight is renormalised later.
    Returns {"chain_bleed_vertices": n}."""
    gi = {g.name: g.index for g in obj.vertex_groups}
    n_tail = len(lm["tail_points"]) - 1
    n_body = len(lm["body_points"]) - 1
    chain = ([f"tail_{i:02d}" for i in range(n_tail, 0, -1)] + ["pelvis"]
             + [f"body_{i:02d}" for i in range(1, n_body + 1)] + ["head"])
    pos = {gi[n]: k for k, n in enumerate(chain) if n in gi}
    changed = 0
    for vi, v in enumerate(obj.data.vertices):
        cw = [(g.group, g.weight) for g in v.groups if g.group in pos and g.weight > 1e-5]
        if len(cw) < 2:
            continue
        dom = max(cw, key=lambda t: t[1])[0]
        drop = [g for g, _ in cw if abs(pos[g] - pos[dom]) > reach]
        for g in drop:
            obj.vertex_groups[g].remove([vi])
        if drop:
            changed += 1
    return {"chain_bleed_vertices": changed}
