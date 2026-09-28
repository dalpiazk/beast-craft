import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
a=s.index('def talon_dive('); b=s.index('\nPRIMS = {')
s=s[:a]+'''def talon_dive(d, C, rng):
    """Sky Rend reroll: one big hooked talon (a claw crescent) plunging down, tearing three gash lines."""
    cx, cy = .38, .4
    outer = [(cx + .36 * math.cos(math.radians(a)), cy + .36 * math.sin(math.radians(a))) for a in np.linspace(-70, 95, 18)]
    inner = [(cx + .2 * math.cos(math.radians(a)) + .06, cy + .22 * math.sin(math.radians(a)) + .02) for a in np.linspace(95, -70, 18)]
    tip = [(cx + .36 * math.cos(math.radians(100)) - .02, cy + .36 * math.sin(math.radians(100)) + .04)]
    poly(d, outer + tip + inner, C["main"])
    poly(d, outer[:9] + [(p[0] - .03, p[1]) for p in reversed(outer[:9])], C["hi"]) if False else None
    for k in range(3):
        line(d, [(.2 + k * .12, .9), (.36 + k * .12, .74)], C["acc"], .03)

'''+s[b:]
p.write_text(s)
