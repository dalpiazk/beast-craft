# Beast Craft ArtLab, remaining-seven finals (2026-09-26): prompts, negatives and swatches of the seven finals (Leviathan, Griffin, Thunderbird, Frost Wyrm, Treant, Tarasque, Basilisk).
"""Finals for the new beasts (producer picks from ai-art-candidates): same prompt/negatives/swatch as the candidate
sheets (ai-art-candidates/scripts/cand.py), so the lock reproduces the picked design in the same house look.
Shared by every new-beast final folder (copy kept per beast)."""
import os

BEASTS = ["leviathan", "griffin", "thunderbird", "frost_wyrm", "treant", "tarasque", "basilisk"]
ELEMENT = {"leviathan": "Water", "griffin": "Air", "thunderbird": "Lightning", "frost_wyrm": "Ice", "treant": "Nature", "tarasque": "Metal", "basilisk": "Dark"}
PERSONALITY = {"leviathan": "Serene", "griffin": "Bold", "thunderbird": "Wild", "frost_wyrm": "Wise", "treant": "Gentle", "tarasque": "Grumpy", "basilisk": "Sly"}

PERS = {"leviathan": "calm, serene, regal, gentle eyes",
        "griffin": "bold, brave, proud chest",
        "thunderbird": "wild, energetic, excited grin, dynamic",
        "frost_wyrm": "old, wise, wry smile, half-lidded eyes",
        "treant": "gentle, kind eyes, soft smile, protective",
        "tarasque": "grumpy, scowl, frowning, lovable",
        "basilisk": "sly, smirk, narrowed eyes"}
SUBJ = {
    "leviathan": "sea serpent, long coiled body, flowing fin frills, protective scales, teal scales, cream belly, "
                 "small horns, closed mouth",
    # griffin final: 'feathered wings raised' -> 'wings raised from the shoulders' (producer's wing note)
    "griffin": "griffin, eagle head, lion body, wings raised from shoulders, eagle talons, lion hindquarters, "
               "tufted tail, golden feathers, tawny fur",
    "thunderbird": "thunderbird, storm bird, swept wings, crackling feathers, lightning streaks, slate grey feathers, "
                   "yellow lightning accents, sleek",
    # final: producer asked for proper irises/pupils and swept-back dragon horns (no ram curls)
    "frost_wyrm": "ice dragon, wingless, frost whiskers, icicle beard, pale blue, dark irises, "
                  "swept back horns",
    "treant": "treant, walking tree creature, mossy bark body, leaf crown, root feet, branch arms, "
              "blossoms, green leaves, brown bark",
    # final: producer asked for 4 sturdy legs
    "tarasque": "tarasque, armored quadruped, four sturdy legs, iron plated shell, spikes, lion face, gold tusks, "
                "steel grey",
    # round-2 lizard body (producer); final pick basilisk_v2_L2
    "basilisk": "basilisk, crested lizard, four legs, low body, long tapering tail, small crown crest, "
                "glowing gold eyes, deep plum scales",
}
TAIL = "heroic pose, bold clean lineart, soft cel shading, painterly, warm light, full body, simple background, masterpiece"
STYLE_NEG = "flat colors, vector art, sticker, glossy, baby, toddler, plush, round blob, pastel"
COLOUR_NEG = {"leviathan": "violet, purple, magenta, red, orange", "griffin": "violet, purple, magenta, cyan, red",
              "thunderbird": "red, pink, green, magenta", "frost_wyrm": "violet, purple, magenta, red, orange",
              "treant": "violet, purple, cyan, blue, red", "tarasque": "violet, purple, cyan, blue, pink",
              "basilisk": "red, cyan, green, blue, pink"}
IP_NEG = {"leviathan": "pokemon, gyarados, milotic, dragonair, lugia, water, waves",
          "griffin": "pokemon, braviary, hippogriff, horse, wolf, fanart",
          "thunderbird": "pokemon, zapdos, articuno, staraptor, pikachu, spiky yellow body",
          "frost_wyrm": "pokemon, kyurem, glaceon, ram horns, blank eyes, wings",
          "treant": "pokemon, torterra, trevenant, sudowoodo, groot, human face",
          "tarasque": "pokemon, torterra, blastoise, bowser, bipedal, two legs",
          "basilisk": "pokemon, snake, coiled, serpent, hood, fangs, wyvern, wings"}
NEG_TAIL = "fire, flames, multiple views, dark background, lowres, bad anatomy, text, worst quality, blurry"
HEAD = "no humans, solo, chibi, mythical creature"


def house_prompt(b, subj=None):
    return f"{HEAD}, {PERS[b]}, {subj or SUBJ[b]}, {TAIL}"


def house_neg(b, colour_lock=True):
    return ", ".join([STYLE_NEG, COLOUR_NEG[b], IP_NEG[b], NEG_TAIL])


SWATCH = {
    "leviathan": [("2f6f7a", 3), ("4f9a9a", 3), ("9fd3c7", 2), ("f3e6cc", 2), ("e8c170", 1), ("23485a", 1)],
    "griffin": [("c98a3a", 3), ("e0b877", 3), ("fff3de", 2), ("7a4a2a", 2), ("f2b84a", 1), ("9cc7d9", 1)],
    "thunderbird": [("3f4a63", 3), ("6f8fb8", 2), ("a9bdd6", 2), ("f8f4e8", 2), ("ffd84a", 2), ("f2a93a", 1)],
    "frost_wyrm": [("c9e4ee", 3), ("f4f8f6", 3), ("8fb8d0", 2), ("4f7a99", 1), ("d8c8a8", 1), ("e8b04a", 1)],
    "treant": [("7a5a3e", 3), ("4e3a2a", 1), ("6f8f4a", 2), ("9fbf6a", 2), ("c7d97a", 1), ("f2b8b0", 1), ("f2c96a", 1)],
    "tarasque": [("6e7278", 3), ("a8adb2", 2), ("a0643a", 2), ("5f7f5a", 1), ("d8c8a8", 2), ("3e4146", 1)],
    "basilisk": [("2e1f3a", 3), ("4a2f5e", 3), ("6e4a82", 2), ("d8c8b8", 1), ("e8b04a", 1), ("1c1224", 1)],
}
BG = (246, 238, 224)
