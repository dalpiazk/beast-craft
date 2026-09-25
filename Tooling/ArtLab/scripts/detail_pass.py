"""Reusable detail / upscale pass: Lanczos upscale, then SDXL tiled img2img at low denoise to add fine painterly
detail without changing the design. Tiles overlap and are blended with linear feather ramps (no visible seams).

  python detail_pass.py IN.png OUT.png [--scale 2] [--strength 0.3] [--tile 1024] [--overlap 256]
                        [--steps 30] [--cfg 5] [--seed 7] [--prompt "..."] [--mask char_mask.png]
                        [--cn 0.0]

--mask     optional character mask (white = character, at IN or OUT size). Detail is composited only inside the
           (feathered) mask; the background keeps the plain Lanczos upscale. Tiles that do not touch the mask are skipped.
--tile-cn  tile-ControlNet weight (xinsir/controlnet-tile-sdxl-1.0, control = the tile itself). Lets the
           denoise go to 0.4-0.55 so the model repaints texture while the tile CN holds shapes and colours.
--cn       optional canny-ControlNet weight (e.g. 0.5) computed per tile from the upscaled input, to pin lines
           when using a higher strength. 0 = plain img2img (default).
Effective denoise steps = steps * strength (30 * 0.3 = 9).
"""
import argparse
from common import *
import cv2

# the finals' detail prompt (phoenix, kirin); the golem used it with 'majestic' -> 'cute' (its personality word)
DETAIL_PROMPT = ("no humans, original creature, majestic, painterly, visible brush strokes, soft cel shading, warm light, rim light, "
                 "fine texture, bold clean lineart, masterpiece, high score, absurdres")
DETAIL_NEG = ("flat colors, vector art, sticker, glossy, lowres, blurry, jpeg artifacts, text, watermark, signature, extra eyes, "
              "realistic, photo, 3d, worst quality, low quality, violet, purple, cyan, magenta")


def positions(total, tile, overlap):
    if total <= tile:
        return [0]
    n = int(np.ceil((total - overlap) / (tile - overlap)))
    return [round(i * (total - tile) / (n - 1)) for i in range(n)]


def ramp(n, ov):
    r = np.ones(n, np.float32)
    k = np.linspace(0, 1, ov + 2, dtype=np.float32)[1:-1]
    r[:ov] = k; r[-ov:] = k[::-1]
    return r


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("inp"); ap.add_argument("out")
    ap.add_argument("--scale", type=float, default=2.0); ap.add_argument("--strength", type=float, default=0.3)
    ap.add_argument("--tile", type=int, default=1024); ap.add_argument("--overlap", type=int, default=256)
    ap.add_argument("--steps", type=int, default=30); ap.add_argument("--cfg", type=float, default=5.0)
    ap.add_argument("--seed", type=int, default=7); ap.add_argument("--prompt", default=DETAIL_PROMPT)
    ap.add_argument("--neg", default=DETAIL_NEG); ap.add_argument("--mask"); ap.add_argument("--cn", type=float, default=0.0)
    ap.add_argument("--tile-cn", type=float, default=0.0); ap.add_argument("--cn-end", type=float, default=1.0)
    a = ap.parse_args()

    src = Image.open(a.inp).convert("RGB")
    tw, th = (round(src.width * a.scale / 8) * 8, round(src.height * a.scale / 8) * 8)
    up = src.resize((tw, th), Image.LANCZOS)
    upa = np.asarray(up).astype(np.float32)
    mask = None
    if a.mask:
        m = np.asarray(Image.open(a.mask).convert("L").resize((tw, th), Image.BILINEAR)).astype(np.float32) / 255
        m = cv2.dilate(m, np.ones((9, 9), np.uint8))
        mask = cv2.GaussianBlur(m, (0, 0), 6)[..., None]

    with Timer() as tl:
        pipe = load_pipe("cn_img2img", cn="tile") if a.tile_cn > 0 else load_pipe("cn_img2img" if a.cn > 0 else "img2img")
    print(f"load {tl.dt:.1f}s", flush=True)
    acc = np.zeros_like(upa); wsum = np.zeros(upa.shape[:2] + (1,), np.float32)
    xs, ys = positions(tw, a.tile, a.overlap), positions(th, a.tile, a.overlap)
    tiles_s = []
    for y in ys:
        for x in xs:
            tw_, th_ = min(a.tile, tw), min(a.tile, th)
            box = (x, y, x + tw_, y + th_)
            crop = up.crop(box)
            if mask is not None and mask[y:y + th_, x:x + tw_].max() < 0.05:
                out_t = np.asarray(crop).astype(np.float32)
            else:
                args = dict(prompt=a.prompt, negative_prompt=a.neg, image=crop, strength=a.strength,
                            num_inference_steps=a.steps, guidance_scale=a.cfg, generator=gen(a.seed))
                if a.tile_cn > 0:
                    args.update(control_image=crop, controlnet_conditioning_scale=a.tile_cn, control_guidance_end=a.cn_end)
                elif a.cn > 0:
                    g = cv2.cvtColor(np.asarray(crop), cv2.COLOR_RGB2GRAY)
                    e = cv2.Canny(cv2.GaussianBlur(g, (3, 3), 0), 60, 140)
                    args.update(control_image=Image.fromarray(e).convert("RGB"), controlnet_conditioning_scale=a.cn)
                with Timer() as t:
                    for attempt in range(5):          # XPU NaN/black tiles: retry the same tile
                        out_t = np.asarray(pipe(**args).images[0]).astype(np.float32)
                        if out_t.max() > 8:
                            break
                        print("NaN/black tile, retrying", box, flush=True)
                        empty_cache()
                    else:
                        raise SystemExit("tile stayed black after 5 tries: rerun in a fresh process")
                tiles_s.append(round(t.dt, 1)); print(f"tile {box} {t.dt:.1f}s", flush=True)
            # feather only on edges shared with a neighbour tile
            ry = ramp(th_, a.overlap if len(ys) > 1 else 1); rx = ramp(tw_, a.overlap if len(xs) > 1 else 1)
            if y == ys[0]: ry[:a.overlap] = 1
            if y == ys[-1]: ry[-a.overlap:] = 1
            if x == xs[0]: rx[:a.overlap] = 1
            if x == xs[-1]: rx[-a.overlap:] = 1
            w = (ry[:, None] * rx[None, :])[..., None]
            acc[y:y + th_, x:x + tw_] += out_t * w; wsum[y:y + th_, x:x + tw_] += w
    res = acc / np.maximum(wsum, 1e-6)
    if mask is not None:
        res = res * mask + upa * (1 - mask)
    Image.fromarray(res.clip(0, 255).round().astype(np.uint8)).save(a.out)
    Image.fromarray(upa.astype(np.uint8)).save(str(a.out).replace(".png", "_lanczos.png"))
    info = dict(inp=str(a.inp), size=[tw, th], tiles=len(xs) * len(ys), tile_s=tiles_s, total_s=round(sum(tiles_s), 1),
                strength=a.strength, steps=a.steps, cfg=a.cfg, seed=a.seed, cn=a.cn, tile_cn=a.tile_cn, prompt=a.prompt)
    pathlib.Path(str(a.out).replace(".png", "_log.json")).write_text(json.dumps(info, indent=1))
    print(json.dumps(info))


if __name__ == "__main__":
    main()
