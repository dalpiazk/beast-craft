import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def fire_tornado(d, C, rng):
    """Firestorm reroll: one swirling funnel of flame, wide at the top, narrow at the ground."""
    ell(d, .5, .86, .16, .04, C["dark"])
    for k in range(9):
        t = k / 8; y = .16 + .66 * t; rx = .36 * (1 - t) + .05; x = .5 + .05 * math.sin(k * 1.3)
        ell(d, x, y, rx, .07 * (1 - t) + .03, C["dark"] if k % 2 else C["main"])
        ell(d, x - rx * .25, y - .01, rx * .55, .035 * (1 - t) + .015, C["hi"])
    for k in range(4):
        a = rng.uniform(0, 2 * math.pi); ell(d, .5 + .4 * math.cos(a), .45 + .36 * math.sin(a), .025, .025, C["acc"])


def fire_ring(d, C, rng, n=12):
    """Flame Wave reroll: a ring of flame tongues around a dark centre."""
    arc(d, .5, .52, .3, 0, 360, C["dark"], .1)
    for k in range(n):
        t = k * 2 * math.pi / n; x, y = .5 + .3 * math.cos(t), .52 + .3 * math.sin(t)
        for sc, c in ((1, C["main"]), (.55, C["hi"])):
            r = .06 * sc
            poly(d, [(x, y - r * 2.6), (x + r, y - r * .2), (x - r, y - r * .2)], c); ell(d, x, y, r, r, c)


def slab_ring(d, C, rng, n=9):
    """Granite Bulwark reroll: upright granite slabs rising in a ring (a stone wall) around a glowing centre."""
    ell(d, .5, .64, .4, .16, C["dark"]); ell(d, .5, .62, .26, .09, mix(C["hi"], (255, 240, 200), .5))
    slabs = []
    for k in range(n):
        t = k * 2 * math.pi / n + .2; x, y = .5 + .34 * math.cos(t), .62 + .13 * math.sin(t); slabs.append((y, x, t))
    for y, x, t in sorted(slabs):
        h = .2 + .08 * (y - .49) / .26; w = .07
        c = C["main"] if math.sin(t) > 0 else mix(C["main"], C["dark"], .35)
        poly(d, [(x - w, y), (x - w * .8, y - h), (x, y - h - .03), (x + w * .8, y - h), (x + w, y)], c)
        poly(d, [(x - w * .5, y - .02), (x - w * .45, y - h + .01), (x, y - h - .015)], C["hi"])
        ell(d, x - w * .2, y - h + .01, w * .6, .018, C["acc"])


def scales(d, C, rng):
    """Stoneskin reroll: overlapping stone armour scales (a hardened hide), not a wall or a shield."""
    for row in range(4):
        for k in range(4 - row % 2):
            x = .26 + k * .16 + (row % 2) * .08; y = .3 + row * .13
            if math.hypot(x - .5, y - .5) > .36:
                continue
            d.chord([(x - .1) * S, (y - .09) * S, (x + .1) * S, (y + .11) * S], 0, 180, fill=C["dark"])
            d.chord([(x - .085) * S, (y - .075) * S, (x + .085) * S, (y + .095) * S], 0, 180, fill=C["main"])
            ell(d, x - .03, y + .03, .03, .015, C["hi"])
    ell(d, .36, .22, .1, .04, C["acc"])


def shove(d, C, rng):
    """Tectonic Shove reroll: a rock shoulder-slamming forward with speed lines, the foe knocked away."""
    for y in (.36, .5, .64):
        line(d, [(.08, y), (.28, y)], C["hi"], .03)
    pts = [(.46 + .2 * math.cos(t) * rng.uniform(.9, 1.05), .5 + .22 * math.sin(t) * rng.uniform(.9, 1.05)) for t in np.linspace(0, 2 * math.pi, 10)[:-1]]
    poly(d, pts, C["main"]); poly(d, [(.36, .36), (.52, .3), (.6, .4), (.46, .44)], C["hi"])
    poly(d, [(.7, .4), (.84, .5), (.7, .6)], C["acc"]); ell(d, .9, .5, .05, .05, C["dark"])
    for a in (-.6, 0, .6):
        line(d, [(.68 + .06 * math.cos(a), .5 + .06 * math.sin(a)), (.68 + .14 * math.cos(a), .5 + .14 * math.sin(a))], C["hi"], .02)


