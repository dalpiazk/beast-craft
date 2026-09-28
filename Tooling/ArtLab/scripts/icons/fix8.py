import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def toxic_cloud(d, C, rng):
    """Spore Cloud reroll 2 (producer): one large billowing toxic-green cloud, small spore specks."""
    for (x, y, r) in ((.28, .56, .17), (.42, .4, .2), (.6, .38, .19), (.73, .54, .17), (.5, .6, .22), (.34, .66, .13), (.66, .67, .13)):
        ell(d, x, y, r + .02, r + .02, (60, 110, 40))
    for (x, y, r) in ((.28, .56, .17), (.42, .4, .2), (.6, .38, .19), (.73, .54, .17), (.5, .6, .22), (.34, .66, .13), (.66, .67, .13)):
        ell(d, x, y, r, r, (150, 210, 70))
    for (x, y, r) in ((.4, .36, .09), (.58, .34, .08), (.3, .52, .07)):
        ell(d, x, y, r, r * .7, (205, 240, 130))
    for k in range(22):
        a = rng.uniform(0, 2 * math.pi); r = rng.uniform(.1, .44)
        ell(d, .5 + r * math.cos(a), .52 + r * math.sin(a) * .9, .016, .016, (235, 250, 170))


def talon_bolts(d, C, rng):
    """Thunder Talons (Lightning redo): three thick jagged lightning claw slashes."""
    for k in range(3):
        o = (k - 1) * .16
        pts = [(.28 + o, .84), (.42 + o, .6), (.34 + o, .56), (.52 + o, .32), (.46 + o, .3), (.64 + o, .12)]
        line(d, pts, C["dark"], .09); line(d, pts, C["main"], .06); line(d, pts, C["hi"], .02)


def chain_nodes(d, C, rng):
    """Chain Lightning (redo): jagged bolts jumping between three targets (a chain)."""
    nodes = [(.24, .72), (.5, .26), (.78, .7)]
    for (a, b) in ((0, 1), (1, 2), (2, 0)):
        (x0, y0), (x1, y1) = nodes[a], nodes[b]; pts = [(x0, y0)]
        for t in (.25, .5, .75):
            nx, ny = -(y1 - y0), (x1 - x0); sg = 1 if t != .5 else -1
            pts.append((x0 + (x1 - x0) * t + nx * .12 * sg, y0 + (y1 - y0) * t + ny * .12 * sg))
        pts.append((x1, y1)); line(d, pts, C["dark"], .07); line(d, pts, C["main"], .045); line(d, pts, C["hi"], .015)
    for (x, y) in nodes:
        ell(d, x, y, .08, .08, C["acc"]); ell(d, x, y, .04, .04, C["hi"])


def charged_feather(d, C, rng):
    """Static Charge (redo): one big feather crackling with cyan static arcs (a self power-up)."""
    feather(d, C, rng, sparks=False)
    for (cx, cy, r, a0) in ((.5, .5, .4, 200), (.5, .5, .34, 20)):
        pts = [(cx + r * math.cos(math.radians(a0 + k * 12)) + rng.uniform(-.02, .02), cy + r * math.sin(math.radians(a0 + k * 12)) + rng.uniform(-.02, .02)) for k in range(9)]
        line(d, pts, C["acc"], .025)


def storm_dive_bolt(d, C, rng):
    """Storm Dive (redo): a dark storm cloud at the top, one thick bolt diving down from it onto a target."""
    for (x, y, r) in ((.3, .22, .13), (.46, .16, .16), (.64, .2, .14), (.52, .28, .15)):
        ell(d, x, y, r, r, C["dark"])
    pts = [(.46, .3), (.58, .3), (.52, .5), (.66, .5), (.44, .88), (.48, .6), (.36, .6)]
    poly(d, pts, C["main"]); poly(d, [(x * .5 + .26, y) for x, y in pts], C["hi"]) if False else None
    ell(d, .44, .88, .12, .04, C["acc"])


def clap2(d, C, rng):
    """Thunderclap (redo): a bolt striking the centre, jagged shockwave rings around it (area)."""
    for k, r in enumerate((.43, .33)):
        pts = [(.5 + (r + (.02 if j % 2 else -.02)) * math.cos(j * math.pi / 12), .54 + (r + (.02 if j % 2 else -.02)) * math.sin(j * math.pi / 12)) for j in range(25)]
        line(d, pts, C["acc"] if k else C["hi"], .025)
    bolt(d, C, rng, scale=.6, cx=.5)


def bolt_row2(d, C, rng, n=4):
    """Plasma Barrage (redo): four bold bolts flying in a diagonal row (a line attack)."""
    for k in range(n):
        t = k / (n - 1); cx, cy = .27 + .46 * t, .73 - .46 * t; sc = .34
        pts = [(.3, .1), (.52, .1), (.44, .38), (.6, .38), (.34, .9), (.4, .52), (.26, .52)]
        P2 = [(cx + (x - .43) * sc, cy + (y - .5) * sc) for x, y in pts]
        poly(d, [(x + .012, y + .012) for x, y in P2], C["dark"]); poly(d, P2, C["main"] if k % 2 == 0 else C["acc"])

PRIMS = {''',1)
# thicker thorns
s=s.replace('''        poly(d, [(x - .03, y), (x + .03, y), (x + sg * .07, y - .08)], C["hi"])''','''        poly(d, [(x - .045, y), (x + .045, y), (x + sg * .12, y - .13)], C["hi"])''')
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"Lightning": dict(bg_in=(120, 140, 190), bg_out=(36, 42, 70), main=(255, 222, 70), hi=(255, 250, 200), dark=(60, 70, 110), acc=(255, 240, 150))',
            '"Lightning": dict(bg_in=(62, 74, 112), bg_out=(14, 18, 36), main=(255, 228, 60), hi=(215, 248, 255), dark=(28, 36, 64), acc=(90, 220, 255))')
t=t.replace('"a tan forest mushroom puffing a big cloud of yellow-green poison spores"','"one large billowing toxic green spore cloud, small glowing spore specks"')
t=t.replace('("Nature", "spore_puff", {}','("Nature", "toxic_cloud", {}')
t=t.replace('"one thick green thorned vine whip lashing in an S curve, big sharp thorns"','"one thick green vine whip with large sharp thorns, lashing in an S curve"')
LT = {
 "thunder_talons": ('"talon_bolts", {}', "three thick jagged lightning claw slashes, electric yellow and cyan, dark stormy background"),
 "chain_lightning": ('"chain_nodes", {}', "lightning jumping in a chain between three glowing points, electric yellow and cyan"),
 "static_charge": ('"charged_feather", {}', "one feather crackling with cyan static electricity arcs, electric yellow"),
 "storm_dive": ('"storm_dive_bolt", {}', "a thick lightning bolt diving down from a dark storm cloud, electric yellow"),
 "thunderclap": ('"clap2", {}', "a lightning bolt striking the centre, jagged electric shockwave rings, cyan and yellow"),
 "plasma_barrage": ('"bolt_row2", {}', "four bold lightning bolts flying in a diagonal row, electric yellow and cyan"),
}
import re
for k, (pk, words) in LT.items():
    t, n = re.subn(r'"%s":\s+\("Lightning", "[a-z_0-9]+", \{[^}]*\}, "[^"]*"\)' % k, '"%s": ("Lightning", %s, "%s")' % (k, pk, words), t); assert n == 1, k
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"thunderclap", "plasma_barrage"]   # producer','"thunderclap", "plasma_barrage", "thunder_talons", "chain_lightning", "static_charge", "storm_dive"]   # producer')
q.write_text(u)
