# Progression, saves and the battle session

How a player's progress is stored, how beasts and the avatar level up, the region campaign they
play through, and the single entry point a scene calls to fight a battle from save data and pay it
out. This covers the runtime code under
`src/BeastCraft.Core/{Save,Session,Progression,Campaign,Economy,Idle}` and the tooling that
tests it. The battle rules themselves are in [battle-system.md](battle-system.md); the
economy (gold, the Trader, gear, consumables, cosmetics) is in [economy-and-shop.md](economy-and-shop.md);
pacing numbers come from [`docs/balance/campaign-pacing-report.md`](../balance/campaign-pacing-report.md)
(the region campaign) and [`docs/balance/pacing-report.md`](../balance/pacing-report.md) (skills and
materials).

Everything here is pure C# over two thin seams: an injected JSON engine and an injected storage
backend. The one file backend is `FileSaveStorage` (below); there is no UI and no Unity Gaming
Services integration yet.

---

## Save format

### `PlayerSave` (schema 5)

One versioned aggregate per player (`BeastCraft.Save.PlayerSave`):

| Field | Type | Contents |
| --- | --- | --- |
| `SchemaVersion` | `int` | The schema the data is in. `CurrentSchemaVersion` is **5**. 0 or missing is never valid. |
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
| `Idle` | `IdleState` | The idle (AFK) reward clock: `{LastClaimUtcTicks, LastClaimMonotonicMs, IdleSeed, ClaimIndex, ClockClamps}` — the last claim's wall clock (UTC ticks; 0 = not started) and monotonic clock (ms since boot; −1 = not available), the claims' seed and count, and how many claims had a tampered clock clamped (schema 5; see "Idle rewards"). |

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
| `ActiveRun` | `MapRun` | The expedition in progress (the stretch of the region map being explored): `{RegionId, Stage, Seed, Nodes, CurrentNodeId, Cleared, Attempts, NodeAttempts, NodeAttemptsNodeId}` (`NodeAttempts` counts the losses at location `NodeAttemptsNodeId`, -1 when none: the team suggestion's per-location loss count; added to schema 4 before it shipped, an older file reads -1). **`RegionId == ""` means none** — JsonUtility writes every class field, so the run is never null. |

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
In the game that is `JsonSaveSerializer` over `BeastCraft.FieldJson` (optional pretty-print).

*The serializer.* `FieldJson` is System.Text.Json restricted to the rules the save DTOs were written
for under Unity's `JsonUtility`: public instance fields only (no properties, no `readonly` or
`[NonSerialized]` fields), names exactly as declared and case-sensitive, enums as numbers, unknown
keys ignored, malformed input throws. It is the System.Text.Json implementation the save tests have
always run on outside Unity, ported verbatim (same options and field filter) when the runtime became
engine-neutral, so saves are written byte-for-byte as before. Golden fixtures pin that: schema 1-5
saves in `Tooling/EditModeTests/Goldens/Saves` must load, migrate and write back byte-identically.
(One known difference from real Unity `JsonUtility`, unchanged by the port: a null string or array
field is written as `null` rather than `""`/`[]`.)

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
| `SaveMigrations.AddIdle` (4 → 5) | A v4 save has no idle clock: read it into the current type (the clock not started, no seed, no claims), fill in anything missing, write it back. The first claim after the upgrade starts the clock and pays nothing (no offline time can be proven). Nothing else moves. |

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

`FileSaveStorage : ISaveStorage` is pure System.IO, rooted at a directory passed in. By default
`SaveLocations.Default()` builds one over the per-user local app-data folder
(`LocalApplicationData/BeastCraft/saves`); a host with its own storage root passes it to
`SaveLocations.DefaultDirectory(root)` or builds `FileSaveStorage` directly.

*WebGL.* On WebGL, `Application.persistentDataPath` is an in-memory file system (Emscripten's
IDBFS) that reaches the browser's IndexedDB only when JavaScript calls `FS.syncfs` after a write.
`FileSaveStorage` does not make that call, so on WebGL a save would be lost on reload. A platform
follow-up if WebGL is ever targeted (see Known gaps).

