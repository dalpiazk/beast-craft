# Economy: gold, the Trader, gear, consumables and cosmetics

**Status: BUILT; every number is a TUNABLE STARTING VALUE and all content (names, stats, looks) is
DRAFT pending producer review.** Runtime code under `BeastCraft/Assets/_Project/Scripts/Runtime/Economy`
(namespace `BeastCraft.Economy`); content under `BeastCraft/Assets/_Project/Data/{Items,Economy,Cosmetics}`
and `Data/Skills/drop-tables.json`; paced by the balance simulator's `--mode campaign`
([`docs/balance/campaign-pacing-report.md`](../balance/campaign-pacing-report.md), "Economy") and
measured in battle by `--economy-probe`. Save shape: [progression-and-saves.md](progression-and-saves.md)
(schema 4).

The lead / user decisions this implements: campaign difficulty **assumes typical gear**; skill tomes
teach **own-species skills only**; **at most one consumable per battle** (each worth at most about
+0.3 of a level); gear **sells back at 25%**, gear only; cosmetics are **per species** (not per body
type) plus the avatar's; look sources are **starter, shop, boss-exclusive, milestone, low-chance
battle drops**, with a **premium tier reserved** for future purchases and **idle (AFK) rewards** a
future source.

---

## The loop

| Earned by | Spent on |
| --- | --- |
| **Gold** on every clear (dens, passes and lairs pay more) and by selling gear | The **Trader**: gear, skill materials, skill tomes and avatar skills, consumables, looks |
| **Gear** from battle drops, the Trader, and every pass's (a common) and lair's (rare or epic) first clear | Worn by beasts and the avatar (stats); sold back for 25% |
| **Consumables** from the Trader | One per battle, used as it begins, spent win or lose |
| **Looks** from starters, the Trader, lairs, milestones and rare battle drops | Worn by the avatar and each beast of the look's species; no stats |

One currency (gold). Gold never buys power the difficulty does not already assume: the encounter
table is calibrated for a player in **typical gear** (below), and consumables are small.

## Gold

`drop-tables.json` (schema 2) `Gold`: a clear of (shape, level) pays

`round((Base + PerLevel x level) x shape multiplier x (1 + v / 100)) + FirstClearBonus (on the cell's first clear)`,

then the caller's `RewardModifiers` (multiplier, flat bonus). Starting values: `Base 10, PerLevel 2`
(so `G(L) = 10 + 2L`: 12 gold at level 1, 110 at 50, 210 at 100), `VariancePct 10`, shapes solo x1.2,
elite x1.5, squad x1.0, horde x1.1, first clear +5. The campaign's modifiers
(`CampaignRules.RewardModifiersFor(node)`): dens (Elite) x1.5, passes (Gate) +5, lairs (Boss) +10.
Paid on a player victory only (`PostBattleAward.AwardGold`, `BattleSession.ApplyRewards`), into
`PlayerSave.Gold` through `Wallet` (held in [0, 9,999,999]; income past the ceiling is lost).

Earned (campaign model, p50): **977** gold in region 1, **4,559** in region 5, **9,026** in region 10,
**49,953** over the campaign, plus about 8,300 from selling replaced gear — **14% of all gold**
(user target 10-15%) — (design targets ~0.9k / 4.4k / 8.8k / 55k).

### Seed streams

Every economy roll has its own `LootRoller.DeriveSeed(battle seed, stream)`, so switching a part of
the economy on or off never moves the battle, the material rolls or each other:

| Stream | Draws |
| ---: | --- |
| 0 | Material drops (unchanged) |
| 1 | Gold variance |
| 2 | The battle's consumable effects |
| 3 | Gear drops |
| 4 | The cosmetic drop |
| 5 (of the node seed) | A pass's or lair's first-clear gear (`CampaignRules.NodeRewardStream`) |
| 6 (of the node seed) | A Trader's stock (`ShopService.StockStream`) |

## The Trader

A map's **trading post** (a Shop node) opens `ShopService` (the `IShopService` of
`CampaignRules.Trade`), and **every camp has a travelling trader** too (the game opens the shop with
`CampaignRules.ShopContextFor(run, campNode)`): every path crosses the camp row, so a player meets a
Trader about once per stage (about every 9 battles in the campaign model). **The camp trader is
approved (user decision)**; without it a Trader is met every ~36 battles and the economy's pacing
does not hold.

