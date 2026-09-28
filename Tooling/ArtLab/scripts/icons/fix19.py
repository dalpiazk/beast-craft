import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def thorn_whip2(d, C, rng):
    """Thorn Lash reroll 2 (producer): a thick S-whip with 6 LARGE curved hook thorns (each >10% of the width)."""
    pts = [(.2 + .6 * t, .84 - .68 * t + .13 * math.sin(t * 6.3)) for t in np.linspace(0, 1, 30)]
    line(d, pts, C["dark"], .12); line(d, pts, C["main"], .085)
    for j, k in enumerate((4, 8, 12, 16, 20, 25)):
        x, y = pts[k]; (x2, y2) = pts[k + 1]; tx, ty = x2 - x, y2 - y; L = math.hypot(tx, ty); tx, ty = tx / L, ty / L
        sg = 1 if j % 2 else -1; nx, ny = -ty * sg, tx * sg
        base = [(x - tx * .06, y - ty * .06), (x + tx * .06, y + ty * .06)]
        tip = (x + nx * .15 + tx * .06, y + ny * .15 + ty * .06); mid = (x + nx * .08 - tx * .01, y + ny * .08 - ty * .01)
        poly(d, [base[0], mid, tip, base[1]], C["dark"])
        poly(d, [(base[0][0] + nx * .01, base[0][1] + ny * .01), (mid[0], mid[1]), (tip[0] - nx * .01, tip[1] - ny * .01), (base[1][0] + nx * .01, base[1][1] + ny * .01)], C["hi"])


def sunder_split(d, C, rng):
    """Sunder reroll 2 (producer): a riveted steel plate split diagonally by a bright slash, the two halves separating."""
    h = [(.5 + .36 * math.cos(math.radians(a)), .5 + .36 * math.sin(math.radians(a))) for a in range(-90, 270, 45)]
    for sgn in (-1, 1):
        off = (.05 * sgn, .05 * sgn)
        half = [p for p in h if (p[0] - .5) - (p[1] - .5) * -1 >= 0] if sgn > 0 else [p for p in h if (p[0] - .5) + (p[1] - .5) <= 0]
        half = [(x + off[0], y + off[1]) for x, y in (half + [(.5 + .4 * .7, .5 - .4 * .7), (.5 - .4 * .7, .5 + .4 * .7)])]
        cx = sum(x for x, _ in half) / len(half); cy = sum(y for _, y in half) / len(half)
        half.sort(key=lambda p: math.atan2(p[1] - cy, p[0] - cx))
        poly(d, half, C["main"]); poly(d, [(cx + (x - cx) * .75, cy + (y - cy) * .75) for x, y in half], C["hi"])
        for k in range(3):
            ell(d, cx + (half[k][0] - cx) * .82, cy + (half[k][1] - cy) * .82, .02, .02, C["dark"])
    line(d, [(.16, .86), (.86, .16)], (255, 236, 170), .05); line(d, [(.16, .86), (.86, .16)], (255, 255, 240), .018)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('("Nature", "thorn_whip", {}, "one thick green vine whip with large sharp thorns, lashing in an S curve")','("Nature", "thorn_whip2", {}, "a thick green vine whip covered in big curved hooked thorns, lashing in an S curve")')
t=t.replace('("MetalD", "plate", {"slash": True}, "a steel armour plate split by a bronze slash, deep crack, sparks, steel grey and bronze")','("MetalD", "sunder_split", {}, "a riveted steel armour plate split in two by a bright diagonal slash, halves breaking apart")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('REROLL_B = ["granite_bulwark"]','REROLL_B = ["granite_bulwark", "thorn_lash", "sunder"]')
q.write_text(u)
