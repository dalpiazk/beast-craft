# Skill-icon batch (2026-09-27): the 91 skills (ids/ArtKeys read from the data, read-only), palettes and motif briefs.
import os, json, pathlib

REPO = pathlib.Path(os.environ.get("BEASTCRAFT_REPO", str(pathlib.Path(__file__).resolve().parents[4])))
HERE = pathlib.Path(__file__).resolve().parent

PAL = {  # bg_in, bg_out, main, hi, dark, acc
    "Fire":      dict(bg_in=(250, 150, 60), bg_out=(110, 25, 20), main=(255, 140, 45), hi=(255, 236, 160), dark=(200, 55, 25), acc=(255, 200, 80)),
    "Earth":     dict(bg_in=(190, 150, 95), bg_out=(90, 62, 38), main=(160, 150, 135), hi=(200, 192, 172), dark=(110, 80, 50), acc=(104, 140, 70)),
    "Water":     dict(bg_in=(110, 190, 190), bg_out=(22, 72, 96), main=(90, 190, 215), hi=(235, 255, 255), dark=(30, 110, 140), acc=(240, 225, 205)),
    "Nature":    dict(bg_in=(175, 210, 115), bg_out=(40, 82, 42), main=(110, 170, 70), hi=(220, 240, 160), dark=(90, 66, 44), acc=(240, 150, 170)),
    "Metal":     dict(bg_in=(185, 188, 192), bg_out=(58, 62, 72), main=(150, 156, 168), hi=(225, 228, 235), dark=(70, 74, 84), acc=(196, 120, 60)),
    "Ice":       dict(bg_in=(150, 200, 230), bg_out=(40, 80, 130), main=(160, 205, 235), hi=(240, 250, 255), dark=(90, 140, 190), acc=(250, 255, 255)),
    "Lightning": dict(bg_in=(62, 74, 112), bg_out=(14, 18, 36), main=(255, 228, 60), hi=(215, 248, 255), dark=(28, 36, 64), acc=(90, 220, 255)),
    "Air":       dict(bg_in=(150, 200, 215), bg_out=(50, 95, 125), main=(245, 250, 245), hi=(255, 255, 255), dark=(120, 170, 180), acc=(222, 184, 104)),
    "Light":     dict(bg_in=(255, 235, 170), bg_out=(160, 105, 40), main=(255, 222, 120), hi=(255, 255, 242), dark=(210, 160, 70), acc=(255, 240, 180)),
    "Dark":      dict(bg_in=(120, 80, 140), bg_out=(38, 22, 52), main=(245, 185, 55), hi=(225, 205, 180), dark=(40, 20, 45), acc=(255, 220, 110)),
    "Avatar":    dict(bg_in=(255, 235, 180), bg_out=(170, 120, 60), main=(215, 160, 70), hi=(255, 246, 215), dark=(140, 90, 40), acc=(255, 210, 120)),
    "AvatarHex": dict(bg_in=(200, 170, 210), bg_out=(90, 60, 110), main=(200, 185, 160), hi=(240, 230, 210), dark=(70, 40, 80), acc=(186, 120, 220)),
    "Gloam":     dict(bg_in=(170, 160, 190), bg_out=(48, 38, 66), main=(200, 190, 215), hi=(240, 236, 210), dark=(70, 60, 92), acc=(232, 230, 168)),
}
LIGHTD = dict(PAL["Light"], bg_in=(168, 108, 48), bg_out=(52, 30, 18))   # darker amber ground for the Light rerolls
AVATARD = dict(PAL["Avatar"], bg_in=(160, 105, 45), bg_out=(45, 28, 16), main=(235, 180, 80))   # darker ground for the avatar rerolls
METALD = dict(PAL["Metal"], bg_in=(120, 125, 135), bg_out=(35, 38, 46), main=(170, 176, 188), dark=(60, 64, 74))   # darker ground for rerolls
GLOAMD = dict(PAL["Gloam"], main=(120, 108, 142), hi=(236, 228, 250), dark=(40, 32, 58))   # darker stone-violet motif for the enemy rerolls
DARKV = dict(PAL["Dark"], main=(150, 200, 80), hi=(210, 240, 150), acc=(180, 110, 200))   # Dark venom/fog variant
DARKC = dict(PAL["Dark"], main=(170, 120, 200), hi=(235, 210, 255), acc=(245, 185, 55))   # Dark claws/fangs

