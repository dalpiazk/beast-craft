"""Prop / limb surgery (v22, enemies batch 2): config-driven, local-only (not run by CI), like
treant_surgery.py. Meshy fuses held props into the body: the Shaman's staff runs along and INTO the
edge of its beard and its mushroom cap into the hood; the Archer's single modelled arm lies fused
across its chest with its fist on the arrow, and its bow hand has no arm at all. A prop that is rigid
to a hand bone can only move if no face joins it to anything that does not move with that hand.

    blender -b --python meshfix/prop_surgery.py -- CONFIG.json PREPPED.glb OUT_DIR

Writes OUT_DIR/<name>_prepped.glb (what rig_creature.py takes), <name>_surgery.blend and
surgery_report.json (verts/tris/open/non-manifold edges/components after every stage). Native
prepped-mesh coords throughout (head = -Y, up = +Z). Regions use rig_creature's rigid_parts shapes:
{"poly": [[x,y,z], ...], "r": R} capsule chains, {"box": [x0,x1,y0,y1,z0,z1]}; a region is the union
of "include" minus "exclude". Stages ("op"):
  delete   faces whose vertices are ALL in the region are removed (loose verts/edges dropped);
           optional "color" / "colors" (any of): {"h": [lo, hi], "s": [..], "v": [..], "z": [..]?}
           also requires each vertex's texture colour (HSV, 0..1) in range (cut a brown staff out of
           a cream beard); copy takes the same "colors";
  fill     every open boundary loop is filled (face + poked centre, relaxed; "poke": false ear-clips
           the ring instead -- long rip strips); the patch UVs come
           from the ring (treant_surgery's fix_patch_uvs: a ring straddling a UV seam gets one solid
           ring texel instead of a smeared atlas);
  rip      flood-fill a prop from `seed` (region + optional list of "colors" gates) and delete the
           faces that mix prop and body vertices: the prop keeps its own surface, the fused band goes;
  drop_component  remove the whole pieces holding the given `seeds` (a ripped-free prop);
  drop_small  remove components under `min_verts` (slivers a rip leaves);
  tube     a new CLOSED tube along a polyline (radius per point or one radius, `sides`), all of it
           mapped to one texel (the UV of the nearest original vertex to `uv_from`) -- a staff shaft;
  mushroom a new closed flattened dome (a mushroom cap): `uv_top_from` / `uv_under_from` texels;
  copy     the faces inside a region are copied (before any delete of the same stage list, read from
           the ORIGINAL mesh), their open rims capped, then placed by a similarity transform mapping
           `from` = [p0, p1] onto `to` = [q0, q1] (optional `mirror_x`: mirror through x = value
           first, faces re-wound; optional `post_mirror_x`: mirror through x = value AFTER placing
           -- the exact mirror image of a limb built like the other side's): a limb template re-used
           as a new, separate limb.
New parts stay SEPARATE closed shells in the same mesh object (a boolean union with the fused
low-poly body explodes; treant_surgery found the same for the crown), skinned by the rig's
rigid_parts / heat weights. The script is deterministic (no randomness)."""
import bpy, bmesh, sys, os, json, math
from mathutils import Vector, Matrix, Quaternion

a = sys.argv[sys.argv.index('--') + 1:]
CFG = json.load(open(a[0])); SRC, OUT = a[1], a[2]
NAME = CFG["name"]
os.makedirs(OUT, exist_ok=True)
REPORT = {"config": CFG}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.name = NAME
me = ob.data


def stats(bm, tag):
    bm.edges.ensure_lookup_table()
    nb = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if not e.is_manifold)
    seen = set(); comps = []
    for v in bm.verts:
        if v.index in seen:
            continue
        st = [v]; seen.add(v.index); n = 0
        while st:
            x = st.pop(); n += 1
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index); st.append(o)
        comps.append(n)
    ntri = sum(len(f.verts) - 2 for f in bm.faces)
    r = dict(verts=len(bm.verts), tris=ntri, boundary_edges=nb, non_manifold_edges=nm,
             components=len(comps), component_sizes=sorted(comps, reverse=True)[:8])
    print(f"STATS {tag}: {r}")
    REPORT[tag] = r
    return r


