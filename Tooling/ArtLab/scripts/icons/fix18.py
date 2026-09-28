import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('''def slab_ring(d, C, rng, n=9):''','''def slab_ring2(d, C, rng, n=7):
    """Granite Bulwark reroll 2: a stone circle of 7 tall pale granite slabs standing on a dark ground ring, glow inside."""
    ell(d, .5, .66, .44, .2, (50, 34, 22)); ell(d, .5, .64, .24, .09, (255, 226, 150))
    slabs = []
    for k in range(n):
        t = k * 2 * math.pi / n + .25; slabs.append((.64 + .15 * math.sin(t), .5 + .34 * math.cos(t), t))
    for y, x, t in sorted(slabs):
        h = .3 + .06 * (y - .49) / .3; w = .075
        poly(d, [(x - w, y), (x - w * .9, y - h), (x + w * .9, y - h - .02), (x + w, y)], (205, 198, 182) if math.sin(t) > 0 else (150, 142, 128))
        poly(d, [(x - w, y), (x - w * .9, y - h), (x - w * .3, y - h - .01), (x - w * .4, y)], (235, 230, 215))
        ell(d, x, y - h - .005, w * .8, .02, C["acc"])


def slab_ring(d, C, rng, n=9):''')
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('"granite_bulwark":  ("Earth", "slab_ring", {}, "a ring of upright granite slabs rising from the ground, circular stone wall, earthy grey and brown, soft protective glow")',
            '"granite_bulwark":  ("Earth", "slab_ring2", {}, "a stone circle of tall upright granite slabs standing around a glowing centre, protective ring of standing stones")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('''def seeds(i):
    if i in REROLL:''','''REROLL_B = ["granite_bulwark"]                          # second reroll (no seed of the first showed the slab ring)


def seeds(i):
    if i in REROLL_B:
        return [11000 + IDX[i], 12000 + IDX[i], 13000 + IDX[i]]
    if i in REROLL:''')
u=u.replace('XNEG.update({k: ", rainbow, multicolored"','XNEG["granite_bulwark"] = ", face, eyes, golem, altar, pedestal, rainbow"\nXNEG.update({k: ", rainbow, multicolored"')
u=u.replace('"stone_challenge", "tectonic_shove", "stoneskin", "sunder", "shrapnel_burst")})','"stone_challenge", "tectonic_shove", "stoneskin", "sunder", "shrapnel_burst") if k not in XNEG})')
q.write_text(u)
