"""Per-beast parts sheet: every part on a checker (with its pivot), the recomposite, and a pose test
(head +12 deg, front limbs/wings -10 deg about their pivots) to show the occlusion fill.

  python parts_sheet.py BEAST...   -> $ARTLAB_OUT/rig/BEAST/parts_sheet.png"""
import sys
from common import *

def checker(w, h, c=24):
    yy, xx = np.mgrid[0:h, 0:w]
    a = np.where(((yy // c) + (xx // c)) % 2 == 0, 236, 214).astype(np.uint8)
    return Image.fromarray(np.dstack([a, a, a])).convert("RGBA")

def pose(meta, OD, rot):
    W2, H2 = meta["canvas"]
    comp = Image.new("RGBA", (W2, H2), BG + (255,))
    for k in meta["order_back_to_front"]:
        p = meta["parts"][k]; im = Image.open(OD / p["file"])
        lay = Image.new("RGBA", (W2, H2), (0, 0, 0, 0)); lay.alpha_composite(im, tuple(p["offset"]))
        if k in rot:
            lay = lay.rotate(rot[k], resample=Image.BICUBIC, center=tuple(p["pivot"]))
        comp.alpha_composite(lay)
    return comp

for b in sys.argv[1:]:
    OD = OUT / "rig" / b; meta = json.loads((OD / "parts.json").read_text())
    cells = []
    for k in meta["order_back_to_front"]:
        p = meta["parts"][k]; im = Image.open(OD / p["file"])
        c = checker(im.width + 40, im.height + 40); c.alpha_composite(im, (20, 20))
        d = ImageDraw.Draw(c); x, y = p["pivot_local"]; x += 20; y += 20
        d.ellipse([x - 12, y - 12, x + 12, y + 12], outline=(200, 30, 30), width=5)
        cells.append((c, k))
    ch = Image.open(OD / "character.png"); c = checker(ch.width, ch.height); c.alpha_composite(ch)
    cells.append((c, "character.png"))
    cells.append((Image.open(OD / "parts_recomposed.png"), "recomposed"))
    heads = {"phoenix": {"head": 12, "front_wing": -14, "back_wing": 10, "tail": -8},
             "kirin": {"head": 12, "horn": 0, "tail": -12, "legs_front": -10, "legs_back": 8},
             "golem": {"head": 12, "legs_front": -8, "legs_back": 8, "stones": 0}}[b]
    rot = dict(heads)
    if b == "kirin":   # horn follows the head
        rot["horn"] = heads["head"]; rot["mane"] = 0
    cells.append((pose(meta, OD, rot), "pose test (rotated about pivots)"))
    th = 360
    ims = [im.convert("RGB").resize((round(im.width * th / im.height), th), Image.LANCZOS) for im, _ in cells]
    wsum = sum(i.width for i in ims) + 12 * (len(ims) + 1)
    sheet = Image.new("RGB", (wsum, th + 80), BG); d = ImageDraw.Draw(sheet); x = 12
    d.text((12, 8), f"{b.capitalize()} rig parts (red ring = pivot; root pivot at the feet)", fill=(59, 28, 38), font=font(22))
    for im, (_, lab) in zip(ims, cells):
        sheet.paste(im, (x, 40)); d.text((x + 4, 44 + th), lab, fill=(59, 28, 38), font=font(16)); x += im.width + 12
    sheet.save(OD / "parts_sheet.png"); print("saved", OD / "parts_sheet.png")
