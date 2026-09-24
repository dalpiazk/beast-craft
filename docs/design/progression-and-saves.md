# Progression, saves and the battle session

How a player's progress is stored, how beasts and the avatar level up, the region campaign they
play through, and the single entry point a scene calls to fight a battle from save data and pay it
out. This covers the runtime code under
`BeastCraft/Assets/_Project/Scripts/Runtime/{Save,Session,Progression,Campaign,Economy}` and the tooling that
tests it outside Unity. The battle rules themselves are in [battle-system.md](battle-system.md); the
economy (gold, the Trader, gear, consumables, cosmetics) is in [economy-and-shop.md](economy-and-shop.md);
pacing numbers come from [`docs/balance/campaign-pacing-report.md`](../balance/campaign-pacing-report.md)
(the region campaign) and [`docs/balance/pacing-report.md`](../balance/pacing-report.md) (skills and
materials).

Everything here is pure C# over two thin seams: an injected JSON engine and an injected storage
backend. The one file backend is `FileSaveStorage` (below); there is no UI and no Unity Gaming
Services integration yet.

---

## Save format

### `PlayerSave` (schema 4)

One versioned aggregate per player (`BeastCraft.Save.PlayerSave`):

| Field | Type | Contents |
| --- | --- | --- |
| `SchemaVersion` | `int` | The schema the data is in. `CurrentSchemaVersion` is **4**. 0 or missing is never valid. |
| `Beasts` | `List<OwnedBeast>` | The beast collection, in the order obtained. |
| `Avatar` | `AvatarProgress` | The avatar's own level and XP. |
| `AvatarSkills` | `AvatarSkillBook` | The avatar's active and passive skill books. |
| `Materials` | `MaterialInventory` | Held skill materials, cleared (shape, band) cells and pity counters. |
| `Gear` | `GearInventory` | Every owned piece of beast and avatar gear, worn or not (schema 2). |
| `AvatarEquippedGear` | `string[3]` | The avatar's worn gear by slot, indexed by `(int)AvatarGearSlot` (schema 2). |
| `Campaign` | `CampaignProgress` | Owned seals, each unlocked region's progress, the current region and the expedition in progress (schema 3; see "Region campaign"). |
| `Gold` | `int` | Gold held, 0 to 9,999,999 (`Economy.Wallet`; schema 4). |
| `Consumables` | `List<ConsumableStack>` | `{ConsumableId, Quantity}`, one stack per consumable, 1 to its max stack (schema 4). |
| `Shops` | `List<ShopVisit>` | Each Trader's stock as first seen, frozen: `{NodeKey, Listings[] {Category, ItemId, Quantity, Price, Remaining}}`, the most recent 16 (schema 4). |
| `Cosmetics` | `CosmeticCollection` | `Unlocked`: account-wide `"categoryId/optionId"` look unlocks (defaults and starter looks are free and never listed; schema 4). |
| `AvatarAppearance` | `CustomizationSelection` | The avatar's chosen looks and colours; a category not listed reads as its default (schema 4). |

`OwnedBeast` is one beast in the collection:

| Field | Type | Contents |
| --- | --- | --- |
| `BeastId` | `string` | Unique within the save; how teams and other save data refer to the beast. Two beasts of one species are two beasts. |
| `Progress` | `BeastProgress` | `{SpeciesId, Level, Xp, BankedXp}`: which species it is, its level (1-based), XP toward the next, and XP banked while held at the level cap (`BankedXp`, schema 3). |
| `Skills` | `BeastSkillBook` | Learned skills with their `SkillProgress` (level, XP, tier) and the 3 equipped slots (slot order is fire priority). |
| `EquippedGear` | `string[3]` | Worn beast gear by slot, indexed by `(int)GearSlot` (schema 2). |
| `Appearance` | `CustomizationSelection` | The beast's chosen looks, from its own species' categories only (schema 4). |

**JsonUtility-compatible by construction.** Every type reachable from `PlayerSave` is
`[Serializable]` with public fields and a parameterless constructor, and every map is a list —
`JsonUtility` drops dictionaries and properties (a test walks the type tree and enforces this).
Everything is named by stable string ids (species, skill, passive, material and gear ids, all under
the never-rename rule), never by asset reference. Empty slots are `null` or `""`: `JsonUtility`
writes null strings back as empty ones, and every reader treats both as empty.

`PlayerSave.EnsureInitialized()` replaces any null collection or sub-object with its empty default
and drops null list entries, so code reading a loaded save never meets a null (`JsonUtility` never
produces them; other serializers and hand-edited files can). It never touches ids or numbers (a
null campaign string becomes "").

### Campaign progress (schema 3)

`CampaignProgress` (`BeastCraft.Campaign`):

| Field | Type | Contents |
| --- | --- | --- |
| `Seals` | `List<string>` | Owned seal ids (`regions.json` `Seals`), in the order granted. Key items, not materials. |
| `Regions` | `List<RegionProgress>` | `{RegionId, StagesCleared, BossCleared}` per **unlocked** region (an entry = unlocked). |
| `CurrentRegionId` | `string` | The region last entered, or "". |
| `ActiveRun` | `MapRun` | The expedition in progress (the stretch of the region map being explored): `{RegionId, Stage, Seed, Nodes, CurrentNodeId, Cleared, Attempts, NodeAttempts}`. **`RegionId == ""` means none** — JsonUtility writes every class field, so the run is never null. |

`MapRun.Nodes` is a snapshot of the generated map (node id = index), so a content or generator change
never moves the ground under a saved expedition. `MapNode` is `{NodeId, Layer, Lane, Type, Level,
ShapeId, TemplateId, EncounterSeed, Next[], Kind, X, Y, LabelKey}`; `Type` is `MapNodeType` (`Battle 0,
Elite 1, Rest 2, Shop 3, Gate 4, Boss 5`, saved as its number — append only), the pacing role;
`Kind` is the location the player sees, `LocationKind` (`Wilds 0, Den 1, Camp 2, TradingPost 3, Pass 4,
Lair 5`, append only; today one per type, separate so the open world can add landmarks), `X` / `Y` its
normalized position on the region map and `LabelKey` its display-name key. The four location fields
were added to schema 3 before it shipped (no bump): a stored map without them is placed on load
(`MapRun.EnsureInitialized` calls `NodeMapGenerator.Place` with the run's own seed, exactly as
generation would), and the validator reports a position outside 0-1 or an unknown kind. `CurrentNodeId` is −1 before the first
step. A new save (`PlayerSave.CreateNew`) starts with `CampaignProgress.StartingRegionId` (`r01`)
unlocked.

