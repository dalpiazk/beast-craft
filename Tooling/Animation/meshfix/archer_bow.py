"""Clean bow + arrow props for the Archer (v22 round 3, producer review). Local-only (not run by CI),
like the rest of meshfix/. Scripted and deterministic (no randomness).

    blender -b --python meshfix/archer_bow.py -- CONFIG.json BODY.glb SOURCE.glb OUT_DIR

BODY.glb   -- the Archer with Meshy's bow tangle removed (meshfix/prop_surgery.py + archer_cfg.json);
SOURCE.glb -- the ORIGINAL prepped Meshy mesh (prep_mesh.py output): its painted bow wood, violet
              flames, bow string, quiver fletching and hood leaves are the colour SOURCES -- every
              new vertex takes its UV from the matching painted surface of the original, so the new
              props use the same texture (one material) in the same painted style.
Writes OUT_DIR/archer_prepped.glb (body + bow + arrow as separate closed shells), archer_bow.blend
and bow_report.json (tris per part, open/non-manifold edges, rest placement for the rig).

The props are modelled at a REST placement chosen to stay inside the body's bounding box (the rig
normalises by it): the bow stands upright beside the Archer's left side, the arrow lies beside it.
The rig never relies on the rest placement -- anim/bow_rig.py keys the "bow" bone so the grip sits in
the bow fist, the "bow_string" bone so the string's nock follows the draw hand, and the "arrow" bone so
the arrow lies nocked on the string and on the bow hand.

Bow (local frame: grip centre at the origin, limbs along +Z/-Z, belly toward -Y, string on +Y):
  two tapered limbs (6-sided elliptical sections) bending back toward the string side with a small
  recurve flick at the tips, a thicker wrapped grip, one thin string (4-sided) between the tips with
  rings at quarter points (the rig ramps the string's weight to the nock bone, so a draw pulls it into
  a clean V), and (round 3 only) violet flame wisps at both tips. Round 4 (the approved art): a
  short, pale, D-shaped self bow -- profile_exp < 2 rounds the curve, sagitta ~27 % of the limb,
  no flames on the bow.
Arrow (local frame: nock at the origin, shaft along -Y): a 6-sided shaft, a leaf-shaped head, three
  fletching vanes and a violet flame at the head; round 4 flares it (tongues rolled round the head)
  and adds a soft smoke-trail wisp streaming back over the shaft.
"""
import bpy, bmesh, sys, os, json, math, colorsys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index('--') + 1:]
CFG = json.load(open(a[0])); BODY, SOURCE, OUT = a[1], a[2], a[3]
os.makedirs(OUT, exist_ok=True)
REPORT = {}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SOURCE)
src = [o for o in bpy.data.objects if o.type == 'MESH'][0]
src.name = "Source"
bpy.ops.import_scene.gltf(filepath=BODY)
body = [o for o in bpy.data.objects if o.type == 'MESH' and o is not src][0]
body.name = "Archer"


def _image(ob):
    for m in ob.data.materials:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                return n.image


IMG = _image(src)
W, H = IMG.size
PIX = list(IMG.pixels[:])


def texel(uv):
    x = int((uv[0] % 1.0) * (W - 1)); y = int((uv[1] % 1.0) * (H - 1))
    i = (y * W + x) * 4
    # round 4: Image.pixels of this 8-bit sRGB texture are already the displayed values; round 3
    # applied an extra 1/2.2 (every colour gate read ~25 % too light -- the "pale" stave was mid-brown)
    if CFG.get("srgb_texels"):
        return [max(0.0, PIX[i + k]) for k in range(3)]
    return [max(0.0, PIX[i + k]) ** (1 / 2.2) for k in range(3)]


# ---- colour source regions on the ORIGINAL mesh -------------------------------------------------
sm = src.data
suv = sm.uv_layers.active.data
SV = [src.matrix_world @ v.co for v in sm.vertices]


def seg_d(p, a_, b_):
    ab = b_ - a_; L2 = ab.length_squared
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


