"""python lmcheck.py MESH.glb LANDMARKS.json BEAST OUTDIR
Landmark sanity check against a prepped mesh (native coords, numpy only). MESH.glb is the prepped
GLB (e.g. the output of prep_mesh.py); LANDMARKS.json is either a single beast's landmark dict or a
multi-beast file keyed by BEAST (BEAST also selects the body-plan heuristics used for the section
views below):
  * every landmark point: inside/outside the mesh (ray parity, 3 rays majority) + distance to the
    nearest vertex,
  * views.png: left (Y-Z), front (X-Z), top (X-Y) silhouettes (triangle fill, alpha-accumulated =
    x-ray density) with every chain drawn on top,
  * sections.png: the sagittal section at the spine's x plus transverse sections through each spine /
    body point, the point drawn in red, and a numeric report of where the point sits in the section
    (fraction of the local section's extent; 0.5 = centre).
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import glb_gate  # noqa: E402


def load_mesh(path):
    js, b = glb_gate.parse_glb(path)
    cache = {}
    P, F = [], []
    off = 0
    for m in js["meshes"]:
        for pr in m["primitives"]:
            pos = glb_gate._accessor(js, b, pr["attributes"]["POSITION"], cache)
            idx = glb_gate._accessor(js, b, pr["indices"], cache).astype(np.int64).reshape(-1, 3)
            P.append(pos @ glb_gate.C.T)  # gltf -> blender
            F.append(idx + off)
            off += len(pos)
    return np.vstack(P), np.vstack(F)


def ray_hits(P, F, o, d):
    v0, v1, v2 = P[F[:, 0]], P[F[:, 1]], P[F[:, 2]]
    e1, e2 = v1 - v0, v2 - v0
    h = np.cross(d, e2)
    a = (e1 * h).sum(1)
    ok = np.abs(a) > 1e-12
    f = np.where(ok, 1.0 / np.where(ok, a, 1), 0)
    s = o - v0
    u = f * (s * h).sum(1)
    q = np.cross(s, e1)
    v = f * (q @ d)
    t = f * (e2 * q).sum(1)
    hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-9)
    return int(hit.sum())


def inside(P, F, p):
    votes = 0
    for d in ((1, 0.013, 0.007), (-0.011, 1, 0.017), (0.009, -0.015, 1)):
        d = np.array(d, float)
        d /= np.linalg.norm(d)
        votes += ray_hits(P, F, np.array(p, float), d) % 2
    return votes >= 2


def section(P, F, axis, val):
    """Segments of the mesh cut by the plane coord[axis]==val (list of (a,b) 3D points)."""
    d = P[:, axis] - val
    segs = []
    dd = d[F]
    sgn = np.sign(dd)
    cand = np.where((sgn.max(1) > 0) & (sgn.min(1) < 0))[0]
    for fi in cand:
        tri = F[fi]
        pts = []
        for i, j in ((0, 1), (1, 2), (2, 0)):
            a, b = d[tri[i]], d[tri[j]]
            if (a > 0) != (b > 0):
                t = a / (a - b)
                pts.append(P[tri[i]] + t * (P[tri[j]] - P[tri[i]]))
        if len(pts) == 2:
            segs.append(pts)
    return segs


def chains_of(L):
    out = []
    sp = L.get("spine", {})
    order = [k for k in ("pelvis", "spine_01", "chest", "neck_base", "neck_mid", "head") if k in sp]
    if order:
        out.append(("spine", [sp[k] for k in order], (255, 160, 0)))
    if "head" in sp:
        for k, c in (("snout_tip", (255, 0, 0)), ("jaw_tip", (255, 0, 255)), ("head_top", (255, 255, 255))):
            if k in sp:
                out.append((k, [sp["head"], sp[k]], c))
    if L.get("body_chain"):
        out.append(("body", L["body_chain"] + ([sp["head"]] if "head" in sp else []), (255, 160, 0)))
    for l in L.get("legs", []):
        col = {"FL": (0, 230, 230), "FR": (255, 0, 255), "BL": (140, 90, 30), "BR": (255, 215, 0)}[l["side"]]
        out.append((l["side"], l["chain"] + [l["toe_tip"]], col))
    for a in L.get("arms", []):
        out.append(("arm" + a["side"], a["chain"] + [a["hand_tip"]], (0, 120, 255) if a["side"] == "L" else (0, 200, 90)))
    if L.get("tail"):
        out.append(("tail", L["tail"], (160, 60, 255)))
    for fn in L.get("fins", []):
        out.append(("fin" + fn["side"], [fn["root"], fn["tip"]], (0, 200, 0)))
    if "crown" in L:
        for t in L["crown"]["tips"]:
            out.append(("crown", [L["crown"]["base"], t], (40, 160, 40)))
    if "horns" in L:
        out.append(("horn", [L["horns"]["base"], L["horns"]["tip"]], (255, 255, 255)))
    if "body" in L and "parts" in L:  # v21 blob: body column + every part bone
        out.append(("body", [L["body"]["base"], L["body"]["top"]], (255, 160, 0)))
        cols = [(255, 0, 0), (0, 120, 255), (0, 200, 90), (160, 60, 255), (0, 230, 230), (255, 0, 255)]
        for i, p in enumerate(L["parts"]):
            out.append((p["name"], [p["head"], p["tail"]], cols[i % len(cols)]))
            for f in p.get("feet", []):
                out.append((p["name"] + "_foot", [p["tail"], [f[0], f[1], 0.0]], cols[i % len(cols)]))
    return out


def all_points(L):
    pts = []
    sp = L.get("spine", {})
    for k, v in sp.items():
        pts.append((k, v))
    for i, p in enumerate(L.get("body_chain", [])):
        pts.append((f"body_{i}", p))
    for l in L.get("legs", []):
        for n, p in zip(("hip", "knee", "ankle", "foot"), l["chain"]):
            pts.append((f"{l['side']}_{n}", p))
        pts.append((f"{l['side']}_toe_tip", l["toe_tip"]))
    for a in L.get("arms", []):
        for n, p in zip(("shoulder", "elbow", "wrist"), a["chain"]):
            pts.append((f"arm{a['side']}_{n}", p))
        pts.append((f"arm{a['side']}_hand_tip", a["hand_tip"]))
    for i, p in enumerate(L.get("tail", [])):
        pts.append((f"tail_{i}", p))
    for fn in L.get("fins", []):
        pts.append((f"fin{fn['side']}_root", fn["root"]))
        pts.append((f"fin{fn['side']}_tip", fn["tip"]))
    if "crown" in L:
        pts.append(("crown_base", L["crown"]["base"]))
        for i, t in enumerate(L["crown"]["tips"]):
            pts.append((f"crown_tip{i}", t))
    if "horns" in L:
        pts.append(("horn_base", L["horns"]["base"]))
        pts.append(("horn_tip", L["horns"]["tip"]))
    if "body" in L and "parts" in L:  # v21 blob
        pts.append(("body_top", L["body"]["top"]))
        for p in L["parts"]:
            pts.append((p["name"] + "_head", p["head"]))
            pts.append((p["name"] + "_tail", p["tail"]))
    return pts


VIEWS = {"left": (1, 2, -1), "front": (0, 2, 1), "top": (0, 1, 1)}  # (right axis, up axis, right sign)


def render_views(P, F, L, out, size=900):
    lo, hi = P.min(0), P.max(0)
    span = (hi - lo).max() * 1.08
    cen = (lo + hi) / 2
    tiles = []
    for name, (ri, ui, rs) in VIEWS.items():
        s = size / span

        def px(p):
            return (size / 2 + rs * (p[ri] - cen[ri]) * s, size / 2 - (p[ui] - cen[ui]) * s)
        acc = np.zeros((size, size), np.float32)
        lay = Image.new("L", (size, size), 0)
        dl = ImageDraw.Draw(lay)
        # x-ray density: draw triangles in batches into separate layers and accumulate
        for k in range(0, len(F), 1500):
            lay.paste(0, (0, 0, size, size))
            for tri in F[k:k + 1500]:
                dl.polygon([px(P[i]) for i in tri], fill=40)
            acc += np.asarray(lay, np.float32)
        img = (255 - np.clip(acc * 0.55, 0, 200)).astype(np.uint8)
        im = Image.fromarray(img).convert("RGB")
        d = ImageDraw.Draw(im)
        gz = size / 2 - (0 - cen[ui]) * s if ui == 2 else None
        if gz is not None:
            d.line([0, gz, size, gz], fill=(0, 150, 0), width=1)
        for nm, pts, col in chains_of(L):
            q = [px(p) for p in pts]
            if len(q) > 1:
                d.line(q, fill=col, width=3)
            for x, y in q:
                d.ellipse([x - 5, y - 5, x + 5, y + 5], outline=(0, 0, 0), fill=col)
        d.text((8, 8), name, fill=(0, 0, 0))
        tiles.append(im)
    o = Image.new("RGB", (size * 3, size), "white")
    for i, t in enumerate(tiles):
        o.paste(t, (i * size, 0))
    o.save(out)


def sec_report(P, F, axis, val, p, label):
    segs = section(P, F, axis, val)
    if not segs:
        return {"label": label, "section": "empty"}
    A = np.array([q for s in segs for q in s])
    other = [i for i in range(3) if i != axis]
    # keep only the section loop(s) local to the point: within 0.25 of it in-plane
    near = A[np.linalg.norm(A[:, other] - np.array(p)[other], axis=1) < 0.25]
    if len(near) == 0:
        near = A
    res = {"label": label, "plane": f"{'xyz'[axis]}={val:.3f}"}
    for i in other:
        a, b = near[:, i].min(), near[:, i].max()
        res["xyz"[i]] = [round(float(a), 3), round(float(b), 3),
                        round(float((p[i] - a) / max(b - a, 1e-6)), 2)]
    return res


def render_sections(P, F, specs, out, size=520):
    tiles = []
    for (axis, val, pts, title) in specs:
        segs = section(P, F, axis, val)
        other = [i for i in range(3) if i != axis]
        A = np.array([q for s in segs for q in s]) if segs else np.zeros((1, 3))
        lo = A[:, other].min(0)
        hi = A[:, other].max(0)
        for _, p in pts:
            lo = np.minimum(lo, np.array(p)[other])
            hi = np.maximum(hi, np.array(p)[other])
        span = (hi - lo).max() * 1.15 + 1e-6
        c = (lo + hi) / 2
        s = size / span
        rs = -1 if other == [1, 2] else 1  # Y-Z: head (-Y) to the right

        def px(q):
            q = np.array(q)
            return (size / 2 + rs * (q[other[0]] - c[0]) * s, size / 2 - (q[other[1]] - c[1]) * s)
        im = Image.new("RGB", (size, size), "white")
        d = ImageDraw.Draw(im)
        for a, b in segs:
            d.line([px(a), px(b)], fill=(0, 0, 0), width=2)
        if other[1] == 2:
            gy = size / 2 - (0 - c[1]) * s
            d.line([0, gy, size, gy], fill=(0, 160, 0))
        for nm, p in pts:
            x, y = px(p)
            d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(230, 0, 0))
            d.text((x + 6, y - 6), nm, fill=(200, 0, 0))
        d.text((6, 6), title, fill=(0, 0, 255))
        tiles.append(im)
    cols = 4
    rows = (len(tiles) + cols - 1) // cols
    o = Image.new("RGB", (size * cols, size * rows), "white")
    for i, t in enumerate(tiles):
        o.paste(t, ((i % cols) * size, (i // cols) * size))
    o.save(out)


def main():
    mesh_path, lmfile, beast, outdir = sys.argv[1:5]
    os.makedirs(outdir, exist_ok=True)
    L = json.load(open(lmfile))
    L = L.get(beast, L)
    P, F = load_mesh(mesh_path)
    print(f"mesh {len(P)} verts {len(F)} tris, bbox {P.min(0).round(3)} {P.max(0).round(3)}")
    rep = {"points": {}, "sections": []}
    for nm, p in all_points(L):
        ins = inside(P, F, p)
        dist = float(np.linalg.norm(P - np.array(p), axis=1).min())
        rep["points"][nm] = {"p": p, "inside": ins, "nearest_vert": round(dist, 3)}
        print(f"  {nm:16s} {str(p):30s} {'inside ' if ins else 'OUTSIDE'} nearest vert {dist:.3f}")
    render_views(P, F, L, os.path.join(outdir, f"{beast}_lm_views.png"))
    sp = L.get("spine", {})
    specs = []
    body_plan = L.get("body_plan", "")
    if "body" in L and "parts" in L:  # v21 blob: sagittal through the body column + sections per part
        bx = L["body"]["base"][0]
        specs.append((0, float(bx), [("body_top", L["body"]["top"])] +
                      [(p["name"], p["head"]) for p in L["parts"]], f"sagittal x={bx:.2f}"))
        for p in L["parts"]:
            for end in ("head", "tail"):
                q = p[end]
                specs.append((1, q[1], [(p["name"] + "_" + end, q)], f"y={q[1]:.2f} {p['name']}_{end}"))
                rep["sections"].append(sec_report(P, F, 1, q[1], q, f"{p['name']}_{end} @y"))
        bt = L["body"]["top"]
        mid = [bt[0], bt[1], bt[2] / 2]
        rep["sections"].append(sec_report(P, F, 1, mid[1], mid, "body_mid @y"))
    elif L.get("body_chain"):  # serpent: sagittal x=0 + transverse z sections of the neck, coil z
        specs.append((0, 0.0, [(f"b{i}", p) for i, p in enumerate(L["body_chain"])] +
                      [(k, v) for k, v in sp.items()], "sagittal x=0"))
        for i, p in enumerate(L["body_chain"]):
            specs.append((2, p[2], [(f"b{i}", p)], f"z={p[2]:.2f} (body_{i})"))
            rep["sections"].append(sec_report(P, F, 2, p[2], p, f"body_{i} @z"))
        specs.append((2, 0.10, [(f"t{i}", p) for i, p in enumerate(L["tail"])], "coil z=0.10"))
        for fn in L.get("fins", []):
            specs.append((2, fn["root"][2], [("finroot" + fn["side"], fn["root"])], f"fin root z"))
    elif "upright" in body_plan or "biped" in body_plan:
        xs = np.median([v[0] for v in sp.values()])
        specs.append((0, float(xs), [(k, v) for k, v in sp.items()], f"sagittal x={xs:.2f}"))
        for k in ("pelvis", "spine_01", "chest", "neck_base", "head"):
            if k in sp:
                specs.append((2, sp[k][2], [(k, sp[k])], f"z={sp[k][2]:.2f} ({k})"))
                rep["sections"].append(sec_report(P, F, 2, sp[k][2], sp[k], f"{k} @z"))
        for a in L.get("arms", []):
            for n, p in zip(("shoulder", "elbow", "wrist"), a["chain"]):
                rep["sections"].append(sec_report(P, F, 0, p[0], p, f"arm{a['side']}_{n} @x"))
        for l in L.get("legs", []):
            for n, p in zip(("hip", "knee", "ankle"), l["chain"][:3]):
                specs.append((2, p[2], [(f"{l['side']}_{n}", p)], f"z={p[2]:.2f} {l['side']}_{n}"))
                rep["sections"].append(sec_report(P, F, 2, p[2], p, f"{l['side']}_{n} @z"))
    else:  # horizontal quadruped
        xs = np.median([v[0] for k, v in sp.items() if k in ("pelvis", "spine_01", "chest")])
        specs.append((0, float(xs), [(k, v) for k, v in sp.items()] +
                      [(f"t{i}", p) for i, p in enumerate(L.get("tail", []))], f"sagittal x={xs:.2f}"))
        for k in ("pelvis", "spine_01", "chest", "neck_base", "neck_mid", "head"):
            if k in sp:
                specs.append((1, sp[k][1], [(k, sp[k])], f"y={sp[k][1]:.2f} ({k})"))
                rep["sections"].append(sec_report(P, F, 1, sp[k][1], sp[k], f"{k} @y"))
        for i, p in enumerate(L.get("tail", [])):
            rep["sections"].append(sec_report(P, F, 1, p[1], p, f"tail_{i} @y"))
        for l in L.get("legs", []):
            for n, p in zip(("hip", "knee", "ankle"), l["chain"][:3]):
                specs.append((2, p[2], [(f"{l['side']}_{n}", p)], f"z={p[2]:.3f} {l['side']}_{n}"))
                rep["sections"].append(sec_report(P, F, 2, p[2], p, f"{l['side']}_{n} @z"))
    render_sections(P, F, specs, os.path.join(outdir, f"{beast}_lm_sections.png"))
    for s in rep["sections"]:
        print("  SEC", s)
    json.dump(rep, open(os.path.join(outdir, f"{beast}_lm_report.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
