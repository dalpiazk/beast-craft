import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def clap(d, C, rng):
    """Thunderclap reroll: a bolt striking the centre with concentric shockwave rings around it (area)."""
    for k, r in enumerate((.42, .33, .24)):
        arc(d, .5, .54, r, 0, 360, C["hi"] if k % 2 == 0 else C["acc"], .03)
    bolt(d, C, rng, scale=.62, cx=.5)


def bolt_row(d, C, rng, n=4):
    """Plasma Barrage reroll: four small lightning bolts in a diagonal row, a line attack."""
    for k in range(n):
        t = k / (n - 1); cx, cy = .24 + .52 * t, .76 - .52 * t; sc = .2 + .05 * t
        pts = [(.3, .1), (.52, .1), (.44, .38), (.6, .38), (.34, .9), (.4, .52), (.26, .52)]
        poly(d, [(cx + (x - .43) * sc * 1.4, cy + (y - .5) * sc * 1.4) for x, y in pts], C["main"] if k % 2 == 0 else C["hi"])
    line(d, [(.14, .86), (.86, .14)], C["dark"], .02)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"thunderclap":      ("Lightning", "impact", {"ground": False, "ring": True}, "a deafening thunderclap shockwave ring, crackling")',
            '"thunderclap":      ("Lightning", "clap", {}, "a lightning bolt striking the centre, booming shockwave rings around it")')
t=t.replace('"plasma_barrage":   ("Lightning", "orbs_line", {}, "four glowing plasma orbs in a row, electric trail")',
            '"plasma_barrage":   ("Lightning", "bolt_row", {}, "four small yellow lightning bolts flying in a diagonal row")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"sunder", "shrapnel_burst"]   # producer','"sunder", "shrapnel_burst", "thunderclap", "plasma_barrage"]   # producer')
q.write_text(u)
