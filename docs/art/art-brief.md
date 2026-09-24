> **DRAFT — for freelance quoting only.** Numbers derived from `content/data/` and `docs/design/` on
> 2026-09-24. All display text in the data is itself DRAFT pending producer sign-off; do not
> treat names/lore below as final.

# Beast Craft — Art & Animation Brief

Beast Craft is a mobile-first (Android now, iOS later; desktop too) turn-based hex-grid PvE
creature-tactics game built in MonoGame. You control a Beastbinder (an avatar who never fights
directly) commanding a roster of ten mythic beasts against wild, Gloam-corrupted creatures.

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

| Species | Element | Stance | Visual/personality cue |
| --- | --- | --- | --- |
| Phoenix | Fire | Ranged | Proud flame bird, strikes from afar, renews itself in fire |
| Leviathan | Water | Vanguard | Calm deep serpent, coils, shields in scales, outlasts |
| Golem | Earth | Vanguard | Stone hillside given legs, slow, stubborn wall for allies |
| Griffin | Air | Skirmisher | Eagle/lion hybrid, bold, hit-and-run with talons |
| Thunderbird | Lightning | Skirmisher | Storm-diving opener, glass-cannon speedster |
| Frost Wyrm | Ice | Vanguard | Old, wry dragon in rime, slows/freezes, patient |
| Treant | Nature | Vanguard | Gentle forest guardian, shelters and mends allies |
| Tarasque | Metal | Vanguard | Grumpy armored river-beast, endures and bites back |
| Kirin | Light | Ranged | Serene luminous healer, fights with light not claws |
| Basilisk | Dark | Ranged | Sly shadow-serpent, gaze attack, hunts the weak |

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

### UI kit

Frames (portrait, item, skill card), buttons (primary/secondary/disabled states), HP/shield/XP
bars, turn-order portrait ring (active/upcoming/KO states), skill detail card **with hex-range
diagram area** (needs a clean vector-friendly hex grid treatment), region map node icons.

## 4. Phasing for quotes

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

## 5. Budget ballparks (rough, pending real quotes)

| Item | Range |
| --- | --- |
| Chibi character illustration | $35-100 / character |
| Spine rig (per character) | $100-200 |
| Illustration + rig bundle | $250-400 / character (full 7-clip animation set costs more) |
| VFX starter pack (per element, ~5 layers) | $50-150 |
| Icon (skill/item) | get quotes — no reliable public range found |
| **Spine Professional license** | **$369 per animating artist** — each animator needs their own non-Education seat |

## 6. Licensing & contract checklist

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

**Remaining question:** which 3 options per beast category ship at launch (artist can quote the
count now; the producer picks the specific options before Phase 2).
