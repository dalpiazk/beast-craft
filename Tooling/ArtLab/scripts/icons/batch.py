# Skill-icon batch (2026-09-27): Treatment B exactly as the style test (template, img2img 0.72, IP 0.35, trio refs,
# 1024, 20 steps, CFG 5, the B post-pass), 2-3 seeds per skill, auto-pick by 64 px readability, slot-spec outputs.
"""python batch.py layouts [GROUP]      -> work/layouts/<id>.png + work/layouts/<group>.jpg
   python batch.py tok
   python batch.py todo GROUP            prints the pending gen tags
   python batch.py gen TAG...            TAG = <id>@<seed>  (exit 4 = recycle every 2 images, 3 = NaN)
   python batch.py pick GROUP            score seeds, write the 256 icons + picks.json + a seeds contact sheet"""
import os, sys, json, pathlib
HERE = pathlib.Path(__file__).resolve().parent
HOLLOW = pathlib.Path(os.environ["ARTLAB_HOLLOW"])                       # the Hollow art work folder
ROOT = pathlib.Path(os.environ.get("ARTLAB_ICONS", str(HOLLOW / "icons")))   # the icon batch folder
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"; sys.dont_write_bytecode = True
sys.path.insert(0, str(HERE)); sys.path.insert(1, str(HERE))
import numpy as np
from PIL import Image, ImageDraw
import briefs, motifs

WK = ROOT / "work"; LAY = WK / "layouts"; RAW = WK / "raw"
for p in (LAY, RAW):
    p.mkdir(parents=True, exist_ok=True)
SK = {s["id"]: s for s in briefs.load()}
HEAD = "no humans, game skill icon, centered, "
TAIL = ", bold, high contrast, vivid colors, strong glow, rim light, dark background, painterly, masterpiece"   # Treatment B
NEG = ("text, letters, words, watermark, signature, frame, border, ui, person, face, character, animal, multiple objects, "
       "lowres, worst quality, low quality, blurry, 3d, photo")
XNEG = {"radiant_bolt": ", starburst, explosion", "deep_shell": ", bubble, ring", "judgment": ", person, statue"}
IDX = {k: i for i, k in enumerate(SK)}
EXTRA = {"radiant_bolt", "deep_shell"}                 # the style test's weak spots: 3 seeds
REROLL = ["firestorm", "flame_wave", "granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin", "serpent_bite", "undertow", "thorn_lash", "spore_cloud", "sunder", "shrapnel_burst", "thunderclap", "plasma_barrage", "thunder_talons", "chain_lightning", "static_charge", "storm_dive", "wind_lance", "updraft", "sky_rend", "radiant_bolt", "sacred_spring", "eclipse_fang", "predator_focus", "rallying_cry", "mending_light", "battle_focus", "battle_hymn", "vengeance", "withering_curse", "giant_crush", "brute_smash", "champion_cleave"]   # producer / review rerolls
XNEG["mending_light"] = ", fire, flame, orange, hands"
XNEG["granite_bulwark"] = ", face, eyes, golem, altar, pedestal, rainbow"
XNEG.update({k: ", rainbow, multicolored" for k in ("granite_bulwark", "stone_challenge", "tectonic_shove", "stoneskin", "sunder", "shrapnel_burst") if k not in XNEG})


REROLL_B = ["granite_bulwark", "thorn_lash", "sunder", "sacred_spring", "mending_light", "withering_curse"]                          # second reroll (no seed of the first showed the slab ring)


def seeds(i):
    if i in REROLL_B:
        return [11000 + IDX[i], 12000 + IDX[i], 13000 + IDX[i]]
    if i in REROLL:
        return [8000 + IDX[i], 9000 + IDX[i], 10000 + IDX[i]]
    base = [5000 + IDX[i], 6000 + IDX[i]]
    return base + [7000 + IDX[i]] if i in EXTRA else base


def prompt(i):
    return HEAD + SK[i]["words"] + TAIL


def neg(i):
    return NEG + XNEG.get(i, "")


def make_layout(i):
    s = SK[i]
    return motifs.layout(s["prim"], briefs.palette(s["pal"]), 40 + IDX[i], soft=s["kind"] == "passive",
                         haze=s["kind"] == "enemy", **s["kw"])


def ids(group):
    return [k for k, s in SK.items() if s["group"] == group]


def gen_main(tags):
    todo = [t for t in tags if not (RAW / f"{t.replace('@', '_s')}.png").exists()]
    if not todo:
        return 0
    os.environ.setdefault("ARTLAB_OUT", str(WK / "lab")); os.environ.setdefault("ARTLAB_FINALS", str(HOLLOW.parent / "ai-art-final"))
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "seven"))   # read-only import
    from common import load_pipe, style_only, style_embeds, gen, Timer   # noqa
    from torch.nn.attention import sdpa_kernel, SDPBackend
    with Timer() as t:
        pipe = load_pipe("img2img", ipa=True); pipe.vae.disable_tiling(); style_only(pipe, 0.35); emb = style_embeds(pipe)
    print(f"loaded {t.dt:.0f}s", flush=True)
    done = 0
    for tag in todo:
        if done >= 2:
            print("recycle", flush=True); return 4
        i, sd = tag.split("@"); sd = int(sd)
        kw = dict(image=Image.open(LAY / f"{i}.png").convert("RGB"), strength=0.72, prompt=prompt(i), negative_prompt=neg(i),
                  num_inference_steps=20, guidance_scale=5.0, generator=gen(sd), ip_adapter_image_embeds=emb)
        ok = False
        for a in range(2):
            with Timer() as t, sdpa_kernel(SDPBackend.MATH):
                im = pipe(**kw).images[0]
            arr = np.asarray(im)
            if arr.max() > 8 and arr.std() > 3:
                ok = True; break
            print("NaN/black retry", tag, flush=True); kw["generator"] = gen(sd)
        if not ok:
            return 3
        im.save(RAW / f"{i}_s{sd}.png"); done += 1
        fp = RAW / "log.json"; L = json.loads(fp.read_text()) if fp.exists() else {}
        L[f"{i}_s{sd}"] = dict(id=i, seed=sd, steps=20, cfg=5.0, size=1024, strength=0.72, ipa=0.35, layout=f"layouts/{i}.png",
                               prompt=prompt(i), neg=neg(i), refs="phoenix/golem/kirin finals, equal (InstantStyle up.block_0)", s=round(t.dt, 1))
        fp.write_text(json.dumps(L, indent=1))
        print(tag, f"{t.dt:.1f}s", flush=True)
    return 0


