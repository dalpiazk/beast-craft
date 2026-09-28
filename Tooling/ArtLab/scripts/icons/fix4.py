import pathlib, json
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def ripple_pull(d, C, rng):
    """Undertow reroll: whirlpool rings with curling currents pulling INWARD to a dark centre."""
    for k, r in enumerate((.42, .31, .2)):
        arc(d, .5, .5, r, 20 + k * 40, 300 + k * 40, C["main"] if k % 2 == 0 else C["hi"], .045)
    ell(d, .5, .5, .09, .09, C["dark"])
    for a in (45, 165, 285):
        t = math.radians(a); x, y = .5 + .36 * math.cos(t), .5 + .36 * math.sin(t)
        tip = (.5 + .2 * math.cos(t), .5 + .2 * math.sin(t)); n = (-math.sin(t) * .06, math.cos(t) * .06)
        poly(d, [tip, (x + n[0], y + n[1]), (x - n[0], y - n[1])], C["acc"])


def jaws(d, C, rng):
    """Serpent Bite reroll: a sea serpent's open jaws snapping shut, curved fangs top and bottom."""
    d.pieslice([.08 * S, .1 * S, .92 * S, .9 * S], 200, 340, fill=C["dark"]); d.pieslice([.08 * S, .1 * S, .92 * S, .9 * S], 20, 160, fill=C["dark"])
    d.pieslice([.14 * S, .16 * S, .86 * S, .84 * S], 205, 335, fill=C["main"]); d.pieslice([.14 * S, .16 * S, .86 * S, .84 * S], 25, 155, fill=C["main"])
    for k in range(5):
        x = .3 + k * .1
        poly(d, [(x - .035, .3), (x + .035, .3), (x, .46)], C["hi"]); poly(d, [(x - .03, .7), (x + .03, .7), (x + .02, .56)], C["hi"])
    for (x, y) in ((.14, .5), (.86, .5)):
        ell(d, x, y, .04, .04, C["hi"])

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"serpent_bite":     ("Water", "fangs", {}, "two big curved sea serpent fangs biting, water splash")','"serpent_bite":     ("Water", "jaws", {}, "open sea serpent jaws snapping shut, curved fangs, water splash")')
t=t.replace('"undertow":         ("Water", "vortex", {"arms": 2}, "a swirling whirlpool undertow pulling inward, deep teal water")','"undertow":         ("Water", "ripple_pull", {}, "a whirlpool of rippling rings with currents pulling inward to a dark centre")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"tectonic_shove", "stoneskin"]   # producer','"tectonic_shove", "stoneskin", "serpent_bite", "undertow"]   # producer')
q.write_text(u)
o=pathlib.Path('../work/overrides.json'); ov=json.loads(o.read_text()); ov.pop("tectonic_shove", None); ov["tidal_renewal"]=6011; o.write_text(json.dumps(ov))
