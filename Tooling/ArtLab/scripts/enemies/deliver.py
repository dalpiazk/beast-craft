# Beast Craft ArtLab, Verdant Hollow enemy finals (2026-09-27): per-enemy review sheets and the accent masks not saved by a fix script.
"""Per-enemy deliverables: before/after sheet, anatomy close-up (counts as text + marked points), accent mask from
the final when not saved by the fix script, 64 px preview option, notes.md.
  python deliver.py TYPE   (reads deliver_specs.json)"""
import os, sys, json, pathlib
import numpy as np, cv2
from PIL import Image, ImageDraw, ImageFont
FE = pathlib.Path(os.environ.get("ARTLAB_ENEMIES") or pathlib.Path(__file__).resolve().parent)  # the nine enemy folders
t = sys.argv[1]; S = json.load(open(pathlib.Path(__file__).resolve().parent / "deliver_specs.json"))[t]
D = FE / t; W_, H_ = 896, 1152
f = ImageFont.truetype("georgiab.ttf", 26); g = ImageFont.truetype("georgiab.ttf", 20); h = ImageFont.truetype("georgia.ttf", 18)
fin = Image.open(D / f"{t}_final.png").convert("RGB")
cand = Image.open(D / "work" / "pick_candidate_sheet.png").convert("RGB")
# before/after
B = Image.new("RGB", (1440, 1010), (246, 238, 224)); d = ImageDraw.Draw(B)
B.paste(cand.resize((700, 900)), (10, 60)); B.paste(fin.resize((700, 900)), (730, 60))
d.text((10, 14), f"BEFORE: candidate {S['src']}", fill=(59, 28, 38), font=f); d.text((730, 14), "AFTER: final", fill=(59, 28, 38), font=f)
d.text((10, 970), S["ba_note"], fill=(59, 28, 38), font=h)
B.save(D / f"{t}_before_after.png")
# accent mask
am = D / "work" / "accent_mask.npy"
if not am.exists() or S.get("accent_from_final"):
    small = np.asarray(fin.resize((W_, H_), Image.LANCZOS))
    hsv = cv2.cvtColor(small, cv2.COLOR_RGB2HSV_FULL).astype(np.float32); hue = hsv[..., 0] * 360 / 256; sat = hsv[..., 1] / 255
    m = np.zeros((H_, W_), bool)
    for (x0, y0, x1, y1), (h0, h1), s0 in S["accent_boxes"]:
        box = np.zeros((H_, W_), bool); box[y0:y1, x0:x1] = True
        hh = (hue >= h0) & (hue <= h1) if h0 <= h1 else (hue >= h0) | (hue <= h1)
        m |= box & hh & (sat > s0) & (hsv[..., 2] > 40)
    m = cv2.morphologyEx(m.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8)).astype(bool)
    np.save(am, m)
m = np.load(am)
Image.fromarray((m * 255).astype(np.uint8)).save(D / "work" / "accent_mask.png")
small = np.asarray(fin.resize((W_, H_), Image.LANCZOS)).astype(np.float32)
ov = small.copy(); ov[m] = ov[m] * .35 + np.array([230, 60, 200]) * .65
P = Image.new("RGB", (2 * W_ + 30, H_ + 70), (246, 238, 224)); P.paste(Image.fromarray(small.astype(np.uint8)), (10, 60)); P.paste(Image.fromarray(ov.astype(np.uint8)), (W_ + 20, 60))
ImageDraw.Draw(P).text((10, 16), f"Element accent (magenta) = {S['accent']}", fill=(59, 28, 38), font=f)
P.save(D / f"{t}_accent_mask.png")
# anatomy close-up
x0, y0, x1, y1 = S["crop"]
c = fin.crop((2 * x0, 2 * y0, 2 * x1, 2 * y1)); sc = min(900 / c.width, 940 / c.height)
c = c.resize((int(c.width * sc), int(c.height * sc)))
A = Image.new("RGB", (c.width + 560, max(c.height + 70, 600)), (246, 238, 224)); A.paste(c, (0, 60)); d = ImageDraw.Draw(A)
d.text((10, 14), f"{t.capitalize()} anatomy check", fill=(59, 28, 38), font=f)
cols = {"eye": (200, 40, 30), "mouth": (200, 120, 30), "limb": (40, 140, 60), "item": (120, 60, 180)}
for kind, x, y in S["marks"]:
    px, py = int((2 * x - 2 * x0) * sc), int((2 * y - 2 * y0) * sc) + 60
    r = 24 if kind != "item" else 40
    d.ellipse([px - r, py - r, px + r, py + r], outline=cols[kind], width=4)
for i, line in enumerate(S["counts"]):
    d.text((c.width + 20, 100 + i * 34), line, fill=(59, 28, 38), font=g)
A.save(D / f"{t}_anatomy_check.png")
# 64 px preview
if S.get("preview64"):
    ch = Image.open(D / "rig" / "character.png").convert("RGBA")
    bg = Image.new("RGBA", ch.size, (246, 238, 224, 255)); bg.alpha_composite(ch); ch = bg.convert("RGB")
    ch.thumbnail((64, 64), Image.LANCZOS)
    Q = Image.new("RGB", (64 * 3 + 40 + 280, 64 * 3 + 60), (246, 238, 224)); d = ImageDraw.Draw(Q)
    Q.paste(ch, (10, 40)); Q.paste(ch.resize((ch.width * 3, ch.height * 3), Image.NEAREST), (90, 40))
    d.text((10, 10), f"{t}: 64 px board preview (left) and 3x nearest-neighbour zoom", fill=(59, 28, 38), font=h)
    ch.save(D / f"{t}_64px.png"); Q.save(D / f"{t}_64px_preview.png")
print("deliver ok", t)
