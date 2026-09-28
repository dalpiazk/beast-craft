# Skill-icon style test (2026-09-27): circular 256 icons, 2 frame/ring options (procedural), the review sheet.
"""python icon_sheet.py  -> work/icons/icon256/{tag}.png, work/icons/frames/*.png, ../icon_style_test.png
Layers per the slot spec (hollow-art-slots.md s.2): ring 1.00 of the box, icon 0.76, frame 0.90; 256 canvas each."""
import math, os, pathlib, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = pathlib.Path(os.environ["ARTLAB_HOLLOW"])   # the Hollow art work folder
IW = ROOT / "work" / "icons"; RAW = IW / "raw"; I256 = IW / "icon256"; FR = IW / "frames"
for p in (I256, FR):
    p.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from icons import ORDER, SK  # noqa

SS = 1024                     # supersampled canvas for the frames
PLUM = (59, 28, 38)
RARITY = {"common": (168, 184, 150), "rare": (80, 150, 235), "epic": (176, 96, 226), "legendary": (246, 186, 56),
          "gloam": (164, 144, 196)}


def down(im, size):
    """Premultiplied Lanczos downscale (straight alpha out)."""
    a = np.asarray(im.convert("RGBA")).astype(np.float32) / 255
    pm = a.copy(); pm[..., :3] *= pm[..., 3:4]
    ch = [Image.fromarray((pm[..., k] * 255).astype(np.uint8)).resize((size, size), Image.LANCZOS) for k in range(4)]
    o = np.stack([np.asarray(c).astype(np.float32) / 255 for c in ch], -1)
    o[..., :3] = np.where(o[..., 3:4] > 1e-3, o[..., :3] / np.maximum(o[..., 3:4], 1e-3), 0)
    return Image.fromarray((np.clip(o, 0, 1) * 255).astype(np.uint8), "RGBA")


def polar():
    yy, xx = np.mgrid[0:SS, 0:SS].astype(np.float32) + 0.5
    dx, dy = (xx - SS / 2) / (SS / 2), (yy - SS / 2) / (SS / 2)
    return np.hypot(dx, dy), np.arctan2(dy, dx)


