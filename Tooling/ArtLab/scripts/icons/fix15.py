import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
old='''    line(d, [(.5, .88), (.5, .56), (.6, .4), (.7, .4)], C["dark"], .035)
    for k in range(5):
        a = math.radians(40 + k * 22); ell(d, .7 + .1 * math.cos(a), .44 + .12 * math.sin(a), .06, .08, C["main"])'''
new='''    line(d, [(.42, .9), (.42, .5), (.5, .3), (.62, .3), (.68, .42)], C["dark"], .05)
    for k in range(5):                                      # drooping head, petals hanging down
        a = math.radians(30 + k * 30); ell(d, .68 + .12 * math.cos(a), .5 + .14 * math.sin(a), .07, .1, C["main"])
    ell(d, .68, .48, .06, .06, C["dark"])
    poly(d, [(.42, .66), (.26, .6), (.3, .72)], C["dark"])'''
assert old in s; s=s.replace(old,new); p.write_text(s)