def seg_d(p, a_, b_):
    ab = b_ - a_
    L2 = ab.length_squared
    t = 0.0 if L2 < 1e-12 else max(0.0, min(1.0, (p - a_).dot(ab) / L2))
    return (p - (a_ + ab * t)).length


def in_shapes(p, shapes):
    for s in shapes:
        if "poly" in s:
            pts = [Vector(q) for q in s["poly"]]
            segs = list(zip(pts, pts[1:])) or [(pts[0], pts[0])]
            if min(seg_d(p, x, y) for x, y in segs) <= s["r"]:
                return True
        elif "box" in s:
            b = s["box"]
            if b[0] <= p.x <= b[1] and b[2] <= p.y <= b[3] and b[4] <= p.z <= b[5]:
                return True
    return False


def in_region(p, reg):
    return in_shapes(p, reg.get("include", [])) and not in_shapes(p, reg.get("exclude", []))


# ---- fill helpers (same method as treant_surgery.py: ring-UV patches, poked + relaxed centres) ----
def boundary_cycles(bm):
    nxt = {}
    for e in bm.edges:
        if not e.is_boundary:
            continue
        l = e.link_loops[0]
        a_, b_ = l.vert, l.link_loop_next.vert
        nxt.setdefault(b_, []).append(a_)
    cycles = []
    while any(nxt.values()):
        start = next(v for v, lst in nxt.items() if lst)
        path = [start]; pos = {start: 0}; cur = start
        while True:
            lst = nxt.get(cur)
            if not lst:
                break
            n = lst.pop()
            if n in pos:
                cyc = path[pos[n]:]
                cycles.append(cyc)
                for x in cyc:
                    pos.pop(x, None)
                path = path[:len(path) - len(cyc)]
                if not path:
                    break
                cur = path[-1]
                pos = {x: i for i, x in enumerate(path)}
                continue
            pos[n] = len(path); path.append(n); cur = n
    return cycles


