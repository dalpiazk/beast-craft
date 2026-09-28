# Skill-icon batch (2026-09-27): parametric colour-mass motif primitives (the img2img inits), one clear shape each.
"""Unit coordinates (0..1) on a 1024 canvas; the motif stays inside the inscribed circle (r <= ~0.44)."""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024


def P(x, y):
    return (x * S, y * S)


def poly(d, pts, c):
    d.polygon([P(*p) for p in pts], fill=c)


def ell(d, cx, cy, rx, ry, c):
    d.ellipse([(cx - rx) * S, (cy - ry) * S, (cx + rx) * S, (cy + ry) * S], fill=c)


def line(d, pts, c, w):
    d.line([P(*p) for p in pts], fill=c, width=int(w * S), joint="curve")
    for p in (pts[0], pts[-1]):
        ell(d, p[0], p[1], w / 2, w / 2, c)


def arc(d, cx, cy, r, a0, a1, c, w):
    d.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], a0, a1, fill=c, width=int(w * S))


def rot(pts, ang, cx=.5, cy=.5):
    ca, sa = math.cos(ang), math.sin(ang)
    return [(cx + (x - cx) * ca - (y - cy) * sa, cy + (x - cx) * sa + (y - cy) * ca) for x, y in pts]


def mix(a, b, t):
    return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))


# ---------------- primitives: f(d, C, rng, **kw); C = palette dict(main, hi, dark, acc) ----------------
def comet(d, C, rng, head=.13, ang=-45, tail=.4):
    a = math.radians(ang); hx, hy = .5 + .12 * math.cos(a), .5 + .12 * math.sin(a)
    tx, ty = .5 - tail * math.cos(a), .5 - tail * math.sin(a)
    for k, (c, f) in enumerate([(C["dark"], 1.3), (C["main"], 1.0), (C["hi"], .55)]):
        w = head * f; n = (-math.sin(a) * w * .8, math.cos(a) * w * .8)
        poly(d, [(tx, ty), (hx + n[0], hy + n[1]), (hx - n[0], hy - n[1])], c)
        ell(d, hx, hy, w, w, c)


def lance(d, C, rng, w=.055, ang=-45, flare=True, glow=False):
    if glow:
        poly(d, rot([(.08, .5), (.5, .5 - w * 2.4), (.94, .5), (.5, .5 + w * 2.4)], math.radians(ang)), C["dark"])
    pts = [(.14, .5), (.5, .5 - w), (.88, .5), (.5, .5 + w)]
    poly(d, rot(pts, math.radians(ang)), C["main"])
    poly(d, rot([(.2, .5), (.5, .5 - w * .45), (.84, .5), (.5, .5 + w * .45)], math.radians(ang)), C["hi"])
    if flare:
        x, y = rot([(.86, .5)], math.radians(ang))[0]
        for k in range(4):
            t = k * math.pi / 2 + math.pi / 4
            poly(d, [(x + .1 * math.cos(t), y + .1 * math.sin(t)), (x + .02 * math.cos(t + 1.6), y + .02 * math.sin(t + 1.6)),
                     (x + .02 * math.cos(t - 1.6), y + .02 * math.sin(t - 1.6))], C["hi"])


def droplet(d, C, rng, cx=.5, cy=.54, r=.24, sparkle=True):
    poly(d, [(cx, cy - r * 1.75), (cx + r * .75, cy - r * .5), (cx - r * .75, cy - r * .5)], C["main"])
    ell(d, cx, cy, r, r, C["main"])
    ell(d, cx - r * .35, cy - r * .2, r * .22, r * .32, C["hi"])
    if sparkle:
        for (x, y) in ((cx + r * 1.3, cy - r * 1.2), (cx - r * 1.4, cy + r * .6)):
            star4(d, x, y, .05, C["hi"])


def star4(d, x, y, r, c):
    poly(d, [(x, y - r), (x + r * .25, y - r * .25), (x + r, y), (x + r * .25, y + r * .25), (x, y + r),
             (x - r * .25, y + r * .25), (x - r, y), (x - r * .25, y - r * .25)], c)


def boulder(d, C, rng, moss=True, cracks=True):
    if cracks:
        poly(d, [(.2, .8), (.8, .8), (.88, .9), (.12, .9)], C["dark"])
        for a in (-.4, -.15, .15, .4):
            line(d, [(.5, .8), (.5 + a, .93)], mix(C["dark"], (0, 0, 0), .4), .014)
    pts = [(.5 + .3 * math.cos(t) * rng.uniform(.85, 1.05), .45 + .27 * math.sin(t) * rng.uniform(.85, 1.05))
           for t in np.linspace(0, 2 * math.pi, 11)[:-1]]
    poly(d, pts, C["main"]); poly(d, [(.3, .3), (.62, .22), (.72, .36), (.45, .4)], C["hi"])
    if moss:
        ell(d, .44, .24, .14, .06, C["acc"])


def fist(d, C, rng):
    poly(d, [(.3, .72), (.7, .72), (.66, .95), (.34, .95)], C["dark"])              # wrist
    d.rounded_rectangle([.26 * S, .34 * S, .74 * S, .76 * S], radius=int(.08 * S), fill=C["main"])
    for k in range(4):
        ell(d, .33 + k * .113, .34, .065, .06, C["hi"])
    d.rounded_rectangle([.2 * S, .5 * S, .42 * S, .64 * S], radius=int(.05 * S), fill=C["hi"])   # thumb
    for a in (-2.4, -1.57, -.7):
        line(d, [(.5 + .34 * math.cos(a), .45 + .34 * math.sin(a)), (.5 + .44 * math.cos(a), .45 + .44 * math.sin(a))], C["acc"], .03)


