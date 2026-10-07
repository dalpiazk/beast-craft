"""Projected eye PAINTING (v22, the Shaman): the Meshy mesh kept the two eye bumps of the approved
sprite but lost their glow (both read as pale knobs, so the face shows no eyes). eyefix_bl.py can only
mirror an eye that exists; here there is none, so a procedural glowing-eye decal (the sprite's look:
pale-yellow core, orange glow, thin dark plum rim, soft halo) is projected onto the base-colour
texture along each eye bump's own surface normal. Plain Python (numpy + Pillow, no Blender).

    python eyepaint.py CFG.json SRC.glb OUT.glb [OUTDIR]

CFG: {"ops": [{"name", "aim": [x, z] (native coords; the surface point is found by a +Y ray from
the front), "r": [rx, ry] (decal half-axes, world units, in the plane normal to the bump),
"normal_radius": r (faces averaged for the projection normal), "depth": d (max distance of a
painted point from the decal plane, so only the bump's own surface is painted)}], "pad": texels}.
Only the base-colour image bufferView of OUT.glb differs from SRC.glb (glbtex.splice); OUTDIR gets
the painted texture, a paint mask and paint_report.json."""
import io, json, os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import glbtex  # noqa: E402
import glb_gate  # noqa: E402

CORE, GLOW, RIM, HALO = (np.array(c, float) for c in ((255, 238, 130), (255, 158, 38), (74, 34, 44), (255, 186, 80)))


def decal(rn):
    """rn = normalised ellipse radius -> (rgb, alpha)."""
    rgb = np.where(rn[:, None] < 0.5, CORE, CORE + (GLOW - CORE) * np.clip((rn[:, None] - 0.5) / 0.32, 0, 1))
    rim = np.clip((rn - 0.82) / 0.08, 0, 1) * np.clip((1.0 - rn) / 0.06 + 1.0, 0, 1)
    rgb = rgb + (RIM - rgb) * rim[:, None] * (rn[:, None] <= 1.0)
    halo = rn > 1.0
    rgb = np.where(halo[:, None], HALO, rgb)
    a = np.where(halo, 0.55 * np.clip((1.35 - rn) / 0.35, 0, 1) ** 2, 1.0)
    return rgb, a


def first_hit(P, F, o, d):
    v0, v1, v2 = P[F[:, 0]], P[F[:, 1]], P[F[:, 2]]
    e1, e2 = v1 - v0, v2 - v0
    h = np.cross(d, e2); a = (e1 * h).sum(1)
    ok = np.abs(a) > 1e-12
    f = np.where(ok, 1 / np.where(ok, a, 1), 0)
    s = o - v0; u = f * (s * h).sum(1); q = np.cross(s, e1); v = f * (q @ d); t = f * (e2 * q).sum(1)
    hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-9)
    i = np.where(hit)[0][np.argmin(t[hit])]
    return o + d * t[i], i


