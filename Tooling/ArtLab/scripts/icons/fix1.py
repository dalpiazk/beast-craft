import pathlib, re
p=pathlib.Path('motifs.py'); s=p.read_text()
# flame: layered teardrops with side tongues
a=s.index('def flame('); b=s.index('def rain(')
s=s[:a]+'''def flame(d, C, rng, cx=.5, cy=.58, s=1.0, feather=False):
    for sc, c in ((1.0, C["dark"]), (.75, C["main"]), (.45, C["hi"])):
        r = .24 * s * sc; y0 = cy + .08 * s * (1 - sc)
        poly(d, [(cx, y0 - r * 2.3), (cx + r * .95, y0 - r * .2), (cx - r * .95, y0 - r * .2)], c)
        ell(d, cx, y0, r, r, c)
        if sc == 1.0:
            for sgn in (-1, 1):
                poly(d, [(cx + sgn * r * .7, y0 - r * .6), (cx + sgn * r * 1.25, y0 - r * 1.7), (cx + sgn * r * .95, y0 + r * .1)], c)
    if feather:
        line(d, [(cx - .28 * s, cy + .34 * s), (cx + .02, cy - .02)], C["hi"], .025)


'''+s[b:]
# fangs: proper open jaw
a=s.index('def fangs('); b=s.index('def wave(')
s=s[:a]+'''def fangs(d, C, rng, n=2, jaw=True):
    if jaw:
        d.chord([.1 * S, .06 * S, .9 * S, .5 * S], 0, 180, fill=C["dark"])        # upper jaw
        d.chord([.1 * S, .5 * S, .9 * S, .94 * S], 180, 360, fill=C["dark"])      # lower jaw
    for k in range(n):
        x = .5 + (k - (n - 1) / 2) * (.3 if n == 2 else .17)
        w = .08 if n == 2 else .06
        poly(d, [(x - w, .26), (x + w, .26), (x, .56)], C["hi"])
        poly(d, [(x - w * .8, .74), (x + w * .8, .74), (x, .52)], C["main"])
    if not jaw:
        ell(d, .5, .5, .44, .44, C["dark"]) if False else None


'''+s[b:]
# wing: bigger
s=s.replace('a = math.radians(-150 + k * 22); L = .38 - k * .03','a = math.radians(-160 + k * 24); L = .5 - k * .04')
s=s.replace('''        poly(d, [(.36, .64), (.36 + L * math.cos(a) + .02''','''        poly(d, [(.4, .7), (.4 + L * math.cos(a) + .02''')
s=s.replace('''.64 + L * math.sin(a)), (.36 + L * math.cos(a + .25), .64 + L * math.sin(a + .25))]''','''.7 + L * math.sin(a)), (.4 + L * math.cos(a + .3), .7 + L * math.sin(a + .3))]''')
# roots: thicker, 5
s=s.replace('for x0, a in ((.3, -1.9), (.5, -1.57), (.7, -1.25)):','for x0, a in ((.22, -2.1), (.36, -1.8), (.5, -1.57), (.64, -1.34), (.78, -1.05)):')
s=s.replace('''        line(d, pts, C["main"], .055)''','''        line(d, pts, C["main"], .075); line(d, pts[:6], C["hi"], .02)''')
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"Ice":       dict(bg_in=(205, 235, 248), bg_out=(70, 120, 165)','"Ice":       dict(bg_in=(150, 200, 230), bg_out=(40, 80, 130)')
t=t.replace('"Air":       dict(bg_in=(200, 232, 236), bg_out=(90, 140, 160)','"Air":       dict(bg_in=(150, 200, 215), bg_out=(50, 95, 125)')
t=t.replace('"Light":     dict(bg_in=(255, 245, 205), bg_out=(200, 150, 60)','"Light":     dict(bg_in=(255, 235, 170), bg_out=(160, 105, 40)')
t=t.replace('"blaze_bolt":       ("Fire", "lance", {},','"blaze_bolt":       ("Fire", "lance", {"w": .08},')
t=t.replace('"radiant_bolt":     ("Light", "lance", {"w": .07},','"radiant_bolt":     ("Light", "lance", {"w": .1, "flare": False},')
t=t.replace('"wind_lance":       ("Air", "lance", {"flare": False},','"wind_lance":       ("Air", "lance", {"w": .07, "flare": False},')
b.write_text(t)