def impact(d, C, rng, n=10, ground=True, ring=False):
    cx, cy = .5, (.56 if ground else .5)
    pts = []
    for k in range(2 * n):
        t = k * math.pi / n; r = .38 if k % 2 == 0 else .17
        pts.append((cx + r * math.cos(t) * rng.uniform(.85, 1.05), cy + r * math.sin(t) * rng.uniform(.85, 1.05)))
    poly(d, pts, C["main"]); ell(d, cx, cy, .12, .12, C["hi"])
    if ground:
        poly(d, [(.12, .78), (.88, .78), (.9, .9), (.1, .9)], C["dark"])
        for a in (-.3, 0, .3):
            line(d, [(.5, .78), (.5 + a, .9)], mix(C["dark"], (0, 0, 0), .5), .014)
    if ring:
        arc(d, cx, cy, .42, 0, 360, C["hi"], .025)


def shield(d, C, rng, spikes=False, cracked=False, glow=True, emblem="dot"):
    if glow:
        ell(d, .5, .52, .42, .42, mix(C["hi"], C["main"], .3))
    if spikes:
        for k in range(9):
            t = -math.pi / 2 + (k - 4) * .38
            poly(d, [(.5 + .44 * math.cos(t), .5 + .44 * math.sin(t)), (.5 + .28 * math.cos(t + .15), .5 + .28 * math.sin(t + .15)),
                     (.5 + .28 * math.cos(t - .15), .5 + .28 * math.sin(t - .15))], C["dark"])
    outer = [(.5, .16), (.77, .27), (.75, .56), (.5, .86), (.25, .56), (.23, .27)]
    inner = [(.5, .24), (.69, .32), (.67, .55), (.5, .76), (.33, .55), (.31, .32)]
    poly(d, outer, C["hi"]); poly(d, inner, C["main"])
    if emblem == "dot":
        ell(d, .5, .46, .06, .06, C["hi"])
    elif emblem == "leaf":
        poly(d, [(.5, .32), (.58, .46), (.5, .64), (.42, .46)], C["acc"])
    elif emblem == "flake":
        for k in range(3):
            t = k * math.pi / 3; line(d, [(.5 - .1 * math.cos(t), .48 - .1 * math.sin(t)), (.5 + .1 * math.cos(t), .48 + .1 * math.sin(t))], C["hi"], .02)
    if cracked:
        line(d, [(.5, .2), (.46, .38), (.55, .5), (.47, .66), (.52, .82)], C["dark"], .022)


def dome(d, C, rng):
    ell(d, .5, .72, .4, .08, C["dark"])
    d.pieslice([.14 * S, .24 * S, .86 * S, 1.2 * S], 180, 360, fill=C["hi"])
    d.pieslice([.2 * S, .3 * S, .8 * S, 1.14 * S], 180, 360, fill=C["main"])
    for x in (.36, .5, .64):
        ell(d, x, .64, .045, .045, C["acc"])


def halo(d, C, rng, dots=0):
    ell(d, .5, .5, .1, .1, C["hi"])
    for w, c in ((.08, C["main"]), (.035, C["hi"])):
        arc(d, .5, .5, .3, 0, 360, c, w)
    for k in range(dots):
        t = k * 2 * math.pi / dots; ell(d, .5 + .3 * math.cos(t), .5 + .3 * math.sin(t), .045, .045, C["acc"])


def eye(d, C, rng, pupil="slit", cracks=False, glare=False):
    pts = [(.5 + .42 * math.cos(t), .5 + .24 * math.sin(t) * abs(math.sin(t)) ** .2) for t in np.linspace(0, 2 * math.pi, 40)]
    poly(d, pts, C["hi"]); ell(d, .5, .5, .19, .19, C["main"]); ell(d, .5, .5, .13, .13, C["acc"])
    if pupil == "slit":
        poly(d, [(.5, .3), (.54, .5), (.5, .7), (.46, .5)], C["dark"])
    else:
        ell(d, .5, .5, .07, .07, C["dark"])
    ell(d, .45, .44, .03, .03, (255, 255, 250))
    if cracks:
        for a in (.9, 2.2, 4.0, 5.3):
            line(d, [(.5 + .44 * math.cos(a), .5 + .44 * math.sin(a)), (.5 + .34 * math.cos(a + .2), .5 + .34 * math.sin(a + .2))], C["dark"], .012)
    if glare:
        for a in np.linspace(0, 2 * math.pi, 9)[:-1]:
            line(d, [(.5 + .3 * math.cos(a), .5 + .3 * math.sin(a) * .6), (.5 + .44 * math.cos(a), .5 + .44 * math.sin(a) * .7)], C["acc"], .02)


def claws(d, C, rng, n=3, ang=-40, bolts=False):
    for k in range(n):
        o = (k - (n - 1) / 2) * .15
        pts = [(.22 + o * .7, .8 + o * .6), (.5 + o, .5 + o * .2 - .02), (.8 + o * .5, .2 + o * .7)]
        if bolts:
            pts = [(.24 + o, .82), (.44 + o, .58), (.36 + o, .54), (.6 + o, .2)]
        line(d, pts, C["main"], .07 if not bolts else .05)
        line(d, pts, C["hi"], .025)


def fangs(d, C, rng, n=2, jaw=True):
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


def wave(d, C, rng, fire=False):
    poly(d, [(.08, .86), (.92, .86), (.92, .62), (.7, .5), (.52, .56), (.3, .7), (.1, .72)], C["dark"])
    d.pieslice([.34 * S, .18 * S, .9 * S, .74 * S], 150, 400, fill=C["main"])
    d.pieslice([.46 * S, .3 * S, .78 * S, .62 * S], 150, 400, fill=C["hi"])
    ell(d, .62, .46, .08, .08, C["dark"] if not fire else C["hi"])
    for k in range(5):
        ell(d, .38 + k * .1, .2 + (k % 2) * .03, .03, .03, C["hi"])


def vortex(d, C, rng, arms=3):
    for a in range(arms):
        pts = []
        for k in range(40):
            t = k / 39; r = .06 + .36 * t; th = a * 2 * math.pi / arms + t * 4.2
            pts.append((.5 + r * math.cos(th), .5 + r * math.sin(th)))
        line(d, pts, C["main"], .075); line(d, pts, C["hi"], .03)
    ell(d, .5, .5, .08, .08, C["dark"])


