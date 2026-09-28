# Skill-icon style test (2026-09-27): motif layouts, img2img per treatment (A soft painterly, B bold glow, C flat+texture).
"""python icons.py layouts          -> work/icons/layouts/{skill}_{soft|flat}.png (1024x1024)
   python icons.py tok
   python icons.py gen TAG...       TAG = {skill}_{A|B|C}  -> work/icons/raw/{TAG}.png  (exit 4 = recycle, 3 = NaN)"""
import os, sys, json, math, pathlib, random
HERE = pathlib.Path(__file__).resolve().parent; ROOT = pathlib.Path(os.environ["ARTLAB_HOLLOW"])   # the Hollow art work folder
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"; sys.dont_write_bytecode = True
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 1024
IW = ROOT / "work" / "icons"; LAY = IW / "layouts"; RAW = IW / "raw"
for p in (LAY, RAW):
    p.mkdir(parents=True, exist_ok=True)

# skill -> (element, bg inner, bg outer, motif words)
SK = {
    "ember_shot":      ("Fire", (250, 150, 60), (110, 25, 20), "a single blazing fireball comet streaking diagonally, flame tail"),
    "boulder_slam":    ("Earth", (190, 150, 95), (90, 62, 38), "one big solid grey stone boulder with a moss cap, smashing onto cracked ground"),
    "radiant_bolt":    ("Light", (255, 245, 205), (200, 150, 60), "a single spear of radiant golden light, bright star burst"),
    "deep_shell":      ("Water", (110, 190, 190), (22, 72, 96), "a closed scallop seashell with a shimmering water bubble shield"),
    "chain_lightning": ("Lightning", (120, 140, 190), (36, 42, 70), "a forked yellow lightning bolt chaining in three arcs"),
    "petrifying_gaze": ("Dark", (120, 80, 140), (38, 22, 52), "a single glowing golden reptile eye, slit pupil, stone cracks"),
    "rime_bolt":       ("Ice", (205, 235, 248), (70, 120, 165), "a sharp pale blue ice crystal shard, frost sparkles"),
    "aegis":           ("Avatar", (255, 235, 180), (170, 120, 60), "one golden heraldic shield in front of a glowing round dome ward, warm light"),
}
ORDER = list(SK)

HEAD = "no humans, game skill icon, centered, "
TR = {
    "A": dict(tail=", painterly, soft brush strokes, warm light, soft cel shading, hand-painted, simple background, masterpiece",
              strength=0.72, ipa=0.45, layout="soft"),
    "B": dict(tail=", bold, high contrast, vivid colors, strong glow, rim light, dark background, painterly, masterpiece",
              strength=0.72, ipa=0.35, layout="soft"),
    "C": dict(tail=", flat graphic emblem, simple bold shape, clean shapes, painterly texture, paper grain, masterpiece",
              strength=0.45, ipa=0.40, layout="flat"),
}
NEG = ("text, letters, words, watermark, signature, frame, border, ui, person, face, character, animal, multiple objects, "
       "lowres, worst quality, low quality, blurry, 3d, photo")


def prompt(skill, t):
    return HEAD + SK[skill][3] + TR[t]["tail"]


# ---------------- layouts ----------------
def radial_bg(c_in, c_out, cx=0.5, cy=0.45):
    yy, xx = np.mgrid[0:S, 0:S] / S
    r = np.clip(np.hypot(xx - cx, yy - cy) / 0.72, 0, 1)[..., None]
    return (np.array(c_in) * (1 - r) + np.array(c_out) * r)


def poly(d, pts, fill):
    d.polygon([(x * S, y * S) for x, y in pts], fill=fill)


def ell(d, cx, cy, rx, ry, fill):
    d.ellipse([(cx - rx) * S, (cy - ry) * S, (cx + rx) * S, (cy + ry) * S], fill=fill)


