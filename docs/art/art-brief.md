> **Approach changed on 2026-09-25: beast art is made in-house with an AI-assisted pipeline, with the
> producer as art director** (section 0). The brief was first written for freelance quoting: the vision,
> the technical specs and the asset list still apply; the commissioning sections (the quote phasing and
> table, the budget ballparks and the contract checklist) are **superseded** and kept for the record.
> Numbers derived from `content/data/` and `docs/design/` on 2026-09-24. All display text in the data is
> itself DRAFT pending producer sign-off; do not treat names/lore below as final.

# Beast Craft — Art & Animation Brief

Beast Craft is a mobile-first (Android now, iOS later; desktop too) turn-based hex-grid PvE
creature-tactics game built in MonoGame. You control a Beastbinder (an avatar who never fights
directly) commanding a roster of ten mythic beasts against wild, Gloam-corrupted creatures.

## 0. Approach: AI-assisted pipeline, producer as art director (2026-09-25)

- **Who makes the art.** The beasts are generated and finished by the local pipeline in `Tooling/ArtLab/`
  (an SDXL anime model steered by our own approved style references, a design lock, a detail pass, a
  deterministic line pass, rig parts and an in-game export). The **producer is the art director**: every
  design is a producer pick from a candidate sheet, with a personality word and notes, and every final is
  producer-approved before it goes in the game.
- **Per asset:** candidate sheet → producer pick + notes → lock → finish → parts → in-game export
  (`Tooling/ArtLab/README.md`). Prompts, seeds, settings, the chosen candidate and the producer's decision
  are recorded per asset in `Tooling/ArtLab/provenance/` (the AI-disclosure and copyright record).
- **Done: all ten beasts are final**, approved and in the game: the starter trio, **Phoenix (Fierce), Golem
  (Cute) and Kirin (Mystic)** (2026-09-25), then **Leviathan (Serene, regal), Thunderbird (Wild, energetic),
  Griffin (Bold, brave), Frost Wyrm (Wise, wry), Treant (Gentle), Tarasque (Grumpy) and Basilisk (Sly)**
  (2026-09-26). Files: `content/art/beasts/<id>/` (in-game sprites), `content/art/source/<id>/`
  (full-resolution masters, rig parts and the design pick). The pixel placeholders stay in the manifest.
- **Before the animation pass:** the known touch-ups per beast are listed in `docs/art/touch-ups.md`.
- **Where it differs from the specs below:** the in-game beasts are currently single illustrated sprites
  (linear filter, 512x512 frames, feet pivot, straight-alpha PNGs premultiplied on load; see
  `docs/design/presentation-and-vfx.md`) with machine-cut rig parts archived beside them, not Spine rigs. The
  Spine plan (section 2) stays the target for animation; an artist pass on the parts' joints is advised first.
- **The models are tools only** (licences in `THIRD-PARTY-NOTICES.md` and `Tooling/ArtLab/README.md`);
  none ships with the game.

## 1. Vision & tone

**Quality bar:** hand-drawn chibi proportions; clean dark linework; soft 2-3-band cel shading;
painterly backgrounds with a warm meadow/sky palette; rounded, friendly UI; layered, readable AoE
effects; smooth skeletal idles. Mood word: *Studio Ghibli-inspired warmth* — a feeling to aim
for, never a style or design to copy. The producer will attach a mood board of several references.

**Keywords:** warm, adventurous, gently whimsical, "travel journal with sketches in the margins."
Wonder first, danger second — never grim (per `docs/design/content-bible.md`, Tone).

**Do:**
- Rounded, friendly silhouettes; chibi proportions (big heads/eyes, short limbs) on beasts and avatar.
- Soft cel shading, 2-3 value bands, warm/cool color contrast per biome.
- Mythic, invented creature and costume design.

