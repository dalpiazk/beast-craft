"""Hard export gate: parses an exported GLB ITSELF (raw glTF JSON + BIN, numpy only -- no Blender
importer in the loop, which would hide exporter/runtime mismatches behind its own conversions) and
compares every clip's bone poses against the Blender-evaluated "truth" pose captured by
export_glb.py BEFORE any rotation-mode normalisation.

Why this exists (v18 round 5, producer-caught): every shipped GLB since round 16 -- Griffin included
-- had a Move clip whose leg/scapula bones were exported as 2-key CONSTANT rotations. export_glb.py
forces every pose bone to XYZ-Euler (so the keyed clips' Euler leg keys apply), but anim/gait.py
keys those bones as rotation_QUATERNION; in XYZ mode Blender silently ignores quaternion fcurves, so
the exporter wrote the static rest rotation. The old check only counted leg channels (present, but
constant), so it passed. This gate measures MOTION and MATCH instead:

  * per clip, at sampled frames, each deform bone's world-space deformation rotation
    D = R_pose(t) @ R_rest^-1 (rest = the GLB's own static node TRS / Blender's bone rest) must
    match the truth to <= `max_ang_err_deg`, and its head position to <= `max_pos_err` (Blender
    units); D is independent of each bone's local axis convention, so it compares like with like;
  * per clip, per leg, the thigh and shin rotation RANGE (max deformation angle vs the clip's first
    sampled frame) measured in the GLB must match the truth's range to <= `max_ang_err_deg`;
  * Move: each leg's thigh range + shin range must be >= `move_min_leg_range_deg` in the GLB.

Coordinates: glTF is Y-up; Blender is Z-up. The Blender glTF exporter maps (x, y, z)_blender ->
(x, z, -y)_gltf, so C (gltf -> blender) = [[1,0,0],[0,0,-1],[0,1,0]].
"""
import json
import math
import struct

import numpy as np

C = np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]], dtype=np.float64)
_CT = {5126: ("f", 4), 5123: ("H", 2), 5125: ("I", 4), 5121: ("B", 1), 5122: ("h", 2), 5120: ("b", 1)}
_NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def parse_glb(path):
    b = open(path, "rb").read()
    if b[:4] != b"glTF":
        raise ValueError(f"{path}: not a GLB")
    off, js, bin_ = 12, None, None
    while off < len(b):
        ln, typ = struct.unpack_from("<I4s", b, off)
        off += 8
        chunk = b[off:off + ln]
        off += ln
        if typ == b"JSON":
            js = json.loads(chunk)
        elif typ[:3] == b"BIN":
            bin_ = chunk
    return js, bin_


def _accessor(js, bin_, i, cache):
    if i in cache:
        return cache[i]
    a = js["accessors"][i]
    bv = js["bufferViews"][a["bufferView"]]
    fmt, sz = _CT[a["componentType"]]
    n = _NC[a["type"]]
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", sz * n)
    if stride == sz * n:
        arr = np.frombuffer(bin_, dtype="<" + fmt, count=a["count"] * n, offset=start)
        arr = arr.reshape(a["count"], n).astype(np.float64)
    else:
        arr = np.array([struct.unpack_from("<" + fmt * n, bin_, start + k * stride)
                        for k in range(a["count"])], dtype=np.float64)
    cache[i] = arr
    return arr


def _quat_to_mat(q):  # glTF order x, y, z, w
    x, y, z, w = q / np.linalg.norm(q)
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def _slerp(q0, q1, u):
    d = float(np.dot(q0, q1))
    if d < 0:
        q1, d = -q1, -d
    if d > 0.9995:
        q = q0 + u * (q1 - q0)
        return q / np.linalg.norm(q)
    th = math.acos(min(1.0, d))
    return (math.sin((1 - u) * th) * q0 + math.sin(u * th) * q1) / math.sin(th)


