import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
old='''    feather(d, C, rng, sparks=False)
    for (cx, cy, r, a0)'''
new='''    ang = math.radians(-50)
    vane = [(.5 + .4 * math.cos(t) , .5 + .14 * math.sin(t) * (1 - .35 * math.cos(t))) for t in np.linspace(0, 2 * math.pi, 40)]
    poly(d, rot(vane, ang), C["main"])
    for k in range(4):                                     # notches in the vane
        x = .3 + k * .12; sg = 1 if k % 2 else -1
        poly(d, rot([(x, .5 + sg * .16), (x + .05, .5 + sg * .06), (x + .07, .5 + sg * .17)], ang), C["dark"])
    line(d, rot([(.06, .5), (.9, .5)], ang), C["hi"], .025)
    for (cx, cy, r, a0)'''
assert old in s; s=s.replace(old,new); p.write_text(s)