# id: (element/palette, primitive, kwargs, motif words)
BEAST = {
    # Earth (Golem): stone, moss
    "boulder_slam":     ("Earth", "boulder", {}, "one big solid grey stone boulder with a moss cap, smashing onto cracked ground"),
    "stone_challenge":  ("Earth", "menhir_shout", {}, "a tall mossy standing stone with booming sound rings, earthy grey and brown"),
    "granite_bulwark":  ("Earth", "slab_ring2", {}, "a stone circle of tall upright granite slabs standing around a glowing centre, protective ring of standing stones"),
    "tectonic_shove":   ("Earth", "shove", {}, "a grey boulder slamming forward with speed lines, pushing, dust burst, earthy brown"),
    "quake":            ("Earth", "impact", {"ground": True}, "the ground cracking in a stomping quake, rock burst, dust"),
    "stoneskin":        ("Earth", "scales", {}, "overlapping grey stone armour scales, a hardened stone hide, moss"),
    # Water (Leviathan)
    "serpent_bite":     ("Water", "jaws", {}, "open sea serpent jaws snapping shut, curved fangs, water splash"),
    "undertow":         ("Water", "ripple_pull", {}, "a whirlpool of rippling rings with currents pulling inward to a dark centre"),
    "deep_shell":       ("Water", "shell_big", {}, "one big scallop seashell filling the icon, teal water glow"),
    "tidal_wave":       ("Water", "wave", {}, "one big curling tidal wave crest, white foam"),
    "maelstrom":        ("Water", "vortex", {"arms": 4}, "a churning maelstrom vortex, spiral water, dark centre"),
    "tidal_renewal":    ("Water", "droplet", {}, "one glowing healing water droplet, gentle sparkles"),
    # Nature (Treant)
    "thorn_lash":       ("Nature", "thorn_whip2", {}, "a thick green vine whip covered in big curved hooked thorns, lashing in an S curve"),
    "verdant_mend":     ("Nature", "leaf", {"drop": True}, "one big green healing leaf with a dew drop"),
    "bark_ward":        ("Nature", "shield", {"emblem": "leaf"}, "a bark wood shield with a leaf emblem, protective"),
    "entangling_roots": ("Nature", "roots", {}, "gnarled roots bursting up from the ground, grasping"),
    "spore_cloud":      ("Nature", "toxic_cloud", {}, "one large billowing toxic green spore cloud, small glowing spore specks"),
    "lifebloom":        ("Nature", "flower", {}, "one big pink blossom flower opening, green leaves"),
    # Metal (Tarasque)
    "sunder":           ("MetalD", "sunder_split", {}, "a riveted steel armour plate split in two by a bright diagonal slash, halves breaking apart"),
    "iron_crush":       ("Metal", "fist", {}, "a heavy iron gauntlet fist crushing down, impact lines"),
    "iron_fortress":    ("Metal", "shield", {"emblem": "dot"}, "a riveted iron tower shield, bronze rim, steadfast"),
    "spiked_carapace":  ("Metal", "shield", {"spikes": True}, "a spiked iron carapace shell, metal spines"),
    "shrapnel_burst":   ("MetalD", "shards", {}, "sharp steel scale shards flying outward from a burst, steel grey and bronze"),
    "juggernaut_charge": ("Metal", "charge", {}, "a massive iron arrow charging forward, speed lines"),
    # Ice (Frost Wyrm)
    "rime_bolt":        ("Ice", "crystal", {}, "a sharp pale blue ice crystal shard, frost sparkles"),
    "deep_freeze":      ("Ice", "iceblock", {}, "a solid ice cube block, frozen, glinting"),
    "frost_breath":     ("Ice", "breath", {}, "a cone of freezing frost breath, icy wind swirls"),
    "blizzard":         ("Ice", "asterisk", {"n": 6}, "a six-way burst of driving snow, frosty gusts"),
    "ice_armor":        ("Ice", "shield", {"emblem": "flake"}, "an ice crystal shield, rime frost, glassy"),
    "absolute_zero":    ("Ice", "snowflake", {}, "one huge intricate snowflake, frozen air, glowing"),
    # Lightning (Thunderbird)
    "thunder_talons": ("Lightning", "talon_bolts", {}, "three thick jagged lightning claw slashes, electric yellow and cyan, dark stormy background"),
    "chain_lightning": ("Lightning", "chain_nodes", {}, "lightning jumping in a chain between three glowing points, electric yellow and cyan"),
    "static_charge": ("Lightning", "charged_feather", {}, "one feather crackling with cyan static electricity arcs, electric yellow"),
    "storm_dive": ("Lightning", "storm_dive_bolt", {}, "a thick lightning bolt diving down from a dark storm cloud, electric yellow"),
    "thunderclap": ("Lightning", "clap2", {}, "a lightning bolt striking the centre, jagged electric shockwave rings, cyan and yellow"),
    "plasma_barrage": ("Lightning", "bolt_row2", {}, "four bold lightning bolts flying in a diagonal row, electric yellow and cyan"),
    # Air (Griffin)
    "gale_talon":       ("Air", "claws", {}, "three sweeping talon slash marks, wind streaks"),
    "wind_lance": ("Air", "wind_spear", {}, "one thick spear of compressed air wrapped in spiralling wind"),
    "gust":             ("Air", "vortex", {"arms": 3}, "a spiralling gust of wind, swirling air currents"),
    "tailwind":         ("Air", "wing", {}, "one feathered wing riding a swirl of wind"),
    "updraft": ("Air", "rising_wind", {}, "three big rising wind swooshes with upward arrows, lifting air"),
    "sky_rend": ("Air", "talon_dive", {}, "one big hooked golden talon plunging down, tearing a jagged gash"),
    # Fire (Phoenix)
    "ember_shot":       ("Fire", "comet", {}, "a single blazing fireball comet streaking diagonally, flame tail"),
    "flame_wave":       ("Fire", "fire_ring", {}, "a circle of flames, a burning fire ring on dark ground"),
    "rebirth_flame":    ("Fire", "flame", {"feather": True}, "one tall renewing flame with a phoenix feather, glowing"),
    "blaze_bolt":       ("Fire", "lance", {"w": .08, "glow": True}, "a concentrated lance of fire, blazing spear"),
    "firestorm":        ("Fire", "fire_tornado", {}, "one swirling fire tornado, a funnel of flame spinning"),
    "sunfire_nova":     ("Fire", "asterisk", {"n": 6}, "a six-way burst of white-hot fire, sun nova"),
    # Light (Kirin)
    "sacred_spring":    ("LightD", "spring_jet", {}, "a round glowing spring pool with one central jet of golden water splashing into droplets, dark amber background"),
    "blessing":         ("Light", "sparkle", {}, "a four-pointed star of blessing light, sparkles"),
    "radiant_bolt":     ("LightD", "lance", {"w": .1, "flare": False, "glow": True}, "one single clean bright spear of golden light, dark amber background"),
    "judgment":         ("Light", "pillar", {}, "a pillar of light striking down from above"),
    "purifying_ward":   ("Light", "dome", {}, "a glowing dome of pure light, protective"),
    "halo":             ("Light", "halo", {"dots": 0}, "one glowing golden halo ring"),
    # Dark (Basilisk)
    "venom_spit":       ("DarkV", "droplet", {}, "one glob of green venom, dripping poison"),
    "coup_de_grace":    ("DarkC", "claws", {}, "three shadow claw slashes, violet shadow"),
    "petrifying_gaze":  ("Dark", "eye", {"cracks": True}, "a single glowing golden reptile eye, slit pupil, stone cracks"),
    "eclipse_fang": ("DarkC", "eclipse_bite", {}, "four sharp shadow fangs biting across a dark eclipse with a glowing rim"),
    "predator_focus": ("Dark", "focus_eye", {}, "a narrowed golden slit eye locked inside a targeting reticle"),
    "miasma":           ("DarkV", "asterisk", {"n": 6, "puffy": True}, "a six-way cross of poison fog, sickly green mist"),
}
AVATAR = {
    "rallying_cry": ("AvatarD", "shout", {"source": "horn"}, "a golden war horn blowing a rallying call, bold sound waves, dark amber background"),
    "mending_light": ("AvatarD", "mend_leaf", {}, "a pale green healing leaf wrapped in a cream bandage, soft cool white glow, sparkles, dark amber background"),
    "aegis":          ("Avatar", "shield", {}, "one golden heraldic shield in front of a glowing round dome ward, warm light"),
    "hex_of_frailty": ("AvatarHex", "curse", {}, "a cracked shield wrapped in violet curse wisps"),
    "battle_focus": ("AvatarD", "reticle", {}, "a bold golden targeting reticle crosshair, dark amber background"),
    "slowing_field":  ("AvatarHex", "hourglass", {}, "a slowed hourglass with heavy sand, thick air"),
}
PASSIVE = {
    "keen_eye":        ("Avatar", "eye", {"pupil": "round"}, "a watchful golden eye, soft glow"),
    "iron_will":       ("Avatar", "fist", {}, "a golden gauntlet fist held firm, steadfast resolve, soft glow"),
    "opening_ward":    ("Avatar", "dome", {}, "a small dawn-lit dome ward, soft glow"),
    "battle_hymn": ("AvatarD", "notes", {}, "two golden music notes of a battle hymn, soft glow, dark amber background"),
    "withering_curse": ("AvatarHex", "wilted_big", {}, "one large wilted drooping flower with a bent stem and falling petals, thin violet wisps"),
    "bloodlust":       ("Fire", "chevrons", {}, "three rising red upward chevrons, surging fervor, soft glow"),
    "vengeance": ("AvatarD", "heart", {"flame_top": True}, "a golden heart with a resolute flame rising from it, soft glow, dark background"),
    "storm_call":      ("Lightning", "bolt", {"branches": 0}, "a bolt called down from a storm, soft glow"),
    "verdant_pulse":   ("Nature", "leaf", {"sprout": True}, "a small green sprout pulsing with life, soft glow"),
    "last_stand":      ("Avatar", "shield", {"cracked": True}, "a cracked golden shield still holding, soft glow"),
}
ENEMY = {  # (enemy, skill): (primitive, kw, words)
    ("giant", "crush"):         ("fist", {}, "a huge grey stone fist punching down, knuckles, impact lines"),
    ("giant", "gaze"):          ("eye", {"pupil": "round", "glare": True}, "a baleful glaring eye, searing"),
    ("giant", "quake"):         ("impact", {"ground": True}, "a heavy stomp cracking the ground"),
    ("giant", "roar"):          ("shout", {"source": "dot"}, "a dread roar, booming sound waves"),
    ("champion", "cleave"):     ("cleave_arc", {}, "one wide bright crescent blade sweep, a great cleaving slash"),
    ("champion", "hex"):        ("curse", {"target": None}, "a curling gloam hex curse, violet wisps"),
    ("champion", "shockwave"):  ("impact", {"ground": False, "ring": True}, "a burst of force, shockwave ring"),
    ("brute", "smash"):         ("club", {}, "a heavy mossy wooden club smashing down on cracked ground"),
    ("stalker", "shadow_claw"): ("claws", {}, "three ambush claw slashes from the shadows"),
    ("archer", "arrow"):        ("arrow", {}, "one twig arrow with leaf fletching flying"),
    ("caster", "bolt"):         ("comet", {}, "a spiteful glowing bolt with a violet trail"),
    ("shaman", "staff"):        ("staff", {}, "a gnarled root staff rapping down, impact"),
    ("shaman", "storm"):        ("cloud", {"dots": False, "rainy": True}, "a small squall storm cloud with rain"),
    ("swarmling", "bite"):      ("fangs", {"n": 2}, "a small biting mouth with two fangs"),
    ("stingling", "sting"):     ("stinger", {}, "a curved stinger with a poison drop"),
}
GLOAM_WORDS = ", violet grey haze wisps"


