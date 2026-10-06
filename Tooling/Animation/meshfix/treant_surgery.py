"""Treant mesh surgery (v20 round 2, producer review). Local-only (not run by CI), like eyefix/.

    blender -b --python meshfix/treant_surgery.py -- TREANT_PREPPED.glb OUT_DIR [clean,arms,crown]

Input: prep_mesh.py's treant_prepped.glb (--no-ground-sheet, native coords: head = -Y, up = +Z).
Output (OUT_DIR): treant_prepped.glb (the new "prepped" mesh rig_creature.py takes), the .blend, and
surgery_report.json (verts/tris/boundary edges/non-manifold edges/components after every step).
Stages:
  clean -- the decimated input has 9 open + 22 non-manifold edges: faces on >2-face edges removed,
           dangling slivers peeled, every boundary loop filled (fan + relaxed centre) -> watertight.
  arms  -- LEFT arm lowered 40 deg about the shoulder (forward axis, smooth 8 cm ramp = a skinned
           shoulder) into a relaxed A-pose. RIGHT forearm + hand (fused into / through the chest:
           its section contours overlap the chest's, so there is no separable back surface to cut
           along) deleted and the chest holes filled; a mirrored copy of the free left arm (mirror
           plane x = -0.03, the trunk's mid-plane) is placed at the right shoulder, aligned half-way
           to the old right upper arm, capped and UNIONED (exact boolean keeps both sides' UVs).
  crown -- the leaf crown came from a front-view drawing (flat slab behind). The front leaf faces
           (green texels, z > 1.18, face excluded) are mirrored through the coronal plane y = 0.19
           (depth x0.7 + a dome bulge), small per-island jitter, the hole over the back of the head
           filled with stepped copies of the leaves above it, decimated, and solidified BACK INTO
           the slab (variable thickness) into closed shells; rim walls get one mid-green texel.
           Kept as separate closed shells (an exact union with the self-intersecting slab blew up
           to 17k tris / 783 non-manifold edges); rig_templates/biped_arms.fix_humanoid_weights
           skins them with the crown they sit on.
Every fill's UVs come from its ring (fix_patch_uvs: chart-consistent, else one solid ring texel).
Final: sub-3 mm slivers welded, re-checked, re-grounded (lowest vertex z = 0), exported.
"""
import bpy, bmesh, sys, os, json, math, random
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = a[0], a[1]
STAGES = a[2].split(',') if len(a) > 2 else ['clean', 'arms', 'crown']
os.makedirs(OUT, exist_ok=True)
REPORT = {}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.name = 'Treant'
me = ob.data


def stats(bm, tag):
    bm.edges.ensure_lookup_table()
    nb = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if not e.is_manifold)
    # components
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
             components=len(comps), component_sizes=sorted(comps, reverse=True)[:6])
    print(f"STATS {tag}: {r}")
    REPORT[tag] = r
    return r


def bm_of():
    bm = bmesh.new(); bm.from_mesh(me)
    if 'patch' not in bm.faces.layers.int:
        bm.faces.layers.int.new('patch')
    bm.verts.index_update(); return bm


def put(bm):
    bm.to_mesh(me); me.update()


def smooth(t):
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def seg_proj(p, pts):
    best = None
    acc = 0.0
    for s0, s1 in zip(pts, pts[1:]):
        d = s1 - s0; L = d.length
        t = max(0.0, min(1.0, (p - s0).dot(d) / (L * L)))
        q = s0 + d * t
        dist = (p - q).length
        if best is None or dist < best[0]:
            best = (dist, q, acc + t * L)
        acc += L
    return best


def drop_small_components(bm, keep_min=40):
    comp = {}; cid = 0; sizes = []
    for v in bm.verts:
        if v in comp:
            continue
        st = [v]; comp[v] = cid; n = 0
        while st:
            x = st.pop(); n += 1
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in comp:
                    comp[o] = cid; st.append(o)
        sizes.append(n); cid += 1
    dead = [v for v in bm.verts if sizes[comp[v]] < keep_min]
    if dead:
        bmesh.ops.delete(bm, geom=dead, context='VERTS')
    return len(dead)


