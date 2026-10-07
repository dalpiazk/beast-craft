"""v22 round 3 (producer review: the Archer's bow): places the bow, its string and the arrow every
frame. Shared by anim/keyed.py (inside the bake, from each clip's "_bow" channels) and anim/gait.py
(a pass over the baked Move).

A rig opts in through the armature property "bow_rig" (rig_templates/biped_arms.py, from the landmark
file): {"bow", "string", "arrow" (bone names), "bow_hand", "draw_hand", "grip_t", "arrow_rest"}.
  bow     child of the bow hand. Keyed every frame so its GRIP sits in the bow fist (the point
          grip_t along the hand bone) and the bow stands upright: limbs along the aim's "up", belly
          facing the aim direction. The prop is modelled at a rest placement beside the Archer
          (meshfix/archer_bow.py: grip centre at the bone head, limbs along +Z, belly toward -Y).
  string  child of the bow, at the string's middle (the nock). Its location carries the nock
          `draw` of the way to the draw-hand fist; the string's skin weight ramps linearly from the
          limb tips (bow) to the nock (string), so a draw pulls the string into a clean V from both
          tips to the hand.
  arrow   child of root. Its nock sits on the string's nock point and its shaft passes through the
          arrow rest beside the grip, so it lies nocked on the string, rests on the bow hand and
          follows the draw hand. `hide` scales it to nothing about the nock (the game spawns the
          projectile at the arrow_release marker; the arrow grows back, nocked, before the clip ends).
Channels (bird_pose "_bow" pseudo-bone, sampled and eased like any key-pose channel):
  yaw, pitch   aim direction: FORWARD turned by yaw about UP, then tipped up by pitch (deg); while
               drawn it blends (by `draw`) toward the line from the draw fist through the bow fist;
  cant         bow tilt about the aim (deg); draw (0..1); hide (0..1);
  drop         (0..1, KO) the bow leaves the fist and falls flat onto the floor at cfg["drop"].
"""
import json
import math

import bpy
from mathutils import Matrix, Vector

UP = Vector((0.0, 0.0, 1.0))


def cfg_of(arm_obj):
    raw = arm_obj.data.get("bow_rig")
    return json.loads(raw) if raw else None


def _aim(fwd, yaw, pitch):
    A = Matrix.Rotation(math.radians(yaw), 3, UP) @ fwd
    return (A * math.cos(math.radians(pitch)) + UP * math.sin(math.radians(pitch))).normalized()


def _up(A, cant):
    U = (UP - A * UP.dot(A)).normalized()
    return (Matrix.Rotation(math.radians(cant), 3, A) @ U).normalized()


def _frame(dirv, up):
    """rotation taking the rest axes (-Y = dirv, +Z = up) to the posed ones"""
    up = (up - dirv * up.dot(dirv)).normalized()
    y = -dirv
    x = y.cross(up).normalized()
    return Matrix((x, y, up)).transposed()


