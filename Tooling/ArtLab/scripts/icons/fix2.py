import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('''def lance(d, C, rng, w=.055, ang=-45, flare=True):
    pts = [(.14, .5), (.5, .5 - w), (.88, .5), (.5, .5 + w)]''','''def lance(d, C, rng, w=.055, ang=-45, flare=True, glow=False):
    if glow:
        poly(d, rot([(.08, .5), (.5, .5 - w * 2.4), (.94, .5), (.5, .5 + w * 2.4)], math.radians(ang)), C["dark"])
    pts = [(.14, .5), (.5, .5 - w), (.88, .5), (.5, .5 + w)]''')
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('("Light", "lance", {"w": .1, "flare": False}','("Light", "lance", {"w": .1, "flare": False, "glow": True}')
t=t.replace('("Fire", "lance", {"w": .08}','("Fire", "lance", {"w": .08, "glow": True}')
b.write_text(t)