def _sample(times, vals, t, interp, is_rot):
    if interp == "CUBICSPLINE":  # [in-tangent, value, out-tangent] triplets -- use the values
        vals = vals[1::3]
    if t <= times[0]:
        return vals[0]
    if t >= times[-1]:
        return vals[-1]
    k = int(np.searchsorted(times, t) - 1)
    if interp == "STEP":
        return vals[k]
    u = (t - times[k]) / (times[k + 1] - times[k])
    if is_rot:
        return _slerp(vals[k], vals[k + 1], u)
    return vals[k] + u * (vals[k + 1] - vals[k])


def _trs(t, r, s):
    m = np.eye(4)
    m[:3, :3] = _quat_to_mat(np.asarray(r, dtype=np.float64)) * np.asarray(s, dtype=np.float64)
    m[:3, 3] = t
    return m


class GlbPose:
    def __init__(self, path):
        self.js, self.bin = parse_glb(path)
        self.cache = {}
        nodes = self.js["nodes"]
        self.names = [n.get("name", f"node{i}") for i, n in enumerate(nodes)]
        self.parent = {}
        for i, n in enumerate(nodes):
            for c in n.get("children", []):
                self.parent[c] = i
        self.static = [(np.array(n.get("translation", [0, 0, 0]), dtype=np.float64),
                        np.array(n.get("rotation", [0, 0, 0, 1]), dtype=np.float64),
                        np.array(n.get("scale", [1, 1, 1]), dtype=np.float64)) for n in nodes]
        self.anims = {a["name"]: a for a in self.js.get("animations", [])}

    def anim_time_range(self, name):
        a = self.anims[name]
        ts = [_accessor(self.js, self.bin, s["input"], self.cache) for s in a["samplers"]]
        return min(float(t[0, 0]) for t in ts), max(float(t[-1, 0]) for t in ts)

    def world(self, anim_name=None, t=0.0):
        """{node_name: 4x4 world matrix in BLENDER (Z-up) coordinates}; anim_name=None = rest."""
        local = [list(x) for x in self.static]
        if anim_name is not None:
            a = self.anims[anim_name]
            for ch in a["channels"]:
                s = a["samplers"][ch["sampler"]]
                times = _accessor(self.js, self.bin, s["input"], self.cache)[:, 0]
                vals = _accessor(self.js, self.bin, s["output"], self.cache)
                path = ch["target"]["path"]
                idx = {"translation": 0, "rotation": 1, "scale": 2}.get(path)
                if idx is None:
                    continue
                local[ch["target"]["node"]][idx] = _sample(
                    times, vals, t, s.get("interpolation", "LINEAR"), path == "rotation")
        out = {}
        memo = {}

        def w(i):
            if i in memo:
                return memo[i]
            m = _trs(*local[i])
            if i in self.parent:
                m = w(self.parent[i]) @ m
            memo[i] = m
            return m
        conv = np.eye(4)
        conv[:3, :3] = C
        for i, nm in enumerate(self.names):
            out[nm] = conv @ w(i) @ np.linalg.inv(conv)
        return out


def _rot_angle(m3):
    u, _, vt = np.linalg.svd(m3)  # strip any scale before measuring
    r = u @ vt
    return math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(r) - 1) / 2))))