### Gear inventory

`GearInventory` holds two lists of `OwnedGear {InstanceId, GearId}`:

- `BeastGear` — `GearId` is a `GearSO.GearId`;
- `AvatarGear` — `GearId` is an `AvatarGearSO.AvatarGearId`.

An instance id is unique across both lists; two copies of the same gear are two instances, each
wearable once. `AddBeastGear(gearId)` / `AddAvatarGear(gearId)` generate ids `gear1`, `gear2`, ...
from `NextInstanceNumber` (saved, only ever grows, skips any id already taken).

Worn gear lives on the owner (`OwnedBeast.EquippedGear`, `PlayerSave.AvatarEquippedGear`) as
instance ids by slot. The rules are `GearRules`':

- the slot equipped into must be the gear's own slot (`GearSO.Slot` / `AvatarGearSO.Slot`);
- a slot holds one instance; equipping into an occupied slot replaces it (the old instance returns
  to the inventory, unequipped);
- an instance is worn by at most one owner, in one slot (`EquippedElsewhere` otherwise — unequip it
  there first);
- beast gear needs the beast at or above the gear's `MinimumLevel` when equipped (`LevelTooLow`).
  Beasts are never de-levelled, and `StatCalculator` ignores under-level gear anyway.

`EquipBeastGear` / `EquipAvatarGear` return a `GearEquipResult` (`Equipped`, `UnknownOwner`,
`UnknownInstance`, `UnknownGear`, `SlotMismatch`, `LevelTooLow`, `EquippedElsewhere`) and look gear
definitions up through an `ISaveGearCatalog` (which `BattleContent` implements).

### Serializing, schema versions and migrations