def palette(k):
    return {"DarkV": DARKV, "DarkC": DARKC, "MetalD": METALD, "LightD": LIGHTD, "AvatarD": AVATARD, "GloamD": GLOAMD}.get(k) or PAL[k]


def load():
    """Every skill with its icon file, ArtKey, name and group, from the data (read-only)."""
    lib = json.loads((REPO / "content/data/Skills/skill-library.json").read_text(encoding="utf-8"))
    en = json.loads((pathlib.Path(os.environ.get("ARTLAB_ICONS", str(pathlib.Path(os.environ.get("ARTLAB_HOLLOW", ".")) / "icons"))) / "work" / "enemy-library.branch.json").read_text(encoding="utf-8"))
    out = []
    for s in lib["BeastSkills"]:
        el, prim, kw, words = BEAST[s["SkillId"]]
        out.append(dict(id=s["SkillId"], name=s["DisplayName"], artkey=s["ArtKey"], group=s["Element"], pal=el, prim=prim, kw=kw,
                        words=words, kind="beast", file=f"content/art/icons/skills/{s['SkillId']}.png", rarity="common"))
    for s in lib["AvatarActives"]:
        el, prim, kw, words = AVATAR[s["SkillId"]]
        out.append(dict(id=s["SkillId"], name=s["DisplayName"], artkey=s["ArtKey"], group="Avatar", pal=el, prim=prim, kw=kw,
                        words=words, kind="active", file=f"content/art/icons/skills/{s['SkillId']}.png", rarity="rare"))
    for s in lib["AvatarPassives"]:
        el, prim, kw, words = PASSIVE[s["PassiveId"]]
        out.append(dict(id=s["PassiveId"], name=s["DisplayName"], artkey=s["ArtKey"], group="Avatar", pal=el, prim=prim, kw=kw,
                        words=words, kind="passive", file=f"content/art/icons/skills/{s['PassiveId']}.png", rarity="rare"))
    for e in en["Enemies"]:
        eid = e.get("EnemyId") or e.get("Id")
        for s in e.get("Skills", []):
            prim, kw, words = ENEMY[(eid, s["SkillId"])]
            pal = "GloamD" if (eid, s["SkillId"]) in (("giant", "crush"), ("brute", "smash"), ("champion", "cleave")) else "Gloam"
            out.append(dict(id=f"{eid}_{s['SkillId']}", name=s["DisplayName"], artkey=s["ArtKey"], group="Enemy", pal=pal, prim=prim,
                            kw=kw, words=words + GLOAM_WORDS, kind="enemy", file=f"content/art/icons/skills/enemy/{eid}_{s['SkillId']}.png",
                            rarity="gloam"))
    return out


GROUPS = ["Fire", "Earth", "Water", "Nature", "Metal", "Ice", "Lightning", "Air", "Light", "Dark", "Avatar", "Enemy"]
