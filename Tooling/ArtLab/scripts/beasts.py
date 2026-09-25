"""The house style: the heroic/proud chibi (head:body about 1:3-1:4) prompt template, IP-avoidance negatives and fixed
per-beast colour swatches (colour-block init, Reinhard target, drift/fidelity metrics).
Every prompt must fit CLIP's 77 tokens: the SDXL pipeline silently truncates longer ones (tokcheck.py checks)."""
import os

BEASTS = ["phoenix", "golem", "kirin"]
ELEMENT = {"phoenix": "Fire", "golem": "Earth", "kirin": "Light"}

# ---------- house template (round 4: bolder/heroic; round 3's 'super deformed', 'cute', 'gentle smile' dropped) ----------
SUBJ = {
    "phoenix": "phoenix, bird, pointed beak, swept flame crest, curved neck, raised flame wings, long plume tail, "
               "cream and orange feathers, bird legs",
    # round 4: 'rock golem' (generic fantasy tag) with 'pokemon' negated; 'stone creature' gave objects, not creatures
    # final (producer pick #10, personality CUTE): friendly round face + big round eyes instead of 'determined' glow
    "golem": "rock golem, quadruped, mossy hill back, standing stones, wildflowers, thick stone legs, "
             "round face, big amber eyes, curious, grey stone",
    "kirin": "qilin, deer, single curved golden horn, short cream mane, amber scales on back, amber eyes, serene, "
             "cream fur, dark hooves",
}
HEAD = os.environ.get("ARTLAB_HEAD", "no humans, chibi, majestic, proud, mythical creature")
TAIL = ("heroic pose, dynamic, elegant, bold clean lineart, soft cel shading, painterly, warm light, full body, "
        "simple background, masterpiece")
STYLE_NEG = "flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel, angry"
COLOUR_NEG = "violet, purple, cyan, magenta, blue"
IP_NEG = {
    "phoenix": "pokemon, moltres, ho-oh, torchic, charmander, fanart",
    "golem": "pokemon, torterra, turtwig, tortoise, sprout, wolf",
    "kirin": "pokemon, rapidash, xerneas, unicorn, horse, wolf",
}
NEG_TAIL = "lowres, bad anatomy, extra legs, text, worst quality, low quality, blurry, 3d"


def house_prompt(b, subj=None):
    return f"{HEAD}, {subj or SUBJ[b]}, {TAIL}"


# round 4: the style refs are all fire birds -> non-fire beasts negate fire explicitly (A/B: flaming wolf golem)
ELEM_NEG = {"phoenix": "", "golem": "fire, flames", "kirin": "fire, flames"}


def house_neg(b, colour_lock=True):
    parts = [STYLE_NEG] + ([COLOUR_NEG] if colour_lock else []) + ([ELEM_NEG[b]] if ELEM_NEG[b] else [])
    return ", ".join(parts + [IP_NEG[b], NEG_TAIL])


# ---------- per-beast swatches (hex, weight): colour block, Reinhard target, drift/fidelity ----------
SWATCH = {
    "phoenix": [("fff1d6", 3), ("ffd98a", 2), ("f79a3a", 3), ("e2552e", 2), ("b8342a", 1), ("f4b183", 1)],
    "golem": [("a89c8c", 3), ("8a7a68", 2), ("6b5f55", 1), ("6f8f4a", 2), ("9fbf6a", 2), ("d8c8a8", 1), ("f2c96a", 1)],
    # round 4: stronger value range than round 3's cream-on-cream (amber + deep amber accents)
    "kirin": [("fff3de", 3), ("f3d9a0", 3), ("e8b04a", 2), ("c9822e", 2), ("8a4f24", 1), ("f2b89a", 1)],
}
BG = (246, 238, 224)  # warm cream background for all colour-blocked bases