def fill_holes(bm, uvl, pl, tag, poke=True):
    vuv = {}
    for f in bm.faces:
        for l in f.loops:
            vuv.setdefault(l.vert, l[uvl].uv.copy())
    for _ in range(10):
        dang = [f for f in bm.faces if sum(1 for e in f.edges if e.is_boundary) >= 2]
        if not dang:
            break
        bmesh.ops.delete(bm, geom=dang, context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    newf = []
    for cyc in boundary_cycles(bm):
        if len(cyc) < 3 or len(set(cyc)) != len(cyc):
            continue
        try:
            f = bm.faces.new(cyc)
        except ValueError:
            continue
        f[pl] = 1
        for l in f.loops:
            if l.vert in vuv:
                l[uvl].uv = vuv[l.vert]
        newf.append(f)
    big = [f for f in newf if len(f.verts) > 3]
    if not poke:  # long thin rip strips: ear-clip the ring (a poked centre would stand out as a cone)
        if big:
            bmesh.ops.triangulate(bm, faces=big, quad_method='BEAUTY', ngon_method='EAR_CLIP')
        print(f"FILL {tag}: {len(newf)} hole faces (ear-clipped)")
        return len(newf)
    cen = bmesh.ops.poke(bm, faces=big)['verts'] if big else []
    for f in bm.faces:
        if any(v in cen for v in f.verts):
            f[pl] = 1
    for _ in range(15):
        for v in cen:
            nb = [e.other_vert(v).co for e in v.link_edges]
            v.co = v.co * 0.3 + sum(nb, Vector()) / len(nb) * 0.7
    print(f"FILL {tag}: {len(newf)} hole faces, {len(cen)} poke centres")
    return len(newf)


def smooth_patch(bm, uvl, pl, tag, cuts=2, iters=30, inflate=0.25, mosaic=True):
    """A long ear-clipped strip is a flat plank. Subdivide the patch faces, relax the new interior
    vertices (ring fixed) and push them out along the mean ring normal by `inflate` x their distance
    to the ring (a rounded surface instead of a plank). mosaic: each patch face takes the UV of the
    original (non-patch) vertex nearest its centre, so the strip continues the colours either side
    of it (cream beard on one side, leaf green on the other) instead of one solid ring texel."""
    from mathutils.kdtree import KDTree
    pf = [f for f in bm.faces if f[pl]]
    ring = {v for f in pf for v in f.verts if any(not g[pl] for g in v.link_faces)}
    edges = list({e for f in pf for e in f.edges})
    if cuts:
        bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
    pf = [f for f in bm.faces if f[pl]]
    ring = {v for f in pf for v in f.verts if any(not g[pl] for g in v.link_faces)}
    inner = {v for f in pf for v in f.verts} - ring
    inner = {v for v in inner if all(g[pl] for g in v.link_faces)}
    bm.normal_update()
    for _ in range(iters):
        for v in inner:
            nb = [e.other_vert(v).co for e in v.link_edges]
            v.co = v.co * 0.4 + sum(nb, Vector()) / len(nb) * 0.6
    if inflate and inner:
        kd = KDTree(len(ring))
        rl = list(ring)
        for i, v in enumerate(rl):
            kd.insert(v.co, i)
        kd.balance()
        n = sum((g.normal for v in ring for g in v.link_faces if not g[pl]), Vector()).normalized()
        for v in inner:
            _, _, d = kd.find(v.co)
            v.co += n * d * inflate
    if mosaic:
        # nearest original vertex of the SAME connected piece (a strip on the freed staff must not
        # pick up the beard's colours across the cut)
        comp = {}
        for v in bm.verts:
            if v in comp:
                continue
            st_ = [v]; comp[v] = v.index
            while st_:
                x = st_.pop()
                for e in x.link_edges:
                    o = e.other_vert(x)
                    if o not in comp:
                        comp[o] = v.index; st_.append(o)
        orig_v = [v for v in bm.verts if any(not g[pl] for g in v.link_faces) and v not in ring]
        trees = {}
        for c_ in {comp[v] for v in orig_v}:
            vs = [v for v in orig_v if comp[v] == c_]
            kd = KDTree(len(vs))
            for i, v in enumerate(vs):
                kd.insert(v.co, i)
            kd.balance()
            trees[c_] = (kd, vs)
        for f in pf:
            kd, vs = trees[comp[f.verts[0]]]
            _, i, _ = kd.find(f.calc_center_median())
            src = vs[i]
            uv = next(l[uvl].uv.copy() for g in src.link_faces if not g[pl] for l in g.loops if l.vert is src)
            for l in f.loops:
                l[uvl].uv = uv
            f[pl] = 2  # mapped: fix_patch_uvs leaves it alone
    print(f"SMOOTH PATCH {tag}: {len(pf)} faces, {len(inner)} inner verts relaxed")
    REPORT.setdefault('smooth_patch', {})[tag] = dict(faces=len(pf), inner=len(inner))


def fix_patch_uvs(bm, uvl, pl, tag, tol=0.03):
    import itertools
    vuvs = {}
    for f in bm.faces:
        if f[pl]:
            continue
        for l in f.loops:
            lst = vuvs.setdefault(l.vert, [])
            if all((l[uvl].uv - u).length > 1e-5 for u in lst):
                lst.append(l[uvl].uv.copy())
    nfix = ncol = 0
    for f in bm.faces:
        if f[pl] != 1:
            continue
        ring = [l for l in f.loops if l.vert in vuvs]
        if not ring:
            continue
        best = None
        for combo in itertools.product(*[vuvs[l.vert][:4] for l in ring]):
            spread = max((x - y).length for x in combo for y in combo)
            if best is None or spread < best[0]:
                best = (spread, combo)
        spread, combo = best
        if spread > tol:
            combo = [combo[0]] * len(combo); ncol += 1
        m = sum(combo, Vector((0, 0))) / len(combo)
        cm = dict(zip([l.vert for l in ring], combo))
        for l in f.loops:
            l[uvl].uv = cm.get(l.vert, m)
        nfix += 1
    print(f"PATCH UVS {tag}: {nfix} faces, {ncol} solid ring colour")
    REPORT.setdefault('patch_uvs', {})[tag] = dict(faces=nfix, solid=ncol)


# ---- new geometry ----
def nearest_uv(bm, uvl, p):
    p = Vector(p)
    best = min((f for f in bm.faces), key=lambda f: (f.calc_center_median() - p).length)
    return best.loops[0][uvl].uv.copy()


def ring_frames(pts):
    """Parallel-transport frames along a polyline: (point, tangent, normal, binormal)."""
    out = []
    t0 = (pts[1] - pts[0]).normalized()
    ref = Vector((1, 0, 0)) if abs(t0.x) < 0.9 else Vector((0, 1, 0))
    n = (ref - t0 * ref.dot(t0)).normalized()
    for i, p in enumerate(pts):
        t = ((pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])).normalized()
        n = (n - t * n.dot(t)).normalized()
        out.append((p, t, n, t.cross(n)))
    return out


