import pathlib, json, re
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def eclipse_bite(d, C, rng):
    """Eclipse Fang reroll: a dark eclipse disc with a bright rim, four sharp shadow fangs biting down across it."""
    ell(d, .5, .46, .34, .34, C["acc"]); ell(d, .54, .44, .3, .3, C["dark"])
    for k in range(4):
        x = .26 + k * .16
        poly(d, [(x - .06, .5), (x + .06, .5), (x, .86)], C["hi"])
        poly(d, [(x - .035, .52), (x + .005, .52), (x - .005, .78)], C["main"])


def focus_eye(d, C, rng):
    """Predator Focus reroll: a narrowed slit eye locked inside a targeting reticle (a self crit buff)."""
    arc(d, .5, .5, .4, 0, 360, C["acc"], .035)
    for a in (0, 90, 180, 270):
        t = math.radians(a); line(d, [(.5 + .32 * math.cos(t), .5 + .32 * math.sin(t)), (.5 + .46 * math.cos(t), .5 + .46 * math.sin(t))], C["acc"], .035)
    pts = [(.5 + .28 * math.cos(t), .5 + .1 * math.sin(t) * abs(math.sin(t)) ** .2) for t in np.linspace(0, 2 * math.pi, 40)]
    poly(d, pts, C["hi"]); ell(d, .5, .5, .1, .1, C["main"]); poly(d, [(.5, .4), (.52, .5), (.5, .6), (.48, .5)], C["dark"])

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
for k, (pal, pk, words) in {"eclipse_fang": ("DarkC", '"eclipse_bite", {}', "four sharp shadow fangs biting across a dark eclipse with a glowing rim"),
                            "predator_focus": ("Dark", '"focus_eye", {}', "a narrowed golden slit eye locked inside a targeting reticle")}.items():
    t, n = re.subn(r'"%s":\s+\("%s", "[a-z_0-9]+", \{[^}]*\}, "[^"]*"\)' % (k, pal), '"%s": ("%s", %s, "%s")' % (k, pal, pk, words), t); assert n == 1, k
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"radiant_bolt", "sacred_spring"]   # producer','"radiant_bolt", "sacred_spring", "eclipse_fang", "predator_focus"]   # producer')
q.write_text(u)
o=pathlib.Path('../work/overrides.json'); ov=json.loads(o.read_text()); ov["petrifying_gaze"]=5056; o.write_text(json.dumps(ov))