**Don't:**
- No gore, blood, wounds, or cruelty — burns "smoulder," poison "stings"; no dripping, no viscera.
- No real-world religions, cultures, peoples, or brands used as costume or motif.
- No existing IP names, silhouettes, or copied designs (mood-board images are for finish level
  only, never to trace).
- No mockery of the player or of bosses in expression/pose.

## 2. Technical specs

| Spec | Value |
| --- | --- |
| Orientation | **Portrait.** Turn-order portrait bar (top) / hex board (middle) / skill strip (bottom) |
| Design canvas | 1080x1920 logical (portrait), safe area inset ~64px top/bottom for notches/nav |
| Source art resolution | 2x-4x target render size, layered PSD |
| Engine export | packed to POT atlases, **2048x2048** budget per atlas, ETC2 (Android) / ASTC (iOS/desktop fallback) |
| Rig pivot | feet (ground contact point), 0,0 local origin |
| Alpha | premultiplied |
| Rig tool | **Spine (Professional)**, pinned at kickoff to the latest stable editor version the official `spine-monogame` runtime supports; artist and runtime use the same major.minor |
| Runtime | official `spine-monogame` runtime |
| Spine conventions | bone-per-limb naming (`bone_head`, `bone_arm_l_upper`, …); cosmetics as **skins**, not separate skeletons; dedicated attachment slots per cosmetic category (hair, outfit, headwear, cape, species-specific: wings/fins/horns/shell/etc.) |
| Naming | every asset file keyed to its data `ArtKey` (e.g. `cosmetic/avatar_hair/braids`, species id for beasts, skill id for icons) — 1:1 traceable to `content/data/**` |
| VFX | hand-painted flipbook PNG layers, additive composite in-engine (ground decal, shockwave ring, radial burst, embers/sparks, rune glyphs) |
| Delivery | layered PSD sources + `.spine`/`.spineproject` files + exported atlas (`.png`) + `.json`/`.skel` per rig |

## 3. Asset list (counts from the data)

### 10 beasts (species)

| Species | Element | Stance | Personality (producer-approved) | Visual/personality cue | Art |
| --- | --- | --- | --- | --- | --- |
| Phoenix | Fire | Ranged | **Fierce** | Proud flame bird, strikes from afar, renews itself in fire | final (AI-assisted) |
| Leviathan | Water | Vanguard | **Serene, regal** | Calm deep serpent, coils, shields in scales, outlasts | final (AI-assisted) |
| Golem | Earth | Vanguard | **Cute** | Stone hillside given legs, slow, stubborn wall for allies | final (AI-assisted) |
| Griffin | Air | Skirmisher | **Bold, brave** | Eagle/lion hybrid, bold, hit-and-run with talons | final (AI-assisted) |
| Thunderbird | Lightning | Skirmisher | **Wild, energetic** | Storm-diving opener, glass-cannon speedster | final (AI-assisted) |
| Frost Wyrm | Ice | Vanguard | **Wise, wry** | Old, wry dragon in rime, slows/freezes, patient | final (AI-assisted) |
| Treant | Nature | Vanguard | **Gentle** | Gentle forest guardian, shelters and mends allies | final (AI-assisted) |
| Tarasque | Metal | Vanguard | **Grumpy** | Grumpy armored river-beast, endures and bites back | final (AI-assisted) |
| Kirin | Light | Ranged | **Mystic** | Serene luminous healer, fights with light not claws | final (AI-assisted) |
| Basilisk | Dark | Ranged | **Sly** | Sly crested shadow-lizard (four legs; snake bodies rejected), gaze attack, hunts the weak | final (AI-assisted) |

