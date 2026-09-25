# Content bible

**DRAFT — every name, description and piece of lore here, and all the game text written from it,
is pending producer review.** This is the reference for anyone writing player-facing text (beasts,
skills, enemies, regions, seals, bosses, map locations, items, looks), so it all sounds like one
game. It records flavour only. Ids and balance numbers are set by the data files and the balance
simulator, never by this document; if text here and the data ever disagree, the data wins and the
text gets fixed.

Where the text lives:

| Text | File |
| --- | --- |
| Beasts | `content/data/Creatures/beast-roster.json` (`DisplayName`, `Description`) |
| Beast skills, Beastbinder arts and passives | `content/data/Skills/skill-library.json` |
| Enemy types and their skills | `content/data/Encounters/enemy-library.json` |
| Bosses | `content/data/Encounters/encounter-library.json` (`Templates`) |
| Regions and Seals | `content/data/Campaign/regions.json` |
| Map location names | `content/data/Campaign/location-names.json` |
| Gear, consumables, materials, looks, shop text | `content/data/Items`, `content/data/Cosmetics`, `content/data/Economy`, the skill library's `Materials` (naming rules: [content-items.md](content-items.md)) |

---

## World premise

The **Farwild** is a vast, half-mapped land of forests, coasts, peaks and fire-mountains. Ten great
beasts are its oldest forces, one for each element: Phoenix (Fire), Leviathan (Water), Golem
(Earth), Griffin (Air), Thunderbird (Lightning), Frost Wyrm (Ice), Treant (Nature), Tarasque (Metal),
Kirin (Light) and Basilisk (Dark). They are species, not single legends: many of each roam the
Farwild, and the young ones can be befriended.

Something is wrong in the wild. **The Gloam**, a dusky, shimmering haze, is creeping down from the
Worldcrown, the peak at the far end of the land. Creatures it settles on turn feral and territorial:
they attack travellers and guard what they should share. The Gloam is not evil in itself and not
the Dark element — it is a fog that makes the wild forget itself. Beating a gloamed creature shakes
the haze loose; it slinks back into the wild, dazed but whole. **No one dies in this world**:
creatures and beasts are *defeated*, *knocked out* or *fall*, never killed.

Long ago the first Beastbinders held the Gloam back with ten **Seals**, great carved stones, one in
each region, each holding part of their bond with the beasts. The Gloam has since swallowed the
Seals, and in every region the strongest creature has grown around its Seal and guards it jealously.

## The Beastbinder

The player is a **Beastbinder**: an explorer who travels with a team of beasts, bound to them by
trust rather than by force. The Beastbinder **never fights**. On the field they stand behind the
team and use **binder arts** (the avatar's active skills: a rallying cry, mending light, a shield
thrown over the team, hexes that slow or weaken the enemy) and carry **bond passives** (auras and
reflexes that fire on their own: a shield when a beast is in trouble, a surge when a friend falls).
In player text, call the avatar "the Beastbinder" (or "you"), never "the avatar"; its skills
support, shield, heal, curse or quicken — they never strike with a weapon.

### The binding limit (level cap)