def boundary_cycles(bm):
    """Directed boundary half-edges (in the winding of the single adjacent face, reversed so a new
    face over the hole winds consistently) chained into simple cycles; pinch vertices handled by
    splitting a walk whenever it revisits a vertex."""
    nxt = {}
    for e in bm.edges:
        if not e.is_boundary:
            continue
        l = e.link_loops[0]
        a_, b = l.vert, l.link_loop_next.vert   # face winds a->b; the hole face must wind b->a
        nxt.setdefault(b, []).append(a_)
    cycles = []
    while any(nxt.values()):
        start = next(v for v, lst in nxt.items() if lst)
        path = [start]; pos = {start: 0}
        cur = start
        while True:
            lst = nxt.get(cur)
            if not lst:
                print(f"  FILL: abandoned open walk of {len(path)} verts"); break
            n = lst.pop()
            if n in pos:
                cyc = path[pos[n]:]
                cycles.append(cyc)
                for x in cyc:
                    pos.pop(x, None)
                path = path[:pos.get(n, len(path)) if n in pos else len(path) - len(cyc)]
                if not path:
                    break
                cur = path[-1]
                pos = {x: i for i, x in enumerate(path)}
                continue
            pos[n] = len(path); path.append(n); cur = n
    return cycles


def fill_holes_smooth(bm, edges=None, tag='', uvl=None, max_sides=400):
    """Fill every boundary cycle with a face, poke it (interior vertex), relax the poke centre, and
    give every new loop the UV its vertex already has on a neighbouring old face (centre = mean)."""
    vuv = {}
    for f in bm.faces:
        for l in f.loops:
            vuv.setdefault(l.vert, l[uvl].uv.copy())
    # peel dangling faces (>= 2 open edges): slivers left by deletes/booleans
    for _ in range(10):
        dang = [f for f in bm.faces if sum(1 for e in f.edges if e.is_boundary) >= 2]
        if not dang:
            break
        bmesh.ops.delete(bm, geom=dang, context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    newf = []
    for cyc in boundary_cycles(bm):
        if len(cyc) < 3:
            print(f"  FILL {tag}: skip short cycle {[tuple(round(c,3) for c in v.co) for v in cyc]}")
            continue
        try:
            f = bm.faces.new(cyc)
        except ValueError as ex:
            print(f"  FILL {tag}: face.new failed ({ex}) len={len(cyc)} uniq={len(set(cyc))}")
            continue
        newf.append(f)
        f[bm.faces.layers.int['patch']] = 1
        for l in f.loops:
            if l.vert in vuv:
                l[uvl].uv = vuv[l.vert]
    if not newf:
        return 0
    big = [f for f in newf if len(f.verts) > 3]
    cen = bmesh.ops.poke(bm, faces=big)['verts'] if big else []
    for _ in range(15):
        for v in cen:
            nb = [e.other_vert(v).co for e in v.link_edges]
            v.co = v.co * 0.3 + sum(nb, Vector()) / len(nb) * 0.7
    for v in cen:
        uvs = [l[uvl].uv for f in v.link_faces for l in f.loops if l.vert is not v]
        m = sum(uvs, Vector((0, 0))) / max(1, len(uvs))
        for l in v.link_loops:
            l[uvl].uv = m
    print(f"FILL {tag}: {len(newf)} hole faces, {len(cen)} poke centres")
    return len(newf)


def repair(bm, uvl, tag):
    for it in range(6):
        wire = [e for e in bm.edges if not e.link_faces]
        if wire:
            bmesh.ops.delete(bm, geom=wire, context='EDGES')
        bad = [e for e in bm.edges if not e.is_manifold and not e.is_boundary]
        if bad:
            bmesh.ops.delete(bm, geom=list({f for e in bad for f in e.link_faces}), context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        drop_small_components(bm)
        fill_holes_smooth(bm, tag=f'{tag}{it}', uvl=uvl)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        nb = sum(1 for e in bm.edges if not e.is_manifold)
        if nb == 0:
            break
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return stats(bm, tag)


def fix_patch_uvs(bm, uvl, tag, tol=0.03):
    """Patch faces (fills) inherit UVs from their ring vertices, but a ring can straddle UV-chart
    seams -> a triangle spanning two charts smears the whole atlas across it. Per patch triangle:
    pick, for each ring corner, the vertex UV (a seam vertex has several) that is closest to the
    other corners'; if they agree within `tol` the triangle maps that small coherent region (the
    poke centre gets the mean, i.e. the ring colour is pulled inward); otherwise the whole
    triangle collapses onto one ring UV (solid ring colour)."""
    pl = bm.faces.layers.int.get('patch')
    if pl is None:
        return 0
    vuvs = {}
    for f in bm.faces:
        if f[pl]:
            continue
        for l in f.loops:
            lst = vuvs.setdefault(l.vert, [])
            if all((l[uvl].uv - u).length > 1e-5 for u in lst):
                lst.append(l[uvl].uv.copy())
    nfix = ncol = 0
    import itertools
    for f in bm.faces:
        if not f[pl]:
            continue
        ring = [l for l in f.loops if l.vert in vuvs]
        if not ring:
            continue
        best = None
        for combo in itertools.product(*[vuvs[l.vert][:4] for l in ring]):
            spread = max((a_ - b).length for a_ in combo for b in combo)
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
    print(f"PATCH UVS {tag}: {nfix} faces re-mapped, {ncol} collapsed to a solid ring colour")
    REPORT.setdefault('patch_uvs', {})[tag] = dict(faces=nfix, solid=ncol)
    return nfix


# ---------------------------------------------------------------- clean
bm = bm_of()
uvl = bm.loops.layers.uv.active
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
stats(bm, 'input_welded')
if 'clean' in STAGES:
    # pre-existing decimation defects: faces on edges shared by >2 faces are removed, then every
    # boundary loop (those + the 9 pre-existing open edges) is filled.
    for it in range(4):
        bad = [e for e in bm.edges if not e.is_manifold and not e.is_boundary]
        if bad:
            fs = {f for e in bad for f in e.link_faces}
            bmesh.ops.delete(bm, geom=list(fs), context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        drop_small_components(bm)
        fill_holes_smooth(bm, tag=f'clean{it}', uvl=uvl)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        r = stats(bm, f'clean_pass{it}')
        if r['boundary_edges'] == 0 and r['non_manifold_edges'] == 0:
            break
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
put(bm); bm.free()

# ---------------------------------------------------------------- arms
SAG_X = -0.03  # body mid-plane (trunk sections at z 0.66-0.95 centre on x -0.03)
L_SH = Vector((-0.235, -0.04, 0.905))
L_PTS = [L_SH, Vector((-0.45, -0.09, 0.915)), Vector((-0.61, -0.11, 0.89)), Vector((-0.67, -0.12, 0.85)),
         Vector((-0.75, -0.13, 0.80))]
R_SH = Vector((0.14, 0.0, 0.9)); R_EL = Vector((0.25, -0.07, 0.78))
R_FORE = [R_EL, Vector((0.07, -0.17, 0.86)), Vector((0.0, -0.19, 0.97)), Vector((-0.005, -0.2, 1.0))]
LOWER_DEG = float(os.environ.get('TREANT_LOWER', '40'))

if 'arms' in STAGES:
    bm = bm_of(); uvl = bm.loops.layers.uv.active
    bm.normal_update()
    # 1. LEFT arm: lower about the shoulder (rotation about the forward axis through L_SH), blended
    #    with a smooth ramp along the arm (s = outward distance past the shoulder) so the shoulder
    #    bends like a skinned joint; restricted to a capsule round the arm chain.
    ang = math.radians(-LOWER_DEG)
    axis = Vector((0, 1, 0))
    moved = 0
    for v in bm.verts:
        dist, q, s = seg_proj(v.co, L_PTS)
        s_out = (L_SH - v.co).x  # outward (−X) distance past the shoulder
        if v.co.z > 1.06 or s_out < -0.02:
            continue
        w = smooth((s_out + 0.0) / 0.08) * smooth((0.14 - dist) / 0.04)
        if w <= 0:
            continue
        R = Quaternion(axis, ang * w).to_matrix()
        v.co = L_SH + R @ (v.co - L_SH)
        moved += 1
    print(f"LEFT ARM lowered {LOWER_DEG} deg: {moved} verts")
    REPORT['left_arm_lowered_deg'] = LOWER_DEG
    R_L = Quaternion(axis, ang).to_matrix()
    L_PTS2 = [L_SH] + [L_SH + R_L @ (p - L_SH) for p in L_PTS[1:]]

    # 2. RIGHT forearm+hand: delete it (fused into/through the chest), fill the chest holes.
    bm.normal_update()
    dele = set()
    flen = sum(((b - a_).length for a_, b in zip(R_FORE, R_FORE[1:])))
    for v in bm.verts:
        dist, q, s = seg_proj(v.co, R_FORE)
        if s < 0.035 or dist > 0.075:
            continue
        r = v.co - q
        out = r.normalized().dot(v.normal) if r.length > 1e-6 else 1.0
        if dist < 0.045 or out > 0.15:
            dele.add(v)
    # grow by one ring inside the capsule only where all neighbours are deleted (clean edge)
    print(f"RIGHT FOREARM: deleting {len(dele)} verts")
    REPORT['right_forearm_deleted_verts'] = len(dele)
    bmesh.ops.delete(bm, geom=list(dele), context='VERTS')
    # drop loose bits left floating
    bm.verts.ensure_lookup_table()
    stats(bm, 'after_forearm_delete')
    drop_small_components(bm)
    fill_holes_smooth(bm, tag='chest', uvl=uvl)
    # dangling flaps: faces with 2 boundary edges etc. -> repeat fill until closed
    for it in range(3):
        if not any(e.is_boundary for e in bm.edges):
            break
        fill_holes_smooth(bm, tag=f'chest{it}', uvl=uvl)
    repair(bm, uvl, 'after_chest_fill')
    put(bm); bm.free()

    # 3. NEW right arm = mirror of the (lowered) LEFT arm beyond its shoulder, capped, placed at the
    #    right shoulder and unioned with the body (exact boolean keeps both sides' UVs).
    bm = bm_of(); uvl = bm.loops.layers.uv.active
    keep = set()
    for v in bm.verts:
        dist, q, s = seg_proj(v.co, L_PTS2)
        if s > 0.02 and dist < 0.085 and v.co.z < 1.0:
            keep.add(v)
    arm_bm = bmesh.new()
    vmap = {}
    for v in keep:
        vmap[v] = arm_bm.verts.new(v.co)
    auv = arm_bm.loops.layers.uv.new('UVMap')
    apl = arm_bm.faces.layers.int.new('patch')
    for f in bm.faces:
        if all(v in keep for v in f.verts):
            nf = arm_bm.faces.new([vmap[v] for v in f.verts])
            for l, lo in zip(nf.loops, f.loops):
                l[auv].uv = lo[uvl].uv
    bm.free()
    bmesh.ops.delete(arm_bm, geom=[v for v in arm_bm.verts if not v.link_faces], context='VERTS')
    # keep the largest component (the arm), drop stray leaf fragments
    arm_bm.verts.ensure_lookup_table()
    comp = {}; cid = 0
    for v in arm_bm.verts:
        if v in comp:
            continue
        st = [v]; comp[v] = cid
        while st:
            x = st.pop()
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in comp:
                    comp[o] = cid; st.append(o)
        cid += 1
    sizes = np.bincount([comp[v] for v in arm_bm.verts])
    big = int(np.argmax(sizes))
    bmesh.ops.delete(arm_bm, geom=[v for v in arm_bm.verts if comp[v] != big], context='VERTS')
    print(f"ARM COPY: components {list(sizes)} kept {sizes[big]}")
    fill_holes_smooth(arm_bm, tag='armcap', uvl=auv)
    # mirror through x = SAG_X
    for v in arm_bm.verts:
        v.co.x = 2 * SAG_X - v.co.x
    bmesh.ops.reverse_faces(arm_bm, faces=arm_bm.faces)
    # align: mirrored shoulder -> R_SH pushed 2 cm inward; mirrored upper-arm dir -> blend of itself
    # and the right upper arm's own direction (so the old upper arm sits inside the new one)
    msh = Vector((2 * SAG_X - L_SH.x, L_SH.y, L_SH.z))
    mel = Vector((2 * SAG_X - L_PTS2[1].x, L_PTS2[1].y, L_PTS2[1].z))
    d_m = (mel - msh).normalized(); d_r = (R_EL - R_SH).normalized()
    d_t = (d_m + d_r).normalized()
    rot = d_m.rotation_difference(d_t).to_matrix()
    tgt_sh = R_SH + Vector((-0.02, 0.0, 0.0))
    for v in arm_bm.verts:
        v.co = tgt_sh + rot @ (v.co - msh)
    REPORT['right_arm'] = dict(mirror_x=SAG_X, shoulder=list(tgt_sh), upper_dir=list(d_t),
                               angle_to_old_upper_deg=math.degrees(d_m.angle(d_r)))
    stats(arm_bm, 'right_arm_shell')
    am = bpy.data.meshes.new('RArm'); arm_bm.to_mesh(am); arm_bm.free()
    am.materials.append(me.materials[0])
    arm_ob = bpy.data.objects.new('RArm', am); bpy.context.scene.collection.objects.link(arm_ob)
    mod = ob.modifiers.new('U', 'BOOLEAN'); mod.operation = 'UNION'; mod.solver = 'EXACT'
    mod.object = arm_ob
    try:
        mod.use_self = False
        mod.use_hole_tolerant = True
    except Exception:
        pass
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier='U')
    bpy.data.objects.remove(arm_ob)
    bm = bm_of(); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    stats(bm, 'after_union_raw')
    uvl = bm.loops.layers.uv.active
    repair(bm, uvl, 'after_union')
    put(bm); bm.free()

# ---------------------------------------------------------------- crown
if 'crown' in STAGES:
    K = int(os.environ.get('TREANT_CLUSTERS', '18'))
    BULGE = float(os.environ.get('TREANT_BULGE', '0.10'))
    rng = random.Random(20)
    img = next(n.image for m in me.materials for n in m.node_tree.nodes if n.type == 'TEX_IMAGE')
    W, H = img.size
    PX = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    bm = bm_of(); uvl = bm.loops.layers.uv.active
    bm.normal_update(); bm.faces.ensure_lookup_table()

    def tex(f):
        u = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
        x = int(min(W - 1, max(0, u.x * W))); y = int(min(H - 1, max(0, u.y * H)))
        return PX[y, x, :3]

    def is_green(f):
        r, g, b = tex(f)
        return g > r * 1.05 and g > b * 1.05
    src = [f for f in bm.faces if f.calc_center_median().z > 1.18 and f.normal.y < -0.3 and is_green(f)
           and not (abs(f.calc_center_median().x + 0.05) < 0.17 and f.calc_center_median().z < 1.5)]
    tgt = [f for f in bm.faces if f.calc_center_median().z > 1.15 and f.normal.y > 0.3]
    print(f"CROWN: {len(src)} front leaf faces (source pool), {len(tgt)} back-facing faces")

    def fps(faces, k, seed_pt):
        pts = [f.calc_center_median() for f in faces]
        chosen = [min(range(len(pts)), key=lambda i: (pts[i] - seed_pt).length)]
        dmin = [(p - pts[chosen[0]]).length for p in pts]
        while len(chosen) < k:
            i = max(range(len(pts)), key=lambda j: dmin[j]); chosen.append(i)
            dmin = [min(dmin[j], (pts[j] - pts[i]).length) for j in range(len(pts))]
        return [pts[i] for i in chosen]
    # mirror the WHOLE front leaf surface through the coronal plane y = Y_M (depth scaled by DEPTH_K
    # so the back bulges ~DEPTH_K x the front's cup), as one sheet; decimate; solidify into a closed
    # shell; small per-island jitter for variety.
    Y_M = float(os.environ.get('TREANT_YM', '0.19')); DEPTH_K = float(os.environ.get('TREANT_DK', '0.7'))
    DOME = float(os.environ.get('TREANT_DOME', '0.09'))

    def dome(c):  # extra rounded bulge, max at the crown's back centre, 0 at its rim
        return DOME * max(0.0, 1 - ((c.x + 0.03) / 0.72) ** 2 - ((c.z - 1.5) / 0.42) ** 2)
    TARGET = int(os.environ.get('TREANT_CROWN_TRIS', '1800'))
    sb = bmesh.new(); suv = sb.loops.layers.uv.new('UVMap'); spl = sb.faces.layers.int.new('patch')
    vm = {}
    for f in src:
        for v in f.verts:
            if v not in vm:
                c = v.co.copy(); c.y = Y_M + DEPTH_K * max(0.0, Y_M - c.y) + 0.012 + dome(c)
                vm[v] = sb.verts.new(c)
        nf = sb.faces.new([vm[v] for v in f.verts])
        for l, lo in zip(nf.loops, f.loops):
            l[suv].uv = lo[uvl].uv
    # the face region was excluded from the source, so the mirrored sheet has a hole over the back
    # of the head: fill it with copies of the leaf patch just above it, stepped down
    above = [f for f in src if (f.calc_center_median() - Vector((-0.04, 0, 1.6))).to_2d().length < 1
             and abs(f.calc_center_median().x + 0.04) < 0.16 and 1.5 < f.calc_center_median().z < 1.72]
    for (dx, dz, ang_) in ((-0.07, -0.22, 14), (0.06, -0.24, -12), (0.0, -0.38, 6)):
        Rr = Matrix.Rotation(math.radians(ang_), 3, 'Y')
        c0 = Vector((-0.04, 0, 1.61))
        vm2 = {}
        for f in above:
            for v in f.verts:
                if v not in vm2:
                    c = v.co.copy(); c.y = Y_M + DEPTH_K * max(0.0, Y_M - c.y) + 0.012 + dome(c)
                    q = Rr @ (Vector((c.x, 0, c.z)) - c0)
                    vm2[v] = sb.verts.new(Vector((c0.x + q.x + dx, c.y - 0.02, c0.z + q.z + dz)))
            nf = sb.faces.new([vm2[v] for v in f.verts])
            for l, lo in zip(nf.loops, f.loops):
                l[suv].uv = lo[uvl].uv
    print(f"CROWN: head-back fill from {len(above)} faces x3")
    bmesh.ops.reverse_faces(sb, faces=sb.faces)
    # drop tiny islands
    comp = {}; sizes = []
    for f in sb.faces:
        if f in comp:
            continue
        st = [f]; comp[f] = len(sizes); n = 0
        while st:
            x = st.pop(); n += 1
            for e in x.edges:
                for g in e.link_faces:
                    if g not in comp:
                        comp[g] = comp[f]; st.append(g)
        sizes.append(n)
    bmesh.ops.delete(sb, geom=[f for f in sb.faces if sizes[comp[f]] < 8], context='FACES')
    bmesh.ops.delete(sb, geom=[v for v in sb.verts if not v.link_faces], context='VERTS')
    # per-island jitter (rotation about the island centre, +-8 deg, scale 0.95-1.12)
    isl = {}
    for f in sb.faces:
        isl.setdefault(comp[f], set()).update(f.verts)
    for cid, vs in isl.items():
        c = sum((v.co for v in vs), Vector()) / len(vs)
        Rj = Matrix.Rotation(math.radians(rng.uniform(-8, 8)), 3, 'Y') @ Matrix.Rotation(math.radians(rng.uniform(-6, 6)), 3, 'X')
        scj = rng.uniform(0.95, 1.12)
        for v in vs:
            v.co = c + Rj @ ((v.co - c) * scj)
    print(f"CROWN SHEET: {len(sb.faces)} faces in {sum(1 for n in sizes if n >= 8)} islands")
    m_ = bpy.data.meshes.new('crownback'); sb.to_mesh(m_); sb.free()
    m_.materials.append(me.materials[0])
    o_ = bpy.data.objects.new('crownback', m_); bpy.context.scene.collection.objects.link(o_)
    nfaces = len(m_.polygons)
    budget_sheet = TARGET / 2.3
    if nfaces > budget_sheet:
        dm = o_.modifiers.new('D', 'DECIMATE'); dm.ratio = budget_sheet / nfaces
        bpy.context.view_layer.objects.active = o_; bpy.ops.object.modifier_apply(modifier='D')
    # thickness reaches back to the slab: per-vertex (dome + mirrored depth gap + 2.5 cm) via a
    # vertex-group factor, so the shell seats on the old back slab with no see-through gap
    vg = o_.vertex_groups.new(name='th')
    TH = 0.30
    for v in m_.vertices:
        c = v.co
        gap = dome(c) + max(0.0, c.y - Y_M) + 0.025
        vg.add([v.index], min(1.0, max(0.07, gap / TH)), 'REPLACE')
    sm = o_.modifiers.new('S', 'SOLIDIFY'); sm.thickness = TH; sm.offset = -1.0; sm.use_rim = True
    sm.vertex_group = 'th'
    m_.materials.append(me.materials[0])  # a 2nd slot so the rim offset is not clamped away
    sm.material_offset_rim = 1  # tag the rim walls (material slot 1) so they can be re-UV'd below
    sm.use_rim_only = False
    bpy.context.view_layer.objects.active = o_; bpy.ops.object.modifier_apply(modifier='S')
    # rim walls: a stretched sliver of whatever the sheet's edge texel was read as pale streaks
    # in-engine; give every rim face one solid mid-green leaf texel (median-luminance source leaf)
    lum = sorted(((0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]), f.index) for f in src for c in [tex(f)])
    mid_f = bm.faces[lum[int(len(lum) * 0.4)][1]]
    rim_uv = sum((l[uvl].uv for l in mid_f.loops), Vector((0, 0))) / len(mid_f.loops)
    nrim = 0
    for poly in m_.polygons:
        if poly.material_index >= 1:
            for li in poly.loop_indices:
                m_.uv_layers.active.data[li].uv = rim_uv
            poly.material_index = 0
            nrim += 1
    while len(m_.materials) > 1:
        m_.materials.pop(index=len(m_.materials) - 1)
    print(f"CROWN SHELL: {nrim} rim faces set to leaf texel {tuple(round(c, 3) for c in rim_uv)} "
          f"(colour {tuple(round(float(c), 2) for c in tex(mid_f))})")
    sb = bmesh.new(); sb.from_mesh(m_)
    sb.faces.layers.int.get('patch') or sb.faces.layers.int.new('patch')
    bmesh.ops.triangulate(sb, faces=[f for f in sb.faces if len(f.verts) > 3])
    repair(sb, sb.loops.layers.uv.active, 'crown_shell')
    bmesh.ops.recalc_face_normals(sb, faces=sb.faces)
    nb = sum(1 for e in sb.edges if not e.is_manifold)
    added = len(sb.faces)
    sb.to_mesh(m_); sb.free()
    shells = [(o_, nb, nfaces)]
    print(f"CROWN SHELL: {added} tris, open/non-manifold edges {nb}")
    REPORT['crown_back'] = dict(source_faces=len(src), mirror_y=Y_M, depth_k=DEPTH_K, tris=added,
                                non_manifold=nb)
    # the crown slab self-intersects heavily, so an exact union explodes (tested: 17k tris, 783
    # non-manifold edges); the clusters stay as separate CLOSED shells joined into the mesh object
    # (seated 3.5 cm into the slab, skinned to the crown bones by the rig like the rest of the crown).
    bpy.ops.object.select_all(action='DESELECT')
    for o_, _, _ in shells:
        o_.select_set(True)
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.join()
    bm = bm_of()
    stats(bm, 'after_crown')
    put(bm); bm.free()

bm = bm_of(); uvl = bm.loops.layers.uv.active
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
# weld the sub-3 mm slivers the boolean union and the fills leave (a 1 mm edge between two nearly
# identical vertices measures as a huge relative edge stretch under any skinning difference)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.003)
bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=0.003)
bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 3])
repair(bm, uvl, 'final_weld')
drop_small_components(bm, keep_min=40)
fix_patch_uvs(bm, uvl, 'final')
stats(bm, 'final')
put(bm); bm.free()
# re-ground: lowest vertex on z = 0
mz = min(v.co.z for v in me.vertices)
for v in me.vertices:
    v.co.z -= mz
REPORT['reground_dz'] = -mz
for o in list(bpy.data.objects):
    if o is not ob:
        bpy.data.objects.remove(o)
for a_ in list(me.attributes):
    if a_.name == 'patch':
        me.attributes.remove(a_)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'treant_surgery.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, 'treant_prepped.glb'), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
json.dump(REPORT, open(os.path.join(OUT, 'surgery_report.json'), 'w'), indent=1, default=str)
print("SURGERY DONE")