The personality word is part of each beast's art direction: it goes into the candidate brief and the
prompt (the Golem's `curious`, `round face, big amber eyes` came from **Cute**).

Each beast needs a **Spine skeleton with skin support** and the animation set: **idle, move,
attack, cast, hit, KO, victory** (7 clips x 10 beasts = 70 clips).

**Per-species cosmetic slots** (from `cosmetic-library.json`, each species has 2-3 discrete
categories + 1 color picker; 21 discrete categories, 176 options): e.g. Phoenix = Plumage + Wings (+Tint); Kirin = Horn + Mane (+Tint);
Tarasque = Shell + Spikes (+Tint); Frost Wyrm has 3 discrete categories. Every discrete option
must exist as a Spine skin/attachment swap on the shared skeleton, not a new rig.

### 9 enemy archetypes

| Archetype | Role | Footprint |
| --- | --- | --- |
| Giant | Boss | **Hex7** (7-tile, huge) |
| Champion | Mini-boss | **Triangle** (3-tile) |
| Brute | Melee tank | Single |
| Stalker | Fast melee hunter | Single |
| Archer | Ranged physical | Single |
| Caster | Ranged special | Single |
| Shaman | Area special | Single |
| Swarmling | Swarm (tiny) | Single |
| Stingling | Swarm (tiny) | Single |

Same 7-clip animation set per archetype (idle/move/attack/cast/hit/KO/victory as applicable).
**Element variants are palette/skin swaps**, not new rigs — enemies can appear in any of the 10
elements via recolor.

### 10 DRAFT region bosses (later-phase work)

All ten (`boss_r01_hollow_warden` … `boss_r10_apex_pair`) are currently DRAFT encounter
templates (composited from the 9 archetypes above, e.g. "two fire champions + 2 archers"). No new
rigs needed beyond archetype variants; budget for unique boss *silhouette* passes (crowns, marks,
scale-ups) once designs are locked.

### Avatar (Beastbinder)

One base body (customizable skin tone + eye color, ColorPicker) plus 4 discrete cosmetic
categories (36 options), from `cosmetic-library.json`:

| Category | Options |
| --- | --- |
| Hair | 9 |
| Outfit | 10 |
| Headwear | 9 |
| Cape | 8 |

The avatar never fights — animation set is **command, cast (support skill), idle, victory** (no
attack/hit/KO). Each discrete option is a Spine skin/attachment on one shared rig.

### Skill icons

| Set | Count |
| --- | --- |
| Beast skills (`BeastSkills`) | **60** |
| Avatar active skills | **6** |
| Avatar passives | **10** |
| **Total skill/passive icons** | **76** |

Circular, painterly, element-colored ring, rarity ring + level badge overlay, matching the
quality-bar finish.

### Item & gear icons

| Set | Count |
| --- | --- |
| Beast gear bases (6 piece types x 5 level bands; 78 pieces in data) | **30** |
| Avatar gear bases (3 piece types x 5 level bands; 30 pieces in data) | **15** |
| Consumables | 5 |
| Skill-training materials | 3 |
| **Total painted item/gear icons** | **53** |

Grouping: a gear base = one piece type (fang, focus stone, barding, mantle, wind charm, collar;
staff, coat, ring) in one level band (1/21/41/61/81); its common/rare/epic versions share it.
**One painted icon per gear base**; rarity is shown by frame colour, glow and small
embellishments (a reusable per-rarity overlay set, not repainted icons).

### VFX

Per element (10: Fire, Water, Earth, Air, Lightning, Ice, Nature, Metal, Light, Dark), the layer
set: **ground decal, shockwave ring, radial burst, embers/sparks, rune glyphs** — hand-painted
flipbook PNGs, additive composite.

Status effects needing an on-apply burst + looping aura/icon: **Stun, Shield, Taunt, Cleanse,
Heal, Buff, Debuff, Burn (DamageOverTime/fire), Poison (DamageOverTime/nature), Knockback.**
(~10 status treatments x 2 = 20 pieces.)

### Hex board tiles & region backgrounds

10 regions (`regions.json`), each needing a tileset + parallax/background treatment:

| Region | Element flavor |
| --- | --- |
| r01 Verdant Hollow | Nature — moss, stones, fireflies |
| r02 Emberreach | Fire — ash ridges, smouldering peak |
| r03 Tidefall | Water/Ice — sea caves, drowned ruins |
| r04 Stormcrag | Air — wind-cut crags, cloud line |
| r05 Rustwood | Metal/Earth — iron trees, ore ground |
| r06 Frostmere | Ice — frozen lake, snowfields |
| r07 Thunderspire | Lightning/Air — glassy spires |
| r08 Deepwild | Nature — ancient towering forest |
| r09 Cinder Throne | Fire/Earth/Metal — volcanic, black glass |
| r10 Worldcrown | Fire/Water — summit, twin elements |
| r11 Duskmeridian (post-game, Phase 3) | Light/Dark — endless gold-and-violet evening, dawn and dusk in one sky |

### UI kit

Frames (portrait, item, skill card), buttons (primary/secondary/disabled states), HP/shield/XP
bars, turn-order portrait ring (active/upcoming/KO states), skill detail card **with hex-range
diagram area** (needs a clean vector-friendly hex grid treatment) and a small glossary
definition popup (tags as chips; highlighted terms in the description), region map node icons. The UI
typeface is already chosen — **Fredoka SemiBold** (SIL OFL, bundled with the game) — so frames,
buttons and cards should be designed around its rounded letterforms; no font needs to be supplied.

## 4. Phasing for quotes

> **SUPERSEDED (2026-09-25)** by the AI-assisted pipeline (section 0). Kept for the record and in case
> parts of the work (Spine animation, UI kit, VFX) are commissioned later. The phase *order* still guides
> production: Phase 1's three beasts are the approved starter trio.

**Phase 1 — vertical slice**
- 3 beasts: Phoenix, Golem, Kirin (confirmed)
- 2 enemy archetypes (Brute, Archer)
- 1 region (r01 Verdant Hollow: tileset + background)
- Fire, Earth, Light VFX sets (5 layers each = 15 pieces)
- ~12 skill/item icons
- Core UI kit (frames, buttons, bars, portrait ring, skill card)

**Phase 2** — remaining 7 beasts, 7 enemy archetypes, remaining ~117 skill/item icons (129
total), remaining 7 element VFX sets (Water, Air, Lightning, Ice, Nature, Metal, Dark), remaining
9 regions, launch cosmetics (all 36 avatar options + 63-option beast subset: 3 per each of the 21
discrete beast categories; colour pickers excluded).

**Phase 3** — 10 DRAFT region bosses, quoted only after the draft boss designs are approved.

**Phase 3, post-game (DRAFT)** — r11 Duskmeridian tileset + background (Light/Dark); the post-game
twin boss "Dusk and Dawn" (two giants, Light and Dark; Normal and Hard share the silhouette pass);
its four lair looks: Dawnshade Horn (Kirin) and Dawnshade Crown (Basilisk), plus the Hard-only
variants Radiant Dawnshade Horn and Eclipse Dawnshade Crown.

**Post-launch** — remaining 113 beast cosmetic options as drops (212 options total in data).

**Sign-off:** the producer approves the style sheet before Phase 1, and each phase's deliveries.

### Quote-request table

| Deliverable | Qty | Price | Weeks |
| --- | --- | --- | --- |
| Beast illustration + 7-clip Spine rig | 3 (P1) / 10 total | | |
| Enemy archetype illustration + rig | 2 (P1) / 9 total | | |
| Avatar base + command/cast/idle/victory rig | 1 | | |
| Avatar cosmetic skins (hair/outfit/headwear/cape) | 36 options (all) | | |
| Beast cosmetic skins, launch subset | 63 options (21 categories x 3) | | |
| Skill/passive icons | 12 (P1) / 76 total | | |
| Gear/item icons (one per base) | 53 (45 gear bases + 8 items) | | |
| Rarity frame/glow/embellishment overlay set | 3 tiers | | |
| VFX element sets (5-layer flipbook) | 3 sets (P1) / 10 total | | |
| Region boss silhouette passes | 10 (P3, quote after design approval) | | |
| Status-effect VFX (burst + aura) | 10 | | |
| Region tileset + background | 1 (P1) / 10 total | | |
| Core UI kit | 1 set | | |
| Post-game region r11 tileset + background (Light/Dark) | 1 (P3) | | |
| Post-game twin boss silhouette pass (Dusk and Dawn; Normal and Hard share it) | 1 (P3, quote after design approval) | | |
| Post-game lair looks: Dawnshade Horn / Crown + Hard variants (Radiant / Eclipse) | 4 options (P3) | | |

## 5. Budget ballparks (rough, pending real quotes)

> **SUPERSEDED (2026-09-25)**: no beast illustrations are being commissioned. Kept for the record.

| Item | Range |
| --- | --- |
| Chibi character illustration | $35-100 / character |
| Spine rig (per character) | $100-200 |
| Illustration + rig bundle | $250-400 / character (full 7-clip animation set costs more) |
| VFX starter pack (per element, ~5 layers) | $50-150 |
| Icon (skill/item) | get quotes — no reliable public range found |
| **Spine Professional license** | **$369 per animating artist** — each animator needs their own non-Education seat |

## 6. Licensing & contract checklist

> **SUPERSEDED (2026-09-25)** for the beasts, which are made in-house (section 0). Keep it for any work that
> is commissioned later. Its AI line is answered by decision 10.

- [ ] Work-for-hire / full IP assignment to [studio] (placeholder: producer's legal entity name), not a license
- [ ] All source files delivered (PSD layers, Spine project files, raw VFX flipbook frames)
- [ ] Full commercial rights, no residual usage restrictions
- [ ] **No AI-generated final assets** unless explicitly disclosed and pre-approved
- [ ] Credit terms agreed up front (in-app credits page vs. none)
- [ ] Revision rounds specified per deliverable (suggest 2 included rounds, rate for extra)
- [ ] Kill-fee / milestone payment schedule if using phased delivery
- [ ] Confirm Spine license type (non-Education) is the artist's own responsibility or reimbursed

## 7. Decisions

1. Phase 1 beasts: Phoenix, Golem, Kirin; VFX sets Fire, Earth, Light.
2. Gear: one painted icon per gear base (45); rarity via frame colour, glow, embellishments.
3. Cosmetics: launch = all 36 avatar options + 63 beast options (3 per category); other 113 post-launch drops.
4. Producer signs off the style sheet before Phase 1 and each phase's deliveries.
5. Quality bar described in words; producer attaches a mood board.
6. No time-zone preference; async reviews, written feedback, 24-48h cycles.
7. Spine pinned to the latest stable editor version `spine-monogame` supports at kickoff (same major.minor).
8. Bosses are Phase 3, quoted after draft boss designs are approved.

9. **Approach (2026-09-25):** beast art is made with the AI-assisted pipeline in `Tooling/ArtLab/`, with
   the producer as art director (pick, personality word, notes, final approval). Commissioning is superseded.
10. **AI-assisted assets are approved by the producer** for in-game use, each one individually (recorded
    in `Tooling/ArtLab/provenance/`), **with disclosure where a store requires it**: Steam's Content Survey
    asks developers to disclose pre-generated AI content, so the Steam submission discloses it; check the
    other stores' policies at submission. Human IP-likeness review of every candidate stays mandatory.
11. **Personality words** (producer-approved): Phoenix Fierce, Golem Cute, Kirin Mystic, Leviathan serene
    and regal, Griffin bold and brave, Thunderbird wild and energetic, Frost Wyrm wise and wry, Treant
    gentle, Tarasque grumpy, Basilisk sly. All ten beasts' art is final (the trio 2026-09-25, the other
    seven 2026-09-26), each approved individually (`Tooling/ArtLab/provenance/<id>.md`).

**Remaining question:** which 3 options per beast category ship at launch (artist can quote the
count now; the producer picks the specific options before Phase 2).