R, TH = polar()
NOISE = np.asarray(Image.fromarray((np.random.default_rng(3).random((SS // 8, SS // 8)) * 255).astype(np.uint8))
                   .resize((SS, SS), Image.BICUBIC)).astype(np.float32) / 255 - 0.5


def band(r0, r1, soft=0.004):
    return np.clip((R - r0) / soft, 0, 1) * np.clip((r1 - R) / soft, 0, 1)


def rgba(rgb, alpha):
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.clip(alpha, 0, 1) * 255]).astype(np.uint8), "RGBA")


def light(r0, r1):
    """Bevel: top-left key light on a ring band (convex profile)."""
    t = np.clip((R - r0) / (r1 - r0), 0, 1)
    prof = np.sin(t * math.pi)                           # raised middle
    key = np.cos(TH - math.radians(-135))                # top-left
    return prof, key


def frame_bronze():
    r0, r1 = 0.82, 0.985; prof, key = light(r0, r1)
    base = np.array([150, 98, 52], np.float32); hi = np.array([240, 196, 128], np.float32); lo = np.array([84, 50, 30], np.float32)
    k = np.clip(0.5 + 0.35 * key * (0.4 + prof) + 0.25 * prof + 0.25 * NOISE, 0, 1)[..., None]
    rgb = np.where(k > 0.5, base + (hi - base) * (k - 0.5) * 2, lo + (base - lo) * k * 2)
    a = band(r0, r1)
    for rr in (r0 + 0.006, r1 - 0.006):                  # plum keylines
        m = band(rr - 0.007, rr + 0.007)[..., None]; rgb = rgb * (1 - m) + np.array(PLUM) * m
    return down(rgba(rgb, a), 256)


def frame_light():
    r0, r1 = 0.82, 0.985; prof, key = light(r0, r1)
    base = np.array([236, 226, 206], np.float32); hi = np.array([255, 252, 240], np.float32); lo = np.array([180, 160, 140], np.float32)
    k = np.clip(0.55 + 0.3 * key * (0.4 + prof) + 0.2 * prof + 0.15 * NOISE, 0, 1)[..., None]
    rgb = np.where(k > 0.5, base + (hi - base) * (k - 0.5) * 2, lo + (base - lo) * k * 2)
    m = band(r0 + 0.02, r0 + 0.035)[..., None]; rgb = rgb * (1 - m) + np.array([214, 172, 92]) * m   # thin gold inner line
    for rr in (r0 + 0.004, r1 - 0.004):
        m = band(rr - 0.005, rr + 0.005)[..., None]; rgb = rgb * (1 - m) + np.array([120, 90, 80]) * m
    return down(rgba(rgb, band(r0, r1)), 256)


def ring_plain(col):
    col = np.array(col, np.float32); r0, r1 = 0.86, 0.975; prof, key = light(r0, r1)
    rgb = col * (0.8 + 0.35 * prof[..., None] + 0.12 * key[..., None]) + 30 * NOISE[..., None]
    glow = np.clip(1 - (R - r1) / 0.025, 0, 1) * (R > r1) * 0.55
    a = np.maximum(band(r0, r1), glow)
    rgb = np.where((R > r1)[..., None], col * 1.1 + 20, rgb)
    return down(rgba(rgb, a), 256)


def ring_gem(col):
    col = np.array(col, np.float32); r0, r1 = 0.88, 0.965
    rgb = np.ones((SS, SS, 3), np.float32) * (col * 0.55 + 90) + 25 * NOISE[..., None]
    a = band(r0, r1) * 0.95
    glow = np.clip(1 - (R - r1) / 0.03, 0, 1) * (R > r1) * 0.45
    a = np.maximum(a, glow); rgb = np.where((R > r1)[..., None], col * 1.05 + 25, rgb)
    im = rgba(rgb, a); d = ImageDraw.Draw(im)
    for k in range(8):                                   # 8 faceted gems, 4 big at the cardinal points
        t = k * math.pi / 4; big = k % 2 == 0; rr = 0.925 * SS / 2; g = (0.06 if big else 0.038) * SS / 2
        cx, cy = SS / 2 + rr * math.cos(t - math.pi / 2), SS / 2 + rr * math.sin(t - math.pi / 2)
        pts = [(cx, cy - g * 1.25), (cx + g, cy), (cx, cy + g * 1.25), (cx - g, cy)]
        d.polygon(pts, fill=tuple(int(v) for v in np.clip(col * 0.7, 0, 255)) + (255,), outline=(255, 246, 225, 255), width=5)
        d.polygon([(cx, cy - g * 1.25), (cx + g, cy), (cx, cy)], fill=tuple(int(v) for v in np.clip(col * 1.15 + 30, 0, 255)) + (255,))
        d.ellipse([cx - g * .35 - g * .3, cy - g * .7, cx - g * .3 + g * .1, cy - g * .3], fill=(255, 255, 255, 230))
    return down(im, 256)


def bold(im):
    """Treatment B post: saturation 1.2, S-curve contrast, darker rim (vignette), bloom on the brights."""
    from PIL import ImageEnhance
    im = ImageEnhance.Color(im).enhance(1.2)
    a = np.asarray(im).astype(np.float32) / 255
    a = a + 0.35 * (a - 0.5) * (1 - np.abs(2 * a - 1))            # S-curve
    a = a * (1 - 0.38 * np.clip((R - 0.45) / 0.55, 0, 1) ** 1.5)[..., None]
    br = np.clip(a - 0.62, 0, 1) * 2.2
    bl = np.asarray(Image.fromarray((np.clip(br, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(28))).astype(np.float32) / 255
    a = 1 - (1 - a) * (1 - 0.55 * bl)                              # screen
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8))


def icon256(tag):
    im = Image.open(RAW / f"{tag}.png").convert("RGB").resize((SS, SS), Image.LANCZOS)
    if tag.endswith("_B"):
        im = bold(im)
    a = np.clip((1.0 - R) / 0.004, 0, 1)                 # inscribed circle, soft 2 px edge
    return down(rgba(np.asarray(im).astype(np.float32), a), 256)


def icon256_img(raw, bold_pass=True):
    """The icon-batch entry point: the same B post-pass and circle cut as icon256(), from an image."""
    im = raw.convert("RGB").resize((SS, SS), Image.LANCZOS)
    if bold_pass:
        im = bold(im)
    a = np.clip((1.0 - R) / 0.004, 0, 1)
    return down(rgba(np.asarray(im).astype(np.float32), a), 256)


def compose(icon, frame, ring, box):
    c = Image.new("RGBA", (box, box), (0, 0, 0, 0))
    for layer, f in ((ring, 1.0), (icon, 0.76), (frame, 0.90)):
        s = round(box * f); l = down(layer, s) if s != layer.width else layer
        c.alpha_composite(l, ((box - s) // 2, (box - s) // 2))
    return c


def font(sz, bold=False):
    for f in (("georgiab.ttf", "arialbd.ttf") if bold else ("georgia.ttf", "arial.ttf")):
        try:
            return ImageFont.truetype(f, sz)
        except OSError:
            pass
    return ImageFont.load_default()


TRN = {"A": "A  Painterly & soft (Ghibli-warm, matches the beast art)",
       "B": "B  Bold, high contrast, glow (mobile-RPG reference; + saturation/contrast/rim-dark/bloom post)",
       "C": "C  Flat-graphic motif + painterly texture"}
NAMES = {"ember_shot": "Ember Shot", "boulder_slam": "Boulder Slam", "radiant_bolt": "Radiant Bolt",
         "deep_shell": "Deep Shell", "chain_lightning": "Chain Lightning", "petrifying_gaze": "Petrifying Gaze",
         "rime_bolt": "Rime Bolt", "aegis": "Aegis"}

if __name__ == "__main__":
    F = {"bronze": frame_bronze(), "light": frame_light()}
    RG = {("bronze", r): ring_plain(c) for r, c in RARITY.items()}
    RG.update({("light", r): ring_gem(c) for r, c in RARITY.items()})
    for k, v in F.items():
        v.save(FR / f"frame_{k}.png")
    for (k, r), v in RG.items():
        v.save(FR / f"ring_{k}_{r}.png")

    LW, CW = 250, 190; BG = (250, 244, 232); DARK = (34, 27, 34)
    blockH = 34 + 172 + 2 * 112 + 20
    W = LW + 8 * CW + 20; top = 120; legendH = 240
    H = top + 3 * blockH + legendH + 20
    sh = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(sh)
    d.text((20, 16), "Skill-icon style test: 8 centres x 3 treatments x 2 frame/ring options", fill=PLUM, font=font(32, True))
    d.text((20, 60), "Top row per treatment: the 256 px centre (shown at 160). Dark rows: framed at the in-game 96 px and 64 px (1:1, "
           "no scaling), bronze frame + coloured rarity ring, then light frame + gem ring. Beast skills: common ring; Aegis (avatar): rare.",
           fill=(90, 60, 60), font=font(16))
    for j, s in enumerate(ORDER):
        d.text((LW + j * CW + 8, 92), f"{NAMES[s]}", fill=PLUM, font=font(17, True))
        d.text((LW + j * CW + 8, 110 - 2), "", fill=PLUM)
    y = top + 10
    for t in "ABC":
        d.text((20, y), TRN[t], fill=PLUM, font=font(20, True)); y += 34
        d.rectangle([LW - 6, y + 172, W - 14, y + 172 + 2 * 112], fill=DARK)
        d.text((20, y + 60), "256 px centre", fill=(90, 60, 60), font=font(16))
        d.text((20, y + 172 + 40), "bronze + ring", fill=(90, 60, 60), font=font(16))
        d.text((20, y + 172 + 152), "light + gem ring", fill=(90, 60, 60), font=font(16))
        for j, s in enumerate(ORDER):
            tag = f"{s}_{t}"
            if not (RAW / f"{tag}.png").exists():
                continue
            ic = icon256(tag); ic.save(I256 / f"{tag}.png")
            x = LW + j * CW
            sh.paste(down(ic, 160), (x + 10, y + 4), down(ic, 160))
            rar = "rare" if s == "aegis" else "common"
            for k, (fr, yy) in enumerate((("bronze", y + 172 + 8), ("light", y + 172 + 120))):
                for sz, xo in ((96, 4), (64, 108)):
                    c = compose(ic, F[fr], RG[(fr, rar)], sz)
                    sh.paste(c, (x + xo, yy + (96 - sz) // 2), c)
        y += blockH - 34
    # legend: the five rarity rings on both frames (Ember Shot A) at 96 px
    d.text((20, y + 6), "Rarity rings, both options (at 96 px): common / rare / epic / legendary / gloam", fill=PLUM, font=font(20, True))
    d.rectangle([LW - 6, y + 40, W - 14, y + 40 + 180], fill=DARK)
    ex = I256 / "ember_shot_A.png"
    if ex.exists():
        ic = Image.open(ex)
        for k, fr in enumerate(("bronze", "light")):
            for j, r in enumerate(RARITY):
                c = compose(ic, F[fr], RG[(fr, r)], 96)
                sh.paste(c, (LW + (k * 5 + j) * 150 + 20, y + 40 + 42), c)
            d.text((LW + k * 750 + 20, y + 40 + 150), "bronze frame + coloured ring" if fr == "bronze" else "light frame + gem ring",
                   fill=(230, 220, 210), font=font(16))
    sh.save(ROOT / "icon_style_test.png")
    print("ok", sh.size)
