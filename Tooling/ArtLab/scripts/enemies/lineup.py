# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): hollow_enemies_lineup.png, the relative in-game sizes (the WorldHeight rule).
"""hollow_enemies_lineup.png: all 9 Verdant Hollow enemies at in-game relative sizes, each standing on its board
footprint (flat-top hexes drawn foreshortened). 1-hex units are fit to one hex width (tall folk capped at 1.7 hex
height); Swarmling/Stingling are swarm units drawn at 0.55 of a 1-hex unit; Champion = 3-hex triangle (1.75 hex wide);
Giant = 7-hex flower (2.5 hex wide). Each character is scaled so its width matches its footprint width."""
import os, pathlib, math
from PIL import Image, ImageDraw, ImageFont
FE = pathlib.Path(os.environ.get("ARTLAB_ENEMIES") or pathlib.Path(__file__).resolve().parent)  # the nine enemy folders
HEX = 170; SQ = 0.42                                   # hex width px, vertical foreshortening
units = [("swarmling", "swarm"), ("stingling", "swarm"), ("brute", 1), ("stalker", 1), ("archer", 1), ("caster", 1),
         ("shaman", 1), ("champion", 3), ("giant", 7)]
labels = {"swarmling": "Swarmling (swarm)", "stingling": "Stingling (swarm)", "archer": "Archer", "caster": "Caster",
          "shaman": "Shaman", "stalker": "Stalker", "brute": "Brute", "champion": "Champion (3-hex)", "giant": "Giant (7-hex)"}
R = HEX / 2
def cells(k):
    dx, dy = 0.75 * HEX, math.sqrt(3) / 2 * HEX
    if k == 7: return [(0, 0), (0, -dy), (0, dy), (-dx, -dy / 2), (-dx, dy / 2), (dx, -dy / 2), (dx, dy / 2)]
    if k == 3: return [(-dx / 3, -dy / 2), (-dx / 3, dy / 2), (2 * dx / 3, 0)]
    return [(0, 0)]
def fp_w(k): return {7: 2.5, 3: 1.75}.get(k, 1.0) * HEX
ims = []
for t, k in units:
    ch = Image.open(FE / t / "rig" / "character.png").convert("RGBA"); ch = ch.crop(ch.getbbox())
    w = fp_w(k) * (0.55 if k == "swarm" else 1.0) * 0.95
    h = ch.height * w / ch.width
    if k == 1 and h > 1.7 * HEX: h = 1.7 * HEX; w = ch.width * h / ch.height
    ims.append((t, k, ch.resize((int(w), int(h)), Image.LANCZOS)))
gap = 40; slot = [max(i.width, fp_w(k if k != "swarm" else 1), 1.35 * HEX) for t, k, i in ims]
Wd = int(sum(slot) + gap * (len(ims) + 1)); top = 90; tallest = max(i.height for *_, i in ims)
base = top + tallest; Hd = int(base + 1.8 * HEX * SQ + 90)
S = Image.new("RGB", (Wd, Hd), (246, 238, 224)); d = ImageDraw.Draw(S)
f = ImageFont.truetype("georgiab.ttf", 34); g = ImageFont.truetype("georgiab.ttf", 22)
d.text((24, 22), "Verdant Hollow enemies at in-game relative sizes, each on its board footprint", fill=(59, 28, 38), font=f)
x = gap
for (t, k, im), sw in zip(ims, slot):
    cx = x + sw / 2
    for ox, oy in cells(1 if k == "swarm" else k):
        pts = [(cx + ox + R * math.cos(math.radians(a)), base + (oy + R * math.sin(math.radians(a))) * SQ) for a in range(0, 360, 60)]
        d.polygon(pts, fill=(226, 216, 190), outline=(150, 120, 110))
    foot = base + {7: 0.35, 3: 0.3}.get(k, 0.15) * HEX * SQ
    S.paste(im, (int(cx - im.width / 2), int(foot - im.height)), im)
    tw = d.textlength(labels[t], font=g); d.text((cx - tw / 2, Hd - 50), labels[t], fill=(59, 28, 38), font=g)
    x += sw + gap
S.save(FE / "hollow_enemies_lineup.png"); print(S.size)