**Stock** is rolled once per trading post (`ShopContext.NodeKey` = region/stage/node/seed) from the
node's seed and **frozen into the save** (`PlayerSave.Shops`, the last 16): leaving, coming back or
reloading never re-rolls it, and purchases stick. `Data/Economy/shop-tables.json` gives each level
band a listing count per category, rolled in a fixed order with weighted draws without replacement
from pools sorted by id:

| Category | Listings | Pool | Price (price units) |
| --- | --- | --- | --- |
| Materials | 1-3 | shard from level 1 (stacks of up to 2), crystal from 21, core from 41 (never before the tier's first-clear band) | 2.1 / 7 / 24.5 at the Trader's level |
| Gear | 2-3 | shop-tagged beast and avatar commons (weight 75) and rares (25) of the Trader's band or the one before; never epics | 3 common, 8 rare, at the piece's MinimumLevel + 10 |
| Skills | 0-2 | tomes of skills an owned beast's species learns (**own species only**), up to 10 levels before it would; the avatar's actives and non-default passives once the avatar is level enough (levels 5-70) | tome 5.6 at the learn level; avatar skill 8.4 at its level |
| Consumables | 2-3 | those of the Trader's region (stacks of up to 2) | 0.7 common, 1.4 rare, at the Trader's level |
| Looks | 1-2 | shop looks of the region not yet usable | 5.6 common, 14 rare, at the region's middle level |

A **price unit** at a level is `10 + 2 x level` (the squad gold curve), so prices keep pace with
income. Gear **sells back** for 25% of its price (epics, never sold, are valued at 10.5 units). The
prices are the design's x0.7 (**approved, user decision**): the Trader is met every ~9 battles, not
the design's ~12. Gear is cheaper still (3 / 8 units, the design's x0.5): it keeps the gold from
selling replaced gear at about 14% of all gold (user target 10-15%).

`ShopService.TryBuy(save, context, listing, targetBeastId)` validates first and changes the save only
on success: `Bought`, or `UnknownListing`, `SoldOut`, `NotEnoughGold`, `StackFull`,
`IneligibleTarget` (a tome needs a beast of a species that learns it, within the window),
`AlreadyKnown`, `LevelTooLow` (avatar skills), `AlreadyUnlocked` (looks), `UnknownItem`.
`TrySellGear(save, instanceId)` refuses worn gear (`Equipped`). `ShopServiceStub` offers nothing.

## Gear

