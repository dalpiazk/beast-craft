"""python fillholes_glb.py IN.glb OUT.glb [max_loop_z]
Minimal hole repair directly on a shipped GLB: finds the mesh's open boundary loops (vertices welded
by position, the glTF seam split undone only for the analysis), keeps loops whose vertices all lie
below `max_loop_z` (the ground-sheet removal only cut faces near the floor), triangulates each loop
by ear clipping in its best-fit plane, oriented consistently with the neighbouring faces, and
APPENDS those triangles to the primitive's index buffer. Existing vertices are never moved; the fill
uses its own copies of the rim vertices (same position and joints/weights -- weighted from their
neighbours -- flat patch normal, one UV per patch). The skeleton and every animation channel are
untouched (byte-identical JSON/BIN for them). Every modified attribute/index accessor is given a
freshly appended bufferView, so the binary chunk is repacked at the end (`repack_buffer`) to drop the
superseded bufferViews it leaves orphaned -- without this, the old, now-unreferenced copies of that
data stay physically embedded in the output file, roughly doubling its size for no reason."""
import json
import os
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import glb_gate  # noqa: E402

src, dst = sys.argv[1:3]
MAXZ = float(sys.argv[3]) if len(sys.argv) > 3 else 0.2
raw = open(src, "rb").read()
js, binb = glb_gate.parse_glb(src)
cache = {}
prim = js["meshes"][0]["primitives"][0]
P = glb_gate._accessor(js, binb, prim["attributes"]["POSITION"], cache) @ glb_gate.C.T
acc = js["accessors"][prim["indices"]]
I = glb_gate._accessor(js, binb, prim["indices"], cache).astype(np.int64).reshape(-1, 3)
key = np.round(P / 1e-5).astype(np.int64)
_, W = np.unique(key, axis=0, return_inverse=True)
W = W.reshape(-1)

und = {}
for f in I:
    for a, b in ((f[0], f[1]), (f[1], f[2]), (f[2], f[0])):
        wa, wb = W[a], W[b]
        if wa == wb:
            continue
        k = (min(wa, wb), max(wa, wb))
        und.setdefault(k, []).append((a, b))
bnd = [v[0] for k, v in und.items() if len(v) == 1]
print(f"boundary edges before: {len(bnd)}")
# fill loop runs each boundary edge REVERSED (b -> a), so the new faces wind like their neighbours.
# A welded vertex can carry two loops (a pinch), so edges are walked as a multigraph: from an unused
# edge, keep taking an unused outgoing edge until back at the start vertex.
out_e, orig = {}, {}
for a, b in bnd:
    out_e.setdefault(W[b], []).append(W[a])
    orig.setdefault(W[b], b)
    orig.setdefault(W[a], a)
used = set()
loops = []
for s0 in list(out_e):
    for t0 in list(out_e[s0]):
        if (s0, t0) in used:
            continue
        loop, v, nx = [s0], s0, t0
        used.add((s0, t0))
        while nx != s0:
            loop.append(nx)
            cand = [w for w in out_e.get(nx, []) if (nx, w) not in used]
            if not cand:
                break
            used.add((nx, cand[0]))
            v, nx = nx, cand[0]
        if nx == s0 and len(loop) >= 3:
            loops.append(loop)
Pw = {}
for i, w in enumerate(W):
    Pw.setdefault(w, P[i])


def ear_clip(loop):
    pts = np.array([Pw[w] for w in loop])
    c = pts.mean(0)
    u, s, vt = np.linalg.svd(pts - c)
    n = vt[2]
    # orientation: the polygon's own normal from Newell's method; make 2-D projection CCW wrt n
    newell = np.zeros(3)
    for i in range(len(pts)):
        p, q = pts[i], pts[(i + 1) % len(pts)]
        newell += np.cross(p, q)
    if newell.dot(n) < 0:
        n = -n
    e1 = vt[0]
    e2 = np.cross(n, e1)
    q = np.stack([(pts - c) @ e1, (pts - c) @ e2], 1)
    idx = list(range(len(loop)))
    tris = []

    def cross2(a, b, cc):
        return (b[0] - a[0]) * (cc[1] - a[1]) - (b[1] - a[1]) * (cc[0] - a[0])

    guard = 0
    while len(idx) > 3 and guard < 10000:
        guard += 1
        best = None
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            if cross2(q[i0], q[i1], q[i2]) <= 1e-12:
                continue
            ok = True
            for j in idx:
                if j in (i0, i1, i2):
                    continue
                if (cross2(q[i0], q[i1], q[j]) >= 0 and cross2(q[i1], q[i2], q[j]) >= 0
                        and cross2(q[i2], q[i0], q[j]) >= 0):
                    ok = False
                    break
            if ok:
                # prefer the fattest ear (better triangles)
                area = cross2(q[i0], q[i1], q[i2])
                if best is None or area > best[0]:
                    best = (area, k)
        if best is None:  # non-simple projection: fall back to a fan for the rest
            for k in range(1, len(idx) - 1):
                tris.append((idx[0], idx[k], idx[k + 1]))
            idx = []
            break
        k = best[1]
        tris.append((idx[k - 1], idx[k], idx[(k + 1) % len(idx)]))
        idx.pop(k)
    if len(idx) == 3:
        tris.append(tuple(idx))
    return [(loop[a], loop[b], loop[c_]) for a, b, c_ in tris]


