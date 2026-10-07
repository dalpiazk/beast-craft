"""Real arms for the Archer (v22 round 5, producer review: "his left arm is attached to his torso at
the wrong place"). Local-only (not run by CI), like the rest of meshfix/. Scripted and deterministic.

    blender -b --python meshfix/archer_arms.py -- meshfix/archer_arms_cfg.json SURGERY.glb OUT_DIR

SURGERY.glb -- prop_surgery.py + archer_cfg.json's output: the body (Meshy's one arm and bow cut away)
               plus two separate closed arm SHELLS, each a copy of Meshy's forearm + fist sunk into the
               chest under the leaf mantle. The mesh had no shoulders or upper arms at all.
Writes OUT_DIR/archer_prepped.glb (the body WITH arms, one closed shell; archer_bow.py adds the props
to it), archer_arms.blend and arms_report.json (tris / open / non-manifold edges before and after,
per-arm tris, the joint points the landmarks use).

Per side (the left arm is built from the mirror image of the right's parameters across the trunk
mid-plane x = cfg["mid_x"], so both arms are symmetric):
  1. the old shell is cut by a plane at the wrist (`wrist_t` along its own shoulder->fist axis); the
     forearm stub is deleted and the FIST is kept as the hand, with one open rim at the wrist cut;
  2. a socket is cut in the torso: a ray from the trunk axis finds the torso wall at the shoulder
     (`socket` y, z, just under the leaf mantle, which overlaps the top of the shoulder), and the
     connected faces within `radius` are removed -- one open rim;
  3. the arm is a 12-sided tube along a smooth centreline: out of the socket along the wall normal
     (the shoulder cap), down the upper arm, a bent elbow, the forearm; rings are placed where the
     joints bend (3 at the shoulder cap, 3 round the elbow);
  4. the tube's first ring is zipped to the socket rim and its last ring to the fist's wrist rim
     (an angle-ordered zipper, so any vertex counts join) -- one watertight, manifold surface;
  5. UVs: every new vertex samples one cream patch of the existing painted atlas (the largest
     cream-skin square found in the texture, nearest the target colour): around the arm a triangle
     wave (no wrap seam), along it the patch height (its painted shading); the zips use the same
     patch, so torso -> arm -> fist has no smeared atlas strip. One material, no new texture.
"""
import bpy, bmesh, sys, os, json, math, colorsys
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index('--') + 1:]
CFG = json.load(open(a[0])); SRC, OUT = a[1], a[2]
os.makedirs(OUT, exist_ok=True)
REPORT = {"config": CFG}
MID = CFG["mid_x"]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.name = "archer"
me = ob.data


def stats(bm, tag):
    bm.edges.ensure_lookup_table()
    nb = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if not e.is_manifold)
    comps = components(bm)
    r = dict(verts=len(bm.verts), tris=sum(len(f.verts) - 2 for f in bm.faces), boundary_edges=nb,
             non_manifold_edges=nm, components=len(comps), component_sizes=sorted(map(len, comps), reverse=True)[:6])
    print(f"STATS {tag}: {r}")
    REPORT[tag] = r
    return r


def components(bm):
    seen, comps = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        st, c = [v], []
        seen.add(v)
        while st:
            x = st.pop(); c.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in seen:
                    seen.add(o); st.append(o)
        comps.append(c)
    return comps


def mirror_pt(p, side):
    p = Vector(p)
    return p if side == "R" else Vector((2 * MID - p.x, p.y, p.z))


def mirror_dir(d, side):
    d = Vector(d)
    return d if side == "R" else Vector((-d.x, d.y, d.z))


def loop_cycles(edges):
    """ordered vertex cycles of a set of boundary edges"""
    adj = {}
    for e in edges:
        v0, v1 = e.verts
        adj.setdefault(v0, []).append(v1); adj.setdefault(v1, []).append(v0)
    seen, cyc = set(), []
    for s in adj:
        if s in seen:
            continue
        c, prev, cur = [s], None, s
        seen.add(s)
        while True:
            nx = [x for x in adj[cur] if x is not prev and x not in seen]
            if not nx:
                break
            prev, cur = cur, nx[0]
            seen.add(cur); c.append(cur)
        cyc.append(c)
    return cyc


bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
uvl = bm.loops.layers.uv.active
stats(bm, "input")

# ---- the painted atlas: one cream-skin patch for every new vertex -------------------------------
IMG = [n.image for m in me.materials for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image][0]
W_, H_ = IMG.size
PIX = np.array(IMG.pixels[:], dtype=np.float32).reshape(H_, W_, -1)[:, :, :3]  # display sRGB values
uvc = CFG["uv_patch"]
if uvc.get("rect"):
    RECT = uvc["rect"]