class Region:
    """faces of the source mesh inside the region and inside the colour gate (HSV of the face's
    mean texel); nearest-surface lookup returns a barycentric UV"""
    def __init__(self, name, spec):
        polys, uvs = [], []
        for p in sm.polygons:
            c = sum((SV[i] for i in p.vertices), Vector()) / len(p.vertices)
            if not in_shapes(c, spec["include"]) or in_shapes(c, spec.get("exclude", [])):
                continue
            puv = [suv[li].uv.copy() for li in p.loop_indices]
            rgb = [sum(texel(u)[k] for u in puv) / len(puv) for k in range(3)]
            h, s_, v_ = colorsys.rgb_to_hsv(*rgb)
            g = spec.get("color", {})
            if not all(lo <= x <= hi for x, (lo, hi) in ((h, g.get("h", (0, 1))), (s_, g.get("s", (0, 1))), (v_, g.get("v", (0, 1))))):
                continue
            polys.append(list(p.vertices)); uvs.append(puv)
        assert polys, name
        self.polys, self.uvs = polys, uvs
        self.bvh = BVHTree.FromPolygons(SV, polys)
        REPORT.setdefault("source_faces", {})[name] = len(polys)

    def uv(self, q):
        loc, _n, fi, _d = self.bvh.find_nearest(q)
        P = [SV[i] for i in self.polys[fi]]; U = self.uvs[fi]
        # barycentric on the first triangle fan that contains the point (Meshy faces are triangles)
        a0, b0, c0 = P[0], P[1], P[2]
        v0, v1, v2 = b0 - a0, c0 - a0, loc - a0
        d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
        d20, d21 = v2.dot(v0), v2.dot(v1)
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-14:
            return U[0].copy()
        w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den; w0 = 1 - w1 - w2
        return U[0] * w0 + U[1] * w1 + U[2] * w2


R = {k: Region(k, v) for k, v in CFG["sources"].items()}


def poly_at(pts, t):
    pts = [Vector(p) for p in pts]
    L = [0.0]
    for x, y in zip(pts, pts[1:]):
        L.append(L[-1] + (y - x).length)
    s = t * L[-1]
    for i in range(len(pts) - 1):
        if s <= L[i + 1] + 1e-9:
            u = (s - L[i]) / max(L[i + 1] - L[i], 1e-9)
            return pts[i].lerp(pts[i + 1], u)
    return pts[-1]


# ---- mesh building (each part keeps, per vertex, the UV source query point) ------------------------
class Part:
    def __init__(self, name):
        self.name, self.V, self.F, self.Q = name, [], [], []   # Q: (region, query point) per vertex
        self.seeds = []   # one vertex per closed shell (the rig's component seeds)

    def v(self, co, region, q):
        self.V.append(Vector(co)); self.Q.append((region, Vector(q))); return len(self.V) - 1

    def f(self, *ids):
        self.F.append(list(ids))


def tube(part, centers, radii, sides, region, query_fn, frame_up, cap0=True, cap1=True):
    """closed tube along `centers` with (rx, ry) radii per ring; query_fn(i_ring, angle) -> query point"""
    rings = []
    n = len(centers)
    for i, c in enumerate(centers):
        t = (centers[min(i + 1, n - 1)] - centers[max(i - 1, 0)]).normalized()
        x = frame_up.cross(t)
        if x.length < 1e-6:
            x = Vector((1, 0, 0)).cross(t)
        x.normalize(); y = t.cross(x).normalized()
        rx, ry = radii[i]
        ring = []
        for k in range(sides):
            ang = 2 * math.pi * k / sides
            p = c + x * (rx * math.cos(ang)) + y * (ry * math.sin(ang))
            ring.append(part.v(p, region, query_fn(i, ang)))
        rings.append(ring)
    part.seeds.append(part.V[rings[0][0]].copy())
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(sides):
            part.f(r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k])
    if cap0:
        c0 = part.v(centers[0], region, query_fn(0, None))
        for k in range(sides):
            part.f(rings[0][(k + 1) % sides], rings[0][k], c0)
    if cap1:
        c1 = part.v(centers[-1], region, query_fn(n - 1, None))
        for k in range(sides):
            part.f(rings[-1][k], rings[-1][(k + 1) % sides], c1)
    return rings