new = []
report = []
dup_src, dup_nrm, dup_uv_src = [], [], []   # per new vertex: source vertex, flat normal, UV source
for loop in loops:
    zs = [Pw[w][2] for w in loop]
    if max(zs) > MAXZ:
        report.append({"edges": len(loop), "skipped_above_z": round(max(zs), 3)})
        continue
    t = ear_clip(loop)
    # The fill gets its OWN copies of the loop vertices (same position, joints and weights as the
    # rim vertex they copy -- i.e. weighted from their neighbours), with the patch's flat normal and
    # ONE UV for the whole patch (the rim vertex nearest the loop centre), so the closed sole shows a
    # single texel colour instead of the rim texture smeared across it.
    pts = np.array([Pw[w] for w in loop])
    nrm = np.zeros(3)
    for i in range(len(pts)):
        nrm += np.cross(pts[i], pts[(i + 1) % len(pts)])
    nrm /= max(np.linalg.norm(nrm), 1e-9)
    cen = pts.mean(0)
    uv_src = orig[loop[int(np.argmin(np.linalg.norm(pts - cen, axis=1)))]]
    base = len(P) + len(dup_src)
    local = {}
    for w in loop:
        if w in local:  # a pinch vertex visited twice by the same loop: one copy
            continue
        local[w] = base + len(local)
        dup_src.append(orig[w])
        dup_nrm.append(nrm)
        dup_uv_src.append(uv_src)
    new += [(local[a], local[b], local[c]) for a, b, c in t]
    report.append({"edges": len(loop), "tris": len(t), "centre": cen.round(3).tolist()})
print(json.dumps(report, indent=0))
newI = np.vstack([I, np.array(new, dtype=np.int64)])
assert newI.max() < 65536
pad = (-len(binb)) % 4
binb2 = binb + bytes(1) * pad


def add_view(arr_bytes, target=None):
    global binb2
    bv = {"buffer": 0, "byteOffset": len(binb2), "byteLength": len(arr_bytes)}
    if target:
        bv["target"] = target
    binb2 += arr_bytes
    binb2 += bytes(1) * ((-len(binb2)) % 4)
    js["bufferViews"].append(bv)
    return len(js["bufferViews"]) - 1


_NP = {5126: "<f4", 5123: "<u2", 5121: "u1", 5125: "<u4"}
src_idx = np.array(dup_src, dtype=np.int64)
for name, ai in prim["attributes"].items():
    a = js["accessors"][ai]
    arr = glb_gate._accessor(js, binb, ai, cache)
    if a.get("normalized"):
        raise SystemExit(f"normalized attribute {name} not handled")
    extra = arr[src_idx].copy()
    if name == "NORMAL":
        extra = np.array(dup_nrm) @ glb_gate.C  # blender -> gltf axes (C is orthonormal)
    if name.startswith("TEXCOORD"):
        extra = arr[np.array(dup_uv_src, dtype=np.int64)].copy()
    full = np.vstack([arr, extra]).astype(_NP[a["componentType"]])
    a["bufferView"] = add_view(full.tobytes(), 34962)
    a["byteOffset"] = 0
    a["count"] = int(len(full))
    if "min" in a:
        a["min"] = [float(x) for x in full.min(0)]
        a["max"] = [float(x) for x in full.max(0)]
acc["bufferView"] = add_view(newI.astype("<u2").tobytes(), 34963)
acc["byteOffset"] = 0
acc["count"] = int(newI.size)


def repack_buffer():
    """Rewrite binb2/js["bufferViews"] to hold only bufferViews an accessor or image still
    references, in a fresh compact layout. add_view() above only ever appends (it never reclaims
    the bytes of a bufferView a reassigned accessor stops pointing at), so without this pass every
    attribute/index buffer this script touches is replaced, not removed -- its old bytes stay
    physically embedded in the file, unreferenced dead weight."""
    global binb2
    referenced = sorted({a["bufferView"] for a in js["accessors"] if "bufferView" in a}
                         | {im["bufferView"] for im in js.get("images", []) if "bufferView" in im})
    new_bin = bytearray()
    new_views = []
    remap = {}
    for old_idx in referenced:
        view = js["bufferViews"][old_idx]
        data = binb2[view["byteOffset"]: view["byteOffset"] + view["byteLength"]]
        pad_len = (-len(new_bin)) % 4
        new_bin += bytes(pad_len)
        new_view = {"buffer": 0, "byteOffset": len(new_bin), "byteLength": len(data)}
        if "target" in view:
            new_view["target"] = view["target"]
        new_bin += bytes(data)
        remap[old_idx] = len(new_views)
        new_views.append(new_view)
    js["bufferViews"] = new_views
    for a in js["accessors"]:
        if "bufferView" in a:
            a["bufferView"] = remap[a["bufferView"]]
    for im in js.get("images", []):
        if "bufferView" in im:
            im["bufferView"] = remap[im["bufferView"]]
    binb2 = bytes(new_bin)
    js["buffers"][0]["byteLength"] = len(binb2)


repack_buffer()
jb = json.dumps(js, separators=(",", ":")).encode()
jb += b" " * ((-len(jb)) % 4)
out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(jb) + 8 + len(binb2))
out += struct.pack("<I4s", len(jb), b"JSON") + jb + struct.pack("<I4s", len(binb2), b"BIN\x00") + binb2
open(dst, "wb").write(out)
print(f"wrote {dst}: +{len(new)} triangles ({len(I)} -> {len(newI)}), +{len(dup_src)} fill vertices (copies of rim vertices)")
