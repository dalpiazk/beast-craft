# Skill-icon batch (2026-09-27): final frame/ring sprites (Frame 1: bronze + coloured rings) and the review sheets.
"""python review.py frames            -> icons/content/art/ui/skill_icon/{frame,ring_<rarity>}.png (256, slot spec s.2)
   python review.py group GROUP...    -> icons/icons_review_<group>.png
   python review.py overview          -> icons/icons_all_overview.png"""
import os, sys, json, math, pathlib
HERE = pathlib.Path(__file__).resolve().parent
HOLLOW = pathlib.Path(os.environ["ARTLAB_HOLLOW"])                       # the Hollow art work folder
ROOT = pathlib.Path(os.environ.get("ARTLAB_ICONS", str(HOLLOW / "icons")))   # the icon batch folder
sys.path.insert(0, str(HERE)); sys.path.insert(1, str(HERE))
from PIL import Image, ImageDraw
import briefs
import icon_sheet as IS

UI = ROOT / "content/art/ui/skill_icon"
SK = {s["id"]: s for s in briefs.load()}
BG, DARK, PLUM = (250, 244, 232), (34, 27, 34), (59, 28, 38)


def frames():
    UI.mkdir(parents=True, exist_ok=True)
    IS.frame_bronze().save(UI / "frame.png")
    for r, c in IS.RARITY.items():
        IS.ring_plain(c).save(UI / f"ring_{r}.png")


def load_ui():
    return Image.open(UI / "frame.png"), {r: Image.open(UI / f"ring_{r}.png") for r in IS.RARITY}


def wrap(text, fnt, width, d):
    words, lines, cur = text.split(), [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if d.textlength(t, font=fnt) <= width:
            cur = t
        else:
            lines.append(cur); cur = w
    return lines + [cur]


def group(g):
    fr, rings = load_ui(); pk = json.loads((ROOT / "work/picks.json").read_text())
    ids = [i for i, s in SK.items() if s["group"] == g]
    cols = 6; cw, ch = 230, 205; rows = math.ceil(len(ids) / cols)
    title = {"Avatar": "Avatar actives + passives", "Enemy": "Enemy skills (Gloam)"}.get(g, f"{g} skills")
    sh = Image.new("RGB", (cols * cw + 40, 110 + rows * ch + 20), BG); d = ImageDraw.Draw(sh)
    d.text((20, 16), f"Skill icons: {title}", fill=PLUM, font=IS.font(30, True))
    d.text((20, 58), "Treatment B + Frame 1 (bronze + rarity ring), at the in-game 96 px and 64 px (1:1). "
           "Ring: beast = common, avatar = rare, enemy = gloam. Seed picked by 64 px readability.", fill=(90, 60, 60), font=IS.font(15))
    d.rectangle([14, 96, cols * cw + 26, 100 + rows * ch + 6], fill=DARK)
    for k, i in enumerate(ids):
        s = SK[i]; x = 26 + (k % cols) * cw; y = 108 + (k // cols) * ch
        ic = Image.open(ROOT / s["file"])
        for sz, xo, yo in ((96, 6, 0), (64, 118, 16)):
            c = IS.compose(ic, fr, rings[s["rarity"]], sz); sh.paste(c, (x + xo, y + yo), c)
        tag = {"passive": " (passive)", "active": " (active)"}.get(s["kind"], "")
        for j, ln in enumerate(wrap(s["name"] + tag, IS.font(16, True), cw - 20, d)[:2]):
            d.text((x + 6, y + 104 + j * 19), ln, fill=(240, 230, 215), font=IS.font(16, True))
        d.text((x + 6, y + 146), f"{i[:26]}  s{pk[i]['seed']}", fill=(150, 140, 135), font=IS.font(12))
    out = ROOT / f"icons_review_{g.lower()}.png"; sh.save(out); return out


def overview():
    fr, rings = load_ui(); cols = 13; cw, ch = 100, 118
    rows_by = [(g, [i for i, s in SK.items() if s["group"] == g]) for g in briefs.GROUPS]
    nrows = sum(math.ceil(len(v) / cols) for _, v in rows_by)
    sh = Image.new("RGB", (150 + cols * cw + 20, 70 + nrows * ch + 20), DARK); d = ImageDraw.Draw(sh)
    d.text((16, 14), f"All {len(SK)} skill icons: Treatment B + Frame 1, at 64 px", fill=(240, 230, 215), font=IS.font(28, True))
    y = 70
    for g, ids in rows_by:
        d.text((16, y + 24), g, fill=(255, 210, 120), font=IS.font(18, True))
        for k, i in enumerate(ids):
            if k and k % cols == 0:
                y += ch
            s = SK[i]; x = 150 + (k % cols) * cw
            c = IS.compose(Image.open(ROOT / s["file"]), fr, rings[s["rarity"]], 64); sh.paste(c, (x + 18, y), c)
            nm = s["name"] if len(s["name"]) <= 14 else s["name"][:13] + "."
            d.text((x + 50 - d.textlength(nm, font=IS.font(12)) / 2, y + 70), nm, fill=(220, 210, 200), font=IS.font(12))
        y += ch
    out = ROOT / "icons_all_overview.png"; sh.save(out); return out


if __name__ == "__main__":
    c = sys.argv[1]
    if c == "frames":
        frames()
    elif c == "group":
        for g in sys.argv[2:]:
            print(group(g))
    elif c == "overview":
        print(overview())
