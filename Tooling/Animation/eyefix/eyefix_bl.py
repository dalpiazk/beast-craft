"""Camera-projection eye mirror (Blender half).
Usage: blender -b --factory-startup -P eyefix_bl.py -- config.json [--glb PATH] [--out DIR]
--glb/--out override the config file's "glb"/"out" fields (which are local-machine paths to a
prepped GLB and a scratch output directory, neither committed to the repo -- see README.md).
Per op: render the SOURCE side (flat, unlit base colour) through an ortho camera C aimed square-on at
the source point; the target camera C' is C reflected through the fitted sagittal plane, whose image
is C's image mirrored horizontally. Every target-side texel (UV-rasterised -> world position) is
projected through C' and gets the mirrored render, weighted by a feathered screen-space ellipse
x visibility from C' (BVH occlusion) x facing (soft backface cull) x side-of-plane. Output: npz with
per-texel C-image sample coords + weights; compose.py does the pixel work."""
import sys, os, json, math; sys.path.insert(0, os.path.dirname(__file__))
import eflib, numpy as np, bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

_argv = sys.argv[sys.argv.index('--') + 1:]
cfg = json.load(open(_argv[0]))
for _i, _a in enumerate(_argv):
    if _a == '--glb':
        cfg['glb'] = _argv[_i + 1]
    elif _a == '--out':
        cfg['out'] = _argv[_i + 1]
out = cfg['out']; os.makedirs(out, exist_ok=True)
ob, img = eflib.setup(cfg['glb'])
W, H = img.size
V, T, UV = eflib.tri_data(ob)
n = np.array(cfg['plane']['n'], float); n /= np.linalg.norm(n); d = cfg['plane']['d']
refl = lambda P: P - 2*((P @ n) - d)[..., None]*n
reflv = lambda v: v - 2*(v @ n)[..., None]*n
bvh = BVHTree.FromPolygons([tuple(v) for v in V], [tuple(int(i) for i in t) for t in T])
FN = np.cross(V[T[:,1]]-V[T[:,0]], V[T[:,2]]-V[T[:,0]]); FA = np.linalg.norm(FN, axis=1); FN /= np.maximum(FA,1e-12)[:,None]
CEN = V[T].mean(1)

def raycast_point(origin, direction):
    hit, nrm, idx, dist = bvh.ray_cast(Vector(origin), Vector(direction))
    return np.array(hit), int(idx)

UVp = np.stack([UV[...,0]*W - 0.5, (1-UV[...,1])*H - 0.5], -1)   # (nt,3,2) texel coords, row 0 = top
def raster(tris, pad=1.5):
    """Conservative UV raster: each texel within `pad` px of a triangle is owned by the nearest one."""
    dist = np.full((H, W), np.inf, np.float32); own = np.full((H, W), -1, np.int64)
    bary = np.zeros((H, W, 3), np.float32)
    for ti in tris:
        a, b, c = UVp[ti]
        x0 = int(max(0, math.floor(min(a[0],b[0],c[0]) - pad))); x1 = int(min(W-1, math.ceil(max(a[0],b[0],c[0]) + pad)))
        y0 = int(max(0, math.floor(min(a[1],b[1],c[1]) - pad))); y1 = int(min(H-1, math.ceil(max(a[1],b[1],c[1]) + pad)))
        if x1 < x0 or y1 < y0: continue
        gx, gy = np.meshgrid(np.arange(x0, x1+1), np.arange(y0, y1+1))
        P = np.stack([gx, gy], -1).astype(np.float64)
        den = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
        if abs(den) < 1e-12: continue
        l0 = ((b[1]-c[1])*(P[...,0]-c[0]) + (c[0]-b[0])*(P[...,1]-c[1]))/den
        l1 = ((c[1]-a[1])*(P[...,0]-c[0]) + (a[0]-c[0])*(P[...,1]-c[1]))/den
        l2 = 1-l0-l1
        inside = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
        def segd(p, q):
            pq = q-p; t = np.clip(((P-p)@pq)/max(pq@pq,1e-12), 0, 1)
            return np.linalg.norm(P - (p + t[...,None]*pq), axis=-1)
        dd = np.where(inside, 0.0, np.minimum(np.minimum(segd(a,b), segd(b,c)), segd(c,a)))
        sl = (slice(y0, y1+1), slice(x0, x1+1))
        better = (dd <= pad) & (dd < dist[sl])
        dist[sl] = np.where(better, dd, dist[sl]); own[sl] = np.where(better, ti, own[sl])
        bary[sl] = np.where(better[...,None], np.stack([l0,l1,l2],-1), bary[sl])
    return dist, own, bary

