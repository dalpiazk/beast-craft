"""Caster crescent glow shell (v23, enemies batch 3). Local-only (not run by CI), like the others.

    blender -b --python meshfix/caster_glow.py -- CASTER_PREPPED.glb OUT_DIR [INFLATE]

Input: prep_mesh.py's caster_prepped.glb (--no-ground-sheet, native coords: head = -Y, up = +Z). The
floating green crescent over the Caster's head is already its own closed shell (Meshy modelled it
apart from the body).

Live3D's toon shader has no emissive term, so "the crescent glows" (the Caster's signature Cast) is
built into the mesh: a GLOW SHELL -- a copy of the crescent shell pushed out along its vertex normals
by INFLATE (default 0.02 native, about 1 % of the height), every corner mapped to one bright
pale-lime texel painted into an unused corner of the atlas (a block no UV triangle covers). The rig
gives it its own bone (`crescent_glow`, a child of the crescent bone, its head at the crescent's
centre so the scaled shell stays concentric round the crescent; its corner normals all point UP so the toon pass lights every pixel of
it in the highlight band -- a flat, bright, glowing read): every clip but Cast holds it scaled
to 0.2 % (an invisible speck in the crescent's empty middle), and Cast swells it out to ~1.1x so the crescent
flares bright and fat, then shrinks it back. Its joint is in the landmark file's outline_mask_zero (no ink hull
round a collapsed shell).

Output (OUT_DIR): caster_prepped.glb, caster_glow.blend, glow_report.json (the crescent and glow
shells' vertex counts, centres and one vertex position of each -- the rig's component seeds -- the
painted texel block and the final stats).
"""
import bpy, bmesh, sys, os, json
import numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
SRC, OUT = a[0], a[1]
INFLATE = float(a[2]) if len(a) > 2 else 0.02
GLOW_RGB = (0.90, 1.00, 0.70)  # pale luminous lime (linear-ish; the texture is sRGB 8-bit)
BLOCK = 8                      # painted block size in texels (one 8 px cell of a 1K atlas)
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
ob.name = 'Caster'
me = ob.data
img = next(n.image for m in me.materials if m and m.node_tree for n in m.node_tree.nodes
           if n.type == 'TEX_IMAGE' and n.image)
TW, TH = img.size

bm = bmesh.new(); bm.from_mesh(me)
uvl = bm.loops.layers.uv.active
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.verts.ensure_lookup_table()


def components(bm):
    seen = {}; comps = []
    for v in bm.verts:
        if v in seen:
            continue
        st = [v]; seen[v] = len(comps); mem = [v]
        while st:
            x = st.pop()
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in seen:
                    seen[o] = len(comps); st.append(o); mem.append(o)
        comps.append(mem)
    return comps


comps = components(bm)
body = max(comps, key=len)
cres = max((c for c in comps if c is not body), key=lambda c: min(v.co.z for v in c))  # floats highest
cset = set(cres)
cfaces = [f for f in bm.faces if f.verts[0] in cset]
nb = sum(1 for e in bm.edges if e.verts[0] in cset and not e.is_manifold)
print(f"CRESCENT shell: {len(cres)} verts, {len(cfaces)} faces, non-manifold edges {nb}")

# --- the unused atlas block --------------------------------------------------------------------
G = 128
cov = np.zeros((G, G), bool)
cc_ = (np.arange(G) + 0.5) / G
for f in bm.faces:  # rasterise every UV triangle (cell centres; edges padded by the 3x3 test below)
    uv = np.array([l[uvl].uv[:] for l in f.loops])
    for k in range(1, len(uv) - 1):
        t = uv[[0, k, k + 1]]
        lo = np.floor(t.min(0) * G).astype(int).clip(0, G - 1)
        hi = np.floor(t.max(0) * G).astype(int).clip(0, G - 1)
        X, Y = np.meshgrid(cc_[lo[0]:hi[0] + 1], cc_[lo[1]:hi[1] + 1])
        (x1, y1), (x2, y2), (x3, y3) = t
        d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
        if abs(d) < 1e-14:
            cov[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] = True
            continue
        l1 = ((y2 - y3) * (X - x3) + (x3 - x2) * (Y - y3)) / d
        l2 = ((y3 - y1) * (X - x3) + (x1 - x3) * (Y - y3)) / d
        inside = (l1 >= -0.02) & (l2 >= -0.02) & (1 - l1 - l2 >= -0.02)
        cov[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1] |= inside
        for (x, y) in t:  # the corners' own cells too
            cov[min(G - 1, int(y * G)), min(G - 1, int(x * G))] = True
