"""Review sheets and the lock's consistency metrics.

  python sheets.py explore BEAST PICK "reason" ["rejects"]   -> $ARTLAB_OUT/{beast}_explore.png: the candidate sheet
                                                                (the pick outlined, the producer's notes under it)
  python sheets.py seeds BEAST DIR TAG [PICK_SEED]           -> $ARTLAB_OUT/seeds_{beast}.png + the beast's entry in
                                                                $ARTLAB_WORK/consistency.json (gate: IoU >= 0.85,
                                                                cool drift <= 2%)
"""
import sys, itertools
from common import *
from colour import drift, fidelity, char_mask
import cv2

INK = (59, 28, 38)


def cell_grid(rows, out, title, thumb=260, lw=230, notes=None):
    """rows = [(row_label, [(img_or_path, caption, highlight)])]"""
    th = round(thumb * H / W); top = 70 + (26 * len(notes) if notes else 0)
    C = max(len(r[1]) for r in rows)
    sheet = Image.new("RGB", (lw + C * (thumb + 8) + 8, top + len(rows) * (th + 34) + 8), BG)
    d = ImageDraw.Draw(sheet)
    d.text((10, 10), title, fill=INK, font=font(24))
    for k, n in enumerate(notes or []):
        d.text((10, 44 + k * 26), n, fill=INK, font=font(18))
    for i, (lab, cells) in enumerate(rows):
        y = top + i * (th + 34)
        for k, ln in enumerate(wrap(lab, 22)):
            d.text((10, y + 10 + k * 22), ln, fill=INK, font=font(18))
        for j, (p, cap, hi) in enumerate(cells):
            x = lw + 8 + j * (thumb + 8)
            if p is None or (not isinstance(p, Image.Image) and not pathlib.Path(p).exists()):
                continue
            im = p if isinstance(p, Image.Image) else Image.open(p)
            sheet.paste(im.convert("RGB").resize((thumb, th), Image.LANCZOS), (x, y))
            if hi:
                d.rectangle([x - 4, y - 4, x + thumb + 3, y + th + 3], outline=(200, 60, 30), width=6)
            d.text((x + 4, y + th + 5), cap, fill=INK, font=font(16))
    sheet.save(out)


def wrap(s, n):
    out, line = [], ""
    for w_ in s.split():
        if len(line + w_) > n:
            out.append(line); line = ""
        line += w_ + " "
    return out + [line]


def explore_sheet(beast, pick, reason, rejects=""):
    d = WORK / "explore"
    fs = sorted(d.glob(f"{beast}_*.png"), key=lambda p: int(p.stem.split("_")[1]))
    cells = [(p, p.stem.split("_")[1], p.stem.split("_")[1] == str(pick)) for p in fs]
    rows = [("", cells[i:i + 8]) for i in range(0, len(cells), 8)]
    notes = [f"PICK: #{pick} (red frame). {reason}"] + ([f"Rejected: {rejects}"] if rejects else [])
    cell_grid(rows, OUT / f"{beast}_explore.png", f"{beast.capitalize()} design exploration: {len(cells)} candidates, "
              f"img2img 0.80 from randomised colour-mass layouts + canny 0.4 + InstantStyle 0.4 + house prompt", thumb=230, lw=20, notes=notes)


def consistency(beast, ims):
    """Shape = mean pairwise IoU of the character masks (same canvas, so IoU measures pose/silhouette agreement).
    Colour = palette distance to the swatch block (mean/max) + mean pairwise distance between the seeds' mean Lab
    colours (seed-to-seed colour agreement) + cool drift."""
    from masks import line_mask
    rgbs = [np.asarray(Image.open(p).convert("RGB")) for p in ims]
    ms = [line_mask(r)[0] for r in rgbs]          # SAM box mask (robust on cream-on-cream and glow halos)
    ious = [(a & b).sum() / max((a | b).sum(), 1) for a, b in itertools.combinations(ms, 2)]
    labs = [cv2.cvtColor(r, cv2.COLOR_RGB2LAB).astype(np.float32)[m].mean(0) for r, m in zip(rgbs, ms)]
    cd = [float(np.linalg.norm(a - b)) for a, b in itertools.combinations(labs, 2)]
    fids = [fidelity(Image.fromarray(r), beast) for r in rgbs]
    drs = [drift(Image.fromarray(r))[0] for r in rgbs]
    return dict(iou_mean=round(float(np.mean(ious)), 3), iou_min=round(float(np.min(ious)), 3),
                colour_pair_mean=round(float(np.mean(cd)), 2), colour_pair_max=round(float(np.max(cd)), 2),
                pal_mean=round(float(np.mean(fids)), 2), pal_max=round(float(np.max(fids)), 2),
                drifted=int(sum(x > 0.02 for x in drs)), n=len(ims), drift_max=round(float(max(drs)), 4))


def seeds(beast, dd, tag, pick=None):
    dd = pathlib.Path(dd)
    fs = sorted([p for p in dd.glob(f"{tag}_{beast}_*.png") if "prelock" not in p.stem], key=lambda p: int(p.stem.split("_")[-1]))
    m = consistency(beast, fs)
    cp = WORK / "consistency.json"
    allm = json.loads(cp.read_text()) if cp.exists() else {}
    allm[beast] = dict(m, dir=str(dd.name), tag=tag); cp.write_text(json.dumps(allm, indent=1))
    lg = json.loads((dd / "log.json").read_text())
    cells = []
    for p in fs:
        e = lg.get(p.stem, {}); s = p.stem.split("_")[-1]
        cells.append((p, f"seed {s} pal {e.get('fid', '?')} cool {e.get('drift', 0) * 100:.1f}%", s == str(pick)))
    ref = WORK / f"prep_{beast}.png"
    notes = [f"Shape: mask IoU mean {m['iou_mean']} (min {m['iou_min']}).  Colour: seed-to-seed Lab distance mean "
             f"{m['colour_pair_mean']} (max {m['colour_pair_max']}); palette distance mean {m['pal_mean']} (max {m['pal_max']}); "
             f"cool drift {m['drifted']}/{m['n']}"]
    cell_grid([(f"{beast} lock, {len(fs)} seeds", cells)], OUT / f"seeds_{beast}.png",
              f"{beast.capitalize()} design lock: same canny + colour block, new seeds (red = used for final)", thumb=230, lw=150, notes=notes)
    print(beast, m)


if __name__ == "__main__":
    c = sys.argv[1]
    if c == "explore":
        explore_sheet(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] if len(sys.argv) > 5 else "")
    elif c == "seeds":
        seeds(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5] if len(sys.argv) > 5 else None)