def place(arm_obj, fwd, ch, cfg=None):
    cfg = cfg or cfg_of(arm_obj)
    pbs = arm_obj.pose.bones
    bow, sb, ar = pbs[cfg["bow"]], pbs[cfg["string"]], pbs[cfg["arrow"]]
    bpy.context.view_layer.update()
    hand, draw_hand = pbs[cfg["bow_hand"]], pbs[cfg["draw_hand"]]
    gt = cfg.get("grip_t", 0.6)
    F = hand.head.lerp(hand.tail, gt)
    D = draw_hand.head.lerp(draw_hand.tail, gt)
    draw = max(0.0, min(1.0, ch.get("draw", 0.0)))
    A = _aim(fwd, ch.get("yaw", 0.0), ch.get("pitch", 0.0))
    if draw > 0.0 and (F - D).length > 1e-6:
        # while drawn the bow faces along the draw: from the draw fist through the bow fist, so the
        # arrow lies in the bow's plane, square to the limbs, and the string's V is symmetric
        A = A.lerp((F - D).normalized(), draw).normalized()
    U = _up(A, ch.get("cant", 0.0))
    G0 = bow.bone.head_local
    # round 4: the bow is modelled (and its bone rests) yawed by rest_yaw about the vertical
    RY_inv = Matrix.Rotation(math.radians(-cfg.get("rest_yaw", 0.0)), 4, "Z")
    R = _frame(A, U).to_4x4() @ RY_inv
    Mb = Matrix.Translation(F) @ R @ Matrix.Translation(-G0)
    drop = max(0.0, min(1.0, ch.get("drop", 0.0)))
    if drop > 0.0 and cfg.get("drop"):
        # KO: the bow leaves the fist and falls clear, ending flat on the floor at cfg["drop"]["at"]
        # (world) with its limbs along "limbs" and its belly toward "belly"; a small toss arc.
        dc = cfg["drop"]
        Mg = (Matrix.Translation(Vector(dc["at"])) @ _frame(Vector(dc["belly"]).normalized(),
                                                            Vector(dc["limbs"]).normalized()).to_4x4()
              @ RY_inv @ Matrix.Translation(-G0))
        la, ra, _s = (Mb @ Matrix.Translation(G0)).decompose()
        lg, rg, _s = (Mg @ Matrix.Translation(G0)).decompose()
        loc = la.lerp(lg, drop) + UP * (dc.get("arc", 0.15) * 4.0 * drop * (1.0 - drop))
        Mb = Matrix.Translation(loc) @ ra.slerp(rg, drop).to_matrix().to_4x4() @ Matrix.Translation(-G0)
    bow.matrix = Mb @ bow.bone.matrix_local
    bpy.context.view_layer.update()
    M0 = sb.bone.head_local
    nock_rest = Mb @ M0
    nock = nock_rest.lerp(D, draw)
    sb.matrix = Matrix.Translation(nock - nock_rest) @ Mb @ sb.bone.matrix_local
    bpy.context.view_layer.update()
    rest_pt = Mb @ Vector(cfg["arrow_rest"])
    d = rest_pt - nock
    d = d.normalized() if d.length > 1e-6 else A
    s = max(1e-3, 1.0 - max(0.0, min(1.0, ch.get("hide", 0.0))))
    N0 = ar.bone.head_local
    Ma = (Matrix.Translation(nock) @ _frame(d, U).to_4x4() @ Matrix.Diagonal((s, s, s, 1.0))
          @ Matrix.Translation(-N0))
    ar.matrix = Ma @ ar.bone.matrix_local
    bpy.context.view_layer.update()


def channels(c):
    """the "_bow" channels out of a bird_pose channel dict"""
    return {k[2]: v for k, v in c.items() if k[0] == "b" and k[1] == "_bow"}