free = [(y, x) for y in range(2, G - 2) for x in range(2, G - 2) if not cov[y - 2:y + 3, x - 2:x + 3].any()]
if not free:
    raise SystemExit("caster_glow.py: no free 5x5-cell block in the atlas")
cy, cx = free[0]
u0, v0 = (cx + 0.5) / G, (cy + 0.5) / G
px = np.array(img.pixels[:], dtype=np.float32).reshape(TH, TW, -1)
x0, y0 = int(u0 * TW) - BLOCK // 2, int(v0 * TH) - BLOCK // 2
px[y0:y0 + BLOCK, x0:x0 + BLOCK, 0] = GLOW_RGB[0]
px[y0:y0 + BLOCK, x0:x0 + BLOCK, 1] = GLOW_RGB[1]
px[y0:y0 + BLOCK, x0:x0 + BLOCK, 2] = GLOW_RGB[2]
img.pixels = px.ravel().tolist()
img.update()
img.pack()
print(f"GLOW TEXEL block {BLOCK}px at uv ({u0:.4f}, {v0:.4f}) (atlas cell {cx},{cy} of {G})")

# --- the glow shell ------------------------------------------------------------------------------
dup = bmesh.ops.duplicate(bm, geom=cfaces)
nverts = [g for g in dup['geom'] if isinstance(g, bmesh.types.BMVert)]
nfaces = [g for g in dup['geom'] if isinstance(g, bmesh.types.BMFace)]
bm.normal_update()
normals = {v: v.normal.copy() for v in nverts}
for v in nverts:
    v.co = v.co + normals[v] * INFLATE
for f in nfaces:
    for l in f.loops:
        l[uvl].uv = (u0, v0)
    f.smooth = True
bm.normal_update()
gc = sum((v.co for v in nverts), Vector()) / len(nverts)
cc = sum((v.co for v in cres), Vector()) / len(cres)
top_c = max(cres, key=lambda v: v.co.z).co.copy()
top_g = max(nverts, key=lambda v: v.co.z).co.copy()
comps = components(bm)
ntri = sum(len(f.verts) - 2 for f in bm.faces)
nbound = sum(1 for e in bm.edges if e.is_boundary)
rep = dict(inflate=INFLATE, crescent_verts=len(cres), glow_verts=len(nverts),
           crescent_centre=[round(c, 4) for c in cc], glow_centre=[round(c, 4) for c in gc],
           crescent_top_vertex=[round(c, 5) for c in top_c], glow_top_vertex=[round(c, 5) for c in top_g],
           crescent_z=[round(min(v.co.z for v in cres), 4), round(max(v.co.z for v in cres), 4)],
           texel_uv=[u0, v0], tris=ntri, boundary_edges=nbound, components=len(comps),
           component_sizes=sorted((len(c) for c in comps), reverse=True))
print("GLOW REPORT", json.dumps(rep))
glow_faces = {f.index for f in nfaces}
bm.to_mesh(me); me.update(); bm.free()
# Live3D's toon pass shades by half-lambert N.L in three bands, so a plain shell is lit on one side
# and shadow-tinted (cool grey) on the other -- it read as a fat grey-and-lime crescent, not a glow.
# Every corner of the glow shell gets a custom normal straight UP (Blender +Z = glTF +Y): with the
# key light (0.45, 0.65, 0.60) that is N.L 0.65 -> the HIGHLIGHT band on every pixel, whatever the
# crescent's spin about the vertical (pitch/bank stay small). The rest of the mesh keeps its own
# corner normals.
cn = [tuple(c.vector) for c in me.corner_normals]
for p in me.polygons:
    if p.index in glow_faces:
        for li in p.loop_indices:
            cn[li] = (0.0, 0.0, 1.0)
me.normals_split_custom_set(cn)
me.update()
rep["glow_normals"] = "custom, all +Z (highlight band)"
json.dump(rep, open(os.path.join(OUT, 'glow_report.json'), 'w'), indent=1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'caster_glow.blend'))
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, 'caster_prepped.glb'), export_format='GLB',
                          use_selection=False, export_yup=True, export_apply=True)
print('GLOW DONE')