def bolt(d, C, rng, branches=0, scale=1.0, cx=.5):
    pts = [(.3, .1), (.52, .1), (.44, .38), (.6, .38), (.34, .9), (.4, .52), (.26, .52)]
    pts = [(cx + (x - .43) * scale, .5 + (y - .5) * scale) for x, y in pts]
    poly(d, pts, C["main"])
    if branches:
        for (x0, y0, x1, y1) in ((.5, .45, .82, .3), (.45, .62, .8, .72))[:branches]:
            line(d, [(x0, y0), ((x0 + x1) / 2, (y0 + y1) / 2 + .05), (x1, y1)], C["hi"], .03); ell(d, x1, y1, .045, .045, C["hi"])


def orbs_line(d, C, rng, n=4):
    for k in range(n):
        t = k / (n - 1); x, y = .2 + .6 * t, .8 - .6 * t; r = .07 + .02 * t
        ell(d, x, y, r * 1.5, r * 1.5, C["main"]); ell(d, x, y, r, r, C["hi"])
    line(d, [(.16, .84), (.84, .16)], C["hi"], .012)


def asterisk(d, C, rng, n=6, core=True, puffy=False):
    for k in range(n):
        t = k * 2 * math.pi / n - math.pi / 2
        if puffy:
            for j in range(3):
                r = .14 + .1 * j; ell(d, .5 + r * math.cos(t), .5 + r * math.sin(t), .07 - .012 * j, .07 - .012 * j, C["main"])
        else:
            poly(d, [(.5 + .44 * math.cos(t), .5 + .44 * math.sin(t)), (.5 + .06 * math.cos(t + 1.3), .5 + .06 * math.sin(t + 1.3)),
                     (.5 + .06 * math.cos(t - 1.3), .5 + .06 * math.sin(t - 1.3))], C["main"])
    if core:
        ell(d, .5, .5, .11, .11, C["hi"])


def crystal(d, C, rng):
    poly(d, [(.5, .08), (.64, .42), (.5, .92), (.36, .42)], C["hi"]); poly(d, [(.5, .08), (.64, .42), (.5, .92)], C["main"])
    for (x, y) in ((.25, .28), (.76, .66), (.27, .72)):
        star4(d, x, y, .05, C["hi"])


def snowflake(d, C, rng):
    for k in range(6):
        t = k * math.pi / 3
        line(d, [(.5, .5), (.5 + .4 * math.cos(t), .5 + .4 * math.sin(t))], C["main"], .05)
        for f in (.2, .3):
            bx, by = .5 + f * math.cos(t), .5 + f * math.sin(t)
            for s in (.6, -.6):
                line(d, [(bx, by), (bx + .1 * math.cos(t + s), by + .1 * math.sin(t + s))], C["hi"], .03)
    ell(d, .5, .5, .07, .07, C["hi"])