def motif(skill, d, rng):
    if skill == "ember_shot":
        for k, (c, w) in enumerate([((230, 70, 30), 0.17), ((255, 150, 50), 0.12), ((255, 230, 150), 0.07)]):
            poly(d, [(0.12, 0.88 - k * .02), (0.58 - w * .4, 0.42 - w * .8), (0.58 + w * .8, 0.42 + w * .4)], c)
            ell(d, 0.62, 0.38, w + 0.05, w + 0.05, c)
    elif skill == "boulder_slam":
        poly(d, [(0.2, 0.78), (0.8, 0.78), (0.88, 0.9), (0.12, 0.9)], (120, 90, 55))
        for a in (-0.5, -0.2, 0.2, 0.5):
            d.line([(0.5 * S, 0.8 * S), ((0.5 + a) * S, 0.95 * S)], fill=(60, 40, 25), width=14)
        pts = [(0.5 + 0.3 * math.cos(t) * rng.uniform(0.85, 1.05), 0.45 + 0.27 * math.sin(t) * rng.uniform(0.85, 1.05))
               for t in np.linspace(0, 2 * math.pi, 11)[:-1]]
        poly(d, pts, (150, 142, 128))
        poly(d, [(0.3, 0.3), (0.62, 0.22), (0.72, 0.36), (0.45, 0.4)], (178, 170, 152))
        ell(d, 0.44, 0.24, 0.14, 0.06, (104, 140, 70))
        for x in (0.14, 0.86):
            ell(d, x, 0.74, 0.07, 0.05, (215, 190, 150))
    elif skill == "radiant_bolt":
        for a in range(8):
            t = a * math.pi / 4; L = 0.42 if a % 2 == 0 else 0.26
            poly(d, [(0.5 + L * math.cos(t), 0.5 + L * math.sin(t)), (0.5 + 0.05 * math.cos(t + 1.57), 0.5 + 0.05 * math.sin(t + 1.57)),
                     (0.5 + 0.05 * math.cos(t - 1.57), 0.5 + 0.05 * math.sin(t - 1.57))], (255, 236, 160))
        poly(d, [(0.18, 0.82), (0.8, 0.2), (0.84, 0.24), (0.22, 0.86)], (255, 255, 240))
        ell(d, 0.5, 0.5, 0.1, 0.1, (255, 255, 250))
    elif skill == "deep_shell":
        ell(d, 0.5, 0.5, 0.4, 0.4, (150, 220, 225))
        ell(d, 0.5, 0.5, 0.36, 0.36, (70, 150, 170))
        pts = [(0.5 + 0.28 * math.cos(t), 0.56 - 0.26 * abs(math.sin(t)) ** 0.8) for t in np.linspace(0, math.pi, 12)]
        pts = [(0.22, 0.6)] + [(0.5 - 0.28 * math.cos(t), 0.6 - 0.3 * math.sin(t)) for t in np.linspace(0, math.pi, 12)] + [(0.78, 0.6), (0.56, 0.74), (0.44, 0.74)]
        poly(d, pts, (240, 225, 205))
        for k in range(-3, 4):
            d.line([(0.5 * S, 0.72 * S), ((0.5 + k * 0.075) * S, (0.33 + abs(k) * 0.03) * S)], fill=(210, 170, 140), width=10)
        ell(d, 0.36, 0.3, 0.05, 0.03, (235, 255, 255))
    elif skill == "chain_lightning":
        pts = [(0.3, 0.1), (0.52, 0.1), (0.44, 0.38), (0.6, 0.38), (0.34, 0.9), (0.4, 0.52), (0.26, 0.52)]
        poly(d, pts, (255, 222, 70))
        for (x0, y0, x1, y1) in ((0.5, 0.45, 0.82, 0.3), (0.45, 0.62, 0.8, 0.72)):
            mx, my = (x0 + x1) / 2, (y0 + y1) / 2 + 0.05
            d.line([(x0 * S, y0 * S), (mx * S, my * S), (x1 * S, y1 * S)], fill=(255, 235, 130), width=22)
            ell(d, x1, y1, 0.04, 0.04, (255, 250, 200))
    elif skill == "petrifying_gaze":
        pts = [(0.5 + 0.42 * math.cos(t), 0.5 + 0.24 * math.sin(t) * abs(math.sin(t)) ** 0.2) for t in np.linspace(0, 2 * math.pi, 40)]
        poly(d, pts, (220, 205, 175))
        ell(d, 0.5, 0.5, 0.19, 0.19, (245, 185, 55))
        ell(d, 0.5, 0.5, 0.13, 0.13, (255, 220, 110))
        poly(d, [(0.5, 0.3), (0.54, 0.5), (0.5, 0.7), (0.46, 0.5)], (40, 20, 40))
        for a in (0.9, 2.2, 4.0, 5.3):
            d.line([((0.5 + 0.44 * math.cos(a)) * S, (0.5 + 0.44 * math.sin(a)) * S),
                    ((0.5 + 0.34 * math.cos(a + .2)) * S, (0.5 + 0.34 * math.sin(a + .2)) * S)], fill=(150, 145, 140), width=10)
    elif skill == "rime_bolt":
        poly(d, [(0.5, 0.08), (0.64, 0.42), (0.5, 0.92), (0.36, 0.42)], (230, 246, 255))
        poly(d, [(0.5, 0.08), (0.64, 0.42), (0.5, 0.92)], (160, 205, 235))
        for (x, y) in ((0.25, 0.28), (0.76, 0.66), (0.27, 0.72)):
            for t in (0, 1.05, 2.1):
                d.line([((x + 0.05 * math.cos(t)) * S, (y + 0.05 * math.sin(t)) * S),
                        ((x - 0.05 * math.cos(t)) * S, (y - 0.05 * math.sin(t)) * S)], fill=(245, 252, 255), width=8)
    elif skill == "aegis":
        ell(d, 0.5, 0.52, 0.4, 0.4, (255, 240, 200))
        ell(d, 0.5, 0.52, 0.33, 0.33, (225, 185, 95))
        pts = [(0.5, 0.2), (0.74, 0.3), (0.72, 0.56), (0.5, 0.82), (0.28, 0.56), (0.26, 0.3)]
        poly(d, pts, (250, 238, 205))
        poly(d, [(0.5, 0.28), (0.66, 0.35), (0.64, 0.55), (0.5, 0.72), (0.36, 0.55), (0.34, 0.35)], (205, 150, 70))
        ell(d, 0.5, 0.47, 0.06, 0.06, (255, 250, 230))


