# Content appendix: items and looks

**Status: DRAFT pending producer review.** Naming and flavour rules for the item and look libraries.
Tone, world and the global consistency rules live in [content-bible.md](content-bible.md); this is its
items-and-looks appendix. Only `DisplayName` / `Description` text follows these rules; ids, stats,
prices, sources and unlocks never change in a content pass (mechanics: [economy-and-shop.md](economy-and-shop.md)).

Files: `Data/Items/gear-library.json`, `Data/Items/consumable-library.json`,
`Data/Skills/skill-library.json` (`Materials` only), `Data/Cosmetics/cosmetic-library.json`.
`shop-tables.json`, `idle-rewards.json` and `drop-tables.json` hold no player-facing text.

## Gear

A piece's name is **prefix + piece noun**. The prefix says the band and rarity; the noun says the slot
and stat, and never changes between bands, so players can see the upgrade at a glance.

| Band (MinimumLevel) | Common (shop, drops) | Rare (shop, drops, early lairs) | Epic (lairs only) |
| --- | --- | --- | --- |
| 1 | Bramble | Wayfarer | - |
| 21 | Copper | Pathfinder | - |
| 41 | Ironbound | Warden | Sealforged |
| 61 | Silversteel | Stormcaller | Titanforged |
| 81 | Sunmetal | Starbound | Primeval |

- **Common** prefixes are materials that climb from hedgerow scraps to sunmetal.
- **Rare** prefixes are the Beastbinder's road ranks (wayfarer, pathfinder, warden, stormcaller,
  starbound): gear a seasoned traveller would trust.
- **Epic** prefixes are mythic origins (a broken seal, a titan, the first age). Epic descriptions
  always end "A lair's prize, never sold."; rare band-81 descriptions may say "the finest ... a Trader sells".

| Noun | Slot | Stat focus the description must hint at |
| --- | --- | --- |
| Fang | WeaponOrCore | Attack ("physical blows") |
| Focus Stone | WeaponOrCore | Special Attack ("elemental power / attacks") |
| Barding | ArmorOrShell | Defense + health |
| Mantle | ArmorOrShell | Special Defense + health |
| Wind Charm | Accessory | Speed |
| Collar | Accessory | CritChance ("weak spots", "critical hits"); plus Speed from band 21 |
| Staff (avatar) | Weapon | Special Attack ("skills and heals") |
| Coat (avatar) | Armor | health + both Defenses |
| Ring (avatar) | Trinket | Speed + CritChance |

Descriptions: one or two sentences, at most about 140 characters, a concrete image first and the stat
hint second. Never promise a number or a stat the piece's `Modifiers` do not have.

## Consumables and materials

Consumables keep short, plain "what it is" names (Fury Draught, Iron Tonic, Hawk-eye Drops, Smoke Bomb,
Venom Flask; the names also appear in economy-and-shop.md). The description gives one sensory detail,
then who it affects (the team or the enemy) and roughly for how long; chance-based effects say "may".

Skill materials (Essence Shard / Crystal / Core) keep their names; the description keeps the exact
mechanical tail ("Feeds a skill ... XP; opens its ... breakthrough (level N).") after a short flavour clause.

## Looks

Look names are one to three words, unique inside their category, and end with the part's noun
(Horn, Mane, Plumage, Wings, Fins, Scales, Crest, Shell, Spikes, Horns, Tail, Canopy, Blossoms, Core,
Runes, Crown) unless a single noun reads better (Pinions, Carapace, Heartstone, Diadem). Category names
stay plain UI labels ("Kirin Horn", "Hair Colour").

- **Fit the beast.** Words come from the species' element and body: Phoenix fire and rebirth,
  Leviathan tides and deep sea, Golem stone and strata, Griffin sky and eagle feathers, Thunderbird
  bolts and squalls, Frost Wyrm rime and glaciers, Treant leaves and bark, Tarasque iron and rivets,
  Kirin dawn light and pearl, Basilisk dusk and shadow.
- **Default** looks name the species' natural state (Ember Plumage, Iron Shell, Cloud Mane).
- **Starter / common shop / drop** looks are simple pattern or colour words (Barred, Dappled, Speckled,
  Moonpearl, Wildfire).
- **Rare** looks (shop rare tier, premium) get the flourish: crowns, halos, stars, auroras
  (Halo Horn, Midnight Diadem, Starfire Plumage).
- **Milestone** looks start with the milestone's name: Seasoned (beast level 25), Veteran (50),
  Elder (75), Ascendant (100); avatar looks: Veteran Binder Garb (avatar level 50), First Seal Laurel
  (first boss), Crown of Seals (all ten bosses).
- **Boss (lair)** looks share one signature word per region, taken from the region's element rather
  than its name, so they survive a region rename:

| Region | Element theme | Signature | Looks |
| --- | --- | --- | --- |
| r01 | Nature (woodland) | Mossgrove | Mossgrove Mane (Kirin) |
| r02 | Fire | Emberflare | Emberflare Wings (Phoenix) |
| r03 | Water | Undertow | Undertow Scales (Leviathan), Undertow Cape (avatar) |
| r04 | Air | Galeborn | Galeborn Wings (Griffin) |
| r05 | Metal (iron forest) | Rustleaf | Rustleaf Spikes (Tarasque), Rustleaf Helm (avatar) |
| r06 | Ice | Rimeglass | Rimeglass Wings (Frost Wyrm) |
| r07 | Lightning | Stormcrown | Stormcrown Wings (Thunderbird) |
| r08 | Nature (deep forest) | Elderroot | Elderroot Blossoms (Treant), Elderroot Cloak (avatar) |
| r09 | Fire and Earth (volcanic) | Magmavein | Magmavein Runes (Golem) |
| r10 | The summit | Summit | Summit Scales (Basilisk), Summit Regalia (avatar) |

Milestone entries' own `DisplayName`s read "Title (condition)", e.g. "Elder (beast level 75)".
