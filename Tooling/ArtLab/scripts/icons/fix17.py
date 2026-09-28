import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
a=s.index('def cleave_arc('); b=s.index('\nPRIMS = {')
s=s[:a]+'''def cleave_arc(d, C, rng):
    """Great Cleave reroll: one wide bright crescent blade sweep, pointed at both ends, with motion lines."""
    outer = [(.5 + .42 * math.cos(math.radians(t)), .62 + .42 * math.sin(math.radians(t))) for t in np.linspace(195, 345, 24)]
    inner = [(.5 + .34 * math.cos(math.radians(t)), .7 + .3 * math.sin(math.radians(t))) for t in np.linspace(345, 195, 24)]
    poly(d, outer + inner, C["hi"])
    for k in range(3):
        arc(d, .5, .72, .26 - k * .06, 215, 325, C["main"], .015)

'''+s[b:]
p.write_text(s)