def main():
    cfg = json.load(open(sys.argv[1])); src, dst = sys.argv[2], sys.argv[3]
    outdir = sys.argv[4] if len(sys.argv) > 4 else os.path.dirname(os.path.abspath(dst))
    os.makedirs(outdir, exist_ok=True)
    js, b = glb_gate.parse_glb(src)
    cache = {}
    pr = js["meshes"][0]["primitives"][0]
    P = glb_gate._accessor(js, b, pr["attributes"]["POSITION"], cache) @ glb_gate.C.T  # gltf -> blender
    UV = glb_gate._accessor(js, b, pr["attributes"]["TEXCOORD_0"], cache)
    F = glb_gate._accessor(js, b, pr["indices"], cache).astype(np.int64).reshape(-1, 3)
    data, mime = glbtex.extract(src)
    img = np.array(Image.open(io.BytesIO(data)).convert("RGB"), float)
    H, W = img.shape[:2]
    FN = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]]); FA = np.linalg.norm(FN, axis=1)
    FN /= np.maximum(FA, 1e-12)[:, None]; CEN = P[F].mean(1)
    acc = np.zeros((H, W, 3)); wsum = np.zeros((H, W)); rep = {"ops": []}
    for op in cfg["ops"]:
        E, ti0 = first_hit(P, F, np.array([op["aim"][0], -5.0, op["aim"][1]]), np.array([0.0, 1.0, 0.0]))
        near = np.linalg.norm(CEN - E, axis=1) < op.get("normal_radius", 0.03)
        nrm = FN[near] * FA[near][:, None]
        nrm *= np.sign(nrm @ np.array([0, -1.0, 0]))[:, None]  # face the front (Meshy winding is mixed)
        n = nrm.sum(0); n /= np.linalg.norm(n)
        up = np.array([0, 0, 1.0]); ex = np.cross(up, n); ex /= np.linalg.norm(ex); ey = np.cross(n, ex)
        rx, ry = op["r"]; reach = max(rx, ry) * 1.4 + op.get("depth", 0.03)
        cand = np.where(np.linalg.norm(CEN - E, axis=1) < reach)[0]
        painted = 0
        for t in cand:
            tri = F[t]; uv = UV[tri] * [W, H] - 0.5
            x0, y0 = np.floor(uv.min(0) - cfg.get("pad", 1.5)).astype(int); x1, y1 = np.ceil(uv.max(0) + cfg.get("pad", 1.5)).astype(int)
            x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W - 1), min(y1, H - 1)
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            G = np.stack([gx.ravel(), gy.ravel()], 1).astype(float)
            a, bb, c = uv
            den = (bb[1] - c[1]) * (a[0] - c[0]) + (c[0] - bb[0]) * (a[1] - c[1])
            if abs(den) < 1e-12:
                continue
            l0 = ((bb[1] - c[1]) * (G[:, 0] - c[0]) + (c[0] - bb[0]) * (G[:, 1] - c[1])) / den
            l1 = ((c[1] - a[1]) * (G[:, 0] - c[0]) + (a[0] - c[0]) * (G[:, 1] - c[1])) / den
            l2 = 1 - l0 - l1
            tol = cfg.get("pad", 1.5) / max(1.0, np.linalg.norm(a - bb), np.linalg.norm(bb - c))
            m = (l0 >= -tol) & (l1 >= -tol) & (l2 >= -tol)
            if not m.any():
                continue
            L = np.stack([l0, l1, l2], 1)[m].clip(-tol, 1 + tol)
            X = L @ P[tri]
            d = X - E
            if abs(FN[t] @ n) < 0.15:
                continue
            dz = np.abs(d @ n); xx = (d @ ex) / rx; yy = (d @ ey) / ry
            rn = np.sqrt(xx ** 2 + yy ** 2)
            keep = (rn < 1.35) & (dz < op.get("depth", 0.03))
            if not keep.any():
                continue
            rgb, al = decal(rn[keep])
            px = G[m][keep].astype(int)
            np.add.at(acc, (px[:, 1], px[:, 0]), rgb * al[:, None])
            np.add.at(wsum, (px[:, 1], px[:, 0]), al)
            painted += int(keep.sum())
        rep["ops"].append({"name": op["name"], "surface_point": E.round(4).tolist(), "normal": n.round(3).tolist(),
                           "texels": painted})
        print(f"EYEPAINT {op['name']}: surface {E.round(3)} normal {n.round(3)} texels {painted}")
    m = wsum > 0
    # a texel hit several times (overlapping UV padding) averages its samples
    out = img.copy()
    col = acc[m] / wsum[m][:, None]
    alpha = np.clip(wsum[m], 0, 1)
    out[m] = img[m] * (1 - alpha[:, None]) + col * alpha[:, None]
    res = Image.fromarray(out.clip(0, 255).astype(np.uint8))
    res.save(os.path.join(outdir, "painted_tex.png"))
    Image.fromarray((np.clip(wsum, 0, 1) * 255).astype(np.uint8)).save(os.path.join(outdir, "paint_mask.png"))
    buf = io.BytesIO()
    if mime == "image/jpeg":
        res.save(buf, "JPEG", quality=95)
    else:
        res.save(buf, "PNG")
    glbtex.splice(src, dst, buf.getvalue(), mime)
    rep["mime"] = mime; rep["texture"] = [W, H]
    json.dump(rep, open(os.path.join(outdir, "paint_report.json"), "w"), indent=1)
    print("EYEPAINT WROTE", dst)


if __name__ == "__main__":
    main()