else:
    mx, mn = PIX.max(2), PIX.min(2)
    sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
    tgt = np.array(colorsys.hsv_to_rgb(*uvc["target_hsv"]), dtype=np.float32)
    dist = np.linalg.norm(PIX - tgt, axis=2)
    mask = (dist < uvc.get("tol", 0.09)) & (sat < 0.32)
    m = mask.copy(); depth = np.zeros(mask.shape, np.int32)
    for k in range(1, 200):  # chessboard erosion depth = the largest square fully inside the mask
        m = (m & np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1)
             & np.roll(np.roll(m, 1, 0), 1, 1) & np.roll(np.roll(m, 1, 0), -1, 1)
             & np.roll(np.roll(m, -1, 0), 1, 1) & np.roll(np.roll(m, -1, 0), -1, 1))
        m[0, :] = m[-1, :] = m[:, 0] = m[:, -1] = False
        if not m.any():
            break
        depth[m] = k
    best = depth.max()
    cand = np.argwhere(depth >= max(4, int(best * 0.8)))
    # among the deepest squares, the one whose mean colour is nearest the target
    def mean_d(rc):
        r_, c_ = rc; h = max(3, int(best * 0.8))
        return np.linalg.norm(PIX[r_ - h:r_ + h, c_ - h:c_ + h].reshape(-1, 3).mean(0) - tgt)
    r0, c0 = min(cand, key=mean_d)
    h = max(3, int(best * 0.8)) - 1
    RECT = [(c0 - h) / W_, (r0 - h) / H_, 2 * h / W_, 2 * h / H_]
    REPORT["uv_patch_found"] = {"center_px": [int(c0), int(r0)], "half_px": int(h),
                                "mean_rgb": PIX[r0 - h:r0 + h, c0 - h:c0 + h].reshape(-1, 3).mean(0).round(3).tolist()}
REPORT["uv_rect"] = RECT
print("UV PATCH", RECT, REPORT.get("uv_patch_found"))


def patch_uv(around, along):
    """around 0..1 (triangle wave: no seam where the ring wraps), along 0..1"""
    tri = 1.0 - abs(2.0 * (around % 1.0) - 1.0)
    return (RECT[0] + RECT[2] * tri, RECT[1] + RECT[3] * max(0.0, min(1.0, along)))


# ---- identify body + the two old arm shells ------------------------------------------------------
comps = components(bm)
body = max(comps, key=len)
bodyset = set(body)
arms_cfg = CFG["arms"]
REPORT["arms"] = {}

# BVH of the body for the socket ray
bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
body_faces = [f for f in bm.faces if f.verts[0] in bodyset]
bvh = BVHTree.FromPolygons([v.co.copy() for v in bm.verts], [[v.index for v in f.verts] for f in body_faces])

new_face_uv = {}   # face -> list of uv per loop (set at the end)
smooth_faces = []


def zipper(A, angA, B, angB, tag):
    """join two closed vertex loops ordered by angle (each unwrapped, A[0]/B[0] at their lowest)"""
    nA, nB = len(A), len(B)
    aA = angA + [angA[0] + 2 * math.pi]
    aB = angB + [angB[0] + 2 * math.pi]
    i = j = 0
    tris = []
    while i < nA or j < nB:
        if j >= nB or (i < nA and aA[i + 1] <= aB[j + 1]):
            tris.append(((A[i % nA], 0, aA[i]), (A[(i + 1) % nA], 0, aA[i + 1]), (B[j % nB], 1, aB[j]))); i += 1
        else:
            tris.append(((A[i % nA], 0, aA[i]), (B[(j + 1) % nB], 1, aB[j + 1]), (B[j % nB], 1, aB[j]))); j += 1
    out = []
    for t in tris:
        try:
            f = bm.faces.new([x[0] for x in t])
        except ValueError:
            print("ZIPPER duplicate face", tag); continue
        out.append((f, t))
    return out


def order_by_angle(loop, center, axis, e1):
    e2 = axis.cross(e1)
    ang = [math.atan2((v.co - center).dot(e2), (v.co - center).dot(e1)) for v in loop]
    # orientation: make the angle increase along the loop
    tot = sum(((ang[(k + 1) % len(ang)] - ang[k] + math.pi) % (2 * math.pi)) - math.pi for k in range(len(ang)))
    if tot < 0:
        loop = loop[::-1]; ang = ang[::-1]
    k0 = min(range(len(ang)), key=lambda k: ang[k] % (2 * math.pi))
    loop = loop[k0:] + loop[:k0]; ang = ang[k0:] + ang[:k0]
    un = [ang[0] % (2 * math.pi)]
    back = 0.0
    for k in range(1, len(ang)):
        d = ((ang[k] - ang[k - 1] + math.pi) % (2 * math.pi)) - math.pi
        back = min(back, d)
        un.append(un[-1] + d)
    return loop, un, math.degrees(back)