def flame(part, base, direction, size, curl_axis, tongues, sides=5, rings=5):
    """violet flame wisp: `tongues` (length, spread angle, curl) cones from `base`, curling about curl_axis"""
    fl = CFG["flame_source_line"]
    for (ln, spread, curl) in tongues:
        d0 = (Matrix.Rotation(math.radians(spread), 3, curl_axis) @ direction).normalized()
        centers, radii = [], []
        p = base.copy(); d = d0.copy()
        for i in range(rings):
            u = i / (rings - 1)
            centers.append(p.copy())
            r = size * (1.0 - u) ** 1.2 * (0.6 + 0.4 * math.sin(math.pi * min(1.0, u * 1.6)))
            radii.append((max(r, 0.0015), max(r * 0.8, 0.0012)))
            d = (Matrix.Rotation(math.radians(curl / (rings - 1)), 3, curl_axis) @ d).normalized()
            p = p + d * (ln / (rings - 1))
        tube(part, centers, radii, sides, "flame",
             lambda i, ang, rings=rings: poly_at(fl, min(0.95, i / (rings - 1))) +
             (Vector((0, 0, 0)) if ang is None else Vector((0, math.cos(ang), math.sin(ang))) * 0.012),
             d0.orthogonal(), cap0=True, cap1=True)


B = CFG["bow"]
LH = B["length"] / 2.0
seg = B["segments_per_limb"]


def limb_y(z):
    # bend * u^profile_exp: 2 = parabolic; lower = a rounder D (more of the curve near the grip)
    u = abs(z) / LH
    return B["bend"] * u ** B.get("profile_exp", 2.0) - B.get("recurve", 0.0) * max(0.0, (u - 0.85) / 0.15) ** 2


bow = Part("bow")
zs = [LH * (-1 + 2 * i / (2 * seg)) for i in range(2 * seg + 1)]
centers = [Vector((0.0, limb_y(z), z)) for z in zs]
radii = []
for z in zs:
    u = abs(z) / LH
    if abs(z) <= B["grip_half"]:
        radii.append((B["grip_r"], B["grip_r"]))
    else:
        w = B["w_root"] + (B["w_tip"] - B["w_root"]) * u
        dpt = B["d_root"] + (B["d_tip"] - B["d_root"]) * u
        radii.append((w, dpt))
wood = CFG["wood_source_line"]
tube(bow, centers, radii, 6, "wood",
     lambda i, ang: poly_at(wood, i / (len(zs) - 1)) + (Vector((0, 0, 0)) if ang is None else
                                                         Vector((math.cos(ang), math.sin(ang), 0)) * 0.02),
     Vector((0, -1, 0)))
# grip wrap: a short sleeve over the grip, leather texels
gz = [-B["grip_half"], -B["grip_half"] * 0.33, B["grip_half"] * 0.33, B["grip_half"]]
tube(bow, [Vector((0, limb_y(z), z)) for z in gz], [(B["grip_r"] * 1.25, B["grip_r"] * 1.25)] * 4, 6, "leather",
     lambda i, ang: Vector(CFG["leather_point"]) + (Vector((0, 0, 0)) if ang is None else Vector((math.cos(ang), 0, math.sin(ang))) * 0.02),
     Vector((0, -1, 0)))
tipT = Vector((0.0, limb_y(LH), LH)); tipB = Vector((0.0, limb_y(-LH), -LH))
nb_bow = len(bow.F)
# string: 4-sided, rings at 0, .25, .5, .75, 1 (the rig ramps weight to the nock bone along it)
sv = [tipB.lerp(tipT, u) for u in (0.0, 0.25, 0.5, 0.75, 1.0)]
for p in sv:
    p.x = 0.0
string_first = len(bow.V)
tube(bow, sv, [(B["string_r"], B["string_r"])] * 5, 4, "string",
     lambda i, ang: poly_at(CFG["string_source_line"], i / 4), Vector((1, 0, 0)))
string_last = len(bow.V)
string_seed = bow.seeds.pop()
nb_string = len(bow.F) - nb_bow
# flames at both tips (round 3 only; round 4 -- the approved art -- has a plain self bow: "flames": false)
ft = (CFG["flame_tongues"])
if B.get("flames", True):
    flame(bow, tipT, Vector((0, -0.35, 1)).normalized(), B["flame_r"], Vector((1, 0, 0)), ft)
    flame(bow, tipB, Vector((0, -1, 0.45)).normalized(), B["flame_r"], Vector((1, 0, 0)),
          [(ln * 0.8, sp, -cu) for ln, sp, cu in ft])
REPORT["bow_parts_faces"] = {"limbs_grip": nb_bow, "string": nb_string, "flames": len(bow.F) - nb_bow - nb_string}