`SaveSerializer` turns a `PlayerSave` into JSON and back through an injected `ISaveJsonSerializer`.
In the game that is `JsonUtilitySaveSerializer` (Unity's `JsonUtility`, optional pretty-print).

- **Writing** stamps `SchemaVersion` to the current version, then serializes.
- **Loading** never throws. It reads only the version first; refuses empty or unreadable text, a
  missing / 0 version, and a version newer than the build; runs the migration steps from the save's
  version up to the current one; reads the result; calls `EnsureInitialized`; and validates. Every
  failure is a `SaveLoadResult` with `Success = false` and an `Error`; a success carries the `Save`,
  its `SourceVersion`, whether it was `Migrated`, and the validation `Issues`.

A migration step (`ISaveMigration`) upgrades the JSON **text** from `FromVersion` to
`FromVersion + 1`, because an old shape may not fit the current type; a step typically reads the
text into a frozen DTO of the old shape (or into `PlayerSave` when only values change) and writes it
back with the serializer it is handed. One step per `FromVersion`; a missing or throwing step fails
the load with a reason. The registered steps are `SaveMigrations.All()`:

| Step | Upgrade |
| --- | --- |
| `SaveMigrations.AddGear` (1 → 2) | A v1 save owns no gear: read it into the current type (the gear fields take their empty defaults), fill in anything missing, write it back. |
| `SaveMigrations.AddCampaign` (2 → 3) | A v2 save has no campaign and no bank: read it into the current type (empty campaign, `BankedXp` 0), fill in anything missing, unlock `r01`, write it back. Beasts keep their levels — one above the starting cap (12) stays there and only banks until the cap passes it. |
| `SaveMigrations.AddEconomy` (3 → 4) | A v3 save has no economy: read it into the current type (no gold, consumables, Trader visits or unlocks; every appearance at its defaults — starter looks are free anyway), fill in anything missing, write it back. Nothing else moves. |

Schema 3 was not yet shipped when the economy landed on top of it, so the map's location fields
(`MapNode.Kind` / `X` / `Y` / `LabelKey`) were added to schema 3 directly rather than bumping it;
a stored map without them is placed on load.

To change the schema: bump `PlayerSave.CurrentSchemaVersion`, add a step whose `FromVersion` is the
previous version, and add a test that loads an example of the old shape. Purely additive fields
with a sensible default need no bump. (`SaveSerializer`'s current version is overridable, so a
migration chain can be tested ahead of a real bump.)

`SaveStore` binds a serializer to an `ISaveStorage` (`Exists`, `TryRead`, `TryWrite`, `Delete` by
slot name) — the thin IO seam. Cloud Save is a later backend; local files are implemented:

### File storage (`FileSaveStorage`)

`FileSaveStorage : ISaveStorage` is pure System.IO, rooted at a directory passed in. In the game,
`UnitySaveLocations.Default()` builds one over `Application.persistentDataPath/saves` — the only
Unity-dependent line in the save system (the UnityStub's `Application.persistentDataPath` is a temp
directory, so CiLint, BalanceSim and the EditMode runner compile and run it).

*WebGL.* On WebGL, `Application.persistentDataPath` is an in-memory file system (Emscripten's
IDBFS) that reaches the browser's IndexedDB only when JavaScript calls `FS.syncfs` after a write.
`FileSaveStorage` does not make that call, so on WebGL a save would be lost on reload. A platform
follow-up if WebGL is ever targeted (see Known gaps).

```csharp
SaveStore store = new SaveStore(UnitySaveLocations.Default(), new SaveSerializer(new JsonUtilitySaveSerializer(), catalog));
store.Save("main", save);
SaveLoadResult loaded = store.Load("main");
```

| Rule | Behaviour |
| --- | --- |
| Layout | Slot `main` → `main.save`; backup `main.save.bak`; `main.save.tmp` only mid-write. |
| Slot names | 1–64 ASCII letters, digits, `_`, `-`; starts with a letter or digit; not a Windows device name (`CON`, `NUL`, `COM1`…). No dots or separators, so no traversal and no custom extension. Anything else is `SaveFileError.InvalidSlot`. |
| Atomic write | Full text to the temp file, flushed (write-through), then swapped in with `File.Replace` (old main → backup), falling back to copy/delete/move where `Replace` is unsupported. A crash leaves the old or the new save readable, never a torn one. One backup generation. |
| Corrupt main | Not valid UTF-8, blank, or failing the content check (default: trimmed text is `{…}`, a cheap truncation check). Reads then use the backup; a write deletes a corrupt main instead of rotating it over a good backup. |
| Read fallback | `Read(slot)` → `SaveFileResult` with `Source` (`Main` / `Backup`), `MainFileProblem` (why main was skipped — worth logging), `LastWriteUtc`. Both unusable → `Corrupt` (or `Io`); neither present → `NotFound`. `ReadBackup(slot)` reads the backup alone. |
| `SaveStore.Load` | Over an `IBackupSaveStorage` (`FileSaveStorage`): the `SaveLoadResult` carries `StorageSource` (`Main` / `Backup` / `None`) and `MainFileProblem`. When the main file passes the content check but fails to load (bad or missing schema version, unreadable JSON, a failed migration), the backup is loaded instead, with `MainFileProblem` = "read but failed to load (…)". No retry when the main save is from a newer build (loading and re-saving the older backup would overwrite it); if the backup fails too, the main failure is returned. Over a plain `ISaveStorage`, a read is reported as `Main`. |
| Writes | Refuse text that would fail the content check (`InvalidContents`); the directory is created on demand. |
| Encoding | UTF-8 without BOM; a leading BOM is tolerated on read. |
| Exists / Delete | `Exists` = a main or backup file is present (content not checked). `Delete` removes main, backup and temp; false (`NotFound`) when there was nothing. |
| Errors | `Read` / `Write` / `Remove` never throw; IO failures are `SaveFileError.Io` results with a message. The `ISaveStorage` methods reduce them to booleans. |
| Concurrency | One process-wide lock around every operation: safe across threads and instances in one process. No cross-process locking — two game processes on one directory race, last swap wins (each file still whole). |

`SaveSlotIndex.Build(storage, json)` lists every slot (`ListSlots()` — valid names with a main or
backup file) newest first as `SaveSlotInfo`: timestamp, which file was read, the `SchemaVersion`
header (no migration or validation) and any problem, for a load/continue menu.

### Validation

`SaveValidator.Validate(save, ISaveContentCatalog, ISaveGearCatalog[, ISaveEconomyCatalog])` reports problems as
`SaveIssue {Kind, Path, Id, Message}` (a `Path` like `Beasts[2].Skills.Known[0].SkillId`). It never
throws and never changes the save: an id that has disappeared from the content is the game's to
handle (typically ignore the entry and log it), not a reason to refuse the file. With null catalogs
only the structural checks run.

| `SaveIssueKind` | Reported when | Needs catalog |
| --- | --- | --- |
| `MissingBeastId` | a beast has no id | — |
| `DuplicateBeastId` | two beasts share an id | — |
| `UnknownSpecies` | a beast's species id is empty, or not in the catalog | content (for unknown) |
| `UnknownSkill` | a learned beast or avatar-active skill id is empty or unknown | content (for unknown) |
| `UnknownPassive` | a learned avatar passive id is empty or unknown | content (for unknown) |
| `UnknownMaterial` | a held material id is empty or unknown | content (for unknown) |
| `EquippedNotLearned` | an equipped skill slot names an id the book has not learned | — |
| `DuplicateSkill` | a book lists the same skill as learned twice | — |
| `InvalidValue` | a level, XP, tier, quantity or pity counter is out of range, or a slot is past the last one | — |
| `UnknownGear` | an owned gear instance's gear id is empty or not in the gear catalog | gear (for unknown) |
| `DuplicateGearInstance` | a gear instance id is empty or used twice | — |
| `UnknownGearInstance` | a worn slot names an instance the right inventory list does not hold | — |
| `DoubleEquippedGear` | an instance is worn in more than one slot or by more than one owner | — |
| `GearSlotMismatch` | a worn instance is in a slot other than its gear's own | gear |
| `GearLevelTooLow` | a beast wears gear above its level (the gear has no effect), or the avatar wears avatar gear above its level (avatar gear has a `MinimumLevel` since schema 4) | gear |
| `UnknownRegion` | a region id (progress, current region, expedition) is empty or unknown | content (for unknown) |
| `UnknownSeal` | an owned seal id is empty or unknown | content (for unknown) |
| `DuplicateCampaignEntry` | a region or seal is listed twice | — |
| `InvalidMapRun` | nodes stored without an expedition; an expedition in a locked region, with no nodes, a node id that is not its index, an unknown node type or location kind, a map position outside 0-1, a link that is not to the next row, or a current / cleared node not on the map | — |
| `UnknownConsumable` | a held consumable id is empty or unknown, or held in two stacks | economy (for unknown) |
| `UnknownCosmetic` | an unlock key or a worn look names no known category / option, or a category of another owner (a species' look on the avatar or another species) | economy (except empty / duplicate) |
| `CosmeticNotUnlocked` | an appearance wears a look that is neither free nor unlocked | economy |
| `InvalidShopVisit` | a Trader visit with an empty or duplicate key, or a listing with no id, an unknown category, or counts / price out of range | — |

Gold outside 0-9,999,999 and a consumable stack outside 1 to its max stack are `InvalidValue`s.
The economy catalog is `Economy.EconomyContent` (the consumable and cosmetic libraries).
`CosmeticRules.RepairAppearances` drops every worn look that is no longer usable, so it reads as the
default again.

Negative `BankedXp`, `StagesCleared`, run `Stage` / attempts and node levels outside 1-100 are
`InvalidValue`s.

`SaveContentCatalog` is an `ISaveContentCatalog` over plain id sets; `SaveContentCatalog.FromData(roster, library)`
builds it from the authored `beast-roster.json` and `skill-library.json`, and
`FromData(roster, library, regions)` adds `regions.json`'s region and seal ids
(`ISaveContentCatalog.IsKnownRegion` / `IsKnownSeal`, added with schema 3). A catalog built without
campaign data answers every non-empty region and seal id as known.

---

## Beast and avatar level

Both are parallel, deliberately uncoupled rules: `AvatarProgression` (avatar level, scales its
stats through `AvatarStatsSO.GetStatsAtLevel`) and `BeastProgression` (a beast's `BeastProgress`,
which `BattleUnitFactory` builds the beast at). Both cap at level 100, keep XP toward the next level,
level up as far as it allows, hold nothing at level 100, and normalize level and XP on every write.
A null progress is a no-op.

| | Avatar (`AvatarProgression`) | Beast (`BeastProgression`) |
| --- | --- | --- |
| XP to next level | `200 + 16 × level` (216 at L1, 1,784 at L99) | `160 + 13 × level` (173 at L1, 1,447 at L99) |
| Every finished battle | 8 XP (won, lost or stalemate) | 6 XP to every **fielded** beast — also when knocked out |
| Clear bonus (player victory) | `50 + 5 × enemy level` | `50 + 5 × enemy level`, only to fielded beasts **still standing** at the end |
| Benched | — | the **bench share** of a standing beast's XP (below) |
| Level-gap falloff | yes | yes (fielded and benched) |
| Level cap | **none** (lead decision) | the campaign's beast level cap, with a bank (below) |

Skill practice XP is separate (`SkillProgression`: 10 XP per time a skill fired, at most 20 uses per
award), is paid by the same reward step, and is never cut by the falloff or the cap.

**Level-gap falloff** (`LevelGapXp`). A battle's **whole** XP (participation included) is cut by
the earner's level before the award minus the encounter level: 0 or below 100%, +1 60%, +2 25%,
+3 10%, +4 5%, +5 and above 0% (integer maths, rounding down). Old content cannot be farmed: in the
campaign model, 25 won battles of region 1 after region 5's boss pay nothing, and 25 of region 5's
second stage pay 0.00 levels (p90 0.02). It costs the fielded team about 14% of its battle XP along
the way (the early rows of each stage sit below it).

**Bench share** (`BeastProgression.BenchXp` / `AwardBench`). A beast left on the bench earns
`min(100%, 10% + 9% × levels below the enemy)` of what a standing fielded beast earns, then the
falloff on its own level: 10% at or above the enemy's level, 55% five levels down, all of it from ten
levels down. A reserve therefore settles a few levels behind the team instead of falling ever
further back, and a new recruit catches up at the full rate. Campaign model: the bench is 5.0-6.0
levels behind the fielded team at every boss from region 3; a level-1 recruit joining at region 5 is
7.7 behind by the end of region 6.

> **Bench numbers retuned, pending lead/user review.** The lead's decision was 50% + 7.5% per level
> (capped at 100%), chosen for its predicted outcome, "reserves ~6-7 levels behind". That estimate
> assumed the fielded beasts earn their full XP; under the falloff and knockouts they earn about 70%
> of it, and 50% + 7.5% kept the bench only 1-2 levels behind (`--mode campaign`). The formula shape
> is kept; the constants (`BenchShareBasePermille` 100, `BenchSharePerLevelPermille` 90) are what
> produce the intended outcome.

**Level cap and bank** (`BeastProgression.AddXp(progress, xp, levelCap)`, `LevelCap`). Below the cap
a beast levels as usual. At the cap its `Xp` fills to one short of the next level and the rest goes
to `BeastProgress.BankedXp`; `Xp + BankedXp` holds at most the XP of `LevelCap.BankLevelLimit` (3)
levels from its level, and more is lost. `LevelCap.Release(progress, newCap)` spends the bank at a
raised cap (the campaign releases every beast when a seal is granted), so a cap rise pays out at most
three levels at once. A beast obtained (or migrated) above the cap keeps its level and only banks.
The cap is `LevelCaps.BeastCap`: the highest owned seal's cap, or the starting cap (see "Region
campaign"). In the campaign model the cap never binds for a player who follows the content (banked
levels at every seal: 0); it stops a grinder from running more than a region ahead.

> **XP numbers are autonomous defaults pending user review.** The clear bonus was `40 + 4 × level`
> for both until the region campaign; the campaign clears about 70% of its battles (80% squads,
> harder elites, gates and bosses, a loss retried), and at the old rate the team fell behind and into
> a loss spiral (1,000+ battles). `50 + 5 × level` keeps it on the content in about 540 battles.

**Pacing.** Two models, both `dotnet run --project Tooling/BalanceSim -c Release -- --mode ...`:

- `--mode campaign` (the region campaign; see "Region campaign" below) — the one the XP numbers are
  tuned to: the fielded team's median level is within a level of every gate and boss (target ±3),
  the avatar's on it.
- `--mode pacing` (1,000 Monte Carlo campaigns of 500 battles, encounter level `1 + battle / 5`, 80%
  cleared, a beast fielded in every battle and knocked out in 20% of them). With the raised clear
  bonus it runs slightly ahead, and the falloff holds it there: the median is one level above the
  encounter level at every 50-battle checkpoint (target ±3), both for the avatar and the beast
  ([pacing-report.md](../balance/pacing-report.md)):

| Battle | 50 | 100 | 150 | 200 | 250 | 300 | 350 | 400 | 450 | 500 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Encounter level | 10 | 20 | 30 | 40 | 50 | 60 | 70 | 80 | 90 | 100 |
| Avatar p50 | 11 | 21 | 31 | 41 | 51 | 61 | 71 | 81 | 91 | 100 |
| Beast p50 | 11 | 21 | 31 | 41 | 51 | 61 | 71 | 81 | 91 | 100 |
| Beast p10 – p90 | 10–11 | 20–21 | 30–31 | 40–41 | 50–51 | 60–61 | 70–71 | 80–81 | 90–91 | 100–100 |

`--self-check` runs either model twice and fails on any difference or missed target.

---

## Region campaign

The campaign the player progresses through: ten regions covering levels 1-100. **To the player each
region is a map to explore** (the open world is TBD): four expeditions ("stages") into it, each
crossing a stretch of the region's locations — wilds where wild beasts roam, beast dens, camps,
trading posts, the guarded pass out, and in the fourth the boss's lair, whose seal raises the beast
level cap. **The seeded node map (Slay-the-Spire style) is the internal pacing model behind that
map**, never shown as a node graph: every node carries a location kind, a normalized map position and
a label key (`MapNode.Kind` / `X` / `Y` / `LabelKey`, see "Node maps") so a spatial map UI can place
and name the locations, and when the open world lands only that presentation layer changes. Runtime code under `Runtime/Campaign` (namespace
`BeastCraft.Campaign`); content in `BeastCraft/Assets/_Project/Data/Campaign/regions.json`; paced by
[`docs/balance/campaign-pacing-report.md`](../balance/campaign-pacing-report.md). **The content is a
DRAFT pending producer review** (names, level bands, map rules, bosses).

### `regions.json`

`{SchemaVersion, StartingLevelCap, LevelCapMargin, MapRules, Seals[], Regions[]}` — the usual
Data / build / validator / SO pattern: `RegionLibraryData`, `RegionLibrary.Build` (indexed regions and
seals, each region's effective rules, which regions a boss unlocks), `RegionLibraryValidator`,
`RegionLibrarySO`, and the Editor importer (Beast Craft/Data/Import Regions, validated against
`encounter-library.json`, all or nothing).

| Content | Value |
| --- | --- |
| Regions | `r01` Verdant Hollow (1-10), `r02` Emberreach (11-20), `r03` Tidefall, `r04` Stormcrag, `r05` Rustwood, `r06` Frostmere, `r07` Thunderspire, `r08` Deepwild, `r09` Cinder Throne, `r10` Apex (91-100); 4 stages each; each requires the one before |
| Battle shapes | squad 45, horde 40, solo 15 (every region) |
| Seals | `seal_r01` … `seal_r10`, one per boss, caps 22, 32, 42, 52, 62, 72, 82, 92, 100, 100 (the next region's max + `LevelCapMargin` 2) |
| Starting cap | 12 (region 1's max + 2) |
| Map rules | 11 rows (row 0 to the top), 4 lanes, 4 paths, rest row 9, node weights Battle 70 / Elite 15 / Shop 8 / Rest 7, 1-3 elites from row 3, at most 1 shop, elites +1 level, gates +0, elite shape `elite` |

A region's own `MapRules` override the library's when its `Layers` is above 0. The validator requires
contiguous levels 1-100 from `r01` (`CampaignProgress.StartingRegionId`), each region requiring an
earlier one, existing shapes and gate / boss templates, and seal caps that never fall and always
cover the next region's max level.

### Node maps (`NodeMapGenerator`)

`Generate(region, rules, stage, seed)` is deterministic (one seeded `System.Random`: paths, then
types, then shapes). `Paths` walks go from a lane of row 0 to the row under the top, stepping −1 / 0 /
+1 lanes without crossing an edge already walked (the first two start in different lanes); their
union is the map, and every node of the row under the top leads to the single top node. Row 0 is
Battle, the rest row is Rest ("Camp"), the top is the Gate — or, on the last stage, the Boss; other
nodes are drawn by weight, never an Elite, Rest or Shop straight after its own type, no Elite below
`EliteMinLayer`, no Rest under the rest row. A draw with the wrong number of elites or shops is
re-drawn (20 tries), then fixed up deterministically.

**Node level** (`RowLevel`): `MinLevel + floor(t × (MaxLevel − MinLevel) / T)` with `t = stage ×
(Layers − 1) + row`, `T = Stages × (Layers − 1)` — about 2.25 levels per stage. Elites +1; Gates +0
(`GateLevelOffset`); the Boss exactly the region's max level. Battle nodes draw a shape from the
region's `ShapeWeights`; Elites and generated Gates use the elite shape; the Boss fields its
template. Each node's `EncounterSeed` is `LootRoller.DeriveSeed(mapSeed, NodeId)`.

**Locations** (`NodeMapGenerator.Place`, presentation only): every node becomes a location on the
region map — `Kind` from its type (`LocationKinds.For`), `X` from its lane and `Y` from its row, each
cell centre jittered by up to a quarter lane and a fifth of a row (the Pass or Lair centred at the far
edge), scaled into [0.05, 0.95] and rounded to 4 decimals, and `LabelKey` = `"{regionId}/{kind}/{0-7}"`
(e.g. `r01/wilds/3`, for a localization table of place names). Its draws come from their own stream
(`DeriveSeed(mapSeed, NodeMapGenerator.PlacementStream)`), so placement never changes a map's
structure, types, levels or encounter seeds; rows never overlap (deeper rows lie further in) and lanes
keep their left-to-right order, so the paths read as routes across the region.

### Rules (`CampaignRules`)

| Call | Does |
| --- | --- |
| `StartRun(save, regions, regionId[, stage], seed)` | Generates and stores the stage's map (default stage: the first uncleared, or the last for a replay). Refused for a locked or unknown region, a stage past the next, or while another expedition is in progress. |
| `CanEnter` / `Choices` | Row 0 before the first step; afterwards only the current node's links; never a cleared node. |
| `PlanFor(node, encounters, enemies)` | The node's `EncounterPlan` (template, or generated from its shape, level and seed — a retry fields the same lineup). Null for Rest / Shop. |
| `BattleSeed(node, attempt)` | `DeriveSeed(EncounterSeed, attempt)`: a new battle seed per retry. |
| `ResolveBattle(save, regions, nodeId, outcome)` | Win: the node is cleared and current. A Gate also clears the stage and ends the expedition; the Boss sets `BossCleared`, grants its seal (`GrantSeal`), unlocks the next region and ends it. Loss (lead decision): nothing moves — retry the node or take another path; `Attempts` counts. Rewards are `BattleSession.ApplyRewards(…, beastLevelCap: BeastCap(save, regions))`, not this. |
| `Camp(save, regions, nodeId, beastId)` | Rest node: trains one chosen beast by a standing clear at the node's level (falloff, cap). Battles already start at full HP, so there is no healing. A travelling trader also waits at every camp: the game opens the shop with `ShopContextFor(run, campNode)` (the economy's pacing assumes it; see [economy-and-shop.md](economy-and-shop.md)). |
| `Trade(save, regions, nodeId, shop)` | Shop node (a trading post): opens the `IShopService` — the economy's `ShopService`, whose stock is rolled once and frozen into the save; `ShopServiceStub` or null offers nothing — and marks the node visited. Buying and selling go through the shop with `ShopContextFor(run, node)`. |
| `ResolveBattle(save, regions, nodeId, outcome, economy)` | As `ResolveBattle`, plus the economy's first-clear rewards: a pass's (Gate's) first clear grants a guaranteed common, a lair's (Boss's) an epic (a rare below band 41) and its boss-exclusive looks, then milestone looks (`CampaignResult.GearGranted`, `CosmeticsUnlocked`). |
| `RewardModifiersFor(node)` | The economy's reward modifiers for `ApplyRewards`: dens (Elite) gold x1.5, passes +5, lairs +10. |
| `Retreat(save)` | Abandons the expedition (stage progress already earned stays). |
| `GrantSeal(save, regions, sealId)` | Adds the seal and releases every beast's bank at the new cap. Bosses call it; it is also the hook for future story-event seals. |
| `BeastCap(save, regions)` | `LevelCaps.BeastCap(save.Campaign.Seals, regions)`. |

### Bosses (DRAFT)

Ten authored templates in `encounter-library.json` (`boss_r01_hollow_warden` … `boss_r10_apex_pair`),
shape `elite` (its drop cells), fought at the region's max level, each marked DRAFT PLACEHOLDER in its
`Description`: r01 champion (Nature) + 2 brutes; r02 2 Fire champions + 2 archers; r03 Water giant +
2 casters (Water / Ice); r04 Air giant + 3 stalkers; r05 2 champions (Metal / Earth) + shaman + 2
brutes; r06 Ice giant + 10 swarmlings + 2 archers; r07 Lightning giant + Air champion + 2 casters;
r08 Nature giant + 12 stinglings + 2 shamans; r09 Fire giant + 2 champions (Earth / Metal); r10 two
giants (Fire / Water). Each `DifficultyOverride` (x0.80-x1.20) was calibrated with the simulator's
fixed-encounter set (`--encounter-set fixed --kit elemental`, 64 samples per step) so the bond-aware
scouted pick clears about 50% at that level (the user's boss target) — on the combat rules before the
tiered-target and behaviour work, so it should be re-run after them.

### Pacing (`--mode campaign`)

`dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign [--runs n] [--seed(s)] [--out
path] [--self-check] [--regions path]`: 1,000 Monte Carlo campaigns through the real save, maps and
rules. The player fields 3 beasts (knocked out in 20% of battles each), benches 3, recruits a level-1
beast when region 5 starts, takes an Elite when the team's mean level is at least its level (else a
Battle, else Rest, Shop), camps the lowest bench beast, and retries every loss. Clear chance at equal
level: squad / horde 80%, elite and gates 60%, solo and bosses 50% (the user's tiers), moved across a
level gap along the design's table in log-odds (+1 level: 60% / 36% / 27%). Result (all gates met):

| Gate | Target | Result |
| --- | --- | --- |
| Battles, whole campaign | 400-600 (p50) | 541 (p10-p90 517-569); 52-54 per region, ~17 of them lost retries |
| Fielded level at every gate and boss | p50 within 3 | within 0.3 of the node level everywhere |
| Avatar level at every gate and boss | p50 within 3 | on the node level |
| Bench | 5-8 behind, from region 3 | 5.0-6.0 |
| Recruit | within 8 by the end of region 6 | 7.7 |
| Level cap | never exceeded | 0 |
| Banked levels at a seal | p50 ≤ 3 | 0 (the cap never binds for this player) |
| Grind probe after r05's boss | < 0.05 levels (r01), < 0.5 (r05 stage 2) | 0.00, 0.00 |
| Focus skill (`--mode pacing`'s gates) | L5 15-20, L10 70-90, L15 160-200, L20 295-325 | 17, 86, 190, 323 |

The clear-chance model is an assumption, not measured from fought battles; the tiered targets are
what the encounter table is calibrated to for a scouting player at equal level.

---

## Battle session

`BeastCraft.Session.BattleSession` is the single entry point a scene calls: fight one battle from
save data, then pay it out. It only calls the battle, bond, placement and progression code's public
APIs, in the balance simulator's order.

### Inputs

**`BattleContent`** — the content ids resolve against: species, skills (beast skills and avatar
actives share one id space), passives, team bonds (in application order), and optionally beast and
avatar gear, and optionally the enemy library's `EnemyCatalog` (`Enemies`). Built from the asset
types the importers and `SkillLibraryBuilder` produce; lookups are by stable id, ordinal, first
entry wins, unknown ids return `null`. It also implements `ISaveGearCatalog`.

**`EncounterSetup`** — the opposition. For the game's PvE encounters `EncounterPlan.ToSetup()`
writes one (see `docs/design/battle-system.md`, "Encounters as game content"); callers and tests can
also build one by hand:

- `Arena` (`ArenaSize`, default `Medium`);
- `ShapeId` and `EncounterLevel` — the drop-table shape and the encounter level, copied into
  `BattleSessionResult` for the rewards (null / 0 when not set);
- `Enemies`: `EnemySpec {UnitId?, SpeciesId, Level, SkillIds?, Position?, StatusResist, Element?,
  StatMultiplier}` — `SpeciesId` is a roster species or, failing that, an enemy-library enemy
  (`BattleContent.Enemies`); no unit id means `enemy1..N`; no skill ids means the species'
  `DefaultLoadout`, or an enemy's catalog kit in its `Element` (an empty list means no skills;
  listed skills are level 1, tier 0); `Element` applies to enemy-library enemies only (null = none);
  `StatMultiplier` (default 1, must be above 0) scales the level-computed HP, Attack, Defense,
  SpecialAttack and SpecialDefense with `EnemyScaling`, as the balance simulator calibrates it; no
  position means auto-placed;
- `PrebuiltEnemies`: `BattleUnit`s the caller built itself (enemy team, unique ids), placed at their
  own position and footprint.

**`BattleSetup`**:

| Field | Meaning |
| --- | --- |
| `Save` | The player's `PlayerSave` (read, never written by `Run`). |
| `TeamBeastIds` | `OwnedBeast.BeastId`s in deployment order — the first takes the front-most tile. 1 to 6, no repeats. |
| `IncludeAvatar` | Whether the avatar takes part (default `true`). |
| `AvatarProfile` | The avatar's `AvatarStatsSO`; null gives the no-stats avatar. |
| `AvatarGear` | Override for the avatar's gear. Null (default) uses what the save's avatar wears; any list, even empty, replaces it. |
| `Content` | The `BattleContent`. |
| `Encounter` | The `EncounterSetup`. |
| `Seed` | Seeds the battle's only `System.Random`. |
| `MaxTime` | The battle's time cap (default `BattleTurnExecutor.DefaultMaxTime`). |

### `BattleSession.Run(setup)` → `BattleSessionResult`

1. **Validates everything up front** and reports every problem at once (see below).
2. **Enemies**: explicitly positioned specs and prebuilt enemies are placed first (a position must
   fit the enemy deployment zone with the species' footprint), then the rest are packed front-most
   first by `DeploymentPacker.TryPack`. Each spec enemy is `BattleUnitFactory.CreateBeast` at its
   level with its loadout and status resist.
3. **Team**: each beast is built at its `BeastProgress.Level`, wearing its saved gear, with its
   `BeastSkillBook` loadout (`BattleUnitFactory.CreateBeast(..., gear, position, skillBook, lookup)`),
   and seated on the front-most free player tiles through `PlacementValidator.TryPlaceAll`. Unit ids
   are `"beast:" + BeastId`.
4. **Avatar** (when included): `BattleAvatar.Create(book, skill lookup, passive lookup, profile,
   progress, gear, out passives)`, id `avatar`.
5. **Bonds**: `TeamBondLoadout.For(content.TeamBonds, TeamBondResolver.MembersOf(team species), team units)`.
6. **Battle**: `new TurnManager(beasts + avatar)` and the bond-aware
   `BattleTurnExecutor.RunBattle(turnManager, units, grid, new Random(Seed), avatar, passives, bonds, MaxTime)`.

The result carries `Success`, `Errors` / `Error`, the `Battle` (`BattleResult`) and `Outcome`, the
`Grid`, `Units` (enemies then the team, end-of-battle state; not the avatar), `Avatar`,
`ActiveBonds`, `StartingStats` (every unit's assembled stats before the battle began, by unit id,
avatar included), `TeamUnitIds` / `UnitIdFor(beastId)`, and the usage counts rewards are paid from:
`SkillUsesByBeastId` (by `BeastId`), `AvatarActiveUses` and `PassiveTriggers`.

**Determinism.** The only randomness is the one `Random(Seed)`: same setup, same seed, same battle
(a test compares full turn-by-turn traces across runs).

**Error handling.** `Run` never throws on bad input. A bad setup returns `Success = false` with
every problem in `Errors` and no battle fought:

- no setup, save, content or encounter;
- an empty team, more than 6 beasts, an empty or repeated beast id, a beast not in the save;
- a beast of an unknown species, or of a multi-tile species (the team deploys single-tile beasts only);
- an equipped skill or passive (beast or avatar) the content cannot resolve — strict, rather than
  silently dropping it;
- worn gear (beast, or avatar unless overridden) that is not in the inventory, unknown, in the wrong
  slot, or worn twice (gear below the beast's level is **not** an error; `StatCalculator` ignores it);
- no enemies, an enemy of unknown species or with an unknown skill, an `Element` on a roster species,
  a `StatMultiplier` that is not a positive number, a prebuilt enemy not on the enemy team, a
  duplicate unit id;
- an enemy position outside the enemy zone or taken, enemies that do not fit the zone, a team that
  does not fit the player zone or that `PlacementValidator` rejects.

### `BattleSession.ApplyRewards(save, result, content, shape, encounterLevel, dropTable, rng = null)` → `BattleRewardSummary`

Pays a finished battle into the save:

- **Practice XP** — `PostBattleAward.AwardPractice` to every team beast's skill book (by the unit it
  fought as) and, when the avatar took part, to its active and passive books, with each skill's or
  passive's progression definition from `content` (null uses the defaults).
- **Drops** — on a player victory only, `PostBattleAward.AwardDrops` / `LootRoller.RollClear` for a
  clear of (`shape`, `encounterLevel`) from `dropTable` into `save.Materials`, first-clear bonus and
  pity included. A null table drops nothing.
- **Beast XP** — `BeastProgression.AwardBattle` to every team beast, knocked out = its unit
  `IsDefeated` at the end, after the level-gap falloff on its level, added under the beast level
  cap.
- **Bench XP** — `BeastProgression.AwardBench` to every other beast in the save (the bench share,
  then the falloff), under the same cap.
- **Avatar XP** — `AvatarProgression.AwardBattle` when the avatar took part (falloff; no cap).
- **Economy** (schema 4; [economy-and-shop.md](economy-and-shop.md)) — on a clear, gold from the drop
  table's `Gold` (first-clear bonus when the material roll was a first clear, then the
  `RewardModifiers`' multiplier and bonus) into the wallet on its own seed stream
  (`DeriveSeed(seed, 1)`), so the material rolls never move; with `RewardModifiers.Gear`, the table's
  gear drops (stream 3); with `RewardModifiers.Cosmetics`, its low-chance look drop (stream 4) and,
  after the XP, any milestone looks reached. The battle's consumables (`BattleSetup.Consumables`,
  at most one, used as it began on stream 2) are spent whatever the outcome.

`rng` drives the drop rolls; null seeds one with `LootRoller.DeriveSeed(result.Seed, 0)`, so rewards
are deterministic either way. The summary reports `Applied`, `Error`, `Outcome`,
`SkillLevelsGained`, `BeastXpGained` (by beast id, after the falloff), `BeastLevelsGained`,
`BenchXpGained` (by beast id), `BenchLevelsGained`, `XpBanked` (by beast id, what went to the bank),
`FalloffPercent` (by beast id, team and bench), `BeastLevelCap`, `AvatarXpGained`,
`AvatarFalloffPercent`, `AvatarLevelsGained`, the `Loot`, `GoldGained`, `GearGained`, `ConsumablesSpent` and
`CosmeticsUnlocked`. It refuses — changing nothing — a null save or result, a failed
battle, or a result already paid out (`BattleSessionResult.RewardsApplied`). A team beast no longer in
the save is skipped.

`BattleSession.ApplyRewards(save, result, content, dropTable, rng = null)` does the same for the
`ShapeId` and `EncounterLevel` the battle's `EncounterSetup` named (`EncounterPlan.ToSetup` sets
both); it refuses, changing nothing, a result whose setup named no shape or no level.

Both have an overload taking `int beastLevelCap` before `rng` (the campaign passes
`CampaignRules.BeastCap(save, regions)`); the overloads without it pass `BeastProgression.MaxLevel`,
no cap. `Session` does not depend on `Campaign`: the cap is just a number. Both also have an overload
taking `RewardModifiers` after the cap (the campaign passes
`CampaignRules.RewardModifiersFor(node).With(economy)`).

---

## Runtime roster builder

`BeastCraft.Creatures.Roster.BeastRosterBuilder` is the one mapping from `beast-roster.json` to the
runtime objects, the roster counterpart of `SkillLibraryBuilder`:

- `ApplyCurve(GrowthCurveData, GrowthRateCurve)` — id, keys (as a piecewise-linear `AnimationCurve`), max level;
- `ApplySpecies(SpeciesData, CreatureSpeciesSO, GrowthRateCurve)` — id, display name, description,
  base stats, growth-curve link, elements, stance and footprint;
- `ParseFootprint(string)` — a `UnitFootprint` member name, case-sensitive; missing, empty or
  unparsable is `Single` (the validator only admits `Single` for a beast);
- `BuildAll(roster, out curves)` — fresh in-memory instances of the whole roster, species in file
  order, each named after its id (the caller owns and destroys them).

It writes only JSON-owned fields: a species' icon, evolution options, learnable skills, default
loadout and customization schema are never touched (the skill-library importer wires the skill
lists). The Editor's `BeastRosterImporter` applies it to existing or new assets (keeping their
GUIDs); tests and the balance simulator's `RosterLoader` build the roster through `BuildAll`.

---

## Running the EditMode tests without Unity

```sh
dotnet test Tooling/EditModeTests
dotnet format Tooling/EditModeTests --verify-no-changes
```

`Tooling/EditModeTests/EditModeTests.csproj` (net10.0, C# 9, NUnit 3) globs in the real
`Scripts/Runtime`, `Scripts/Editor` and `Scripts/Tests/EditMode` sources and runs the whole EditMode
suite. It is local-only — deliberately not a CI job, for the Actions budget — and no substitute for
Unity's Test Runner, which exercises the real `JsonUtility` and asset serialization. The authored
JSON data is not copied; the tests find it by walking up from the output directory.

**The `JsonUtility` swap.** The project compiles the `Tooling/CiStubs/UnityStub` source files
directly into the test assembly (not through a project reference) with the
`UNITYSTUB_SYSTEM_TEXT_JSON` symbol defined. Under that symbol the stub's `JsonUtility.FromJson` /
`ToJson(obj[, prettyPrint])` are real, implemented over System.Text.Json and restricted to
JsonUtility's rules: public instance fields only (no properties, no readonly or `[NonSerialized]`
fields), names exactly as declared and case-sensitive, enums as numbers, unknown keys ignored,
malformed input throws. Without the symbol — how CiLint and BalanceSim build the stub — they still
throw `NotSupportedException`. `Object.DestroyImmediate` is an honest no-op in every build. So the
save tests run through `JsonUtilitySaveSerializer` in both places: the real `JsonUtility` in Unity,
its System.Text.Json twin here.

---

## Known gaps / follow-ups

- **Economy content is DRAFT** (gear, consumable and look names and numbers, shop prices); no UI
  and no art (looks carry placeholder art keys). See [economy-and-shop.md](economy-and-shop.md),
  "Open items".
- **Region content is DRAFT.** Region and seal names, level bands, map rules and the ten boss
  templates (names, elements, escorts) are placeholders for producer review; the boss
  `DifficultyOverride`s were calibrated before the tiered difficulty targets and should be
  re-calibrated after them (see "Region campaign").
- **Camp traders (approved).** The economy's pacing relies on a travelling trader at every camp
  (about one Trader visit per stage); the game must open the shop there.
- **Bench share retuned away from the lead's numbers** (10% + 9% per level instead of 50% + 7.5%)
  to reach the intended "reserves ~6 levels behind"; pending lead/user review (see "Beast and avatar
  level").
- **Beast XP defaults need review** (see above), as do the campaign model's tiered clear chances
  and both models' 20% knockout assumption.
- **Enemies wear no gear.** `EnemySpec` has no gear field; a caller needing geared enemies builds
  them itself and passes them as `PrebuiltEnemies`.
- **Storage is local files only.** `FileSaveStorage` has no Cloud Save counterpart, no
  cross-process lock and one backup generation; the content check catches truncation, not subtler
  corruption (no checksum), which the serializer then reports on load (and `SaveStore` retries
  the backup). A slot recovered from its backup is not rewritten automatically: the game should
  log `SaveLoadResult.MainFileProblem` and save again.
- **WebGL saves would not persist.** WebGL's `persistentDataPath` is an in-memory file system that
  needs a JavaScript `FS.syncfs` flush after each write to reach IndexedDB; `FileSaveStorage` does
  not flush (and the whole save path is untested on WebGL). A platform follow-up (a small `.jslib`
  called after `Save`/`Delete`, or a WebGL `ISaveStorage`) if WebGL is ever targeted.
- **Never run in Unity.** The project has not been opened in an Editor, so the save round trip has
  not yet been exercised against the real `JsonUtility`.