def hermite(p0, p1, t0, t1, s):
    h00 = 2 * s ** 3 - 3 * s ** 2 + 1; h10 = s ** 3 - 2 * s ** 2 + s
    h01 = -2 * s ** 3 + 3 * s ** 2; h11 = s ** 3 - s ** 2
    return p0 * h00 + t0 * h10 + p1 * h01 + t1 * h11


A_ = CFG["arm"]
for side in ("R", "L"):
    sc = arms_cfg[side]
    # 1. the fist out of the old shell
    seed = Vector(sc["shell_seed"])
    shell = min((c for c in components(bm) if c[0] not in bodyset), key=lambda c: min((v.co - seed).length for v in c))
    p0, p1 = Vector(sc["old_axis"][0]), Vector(sc["old_axis"][1])
    ax = (p1 - p0).normalized()
    sv = set(shell)
    geom = list(sv) + list({e for v in sv for e in v.link_edges}) + list({f for v in sv for f in v.link_faces})
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=p0 + ax * sc["wrist_t"], plane_no=ax, clear_inner=True)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    fist = min((c for c in components(bm) if not (c[0] in bodyset)),
               key=lambda c: min((v.co - p1).length for v in c))
    fset = set(fist)
    rim_edges = [e for v in fist for e in v.link_edges if e.is_boundary]
    cyc = loop_cycles(set(rim_edges))
    if len(cyc) != 1:
        raise SystemExit(f"{side}: wrist cut gave {len(cyc)} rims")
    rim = cyc[0]
    rim_c = sum((v.co for v in rim), Vector()) / len(rim)
    fist_tris = sum(len(f.verts) - 2 for f in {f for v in fist for f in v.link_faces})
    # 2. the centreline (right-side parameters, mirrored for the left)
    so = A_["socket"]
    o = mirror_pt((MID, so["y"], so["z"]), side)
    n0 = mirror_dir(so["dir"], side).normalized()
    hit = bvh.ray_cast(o, n0)
    if hit[0] is None:
        raise SystemExit(f"{side}: socket ray missed the torso")
    S = hit[0]
    nrm = hit[1] if hit[1].dot(n0) > 0 else -hit[1]
    n = (nrm * so.get("normal_mix", 0.5) + n0 * (1 - so.get("normal_mix", 0.5))).normalized()
    du = mirror_dir(A_["upper_dir"], side).normalized()
    df = mirror_dir(A_["fore_dir"], side).normalized()
    C = S + n * A_["cap_out"]
    E = C + du * A_["upper_len"]
    Wp = E + df * A_["fore_len"]
    tC = (n + du).normalized(); tE = (du + df).normalized()
    segs = [(S, C, n, tC), (C, E, tC, tE), (E, Wp, tE, df)]
    rings_spec = A_["rings"]  # [[segment, s, radius], ...]
    # 3. the socket in the torso
    rad = so["radius"]
    body_faces = [f for f in body_faces if f.is_valid]
    f0 = min(body_faces, key=lambda f: (f.calc_center_median() - S).length)
    sel, st = {f0}, [f0]
    while st:
        f = st.pop()
        for e in f.edges:
            for g in e.link_faces:
                if g not in sel and g.is_valid and (g.calc_center_median() - S).length < rad:
                    sel.add(g); st.append(g)
    sock_faces = len(sel)
    bmesh.ops.delete(bm, geom=list(sel), context='FACES_ONLY')
    loose = [v for v in bm.verts if not v.link_faces]
    loose_e = [e for e in bm.edges if not e.link_faces]
    bmesh.ops.delete(bm, geom=loose_e, context='EDGES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.is_valid and not v.link_faces], context='VERTS')
    hole_edges = {e for e in bm.edges if e.is_boundary and e.verts[0] in bodyset and e.verts[1] in bodyset}
    hcyc = loop_cycles(hole_edges)
    if len(hcyc) != 1:
        raise SystemExit(f"{side}: socket gave {len(hcyc)} rims")
    hole = hcyc[0]
    # ring frames (parallel transport from a fixed reference, mirrored per side)
    ref = mirror_dir(A_["ring_ref"], side).normalized()
    centres, tangents, radii = [], [], []
    for si, s_, r_ in rings_spec:
        a0, a1, t0, t1 = segs[si]
        L = (a1 - a0).length
        p = hermite(a0, a1, t0 * L, t1 * L, s_)
        dp = (hermite(a0, a1, t0 * L, t1 * L, min(1, s_ + 1e-3)) - hermite(a0, a1, t0 * L, t1 * L, max(0, s_ - 1e-3))).normalized()
        centres.append(p); tangents.append(dp); radii.append(r_)
    sides = A_["sides"]
    e1 = (ref - tangents[0] * ref.dot(tangents[0])).normalized()
    rings, frames = [], []
    for k, (p, t, r_) in enumerate(zip(centres, tangents, radii)):
        e1 = (e1 - t * e1.dot(t)).normalized()
        e2 = t.cross(e1)
        if side == "L":
            e2 = -e2  # mirror image: the left ring runs the other way round
        frames.append((e1.copy(), e2.copy()))
        rings.append([bm.verts.new(p + (e1 * math.cos(2 * math.pi * j / sides) + e2 * math.sin(2 * math.pi * j / sides)) * r_)
                      for j in range(sides)])
    # the hand: the fist's wrist rim lands at the forearm end, fist continuing the forearm
    tw = tangents[-1]
    R_al = ax.rotation_difference(df).to_matrix()
    roll = math.radians(sc.get("fist_roll", 0.0))
    Rm = Matrix.Rotation(roll, 3, df) @ R_al
    Wend = centres[-1] + tw * A_["wrist_gap"]
    for v in fist:
        v.co = Wend + Rm @ (v.co - rim_c)
    rim_c2 = Wend
    # quads between rings
    nr = len(rings)
    arm_faces = []
    for k in range(nr - 1):
        for j in range(sides):
            q = [rings[k][j], rings[k][(j + 1) % sides], rings[k + 1][(j + 1) % sides], rings[k + 1][j]]
            f = bm.faces.new(q)
            al = [k / (nr - 1)] * 2 + [(k + 1) / (nr - 1)] * 2
            ar = [j / sides, (j + 1) / sides, (j + 1) / sides, j / sides]
            new_face_uv[f] = [patch_uv(x, y) for x, y in zip(ar, al)]
            arm_faces.append(f)
    # zips: socket rim -> first ring; last ring -> fist rim
    e1a, e2a = frames[0]
    hole_o, hang, hback = order_by_angle(hole, centres[0], tangents[0] if side == "R" else -tangents[0], e1a)
    ring0_ang = [2 * math.pi * j / sides for j in range(sides)]
    zf = zipper(hole_o, hang, rings[0], ring0_ang, f"{side} socket")
    for f, t in zf:
        new_face_uv[f] = [patch_uv(x[2] / (2 * math.pi), 0.0) for x in t]
    e1b, e2b = frames[-1]
    rim_o, rang, rback = order_by_angle(rim, rim_c2, tangents[-1] if side == "R" else -tangents[-1], e1b)
    zf2 = zipper(rings[-1], ring0_ang, rim_o, rang, f"{side} wrist")
    for f, t in zf2:
        new_face_uv[f] = [patch_uv(x[2] / (2 * math.pi), 1.0) for x in t]
    arm_faces += [f for f, _ in zf] + [f for f, _ in zf2]
    smooth_faces += arm_faces
    tip = max(fist, key=lambda v: (v.co - Wend).dot(df)).co.copy()
    REPORT["arms"][side] = {
        "socket": list(S), "socket_normal": list(n), "socket_faces_removed": sock_faces,
        "socket_rim_verts": len(hole), "socket_rim_backstep_deg": round(hback, 1),
        "wrist_rim_verts": len(rim), "wrist_rim_backstep_deg": round(rback, 1),
        "cap_centre": list(C), "elbow": list(E), "wrist": list(Wend), "fist_tip": list(tip),
        "tube_tris": 2 * sides * (nr - 1), "zip_tris": len(zf) + len(zf2), "fist_tris": fist_tris,
        "arm_tris": 2 * sides * (nr - 1) + len(zf) + len(zf2) + fist_tris,
        "rings": nr, "ring_centres": [list(p) for p in centres], "ring_radii": radii}
    print(f"ARM {side}: {REPORT['arms'][side]['arm_tris']} tris, socket rim {len(hole)} (backstep {hback:.1f}), "
          f"wrist rim {len(rim)} (backstep {rback:.1f})")
    bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()

for f, uvs in new_face_uv.items():
    for l, uv in zip(f.loops, uvs):
        l[uvl].uv = uv
for f in smooth_faces:
    f.smooth = True
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
stats(bm, "final")
bm.to_mesh(me); me.update(); bm.free()
REPORT["final_min_z"] = min(v.co.z for v in me.vertices)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "archer_arms.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "archer_prepped.glb"), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
json.dump(REPORT, open(os.path.join(OUT, "arms_report.json"), "w"), indent=1, default=str)
print("ARCHER ARMS DONE")