`Data/Items/gear-library.json`: **78 beast pieces** and **30 avatar pieces** in five bands
(`MinimumLevel` 1 / 21 / 41 / 61 / 81). Per band six beast pieces — Striker fang (Attack) and focus
stone (SpecialAttack), Bulwark barding (Defense + HP) and warding mantle (SpecialDefense + HP), Swift
wind charm (Speed) and keen collar (CritChance) — each common (shop, drops) and rare (shop, drops,
lair rewards); from band 41 an epic of each (lairs only, never sold). Avatar: staff
(SpecialAttack), coat (HP / Defense / SpecialDefense), ring (Speed + crit), common and rare per band.
`AvatarGearSO` gained `MinimumLevel` (gated on the avatar's level by `GearRules.EquipAvatarGear`).

**Budget** (`GearLibraryValidator`, run by the importer and the tests against the roster): a piece's
points are `sum(Flat / ref + Pct)` with `ref` the roster's mean of the stat at `MinimumLevel + 10`,
Speed x1.5, each crit point 0.01, and must be within 10% of **common 5%, rare 8%, epic 12%** in the
first band, scaled per band by `(T(11) / T(MinimumLevel + 10))^0.65` (`T` = the roster's mean stat
total): a level adds a smaller share of a stat the higher it is, so the same share buys more levels
late, and the scale keeps a piece's worth in levels-equivalent flat. No piece may add more than 25%
to any species' stat at its minimum level; MoveRange is not allowed. Flat modifiers where they round
cleanly (per-band obsolescence), a percent remainder otherwise.

**Measured** (`--economy-probe`: every cell replayed at its calibrated multiplier, no scouting,
levels-equivalent against the team one level above the enemies):

| Three pieces | L1 | L50 | L100 | Target |
| --- | ---: | ---: | ---: | --- |
| Common | 0.69 | 0.74 | 0.69 | ~0.7 |
| Rare | 1.56 | 1.27 | 1.04 | ~1.2 |
| Epic (rare at L1) | 1.56 | 1.78 | 1.15 | ~1.8 |
| Typical (see below) | 0.69 | 1.21 | 1.49 | — |

The balance guard holds under `--gear rare` (`--seeds 12345,777,4242`: elemental normalized overall
means -2.8 to +2.0 against +/-4, neutral -3.8 to +6.2 against +/-7); the committed default report and
the guard stay **gearless**.

**Drops** (`drop-tables.json` `GearDrops`, stream 3, with `RewardModifiers.Gear`): elite 8% common and
1.5% rare, solo 3% common, squad and horde 1% common — a piece of that rarity from the encounter
level's band's drop pool. **Passes and lairs** (`CampaignRules.ResolveBattle(..., economy)`, first
clear only): a guaranteed **common** of the band at a pass (stage gate; user decision), an epic from
the boss pool at a region's lair (a rare below band 41). Only lairs guarantee rare or epic gear.

### Typical gear and the shipping difficulty

The balance simulator's `--gear typical` is what a player normally wears (weapon / armor /
accessory): band 1-20 three commons; 21-40 a rare weapon; 41-60 rare weapon and armor; 61-80 an epic
weapon, rare armor and accessory; 81-100 epic weapon and armor, a rare accessory. The shipping
`encounter-difficulty.json` is calibrated with it (the lead's / user's decision), as are the boss
templates' `DifficultyOverride`s:

```
dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --gear typical --write-difficulty BeastCraft/Assets/_Project/Data/Encounters/encounter-difficulty.json
```

Applied after the combat merge (behaviour bonds, tiered targets): against the gearless table the
`elemental` multipliers rose by 0-5.1% except `horde` at level 100 (-4.1%; see the log); see the tuning log, "Shipping difficulty table in typical gear". The tuned report
stays gearless. The `--mode campaign` model measures the modelled player's gear
against this profile ("Gear at typical", 22-96% of slots by boss).

## Consumables

`Data/Items/consumable-library.json` (`ConsumableSO`): **at most one per battle**, chosen before the
fight (`BattleSetup.Consumables`), used as it begins — before the team's bonds and the avatar's
opening passives, on stream 2 — and spent by `ApplyRewards` win or lose (once). A team consumable is
applied by every living beast to itself (as a bond is); an enemy one by the team's first living beast
to every living enemy (less their status resist). No consumable = exactly the old battle.

| Consumable | Effect | Region | Measured LE (L50 / mean) |
| --- | --- | ---: | ---: |
| Fury Draught | team +4% Attack and SpecialAttack, 3 turns | 1 | 0.20 / 0.17 |
| Iron Tonic | team +4% Defense and SpecialDefense, 3 turns | 1 | 0.39 / 0.22 |
| Hawk-eye Drops | team +12 crit, 3 turns | 2 | 0.21 / 0.22 |
| Smoke Bomb | enemies -5% Attack and SpecialAttack, 2 turns | 3 | 0.25 / 0.22 |
| Venom Flask | enemies: 50% a damage-over-time of power 8, 3 turns | 4 | 0.25 / 0.26 |

Left out, with findings for the combat lane: **speed consumables** (the design's Quickstep Salve
+10% Speed and Smoke Bomb -10% enemy Speed) measured **negative** (-0.4 to -0.7 LE) — a pre-battle
Speed change makes battles worse for the team; worth a look at the ATB gauge; and a **consumable
shield** displaced the stance bonds' longer shields (net negative). No heals (battles start at full
HP), revives, or cleanses yet (the validator now accepts a team `Cleanse`; see "Open items").
Percent buffs round to nothing on the tiny stats of the first few levels.

## Skills for sale

**Tomes** teach a skill of the target beast's own species (no cross-species tutoring yet — a
producer item) up to 10 levels before the beast would learn it. **Avatar skills**: all six actives
and the seven non-default passives, each sold from its avatar level (5-70) once the avatar reaches
it, until learned.

## Cosmetics