def layout(skill, flat):
    rng = random.Random(ORDER.index(skill) + 40); nrng = np.random.default_rng(ORDER.index(skill))
    el, cin, cout, _ = SK[skill]
    bg = radial_bg(cin, cout) if not flat else np.ones((S, S, 3)) * (np.array(cin) * 0.35 + np.array(cout) * 0.65)
    im = Image.fromarray(bg.astype(np.uint8)); d = ImageDraw.Draw(im)
    motif(skill, d, rng)
    if flat:
        a = np.asarray(im).astype(np.float32) + nrng.normal(0, 3, (S, S, 3))
    else:
        im = im.filter(ImageFilter.GaussianBlur(6))
        a = np.asarray(im).astype(np.float32) + nrng.normal(0, 6, (S, S, 3))
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


# ---------------- generation ----------------
def gen_main(tags):
    todo = [t for t in tags if not (RAW / f"{t}.png").exists()]
    if not todo:
        return 0
    os.environ.setdefault("ARTLAB_OUT", str(ROOT / "work" / "lab"))
    os.environ.setdefault("ARTLAB_FINALS", str(ROOT.parent / "ai-art-final"))
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "seven"))   # read-only import
    from common import load_pipe, style_only, style_embeds, gen, Timer   # noqa
    import torch
    from torch.nn.attention import sdpa_kernel, SDPBackend
    with Timer() as t:
        pipe = load_pipe("img2img", ipa=True); pipe.vae.disable_tiling()
    print(f"loaded {t.dt:.0f}s", flush=True)
    emb = style_embeds(pipe); done = 0
    for tag in todo:
        if done >= 2:
            print("recycle", flush=True); return 4
        skill, tr = tag.rsplit("_", 1); cfg = TR[tr]
        style_only(pipe, cfg["ipa"])
        seed = 900 + ORDER.index(skill)
        kw = dict(image=Image.open(LAY / f"{skill}_{cfg['layout']}.png").convert("RGB"), strength=cfg["strength"],
                  prompt=prompt(skill, tr), negative_prompt=NEG, num_inference_steps=20, guidance_scale=5.0,
                  generator=gen(seed), ip_adapter_image_embeds=emb)
        ok = False
        for a in range(2):
            with Timer() as t, sdpa_kernel(SDPBackend.MATH):
                im = pipe(**kw).images[0]
            arr = np.asarray(im)
            if arr.max() > 8 and arr.std() > 3:
                ok = True; break
            print("NaN/black retry", tag, flush=True); kw["generator"] = gen(seed)
        if not ok:
            return 3
        im.save(RAW / f"{tag}.png"); done += 1
        fp = RAW / "log.json"; L = json.loads(fp.read_text()) if fp.exists() else {}
        L[tag] = dict(seed=seed, steps=20, cfg=5.0, size=S, **{k: v for k, v in cfg.items() if k != "tail"},
                      prompt=prompt(skill, tr), neg=NEG, refs="phoenix/golem/kirin finals, equal", s=round(t.dt, 1))
        fp.write_text(json.dumps(L, indent=1))
        print(tag, f"{t.dt:.1f}s", flush=True)
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "layouts":
        for s in ORDER:
            layout(s, False).save(LAY / f"{s}_soft.png"); layout(s, True).save(LAY / f"{s}_flat.png")
        ims = [Image.open(LAY / f"{s}_{k}.png").resize((200, 200)) for k in ("soft", "flat") for s in ORDER]
        sh = Image.new("RGB", (1600, 400))
        for i, im in enumerate(ims):
            sh.paste(im, ((i % 8) * 200, (i // 8) * 200))
        sh.save(LAY / "layouts.jpg")
    elif cmd == "tok":
        sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent / "seven"))
        os.environ.setdefault("ARTLAB_OUT", str(ROOT / "work" / "lab"))
        from transformers import CLIPTokenizer
        import common
        tok = CLIPTokenizer.from_pretrained(common.BASE, subfolder="tokenizer")
        for s in ORDER:
            for t in TR:
                n = len(tok(prompt(s, t)).input_ids); assert n <= 77, (s, t, n)
        print("max", max(len(tok(prompt(s, t)).input_ids) for s in ORDER for t in TR), "neg", len(tok(NEG).input_ids))
    elif cmd == "gen":
        sys.exit(gen_main(sys.argv[2:]))
