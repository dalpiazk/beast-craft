# Skill-icon batch (2026-09-27): pick the reroll seeds, write the icons, and the before/after sheets.
"""python reroll.py pick                -> icons + picks.json for batch.REROLL (overrides.json respected)
   python reroll.py sheet NAME ID...    -> icons/icons_reroll_<name>.png (before | after, framed 96 + 64, plus every seed)"""
import os, sys, json, pathlib
HERE = pathlib.Path(__file__).resolve().parent
HOLLOW = pathlib.Path(os.environ["ARTLAB_HOLLOW"])                       # the Hollow art work folder
ROOT = pathlib.Path(os.environ.get("ARTLAB_ICONS", str(HOLLOW / "icons")))   # the icon batch folder
sys.path.insert(0, str(HERE)); sys.path.insert(1, str(HERE))
from PIL import Image, ImageDraw
import batch, review
import icon_sheet as IS

WK = ROOT / "work"; BEF = WK / "before"


def pick():
    pk = json.loads((WK / "picks.json").read_text()); over = json.loads((WK / "overrides.json").read_text()) if (WK / "overrides.json").exists() else {}
    for i in batch.REROLL:
        sc = {sd: batch.readability(i, sd) for sd in batch.seeds(i) if (batch.RAW / f"{i}_s{sd}.png").exists()}
        if len(sc) < len(batch.seeds(i)):
            print("reroll seeds incomplete for", i, len(sc)); continue
        best = int(over[i]) if str(over.get(i, "")).isdigit() and int(over[i]) in sc else max(sc, key=lambda s: sc[s][0])
        sc[best][3].save(ROOT / batch.SK[i]["file"])
        if not pk[i].get("reroll"):
            pk[i]["before_seed"] = pk[i]["seed"]
        pk[i].update(seed=best, reroll=True,
                     scores={str(s): list(v[:3]) for s, v in sc.items()}, override=i in over, motif=batch.SK[i]["words"], primitive=batch.SK[i]["prim"])
    (WK / "picks.json").write_text(json.dumps(pk, indent=1))


def sheet(name, ids):
    fr, rings = review.load_ui(); pk = json.loads((WK / "picks.json").read_text())
    rh = 150; W = 1180
    sh = Image.new("RGB", (W, 110 + len(ids) * rh + 20), review.BG); d = ImageDraw.Draw(sh)
    d.text((20, 16), f"Skill icons: reroll {name} (before -> after)", fill=review.PLUM, font=IS.font(30, True))
    d.text((20, 58), "Treatment B + Frame 1 at 96 px and 64 px. Right: all 3 reroll seeds at 64 px (the pick is boxed).", fill=(90, 60, 60), font=IS.font(15))
    d.rectangle([14, 96, W - 14, 100 + len(ids) * rh + 6], fill=review.DARK)
    for r, i in enumerate(ids):
        s = batch.SK[i]; y = 110 + r * rh; ring = rings[s["rarity"]]
        d.text((26, y + 8), s["name"], fill=(240, 230, 215), font=IS.font(18, True))
        d.text((26, y + 34), (s["words"][:52] + ("..." if len(s["words"]) > 52 else "")), fill=(160, 150, 145), font=IS.font(12))
        x = 330
        if (BEF / f"{i}.png").exists():
            b = Image.open(BEF / f"{i}.png")
            for sz, xo, yo in ((96, 0, 0), (64, 104, 16)):
                c = IS.compose(b, fr, ring, sz); sh.paste(c, (x + xo, y + yo), c)
        d.text((x + 50, y + 102), "before", fill=(160, 150, 145), font=IS.font(13))
        d.text((x + 186, y + 34), "->", fill=(255, 210, 120), font=IS.font(26, True))
        x = 560; a = Image.open(ROOT / s["file"])
        for sz, xo, yo in ((96, 0, 0), (64, 104, 16)):
            c = IS.compose(a, fr, ring, sz); sh.paste(c, (x + xo, y + yo), c)
        d.text((x + 50, y + 102), f"after (s{pk[i]['seed']})", fill=(255, 210, 120), font=IS.font(13))
        for k, sd in enumerate(batch.seeds(i)):
            f = batch.RAW / f"{i}_s{sd}.png"
            if not f.exists():
                continue
            ic = IS.icon256_img(Image.open(f)); c = IS.compose(ic, fr, ring, 64); xx = 820 + k * 110
            sh.paste(c, (xx, y + 16), c)
            if sd == pk[i]["seed"]:
                d.rectangle([xx - 4, y + 12, xx + 68, y + 84], outline=(255, 210, 90), width=2)
            d.text((xx + 8, y + 90), f"s{sd}", fill=(170, 160, 150), font=IS.font(12))
    out = ROOT / f"icons_reroll_{name.lower()}.png"; sh.save(out); return out


if __name__ == "__main__":
    if sys.argv[1] == "pick":
        pick()
    elif sys.argv[1] == "sheet":
        print(sheet(sys.argv[2], sys.argv[3:]))
