"""grade.py -- deterministic post-process colour grade, Pillow + numpy only (no models, no SDXL,
no network). Unifies independently-generated region backdrops under one shared "painted journal"
treatment so round-5's strong-identity, prompt-only images (see
`../../provenance/menu-backdrops.md`) read as one game instead of twelve different renders.

Producer decision: keep the prompt-only images (no IP-Adapter / style-ref conditioning -- that path
was tried and rejected, see the provenance doc) and unify them with a shared colour treatment applied
AFTERWARDS, rather than chasing a single prompt/IP-Adapter setup that holds both identity and style at
once (several rounds tried that and kept trading one for the other). This script is that treatment.

Pipeline (see grade_image() for the exact order):
  1. Lift blacks / compress highlights   -- soften contrast: nothing darker than roughly ink-plum's
                                             own luminance, highlights rolled off gently above a knee.
  2. Reduce saturation, with extra pull on extreme orange/green/electric-blue hues specifically (the
     exact hue bands the "fantasy poster" drift leaned on) -- NOT a uniform desaturation, so ember
     glow stays warm and lightning stays bright; only the most radioactive-looking extremes get
     pulled down harder.
  3. Split-tone / gradient-map colour grade toward the v2 palette (`texlib.py`): shadows pulled toward
     COOL_SHADOW, highlights toward APRICOT, midtones gently toward PAPER -- blended at moderate
     strength (not a full recolour; the graded image is still clearly the same photo/painting).
  4. Optional top haze (atmospheric perspective) so skies read airy instead of flat.
  5. A light painted-paper grain + watercolour-wash pooling, reusing `texlib.py`'s own generators, at
     low opacity.
  6. A very soft vignette.

DEPENDENCY NOTE: this script imports `texlib` (the shared palette/paper-texture module: INK_PLUM,
PAPER, APRICOT, COOL_SHADOW, paper_texture(), watercolor_wash()) from the ArtLab `scripts/` directory,
one level up from this file. `texlib.py` is not part of this commit -- it ships with the Journal/
Settings UI kit (issue #52) on this same branch. Until that lands, `python -m py_compile` on this file
still passes (compilation doesn't execute imports), but running it will raise `ModuleNotFoundError:
texlib` -- expected, not a bug in this commit.

Usage (ArtLab venv python -- this script has no SDXL/torch dependency, plain Pillow+numpy, so it's
safe/cheap to run directly, including in the foreground on a laptop GPU that must stay free):
  python grade.py <input.png> [input2.png ...] [--out-dir graded] [params...]
  python grade.py --help   # full parameter list with defaults

Output never overwrites the input: graded/<same filename> by default (graded/ is created on demand
next to wherever you run this from), or --out-dir to redirect.
"""
import argparse
import hashlib
import pathlib
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import texlib as tex  # noqa: E402  -- INK_PLUM, PAPER, APRICOT, COOL_SHADOW, paper_texture, watercolor_wash

HERE = pathlib.Path(__file__).resolve().parent
OUT_DIR_DEFAULT = HERE / "graded"


