import pathlib
p=pathlib.Path('motifs.py'); s=p.read_text()
s=s.replace('\nPRIMS = {', '''

def spring_jet(d, C, rng):
    """Sacred Spring reroll 2 (producer): a round spring pool seen from slightly above, one central upward jet of
    glowing water splashing into droplets at the top. No arcs meeting at a peak."""
    ell(d, .5, .74, .38, .14, C["dark"]); ell(d, .5, .72, .32, .1, C["main"]); ell(d, .5, .71, .22, .06, C["hi"])
    for r in (.16, .26):
        arc(d, .5, .72, r, 0, 360, C["acc"], .01) if False else None
    poly(d, [(.46, .7), (.54, .7), (.52, .28), (.48, .28)], C["hi"])
    ell(d, .5, .26, .09, .07, C["hi"])
    for (x, y, r) in ((.36, .24, .035), (.64, .24, .035), (.42, .14, .03), (.58, .14, .03), (.3, .34, .028), (.7, .34, .028), (.5, .1, .03)):
        droplet(d, dict(C, main=C["hi"]), rng, cx=x, cy=y, r=r, sparkle=False)

PRIMS = {''',1)
p.write_text(s)
b=pathlib.Path('briefs.py'); t=b.read_text()
old='"sacred_spring":    ("LightD", "fountain", {}, "a fountain of golden light welling up from a spring pool, dark amber background")'
assert old in t
t=t.replace(old,'"sacred_spring":    ("LightD", "spring_jet", {}, "a round glowing spring pool with one central jet of golden water splashing into droplets, dark amber background")')
b.write_text(t)
q=pathlib.Path('batch.py'); u=q.read_text()
u=u.replace('REROLL_B = ["granite_bulwark", "thorn_lash", "sunder"]','REROLL_B = ["granite_bulwark", "thorn_lash", "sunder", "sacred_spring"]')
q.write_text(u)