```csharp
SaveStore store = new SaveStore(SaveLocations.Default(), new SaveSerializer(new JsonSaveSerializer(), catalog));
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
header (no migration or validation) and any problem, for a load/continue menu. It never lists the
settings slot (below).

### Player settings (`PlayerSettings`, `PlayerSettingsStore`)

Preferences, not progress, so they live outside `PlayerSave`: its schema and migrations are
untouched, and one device's settings apply to every save slot.

- **`PlayerSettings`** — `[Serializable]`, public fields, JsonUtility-safe: `SchemaVersion`
  (`CurrentSchemaVersion` = 1) and `TeamSuggestionsEnabled` (default **true**: whether the game may
  suggest a counter team after repeated losses; see `docs/design/battle-system.md`, "Encounter
  preview", `TeamSuggestionPolicy`), and the battle effects settings: `EffectsIntensity` (`Full`
  (default), `Reduced` or `Minimal`, stored as its number), `ScreenShake` (default **true**) and
  `Flashes` (default **true**; accessibility: the hit flash and bright additive bursts), all
  presentation only (see `docs/design/presentation-and-vfx.md`, "Effects settings"). New settings
  are added as fields with defaults: a key an older file lacks keeps its default, so a purely
  additive setting needs no version bump (a file from before the effects settings loads as Full,
  shake on, flashes on).
- **`PlayerSettingsStore(storage, json)`** — saves and loads through the same `ISaveStorage` and
  `ISaveJsonSerializer` as the game saves (`FileSaveStorage` in the game, so the atomic write and the
  one-generation backup apply), in the reserved slot **`settings`** (`PlayerSettingsStore.SlotName`).
  `Save` stamps the current version; `Load` never fails: a missing slot, corrupt text (after the
  storage's backup fallback), JSON the serializer rejects or a version below 1 give
  `new PlayerSettings()` (`TryLoad` says whether the file was used). A newer build's file is read for
  the fields this build knows.
- **The reserved slot.** Settings and saves share one directory, so `settings` is reserved in any
  letter case (`PlayerSettingsStore.IsReservedSlot`; file names are case-insensitive on Windows and
  macOS): `SaveStore.Save` / `Load` refuse it and `Exists` reports false, and `SaveSlotIndex` skips
  it. Whatever names game-save slots (today only `main`) must not use it.

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
`InvalidValue`s, as are (schema 5) a negative idle `LastClaimUtcTicks`, `ClaimIndex` or `ClockClamps`,
a `LastClaimMonotonicMs` below −1, and a started idle clock without a seed.

`SaveContentCatalog` is an `ISaveContentCatalog` over plain id sets; `SaveContentCatalog.FromData(roster, library)`
builds it from the authored `beast-roster.json` and `skill-library.json`, and
`FromData(roster, library, regions)` adds `regions.json`'s region and seal ids
(`ISaveContentCatalog.IsKnownRegion` / `IsKnownSeal`, added with schema 3). A catalog built without
campaign data answers every non-empty region and seal id as known.

### Schema 6: the expedition's difficulty (`MapRun.Difficulty`)

Save schema **6** adds one field: `Campaign.ActiveRun.Difficulty`, a `RunDifficulty` (`Normal` = 0,
`Hard` = 1; written as its number, after `NodeAttemptsNodeId`), the difficulty the expedition in
progress was started on (`CampaignRules.StartRun(..., difficulty)`; see "Post-game region" below).
`CurrentSchemaVersion` is **6** (the schema-5 table above is otherwise unchanged).

- *Migration.* `SaveMigrations.AddRunDifficulty` (5 to 6) reads a v5 save, sets any expedition in
  progress to `Normal` (the only difficulty that existed) and stamps schema 6.
- *Validation.* `SaveValidator` reports `InvalidMapRun` for an undefined difficulty, for `Hard` in a
  region that is not post-game (`ISaveContentCatalog.IsPostGameRegion`, from `regions.json`
  `IsPostGame`; a catalog built without regions does not check it), and for a non-Normal difficulty
  with no expedition. `MapRun.Clear()` resets it to Normal.
- *Golden saves.* New `min-v6` and `rich-v6` fixtures (`rich-v6.input.json` generated by reflection,
  like `rich-v5`, with every field filled). No v1-v5 input changed; every v1-v5 expected output
  changed in exactly two places, both from writing schema 6: `"SchemaVersion":5` became `6`, and
  `,"Difficulty":0` now follows `"NodeAttemptsNodeId"` in the `ActiveRun` (15 bytes per file).

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
the way without idle rewards (the early rows of each stage sit below it), and about 22% with them
(idle XP keeps the team a fraction of a level ahead; see "Idle rewards").

**Bench share** (`BeastProgression.BenchXp` / `AwardBench`). A beast left on the bench earns
`min(100%, 10% + 9% × levels below the enemy)` of what a standing fielded beast earns, then the
falloff on its own level: 10% at or above the enemy's level, 55% five levels down, all of it from ten
levels down. A reserve therefore settles a few levels behind the team instead of falling ever
further back, and a new recruit catches up at the full rate. Campaign model (idle rewards included):
the bench is 5.0-5.7 levels behind the fielded team at every boss from region 3; a level-1 recruit
joining at region 5 is 6.3 behind by the end of region 6 (without idle: 5.0-6.0 and 7.7).

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
`BeastCraft.Campaign`); content in `content/data/Campaign/regions.json`; paced by
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
| Regions | `r01` Verdant Hollow (1-10), `r02` Emberreach (11-20), `r03` Tidefall, `r04` Stormcrag, `r05` Rustwood, `r06` Frostmere, `r07` Thunderspire, `r08` Deepwild, `r09` Cinder Throne, `r10` Worldcrown (91-100); 4 stages each; each requires the one before |
| Battle shapes | squad 45, horde 40, solo 15 (every region) |
| Seals | `seal_r01` … `seal_r10`, one per boss, caps 22, 32, 42, 52, 62, 72, 82, 92, 100, 100 (the next region's max + `LevelCapMargin` 2) |
| Starting cap | 12 (region 1's max + 2) |
| Map rules | 11 rows (row 0 to the top), 4 lanes, 4 paths, rest row 9, node weights Battle 70 / Elite 15 / Shop 8 / Rest 7, 1-3 elites from row 3, at most 1 shop, elites +1 level, gates +0, elite shape `elite` |

A region's own `MapRules` override the library's when its `Layers` is above 0. The validator requires
contiguous levels 1-100 from `r01` (`CampaignProgress.StartingRegionId`), each region requiring an
earlier one, existing shapes and gate / boss templates, and seal caps that never fall and always
cover the next region's max level.

### Location names (`location-names.json`)

`{SchemaVersion, Regions[] {RegionId, Wilds[], Den[], Camp[], TradingPost[], Pass[], Lair[]}}`: for
every region, exactly `NodeMapGenerator.LabelVariants` (8) display names per `LocationKind` — the place
names a map UI prints. `LocationNameTable.Resolve(labelKey)` (or `Resolve(node)`) parses
`"{regionId}/{kind}/{variant}"` and returns that region's `variant`-th name for the kind; a key the
table does not cover falls back to the kind's generic name (Wilds, Den, Camp, Trading Post, Pass,
Lair), a malformed key to "". `LocationNameTableValidator` requires every region exactly once, 8
non-empty, trimmed names of at most 24 characters per kind, unique within the region; the Region
importer imports the file with `regions.json` (all or nothing) into `RegionLibrarySO.LocationNames`
(`RegionLibrarySO.Names` is the built table). Presentation only: a save keeps the key, so renaming a
place renames it everywhere. Seals also carry a `Description` (flavour text). All region, seal,
location and boss text is DRAFT, written to [content-bible.md](content-bible.md).

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
(e.g. `r01/wilds/3`), resolved to a place name by `LocationNameTable` (below). Its draws come from their own stream
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
| `ProgressLevel(save, regions)` | How far the player has got: the level of the highest cleared map location (a beaten boss's region max, else the pass of the last cleared stage, and any battle location cleared in the expedition in progress); 0 before the first clear. The idle rewards are paid at it. |

### Bosses (DRAFT)

Ten authored templates in `encounter-library.json` (`boss_r01_hollow_warden` … `boss_r10_apex_pair`),
shape `elite` (its drop cells), fought at the region's max level, each flagged `"Draft": true` (not
player-facing; names and lore: [content-bible.md](content-bible.md)): r01 champion (Nature) + 2 brutes; r02 2 Fire champions + 2 archers; r03 Water giant +
2 casters (Water / Ice); r04 Air giant + 3 stalkers; r05 2 champions (Metal / Earth) + shaman + 2
brutes; r06 Ice giant + 10 swarmlings + 2 archers; r07 Lightning giant + Air champion + 2 casters;
r08 Nature giant + 12 stinglings + 2 shamans; r09 Fire giant + 2 champions (Earth / Metal); r10 two
giants (Fire / Water). Each `DifficultyOverride` (x0.86-x1.43) is calibrated with the simulator's
fixed-encounter set (`--encounter-set fixed --kit elemental --scouted bonds --calibrate-samples 64
--gear typical`) so the bond-aware scouted pick, wearing typical gear (the shipping difficulty's
assumption, as the shape table), clears about 50% at that level (the user's boss target), on the
merged combat rules (behaviour bonds, enemy statuses); see the tuning log, "Boss re-calibration".

### Pacing (`--mode campaign`)

`dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign [--runs n] [--seed(s)] [--out
path] [--self-check] [--regions path] [--battles-per-day n] [--idle-hours-per-day h]
[--idle-claims-per-day n]`: 1,000 Monte Carlo campaigns through the real save, maps and rules, idle
rewards included (25 battles and two 8-hour claims a day; see "Idle rewards"). The player fields 3 beasts (knocked out in 20% of battles each), benches 3, recruits a level-1
beast when region 5 starts, takes an Elite when the team's mean level is at least its level (else a
Battle, else Rest, Shop), camps the lowest bench beast, and retries every loss. Clear chance at equal
level: squad / horde 80%, elite and gates 60%, solo and bosses 50% (the user's tiers: a generated
node reads its shape's `TargetClear` from `encounter-library.json`, the boss templates the 50% their
`DifficultyOverride`s are calibrated to, `CampaignPacingSimulator.BossClear`), moved across a
level gap along the design's table in log-odds (+1 level: 60% / 36% / 27%). Result (all gates met):

| Gate | Target | Result |
| --- | --- | --- |
| Battles, whole campaign | 400-600 (p50) | 507 (p10-p90 489-527); 50-51 per region, ~13 of them lost retries (without idle: 541, 52-54, ~17) |
| Fielded level at every gate and boss | p50 within 3 | within 1 of the node level everywhere (up to 1 above: idle XP) |
| Avatar level at every gate and boss | p50 within 3 | within 1 of the node level |
| Bench | 5-8 behind, from region 3 | 5.0-5.7 |
| Recruit | within 8 by the end of region 6 | 6.3 |
| Level cap | never exceeded | 0 |
| Banked levels at a seal | p50 ≤ 3 | 0 (the cap never binds for this player) |
| Grind probe after r05's boss | < 0.05 levels (r01), < 0.5 (r05 stage 2) | 0.00, 0.00 |
| Focus skill (the economy design's gates) | L5 15-20, L10 72-88, L15 162-198, L20 270+ | 17, 77, 171, 297 |
| Idle shares of the campaign (p50) | gold ≤ 15%, materials ≤ 15%, beast XP ≤ 10% | 5.6%, 13.4%, 9.3% |

The clear-chance model is an assumption, not measured from fought battles; the tiered targets are
what the encounter table is calibrated to for a scouting player at equal level.

### Post-game region (r11, DRAFT)

`regions.json` ends with a post-game region, **r11 Duskmeridian** (`IsPostGame: true`), after the ten
mainline regions: unlocked by r10's boss, flat level 100 (`MinLevel = MaxLevel = 100`), four stages,
no seal (`BossRewardSealId: ""`: the cap is already 100), its own map rules (Battle 55 / Elite 30 /
Shop 5 / Rest 10, 2-6 Elites, no level offsets) and a `HardMode` (Hard shapes and boss). Notes:

- **Validation.** `RegionLibraryValidator` validates the ten mainline regions exactly as before (a
  golden test pins the error text) and post-game regions in a separate pass (see battle-system.md,
  "Post-game region").
- **Difficulty per run.** `CampaignRules.StartRun(save, regions, regionId, [stage,] seed, difficulty)`
  starts an expedition on `RunDifficulty.Normal` (the default; every existing overload) or `Hard`
  (post-game regions only; refused elsewhere), stored as `MapRun.Difficulty` (schema 6, above).
  `RegionLibrary.RegionFor` / `RulesFor(region, difficulty)` give what the difficulty fields.
- **Clearing r11's boss** grants no seal and unlocks nothing; the first clear grants the lair's gear and
  looks as every lair does, and **every Hard clear** also unlocks the region's Hard-only looks
  (`boss_hard`, `CosmeticRules.UnlockHardBossLooks`) not yet owned. Loot is Normal's on Hard.
- **Progression.** No new XP mechanic: beasts at level 100 discard overflow XP as before, and
  `ProgressLevel` (the idle rewards' level) is already 100 after r10's boss, so idle rates do not move.
- **Pacing.** `--mode campaign` never plays post-game regions in its mainline campaigns; its appended
  "Post-game" section plays r11 on Normal and Hard with its own seeds (66 and 90 battles p50).

---

## Idle rewards

Time away from the game pays gold, beast XP, skill materials and, very rarely, a look (AFK rewards).
Runtime code under `Runtime/Idle` (namespace `BeastCraft.Idle`); rates in
`content/data/Idle/idle-rewards.json`; paced by `--mode campaign`
([`docs/balance/campaign-pacing-report.md`](../balance/campaign-pacing-report.md), "Idle rewards").
**Every rate is a tunable starting value; no UI yet.**

The lead / user decisions this implements: an **8-hour accumulation cap**; idle is at most **~15% of
gold and materials** and **~10% of beast XP** over a campaign; idle looks come from the **same pool as
battle drops** (no idle-exclusive looks); idle XP goes to the **current party and the bench, through
the battles' catch-up rule**; clock tampering is **clamped silently to the real elapsed time** (no
message, no penalty); idle respects the **level cap and bank** and the **level-gap falloff**; it gives
**no first-clear or pity credit**; the **avatar earns idle XP at the party's rate** through its own
falloff (no cap).

> **Local play only — the fairness choices rely on it.** Beast Craft is slated for **local,
> single-player play** (not an MMO, no shared or competitive online play). The idle calculation's
> fairness choices are acceptable only because no player's idle income is ever set against another's:
> paying the whole period **at the rate of claim time**, a **larger idle share for lighter players**,
> and **client-clock clamping without a server**. **If online, MMO or competitive play is ever added,
> the idle calculation must be revisited** — server-authoritative time, rate-over-period accounting
> (each hour paid at the progress level it was spent at) and tighter shares — because as it stands it
> would come across as unfair.

**Approved behaviour and current defaults.** Approved (user decisions): paying the whole claim at the
progress level **at claim time** (clearing a higher location just before claiming pays the whole
period at the new rate, as AFK games commonly do); idle's share **rising for lighter players** (below).
Current defaults, tunable and open to review: idle gold held at ~6% by the Trader's affordability gate
(below); the offline clock limits (a clock set forward across a reboot, cross-device claims; "No
server clock"); the look chance scaled by `hours / CapHours`; material rolls from the `squad` cell;
the first claim of a new or migrated save only starting the clock.

### The claim (`IdleRewardCalculator.Claim(save, content, nowUtc, nowMonotonic, partyBeastIds)`)

`IdleContent` holds the rates (`IdleRewards`, built from `idle-rewards.json` by `IdleRewardsBuilder`),
the drop tables, the cosmetic library (null = no look roll) and the region library. The first claim of
a save (new or migrated) only starts the clock and pays nothing. Every later claim:

1. **Time.** Credits the real idle time since the last claim (`IdleRewardCalculator.Elapsed`, below),
   at most `CapHours` (8). Time beyond the cap is **not banked**. **Capped** means one thing for the
   claim and the preview: the idle time has reached the cap (at least `CapHours`), so waiting longer
   earns nothing (`IdleClaimResult.Capped`; the UI's "capped" is `IdleRewardCalculator.Preview(...).Capped`,
   which never changes the save).
2. **Rates.** Reads the band of the **progress level** — `CampaignRules.ProgressLevel(save, regions)`,
   the level of the highest cleared map location: a beaten boss's region max, else the pass of the last
   cleared stage, and any battle location cleared in the expedition in progress; 0 before the first
   clear, which pays nothing.
3. **Gold**: `floor(hours × GoldPerHour)` into the `Wallet` (no first-clear bonus, no modifiers).
4. **Materials**: `hours × MaterialRollsPerHour` rolls (stochastically rounded, so short claims lose
   nothing on average) of the drop tables' cell for (`Shape` = `squad`, progress level), every chance
   × the band's `MaterialChanceMultiplier` (`LootRoller.RollScaled`). **No pity, no first clear**: the
   pity counters and cleared cells are never read or written.
5. **Beast XP**: `floor(hours × XpPerHour)` to each beast of `partyBeastIds` (the current party; the
   save does not store one, so the game passes it), cut by the level-gap falloff on the beast's level
   against the progress level — idle cannot out-level the content or skip a seal. Every other beast
   (the bench) earns `BeastProgression.BenchSharePermille(progressLevel, its level)` of it (10% at or
   above, +9% per level below, all of it from ten below), then the falloff. All of it goes through
   `BeastProgression.AddXp(progress, xp, cap)` under `CampaignRules.BeastCap`: at the cap it banks, at
   most three levels' worth, and the rest is not granted (`IdleClaimResult.XpBanked` /
   `XpLostAtCap`; the UI shows "banked at cap"). No clear bonus (not a battle).
   **The avatar** (user decision) earns the party's rate, `floor(hours × XpPerHour)`, cut by the
   falloff on its own level against the progress level, with no cap (`AvatarProgression.AddXp`; the
   avatar has none): `IdleClaimResult.AvatarXpGained` / `AvatarFalloffPercent` / `AvatarLevelsGained`.
6. **A look**: one roll, chance `CosmeticChancePer10k / 10,000 × hours / CapHours` (so claiming often
   gains nothing), from the battle-drop pool (`CosmeticRules.PickDrop`, what `RollDrop` draws on a hit:
   the progress level's region's `drop` looks not yet owned). Milestone looks the XP reaches unlock too.
7. Re-anchors both clocks at the readings just used and counts the claim (`ClaimIndex`).

**Deterministic.** Claim `n`'s rolls come from `LootRoller.DeriveSeed(IdleSeed, n)`: materials on
stream 0, the look on stream 4 (the battle's cosmetic stream). `IdleSeed` is drawn from the first
claim's clock when unset. Same save, clock readings and party, same claim. Non-throwing; a claim with
no save or no rates is refused and changes nothing.

### No server clock (the offline rule)

This rule assumes **local-only play** (see the note above): with no shared or competitive play, a
client-clamped clock only ever affects the player's own game. Online, MMO or competitive play would
need server-authoritative time instead.

The game is offline-first and has no trusted time source yet (Cloud Code is a later backend), so the
claim trusts **the smaller of two clocks**: the wall clock (UTC, which the player can set) and a
**monotonic clock** — time since the device booted, including sleep (Android `elapsedRealtime`, iOS
continuous time), which the player cannot set but which resets on a reboot. The game passes both on
every claim (`nowMonotonic` negative = not available). `Elapsed`:

- **Same boot** (the monotonic reading has not gone back): the wall-clock time, never more than the
  monotonic time; a wall clock set back reads as the monotonic time. A wall clock set back, or ahead by
  more than 2 minutes (`ClockSlackMs`, room for NTP corrections), is a **clamp**.
- **Rebooted in between** (the monotonic reading went back): at least the time since boot really
  passed, so the wall-clock time, or the time since boot when that is more (a clamp).
- **No monotonic reading**: the wall-clock time, or 0 when it went back (a clamp).

A clamp is **silent** (lead / user decision): the player sees nothing, loses nothing that provably
passed and gains nothing that did not; `IdleState.ClockClamps` counts them for diagnostics. The
residual hole — a clock set forward across a reboot cannot be told apart from a real absence offline —
is bounded by the 8-hour cap. A save moved to another device (Cloud Save) carries the old device's
monotonic reading; the first claim there reads as a reboot. A server clock would close both.

### `idle-rewards.json`

`{SchemaVersion, CapHours, Shape, MaterialRollsPerHour, Bands[] {MinProgressLevel, MaxProgressLevel,
XpPerHour, GoldPerHour, MaterialChanceMultiplier, CosmeticChancePer10k}}` — the usual Data / validator
/ builder / SO pattern (`IdleRewardsData`, `IdleRewardsValidator`, `IdleRewardsBuilder`,
`IdleRewardsSO`) and an Editor importer (Beast Craft/Data/Import Idle Rewards, validated against
`drop-tables.json`, all or nothing). The validator requires schema 1, a cap of 1-24 hours, a drop-table
shape, bands ascending and contiguous over 1-100, no negative rate, XP and gold per hour that never
fall as the level rises, multipliers in 0-1 and a look chance of at most 1% per claim.

| Progress levels | Gold / hour | XP / hour | Material chance | Look / full claim |
| --- | ---: | ---: | ---: | ---: |
| 1-10 | 2 | 10 | x0.4 | 0.08% |
| 11-20 | 4 | 15 | x0.4 | 0.08% |
| 21-30 | 6 | 21 | x0.4 | 0.08% |
| 31-40 | 8 | 27 | x0.4 | 0.08% |
| 41-50 | 10 | 32 | x0.4 | 0.08% |
| 51-60 | 12 | 38 | x0.4 | 0.08% |
| 61-70 | 14 | 43 | x0.4 | 0.08% |
| 71-80 | 16 | 49 | x0.4 | 0.08% |
| 81-90 | 18 | 55 | x0.4 | 0.08% |
| 91-100 | 20 | 60 | x0.4 | 0.08% |

Gold is 0.1 × the squad gold curve `G(L) = 10 + 2L` and XP 0.2 × a standing beast's clear XP net of
losses (`34 + 2.8L`), each at the band's middle level; one material roll an hour. An 8-hour claim at
progress level 50 pays 80 gold (about 0.7 of a squad clear's), 256 XP to each party beast (about 1.5
average battles' worth, losses and knockouts included) and 8 rolls of the squad cell at 40% of its chances.

### Pacing (`--mode campaign`)

The campaign model claims with the game's calculator on its own save: 25 battles a day, away 16 hours a
day, two claims a day (a claim every 12.5 battles, 8 hours each; `--battles-per-day`,
`--idle-hours-per-day`, `--idle-claims-per-day`). About 40 claims (320 idle hours) per campaign.

| Over the campaign (p50) | Ceiling | Result |
| --- | --- | --- |
| Idle gold / all gold (clears, gear sales, idle) | ≤ 15% | 5.6% (about 3,500 gold) |
| Idle materials / all materials (by XP value) | ≤ 15% | 13.4% (p90 16.8%) |
| Idle beast XP / all beast XP (battles, camps, idle) | ≤ 10% | 9.3% (p90 9.8%) |
| Every earlier campaign gate | met | met (507 battles p50; want-list affordability 74%) |

**Gold is held well under its ceiling by the Trader.** More idle gold pushes the economy's want-list
affordability gate (p50 55-80%) to 100%: at 0.11 × G(L) (6.4% of all gold) it is 75%, at 0.12 × (6.9%)
most visits are fully affordable. Reaching ~15% idle gold needs higher Trader prices or a gold sink
first (producer item). The shares depend on the player's cadence: at 15 battles a day idle is 8.8% of
gold, 20% of materials and 14% of XP (over the ceilings); at 40 a day 3.7%, 8.9% and 6.2%; with one
claim a day (a 16-hour absence capped at 8) 3.0%, 7.4% and 5.0%. **A lighter player's larger idle
share is accepted** (user decision; acceptable for local-only play). The avatar's idle XP is 8.7% of
its XP (p50, not gated; it stays within 1 level of every gate and boss). With `--idle-hours-per-day 0` the
report is the pre-idle one plus an "off" line.

Reproduce: `dotnet run --project Tooling/BalanceSim -c Release -- --mode campaign --self-check --out
docs/balance/campaign-pacing-report.md`.

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
6. **Consumables** (when chosen): spent from the save's pack (`ConsumableInventory.TryRemove`,
   one each) and used on stream 2 (`ConsumableLoadout.Apply`) — the only write `Run` makes to the
   save, and only once every check above has passed.
7. **Battle**: `new TurnManager(beasts + avatar)` and the bond-aware
   `BattleTurnExecutor.RunBattle(turnManager, units, grid, new Random(Seed), avatar, passives, bonds, MaxTime)`.

The result carries `Success`, `Errors` / `Error`, the `Battle` (`BattleResult`) and `Outcome`, the
`Grid`, `Units` (enemies then the team, end-of-battle state; not the avatar), `Avatar`,
`ActiveBonds`, `StartingStats` (every unit's assembled stats before the battle began, by unit id,
avatar included), `TeamUnitIds` / `UnitIdFor(beastId)`, and the usage counts rewards are paid from:
`SkillUsesByBeastId` (by `BeastId`), `AvatarActiveUses` and `PassiveTriggers`; and `ConsumablesUsed`
with `ConsumablesDeducted` (true once `Run` has spent them).

**Determinism.** The only randomness is the one `Random(Seed)`: same setup, same seed, same battle
(a test compares full turn-by-turn traces across runs).

**Error handling.** `Run` never throws on bad input. A bad setup returns `Success = false` with
every problem in `Errors`, no battle fought and the save untouched (no consumable spent):

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
  at most one, used as it began on stream 2) were already spent by `Run` as the battle began,
  whatever the outcome (so they are spent even if rewards are never applied); `ApplyRewards` only
  reports them in `ConsumablesSpent` and never spends them again. A result not marked
  `ConsumablesDeducted` (none from the current `Run`) has them spent here instead, once.

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

## Running the tests

```sh
dotnet test Tooling/EditModeTests
dotnet format Tooling/EditModeTests --verify-no-changes
```

`Tooling/EditModeTests/EditModeTests.csproj` (net10.0, C# 9, NUnit 3) references
`src/BeastCraft.Core` and compiles the suite in `Tooling/EditModeTests/Tests`
plus its own golden tests (`Tooling/EditModeTests/Goldens`). CI runs it too (format check, then
`dotnet test --configuration Release`), as a final gate after a green local run. The authored JSON
data is not copied; the tests find it by walking up from the output directory.

---

## Known gaps / follow-ups

- **The team suggestion has no UI.** The rule is wired (`CampaignRules.SuggestionFor`: the map
  run's per-location loss count, `TeamSuggestionPolicy`, `TeamSuggester`, the player's settings) and
  tested; the pre-fight screen that shows it is not built.
- **How a map node picks an encounter is not decided.** Encounters are game content
  (`EncounterPlan`), but which shape and level a node offers, and the campaign's difficulty target
  (the shipped table is calibrated for a scouting player at the shapes' tiered targets, `squad` and `horde` 80%, `elite` 60%, `solo` 50%; `DifficultyScale` 1.0), are pending
  producer review; see `docs/design/battle-system.md`, "Encounters as game content".
- **Economy content is DRAFT** (gear, consumable and look names and numbers, shop prices); no UI
  and no art (looks carry placeholder art keys). See [economy-and-shop.md](economy-and-shop.md),
  "Open items".
- **Region content is DRAFT.** Region and seal names, level bands, map rules and the ten boss
  templates (names, elements, escorts) are placeholders for producer review; the boss
  `DifficultyOverride`s are calibrated (typical gear, 50% scouted) and must be re-run after any
  combat, roster or enemy change (see "Bosses (DRAFT)").
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
- **Idle rewards assume local-only play.** If online, MMO or competitive play is ever added, revisit
  the idle calculation (server-authoritative time, rate-over-period accounting, tighter shares); see
  "Idle rewards".
- **Idle rewards have no UI and no server clock.** The claim, preview, "capped" and "banked at cap"
  data are built and tested; the idle screen is not. The offline clock rule leaves a clock set forward
  across a reboot (bounded by the 8-hour cap) and cross-device claims (Cloud Save) to a future server
  time source. Idle gold sits at ~6% of all gold, under its 15% ceiling, because more breaks the
  Trader's affordability gate; the idle shares also depend on the modelled 25 battles a day (see
  "Idle rewards"). Pending producer review.
- **Never run in Unity.** The project has not been opened in an Editor, so the save round trip has
  not yet been exercised against the real `JsonUtility`.