def menhir_shout(d, C, rng):
    """Stone Challenge reroll: a tall mossy standing stone planted in the ground, sound rings bursting both ways."""
    ell(d, .5, .84, .22, .05, C["dark"])
    poly(d, [(.4, .84), (.38, .34), (.46, .18), (.56, .2), (.62, .36), (.6, .84)], C["main"])
    poly(d, [(.42, .8), (.41, .36), (.47, .22), (.5, .24), (.48, .8)], C["hi"]); ell(d, .5, .24, .1, .045, C["acc"])
    for sgn in (-1, 1):
        for k, r in enumerate((.16, .26, .36)):
            arc(d, .5, .46, r, (-40 if sgn > 0 else 140), (40 if sgn > 0 else 220), C["acc"] if k % 2 else C["hi"], .03)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
rep = {
 '"firestorm":        ("Fire", "rain", {}, "fireballs raining down, burning meteors")': '"firestorm":        ("Fire", "fire_tornado", {}, "one swirling fire tornado, a funnel of flame spinning")',
 '"flame_wave":       ("Fire", "wave", {"fire": True}, "a rolling wave of fire, flame crest")': '"flame_wave":       ("Fire", "fire_ring", {}, "a circle of flames, a burning fire ring on dark ground")',
 '"granite_bulwark":  ("Earth", "shield", {"emblem": "dot"}, "a heavy granite slab shield, stone blocks, protective")': '"granite_bulwark":  ("Earth", "slab_ring", {}, "a ring of upright granite slabs rising from the ground, circular stone wall, earthy grey and brown, soft protective glow")',
 '"stone_challenge":  ("Earth", "shout", {"source": "stone"}, "a mossy standing stone bellowing, bold shout sound waves")': '"stone_challenge":  ("Earth", "menhir_shout", {}, "a tall mossy standing stone with booming sound rings, earthy grey and brown")',
 '"tectonic_shove":   ("Earth", "charge", {}, "a thick stone arrow shoving forward, rock shoulder charge, dust")': '"tectonic_shove":   ("Earth", "shove", {}, "a grey boulder slamming forward with speed lines, pushing, dust burst, earthy brown")',
 '"stoneskin":        ("Earth", "plate", {"slash": False}, "a hexagonal stone hide plate, hardened grey stone, moss edge")': '"stoneskin":        ("Earth", "scales", {}, "overlapping grey stone armour scales, a hardened stone hide, moss")',
 '"predator_focus":   ("Dark", "eye", {"pupil": "slit"}': '"predator_focus":   ("Dark", "eye", {"pupil": "slit", "glare": True}',
 '"iron_will":       ("Avatar", "plate", {"slash": False}, "a steady golden emblem plate, calm, soft glow")': '"iron_will":       ("Avatar", "fist", {}, "a golden gauntlet fist held firm, steadfast resolve, soft glow")',
}
for a_, b_ in rep.items():
    assert a_ in t, a_; t = t.replace(a_, b_)
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('''EXTRA = {"radiant_bolt", "deep_shell"}                 # the style test's weak spots: 3 seeds


def seeds(i):
    base = [5000 + IDX[i], 6000 + IDX[i]]''','''EXTRA = {"radiant_bolt", "deep_shell"}                 # the style test's weak spots: 3 seeds
REROLL = ["firestorm", "flame_wave", "granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin"]   # producer / review rerolls
XNEG.update({k: ", rainbow, multicolored" for k in ("granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin")})


def seeds(i):
    if i in REROLL:
        return [8000 + IDX[i], 9000 + IDX[i], 10000 + IDX[i]]
    base = [5000 + IDX[i], 6000 + IDX[i]]''')
q.write_text(u)