def iceblock(d, C, rng):
    h = [(.5 + .36 * math.cos(math.radians(a)), .52 + .36 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
    poly(d, [h[0], h[1], (.5, .52), h[5]], C["hi"]); poly(d, [h[1], h[2], h[3], (.5, .52)], C["main"])
    poly(d, [h[3], h[4], h[5], (.5, .52)], C["dark"])
    for (x, y) in ((.8, .2), (.2, .82)):
        star4(d, x, y, .05, C["hi"])


def breath(d, C, rng):
    poly(d, [(.12, .5), (.86, .16), (.9, .84)], C["main"])
    poly(d, [(.12, .5), (.8, .3), (.82, .7)], C["hi"])
    for k in range(3):
        arc(d, .62 + k * .08, .5, .12, 250, 470, C["dark"], .018)


def flame(d, C, rng, cx=.5, cy=.58, s=1.0, feather=False):
    for sc, c in ((1.0, C["dark"]), (.75, C["main"]), (.45, C["hi"])):
        r = .24 * s * sc; y0 = cy + .08 * s * (1 - sc)
        poly(d, [(cx, y0 - r * 2.3), (cx + r * .95, y0 - r * .2), (cx - r * .95, y0 - r * .2)], c)
        ell(d, cx, y0, r, r, c)
        if sc == 1.0:
            for sgn in (-1, 1):
                poly(d, [(cx + sgn * r * .7, y0 - r * .6), (cx + sgn * r * 1.25, y0 - r * 1.7), (cx + sgn * r * .95, y0 + r * .1)], c)
    if feather:
        line(d, [(cx - .28 * s, cy + .34 * s), (cx + .02, cy - .02)], C["hi"], .025)


def rain(d, C, rng, n=5):
    for k in range(n):
        x, y = .22 + .14 * k + rng.uniform(-.03, .03), .3 + (k % 2) * .25 + rng.uniform(-.05, .05)
        poly(d, [(x - .12, y - .22), (x - .04, y - .02), (x + .03, y - .06)], C["main"]); ell(d, x, y, .06, .06, C["hi"])


def cloud(d, C, rng, dots=True, rainy=False, dark=False):
    c = C["dark"] if dark else C["main"]
    for (x, y, r) in ((.34, .52, .16), (.5, .42, .2), (.66, .52, .16), (.5, .58, .18)):
        ell(d, x, y, r, r, c)
    ell(d, .46, .4, .08, .06, C["hi"])
    if dots:
        for k in range(10):
            t = rng.uniform(0, 2 * math.pi); r = rng.uniform(.36, .44); ell(d, .5 + r * math.cos(t), .5 + r * math.sin(t), .025, .025, C["acc"])
    if rainy:
        for x in (.36, .5, .64):
            line(d, [(x, .74), (x - .05, .88)], C["hi"], .025)


def leaf(d, C, rng, sprout=False, drop=False):
    if sprout:
        line(d, [(.5, .88), (.5, .5)], C["dark"], .04)
        for s in (-1, 1):
            poly(d, rot([(.5, .5), (.62, .36), (.8, .36), (.66, .52)], 0) if s > 0 else [(.5, .5), (.38, .36), (.2, .36), (.34, .52)], C["main"])
    else:
        pts = [(.5 + .3 * math.sin(t) * (1 if k < 13 else 1), .5 - .38 * math.cos(t)) for k, t in enumerate(np.linspace(0, math.pi, 13))]
        pts += [(1 - x, y) for x, y in reversed(pts[1:-1])]
        poly(d, rot(pts, math.radians(35)), C["main"])
        line(d, rot([(.5, .88), (.5, .16)], math.radians(35)), C["hi"], .02)
    if drop:
        droplet(d, dict(C, main=C["acc"]), rng, cx=.72, cy=.72, r=.08, sparkle=False)


def flower(d, C, rng, n=5):
    for k in range(n):
        t = k * 2 * math.pi / n - math.pi / 2
        ell(d, .5 + .2 * math.cos(t), .5 + .2 * math.sin(t), .17, .17, C["acc"])
    ell(d, .5, .5, .12, .12, C["hi"])
    for t in (.6, 2.5):
        poly(d, rot([(.5, .5), (.62, .8), (.5, .95), (.42, .8)], t), C["main"])


def vine(d, C, rng):
    pts = [(.2 + .6 * t, .82 - .64 * t + .12 * math.sin(t * 7)) for t in np.linspace(0, 1, 24)]
    line(d, pts, C["main"], .06)
    for k in range(3, 22, 4):
        x, y = pts[k]; poly(d, [(x, y - .02), (x + .06, y - .08), (x + .02, y)], C["acc"]); poly(d, [(x, y + .02), (x - .06, y + .08), (x - .02, y)], C["acc"])
    ell(d, .8, .18, .05, .05, C["hi"])


def roots(d, C, rng):
    poly(d, [(.1, .76), (.9, .76), (.9, .9), (.1, .9)], C["dark"])
    for x0, a in ((.22, -2.1), (.36, -1.8), (.5, -1.57), (.64, -1.34), (.78, -1.05)):
        pts = []; x, y = x0, .8
        for k in range(10):
            pts.append((x, y)); x += .05 * math.cos(a); y += .05 * math.sin(a); a += (.25 if x0 < .5 else -.25 if x0 > .5 else .08)
        line(d, pts, C["main"], .075); line(d, pts[:6], C["hi"], .02)


def fountain(d, C, rng):
    ell(d, .5, .76, .34, .1, C["dark"]); ell(d, .5, .74, .28, .07, C["main"])
    for s in (-1, 0, 1):
        pts = [(.5 + s * .3 * t, .72 - .5 * math.sin(math.pi * min(1, .5 + t * .6)) * (1 if s else 1.1) * (1 - .3 * t)) for t in np.linspace(0, 1, 12)]
        line(d, pts, C["hi"], .045)
    ell(d, .5, .24, .06, .06, C["hi"])


def pillar(d, C, rng):
    ell(d, .5, .8, .3, .08, C["main"])
    poly(d, [(.38, .02), (.62, .02), (.58, .8), (.42, .8)], C["main"]); poly(d, [(.45, .02), (.55, .02), (.53, .8), (.47, .8)], C["hi"])


def sparkle(d, C, rng):
    star4(d, .5, .5, .38, C["main"]); star4(d, .5, .5, .2, C["hi"])
    for (x, y, r) in ((.8, .22, .08), (.22, .76, .07), (.78, .78, .05)):
        star4(d, x, y, r, C["hi"])


def plate(d, C, rng, slash=True, spikes=False):
    h = [(.5 + .36 * math.cos(math.radians(a)), .5 + .32 * math.sin(math.radians(a))) for a in range(-90, 270, 60)]
    poly(d, h, C["main"]); poly(d, [(x * .7 + .15, y * .7 + .15) for x, y in h], C["hi"])
    if slash:
        line(d, [(.2, .78), (.8, .22)], C["acc"], .05)
        line(d, [(.5, .5), (.56, .62), (.5, .7)], C["dark"], .018)


def shards(d, C, rng, n=10):
    ell(d, .5, .5, .12, .12, C["main"])
    for k in range(n):
        t = k * 2 * math.pi / n + rng.uniform(-.1, .1); r0, r1 = .2, .44
        poly(d, [(.5 + r1 * math.cos(t), .5 + r1 * math.sin(t)), (.5 + r0 * math.cos(t + .12), .5 + r0 * math.sin(t + .12)),
                 (.5 + r0 * math.cos(t - .12), .5 + r0 * math.sin(t - .12))], C["hi"] if k % 2 else C["main"])


def charge(d, C, rng, horns=False):
    poly(d, [(.14, .38), (.56, .38), (.56, .2), (.88, .5), (.56, .8), (.56, .62), (.14, .62)], C["main"])
    poly(d, [(.2, .44), (.6, .44), (.6, .32), (.8, .5), (.6, .68), (.6, .56), (.2, .56)], C["hi"])
    for y in (.26, .74):
        line(d, [(.08, y), (.3, y)], C["acc"], .025)


def shout(d, C, rng, source="stone"):
    if source == "stone":
        ell(d, .28, .5, .14, .16, C["main"])
    elif source == "horn":
        poly(d, [(.14, .56), (.4, .4), (.44, .64)], C["main"]); ell(d, .42, .52, .05, .13, C["hi"])
    else:
        ell(d, .28, .5, .1, .1, C["main"])
    for k, r in enumerate((.2, .3, .4)):
        arc(d, .28, .5, r, -45, 45, C["hi"] if k % 2 == 0 else C["acc"], .035)


def wing(d, C, rng, up=False):
    for k in range(5):
        a = math.radians(-160 + k * 24); L = .5 - k * .04
        poly(d, [(.4, .7), (.4 + L * math.cos(a) + .02, .7 + L * math.sin(a)), (.4 + L * math.cos(a + .3), .7 + L * math.sin(a + .3))], C["main"] if k % 2 else C["hi"])
    for k in range(3):
        arc(d, .58, .5 - k * .02, .22 + k * .07, 300, 380, C["acc"], .03)
    if up:
        poly(d, [(.74, .14), (.86, .32), (.62, .32)], C["acc"])


def feather(d, C, rng, sparks=True):
    line(d, [(.26, .84), (.72, .18)], C["dark"], .02)
    pts = [(.3, .76), (.36, .5), (.56, .22), (.74, .16), (.7, .36), (.52, .62)]
    poly(d, pts, C["main"]); poly(d, [(.36, .7), (.42, .5), (.6, .28), (.66, .32), (.52, .56)], C["hi"])
    if sparks:
        for (x, y) in ((.78, .44), (.2, .4), (.66, .74)):
            bolt(d, dict(C, main=C["acc"]), rng, scale=.22, cx=x) if False else star4(d, x, y, .06, C["acc"])


def crescent(d, C, rng):
    d.pieslice([.12 * S, .12 * S, .88 * S, .88 * S], 200, 340, fill=C["main"])
    d.pieslice([.2 * S, .24 * S, .8 * S, .9 * S], 190, 350, fill=C["dark"])
    arc(d, .5, .5, .36, 205, 335, C["hi"], .025)


def hourglass(d, C, rng):
    poly(d, [(.3, .16), (.7, .16), (.52, .5), (.7, .84), (.3, .84), (.48, .5)], C["main"])
    poly(d, [(.36, .2), (.64, .2), (.5, .44)], C["hi"]); poly(d, [(.38, .8), (.62, .8), (.5, .64)], C["hi"])
    for y in (.14, .86):
        d.rounded_rectangle([.24 * S, (y - .03) * S, .76 * S, (y + .03) * S], radius=10, fill=C["dark"])


def reticle(d, C, rng):
    arc(d, .5, .5, .3, 0, 360, C["main"], .05)
    for a in (0, 90, 180, 270):
        t = math.radians(a); line(d, [(.5 + .16 * math.cos(t), .5 + .16 * math.sin(t)), (.5 + .42 * math.cos(t), .5 + .42 * math.sin(t))], C["hi"], .035)
    ell(d, .5, .5, .05, .05, C["acc"])


def heart(d, C, rng, flame_top=False):
    ell(d, .38, .42, .15, .15, C["main"]); ell(d, .62, .42, .15, .15, C["main"])
    poly(d, [(.24, .48), (.76, .48), (.5, .82)], C["main"]); ell(d, .36, .38, .05, .05, C["hi"])
    if flame_top:
        flame(d, C, rng, cx=.5, cy=.3, s=.35)


def notes(d, C, rng):
    for (x, y) in ((.36, .7), (.66, .62)):
        ell(d, x, y, .09, .07, C["main"]); line(d, [(x + .08, y), (x + .08, y - .44)], C["main"], .035)
    line(d, [(.44, .26), (.74, .18)], C["main"], .06)
    for (x, y) in ((.22, .3), (.84, .4)):
        star4(d, x, y, .05, C["hi"])


def chevrons(d, C, rng, n=3):
    for k in range(n):
        y = .7 - k * .2; c = (C["main"], C["hi"], C["acc"])[k % 3]
        poly(d, [(.22, y + .1), (.5, y - .1), (.78, y + .1), (.78, y + .2), (.5, y), (.22, y + .2)], c)


def arrow(d, C, rng):
    line(d, [(.2, .8), (.76, .24)], C["main"], .03)
    poly(d, rot([(.76, .5), (.9, .5), (.76, .44)], 0), C["hi"])
    poly(d, [(.82, .18), (.7, .24), (.76, .30)], C["hi"])
    for k in range(2):
        poly(d, [(.2 + k * .05, .8 - k * .05), (.12 + k * .05, .76 - k * .05), (.2 + k * .05, .72 - k * .05)], C["acc"])
        poly(d, [(.2 + k * .05, .8 - k * .05), (.24 + k * .05, .88 - k * .05), (.28 + k * .05, .8 - k * .05)], C["acc"])


def staff(d, C, rng):
    line(d, [(.24, .88), (.34, .6), (.48, .44), (.62, .28)], C["main"], .05)
    ell(d, .66, .24, .1, .1, C["hi"]); impact_small(d, C, .72, .2)


def impact_small(d, C, x, y):
    for a in np.linspace(0, 2 * math.pi, 7)[:-1]:
        line(d, [(x + .12 * math.cos(a), y + .12 * math.sin(a)), (x + .2 * math.cos(a), y + .2 * math.sin(a))], C["acc"], .02)


def stinger(d, C, rng):
    pts = [(.3, .2), (.5, .24), (.64, .4), (.68, .6), (.6, .8)]
    line(d, pts, C["main"], .1); poly(d, [(.56, .76), (.66, .74), (.5, .94)], C["hi"])
    droplet(d, dict(C, main=C["acc"]), rng, cx=.36, cy=.76, r=.07, sparkle=False)


def curse(d, C, rng, target="shield"):
    if target == "shield":
        shield(d, C, rng, cracked=True, glow=False, emblem=None)
    for k in range(2):
        pts = [(.5 + (.3 + .08 * k) * math.cos(t), .5 + (.3 + .08 * k) * math.sin(t) * .9) for t in np.linspace(k * 2, k * 2 + 4.2, 20)]
        line(d, pts, C["acc"], .04)



def shell_big(d, C, rng):
    """Deep Shell fix: one big scallop filling ~80% of the circle, no bubble ring."""
    pts = [(.5, .86)] + [(.5 - .4 * math.cos(t), .56 - .4 * math.sin(t)) for t in np.linspace(0, math.pi, 16)]
    poly(d, pts, C["acc"])
    for k in range(-4, 5):
        t = math.pi / 2 + k * .3
        line(d, [(.5, .84), (.5 - .36 * math.cos(t), .56 - .36 * math.sin(t))], mix(C["acc"], C["dark"], .45), .025)
    poly(d, [(.4, .9), (.6, .9), (.56, .8), (.44, .8)], mix(C["acc"], C["dark"], .3))
    ell(d, .36, .3, .05, .03, C["hi"])


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


def slab_ring2(d, C, rng, n=7):
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


def spore_puff(d, C, rng):
    """Spore Cloud reroll: a mushroom at the bottom puffing a big dotted spore cloud upward."""
    for (x, y, r) in ((.32, .38, .15), (.5, .28, .18), (.68, .38, .15), (.5, .44, .16)):
        ell(d, x, y, r, r, (206, 214, 120))
    for k in range(16):
        ell(d, rng.uniform(.24, .76), rng.uniform(.18, .52), .022, .022, C["dark"])
    poly(d, [(.44, .88), (.56, .88), (.54, .66), (.46, .66)], (236, 226, 200))
    d.chord([.26 * S, .52 * S, .74 * S, .84 * S], 180, 360, fill=(176, 118, 70))   # tan cap (not a red/white-spot game mushroom)
    for (x, y) in ((.38, .62), (.52, .58), (.62, .64)):
        ell(d, x, y, .03, .025, (250, 240, 220))


def thorn_whip(d, C, rng):
    """Thorn Lash reroll: one thick thorned vine whip in an S-lash, big thorns."""
    pts = [(.16 + .7 * t, .84 - .7 * t + .14 * math.sin(t * 6.5)) for t in np.linspace(0, 1, 26)]
    line(d, pts, C["dark"], .11); line(d, pts, C["main"], .075)
    for k in range(2, 25, 3):
        x, y = pts[k]; sg = 1 if k % 2 else -1
        poly(d, [(x - .045, y), (x + .045, y), (x + sg * .12, y - .13)], C["hi"])
    for (x, y) in (pts[-1],):
        ell(d, x, y, .05, .05, C["acc"])


def clap(d, C, rng):
    """Thunderclap reroll: a bolt striking the centre with concentric shockwave rings around it (area)."""
    for k, r in enumerate((.42, .33, .24)):
        arc(d, .5, .54, r, 0, 360, C["hi"] if k % 2 == 0 else C["acc"], .03)
    bolt(d, C, rng, scale=.62, cx=.5)


def bolt_row(d, C, rng, n=4):
    """Plasma Barrage reroll: four small lightning bolts in a diagonal row, a line attack."""
    for k in range(n):
        t = k / (n - 1); cx, cy = .26 + .48 * t, .74 - .48 * t; sc = .3 + .04 * t
        pts = [(.3, .1), (.52, .1), (.44, .38), (.6, .38), (.34, .9), (.4, .52), (.26, .52)]
        poly(d, [(cx + (x - .43) * sc * 1.4, cy + (y - .5) * sc * 1.4) for x, y in pts], C["main"] if k % 2 == 0 else C["hi"])
    line(d, [(.14, .86), (.86, .14)], C["dark"], .02)


def toxic_cloud(d, C, rng):
    """Spore Cloud reroll 2 (producer): one large billowing toxic-green cloud, small spore specks."""
    for (x, y, r) in ((.28, .56, .17), (.42, .4, .2), (.6, .38, .19), (.73, .54, .17), (.5, .6, .22), (.34, .66, .13), (.66, .67, .13)):
        ell(d, x, y, r + .02, r + .02, (60, 110, 40))
    for (x, y, r) in ((.28, .56, .17), (.42, .4, .2), (.6, .38, .19), (.73, .54, .17), (.5, .6, .22), (.34, .66, .13), (.66, .67, .13)):
        ell(d, x, y, r, r, (150, 210, 70))
    for (x, y, r) in ((.4, .36, .09), (.58, .34, .08), (.3, .52, .07)):
        ell(d, x, y, r, r * .7, (205, 240, 130))
    for k in range(22):
        a = rng.uniform(0, 2 * math.pi); r = rng.uniform(.1, .44)
        ell(d, .5 + r * math.cos(a), .52 + r * math.sin(a) * .9, .016, .016, (235, 250, 170))


def talon_bolts(d, C, rng):
    """Thunder Talons (Lightning redo): three thick jagged lightning claw slashes."""
    for k in range(3):
        o = (k - 1) * .16
        pts = [(.28 + o, .84), (.42 + o, .6), (.34 + o, .56), (.52 + o, .32), (.46 + o, .3), (.64 + o, .12)]
        line(d, pts, C["dark"], .09); line(d, pts, C["main"], .06); line(d, pts, C["hi"], .02)


def chain_nodes(d, C, rng):
    """Chain Lightning (redo): jagged bolts jumping between three targets (a chain)."""
    nodes = [(.24, .72), (.5, .26), (.78, .7)]
    for (a, b) in ((0, 1), (1, 2), (2, 0)):
        (x0, y0), (x1, y1) = nodes[a], nodes[b]; pts = [(x0, y0)]
        for t in (.25, .5, .75):
            nx, ny = -(y1 - y0), (x1 - x0); sg = 1 if t != .5 else -1
            pts.append((x0 + (x1 - x0) * t + nx * .12 * sg, y0 + (y1 - y0) * t + ny * .12 * sg))
        pts.append((x1, y1)); line(d, pts, C["dark"], .07); line(d, pts, C["main"], .045); line(d, pts, C["hi"], .015)
    for (x, y) in nodes:
        ell(d, x, y, .08, .08, C["acc"]); ell(d, x, y, .04, .04, C["hi"])


def charged_feather(d, C, rng):
    """Static Charge (redo): one big feather crackling with cyan static arcs (a self power-up)."""
    ang = math.radians(-50)
    vane = [(.5 + .4 * math.cos(t) , .5 + .14 * math.sin(t) * (1 - .35 * math.cos(t))) for t in np.linspace(0, 2 * math.pi, 40)]
    poly(d, rot(vane, ang), C["main"])
    for k in range(4):                                     # notches in the vane
        x = .3 + k * .12; sg = 1 if k % 2 else -1
        poly(d, rot([(x, .5 + sg * .16), (x + .05, .5 + sg * .06), (x + .07, .5 + sg * .17)], ang), C["dark"])
    line(d, rot([(.06, .5), (.9, .5)], ang), C["hi"], .025)
    for (cx, cy, r, a0) in ((.5, .5, .4, 200), (.5, .5, .34, 20)):
        pts = [(cx + r * math.cos(math.radians(a0 + k * 12)) + rng.uniform(-.02, .02), cy + r * math.sin(math.radians(a0 + k * 12)) + rng.uniform(-.02, .02)) for k in range(9)]
        line(d, pts, C["acc"], .025)


def storm_dive_bolt(d, C, rng):
    """Storm Dive (redo): a dark storm cloud at the top, one thick bolt diving down from it onto a target."""
    for (x, y, r) in ((.3, .22, .13), (.46, .16, .16), (.64, .2, .14), (.52, .28, .15)):
        ell(d, x, y, r, r, C["dark"])
    pts = [(.46, .3), (.58, .3), (.52, .5), (.66, .5), (.44, .88), (.48, .6), (.36, .6)]
    poly(d, pts, C["main"]); poly(d, [(x * .5 + .26, y) for x, y in pts], C["hi"]) if False else None
    ell(d, .44, .88, .12, .04, C["acc"])


def clap2(d, C, rng):
    """Thunderclap (redo): a bolt striking the centre, jagged shockwave rings around it (area)."""
    for k, r in enumerate((.43, .33)):
        pts = [(.5 + (r + (.02 if j % 2 else -.02)) * math.cos(j * math.pi / 12), .54 + (r + (.02 if j % 2 else -.02)) * math.sin(j * math.pi / 12)) for j in range(25)]
        line(d, pts, C["acc"] if k else C["hi"], .025)
    bolt(d, C, rng, scale=.6, cx=.5)


def bolt_row2(d, C, rng, n=4):
    """Plasma Barrage (redo): four bold bolts flying in a diagonal row (a line attack)."""
    for k in range(n):
        t = k / (n - 1); cx, cy = .27 + .46 * t, .73 - .46 * t; sc = .34
        pts = [(.3, .1), (.52, .1), (.44, .38), (.6, .38), (.34, .9), (.4, .52), (.26, .52)]
        P2 = [(cx + (x - .43) * sc, cy + (y - .5) * sc) for x, y in pts]
        poly(d, [(x + .012, y + .012) for x, y in P2], C["dark"]); poly(d, P2, C["main"] if k % 2 == 0 else C["acc"])


def wind_spear(d, C, rng):
    """Wind Lance reroll: one thick spear of air wrapped in spiralling wind lines (a line attack)."""
    lance(d, C, rng, w=.09, flare=False)
    for k in range(4):
        t = .25 + k * .15; x, y = .14 + .74 * t, .86 - .74 * t
        arc(d, x, y, .1, 200, 380, C["acc"], .025)


def rising_wind(d, C, rng):
    """Updraft reroll: three big upward wind swooshes lifting a wing (a team speed/move buff)."""
    for k, x in enumerate((.3, .5, .7)):
        pts = [(x + .06 * math.sin(t * 5 + k), .86 - .6 * t) for t in np.linspace(0, 1, 14)]
        line(d, pts, C["hi"] if k == 1 else C["main"], .05)
        tx, ty = pts[-1]; poly(d, [(tx, ty - .08), (tx + .07, ty + .02), (tx - .07, ty + .02)], C["acc"])


def talon_dive(d, C, rng):
    """Sky Rend reroll: one big hooked talon (a claw crescent) plunging down, tearing three gash lines."""
    cx, cy = .38, .4
    outer = [(cx + .36 * math.cos(math.radians(a)), cy + .36 * math.sin(math.radians(a))) for a in np.linspace(-70, 95, 18)]
    inner = [(cx + .2 * math.cos(math.radians(a)) + .06, cy + .22 * math.sin(math.radians(a)) + .02) for a in np.linspace(95, -70, 18)]
    tip = [(cx + .36 * math.cos(math.radians(100)) - .02, cy + .36 * math.sin(math.radians(100)) + .04)]
    poly(d, outer + tip + inner, C["main"])
    poly(d, outer[:9] + [(p[0] - .03, p[1]) for p in reversed(outer[:9])], C["hi"]) if False else None
    for k in range(3):
        line(d, [(.2 + k * .12, .9), (.36 + k * .12, .74)], C["acc"], .03)



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


def wilted(d, C, rng):
    """Withering Curse reroll: a drooping wilted flower wrapped in violet curse wisps (enemies weakened)."""
    line(d, [(.42, .9), (.42, .5), (.5, .3), (.62, .3), (.68, .42)], C["dark"], .05)
    for k in range(5):                                      # drooping head, petals hanging down
        a = math.radians(30 + k * 30); ell(d, .68 + .12 * math.cos(a), .5 + .14 * math.sin(a), .07, .1, C["main"])
    ell(d, .68, .48, .06, .06, C["dark"])
    poly(d, [(.42, .66), (.26, .6), (.3, .72)], C["dark"])
    for k in range(2):
        pts = [(.5 + (.32 + .08 * k) * math.cos(t), .52 + (.32 + .08 * k) * math.sin(t) * .9) for t in np.linspace(k * 2.4, k * 2.4 + 3.8, 20)]
        line(d, pts, C["acc"], .045)


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
    """Great Cleave reroll: one wide bright crescent blade sweep, pointed at both ends, with motion lines."""
    outer = [(.5 + .42 * math.cos(math.radians(t)), .62 + .42 * math.sin(math.radians(t))) for t in np.linspace(195, 345, 24)]
    inner = [(.5 + .34 * math.cos(math.radians(t)), .7 + .3 * math.sin(math.radians(t))) for t in np.linspace(345, 195, 24)]
    poly(d, outer + inner, C["hi"])
    for k in range(3):
        arc(d, .5, .72, .26 - k * .06, 215, 325, C["main"], .015)



def thorn_whip2(d, C, rng):
    """Thorn Lash reroll 2 (producer): a thick S-whip with 6 LARGE curved hook thorns (each >10% of the width)."""
    pts = [(.2 + .6 * t, .84 - .68 * t + .13 * math.sin(t * 6.3)) for t in np.linspace(0, 1, 30)]
    line(d, pts, C["dark"], .12); line(d, pts, C["main"], .085)
    for j, k in enumerate((4, 8, 12, 16, 20, 25)):
        x, y = pts[k]; (x2, y2) = pts[k + 1]; tx, ty = x2 - x, y2 - y; L = math.hypot(tx, ty); tx, ty = tx / L, ty / L
        sg = 1 if j % 2 else -1; nx, ny = -ty * sg, tx * sg
        base = [(x - tx * .06, y - ty * .06), (x + tx * .06, y + ty * .06)]
        tip = (x + nx * .15 + tx * .06, y + ny * .15 + ty * .06); mid = (x + nx * .08 - tx * .01, y + ny * .08 - ty * .01)
        poly(d, [base[0], mid, tip, base[1]], C["dark"])
        poly(d, [(base[0][0] + nx * .01, base[0][1] + ny * .01), (mid[0], mid[1]), (tip[0] - nx * .01, tip[1] - ny * .01), (base[1][0] + nx * .01, base[1][1] + ny * .01)], C["hi"])


def sunder_split(d, C, rng):
    """Sunder reroll 2 (producer): a riveted steel plate split diagonally by a bright slash, the two halves separating."""
    h = [(.5 + .36 * math.cos(math.radians(a)), .5 + .36 * math.sin(math.radians(a))) for a in range(-90, 270, 45)]
    for sgn in (-1, 1):
        off = (.05 * sgn, .05 * sgn)
        half = [p for p in h if (p[0] - .5) - (p[1] - .5) * -1 >= 0] if sgn > 0 else [p for p in h if (p[0] - .5) + (p[1] - .5) <= 0]
        half = [(x + off[0], y + off[1]) for x, y in (half + [(.5 + .4 * .7, .5 - .4 * .7), (.5 - .4 * .7, .5 + .4 * .7)])]
        cx = sum(x for x, _ in half) / len(half); cy = sum(y for _, y in half) / len(half)
        half.sort(key=lambda p: math.atan2(p[1] - cy, p[0] - cx))
        poly(d, half, C["main"]); poly(d, [(cx + (x - cx) * .75, cy + (y - cy) * .75) for x, y in half], C["hi"])
        for k in range(3):
            ell(d, cx + (half[k][0] - cx) * .82, cy + (half[k][1] - cy) * .82, .02, .02, C["dark"])
    line(d, [(.16, .86), (.86, .16)], (255, 236, 170), .05); line(d, [(.16, .86), (.86, .16)], (255, 255, 240), .018)


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


def mend_leaf(d, C, rng):
    """Mending Light reroll 2 (producer): a pale-green mending leaf wrapped by a cream bandage strip, soft cool-white
    glow and small four-point sparkles. No orange, nothing flame-shaped."""
    ell(d, .5, .5, .4, .4, (96, 74, 44))
    pts = [(.5 + .19 * math.sin(t), .5 - .4 * math.cos(t)) for t in np.linspace(0, math.pi, 13)]
    pts += [(1 - x, y) for x, y in reversed(pts[1:-1])]
    poly(d, rot(pts, math.radians(35)), (170, 225, 160))
    line(d, rot([(.5, .86), (.5, .18)], math.radians(35)), (225, 245, 215), .02)
    poly(d, rot([(.34, .46), (.66, .46), (.66, .56), (.34, .56)], math.radians(-20)), (245, 236, 214))
    for x in (.42, .5, .58):
        line(d, rot([(x, .47), (x, .55)], math.radians(-20)), (210, 196, 170), .008)
    for (x, y, r) in ((.8, .24, .06), (.2, .74, .05), (.76, .78, .04)):
        star4(d, x, y, r, (240, 250, 245))


def wilted_big(d, C, rng):
    """Withering Curse reroll 2 (producer): a LARGE drooping flower, bent stem, falling petals; thin violet wisps only."""
    pts = [(.5 + .35 * math.cos(t), .5 + .38 * math.sin(t) * .9) for t in np.linspace(2.4, 5.6, 16)]
    line(d, pts, C["acc"], .018)
    line(d, [(.34, .92), (.34, .56), (.4, .34), (.54, .26), (.62, .32)], (70, 58, 40), .06)       # bent stem
    poly(d, [(.34, .7), (.18, .62), (.24, .76)], (120, 110, 76))                                         # droopy leaf
    for k in range(6):                                                                                   # hanging bloom
        a = math.radians(20 + k * 28)
        ell(d, .64 + .14 * math.cos(a), .46 + .17 * math.sin(a), .085, .12, (176, 92, 160))
    ell(d, .64, .42, .07, .06, (90, 40, 80))
    for (x, y, r) in ((.52, .76, .04), (.74, .84, .035), (.84, .66, .03)):                             # falling petals
        ell(d, x, y, r, r * .6, (176, 92, 160))

PRIMS = {k: v for k, v in globals().items() if callable(v) and k not in ("P", "poly", "ell", "line", "arc", "rot", "mix", "star4", "impact_small")}


def layout(prim, C, seed, soft=False, haze=False, **kw):
    import random
    rng = random.Random(seed); nrng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:S, 0:S] / S
    r = np.clip(np.hypot(xx - .5, yy - .45) / .72, 0, 1)[..., None]
    bg = np.array(C["bg_in"]) * (1 - r) + np.array(C["bg_out"]) * r
    im = Image.fromarray(bg.astype(np.uint8)); d = ImageDraw.Draw(im)
    if haze:                                   # the Gloam: lilac wisps around the motif
        ov = Image.new("RGBA", (S, S), (0, 0, 0, 0)); od = ImageDraw.Draw(ov)
        for k in range(5):
            t = rng.uniform(0, 2 * math.pi); pts = [(.5 + (.3 + .1 * j / 10) * math.cos(t + j * .12), .5 + (.34 + .08 * j / 10) * math.sin(t + j * .12)) for j in range(12)]
            od.line([P(*p) for p in pts], fill=(143, 134, 160, 170), width=int(.05 * S))
        im = Image.alpha_composite(im.convert("RGBA"), ov.filter(ImageFilter.GaussianBlur(14))).convert("RGB"); d = ImageDraw.Draw(im)
    PRIMS[prim](d, C, rng, **kw)
    im = im.filter(ImageFilter.GaussianBlur(9 if soft else 6))
    a = np.asarray(im).astype(np.float32) + nrng.normal(0, 6, (S, S, 3))
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