def resample(poly, step):
    pts = [Vector(q) for q in poly]
    out = [pts[0]]
    for a_, b_ in zip(pts, pts[1:]):
        k = max(1, int(math.ceil((b_ - a_).length / step)))
        for i in range(1, k + 1):
            out.append(a_.lerp(b_, i / k))
    return out


def add_tube(bm, uvl, uv, st):
    pts = resample(st["axis"], st.get("seg", 0.06))
    rads = st["r"] if isinstance(st["r"], list) else [st["r"]] * len(st["axis"])
    # radius per resampled point: interpolate along the original polyline by arc length
    L = [0.0]
    for x, y in zip(pts, pts[1:]):
        L.append(L[-1] + (y - x).length)
    LA = [0.0]
    axp = [Vector(q) for q in st["axis"]]
    for x, y in zip(axp, axp[1:]):
        LA.append(LA[-1] + (y - x).length)

    def rad_at(s):
        for i in range(len(LA) - 1):
            if s <= LA[i + 1] + 1e-9:
                u = (s - LA[i]) / max(LA[i + 1] - LA[i], 1e-9)
                return rads[i] + (rads[i + 1] - rads[i]) * u
        return rads[-1]
    n = st.get("sides", 8)
    rings = []
    for (p, t, nn, bb), s in zip(ring_frames(pts), L):
        r = rad_at(s)
        rings.append([bm.verts.new(p + (nn * math.cos(2 * math.pi * k / n) + bb * math.sin(2 * math.pi * k / n)) * r)
                      for k in range(n)])
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(n):
            faces.append(bm.faces.new([r0[k], r0[(k + 1) % n], r1[(k + 1) % n], r1[k]]))
    c0 = bm.verts.new(pts[0]); c1 = bm.verts.new(pts[-1])
    for k in range(n):
        faces.append(bm.faces.new([rings[0][(k + 1) % n], rings[0][k], c0]))
        faces.append(bm.faces.new([rings[-1][k], rings[-1][(k + 1) % n], c1]))
    for f in faces:
        for l in f.loops:
            l[uvl].uv = uv
    bmesh.ops.recalc_face_normals(bm, faces=faces)
    print(f"TUBE: {len(pts)} rings x {n}, {len(faces)} faces, uv {tuple(round(c, 4) for c in uv)}")
    return faces


def add_mushroom(bm, uvl, uvt, uvu, st):
    c = Vector(st["center"]); R = st["r"]; h = st["h"]; n = st.get("sides", 12); rings_n = st.get("rings", 4)
    up = Vector(st.get("up", (0, 0, 1))).normalized()
    ref = Vector((1, 0, 0)) if abs(up.x) < 0.9 else Vector((0, 1, 0))
    e1 = (ref - up * ref.dot(up)).normalized(); e2 = up.cross(e1)
    top = []
    for j in range(1, rings_n + 1):  # dome rings from the top pole down to the rim
        th = (math.pi / 2) * j / rings_n
        top.append([bm.verts.new(c + up * (h * math.cos(th)) + (e1 * math.cos(2 * math.pi * k / n) + e2 * math.sin(2 * math.pi * k / n)) * (R * math.sin(th)))
                    for k in range(n)])
    pole = bm.verts.new(c + up * h)
    under = bm.verts.new(c - up * (h * 0.35))
    faces_t, faces_u = [], []
    for k in range(n):
        faces_t.append(bm.faces.new([pole, top[0][k], top[0][(k + 1) % n]]))
    for r0, r1 in zip(top, top[1:]):
        for k in range(n):
            faces_t.append(bm.faces.new([r0[k], r1[k], r1[(k + 1) % n], r0[(k + 1) % n]]))
    for k in range(n):
        faces_u.append(bm.faces.new([top[-1][(k + 1) % n], top[-1][k], under]))
    for fs, uv in ((faces_t, uvt), (faces_u, uvu)):  # dome = top texel, underside = gill texel
        for f in fs:
            for l in f.loops:
                l[uvl].uv = uv
    bmesh.ops.recalc_face_normals(bm, faces=faces_t + faces_u)
    print(f"MUSHROOM: {len(faces_t) + len(faces_u)} faces")
    return faces_t + faces_u