def post_action(arm_obj, scene, action, fwd, ch_fn, log=print, mesh_obj=None, loop=True):
    """re-place the bow/string/arrow on every frame of an already-baked action (anim/gait.py's Move)
    and key their rotation, location and scale (round 4: with the auto_clear offsets when the rig
    opts in and mesh_obj is given)."""
    cfg = cfg_of(arm_obj)
    names = [cfg["bow"], cfg["string"], cfg["arrow"]]
    f0, f1 = [int(round(x)) for x in action.frame_range]
    nfr = max(1, f1 - f0)
    snaps = {}
    for f in range(f0, f1 + 1):
        arm_obj.animation_data.action = action
        scene.frame_set(f)
        snaps[f] = {pb.name: pb.matrix_basis.copy() for pb in arm_obj.pose.bones}
    arm_obj.animation_data.action = None
    offs = None
    if cfg.get("auto_clear") and mesh_obj is not None:
        def pose_fn(t):
            f = f0 + int(round(t * nfr))
            for pb in arm_obj.pose.bones:
                pb.matrix_basis = snaps[f][pb.name]
            ch = ch_fn(t)
            place(arm_obj, fwd, ch, cfg)
            return ch
        offs = solve_clearance(arm_obj, mesh_obj, fwd, nfr, pose_fn, loop=loop, log=log, name=action.name)
    out = {}
    for f in range(f0, f1 + 1):
        for pb in arm_obj.pose.bones:
            pb.matrix_basis = snaps[f][pb.name]
        t = (f - f0) / nfr
        ch = dict(ch_fn(t))
        if offs:
            for k, o in offs(t).items():
                ch[k] = ch.get(k, 0.0) + o
        place(arm_obj, fwd, ch, cfg)
        out[f] = {}
        for n in names:
            pb = arm_obj.pose.bones[n]
            pb.rotation_mode = "XYZ"
            out[f][n] = (pb.rotation_euler.copy(), pb.location.copy(), pb.scale.copy())
    arm_obj.animation_data.action = action
    prev = {}
    for f, per in out.items():
        for n, (r, l_, s) in per.items():
            if n in prev:
                import bird_pose as _BPm
                r = _BPm.euler_near(r, prev[n])
            prev[n] = r
            pb = arm_obj.pose.bones[n]
            pb.rotation_euler, pb.location, pb.scale = r, l_, s
            pb.keyframe_insert(data_path="rotation_euler", frame=f)
            pb.keyframe_insert(data_path="location", frame=f)
            pb.keyframe_insert(data_path="scale", frame=f)
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                cb = strip.channelbag(slot)
                if cb:
                    for fc in cb.fcurves:
                        if any(f'pose.bones["{n}"]' in fc.data_path for n in names):
                            for kp in fc.keyframe_points:
                                kp.interpolation = "LINEAR"
    log(f"BOW RIG {action.name}: {len(out)} frames placed")


# ---- v22 round 4: automatic per-frame clearance ------------------------------------------------
# The bow is placed from channels, so it can be kept out of the body by adjusting ITS OWN channels
# (cant about the aim, then yaw) instead of the arm -- the string and arrow stay consistent because
# place() derives them from the same channels. Opt-in: armature property bow_rig["auto_clear"]
# {"cant": [offsets...], "yaw": [offsets...], "dilate": frames, "smooth": passes}.
def _probers(arm_obj, mesh_obj):
    import prop_clear as _PC
    gates = json.loads(arm_obj.data.get("prop_gate", "[]"))
    return [_PC.Prober(arm_obj, mesh_obj, g) for g in gates if g["name"] in ("bow", "arrow")]


def _hits(probers):
    return sum(pr.depth(0.0105)[0] for pr in probers)