def hex_to_rgb01(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def luma_255(rgb):
    """Simple perceptual luma (display-space, not linear-light -- this is a levels/curves operation
    on the image's own 0-255 values, not a WCAG-style contrast calc, so no sRGB linearization)."""
    r, g, b = rgb
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


# ---------------------------------------------------------------- vectorized HSV (numpy, no deps)

def rgb_to_hsv(arr):
    """arr: float array (...,3) in 0..1. Returns (...,3) h,s,v each 0..1."""
    r, g, b = arr[..., 0], arr[..., 1], arr[..., 2]
    maxc = np.max(arr, axis=-1)
    minc = np.min(arr, axis=-1)
    v = maxc
    delta = maxc - minc
    s = np.where(maxc <= 1e-8, 0.0, delta / np.where(maxc <= 1e-8, 1.0, maxc))
    delta_safe = np.where(delta <= 1e-8, 1.0, delta)
    rc = (maxc - r) / delta_safe
    gc = (maxc - g) / delta_safe
    bc = (maxc - b) / delta_safe
    h = np.zeros_like(maxc)
    h = np.where(maxc == r, (bc - gc), h)
    h = np.where(maxc == g, 2.0 + rc - bc, h)
    h = np.where(maxc == b, 4.0 + gc - rc, h)
    h = (h / 6.0) % 1.0
    h = np.where(delta <= 1e-8, 0.0, h)
    return np.stack([h, s, v], axis=-1)


def hsv_to_rgb(arr):
    h, s, v = arr[..., 0], arr[..., 1], arr[..., 2]
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = v * (1.0 - s)
    q = v * (1.0 - s * f)
    t = v * (1.0 - s * (1.0 - f))
    i = i.astype(np.int64) % 6
    conds = [i == k for k in range(6)]
    r = np.select(conds, [v, q, p, p, t, v])
    g = np.select(conds, [t, v, v, q, p, p])
    b = np.select(conds, [p, p, t, v, v, q])
    return np.stack([r, g, b], axis=-1)


# ---------------------------------------------------------------- grade stages

def lift_and_compress(arr, black_floor, knee, highlight_scale):
    """Lift: raise the floor of the tonal range to black_floor (0..1) uniformly per channel (no
    colour tint here -- that's the split-tone stage's job). Compress: soft knee above `knee`,
    scaling the remaining headroom by highlight_scale so pure white doesn't stay pure white."""
    out = black_floor + (1.0 - black_floor) * arr
    out = np.where(out < knee, out, knee + (out - knee) * highlight_scale)
    return out


# Hue bands (degrees) that review flagged as the "fantasy poster" offenders: ember/lava orange,
# forest/undergrowth green, lightning/electric cyan-blue. Only pulled down HARDER when already
# saturated -- this is on top of the uniform sat_reduce below, not instead of it.
EXTREME_HUES_DEG = (28.0, 130.0, 195.0)
EXTREME_WIDTH_DEG = (16.0, 20.0, 18.0)


def reduce_saturation(arr, base_reduce, extra_pull, hues_deg=EXTREME_HUES_DEG, widths_deg=EXTREME_WIDTH_DEG,
                       extreme_gate=(0.55, 0.85)):
    """base_reduce applies to everything uniformly. extra_pull applies ONLY where a pixel is both
    hue-close to one of hues_deg AND already highly saturated (extreme_gate: a 0..1 smoothstep from
    extreme_gate[0] to extreme_gate[1], so e.g. a moderately warm ember-orange at s~0.5 gets none of
    the extra pull and stays warm, while a neon/radioactive s~0.9 orange gets the full extra pull).
    TUNING NOTE: the first pass gated the extra pull by raw saturation `s` continuously from 0, which
    hit moderately-saturated legitimate colour (ember glow) almost as hard as true neon extremes --
    failing the "ember glow still warm" requirement. This gate fixes that by only engaging well above
    the ordinary-colour range."""
    hsv = rgb_to_hsv(arr)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    hdeg = h * 360.0
    extra_weight = np.zeros_like(s)
    for center, width in zip(hues_deg, widths_deg):
        d = np.minimum(np.abs(hdeg - center), 360.0 - np.abs(hdeg - center))
        extra_weight = np.maximum(extra_weight, np.exp(-0.5 * (d / width) ** 2))
    gate_lo, gate_hi = extreme_gate
    sat_gate = np.clip((s - gate_lo) / max(gate_hi - gate_lo, 1e-6), 0.0, 1.0)
    s_new = s * (1.0 - base_reduce)
    s_new = s_new - extra_pull * extra_weight * sat_gate
    s_new = np.clip(s_new, 0.0, 1.0)
    return hsv_to_rgb(np.stack([h, s_new, v], axis=-1))


def split_tone(arr, cool_rgb01, warm_rgb01, paper_rgb01, strength):
    """Gradient-map-style blend: shadows -> cool_rgb01, highlights -> warm_rgb01, midtones ->
    paper_rgb01, by luminance zone, blended toward the original at `strength` (moderate, not a full
    recolour)."""
    lum = 0.2126 * arr[..., 0] + 0.7152 * arr[..., 1] + 0.0722 * arr[..., 2]
    shadow_w = np.clip(1.0 - 2.0 * lum, 0.0, 1.0)
    highlight_w = np.clip(2.0 * lum - 1.0, 0.0, 1.0)
    mid_w = np.clip(1.0 - shadow_w - highlight_w, 0.0, 1.0)
    target = (shadow_w[..., None] * np.array(cool_rgb01)
              + highlight_w[..., None] * np.array(warm_rgb01)
              + mid_w[..., None] * np.array(paper_rgb01))
    return arr * (1.0 - strength) + target * strength


def top_haze(arr, strength, height_frac, color_rgb01):
    if strength <= 0:
        return arr
    h, w = arr.shape[:2]
    yy = (np.arange(h, dtype=np.float32) / h)[:, None]
    grad = np.clip(1.0 - yy / max(height_frac, 1e-6), 0.0, 1.0) ** 1.2
    grad = np.repeat(grad, w, axis=1)
    alpha = strength * grad
    return arr * (1.0 - alpha[..., None]) + np.array(color_rgb01) * alpha[..., None]


def apply_paper_texture(arr01, opacity, seed):
    """Reuses texlib.paper_texture (grain/mottle) and texlib.watercolor_wash (pooled wash) at low
    opacity. The grain layer is applied as an additive delta from a flat PAPER fill (i.e. exactly the
    mottle+grain+fiber texlib already generates, nothing more), scaled by `opacity`; the wash is
    alpha-blended at its own low built-in alpha further scaled by `opacity`."""
    if opacity <= 0:
        return arr01
    h, w = arr01.shape[:2]
    paper_img = tex.paper_texture(w, h, tex.PAPER, seed=seed, mottle_strength=12, grain_strength=9)
    paper_arr = np.asarray(paper_img, dtype=np.float32)
    delta = (paper_arr - np.array(tex.PAPER, dtype=np.float32)) / 255.0
    out = arr01 + delta * opacity

    wash = tex.watercolor_wash(w, h, tex.PAPER, seed=seed + 5, blobs=4, alpha=26, spread=0.9)
    wash_arr = np.asarray(wash, dtype=np.float32)
    wash_rgb = wash_arr[..., :3] / 255.0
    wash_a = (wash_arr[..., 3:4] / 255.0) * opacity
    out = out * (1.0 - wash_a) + wash_rgb * wash_a
    return out


def apply_vignette(arr01, strength):
    if strength <= 0:
        return arr01
    h, w = arr01.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    cx, cy = w / 2.0, h / 2.0
    d = np.sqrt(((xx - cx) / (w / 2.0)) ** 2 + ((yy - cy) / (h / 2.0)) ** 2)
    mask = np.clip((d - 0.55) / 0.45, 0.0, 1.0) ** 1.5
    factor = 1.0 - strength * mask
    return arr01 * factor[..., None]


# Final producer-approved parameters (see ../../provenance/menu-backdrops.md section "grade.py
# parameters" for the tuning pass that landed on these).
DEFAULTS = dict(
    black_floor=None,          # None -> computed from INK_PLUM's luma at runtime
    knee=0.78,
    highlight_scale=0.72,
    sat_reduce=0.27,           # within the producer's requested 25-40% range, gentle end (a first
                                # tuning pass at 0.32 plus an ungated extra-pull was reading as a
                                # wash-out on direction D's own backdrop and killed r02's ember warmth)
    extra_sat_pull=0.45,       # stronger, but gated to only engage on genuinely extreme/neon
                                # saturation (see reduce_saturation's extreme_gate) -- moderate colour
                                # (ember glow, lightning) is no longer touched by this term at all
    grade_strength=0.17,       # moderate split-tone blend, not a recolour
    haze_strength=0.08,
    haze_height=0.32,
    texture_opacity=0.32,
    vignette_strength=0.14,
    seed=None,                 # None -> derived per-input-filename so grain doesn't repeat identically
)


def grade_image(im, seed_hint="grade", **overrides):
    p = dict(DEFAULTS)
    p.update({k: v for k, v in overrides.items() if v is not None})
    if p["black_floor"] is None:
        p["black_floor"] = luma_255(tex.INK_PLUM) / 255.0
    if p["seed"] is None:
        p["seed"] = int(hashlib.sha1(seed_hint.encode()).hexdigest(), 16) % 10_000

    arr = np.asarray(im.convert("RGB"), dtype=np.float32) / 255.0
    arr = lift_and_compress(arr, p["black_floor"], p["knee"], p["highlight_scale"])
    arr = reduce_saturation(arr, p["sat_reduce"], p["extra_sat_pull"])
    arr = split_tone(arr, hex_to_rgb01("%02x%02x%02x" % tex.COOL_SHADOW),
                      hex_to_rgb01("%02x%02x%02x" % tex.APRICOT),
                      hex_to_rgb01("%02x%02x%02x" % tex.PAPER),
                      p["grade_strength"])
    arr = top_haze(arr, p["haze_strength"], p["haze_height"],
                    hex_to_rgb01("%02x%02x%02x" % tex.PAPER))
    arr = apply_paper_texture(arr, p["texture_opacity"], p["seed"])
    arr = apply_vignette(arr, p["vignette_strength"])
    arr = np.clip(arr, 0.0, 1.0)
    return Image.fromarray((arr * 255.0 + 0.5).astype(np.uint8), "RGB")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+", help="input PNG path(s)")
    ap.add_argument("--out-dir", default=str(OUT_DIR_DEFAULT), help="output directory (default: graded/)")
    ap.add_argument("--black-floor", type=float, default=None,
                     help="0..1 shadow floor (default: ink-plum's own luma)")
    ap.add_argument("--knee", type=float, default=DEFAULTS["knee"])
    ap.add_argument("--highlight-scale", type=float, default=DEFAULTS["highlight_scale"])
    ap.add_argument("--sat-reduce", type=float, default=DEFAULTS["sat_reduce"],
                     help="uniform saturation reduction, 0..1 (default 0.27, i.e. 27%%)")
    ap.add_argument("--extra-sat-pull", type=float, default=DEFAULTS["extra_sat_pull"],
                     help="extra saturation pull on extreme orange/green/electric-blue hues, 0..1")
    ap.add_argument("--grade-strength", type=float, default=DEFAULTS["grade_strength"],
                     help="split-tone blend strength toward cool-shadow/apricot/paper, 0..1")
    ap.add_argument("--haze-strength", type=float, default=DEFAULTS["haze_strength"])
    ap.add_argument("--haze-height", type=float, default=DEFAULTS["haze_height"],
                     help="fraction of image height (from top) the haze fades out over")
    ap.add_argument("--texture-opacity", type=float, default=DEFAULTS["texture_opacity"])
    ap.add_argument("--vignette-strength", type=float, default=DEFAULTS["vignette_strength"])
    ap.add_argument("--seed", type=int, default=None, help="texture seed (default: derived per filename)")
    a = ap.parse_args()

    out_dir = pathlib.Path(a.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    overrides = dict(black_floor=a.black_floor, knee=a.knee, highlight_scale=a.highlight_scale,
                      sat_reduce=a.sat_reduce, extra_sat_pull=a.extra_sat_pull,
                      grade_strength=a.grade_strength, haze_strength=a.haze_strength,
                      haze_height=a.haze_height, texture_opacity=a.texture_opacity,
                      vignette_strength=a.vignette_strength, seed=a.seed)
    for in_path in a.inputs:
        in_path = pathlib.Path(in_path)
        im = Image.open(in_path)
        graded = grade_image(im, seed_hint=in_path.stem, **overrides)
        out_path = out_dir / in_path.name
        graded.save(out_path)
        print(f"wrote {out_path}")


if __name__ == "__main__":
    main()