def readability(i, sd):
    """64 px test: motif fidelity to the layout (correlation of the 64 px luminance inside the circle) and
    figure/ground contrast (luminance std inside the inner circle)."""
    import icon_sheet as IS
    ic = IS.icon256_img(Image.open(RAW / f"{i}_s{sd}.png"))
    g = np.asarray(IS.down(ic, 64).convert("L")).astype(np.float32)
    lay = np.asarray(make_layout(i).resize((64, 64), Image.LANCZOS).convert("L")).astype(np.float32)
    yy, xx = np.mgrid[0:64, 0:64]; m = np.hypot(xx - 31.5, yy - 31.5) < 26
    fid = float(np.corrcoef(g[m], lay[m])[0, 1]); con = float(g[m].std()) / 64
    return round(0.6 * fid + 0.4 * min(con, 1.0), 3), round(fid, 3), round(con, 3), ic


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "layouts":
        grp = sys.argv[2:] or briefs.GROUPS
        for g in grp:
            L = []
            for i in ids(g):
                im = make_layout(i); im.save(LAY / f"{i}.png"); L.append(im.resize((160, 160)))
            sh = Image.new("RGB", (160 * len(L), 160))
            for k, im in enumerate(L):
                sh.paste(im, (160 * k, 0))
            sh.save(LAY / f"_{g}.jpg")
    elif cmd == "tok":
        sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "seven"))
        os.environ.setdefault("ARTLAB_OUT", str(WK / "lab"))
        from transformers import CLIPTokenizer
        import common
        tok = CLIPTokenizer.from_pretrained(common.BASE, subfolder="tokenizer")
        n = {i: len(tok(prompt(i)).input_ids) for i in SK}; bad = {k: v for k, v in n.items() if v > 77}
        print("skills", len(SK), "max", max(n.values()), "neg max", max(len(tok(neg(i)).input_ids) for i in SK), "over", bad)
    elif cmd == "todo":
        print(" ".join(f"{i}@{s}" for g in sys.argv[2:] for i in ids(g) for s in seeds(i)))
    elif cmd == "gen":
        sys.exit(gen_main(sys.argv[2:]))
    elif cmd == "pick":
        import icon_sheet as IS
        OUT = ROOT
        pk = json.loads((WK / "picks.json").read_text()) if (WK / "picks.json").exists() else {}
        over = json.loads((WK / "overrides.json").read_text()) if (WK / "overrides.json").exists() else {}
        for g in sys.argv[2:]:
            rows = []
            for i in ids(g):
                sc = {sd: readability(i, sd) for sd in seeds(i) if (RAW / f"{i}_s{sd}.png").exists()}
                best = int(over.get(i) or max(sc, key=lambda s: sc[s][0]))
                ic = sc[best][3]; f = OUT / SK[i]["file"]; f.parent.mkdir(parents=True, exist_ok=True); ic.save(f)
                pk[i] = dict(seed=best, file=SK[i]["file"], artkey=SK[i]["artkey"], name=SK[i]["name"], group=g,
                             scores={str(s): list(v[:3]) for s, v in sc.items()}, override=i in over,
                             motif=SK[i]["words"], primitive=SK[i]["prim"])
                rows.append((i, sc, best))
            # seeds contact sheet (internal review): every seed at 128 + 64, the pick boxed
            W = 60 + max(len(r[1]) for r in rows) * 210; sh = Image.new("RGB", (W + 150, 20 + len(rows) * 150), (34, 27, 34)); d = ImageDraw.Draw(sh)
            for r, (i, sc, best) in enumerate(rows):
                d.text((6, 20 + r * 150 + 60), i[:20], fill=(230, 220, 210))
                for c, (sd, v) in enumerate(sc.items()):
                    x = 150 + c * 210; y = 20 + r * 150
                    sh.paste(IS.down(v[3], 128), (x, y), IS.down(v[3], 128)); sh.paste(IS.down(v[3], 64), (x + 134, y + 32), IS.down(v[3], 64))
                    d.text((x, y + 130), f"s{sd} {v[0]:.2f} (fid {v[1]:.2f} con {v[2]:.2f})", fill=(255, 220, 120) if sd == best else (170, 160, 150))
                    if sd == best:
                        d.rectangle([x - 3, y - 3, x + 131, y + 131], outline=(255, 210, 90), width=3)
            sh.save(WK / f"seeds_{g}.png")
        (WK / "picks.json").write_text(json.dumps(pk, indent=1))
        print("picked", sum(1 for v in pk.values()), "total")
