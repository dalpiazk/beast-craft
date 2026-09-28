import pathlib, re
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def mend_leaf(d, C, rng):
    """Mending Light reroll 2 (producer): a pale-green mending leaf wrapped by a cream bandage strip, soft cool-white
    glow and small four-point sparkles. No orange, nothing flame-shaped."""
    ell(d, .5, .5, .36, .36, (120, 150, 110))
    pts = [(.5 + .26 * math.sin(t), .5 - .36 * math.cos(t)) for t in np.linspace(0, math.pi, 13)]
    pts += [(1 - x, y) for x, y in reversed(pts[1:-1])]
    poly(d, rot(pts, math.radians(35)), (170, 225, 160))
    line(d, rot([(.5, .86), (.5, .18)], math.radians(35)), (225, 245, 215), .02)
    poly(d, rot([(.3, .46), (.7, .46), (.7, .56), (.3, .56)], math.radians(-20)), (245, 236, 214))
    for x in (.4, .5, .6):
        line(d, rot([(x, .47), (x, .55)], math.radians(-20)), (210, 196, 170), .008)
    for (x, y, r) in ((.8, .24, .06), (.2, .74, .05), (.76, .78, .04)):
        star4(d, x, y, r, (240, 250, 245))


def wilted_big(d, C, rng):
    """Withering Curse reroll 2 (producer): a LARGE drooping flower, bent stem, falling petals; thin violet wisps only."""
    pts = [(.5 + .35 * math.cos(t), .5 + .38 * math.sin(t) * .9) for t in np.linspace(2.4, 5.6, 16)]
    line(d, pts, C["acc"], .018)
    line(d, [(.34, .92), (.34, .56), (.4, .34), (.54, .26), (.62, .32)], (110, 96, 70), .05)       # bent stem
    poly(d, [(.34, .7), (.18, .62), (.24, .76)], (120, 110, 76))                                         # droopy leaf
    for k in range(6):                                                                                   # hanging bloom
        a = math.radians(20 + k * 28)
        ell(d, .64 + .14 * math.cos(a), .46 + .17 * math.sin(a), .085, .12, (206, 170, 196))
    ell(d, .64, .42, .07, .06, (120, 80, 110))
    for (x, y, r) in ((.52, .76, .04), (.74, .84, .035), (.84, .66, .03)):                             # falling petals
        ell(d, x, y, r, r * .6, (206, 170, 196))

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
for k, pal, pk, words in (("mending_light", "AvatarD", '"mend_leaf", {}', "a pale green healing leaf wrapped in a cream bandage, soft cool white glow, sparkles, dark amber background"),
                          ("withering_curse", "AvatarHex", '"wilted_big", {}', "one large wilted drooping flower with a bent stem and falling petals, thin violet wisps")):
    t, n = re.subn(r'"%s":\s+\("[A-Za-z]+", "[a-z_0-9]+", \{[^}]*\}, "[^"]*"\)' % k, '"%s": ("%s", %s, "%s")' % (k, pal, pk, words), t); assert n == 1, k
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('REROLL_B = ["granite_bulwark", "thorn_lash", "sunder", "sacred_spring"]','REROLL_B = ["granite_bulwark", "thorn_lash", "sunder", "sacred_spring", "mending_light", "withering_curse"]')
u=u.replace('XNEG["granite_bulwark"] =','XNEG["mending_light"] = ", fire, flame, orange, hands"\nXNEG["granite_bulwark"] =')
q.write_text(u)