def solve_clearance(arm_obj, mesh_obj, fwd, n, pose_fn, loop=False, log=print, name=""):
    """pose_fn(t) poses the whole rig for clip fraction t (channels applied, post incl. place()) and
    returns its "_bow" channels. Returns offs(t) -> {channel: offset} (smoothed per-frame offsets
    that clear the bow/arrow), or None when no frame needs one."""
    cfg = cfg_of(arm_obj)
    ac = cfg.get("auto_clear") or {}
    probers = _probers(arm_obj, mesh_obj)
    cands = [("cant", o) for o in ac.get("cant", [])] + [("yaw", o) for o in ac.get("yaw", [])]
    raw = []
    hit_frames, unsolved, hit_list = 0, [], []
    for i in range(n + 1):
        t = i / n
        ch = pose_fn(t)
        rec = {}
        if _hits(probers):
            hit_frames += 1
            hit_list.append(i)
            best = None
            for k, o in cands:
                c2 = dict(ch); c2[k] = c2.get(k, 0.0) + o
                place(arm_obj, fwd, c2, cfg)
                if not _hits(probers):
                    best = (k, o); break
            if best:
                rec[best[0]] = best[1]
            else:
                unsolved.append(i)
            place(arm_obj, fwd, ch, cfg)
        raw.append(rec)
    if not hit_frames:
        log(f"BOW CLEAR {name}: no frame needs a correction")
        return None
    keys = sorted({k for r in raw for k in r})
    N = n + 1
    d = int(ac.get("dilate", 2))
    idx = (lambda j: j % N) if loop else (lambda j: max(0, min(N - 1, j)))
    req = {k: [r.get(k, 0.0) for r in raw] for k in keys}

    def smooth(v):
        dil = [max((v[idx(j)] for j in range(i - d, i + d + 1)), key=abs) for i in range(N)]
        for _ in range(int(ac.get("smooth", 2)) + 1):
            sm = [(dil[idx(i - 1)] + 2 * dil[i] + dil[idx(i + 1)]) / 4.0 for i in range(N)]
            dil = [s_ if abs(s_) >= abs(r_) else r_ for s_, r_ in zip(sm, v)]  # never under the need
        if loop:
            dil[-1] = dil[0]
        return dil
    # smooth -> verify every frame -> raise the per-frame requirement where it still hits -> repeat,
    # so a fix is always eased in/out over neighbouring frames (no single-frame pops)
    still = []
    for _pass in range(6):
        out = {k: smooth(req[k]) for k in keys}
        still, changed = [], False
        for i in range(N):
            ch = pose_fn(i / n)
            c2 = dict(ch)
            for k, arr in out.items():
                c2[k] = c2.get(k, 0.0) + arr[i]
            place(arm_obj, fwd, c2, cfg)
            if not _hits(probers):
                continue
            fixed = False
            for k, o in cands:
                c3 = dict(c2); c3[k] = c3.get(k, 0.0) + o
                place(arm_obj, fwd, c3, cfg)
                if not _hits(probers):
                    if k not in req:
                        req[k] = [0.0] * N; keys.append(k)
                    req[k][i] = out.get(k, [0.0] * N)[i] + o; fixed = changed = True; break
            if not fixed:
                still.append(i)
        if not changed:
            break
    out = {k: smooth(req[k]) for k in keys}

    def frame_hits(i):
        ch = pose_fn(i / n); c2 = dict(ch)
        for k, arr in out.items():
            c2[k] = c2.get(k, 0.0) + arr[i]
        place(arm_obj, fwd, c2, cfg)
        return _hits(probers), c2
    # final pass on the RETURNED offsets: a frame that still hits gets the smallest fix, tapered over
    # +-2 frames, and its neighbourhood is re-checked
    still = []
    for _final in range(4):
        bad = [i for i in range(N) if frame_hits(i)[0]]
        if not bad:
            still = []; break
        still = []
        for i in bad:
            h, c2 = frame_hits(i)
            if not h:
                continue
            done = False
            for k, o in cands:
                c3 = dict(c2); c3[k] = c3.get(k, 0.0) + o
                place(arm_obj, fwd, c3, cfg)
                if not _hits(probers):
                    out.setdefault(k, [0.0] * N)
                    if k not in keys:
                        keys.append(k)
                    for j in range(i - 2, i + 3):
                        jj = idx(j)
                        out[k][jj] += o * (1.0 - abs(j - i) / 3.0)
                    done = True; break
            if not done:
                still.append(i)
        if loop:
            for k in keys:
                out[k][-1] = out[k][0]
    unsolved = still
    log(f"BOW CLEAR {name}: base hits on frames {hit_list}; passes {_pass + 1}")
    for k in keys:
        log(f"BOW CLEAR {name}: {k} offsets " + " ".join(f"{x:.0f}" for x in out[k]))
    log(f"BOW CLEAR {name}: {hit_frames}/{N} frames hit; offsets {', '.join(f'{k} max {max(map(abs, out[k])):.0f}' for k in keys)}"
        + (f"; UNSOLVED frames {unsolved}" if unsolved else ""))

    def offs(t):
        x = t * n; i0 = int(math.floor(x)); i1 = min(n, i0 + 1); u = x - i0
        return {k: arr[min(n, i0)] * (1 - u) + arr[i1] * u for k, arr in out.items()}
    return offs
