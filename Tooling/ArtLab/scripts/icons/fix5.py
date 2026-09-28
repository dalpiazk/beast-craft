import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def spore_puff(d, C, rng):
    """Spore Cloud reroll: a mushroom at the bottom puffing a big dotted spore cloud upward."""
    for (x, y, r) in ((.32, .38, .15), (.5, .28, .18), (.68, .38, .15), (.5, .44, .16)):
        ell(d, x, y, r, r, (206, 214, 120))
    for k in range(16):
        ell(d, rng.uniform(.24, .76), rng.uniform(.18, .52), .022, .022, C["dark"])
    poly(d, [(.44, .88), (.56, .88), (.54, .66), (.46, .66)], (236, 226, 200))
    d.chord([.26 * S, .52 * S, .74 * S, .84 * S], 180, 360, fill=(196, 80, 70))
    for (x, y) in ((.38, .62), (.52, .58), (.62, .64)):
        ell(d, x, y, .03, .025, (250, 240, 220))


def thorn_whip(d, C, rng):
    """Thorn Lash reroll: one thick thorned vine whip in an S-lash, big thorns."""
    pts = [(.16 + .7 * t, .84 - .7 * t + .14 * math.sin(t * 6.5)) for t in np.linspace(0, 1, 26)]
    line(d, pts, C["dark"], .11); line(d, pts, C["main"], .075)
    for k in range(2, 25, 3):
        x, y = pts[k]; sg = 1 if k % 2 else -1
        poly(d, [(x - .03, y), (x + .03, y), (x + sg * .07, y - .08)], C["hi"])
    for (x, y) in (pts[-1],):
        ell(d, x, y, .05, .05, C["acc"])

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"thorn_lash":       ("Nature", "vine", {}, "a thorned green vine whip lashing, sharp thorns")','"thorn_lash":       ("Nature", "thorn_whip", {}, "one thick green thorned vine whip lashing in an S curve, big sharp thorns")')
t=t.replace('"spore_cloud":      ("Nature", "cloud", {"dots": True}, "a puffy cloud of glowing spores, poison motes")','"spore_cloud":      ("Nature", "spore_puff", {}, "a red mushroom puffing a big cloud of yellow-green poison spores")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"serpent_bite", "undertow"]   # producer','"serpent_bite", "undertow", "thorn_lash", "spore_cloud"]   # producer')
q.write_text(u)
