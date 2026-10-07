"""v22 round 2 (producer review: the Shaman's staff clipped through his body): keep a held prop out
of the body, IN THE ANIMATION. Shared by anim/gait.py (Move) and anim/keyed.py (every keyed clip).

A rig opts in through two armature properties written by rig_creature.py / the template:
  prop_gate   [{name, verts, grip_bones, grip_radius}] -- the prop's vertex set (verify.py's
              prop_clearance gate reads the same record);
  prop_clear  {"prop": name, "adjust": [[bone, channel, deg], ...], "tol": depth}.
`adjust` is ONE direction of correction (or "options": several, the best one per clip is used),
scaled by s in [0, 1] and applied on top of the baked pose,
parent bones first: "abd" rotates about FORWARD (mirrored per side, bird_pose's abduction axis),
"flex" about the bone's own posed local X, "turn" about UP. For the Shaman it abducts the shoulder a
little and tilts the staff outward at the wrist (the fist stays round the staff: it is rigid to the
hand), so the staff's head swings out past the hood and antlers instead of through them.

fix(action): pass 1 finds, per baked frame, the smallest s that leaves no prop vertex deeper than
`tol` inside the rest of the mesh (bisection); s is then dilated over +-2 frames and smoothed so the
correction eases in and out (no pop, no jitter), and pass 2 writes the corrected rotations as keys.
Frames that need no correction keep s = 0, so the clip is unchanged where it was already clean.
"""
import json
import math

import bpy
import mathutils
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

UP = Vector((0.0, 0.0, 1.0))


def _side(name):
    return -1.0 if "_L_" in name or name.endswith("_L") else 1.0


class Prober:
    def __init__(self, arm_obj, mesh_obj, gate):
        self.arm, self.mesh, self.g = arm_obj, mesh_obj, gate
        pset = set(gate["verts"])
        polys = [tuple(p.vertices) for p in mesh_obj.data.polygons]
        rest = [p for p in polys if not any(i in pset for i in p)]
        par = list(range(len(mesh_obj.data.vertices)))

        def find(x):
            while par[x] != x:
                par[x] = par[par[x]]
                x = par[x]
            return x
        for p in rest:
            for i in p[1:]:
                a, b = find(p[0]), find(i)
                if a != b:
                    par[a] = b
        comps = {}
        for p in rest:
            comps.setdefault(find(p[0]), []).append(p)
        if gate.get("attached"):  # same rule as verify.py's prop_clearance
            own = {find(i) for p in polys if any(j in pset for j in p) for i in p if i not in pset}
            comps = {k: v for k, v in comps.items() if k not in own}
        self.comps = [c for c in comps.values() if len(c) >= 8]
        self.dirs = [Vector(d).normalized() for d in ((1, 0.013, 0.007), (-0.011, 1, 0.017), (0.009, -0.015, 1))]

    def depth(self, tol):
        """(number of prop vertices deeper than tol inside another closed component, max depth)."""
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        eo = self.mesh.evaluated_get(dg)
        me = eo.to_mesh()
        V = [eo.matrix_world @ v.co for v in me.vertices]
        eo.to_mesh_clear()
        segs = [(self.arm.matrix_world @ self.arm.pose.bones[b].head, self.arm.matrix_world @ self.arm.pose.bones[b].tail)
                for b in self.g["grip_bones"]]
        trees = [BVHTree.FromPolygons(V, c) for c in self.comps]
        n, worst = 0, 0.0
        for i in self.g["verts"]:
            q = V[i]
            if any((q - (h + (t - h) * max(0.0, min(1.0, (q - h).dot(t - h) / max((t - h).length_squared, 1e-12))))).length
                   < self.g["grip_radius"] for h, t in segs):
                continue
            for tr in trees:
                loc, _n, _i, d = tr.find_nearest(q)
                if loc is None or d <= tol:
                    continue
                votes = 0
                for dv in self.dirs:
                    o, c = q.copy(), 0
                    for _ in range(40):
                        hit = tr.ray_cast(o, dv)
                        if hit[0] is None:
                            break
                        c += 1
                        o = hit[0] + dv * 1e-5
                    votes += c % 2
                if votes >= 2:
                    n += 1
                    worst = max(worst, d)
                    break
        return n, worst


def apply_delta(arm_obj, fwd, adjust, s):
    """Rotate each listed bone by s * deg on top of its current pose (about its head)."""
    for bone, ch, deg in adjust:
        pb = arm_obj.pose.bones[bone]
        M = pb.matrix.copy()
        h = M.translation.copy()
        if ch == "abd":
            if pb.parent:
                P = pb.parent.matrix.to_3x3().normalized() @ pb.parent.bone.matrix_local.to_3x3().normalized().inverted()
            else:
                P = Matrix.Identity(3)
            axis = (P @ (fwd * _side(bone))).normalized()
        elif ch == "flex":
            axis = M.to_3x3().normalized().col[0].normalized()
        else:  # "turn"
            axis = UP
        R = Matrix.Rotation(math.radians(deg * s), 4, axis)
        pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ M
        bpy.context.view_layer.update()


