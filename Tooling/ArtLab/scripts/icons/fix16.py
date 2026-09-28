import pathlib, json
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def club(d, C, rng):
    """Brute Smash reroll: a heavy mossy wooden club slamming down onto cracked ground."""
    poly(d, [(.12, .8), (.88, .8), (.9, .9), (.1, .9)], C["dark"])
    for a in (-.3, 0, .3):
        line(d, [(.56, .8), (.56 + a, .92)], mix(C["dark"], (0, 0, 0), .5), .014)
    line(d, [(.2, .16), (.44, .5)], (140, 100, 70), .07)
    pts = rot([(.36, .5), (.44, .4), (.74, .56), (.76, .74), (.6, .78), (.38, .62)], 0)
    poly(d, pts, (120, 88, 60)); ell(d, .6, .56, .08, .05, C["acc"] if False else (110, 150, 80))
    for a in np.linspace(-2.6, -.5, 4):
        line(d, [(.62 + .26 * math.cos(a), .76 + .2 * math.sin(a)), (.62 + .36 * math.cos(a), .76 + .28 * math.sin(a))], C["hi"], .025)


def cleave_arc(d, C, rng):
    """Great Cleave reroll: one wide bright crescent sweep (a blade arc) across the icon."""
    d.pieslice([.08 * S, .1 * S, .92 * S, .94 * S], 190, 350, fill=C["hi"])
    d.pieslice([.16 * S, .26 * S, .84 * S, 1.0 * S], 185, 355, fill=C["bg_out"] if "bg_out" in C else C["dark"])
    for k in range(3):
        arc(d, .5, .52, .44 - k * .05, 200, 340, C["acc"] if k == 1 else C["main"], .012)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('DARKV = dict(', 'GLOAMD = dict(PAL["Gloam"], main=(120, 108, 142), hi=(236, 228, 250), dark=(40, 32, 58))   # darker stone-violet motif for the enemy rerolls\nDARKV = dict(',1)
t=t.replace('"AvatarD": AVATARD}','"AvatarD": AVATARD, "GloamD": GLOAMD}')
t=t.replace('("giant", "crush"):         ("fist", {}, "a huge mossy stone fist crushing down")','("giant", "crush"):         ("fist", {}, "a huge grey stone fist punching down, knuckles, impact lines")')
t=t.replace('("brute", "smash"):         ("boulder", {"moss": True, "cracks": True}, "a heavy mossy club smash, impact")','("brute", "smash"):         ("club", {}, "a heavy mossy wooden club smashing down on cracked ground")')
t=t.replace('("champion", "cleave"):     ("crescent", {}, "a great sweeping crescent cleave slash")','("champion", "cleave"):     ("cleave_arc", {}, "one wide bright crescent blade sweep, a great cleaving slash")')
t=t.replace('''            out.append(dict(id=f"{eid}_{s['SkillId']}", name=s["DisplayName"], artkey=s["ArtKey"], group="Enemy", pal="Gloam", prim=prim,''','''            pal = "GloamD" if (eid, s["SkillId"]) in (("giant", "crush"), ("brute", "smash"), ("champion", "cleave")) else "Gloam"
            out.append(dict(id=f"{eid}_{s['SkillId']}", name=s["DisplayName"], artkey=s["ArtKey"], group="Enemy", pal=pal, prim=prim,''')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"vengeance", "withering_curse"]   # producer','"vengeance", "withering_curse", "giant_crush", "brute_smash", "champion_cleave"]   # producer')
q.write_text(u)
o=pathlib.Path('../work/overrides.json'); ov=json.loads(o.read_text()); ov.update(shaman_staff=6087, shaman_storm=5088); o.write_text(json.dumps(ov))
