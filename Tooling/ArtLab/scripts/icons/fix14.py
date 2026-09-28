import pathlib, json, re
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def wilted(d, C, rng):
    """Withering Curse reroll: a drooping wilted flower wrapped in violet curse wisps (enemies weakened)."""
    line(d, [(.5, .88), (.5, .56), (.6, .4), (.7, .4)], C["dark"], .035)
    for k in range(5):
        a = math.radians(40 + k * 22); ell(d, .7 + .1 * math.cos(a), .44 + .12 * math.sin(a), .06, .08, C["main"])
    for k in range(2):
        pts = [(.5 + (.32 + .08 * k) * math.cos(t), .52 + (.32 + .08 * k) * math.sin(t) * .9) for t in np.linspace(k * 2.4, k * 2.4 + 3.8, 20)]
        line(d, pts, C["acc"], .045)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
t=t.replace('METALD = dict(', 'AVATARD = dict(PAL["Avatar"], bg_in=(160, 105, 45), bg_out=(45, 28, 16), main=(235, 180, 80))   # darker ground for the avatar rerolls\nMETALD = dict(',1)
t=t.replace('"LightD": LIGHTD}','"LightD": LIGHTD, "AvatarD": AVATARD}')
new = {
 "rallying_cry": ('AVATAR', "AvatarD", '"shout", {"source": "horn"}', "a golden war horn blowing a rallying call, bold sound waves, dark amber background"),
 "mending_light": ('AVATAR', "AvatarD", '"droplet", {}', "a warm drop of healing golden light with sparkles, dark amber background"),
 "battle_focus": ('AVATAR', "AvatarD", '"reticle", {}', "a bold golden targeting reticle crosshair, dark amber background"),
 "battle_hymn": ('PASSIVE', "AvatarD", '"notes", {}', "two golden music notes of a battle hymn, soft glow, dark amber background"),
 "vengeance": ('PASSIVE', "AvatarD", '"heart", {"flame_top": True}', "a golden heart with a resolute flame rising from it, soft glow, dark background"),
 "withering_curse": ('PASSIVE', "AvatarHex", '"wilted", {}', "a wilted drooping flower wrapped in violet curse wisps, soft glow"),
}
for k, (_, pal, pk, words) in new.items():
    t, n = re.subn(r'"%s":\s+\("[A-Za-z]+", "[a-z_0-9]+", \{[^}]*\}, "[^"]*"\)' % k, '"%s": ("%s", %s, "%s")' % (k, pal, pk, words), t); assert n == 1, k
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('"eclipse_fang", "predator_focus"]   # producer','"eclipse_fang", "predator_focus", "rallying_cry", "mending_light", "battle_focus", "battle_hymn", "vengeance", "withering_curse"]   # producer')
q.write_text(u)
o=pathlib.Path('../work/overrides.json'); ov=json.loads(o.read_text()); ov["verdant_pulse"]=5074; o.write_text(json.dumps(ov))