results = []
for k, op in enumerate(cfg['ops']):
    side = op['target_side']          # -1: target texels where n.p-d < 0
    E, ei = raycast_point(op['src_ray_origin'], op['src_ray_dir'])
    near = np.linalg.norm(CEN - E, axis=1) < op.get('normal_radius', 0.05)
    NE = (FN[near]*FA[near][:,None]).sum(0); NE /= np.linalg.norm(NE)
    if 'cam_dir' in op: f = np.array(op['cam_dir'], float); f /= np.linalg.norm(f)
    else: f = -NE
    s = op['ortho_scale']; res = op.get('res', 2048)
    co = eflib.look_cam(f'C{k}', E, f, (0,0,1), s, res)
    co.data.clip_start = 10.0 - op.get('clip_front', 0.3)
    srcpng = os.path.join(out, f'op{k}_src.png'); eflib.render(srcpng)
    r_, u_, z_, t_ = eflib.cam_basis(co)
    mir = op.get('mirror', True)                     # False: plain translated stamp (C' = C)
    fr = refl if mir else (lambda X: X); sg = -1.0 if mir else 1.0
    fp = reflv(f) if mir else f                      # C' view direction
    ex, ey = (E - t_) @ r_, (E - t_) @ u_
    mrx, mry = op['mask_r']; mcx, mcy = op.get('mask_c', [0, 0])
    tx, ty = op.get('nudge', [0, 0]); sc = op.get('nudge_scale', 1.0)
    if 'target_sd' in op:   # place the eye-image centre on the face point at signed plane distance target_sd
        x = op.get('target_x0', 0.0); z = op['target_z']
        for _ in range(8):
            Th, _i = raycast_point((x, -5.0, z), (0, 1, 0)); x += (op['target_sd'] - (Th @ n - d))/n[0]
        Th, _i = raycast_point((x, -5.0, z), (0, 1, 0))
        QT = fr(Th); tx = sg*((QT - t_) @ r_ - ex) + op.get('nudge_extra', [0, 0])[0]; ty = (QT - t_) @ u_ - ey + op.get('nudge_extra', [0, 0])[1]
        print(f"op{k}: target={Th.round(4).tolist()} sd={float(Th @ n - d):.4f} nudge=({tx:.4f},{ty:.4f})")
    # candidate tris: bbox of the 3 reflected vertices (C' frame) overlaps the nudged mask box
    RV = fr(V[T]); vx = sg*((RV - t_) @ r_ - ex); vy = (RV - t_) @ u_ - ey          # (nt,3)
    bx0, bx1 = tx + mcx*sc - mrx*sc*1.1 - 0.01, tx + mcx*sc + mrx*sc*1.1 + 0.01
    by0, by1 = ty + mcy*sc - mry*sc*1.1 - 0.01, ty + mcy*sc + mry*sc*1.1 + 0.01
    sdv = (V[T] @ n - d)*side
    cand = np.where((vx.max(1) >= bx0) & (vx.min(1) <= bx1) & (vy.max(1) >= by0) & (vy.min(1) <= by1) & (sdv.max(1) > -0.02))[0]
    cb0 = UVp[cand].reshape(-1,2).min(0) - 3; cb1 = UVp[cand].reshape(-1,2).max(0) + 3
    tb0 = UVp.min(1); tb1 = UVp.max(1)
    over = np.where(np.all(tb1 >= cb0, 1) & np.all(tb0 <= cb1, 1))[0]
    print(f"op{k}: E={E.round(4).tolist()} NE={NE.round(3).tolist()} cand={len(cand)} overlapping={len(over)}")
    dist, own, bary = raster(over, cfg.get('pad', 1.5))
    candset = np.zeros(len(T), bool); candset[cand] = True
    ys, xs = np.where(own >= 0)
    m = candset[own[ys, xs]]; ys, xs = ys[m], xs[m]
    ti = own[ys, xs]; bc = bary[ys, xs].astype(np.float64)
    P = (V[T[ti]] * bc[..., None]).sum(1)
    sd = P @ n - d
    sf = op.get('side_feather', 0.01); off = op.get('side_offset', 0.0)
    w_side = np.clip((side*sd - off)/sf, 0, 1) if side else np.ones(len(sd))
    nf = FN[ti]; cosf = nf @ (-fp)
    if op.get('two_sided'):  # Meshy winding is inconsistent: orient each face toward C', rely on BVH occlusion for culling
        nf = nf*np.sign(cosf + 1e-12)[:, None]; cosf = np.abs(cosf)
    fc0, fc1 = op.get('face_ramp', [0.05, 0.3]); w_face = np.clip((cosf - fc0)/(fc1 - fc0), 0, 1)
    Q = fr(P); qx = (Q - t_) @ r_ - ex; qy = (Q - t_) @ u_ - ey
    xp, yp = sg*qx, qy                                # C' image coords (rel. to mirrored E)
    xs_, ys_ = (xp - tx)/sc, (yp - ty)/sc           # inverse nudge -> eye-image space (C' frame)
    rn = np.sqrt(((xs_ - mcx)/mrx)**2 + ((ys_ - mcy)/mry)**2)
    fe = op.get('feather', 0.35)
    tt = np.clip((1 - rn)/fe, 0, 1); w_mask = tt*tt*(3-2*tt)
    w_vis = np.zeros(len(P))
    act = np.where((w_mask*w_side*w_face) > 0)[0]
    for i in act:
        o = Vector(P[i] - fp*1e-4 + nf[i]*2e-4)
        hit = bvh.ray_cast(o, Vector(-fp), 50.0)
        w_vis[i] = 0.0 if (hit[0] is not None and hit[3] > op.get('occ_tol', 0.0)) else 1.0
    w = w_mask*w_side*w_face*w_vis
    for dt in cfg.get('debug_tris', []):
        mm = ti == dt
        print(f"op{k} dbg tri {dt}: in_cand={bool(candset[dt])} texels={int(mm.sum())} mask={w_mask[mm].round(2).tolist()[:6]} side={w_side[mm].round(2).tolist()[:3]} face={w_face[mm].round(2).tolist()[:3]} cos={cosf[mm].round(2).tolist()[:3]} vis={w_vis[mm].tolist()[:6]}")
        print("   P", P[mm][:3].round(4).tolist(), "verts", V[T[dt]].round(4).tolist(), "xp,yp", np.stack([xp[mm], yp[mm]],1)[:3].round(4).tolist(), "bc", bc[mm][:3].round(3).tolist())
    # 4x4 supersampling inside each texel (affine texel->world map of the owning triangle)
    A = np.concatenate([UVp[ti], np.ones((len(ti), 3, 1))], -1)          # (N,3,3) rows [x y 1]
    Mx = np.linalg.solve(A, V[T[ti]])                                      # (N,3,3): [x y 1] @ Mx = P
    offs = (np.arange(4) + 0.5)/4 - 0.5
    ox, oy = [g.ravel() for g in np.meshgrid(offs, offs)]
    PX = np.zeros((len(P), 16)); PY = np.zeros((len(P), 16))
    for j in range(16):
        Pj = P + ox[j]*Mx[:, 0] + oy[j]*Mx[:, 1]
        Qj = fr(Pj); xpj = sg*((Qj - t_) @ r_ - ex); ypj = (Qj - t_) @ u_ - ey
        sxj = sg*(xpj - tx)/sc; syj = (ypj - ty)/sc
        PX[:, j] = ((sxj + ex)/s + 0.5)*res - 0.5; PY[:, j] = (0.5 - (syj + ey)/s)*res - 0.5
    px, py = PX, PY
    keep = w > 0
    print(f"op{k}: texels painted={keep.sum()} full={int((w>0.99).sum())} occluded={len(act)-int((w_vis>0).sum())}")
    results.append(dict(ys=ys[keep], xs=xs[keep], w=w[keep], px=px[keep], py=py[keep]))
    json.dump({"E": E.tolist(), "Ep": fr(E).tolist(), "mirror": mir, "nudge": [float(tx), float(ty)], "f": f.tolist(), "fp": fp.tolist(), "NE": NE.tolist(),
               "cam_t": t_.tolist(), "cam_r": r_.tolist(), "cam_u": u_.tolist(), "ortho": s, "res": res, "src": srcpng},
              open(os.path.join(out, f'op{k}_cam.json'), 'w'))
np.savez(os.path.join(out, 'proj.npz'), **{f'{k}_{kk}': v for k, r in enumerate(results) for kk, v in r.items()})
json.dump({"W": W, "H": H, "nops": len(results)}, open(os.path.join(out, 'proj_meta.json'), 'w'))
print("DONE")
