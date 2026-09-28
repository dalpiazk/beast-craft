import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def shell_big(d, C, rng):
    """Deep Shell fix: one big scallop filling ~80% of the circle, no bubble ring."""
    pts = [(.5, .86)] + [(.5 - .4 * math.cos(t), .56 - .4 * math.sin(t)) for t in np.linspace(0, math.pi, 16)]
    poly(d, pts, C["acc"])
    for k in range(-4, 5):
        t = math.pi / 2 + k * .3
        line(d, [(.5, .84), (.5 - .36 * math.cos(t), .56 - .36 * math.sin(t))], mix(C["acc"], C["dark"], .45), .025)
    poly(d, [(.4, .9), (.6, .9), (.56, .8), (.44, .8)], mix(C["acc"], C["dark"], .3))
    ell(d, .36, .3, .05, .03, C["hi"])

PRIMS = {''',1)
p.write_text(s)