def copy_region(src_bm, uvl_src, st):
    """Faces fully inside the region -> a new bmesh (largest component), rims capped, transformed."""
    gates = st.get("colors") or [None]
    hsv = vert_hsv(src_bm, uvl_src) if any(gates) else {}
    keep = {v for v in src_bm.verts if in_region(v.co, st)
            and any(color_ok(hsv.get(v, (0, 0, 0)), g, v.co.z) for g in gates)}
    nb_ = bmesh.new(); vmap = {}
    for v in keep:
        vmap[v] = nb_.verts.new(v.co)
    uvl = nb_.loops.layers.uv.new('UVMap'); pl = nb_.faces.layers.int.new('patch')
    for f in src_bm.faces:
        if all(v in keep for v in f.verts):
            nf = nb_.faces.new([vmap[v] for v in f.verts])
            for l, lo in zip(nf.loops, f.loops):
                l[uvl].uv = lo[uvl_src].uv
    bmesh.ops.delete(nb_, geom=[v for v in nb_.verts if not v.link_faces], context='VERTS')
    nb_.verts.ensure_lookup_table()
    comp = {}; sizes = []
    for v in nb_.verts:
        if v in comp:
            continue
        st_ = [v]; comp[v] = len(sizes); n = 0
        while st_:
            x = st_.pop(); n += 1
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in comp:
                    comp[o] = len(sizes); st_.append(o)
        sizes.append(n)
    big = max(range(len(sizes)), key=lambda i: sizes[i])
    bmesh.ops.delete(nb_, geom=[v for v in nb_.verts if comp[v] != big], context='VERTS')
    for _ in range(3):
        if not any(e.is_boundary for e in nb_.edges):
            break
        fill_holes(nb_, uvl, pl, f"copy_cap")
    fix_patch_uvs(nb_, uvl, pl, "copy_cap")
    tr = st["transform"]
    if "mirror_x" in tr:
        x0 = tr["mirror_x"]
        for v in nb_.verts:
            v.co.x = 2 * x0 - v.co.x
        bmesh.ops.reverse_faces(nb_, faces=nb_.faces)
    p0, p1 = (Vector(q) for q in tr["from"]); q0, q1 = (Vector(q) for q in tr["to"])
    if "mirror_x" in tr:
        p0.x = 2 * tr["mirror_x"] - p0.x; p1.x = 2 * tr["mirror_x"] - p1.x
    s = (q1 - q0).length / (p1 - p0).length if tr.get("scale", True) else 1.0
    rot = (p1 - p0).normalized().rotation_difference((q1 - q0).normalized()).to_matrix()
    if "roll_deg" in tr:  # extra roll about the new axis
        rot = Matrix.Rotation(math.radians(tr["roll_deg"]), 3, (q1 - q0).normalized()) @ rot
    for v in nb_.verts:
        v.co = q0 + rot @ ((v.co - p0) * s)
    if "post_mirror_x" in tr:
        # v22 round 4 (Archer): build the copy as the OTHER side's arm would be built, then mirror
        # it across the trunk mid-plane -- the result is the exact mirror image of that arm (same
        # shoulder geometry and roll), so the two shoulders are symmetric
        x1 = tr["post_mirror_x"]
        for v in nb_.verts:
            v.co.x = 2 * x1 - v.co.x
        bmesh.ops.reverse_faces(nb_, faces=nb_.faces)
    REPORT.setdefault("copies", []).append(dict(sizes=sizes, kept=sizes[big], scale=s,
                                                angle_deg=math.degrees((p1 - p0).angle(q1 - q0))))
    print(f"COPY: components {sizes} kept {sizes[big]}, scale {s:.3f}")
    return nb_


bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
uvl = bm.loops.layers.uv.active


def _image():
    for m in me.materials:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                return n.image


IMG = _image()
import numpy as np  # noqa: E402
PIX = np.array(IMG.pixels[:], dtype=np.float32).reshape(IMG.size[1], IMG.size[0], -1)[:, :, :3]


def vert_hsv(bm_, uvl_):
    """Per-vertex base colour (mean of its loops' texels, display sRGB) as HSV -- for the optional
    "color" gate of a delete region (a staff is brown, the beard it is fused into is cream)."""
    import colorsys
    H_, W_ = PIX.shape[:2]
    acc = {}
    for f in bm_.faces:
        for l in f.loops:
            u, v = l[uvl_].uv
            c = PIX[int((v % 1.0) * (H_ - 1)), int((u % 1.0) * (W_ - 1))]
            s_ = acc.setdefault(l.vert, [0.0, 0.0, 0.0, 0])
            s_[0] += c[0]; s_[1] += c[1]; s_[2] += c[2]; s_[3] += 1
    out = {}
    for v_, (r, g, b_, n) in acc.items():
        rgb = [max(0.0, x / n) ** (1 / 2.2) for x in (r, g, b_)]
        out[v_] = colorsys.rgb_to_hsv(*rgb)
    return out


def color_ok(hsv, gate, z=None):
    if not gate:
        return True
    if z is not None and "z" in gate and not (gate["z"][0] <= z <= gate["z"][1]):
        return False  # a gate can be limited to a height band (the cap's green, not the cloak's)
    h, s_, v_ = hsv
    return all(lo <= x <= hi for x, (lo, hi) in ((h, gate.get("h", (0, 1))), (s_, gate.get("s", (0, 1))),
                                                   (v_, gate.get("v", (0, 1)))))