def run_gate(glb_path, truth, fps, max_ang_err_deg=2.0, max_pos_err=0.01,
             move_min_leg_range_deg=15.0):
    """truth: {"rest": {bone: 4x4}, "clips": {clip: {"frame_start": f0, "frames": {f: {bone: 4x4}}}}}
    (Blender world matrices, lists). Returns (passed, report)."""
    g = GlbPose(glb_path)
    g_rest = g.world()
    rest = {b: np.array(m) for b, m in truth["rest"].items()}
    bones = [b for b in rest if b in g_rest]
    report = {"clips": {}, "failures": []}
    for clip, cdat in truth["clips"].items():
        if clip not in g.anims:
            report["failures"].append(f"{clip}: animation missing from GLB")
            continue
        t0, _ = g.anim_time_range(clip)
        f0 = cdat["frame_start"]
        worst_ang, worst_pos, worst_where = 0.0, 0.0, None
        d_g, d_t = {}, {}
        bone_worst = {}
        for f, pose in sorted(cdat["frames"].items(), key=lambda kv: float(kv[0])):
            t = t0 + (float(f) - f0) / fps
            gw = g.world(clip, t)
            for b in bones:
                tw = np.array(pose[b])
                dt = tw[:3, :3] @ np.linalg.inv(rest[b][:3, :3])
                dg = gw[b][:3, :3] @ np.linalg.inv(g_rest[b][:3, :3])
                ang = _rot_angle(dg.T @ dt)
                pos = float(np.linalg.norm(gw[b][:3, 3] - tw[:3, 3]))
                d_g.setdefault(b, []).append(dg)
                d_t.setdefault(b, []).append(dt)
                if b not in bone_worst or ang > bone_worst[b][0]:
                    bone_worst[b] = (round(ang, 2), round(pos, 4), f)
                if ang > worst_ang:
                    worst_ang, worst_where = ang, (b, f)
                worst_pos = max(worst_pos, pos)
        leg_ranges = {}
        for b in bones:
            if not b.startswith("leg_") or not (b.endswith("_thigh") or b.endswith("_shin")):
                continue
            rg = max(_rot_angle(m @ d_g[b][0].T) for m in d_g[b])
            rt = max(_rot_angle(m @ d_t[b][0].T) for m in d_t[b])
            leg_ranges[b] = (round(rg, 2), round(rt, 2))
            if abs(rg - rt) > max_ang_err_deg:
                report["failures"].append(f"{clip}: {b} range GLB {rg:.1f} vs Blender {rt:.1f} deg")
        if worst_ang > max_ang_err_deg:
            report["failures"].append(f"{clip}: max bone rotation error {worst_ang:.2f} deg at "
                                      f"{worst_where} (limit {max_ang_err_deg})")
        if worst_pos > max_pos_err:
            report["failures"].append(f"{clip}: max bone position error {worst_pos:.4f} "
                                      f"(limit {max_pos_err})")
        if clip == "Move":
            sides = sorted({b.split("_")[1] for b in leg_ranges})
            for s in sides:
                tot = leg_ranges.get(f"leg_{s}_thigh", (0, 0))[0] + leg_ranges.get(f"leg_{s}_shin", (0, 0))[0]
                if tot < move_min_leg_range_deg:
                    report["failures"].append(f"Move: leg {s} thigh+shin range {tot:.1f} deg < "
                                              f"{move_min_leg_range_deg} in the GLB (legs not stepping)")
        report["clips"][clip] = {"max_rot_err_deg": round(worst_ang, 3),
                                 "max_pos_err": round(worst_pos, 5),
                                 "worst_at": worst_where,
                                 "worst_bones": sorted(bone_worst.items(), key=lambda kv: -kv[1][0])[:6],
                                 "leg_range_glb_vs_blender_deg": leg_ranges}
    return (not report["failures"]), report


if __name__ == "__main__":
    # Standalone: python glb_gate.py EXPORTED.glb glb_gate_truth.json  (written by export_glb.py)
    import sys
    _truth = json.load(open(sys.argv[2]))
    _truth["clips"] = {c: {"frame_start": d["frame_start"],
                           "frames": {float(f): p for f, p in d["frames"].items()}}
                       for c, d in _truth["clips"].items()}
    ok, rep = run_gate(sys.argv[1], _truth, _truth.get("fps", 24))
    for c, r in rep["clips"].items():
        print(f"{c:8s} max_rot_err={r['max_rot_err_deg']:7.2f} deg  max_pos_err={r['max_pos_err']:.4f}")
    print("PASS" if ok else "FAIL:")
    for x in rep["failures"]:
        print("  " + x)
    sys.exit(0 if ok else 1)
