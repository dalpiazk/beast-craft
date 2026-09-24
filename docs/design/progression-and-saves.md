# Progression, saves and the battle session

How a player's progress is stored, how beasts and the avatar level up, and the single entry point
a scene calls to fight a battle from save data and pay it out. This covers the runtime code under
`BeastCraft/Assets/_Project/Scripts/Runtime/{Save,Session,Progression}` and the tooling that tests
it outside Unity. The battle rules themselves are in [battle-system.md](battle-system.md); pacing
numbers come from [`docs/balance/pacing-report.md`](../balance/pacing-report.md).

Everything here is pure C# over two thin seams: an injected JSON engine and an injected storage
backend. The one file backend is `FileSaveStorage` (below); there is no UI and no Unity Gaming
Services integration yet.

---

## Save format

### `PlayerSave` (schema 2)

One versioned aggregate per player (`BeastCraft.Save.PlayerSave`):

| Field | Type | Contents |
| --- | --- | --- |
| `SchemaVersion` | `int` | The schema the data is in. `CurrentSchemaVersion` is **2**. 0 or missing is never valid. |
| `Beasts` | `List<OwnedBeast>` | The beast collection, in the order obtained. |
| `Avatar` | `AvatarProgress` | The avatar's own level and XP. |
| `AvatarSkills` | `AvatarSkillBook` | The avatar's active and passive skill books. |
| `Materials` | `MaterialInventory` | Held skill materials, cleared (shape, band) cells and pity counters. |
| `Gear` | `GearInventory` | Every owned piece of beast and avatar gear, worn or not (schema 2). |
| `AvatarEquippedGear` | `string[3]` | The avatar's worn gear by slot, indexed by `(int)AvatarGearSlot` (schema 2). |

`OwnedBeast` is one beast in the collection:

| Field | Type | Contents |
| --- | --- | --- |
| `BeastId` | `string` | Unique within the save; how teams and other save data refer to the beast. Two beasts of one species are two beasts. |
| `Progress` | `BeastProgress` | `{SpeciesId, Level, Xp}`: which species it is, its level (1-based) and XP banked toward the next. |
| `Skills` | `BeastSkillBook` | Learned skills with their `SkillProgress` (level, XP, tier) and the 3 equipped slots (slot order is fire priority). |
| `EquippedGear` | `string[3]` | Worn beast gear by slot, indexed by `(int)GearSlot` (schema 2). |

**JsonUtility-compatible by construction.** Every type reachable from `PlayerSave` is
`[Serializable]` with public fields and a parameterless constructor, and every map is a list —
`JsonUtility` drops dictionaries and properties (a test walks the type tree and enforces this).
Everything is named by stable string ids (species, skill, passive, material and gear ids, all under
the never-rename rule), never by asset reference. Empty slots are `null` or `""`: `JsonUtility`
writes null strings back as empty ones, and every reader treats both as empty.

`PlayerSave.EnsureInitialized()` replaces any null collection or sub-object with its empty default
and drops null list entries, so code reading a loaded save never meets a null (`JsonUtility` never
produces them; other serializers and hand-edited files can). It never touches ids or numbers.

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

`SaveValidator.Validate(save, ISaveContentCatalog, ISaveGearCatalog)` reports problems as
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
| `GearLevelTooLow` | a beast wears gear above its level (the gear has no effect) | gear |

`SaveContentCatalog` is an `ISaveContentCatalog` over plain id sets; `SaveContentCatalog.FromData(roster, library)`
builds it from the authored `beast-roster.json` and `skill-library.json`.

---

## Beast and avatar level

Both are parallel, deliberately uncoupled rules: `AvatarProgression` (avatar level, scales its
stats through `AvatarStatsSO.GetStatsAtLevel`) and `BeastProgression` (a beast's `BeastProgress`,
which `BattleUnitFactory` builds the beast at). Both cap at level 100, bank XP toward the next level,
level up as far as the bank allows, hold the bank at 0 at the cap, and normalize level and XP on
every write. A null progress is a no-op.