A = CFG["arrow"]
arrow = Part("arrow")
L = A["length"]
sh = [Vector((0, -L * u, 0)) for u in (0.0, 1.0)]
tube(arrow, [Vector((0, 0, 0)), Vector((0, -(L - A["head_len"] * 0.6), 0))], [(A["shaft_r"], A["shaft_r"])] * 2, 6, "wood",
     lambda i, ang: poly_at(wood, 0.3 + 0.4 * i), Vector((0, 0, 1)))
# leaf head: flattened bipyramid (leaf blade in the X-Y plane)
hb = Vector((0, -(L - A["head_len"]), 0)); ht = Vector((0, -L, 0)); hm = hb.lerp(ht, 0.42)
hw, hd = A["head_w"], A["head_w"] * 0.25
leaf = CFG["leaf_point"]
ids = [arrow.v(hb, "leaf", Vector(leaf)), arrow.v(ht, "leaf", Vector(leaf) + Vector((0.03, 0, 0))),
       arrow.v(hm + Vector((hw, 0, 0)), "leaf", Vector(leaf) + Vector((0.0, 0.0, 0.02))),
       arrow.v(hm + Vector((-hw, 0, 0)), "leaf", Vector(leaf) + Vector((0.0, 0.0, -0.02))),
       arrow.v(hm + Vector((0, 0, hd)), "leaf", Vector(leaf) + Vector((0.02, 0, 0))),
       arrow.v(hm + Vector((0, 0, -hd)), "leaf", Vector(leaf) + Vector((-0.02, 0, 0)))]
b0, t0, r_, l_, u_, d_ = ids
arrow.seeds.append(arrow.V[r_].copy())
for x, y in ((r_, u_), (u_, l_), (l_, d_), (d_, r_)):
    arrow.f(b0, y, x); arrow.f(t0, x, y)
# fletching: three thin vanes (closed thin prisms) at 120 deg near the nock
fl_pt = CFG["fletch_point"]
for k in range(3):
    ang = math.radians(90 + 120 * k)
    o = Vector((math.cos(ang), 0, math.sin(ang)))
    side = Vector((0, 1, 0)).cross(o).normalized() * 0.002
    y0, y1 = -0.02, -A["fletch_len"]
    quad = [Vector((0, y0, 0)) + o * A["shaft_r"], Vector((0, y1, 0)) + o * A["shaft_r"],
            Vector((0, y1 + 0.02, 0)) + o * A["fletch_h"], Vector((0, y0 - 0.005, 0)) + o * A["fletch_h"] * 1.15]
    va = [arrow.v(q + side, "fletch", Vector(fl_pt) + Vector((0, 0.01 * i, 0))) for i, q in enumerate(quad)]
    vb = [arrow.v(q - side, "fletch", Vector(fl_pt) + Vector((0, 0.01 * i, 0))) for i, q in enumerate(quad)]
    arrow.seeds.append(arrow.V[va[0]].copy())
    arrow.f(*va); arrow.f(*reversed(vb))
    for i in range(4):
        j = (i + 1) % 4
        arrow.f(va[j], va[i], vb[i], vb[j])
if "flame_tongues" in A:
    # round 4: a flaring violet flame wrapped round the head -- tongues (length, spread, curl, roll
    # about the shaft) from just behind the head's widest point, sweeping back and up
    for ln, sp, cu, roll in A["flame_tongues"]:
        axis = Matrix.Rotation(math.radians(roll), 3, Vector((0, 1, 0))) @ Vector((1, 0, 0))
        d = Matrix.Rotation(math.radians(roll), 3, Vector((0, 1, 0))) @ Vector((0, 0.55, 1)).normalized()
        flame(arrow, hm + Vector((0, 0.012, 0)), d, A["flame_r"], axis, [(ln, sp, cu)], sides=4, rings=4)
else:
    flame(arrow, ht + Vector((0, 0.01, 0)), Vector((0, -1, 0.5)).normalized(), A["flame_r"], Vector((1, 0, 0)),
          [(ln * 0.45, sp, cu) for ln, sp, cu in ft[:2]], sides=4, rings=4)
