import pathlib, re
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def wind_spear(d, C, rng):
    """Wind Lance reroll: one thick spear of air wrapped in spiralling wind lines (a line attack)."""
    lance(d, C, rng, w=.09, flare=False)
    for k in range(4):
        t = .25 + k * .15; x, y = .14 + .74 * t, .86 - .74 * t
        arc(d, x, y, .1, 200, 380, C["acc"], .025)


def rising_wind(d, C, rng):
    """Updraft reroll: three big upward wind swooshes lifting a wing (a team speed/move buff)."""
    for k, x in enumerate((.3, .5, .7)):
        pts = [(x + .06 * math.sin(t * 5 + k), .86 - .6 * t) for t in np.linspace(0, 1, 14)]
        line(d, pts, C["hi"] if k == 1 else C["main"], .05)
        tx, ty = pts[-1]; poly(d, [(tx, ty - .08), (tx + .07, ty + .02), (tx - .07, ty + .02)], C["acc"])


def talon_dive(d, C, rng):
    """Sky Rend reroll: one big hooked talon plunging down, tearing a jagged gash."""
    pts = [(.62, .12), (.72, .3), (.7, .52), (.58, .7), (.44, .76)]
    line(d, pts, C["dark"], .1); line(d, pts, C["main"], .07)
    poly(d, [(.44, .7), (.44, .82), (.3, .8)], C["hi"])
    gash = [(.18, .9), (.3, .84), (.26, .78), (.4, .72)]
    line(d, gash, C["acc"], .03)
    for k in range(3):
        line(d, [(.78 + k * .04, .1 + k * .05), (.86 + k * .04, .24 + k * .05)], C["hi"], .02)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
LT = {"wind_lance": ('"wind_spear", {}', "one thick spear of compressed air wrapped in spiralling wind"),
      "updraft": ('"rising_wind", {}', "three big rising wind swooshes with upward arrows, lifting air"),
      "sky_rend": ('"talon_dive", {}', "one big hooked golden talon plunging down, tearing a jagged gash")}
for k, (pk, words) in LT.items():
    t, n = re.subn(r'"%s":\s+\("Air", "[a-z_0-9]+", \{[^}]*\}, "[^"]*"\)' % k, '"%s": ("Air", %s, "%s")' % (k, pk, words), t); assert n == 1, k
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"static_charge", "storm_dive"]   # producer','"static_charge", "storm_dive", "wind_lance", "updraft", "sky_rend"]   # producer')
q.write_text(u)