`Data/Cosmetics/cosmetic-library.json`: **per-species looks** — each of the ten species has its own
parts (two, three for the frost wyrm: horns, wings, tail; wings where the species has them) and a tint
colour — plus the avatar's hair, outfit, headwear, cape and hair / skin / eye colours: 38 categories,
212 looks. **No stats.** Unlock keys are `"categoryId/optionId"` (category ids are global),
account-wide, in `PlayerSave.Cosmetics`; appearances are `PlayerSave.AvatarAppearance` and each
`OwnedBeast.Appearance` (a beast wears only its species' categories; a category not chosen reads as
its default).

| Source | How | Per category |
| --- | --- | --- |
| default | free | exactly one |
| starter | free from the start | at least one (1-3) |
| shop | the Trader (from `MinRegion`) | common or rare price |
| boss | a region lair's first clear (`UnlockId` = the region); never sold | at least one per region (each species' second part; avatar headwear, cape and outfit) |
| milestone | `Milestones`: a beast of the species reaching level 25 / 50 / 75 / 100 (any beast for an avatar look), avatar level 50, the first boss, all ten bosses | species parts at 50 and 100 (the frost wyrm's tail at 25), avatar looks |
| drop | a low chance per won battle (`drop-tables.json` `CosmeticDrops`: elite 2%, solo 1%, squad / horde 0.5%), a look not yet owned, stream 4 | two per species (three for the frost wyrm), three for the avatar |
| premium | **reserved for future purchases; never obtainable with gold or play** | one per species' first part, avatar hair / outfit / headwear |

`CosmeticRules`: `IsUsable` (free or unlocked), `TrySetOption` / `TrySetColor` (colours free),
`Worn`, `Unlock`, `UnlockBossLooks`, `UnlockMilestones`, `RollDrop`, `RepairAppearances` (back to the
default). The importer (Beast Craft/Data/Import Cosmetics) writes the category assets (source,
unlock id, region, rarity and art key in `UnlockTags`; no art yet), the avatar's schema and one
`CreatureCustomizationSchema` per species on its `CreatureSpeciesSO`. Campaign model: about 31 looks
bought, 14 from lairs, 14 from milestones and 3 from drops per campaign.

**Idle (AFK) rewards** are a future source (designed separately, built after this): the numbers
leave room for them — gold held at every boss is under two Trader visits' income, looks and drops
are unlocks rather than power.

## Pacing (`--mode campaign`)

`CampaignEconomyModel` plays the economy inside the campaign model on its own random stream: gold,
drops, pass / lair rewards, the Trader at every trading post taken and every camp, and a greedy
shopper (the focus skill's gate material when it waits without one; the best gear upgrade; two
consumables; one skill; one look while two price units stay in the purse; replaced gear sold; a
consumable at every den, pass and lair battle). Purchases do not change the clear chance.

| Gate | Target | Result |
| --- | --- | --- |
| Want-list affordability (spent / wanted per visit) | p50 55-80% | 63% (p10 5%, p90 100%) |
| Gold from selling gear | about 10-15% of all gold | 14% (8,340 of ~58,300) |
| Visits where nothing meaningful in stock is affordable | < 5% | 0% |
| Gold held at every boss | < 2 visits' income (p50) | 1.0-1.6 |
| Focus skill L5 / L10 / L15 / L20 | 15-20 / 72-88 / 162-198 / >= 270 | 17 / 86 / 190 / 323 |

Every earlier campaign gate still holds (545 battles p50). The gate material is almost never wanted:
drops and pity deliver materials before a gate waits.

## Reproduce

```
dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign --self-check --out docs/balance/campaign-pacing-report.md
dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --economy-probe --out <scratch>/probe.md
dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds 12345,777,4242 --gear rare --out <scratch>/guard-rare.md
```

## Open items (producer review)

- Every name, number and look (DRAFT); no art (placeholder `ArtKey`s), no UI.
- Speed consumables and consumable shields (above); cross-species tomes; a cleanse consumable
  (`ConsumableLibraryValidator` accepts an instant team `Cleanse` effect, but consumables are used
  as a battle begins, before anything can stun or burn the team, so one needs an in-battle use or
  a trigger first; no content yet).
- Premium looks and idle rewards (future).