def fix(arm_obj, mesh_obj, scene, action, fwd, spec=None, log=print):
    """See the module docstring. Returns {"frames_corrected", "s_max", "residual"}."""
    data = arm_obj.data
    if spec is None:
        if "prop_clear" not in data:
            return None
        spec = json.loads(data["prop_clear"])
    gate = next(g for g in json.loads(data["prop_gate"]) if g["name"] == spec["prop"])
    tol = spec.get("tol", 0.006)
    adjust = spec.get("adjust") or spec["options"][0]
    pr = Prober(arm_obj, mesh_obj, gate)
    arm_obj.animation_data.action = action
    f0, f1 = [int(round(x)) for x in action.frame_range]
    frames = list(range(f0, f1 + 1))

    def pose_at(f):
        arm_obj.animation_data.action = action
        scene.frame_set(f)
        snap = {pb.name: pb.matrix_basis.copy() for pb in arm_obj.pose.bones}
        arm_obj.animation_data.action = None
        for pb in arm_obj.pose.bones:
            pb.matrix_basis = snap[pb.name]
        bpy.context.view_layer.update()
        return snap

    def restore(snap):
        for pb in arm_obj.pose.bones:
            pb.matrix_basis = snap[pb.name]
        bpy.context.view_layer.update()

    def solve(adj):
        """per-frame smallest s clearing the frame (or the s leaving the fewest hits) + unclear count"""
        need, bad = {}, 0
        for f in frames:
            snap = pose_at(f)
            n, _w = pr.depth(tol)
            if n == 0:
                need[f] = 0.0
                continue
            lo, hi = 0.0, 1.0
            restore(snap)
            apply_delta(arm_obj, fwd, adj, 1.0)
            if pr.depth(tol)[0]:
                # the correction alone cannot clear this frame: take the s that leaves the least
                best = (n, 0.0)
                for s_ in (0.25, 0.5, 0.75, 1.0):
                    restore(snap)
                    apply_delta(arm_obj, fwd, adj, s_)
                    best = min(best, (pr.depth(tol)[0], s_))
                need[f] = best[1]
                bad += best[0]
                continue
            for _ in range(7):
                mid = (lo + hi) / 2
                restore(snap)
                apply_delta(arm_obj, fwd, adj, mid)
                if pr.depth(tol)[0]:
                    lo = mid
                else:
                    hi = mid
            need[f] = hi
        return need, bad

    # "options": alternative correction directions; the one that leaves the fewest hits over the
    # whole clip (then the smallest total correction) is used for the clip -- one direction per
    # clip, so the correction never switches direction mid-clip.
    best = None
    for k, adj in enumerate(spec.get("options") or [adjust]):
        need_k, bad_k = solve(adj)
        key = (bad_k, sum(need_k.values()))
        if best is None or key < best[0]:
            best = (key, k, adj, need_k)
        if bad_k == 0 and not any(need_k.values()):
            break
    _key, opt_k, adjust, need = best
    bones = [b for b, _c, _d in adjust]
    # dilate +-2 frames, then a 5-tap smooth that never drops below the need
    dil = {f: max(need[g] for g in frames if abs(g - f) <= 2) for f in frames}
    sm = {}
    for f in frames:
        w = [dil[g] for g in frames if abs(g - f) <= 2]
        sm[f] = max(need[f], sum(w) / len(w))
    if any(f1 == g for g in frames) and spec.get("loop"):
        sm[f1] = sm[f0] = max(sm[f0], sm[f1])
    rots, resid = {}, 0
    for f in frames:
        snap = pose_at(f)
        if sm[f] > 0:
            apply_delta(arm_obj, fwd, adjust, sm[f])
        n, _w = pr.depth(tol * 1.5)
        resid = max(resid, n)
        rots[f] = {}
        for b in bones:
            pb = arm_obj.pose.bones[b]
            pb.rotation_mode = "XYZ"
            rots[f][b] = pb.rotation_euler.copy()
        restore(snap)
    arm_obj.animation_data.action = action
    prev = {}
    for f in frames:
        for b in bones:
            pb = arm_obj.pose.bones[b]
            e = rots[f][b]
            if b in prev:
                e = e.copy()
                e.make_compatible(prev[b])
            prev[b] = e
            pb.rotation_euler = e
            pb.keyframe_insert(data_path="rotation_euler", frame=f)
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if cb:
                    for fc in cb.fcurves:
                        if any(fc.data_path == f'pose.bones["{b}"].rotation_euler' for b in bones):
                            for kp in fc.keyframe_points:
                                kp.interpolation = "LINEAR"
    nfix = sum(1 for f in frames if sm[f] > 0)
    log(f"PROP CLEAR {action.name}: option {opt_k} {adjust}, {nfix}/{len(frames)} frames corrected, "
        f"s max {max(sm.values()):.3f}, residual hits {resid}")
    return {"frames_corrected": nfix, "s_max": round(max(sm.values()), 3), "residual": resid}
