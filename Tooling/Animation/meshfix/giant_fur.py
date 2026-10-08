"""Giant fur back (v23, enemies batch 3). Local-only (not run by CI), like treant_surgery.py.

    blender -b --python meshfix/giant_fur.py -- GIANT_PREPPED.glb OUT_DIR [BODY_TARGET_TRIS]

Input: prep_mesh.py's giant_prepped.glb (--no-ground-sheet, native coords: head = -Y, up = +Z).
Output (OUT_DIR): giant_prepped.glb (the new "prepped" mesh rig_creature.py takes), giant_fur.blend
and fur_report.json (stats after every step, the config and the mapping counts).

Why: Meshy's image-to-3D only saw the front of the approved sprite, so the Giant's back (and the
back half of each flank and the back of the head) is a smooth, featureless blob, while the front
is a shaggy coat of fur locks. This gives the back the SAME fur, taken from the front -- the same
idea as treant_surgery.py's crown back (mirror and vary the front onto the back), done here as
surface relief + re-pointed UVs, so the body stays one watertight shell (no extra shells to skin):

  clean    the 3 non-manifold edges (a triple-face sliver at the left cheek) are removed and the
           holes filled (fill faces take a neighbour's texel). That cut frees a 66-vertex cheek
           lock, kept as its own closed shell (shards under 10 vertices would be dropped).
  relief   fur relief field: r = (P - S(P)) . N_S, S a Taubin (shrink-free) smoothed copy of the
           mesh (150 passes), then low-passed over 3 rings to clump scale (the back's grid cannot
           carry finer locks). The coat measures |r| up to ~4 cm; the smooth back's std is about
           half the coat's -- too weak a contrast to gate on, so the region is geometric.
  region   back weight w per vertex: azimuth about the body's vertical axis (0 = straight back),
           full to 55 deg and gone by 85 deg below z 0.9, opening to 80 / 115 deg above z 1.3 (the
           smooth patch reaches round the upper flanks to the cheek fur); a crown term carries it
           over the top of the head to just behind the brow; faded in above the floor fringe
           (z 0.08 -> 0.25); zero on the violet cape (texture-gated, dilated 2 rings) and on real
           protrusions (|own r| 1.5 -> 3 cm: the cat ears); relaxed over the mesh.
  subdiv   back faces (median edge 7 cm vs the coat's 3.3 cm) subdivided once.
  map      each back vertex is mapped onto the coat below the face (z 0.25 .. 1.15, within 80 deg
           of straight ahead, no cape): azimuth x0.8 into the coat's LIT (+X) side for both flanks
           (mirrored through the coronal plane, and for the left flank also through the sagittal
           plane -- the coat's left half is painted in shadow and made a hard dark/pale seam), the
           same height (keeps the coat's light-top / dark-base gradient), folded back down into
           the pale upper coat above it for the back of the head, plus a low-frequency noise warp
           and a 2.0..3.0 relief-gain noise so no stretch repeats the front exactly.
  displace P += N * clamp(r_src * gain, +-7 cm) * w; faces with every w >= 0.15 take the coat's
           texels at the mapped points (barycentric in the coat face it lands in); a face whose
           corners land in different UV charts takes one texel (its centre's).
  decimate whole mesh Collapse-decimated back to TARGET_TRIS (default 15900, under the 16k budget).
Final: re-checked (0 open / 0 non-manifold edges), re-grounded (lowest vertex z = 0), exported.

Round 2 (lead review: the back still read as a bald dome, a hard-edged pale crown patch, a flat dark
swipe paw). Replaces the coat-texel copy and the decimate target above:
  paws     the two Meshy front paws (dark-painted lobes under the bib) are blended into the Taubin
           surface (z < 0.27, within 0.2 of each paw centre) -- modelled arms replace them.
  palette  a 12 x 4 palette of the coat's own painted colours (lightness quantiles x warmth) plus an
           ivory claw patch is painted into a free 32 x 28 px atlas block. The back and the paw stubs
           read it per vertex: the dark undertone deep in the region (the gaps between clumps),
           blended toward the palette entry nearest the ORIGINAL texel of the closest outside vertex
           at the region's edge (no hard crown edge).
  decimate the body to 11000 tris, the back weighted to take most of it (vertex group).
  arms     per front paw one closed lofted shell, shoulder (inside the body) -> elbow -> wrist ->
           a rounded paw with a flat sole, fur-coloured; four ivory claw pyramids; 9-11 fur locks
           on the forearm and a wrist ruff (none on the side facing the other arm).
  clumps   ~265 fur locks (11 verts, 18 tris each, closed): Poisson-disk roots (0.105) on the back,
           flanks (> 68 deg from straight ahead) and crown; each lock falls down/back, re-projected
           onto the surface along its length (hugs the body), 30-42 cm long, a random bend and
           twist; palette dark at the root and underside, lighter along the ridge and tip (lighter
           higher up). Locks that would reach below z 0.02 are dropped.
  The rig's landmark file binds the arms rigidly (rigid_parts component seeds = the toe-tip cap
  vertices, printed as ARMS) and every lock/claw to the shell under it (shell_bind).
Deterministic (fixed noise and numpy seeds; no random module).
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, kdtree, noise
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = a[0], a[1]
TARGET = int(a[2]) if len(a) > 2 else 11000
os.makedirs(OUT, exist_ok=True)
REPORT = {}
CFG = dict(az_full=55.0, az_zero=85.0, az_full_hi=80.0, az_zero_hi=115.0, az_open_z=(0.9, 1.3),
           crown=(-0.42, -0.25), crown_z=1.6, own_relief=(0.015, 0.03), max_disp=0.07, z_in=(0.08, 0.25), src_z=(0.25, 1.15), src_az=80.0,
           back_z=(0.10, None), az_scale=0.8, warp=0.07, warp_freq=2.2, gain=(2.0, 3.0), relief_smooth=3,
           gain_freq=3.0, taubin_iters=150, uv_tol=0.03, uv_w=0.15,
           # round 2 (lead review): the old Meshy paws, the palette, the arms and the fur clumps
           paw_centres=((-0.20, -0.38), (0.06, -0.40)), paw_r=(0.13, 0.20), paw_z=(0.15, 0.27),
           pal_nu=12, pal_nv=4, pal_q=(0.03, 0.97), blend_w=0.6,
           clump_spacing=0.105, clump_az=68.0, clump_w_min=0.4, clump_z_min=0.16, clump_seed=7)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.name = 'Giant'
me = ob.data
img = None
for m in me.materials:
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                img = n.image
TW, TH = img.size
TEX = np.array(img.pixels[:], dtype=np.float32).reshape(TH, TW, -1)[:, :, :3]


def texel(uv):
    x = int(min(TW - 1, max(0, uv[0] * TW)))
    y = int(min(TH - 1, max(0, uv[1] * TH)))
    return TEX[y, x]


def is_violet(c):
    r, g, b = c
    return b > g * 1.08 and r > g * 0.95 and b > 0.12


def stats(bm, tag):
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
    r = dict(verts=len(bm.verts), tris=sum(len(f.verts) - 2 for f in bm.faces), boundary_edges=nb,
             non_manifold_edges=nm, components=len(comps), component_sizes=sorted(comps, reverse=True)[:4])
    print(f"STATS {tag}: {r}")
    REPORT[tag] = r
    return r


bm = bmesh.new(); bm.from_mesh(me)
uvl = bm.loops.layers.uv.active
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.verts.index_update()
stats(bm, 'input_welded')

# ---- clean: drop faces on non-manifold edges, fill the holes (ring texel UVs) ----------------
for it in range(4):
    bad = [e for e in bm.edges if not e.is_manifold]
    if not bad:
        break
    faces = {f for e in bad if len(e.link_faces) > 2 for f in e.link_faces}
    if faces:
        bmesh.ops.delete(bm, geom=list(faces), context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bnd = [e for e in bm.edges if e.is_boundary]
    res = bmesh.ops.holes_fill(bm, edges=bnd, sides=64)
    bmesh.ops.triangulate(bm, faces=res['faces'])
    bm.faces.ensure_lookup_table()
bm.normal_update()
# new fill faces: one texel from a neighbour (holes_fill leaves their UVs at 0,0)
for f in bm.faces:
    if all(l[uvl].uv.length < 1e-9 for l in f.loops):
        nb = [l2 for v in f.verts for l2 in v.link_loops if l2.face is not f and l2[uvl].uv.length > 1e-9]
        if nb:
            uv = nb[0][uvl].uv.copy()
            for l in f.loops:
                l[uvl].uv = uv
        f.smooth = True
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
# the deleted triple-face sliver can leave a few-vertex shard behind (dropped); a real piece it
# joined (a 66-vertex cheek lock) stays as its own closed shell
bm.verts.ensure_lookup_table()
seen = {}; cid = 0
for v in bm.verts:
    if v in seen:
        continue
    st = [v]; seen[v] = cid
    while st:
        x = st.pop()
        for e in x.link_edges:
            o = e.other_vert(x)
            if o not in seen:
                seen[o] = cid; st.append(o)
    cid += 1
sizes = [0] * cid
for v, c in seen.items():
    sizes[c] += 1
main = max(range(cid), key=lambda c: sizes[c])
drop = [v for v, c in seen.items() if c != main and sizes[c] < 10]
if drop:
    print(f"CLEAN dropped {len(drop)} shard vertices ({sorted(sizes)[:-1]})")
    bmesh.ops.delete(bm, geom=drop, context='VERTS')
bm.verts.index_update(); bm.faces.index_update()
stats(bm, 'clean')


# ---- relief field --------------------------------------------------------------------------
def vertex_arrays(bm):
    bm.verts.ensure_lookup_table()
    P = np.array([v.co[:] for v in bm.verts])
    nbr = [[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]
    return P, nbr


def lap(X, nbr):
    return np.array([X[n].mean(0) if n else X[i] for i, n in enumerate(nbr)]) - X


def taubin(P, nbr, iters):
    X = P.copy()
    for _ in range(iters):
        X = X + 0.5 * lap(X, nbr)
        X = X - 0.53 * lap(X, nbr)
    return X


def vnormals(X, bm):
    N = np.zeros_like(X)
    for f in bm.faces:
        ids = [v.index for v in f.verts]
        n = np.cross(X[ids[1]] - X[ids[0]], X[ids[2]] - X[ids[0]])
        N[ids] += n
    return N / np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-12)


P, nbr = vertex_arrays(bm)
S = taubin(P, nbr, CFG['taubin_iters'])
NS = vnormals(S, bm)
R = ((P - S) * NS).sum(1)
R_raw = R.copy()
for _ in range(CFG['relief_smooth']):  # keep clump-scale locks (the 3.6 cm back grid can't carry finer)
    R = 0.5 * R + 0.5 * np.array([R[n].mean() if n else R[i] for i, n in enumerate(nbr)])
# 2-ring mean |r| = roughness
A1 = np.abs(R_raw)
rough = np.array([A1[[i] + n].mean() for i, n in enumerate(nbr)])
rough = np.array([rough[[i] + n].mean() for i, n in enumerate(nbr)])
yc = float((P[:, 1].min() + P[:, 1].max()) / 2)
Z1 = float(P[:, 2].max())
back_ref = rough[(P[:, 1] > 0.35) & (P[:, 2] > 0.4)]
front_ref = rough[(P[:, 1] < -0.45) & (P[:, 2] > 0.3) & (P[:, 2] < 1.1)]
r_lo, r_hi = float(np.percentile(back_ref, 75)), float(np.percentile(front_ref, 40))
print(f"RELIEF yc={yc:.3f} rough back p75={r_lo:.4f} front p40={r_hi:.4f} |r| front max={A1[P[:,1]<-0.4].max():.4f}")

# vertex colour (any loop) for the cape gate
vcol = np.zeros((len(bm.verts), 3))
for v in bm.verts:
    if v.link_loops:
        vcol[v.index] = texel(v.link_loops[0][uvl].uv)
violet = np.array([is_violet(c) for c in vcol])
# dilate the cape mask by 2 rings
for _ in range(2):
    violet = violet | np.array([violet[n].any() if n else False for n in nbr])


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def back_weight(P, rough_v, violet_v):
    az = np.degrees(np.abs(np.arctan2(P[:, 0], P[:, 1] - yc)))
    # the smooth patch reaches further round the upper body (to the cheek fur): the azimuth limits
    # open up from (az_full, az_zero) below z_lo to (az_full_hi, az_zero_hi) above z_hi
    h = sstep(CFG['az_open_z'][0], CFG['az_open_z'][1], P[:, 2])
    a0 = CFG['az_full'] + h * (CFG['az_full_hi'] - CFG['az_full'])
    a1 = CFG['az_zero'] + h * (CFG['az_zero_hi'] - CFG['az_zero'])
    w = 1.0 - np.clip((az - a0) / (a1 - a0), 0, 1) ** 2 * (3 - 2 * np.clip((az - a0) / (a1 - a0), 0, 1))
    # the crown: over the top of the head the fur runs forward to just behind the brow
    crown = sstep(CFG['crown'][0], CFG['crown'][1], P[:, 1]) * sstep(CFG['crown_z'] - 0.1, CFG['crown_z'], P[:, 2])
    w = np.maximum(w, crown)
    w *= sstep(CFG['z_in'][0], CFG['z_in'][1], P[:, 2])
    w[violet_v] = 0.0
    return w


W = back_weight(P, rough, violet)
# keep real protrusions (the cat ears, the cape tips) as they are: no fur relief on a vertex that
# already stands far out of the smooth shape (displacing a thin ear along its own normals bloats it)
W *= 1.0 - sstep(CFG['own_relief'][0], CFG['own_relief'][1], np.abs(R_raw))
for _ in range(6):  # relax the weight field over the surface
    W = 0.5 * W + 0.5 * np.array([W[n].mean() if n else W[i] for i, n in enumerate(nbr)])
W[violet] = 0.0
print(f"REGION back verts w>0.5: {(W > 0.5).sum()}  w>0.05: {(W > 0.05).sum()}  cape verts {violet.sum()}")


# ---- round 2: the Meshy front paws are smoothed away (modelled arms replace them, see below) -----
def paw_weight(P):
    z = sstep(CFG['paw_z'][0], CFG['paw_z'][1], P[:, 2])
    d = np.min([np.hypot(P[:, 0] - c[0], P[:, 1] - c[1]) for c in CFG['paw_centres']], axis=0)
    return (1.0 - z) * (1.0 - sstep(CFG['paw_r'][0], CFG['paw_r'][1], d))


WP = paw_weight(P)
WP[violet] = 0.0
DP = WP[:, None] * (S - P)
P = P + DP
for v in bm.verts:
    if WP[v.index] > 0:
        v.co = Vector(P[v.index])
bm.normal_update()
print(f"PAWS smoothed {(WP > 0.05).sum()} vertices (max move {np.linalg.norm(DP, axis=1).max():.3f})")

# ---- source (front coat) faces in (azimuth*R, z) parameter space ------------------------------
RN = 0.75
bm.faces.ensure_lookup_table()
src = []
for f in bm.faces:
    ids = [v.index for v in f.verts]
    c = P[ids].mean(0)
    azf = math.degrees(math.atan2(c[0], -(c[1] - yc)))
    if not (CFG['src_z'][0] <= c[2] <= CFG['src_z'][1] and abs(azf) <= CFG['src_az']):
        continue
    if violet[ids].any() or W[ids].max() > 0.05:
        continue
    q = []
    for v, l in zip(f.verts, f.loops):
        p = P[v.index]
        q.append((math.radians(math.degrees(math.atan2(p[0], -(p[1] - yc)))) * RN, p[2],
                  R[v.index], tuple(l[uvl].uv)))
    src.append(q)
kd = kdtree.KDTree(len(src))
for i, q in enumerate(src):
    kd.insert(Vector((sum(t[0] for t in q) / 3, sum(t[1] for t in q) / 3, 0.0)), i)
kd.balance()
print(f"SOURCE coat faces: {len(src)}")


def bary(p, q):
    (x1, y1), (x2, y2), (x3, y3) = [(t[0], t[1]) for t in q]
    d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
    if abs(d) < 1e-14:
        return None
    l1 = ((y2 - y3) * (p[0] - x3) + (x3 - x2) * (p[1] - y3)) / d
    l2 = ((y3 - y1) * (p[0] - x3) + (x1 - x3) * (p[1] - y3)) / d
    return (l1, l2, 1 - l1 - l2)


def lookup(u, v):
    """-> (relief, uv, source face id) at parameter point (u, v) of the front coat"""
    best = None
    for co, i, d in kd.find_n(Vector((u, v, 0.0)), 6):
        b = bary((u, v), src[i])
        if b is None:
            continue
        m = min(b)
        if best is None or m > best[0]:
            best = (m, i, b)
        if m >= 0:
            break
    _, i, b = best
    b = np.clip(np.array(b), 0, None); b = b / b.sum()
    q = src[i]
    r = sum(bb * t[2] for bb, t in zip(b, q))
    uv = (sum(bb * t[3][0] for bb, t in zip(b, q)), sum(bb * t[3][1] for bb, t in zip(b, q)))
    return r, uv, i


# ---- round 2: a fur palette painted into a free atlas block ------------------------------------
# The back no longer copies the coat's texels (they are shared with the front, so they cannot be
# blended or darkened): every new surface (the back, the smoothed paw stubs, the fur clumps, the
# arms) reads a small palette of the coat's OWN painted colours -- NU lightness bins (coat texel
# quantiles, darkest = the shadowed undertone) x NV warmth bins -- so a per-vertex palette
# coordinate interpolates smoothly (no texel seams) and can be blended into the original texels.
NU, NV = CFG['pal_nu'], CFG['pal_nv']
BW, BH = 32, 28                       # block px: palette 2 px/bin + 4 px replicated pad, claw rows
G = 128
cell = TW // G
cov = np.zeros((G, G), bool)
cc_ = (np.arange(G) + 0.5) / G
for f in bm.faces:
    uv = np.array([l[uvl].uv[:] for l in f.loops])
    for k in range(1, len(uv) - 1):
        t = uv[[0, k, k + 1]]
        lo = np.floor(t.min(0) * G).astype(int).clip(0, G - 1)
        hi = np.floor(t.max(0) * G).astype(int).clip(0, G - 1)
        X, Y = np.meshgrid(cc_[lo[0]:hi[0] + 1], cc_[lo[1]:hi[1] + 1])
        (x1, y1), (x2, y2), (x3, y3) = t
        d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
        for (x, y) in t:  # the corners' own cells too
            cov[min(G - 1, int(y * G)), min(G - 1, int(x * G))] = True
        if abs(d) < 1e-14:
            continue
        l1 = ((y2 - y3) * (X - x3) + (x3 - x2) * (Y - y3)) / d
        l2 = ((y3 - y1) * (X - x3) + (x1 - x3) * (Y - y3)) / d
        cov[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] |= (l1 >= -0.3) & (l2 >= -0.3) & (1 - l1 - l2 >= -0.3)
need_w, need_h = -(-BW // cell) + 2, -(-BH // cell) + 2
free = [(y, x) for y in range(1, G - need_h) for x in range(1, G - need_w)
        if not cov[y:y + need_h, x:x + need_w].any()]
if not free:
    raise SystemExit("giant_fur.py: no free atlas block for the fur palette")
cy, cx = free[0]
X0, Y0 = (cx + 1) * cell, (cy + 1) * cell
cols = []
for q in src:
    for t in q:
        cols.append(texel(t[3]))
cols = np.array(cols)
lum = cols @ np.array([0.3, 0.59, 0.11])
warm = (cols[:, 0] - cols[:, 2]) / np.maximum(lum, 1e-3)
qs = np.quantile(lum, np.linspace(CFG['pal_q'][0], CFG['pal_q'][1], NU + 1))
PAL = np.zeros((NU, NV, 3))
for bu in range(NU):
    m = (lum >= qs[bu]) & (lum <= qs[bu + 1])
    wq = np.quantile(warm[m], np.linspace(0, 1, NV + 1))
    for bv in range(NV):
        mm = m & (warm >= wq[bv]) & (warm <= wq[bv + 1])
        PAL[bu, bv] = np.median(cols[mm], axis=0)
CLAW_RGB = np.array([0.93, 0.91, 0.84])
px = np.array(img.pixels[:], dtype=np.float32).reshape(TH, TW, -1)
for j in range(BH):
    for i in range(BW):
        if j < 18:
            px[Y0 + j, X0 + i, :3] = PAL[min(NU - 1, max(0, (i - 4) // 2)), min(NV - 1, max(0, (j - 4) // 2))]
        else:
            px[Y0 + j, X0 + i, :3] = CLAW_RGB
img.pixels = px.ravel().tolist()
img.update()
img.pack()
TEX = px[:, :, :3]
CLAW_UV = ((X0 + BW / 2) / TW, (Y0 + 23) / TH)
print(f"PALETTE {NU}x{NV} at px ({X0},{Y0}) lum {lum.min():.2f}..{lum.max():.2f}")


def pal_uv(u, v):
    u = min(1.0, max(0.0, u)); v = min(1.0, max(0.0, v))
    return ((X0 + 5 + u * (2 * NU - 2)) / TW, (Y0 + 5 + v * (2 * NV - 2)) / TH)


PAL_FLAT = PAL.reshape(-1, 3)


def pal_match(c):
    k = int(np.argmin(((PAL_FLAT - c) ** 2).sum(1)))
    return (k // NV) / (NU - 1), (k % NV) / (NV - 1)


# ---- subdivide the back once ---------------------------------------------------------------
old_kd = kdtree.KDTree(len(P))
for i, p in enumerate(P):
    old_kd.insert(Vector(p), i)
old_kd.balance()
sub_faces = [f for f in bm.faces if min(W[v.index] for v in f.verts) > 0.02]
edges = list({e for f in sub_faces for e in f.edges})
bmesh.ops.subdivide_edges(bm, edges=edges, cuts=1, use_grid_fill=True, smooth=0.0)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
bm.verts.index_update(); bm.faces.index_update(); bm.normal_update()
stats(bm, 'subdivided')

P2 = np.array([v.co[:] for v in bm.verts])
W2 = np.zeros(len(P2))
WP2 = np.zeros(len(P2))
for v in bm.verts:  # weight from the nearest original vertices (inverse-distance, 3 nearest)
    hits = old_kd.find_n(v.co, 3)
    ws = np.array([1.0 / max(d, 1e-6) for _, _, d in hits])
    W2[v.index] = float((ws * np.array([W[i] for _, i, _ in hits])).sum() / ws.sum())
    WP2[v.index] = float((ws * np.array([WP[i] for _, i, _ in hits])).sum() / ws.sum())
N2 = np.array([v.normal[:] for v in bm.verts])
z0b = CFG['back_z'][0]
z1b = Z1
seed = Vector((17.3, 4.1, 9.7))


def map_point(p):
    # 0 = straight back. Both flanks read the coat's LIT (+X) side: mirrored through the coronal
    # plane (the right flank) and also through the sagittal plane (the left flank) -- the coat's
    # left side is painted in shadow, and copying that shadow onto the left back made a hard
    # dark/pale seam against the pale original upper flank.
    az = abs(math.atan2(p[0], p[1] - yc))
    u = az * CFG['az_scale'] * RN
    # same height on the coat (keeps the coat's light-top / dark-base gradient), folded back down
    # into the upper coat (the pale beard) above it for the back of the head
    lo, hi = CFG['src_z']
    v = p[2] if p[2] <= hi else hi - 0.5 * (p[2] - hi)
    v = max(lo, v)
    q = Vector(p) * CFG['warp_freq'] + seed
    u += CFG['warp'] * noise.noise(q)
    v += CFG['warp'] * 0.6 * noise.noise(q + Vector((31.0, 0, 0)))
    v = max(CFG['src_z'][0], min(CFG['src_z'][1], v))
    return u, v


g0, g1 = CFG['gain']
MAP = {}
for v in bm.verts:
    w = W2[v.index]
    if w <= 1e-3:
        continue
    p = P2[v.index]
    u, vv = map_point(p)
    r, uv, fi = lookup(u, vv)
    gn = g0 + (g1 - g0) * 0.5 * (1 + noise.noise(Vector(p) * CFG['gain_freq'] + seed * 2))
    MAP[v.index] = (r, uv, fi)
    d = max(-CFG['max_disp'], min(CFG['max_disp'], r * gn))
    v.co = Vector(p + N2[v.index] * d * w)
print(f"DISPLACED {len(MAP)} vertices")

# ---- UVs (round 2): the back and the paw stubs read the palette, blended into the coat -------
# Every vertex of the new region gets a palette coordinate: deep in the region the dark fur
# undertone (the gaps between the clumps; lighter higher up), and toward the region's edge the
# palette entry nearest the ORIGINAL texel of the closest outside vertex, so the colour runs
# continuously into the untouched coat (v23's crown edge was a hard straight line: copied coat
# texels against the original pale crown).
WC = np.maximum(W2, WP2)
bm.verts.ensure_lookup_table()
outside = [v.index for v in bm.verts if WC[v.index] < CFG['uv_w']]
okd = kdtree.KDTree(len(outside))
for i in outside:
    okd.insert(bm.verts[i].co, i)
okd.balance()
bm.verts.ensure_lookup_table()
vtex = {}
for v in bm.verts:
    if v.link_loops:
        vtex[v.index] = texel(v.link_loops[0][uvl].uv)
VUV = {}
for v in bm.verts:
    if WC[v.index] < CFG['uv_w']:
        continue
    p = P2[v.index]
    _, j_, _ = okd.find(v.co)
    mu, mv = pal_match(vtex.get(j_, PAL[NU // 2, 1]))
    q = Vector(p) * 3.0 + seed
    gu = 0.12 + 0.28 * sstep(0.1, 1.6, p[2]) + 0.22 * sstep(1.3, 1.75, p[2]) + 0.08 * noise.noise(q)
    gv = 0.5 + 0.45 * noise.noise(q * 0.8 + Vector((5.0, 0, 0)))
    t = float(sstep(CFG['uv_w'], CFG['blend_w'], WC[v.index]))
    VUV[v.index] = pal_uv(mu + (gu - mu) * t, mv + (gv - mv) * t)
nuv = 0
for f in bm.faces:
    ids = [v.index for v in f.verts]
    if any(i not in VUV for i in ids):
        continue
    for l, i in zip(f.loops, ids):
        l[uvl].uv = VUV[i]
    nuv += 1
nfallback = 0
print(f"UV faces on the palette {nuv}")
bm.normal_update()
stats(bm, 'displaced')
bm.to_mesh(me); me.update()
bm.free()

# ---- decimate to budget ------------------------------------------------------------------
cur = len(me.polygons)
if cur > TARGET:
    bpy.context.view_layer.objects.active = ob
    md = ob.modifiers.new('dec', 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.ratio = TARGET / cur
    md.use_collapse_triangulate = True
    # round 2: the back's surface now sits under the fur clumps -- it takes most of the
    # decimation (vertex-group weight 1), the front coat (weight 0.25) keeps its locks
    vg = ob.vertex_groups.new(name='dec')
    for i, w in enumerate(WC):
        vg.add([i], float(0.25 + 0.75 * min(1.0, w / 0.5)), 'REPLACE')
    md.vertex_group = 'dec'
    md.vertex_group_factor = 1.0
    bpy.ops.object.modifier_apply(modifier='dec')
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
mz = min(v.co.z for v in bm.verts)
for v in bm.verts:
    v.co.z -= mz
bm.normal_update()
stats(bm, 'body')
uvl = bm.loops.layers.uv.active
bm.verts.ensure_lookup_table()
BODY_VERTS = [(v.co.copy(), v.normal.copy()) for v in bm.verts if v.link_faces]
BODY_TRIS = [[v.co.copy() for v in f.verts] for f in bm.faces]
BVH = BVHTree.FromBMesh(bm)


# ---- round 2: modelled arms + fur-clump shells -------------------------------------------------
def shell(verts, tris, uvs):
    """New closed shell: verts (list of 3-vectors), tris (index triples), uvs (per vertex)."""
    bv = [bm.verts.new(Vector(c)) for c in verts]
    fs = []
    for t in tris:
        f = bm.faces.new([bv[k] for k in t])
        f.smooth = True
        for l, k in zip(f.loops, t):
            l[uvl].uv = uvs[k]
        fs.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    return bv


def loft(rings, cap0, cap1):
    """Triangles of a closed tube: rings (lists of n vertex ids) and centre-vertex end caps."""
    n = len(rings[0]); T = []
    for a_, b_ in zip(rings[:-1], rings[1:]):
        for j in range(n):
            j2 = (j + 1) % n
            T += [(a_[j], b_[j], b_[j2]), (a_[j], b_[j2], a_[j2])]
    for j in range(n):
        T.append((cap0, rings[0][(j + 1) % n], rings[0][j]))
        T.append((cap1, rings[-1][j], rings[-1][(j + 1) % n]))
    return T


def clump(root, n, t, L, W, lift, th, lc, hv, embed=0.015, surf=None, bend=0.0):
    """One fur lock: a long, tapering, triangle-section strand rooted (embedded) in the surface at
    `root` (normal n) and lying along it in direction t: each spine point is re-projected onto the
    surface (`surf`, a BVH; None = a straight lock, the arm fur) and lifted off it by a small
    offset, so the lock hugs the round body instead of sticking out like a scale. 11 verts, 18 tris.
    Palette: the dark undertone at the root and on the underside, lighter (lc) along the ridge and
    toward the tip."""
    n = Vector(n).normalized(); t = Vector(t).normalized()
    root = Vector(root)
    spine = []
    for k, off in ((0.0, -embed), (0.36, lift), (0.7, lift * 0.85), (1.0, lift * 0.45)):
        q = root + t * (L * k) + n.cross(t) * (bend * L * k * k)
        nn = n
        if surf is not None and k > 0:
            hit = surf.find_nearest(q)
            if hit[0] is not None:
                q, nn = hit[0], hit[1].normalized()
        else:
            q = q + n * 0.0
        spine.append((q + nn * off, nn))
    V, U, rings = [], [], []
    for (c, nn), (c2, _), ws, ul in zip(spine[:3], spine[1:], (0.8, 1.0, 0.62), (None, 0.0, 0.06)):
        tk = (c2 - c).normalized()
        b = nn.cross(tk).normalized()
        ids = []
        for off, uu in ((b * W * ws - nn * th * 0.45, lc - 0.1), (nn * th * ws, lc),
                        (-b * W * ws - nn * th * 0.45, lc - 0.1)):
            ids.append(len(V)); V.append(c + off)
            U.append(pal_uv(0.1 if ul is None else uu + ul, hv))
        rings.append(ids)
    V.append(spine[0][0] - spine[0][1] * 0.01 - t * 0.02); U.append(pal_uv(0.06, hv)); c0 = len(V) - 1
    V.append(spine[3][0]); U.append(pal_uv(lc + 0.02, hv)); c1 = len(V) - 1
    if min(q.z for q in V) < 0.02:  # never through the floor (the ground gate)
        return None
    return shell(V, loft(rings, c0, c1), U)


ARMS = {}
NR = 10
for side, (cx, cy) in zip(("L", "R"), CFG['paw_centres']):
    sx = cx - 0.25 if side == "L" else cx + 0.25
    # path shoulder -> elbow -> wrist -> paw back / mid / front -> toe tip; (lateral, vertical) radii
    path = [((sx, -0.36, 0.86), 0.15, 0.15), (((sx + cx) / 2, -0.40, 0.48), 0.13, 0.13),
            ((cx, -0.37, 0.17), 0.10, 0.10), ((cx, -0.33, 0.08), 0.115, 0.08),
            ((cx, -0.43, 0.075), 0.135, 0.075), ((cx, -0.51, 0.064), 0.115, 0.062),
            ((cx, -0.56, 0.055), 0.06, 0.035)]
    C = [Vector(pp) for pp, _, _ in path]
    V, U, rings = [], [], []
    for k, (pp, rx, rz) in enumerate(path):
        T = (C[min(k + 1, len(C) - 1)] - C[max(k - 1, 0)]).normalized()
        A = Vector((1, 0, 0)); A = (A - T * A.dot(T)).normalized()
        B = T.cross(A).normalized()
        if B.z < 0 and abs(T.z) < 0.7:
            B = -B
        ids = []
        for j in range(NR):
            ph = 2 * math.pi * j / NR
            q = C[k] + A * (rx * math.cos(ph)) + B * (rz * math.sin(ph))
            if k >= 3:
                q.z = max(q.z, 0.004)  # a flat paw sole on the floor
            nz = (q - C[k]).normalized().z
            ids.append(len(V)); V.append(q)
            U.append(pal_uv(0.5 + 0.25 * nz, 0.62))
        rings.append(ids)
    V.append(C[0] + (C[0] - C[1]).normalized() * 0.06); U.append(pal_uv(0.4, 0.62)); c0 = len(V) - 1
    V.append(C[-1] + (C[-1] - C[-2]).normalized() * 0.025); U.append(pal_uv(0.5, 0.62)); c1 = len(V) - 1
    shell(V, loft(rings, c0, c1), U)
    nsh = 1
    # claws: four ivory pyramids at the paw front, pointing forward and down
    for dx in (-0.075, -0.025, 0.025, 0.075):
        base = Vector((cx + dx, -0.51 - 0.05 * (1 - (dx / 0.115) ** 2), 0.032))
        tip = base + Vector((0.0, -0.05, -0.024))
        s_ = 0.013
        cv = [base + Vector((-s_, 0.012, -s_)), base + Vector((s_, 0.012, -s_)),
              base + Vector((s_, 0.012, s_)), base + Vector((-s_, 0.012, s_)), tip]
        shell(cv, [(0, 1, 2), (0, 2, 3), (0, 4, 1), (1, 4, 2), (2, 4, 3), (3, 4, 0)], [CLAW_UV] * 5)
        nsh += 1
    # fur on the forearm (falling toward the paw) and a ruff over the wrist
    seg = C[2] - C[0]
    T = seg.normalized()
    A = Vector((1, 0, 0)); A = (A - T * A.dot(T)).normalized(); B = T.cross(A).normalized()
    for s0, angs, L_, W_ in ((0.55, (-90, -30, 30, 90, 150, 210), 0.2, 0.045),
                             (0.75, (-60, 0, 60, 120, 180, 240), 0.18, 0.045),
                             (0.93, (-90, -45, 0, 45, 90), 0.15, 0.05)):
        cpt = C[0] + seg * s0
        r_ = 0.15 + (0.10 - 0.15) * s0
        for ang in angs:
            ph = math.radians(ang)
            nrm = A * math.cos(ph) + B * math.sin(ph)
            if nrm.x * (1.0 if side == "L" else -1.0) > 0.5:
                continue  # no fur on the side facing the other arm (they almost touch)
            clump(cpt + nrm * r_, nrm, T + nrm * 0.15, L_, W_, 0.022, 0.022, 0.62, 0.62)
            nsh += 1
    ARMS[side] = dict(seed=[round(c, 5) for c in V[c1]],
                      shoulder=[round(c, 4) for c in C[0]], wrist=[round(c, 4) for c in C[2]], shells=nsh)

# fur clumps over the back, flanks and crown (the region the v23 relief covers)
kdW = kdtree.KDTree(len(P2))
for i, p in enumerate(P2):
    kdW.insert(Vector(p), i)
kdW.balance()
xc = float(np.mean([c.x for c, _ in BODY_VERTS]))
rng = np.random.RandomState(CFG['clump_seed'])
# candidates: area-uniform random points on the body; a lock may root anywhere that is NOT the
# front coat (more than clump_az from straight ahead, or the furred crown), off the cape, off the
# smoothed paw stubs and above the floor fringe
cand = []
areas = np.array([((b - a_).cross(c - a_)).length / 2 for a_, b, c in BODY_TRIS])
cum = np.cumsum(areas) / areas.sum()
NS = int(areas.sum() / (0.05 * CFG['clump_spacing'] ** 2))
for fi, r1, r2 in zip(np.searchsorted(cum, rng.random_sample(NS)), rng.random_sample(NS), rng.random_sample(NS)):
    a_, b, c = BODY_TRIS[min(fi, len(BODY_TRIS) - 1)]
    if r1 + r2 > 1:
        r1, r2 = 1 - r1, 1 - r2
    q = a_ + (b - a_) * r1 + (c - a_) * r2
    if q.z < CFG['clump_z_min']:
        continue
    _, j_, _ = kdW.find(q)
    _, jo, _ = old_kd.find(q)
    az_front = math.degrees(abs(math.atan2(q.x, -(q.y - yc))))
    if violet[jo] or WP2[j_] > 0.05 or not (az_front >= CFG['clump_az'] or W2[j_] >= CFG['clump_w_min'] or (q.z > 1.45 and q.y > -0.3)):
        continue
    hit = BVH.find_nearest(q)
    cand.append((q, hit[1].normalized()))
order = rng.permutation(len(cand))
sp = CFG['clump_spacing']
roots = []
for oi in order:
    p, n = cand[oi]
    if any((p - q).length < sp for q in roots):
        continue
    roots.append(p)
    radial = Vector((p.x - xc, p.y - yc, 0.0))
    radial = radial.normalized() if radial.length > 1e-6 else Vector((0, 1, 0))
    d = Vector((0, 0, -1)) + radial * 0.2 + Vector((0, 0.3, 0))
    t = d - n * d.dot(n)
    if t.length < 0.2:
        t = Vector((0, 1, 0)) - n * n.y
    t = t.normalized()
    ang = math.radians(rng.uniform(-25, 25))
    t = (t * math.cos(ang) + n.cross(t) * math.sin(ang)).normalized()
    zt = min(1.0, max(0.0, (p.z - 0.15) / 1.55))
    L = rng.uniform(0.30, 0.42) * (1.1 - 0.2 * zt)
    W_ = rng.uniform(0.065, 0.095)
    lift = rng.uniform(0.022, 0.05)
    th = rng.uniform(0.025, 0.035)
    tip = p + t * L
    if tip.z < 0.05:
        L = max(0.1, L * (p.z - 0.05) / max(p.z - tip.z, 1e-6))
    lc = 0.5 + 0.4 * zt + rng.uniform(-0.1, 0.1)
    hv = rng.uniform(0.0, 1.0)
    if clump(p, n, t, L, W_, lift, th, min(0.95, lc), hv, surf=BVH, bend=rng.uniform(-0.3, 0.3)) is None:
        roots.pop()
print(f"CLUMPS {len(roots)} on {len(cand)} candidate vertices; arms {ARMS}")
REPORT['arms'] = ARMS
REPORT['clumps'] = len(roots)
bm.normal_update()
fin = stats(bm, 'final')
bm.to_mesh(me); me.update(); bm.free()
for p in me.polygons:
    p.use_smooth = True
REPORT['regrounded_by'] = -mz
REPORT['cfg'] = CFG
REPORT['relief'] = dict(yc=yc, rough_lo=r_lo, rough_hi=r_hi, source_faces=len(src), displaced=len(MAP),
                        uv_faces=nuv, uv_fallback=nfallback)
json.dump(REPORT, open(os.path.join(OUT, 'fur_report.json'), 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'giant_fur.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, 'giant_prepped.glb'), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
print('FUR DONE', fin)