pl = bm.faces.layers.int.new('patch')
stats(bm, 'input_welded')
orig = bm.copy()
orig_uvl = orig.loops.layers.uv.active
shells = []
for i, st in enumerate(CFG["stages"]):
    tag = f"{i}_{st['op']}" + (f"_{st['tag']}" if 'tag' in st else '')
    if st["op"] == "delete":
        gates = st.get("colors") or [st.get("color")]
        hsv = vert_hsv(bm, uvl) if any(gates) else {}
        dv = {v for v in bm.verts if in_region(v.co, st)
              and any(color_ok(hsv.get(v, (0, 0, 0)), g, v.co.z) for g in gates)}
        df = [f for f in bm.faces if all(v in dv for v in f.verts)]
        bmesh.ops.delete(bm, geom=df, context='FACES')
        bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context='EDGES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        REPORT.setdefault("deleted_faces", {})[tag] = len(df)
        print(f"DELETE {tag}: {len(df)} faces")
    elif st["op"] == "rip":
        # flood-fill the prop from `seed` through mesh edges, staying inside the region and (if
        # given) any of the colour gates; then delete the faces that mix prop and non-prop vertices
        # (the thin fused band), so the prop and the body are separate pieces; holes filled later.
        hsv = vert_hsv(bm, uvl)
        gates = st.get("colors") or [None]
        ok = {v for v in bm.verts if in_region(v.co, st) and any(color_ok(hsv.get(v, (0, 0, 0)), g, v.co.z) for g in gates)}
        bm.verts.ensure_lookup_table()
        seed = min(bm.verts, key=lambda v: (v.co - Vector(st["seed"])).length)
        prop = {seed}; stk = [seed]
        while stk:
            x = stk.pop()
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in prop and o in ok:
                    prop.add(o); stk.append(o)
        mixed = [f for f in bm.faces if 0 < sum(v in prop for v in f.verts) < len(f.verts)]
        bmesh.ops.delete(bm, geom=mixed, context='FACES')
        bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context='EDGES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        REPORT.setdefault("rip", {})[tag] = dict(prop_verts=len(prop), mixed_faces_removed=len(mixed))
        print(f"RIP {tag}: prop {len(prop)} verts, {len(mixed)} mixed faces removed")
    elif st["op"] == "drop_component":
        # remove whole connected pieces: the one holding the vertex nearest each seed point
        bm.verts.ensure_lookup_table()
        dead = set()
        for sd in st["seeds"]:
            v0 = min(bm.verts, key=lambda v: (v.co - Vector(sd)).length)
            if v0 in dead:
                continue
            comp = {v0}; stk = [v0]
            while stk:
                x = stk.pop()
                for e in x.link_edges:
                    o = e.other_vert(x)
                    if o not in comp:
                        comp.add(o); stk.append(o)
            dead |= comp
        bmesh.ops.delete(bm, geom=list(dead), context='VERTS')
        REPORT.setdefault("dropped", {})[tag] = len(dead)
        print(f"DROP COMPONENT {tag}: {len(dead)} verts")
    elif st["op"] == "drop_small":
        n_min = st.get("min_verts", 30)
        seen = set(); dead = []
        for v in bm.verts:
            if v in seen:
                continue
            comp = [v]; seen.add(v); i = 0
            while i < len(comp):
                for e in comp[i].link_edges:
                    o = e.other_vert(comp[i])
                    if o not in seen:
                        seen.add(o); comp.append(o)
                i += 1
            if len(comp) < n_min:
                dead += comp
        bmesh.ops.delete(bm, geom=dead, context='VERTS')
        print(f"DROP SMALL {tag}: {len(dead)} verts")
    elif st["op"] == "fill":
        for k in range(4):
            if not any(e.is_boundary for e in bm.edges):
                break
            fill_holes(bm, uvl, pl, f"{tag}{k}", poke=st.get("poke", True))
        if st.get("smooth"):
            smooth_patch(bm, uvl, pl, tag, **st["smooth"])
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
        fix_patch_uvs(bm, uvl, pl, tag)
    elif st["op"] == "tube":
        sb = bmesh.new(); suv = sb.loops.layers.uv.new('UVMap'); sb.faces.layers.int.new('patch')
        add_tube(sb, suv, nearest_uv(orig, orig_uvl, st["uv_from"]), st)
        shells.append((tag, sb))
    elif st["op"] == "mushroom":
        sb = bmesh.new(); suv = sb.loops.layers.uv.new('UVMap'); sb.faces.layers.int.new('patch')
        add_mushroom(sb, suv, nearest_uv(orig, orig_uvl, st["uv_top_from"]), nearest_uv(orig, orig_uvl, st["uv_under_from"]), st)
        shells.append((tag, sb))
    elif st["op"] == "copy":
        shells.append((tag, copy_region(orig, orig_uvl, st)))
    stats(bm, tag)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
stats(bm, 'body_final')
bm.to_mesh(me); me.update(); bm.free(); orig.free()
objs = [ob]
for tag, sb in shells:
    bmesh.ops.triangulate(sb, faces=[f for f in sb.faces if len(f.verts) > 3])
    stats(sb, f'shell_{tag}')
    m_ = bpy.data.meshes.new(tag); sb.to_mesh(m_); sb.free()
    m_.materials.append(me.materials[0])
    o_ = bpy.data.objects.new(tag, m_); bpy.context.scene.collection.objects.link(o_)
    objs.append(o_)
bpy.ops.object.select_all(action='DESELECT')
for o_ in objs:
    o_.select_set(True)
bpy.context.view_layer.objects.active = ob
bpy.ops.object.join()
fb = bmesh.new(); fb.from_mesh(me); stats(fb, 'final'); fb.free()
for a_ in list(me.attributes):
    if a_.name == 'patch':
        me.attributes.remove(a_)
REPORT["final_min_z"] = min(v.co.z for v in me.vertices)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, f'{NAME}_surgery.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, f'{NAME}_prepped.glb'), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
json.dump(REPORT, open(os.path.join(OUT, 'surgery_report.json'), 'w'), indent=1, default=str)
print("PROP SURGERY DONE")