if "smoke" in A:
    # round 4: a soft smoke-trail wisp streaming back from the flame over the shaft (pale lilac)
    sk = A["smoke"]; n = sk["rings"]
    base = hm + Vector((0, 0.03, A["flame_r"] * 0.9))
    cs = [base + Vector((sk["sway"] * math.sin(math.pi * 2 * sk["waves"] * i / (n - 1)), sk["length"] * i / (n - 1),
                         sk["rise"] * (i / (n - 1)) ** 1.5)) for i in range(n)]
    rr = [(max(0.002, sk["r"] * math.sin(math.pi * min(1.0, 0.15 + i / (n - 1)))),) * 2 for i in range(n)]
    smoke_reg = "smoke" if "smoke" in R else "flame"
    tube(arrow, cs, rr, 4, smoke_reg,
         lambda i, ang: poly_at(CFG["flame_source_line"], 0.9) + (Vector((0, 0, 0)) if ang is None else
                                                                   Vector((0, math.cos(ang), math.sin(ang))) * 0.01),
         Vector((1, 0, 0)), cap0=True, cap1=True)


def build(part, origin, name):
    me = bpy.data.meshes.new(name); bm_ = bmesh.new()
    vs = [bm_.verts.new(origin + p) for p in part.V]
    uvl = bm_.loops.layers.uv.new("UVMap")
    for f in part.F:
        try:
            face = bm_.faces.new([vs[i] for i in f])
        except ValueError:
            continue
        uvq = [R[part.Q[i][0]].uv(part.Q[i][1]) for i in f]
        spread = max((x - y).length for x in uvq for y in uvq)
        if spread > CFG.get("uv_spread_max", 0.06):
            uvq = [uvq[0]] * len(uvq)
        for l, u in zip(face.loops, uvq):
            l[uvl].uv = u
    bmesh.ops.remove_doubles(bm_, verts=bm_.verts, dist=1e-6)
    bmesh.ops.triangulate(bm_, faces=bm_.faces)
    bmesh.ops.recalc_face_normals(bm_, faces=bm_.faces)
    nb = sum(1 for e in bm_.edges if e.is_boundary); nm = sum(1 for e in bm_.edges if not e.is_manifold)
    REPORT[name] = {"tris": len(bm_.faces), "verts": len(bm_.verts), "open_edges": nb, "non_manifold_edges": nm}
    print(f"PART {name}: {REPORT[name]}")
    bm_.to_mesh(me); bm_.free()
    me.materials.append(body.data.materials[0])
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob


G0 = Vector(CFG["rest"]["bow_grip"]); N0 = Vector(CFG["rest"]["arrow_nock"])
# round 4: the bow's REST yaw about the vertical (rig: anim/bow_rig.py "rest_yaw"). Its poses span
# yaw ~-115..0 deg; resting midway keeps the keyed bone's XYZ Euler twist inside +-90 (no gimbal).
RY = Matrix.Rotation(math.radians(CFG["rest"].get("bow_yaw", 0.0)), 3, "Z")
bow.V = [RY @ v for v in bow.V]; bow.seeds = [RY @ v for v in bow.seeds]
tipT, tipB, string_seed = RY @ tipT, RY @ tipB, RY @ string_seed
ob_bow = build(bow, G0, "bow")
ob_arrow = build(arrow, N0, "arrow")
REPORT["rest"] = {"bow_grip": list(G0), "bow_tip_top": list(G0 + tipT), "bow_tip_bottom": list(G0 + tipB),
                  "string_nock": list(G0 + RY @ Vector((0, limb_y(LH), 0))), "arrow_nock": list(N0),
                  "bow_yaw": CFG["rest"].get("bow_yaw", 0.0),
                  "arrow_head": list(N0 + ht), "arrow_length": L}
REPORT["shell_seeds"] = {"bow": [list(G0 + p) for p in bow.seeds], "string": list(G0 + string_seed),
                         "arrow": [list(N0 + p) for p in arrow.seeds]}
bpy.data.objects.remove(src)
for o in bpy.data.objects:
    o.select_set(o in (body, ob_bow, ob_arrow))
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
mz = min((body.matrix_world @ v.co).z for v in body.data.vertices)
REPORT["min_z"] = mz
assert mz >= -1e-6, f"props below the floor at rest: {mz}"
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "archer_bow.blend"))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, "archer_prepped.glb"), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
json.dump(REPORT, open(os.path.join(OUT, "bow_report.json"), "w"), indent=1, default=str)
print("ARCHER BOW DONE", json.dumps({k: REPORT[k] for k in ("bow", "arrow", "bow_parts_faces")}))