A bond can only grow so strong before it needs something to anchor it. That ceiling is the
**binding limit**: the highest level any of the Beastbinder's beasts can reach. A new Beastbinder's
bond reaches level 12. Each Seal freed from a region's guardian holds a share of the first
Beastbinders' bond; claiming it raises the binding limit (to 22, 32 … 92, and 100 at the ninth). Beasts
at the limit bank some XP (up to three levels' worth) and grow the moment the next Seal is claimed.
The Beastbinder has no limit.
The tenth Seal, the Crown Seal, raises nothing further: it is the one that stops the Gloam at its
source. In text, "Seal" is always capitalised; "binding limit" is lower case.

## The ten beasts

Stance, element and kit come from the data; the personality follows them.

- **Phoenix** (Fire, Ranged). Proud, bright and a little reckless. A bird of living flame that
  strikes from afar with burning bolts, and once a battle wraps itself in renewing fire. Fragile up
  close, and knows it.
- **Leviathan** (Water, Vanguard). Calm, patient, unhurried. A sea-serpent whose whirlpools drag
  enemies to it; it shields itself in its coils and simply outlasts whatever breaks against it.
- **Golem** (Earth, Vanguard). Stubborn and loyal, few words. A hillside that learned to stand: slow,
  immovable, it challenges every enemy nearby and raises granite walls around its friends.
- **Griffin** (Air, Skirmisher). Bold and restless, loves an open sky. Half eagle, half lion, it
  darts in with talons and lances of wind, hurls enemies aside with its wings, and is gone again.
- **Thunderbird** (Lightning, Skirmisher). Excitable, fastest of all. Its wingbeat is thunder: it
  opens every fight with one great dive, then crackles through swarms with chained lightning.
  Hits first; cannot take many hits back.
- **Frost Wyrm** (Ice, Vanguard). Old, dry-humoured, never hurries. A dragon wrapped in rime
  that chills and slows everything near it and waits for the cold to win.
- **Treant** (Nature, Vanguard). Gentle and protective, the team's grandparent. A walking tree that
  shelters allies under bark, mends the worst-hurt friend and lashes enemies with thorned vines.
- **Tarasque** (Metal, Vanguard). Grumpy, dependable, hits back. A river-beast in a shell of iron
  plates and spines; where the Golem only endures, the Tarasque cracks armour and crushes.
- **Kirin** (Light, Ranged). Serene and kind. A luminous deer-like beast that shuns horns and claws:
  it heals the whole team, blesses it, and calls down a single pillar of judging light.
- **Basilisk** (Dark, Ranged). Sly, clever, secretly soft-hearted. A serpent-king whose gaze can
  turn a foe to stone; it slips through shadow to find the weakest enemy and finish the fight.
  Dark is an element like any other, not wickedness: the Basilisk is a friend.

## Enemies: the gloamed

Enemies are **gloamed** creatures and wild folk, never roster beasts. Their element varies — any
type may carry any of the ten — so names and descriptions stay element-free ("a Fire Giant" is the
game combining the element with the type, not a separate enemy).

| Type | Role | Lore |
| --- | --- | --- |
| Giant | boss (seven hexes) | Hill-sized elders of the wild that sleep for centuries; the Gloam woke them cross. They crush, glare, stamp the ground and roar. |
| Champion | mini-boss (three hexes) | The strongest creature of a pack, swollen with Gloam. Cleaves the front line and hexes the weakest. |
| Brute | melee tank | Thick-hided horned beasts that plant themselves in the way and smash. |
| Stalker | fast melee hunter | Lean, shadowy hunters that circle wide and pounce on whoever is hurt worst. |
| Archer | ranged physical | Gloamed wild folk who keep to high ground and loose arrows from afar. |
| Caster | ranged special | Gloamed hedge-mages whose spiteful bolts seek the weakest and may leave it burning. |
| Shaman | area special | Wild-folk elders with gnarled staffs, calling squalls down on groups. |
| Swarmling | swarm (bite) | Tiny scuttling mites that come in dozens; each bite is small, the swarm is not. |
| Stingling | swarm (sting) | Buzzing, wasp-like pests whose sting may leave a lingering poison. |

## Regions

Ten regions, walked in order, each a map of wilds, dens, camps, trading posts, passes and a lair.
The element listed is the region's *flavour*; generated battles anywhere can field any element.

| Region | Levels | Flavour | Boss (its Seal) |
| --- | --- | --- | --- |
| r01 Verdant Hollow | 1-10 | Nature: mossy woodland, old stones, fireflies | Mossjaw the Warden (Moss Seal) |
| r02 Emberreach | 11-20 | Fire: ash-dusted ridges under a smouldering peak | Ember Twins (Ember Seal) |
| r03 Tidefall | 21-30 | Water and Ice: sea caves, drowned ruins | Drowned Colossus (Tide Seal) |
| r04 Stormcrag | 31-40 | Air: wind-cut crags above the clouds | Gale Titan (Gale Seal) |
| r05 Rustwood | 41-50 | Metal and Earth: a forest of ringing iron trees | Iron Stags (Iron Seal) |
| r06 Frostmere | 51-60 | Ice: a frozen lake and its snowfields | Frost Matriarch (Rime Seal) |
| r07 Thunderspire | 61-70 | Lightning and Air: storm-struck glass spires | Thunder Court (Thunder Seal) |
| r08 Deepwild | 71-80 | Nature: the oldest forest, humming and huge | Deepwild Heart (Root Seal) |
| r09 Cinder Throne | 81-90 | Fire, Earth and Metal: the volcano's heart | Cinder King (Cinder Seal) |
| r10 Worldcrown | 91-100 | Fire and Water: the summit where the Gloam rises | Flame and Flood (Crown Seal) |

The arc: the Hollow is gentle and the Gloam thin; each region is wilder and the Gloam thicker, until
the Worldcrown, where fire and water pour from the same summit and the haze wells up between them.
Light and Dark have no home region yet (open question for the producer).

Map locations (`location-names.json`, eight names per kind per region, drawn by the map's seed):

| Kind | Node | What it is |
| --- | --- | --- |
| Wilds | Battle | Open country where gloamed creatures roam. |
| Den | Elite | A strong creature's home turf; tougher, better pay. |
| Camp | Rest | A safe spot to rest and train one beast; a travelling trader drops by. |
| Trading post | Shop | A trader's stall, wagon or market. |
| Pass | Gate | The guarded way on to the next part of the region. |
| Lair | Boss | Where the region's guardian keeps its Seal. |

## Post-game region (DRAFT)

**r11 Duskmeridian** — Light and Dark, the post-game region (all text DRAFT pending producer
review; answers "Light and Dark have no home region yet" above). Beyond the Worldcrown, once the
Crown Seal has stemmed the Gloam at its source, the last of the haze thins into an endless
gold-and-violet evening where radiant dawn and deep dusk share one sky. Light and Dark are two edges of
the same evening here, never good against evil: the Kirin and the Basilisk are both at home. The
eldest creatures of the Farwild gather to test any bond whole enough to reach them.

| Region | Levels | Flavour | Boss |
| --- | --- | --- | --- |
| r11 Duskmeridian (post-game) | 100 | Light and Dark: an endless gold-and-violet evening | Dusk and Dawn (no Seal) |

- **Boss: Dusk and Dawn.** Two ancient giants, one of radiant dawn and one of deepest dusk, who keep
  the evening in balance (the Worldcrown's Flame and Flood, mirrored). On Hard the same pair comes
  "with halo and shadow at full blaze". There is no Seal to claim: the binding limit is already at its
  peak, and the reward is the challenge itself (and a few looks).
- **Difficulty words.** The player chooses Normal or Hard when setting out; Hard text leans on
  *radiant* and *eclipse* (Light and Dark at full strength), never on grimness.
- **Looks.** Signature word **Dawnshade** (dawn = Light, shade = Dark): Dawnshade Horn (Kirin),
  Dawnshade Crown (Basilisk); Hard-only: Radiant Dawnshade Horn, Eclipse Dawnshade Crown.
- **Map names** (`location-names.json`, r11): dawn, dusk, halo, shade, meridian and lantern words
  (Dawnfield Road, Halo Grotto, Meridian Market, Dawndusk Court).

## Tone

Warm, adventurous, slightly whimsical mythic fantasy for all ages: wonder first, danger second,
never grim. Picture a travel journal with sketches in the margins.

- No gore, blood, death or cruelty: creatures are defeated, knocked out, sent packing. Burning and
  poison are "burning" and "poison", described as stinging or smouldering, not wounding.
- Light humour is welcome in item and look descriptions and the odd place name; never in a way that
  mocks the player or undercuts a boss.
- No real religions, cultures, peoples or brands as costume, and no existing-IP names. Mythic
  creatures (the ten beasts, giants) are fine.
- Present tense, active voice. Speak about the thing, not the design ("A spring of light heals the
  team", not "This skill is the healer's niche").

## Naming rules

- **Display names:** 1–3 words, Title Case, at most 24 characters, easy to read aloud. No apostrophes,
  no hyphen chains, no invented spelling soup ("Tharn'Qull"). Compound words are fine when they read
  at a glance (Emberreach, Stormglass).
- **Element words** (use them to theme text, and only where the data says that element): Fire —
  ember, cinder, ash, flame; Water — tide, brine, deep, kelp; Earth — stone, granite, boulder;
  Air — gale, wind, sky, cloud; Lightning — storm, thunder, spark, static; Ice — frost, rime, snow,
  glacier; Nature — moss, thorn, root, bloom; Metal — iron, rust, copper, ore; Light — radiant, dawn,
  halo; Dark — shade, dusk, shadow. "Gloam" is only the corruption, never an element word.
- **Enemy names** stay element-free; **boss names** may carry their element because their elements
  are fixed.
- **Seals** are "<Motif> Seal" (Moss Seal, Crown Seal).
- **Map locations** read as places on a map (Mossy Glade, Cinder Pass); a den, camp or trading post
  name says what it is where it can (…Den, …Camp, …Market, …Stall). Names are unique within a region.
- Ids never change with a rename: text is `DisplayName` / `Description` / label data only.

## Text length

| Text | Length |
| --- | --- |
| Any display name, location name | 1–3 words, ≤ 24 characters |
| Item, consumable, material, look description | 1–2 sentences, ≤ ~140 characters |
| Skill, passive, bond description | 1–2 sentences, ≤ ~200 characters; state the mechanic (numbers as authored, before level scaling) |
| Beast, region, boss, Seal description | 1–2 sentences, ≤ ~280 characters |

Mechanical hints must match the data exactly (targets, range in hexes, durations, chances, once per
battle). When the numbers change, the description changes with them; when a description would need
a mechanic that does not exist, say what the skill really does instead.

Boss templates carry `"Draft": true` in `encounter-library.json` until the producer signs the bosses off
(the EditMode tests require it while they are placeholders). The flag is never shown to the player, so
no draft marker goes in a `DisplayName` or `Description` (the encounter validator rejects `[DRAFT]` there).

Items, consumables, materials and looks have their own appendix: [content-items.md](content-items.md)
(gear prefixes, boss-look signature words per region, milestone titles).