| | Avatar (`AvatarProgression`) | Beast (`BeastProgression`) |
| --- | --- | --- |
| XP to next level | `200 + 16 × level` (216 at L1, 1,784 at L99) | `160 + 13 × level` (173 at L1, 1,447 at L99) |
| Every finished battle | 8 XP (won, lost or stalemate) | 6 XP to every **fielded** beast — also when knocked out |
| Clear bonus (player victory) | `40 + 4 × enemy level` | `40 + 4 × enemy level`, only to fielded beasts **still standing** at the end |
| Benched | — | nothing |

Skill practice XP is separate (`SkillProgression`: 10 XP per time a skill fired, at most 20 uses per
award) and is paid by the same reward step.

> **Beast XP numbers are autonomous defaults pending user review.** They were chosen without a
> design brief, to one target — a beast fielded every battle levels alongside the encounters, as the
> avatar does — and are all tunable. The avatar numbers predate this document.

**Pacing.** `dotnet run --project Tooling/BalanceSim -c Release -- --mode pacing` plays 1,000
Monte Carlo campaigns of 500 battles (encounter level `1 + battle / 5`, 80% of battles cleared) and
checks, at every 50-battle checkpoint, that the median level is within 3 of the encounter level.
The beast in that model is fielded in every battle and knocked out in 20% of them (a modelling
assumption, not measured from the PvE simulation). Current result
([pacing-report.md](../balance/pacing-report.md)):

| Battle | 50 | 100 | 150 | 200 | 250 | 300 | 350 | 400 | 450 | 500 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Encounter level | 10 | 20 | 30 | 40 | 50 | 60 | 70 | 80 | 90 | 100 |
| Avatar p50 | 11 | 21 | 31 | 40 | 50 | 60 | 70 | 81 | 91 | 100 |
| Beast p50 | 10 | 20 | 30 | 40 | 50 | 60 | 70 | 80 | 90 | 100 |
| Beast p10 – p90 | 9–11 | 19–22 | 29–32 | 39–42 | 48–52 | 58–62 | 68–72 | 78–82 | 87–92 | 97–100 |

Every checkpoint is inside the ±3 target for both. Why it works for the beast: at encounter level
`L` a battle pays on average `6 + 0.8 × 0.8 × (40 + 4L) ≈ 31.6 + 2.56L`; five battles per encounter
level pay `≈ 158 + 12.8L` against a level cost of `160 + 13L`. Fighting below one's level pays less,
so grinding easy content is slow. `--self-check` runs the model twice and fails on any difference
or missed target.

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
  `IsDefeated` at the end. Benched beasts earn nothing.
- **Avatar XP** — `AvatarProgression.AwardBattle` when the avatar took part.

`rng` drives the drop rolls; null seeds one with `LootRoller.DeriveSeed(result.Seed, 0)`, so rewards
are deterministic either way. The summary reports `Applied`, `Error`, `Outcome`,
`SkillLevelsGained`, `BeastXpGained` (by beast id), `BeastLevelsGained`, `AvatarXpGained`,
`AvatarLevelsGained` and the `Loot`. It refuses — changing nothing — a null save or result, a failed
battle, or a result already paid out (`BattleSessionResult.RewardsApplied`). A team beast no longer in
the save is skipped.

`BattleSession.ApplyRewards(save, result, content, dropTable, rng = null)` does the same for the
`ShapeId` and `EncounterLevel` the battle's `EncounterSetup` named (`EncounterPlan.ToSetup` sets
both); it refuses, changing nothing, a result whose setup named no shape or no level.

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
suite. CI runs it too (format check, then `dotnet test --configuration Release`), as a final gate
after a green local run, and it is no substitute for
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

- **No gear content.** There are no gear data files or importer; `BattleContent` gets gear only
  from its caller, and the gear tests use in-memory `GearSO` / `AvatarGearSO` instances.
- **How a map node picks an encounter is not decided.** Encounters are game content
  (`EncounterPlan`), but which shape and level a node offers, and the campaign's difficulty target
  (the shipped table is calibrated for a scouting player at 50%, `DifficultyScale` 1.0), are pending
  producer review; see `docs/design/battle-system.md`, "Encounters as game content".
- **Beast XP defaults need review** (see above), as does the pacing model's 20% knockout assumption.
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
