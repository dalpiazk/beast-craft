# Tooling/BalanceSim

A headless balance simulator for Beast Craft. It runs the **real** battle code (the engine-neutral
game runtime, `src/BeastCraft.Core`, by project reference) and prints a
Markdown report of how much each beast in the starter roster helps a team.

- **Primary mode, PvE (team vs encounter).** The game is expected to be PvE (see "Encounter
  direction" in `docs/design/battle-system.md`), so every team of beasts fights synthetic enemy
  encounters: by default randomly generated compositions of mixed enemy types (one giant, a boss
  with an escort, a mixed squad, a horde of up to two dozen) with elements that vary between and
  within enemy teams. Each beast is scored by how much it moves its team's clear rate.
- **Secondary mode, PvP (1v1 round-robin).** The original beast-vs-beast round-robin, kept as a
  reference section.

**Local-only.** This is not a CI job and must not be added to `.github/workflows` (Actions minutes
are a scarce budget; see the repo's CI-budget rules). It is never shipped and game code never
references it.

**Its output is an input to design decisions, not a decision.** Nothing the simulator measures is
applied to `beast-roster.json`, the damage formula or the element chart automatically; a human reads
the report and changes the data by hand.

## Running it

From the repo root:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- [options]
```

| Option | Default | Meaning |
| --- | --- | --- |
| `--mode <m>` | `both` | `pve`, `pvp` or `both`. |
| `--kit <k>` | `both` | The element axis: `elemental`, `neutral` or `both` (see below). |
| `--skill-kit <k>` | `library` | The skill axis: `library` (each beast's authored `DefaultLoadout` from `skill-library.json`, the real game setup and the committed report's setting; see "Library kits") or `standard` (the same standard kit for every beast, so the stat lines are what is measured; see "The standard kit"). Before the authored-kits retune this was `--kit standard|library`; `--kit` now takes only the element axis. |
| `--skill-level <n>` | `1` | Skill level (1-20) for library skills and library avatar skills; the tier is the gates below that level (16+ = all three passed). |
| `--skill-library <path>` | found by walking up | Path to `skill-library.json` (read only with `--skill-kit library` or `--avatar library`, i.e. by default). |
| `--bonds <on\|off>` | `on` | Team bonds from the library's `TeamBonds`, for every player team that meets a bond's condition (never enemies): battle-start effects, and the behaviour bonds' reactions on every turn. Library kit only (ignored with `--skill-kit standard`). Adds the "PvE team bonds" section. See "Library kits". |
| `--scouted <list>` | `all` | Scouted picking, the "PvE scouted picking" section: comma-separated `random`, `heuristic`, `bonds` (the bond-aware heuristic; bonds on only), `oracle` (also adds the held-out best team), or `all` / `none`. Post-processing of the battles already run: no extra battles, sub-second. `none` removes the section and its header line; the rest of the report is unchanged (the default calibration still uses the bond-aware picker, see `--calibrate-on`). See "Scouted picking". |
| `--scouted-detail <d>` | `full` | What the heuristic pickers see of each composition (`ScoutingDetail`): `full`, `elements-only` or `dominant-element`. |
| `--scouted-vanguard-min <n>` | `1` | Fewest Vanguards a heuristic pick fields, 0 to `--team-size`. |
| `--levels <list>` | `1,50,100` | Comma-separated levels; beasts and enemies fight at the same level (see `--level-gap` for fights across a level gap). |
| `--encounter-set <s>` | `generated` | `generated`: random compositions per shape of the game's encounter content (see "Generated encounters"). `fixed`: the three legacy hand-authored simulator fixtures (`boss`, `swarm`, `pack`) in `encounters.json`. |
| `--compositions <n>` | `8` | Generated compositions per shape. |
| `--encounters <list>` | all | Comma-separated shape ids (`solo`, `elite`, `squad`, `horde`) or, with `--encounter-set fixed`, encounter ids (`boss`, `swarm`, `pack`). |
| `--team-size <n>` | `4` | Beasts per PvE team, 1-6. Every combination of the roster is fielded (C(10,4) = 210). |
| `--target-clear <t>` | library | Clear rate the difficulty calibration aims for (the scouted pick's by default; see `--calibrate-on`). By default each shape's own `TargetClear` in `encounter-library.json` (the game's tiered targets: `squad` and `horde` 80, `elite` 60, `solo` 50; 50 for a fixed-set encounter). A single percentage (e.g. `50`) is the legacy uniform target for every shape (the balance guard is judged at `--target-clear 50`); `shape=pct` pairs (e.g. `squad=70,solo=45`) override single shapes. |
| `--calibrate-on <t>` | `bonds` | Whose clear rate the PvE difficulty is calibrated to `--target-clear`. `bonds`: the team the bond-aware scouted picker (heuristic + bonds) fields against each composition, i.e. the player scouts and counter-picks; falls back to `heuristic` when bonds are not active (`--bonds off`, `--skill-kit standard`). `heuristic`: the plain element counter-pick. `mean`: the mean of every team (the unscouted player), the calibration before scouting; it reproduces the pre-scouting report byte for byte. See "Difficulty calibration". |
| `--calibrate-samples <n>` | `16` | Scouted-pick calibration only: battles per composition the picked team fights at each search step (8 compositions x 16 = 128 battles per step, a binomial SE of about 4.4 points at 50%). Raise it if a cell's search is non-monotone. |
| `--level-gap <list>` | off | PvE only. Also replay every cell with the enemies `g` levels above the team (negative = below) at the cell's calibrated multiplier, and add the "PvE level gap" section (and "PvE level gap over seeds" with `--seeds`). Comma-separated gaps and inclusive ranges, e.g. `-5..10` or `0,2,3,5`. The team and the avatar (unless `--avatar-level`) stay at the row's level; the enemies' stats follow their curve to their level, and the damage formula's level-difference term applies. Each battle's seed ignores the gap, so gap 0 is the calibration itself (no extra battles). A gap that puts the enemies outside 1-100 is not run (`—`). Suggested with `--levels 10,30,50,70,90`. See "Level gap". |
| `--gap-mix <spec>` | `-3:5,-2:10,-1:15,0:40,1:15,2:10,3:5` | PvE. The **level-gap mix** the balance sections are judged over: comma-separated `gap:weight` pairs (enemy level minus team level; weights normalized to their sum; a bare gap weighs 1). Every cell's every-team battles are replayed with each battle dealt one gap in those proportions; the per-beast marginals, niches, flags, element matchups, team composition and bond sections read those battles, while the calibration, the difficulty table, scouting and the plumbing checks stay at gap 0. `0` (or `off`) is gap 0 alone and reproduces the report before the mix byte for byte. See "Level-gap mix". |
| `--level-gap-teams <n>` | `42` | `--level-gap` only: how many teams (a seeded subset of the 210) the **no-scouting** rate at each nonzero gap is measured over, against every composition (42 x 8 = 336 battles). The **scouted** rate always uses the picked team, `--calibrate-samples` battles per composition. |
| `--avatar-value` | off | PvE only (needs an avatar and a scouted-pick calibration). Also replay every cell's picked-team battles (`--calibrate-samples` per composition, the same seeds) **without the avatar** at the calibrated multiplier, and add the "PvE avatar value" section ("over seeds" with `--seeds`): per cell the scouted rate with and without the avatar, the difference (the avatar's **value** in points of clear rate), the avatar's turns, and its **direct share** of the team's output: the avatar's damage (its own turns and its passives' hits) + healing (team HP restored on its turns) + shield soak (damage a shield absorbed, credited to the shield's caster) as a percent of the team's total, with the part its passives produced. Read-only accounting; the rest of the report is unchanged. See "Avatar value". |
| `--turn-detail` | off | PvE only. Add the "PvE beast turns" section: per kit mode, shape and beast (every team's battles at the calibrated multiplier, levels pooled) its turns per battle, the share of them on which no skill fired, split into held by its stance and out of reach, stunned turns, and the share of its damage that came off large enemies (bosses: giant, champion). A diagnostic; never changes a battle. |
| `--marginal-threshold <x>` | `5` | Flag a beast whose overall marginal clear rate is outside +/-x points. |
| `--enemy-element <e>` | `authored` | `authored` (as generated, or as authored in the fixed set), `None` or an element name: override every enemy's element. |
| `--max-time <n>` | `2000` | Battle-time cap (normalized, see below); a battle that reaches it is a stalemate. |
| `--seed <n>` | `12345` | Base seed; each battle derives its own seed from it. |
| `--samples <n>` | PvE 1 (generated) or 5 (fixed); PvP 5 | Battles per PvE team and composition (at the calibrated multiplier, and at every calibration step with `--calibrate-on mean`) and per PvP game, each with its own seed. Damage variance and crits make battles random; see "Sampling" below. |
| `--matrix-level <n>` | `50` | Level of the PvP win matrix and the stat table (falls back to the highest simulated level). |
| `--roster <path>` | found by walking up | Path to `beast-roster.json`. |
| `--enemy-library <path>` | found by walking up | Path to the game's `enemy-library.json` (`content/data/Encounters/`). |
| `--encounter-library <path>` | found by walking up | Path to the game's `encounter-library.json` (same folder). |
| `--drop-tables <path>` | found by walking up | Path to `drop-tables.json`: rolled by `--mode pacing`; in PvE the encounter library's shape ids are checked against it. |
| `--encounters-file <path>` | found by walking up | Path to the legacy fixed set's `encounters.json` (beside this file). |
| `--write-difficulty <path>` | off | PvE, generated set, one seed: also write the calibrated multipliers as the game's `encounter-difficulty.json` (see "Difficulty table for the game"). Never changes the report. |
| `--avatar <preset>` | `library` | PvE only. `library` (the committed report's setting) fields the skill library's default avatar: its first three actives and `AvatarDefaultPassives`, at `--skill-level`. `support` fields a fixture avatar with three passive skills beside every player team (`AvatarPresets.cs`; not authored content). `none` fields no avatar. `library` and `support` add an "Avatar passives" section with firings per battle and the avatar's turns and active casts per battle. See `docs/design/battle-system.md`, "Avatar passives" and "Beast skill kits". |
| `--avatar-level <n>` | encounter level | PvE only. The fielded avatar's level, 1-100: its fixture stats on the medium curve (Speed included) and its damage-formula level. By default each battle's encounter level (the avatar levels alongside the encounters; see `--mode pacing`, "Avatar level"). |
| `--out <path>` | none | Also write the report to this file (it always goes to stdout). |
| `--self-check` | off | Run everything twice and fail unless both reports are identical; also replay sample PvE battles through `BattleTurnExecutor.RunBattle` and fail if the simulator's loop disagrees. |
| `--seeds <list>` | none | Comma-separated base seeds, run one after another in one process (cannot be combined with `--seed`). Each seed's run is exactly the `--seed <n>` run; stdout (and `--out`) get the multi-seed aggregate, and with `--out` each seed's full report is also written beside it as `<name>.seed<n>.md`. See "Multi-seed runs". |
| `--panel <KxS>` | off | PvE, generated set. Also fight the **composition panel**: K compositions per shape drawn from a constant seed (`SimOptions.PanelSeed`, never `--seed`), every team S times each (S >= 2), at the `--panel-level` cell's calibrated multiplier, and add the "PvE composition panel" section (and "PvE composition panel over seeds" with `--seeds`): the team main-effect SD, the team x composition interaction SD (the value of counter-picking), clear rate by stance mix, and per bond its excess over the additive prediction and reactions per battle. The committed tuned report uses `16x4` (53,760 battles per kit mode, about 14 s). See "Composition panel". |
| `--panel-level <n>` | `50` | `--panel` only: the level the panel is fought at; one of `--levels`. |
| `--calibrate-sample <n>` | off | `--calibrate-on mean` only (an error otherwise). **Opt-in, changes results.** The difficulty search evaluates a seeded subset of `n` teams; the chosen multiplier is then run once with every team, and every number in the report comes from that full run. See "Performance". |
| `--timings` | off | Print a wall-clock breakdown to stderr: per PvE cell, every calibration step (multiplier, clear rate, seconds), PvP, the report and GC counts. Never changes the report. |

Exit codes: `0` success, `1` bad arguments, `2` missing or invalid roster, skill library or encounters
(the roster is checked with `BeastRosterValidator` and the library with `SkillLibraryValidator`
first, exactly as the Editor importers do), `3` a self-check failed. The run time goes to stderr, never into the report. The default run (PvE and PvP, both element modes,
three levels, four shapes x 8 compositions, 1 sample per team and composition) takes about
16 s on an 8-core machine (13 s with `--gap-mix 0`; about 32 s with `--self-check`, which runs
everything twice and replays two teams per composition through `RunBattle`); it took 50 s before
the scouted-pick calibration and 210 s before the performance pass (see "Performance"). PvE battles run in
parallel, and the output is identical whatever the thread count.

Two reports are committed, both the default arguments:

- `docs/balance/baseline-report.md` — the "before" picture, on the roster's first-draft stats. It is
  kept as a record and is **not** regenerated (a fresh run now reads the tuned roster). It predates
  the ATB turn order, so its battle lengths are in rounds.
- `docs/balance/tuned-report.md` — the current roster and skill library after the third tuning pass
  and its element chart v2 follow-up (see `docs/balance/tuning-log.md`, "Retune with authored kits,
  avatar passives, sqrt speed and mitigation", "Element chart v2", "Thunderbird range vs move" and "Niche pass: Thunderbird opener, Phoenix/Frost Wyrm lifts, remaining negatives", then "Team bonds"; "Scouting and counter-picking" added the scouted-picking section, no balance change; "Avatar gauge" moved the avatar onto its own ATB gauge, no tuning; "Large enemies (footprints)" made the giant, colossus and champion multi-hex, no tuning beyond the bosses' range parity; "Scouting-based calibration" calibrates the difficulty on the bond-aware scouted pick instead of the average team, no balance change; "Scaling bonds" added three per-count stance bonds; milestone 2's final retune: "Avatar retune", "Thunderbird in `elite`", "Beast retune under the scouted calibration" and "Level-gap re-check"), under the real game setup (every beast's authored default loadout, the library
  avatar with its passives, the library's team bonds, skill level 1), the current Runtime (the square-root ATB turn order, the
  mitigation damage formula, `SpecialAttack`-scaled heals, combat stances, variance and crits) and
  the generated encounters. Regenerate it, and the game's difficulty table with it, whenever the
  roster, the skill library, the encounter content, the simulator or the Runtime change. Since
  "Behaviour bonds and tiered difficulty" it runs the behaviour bonds, the composition panel
  (`--panel 16x4`), the avatar-value replay (`--avatar-value`, for the no-avatar column of
  "Difficulty by shape") and the tiered targets. Since "Level-gap mix" (see below) its balance
  sections are judged over the default gap mix. Since the campaign merge the report and the game's
  difficulty table come from two commands: the report stays **gearless** (the per-beast balance
  guard's setting), and the shipping table is calibrated with **typical gear** (`--gear typical`, a
  user decision; "Difficulty table for the game"):

```sh
# the committed report (gearless)
dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --out docs/balance/tuned-report.md
# the shipping difficulty table (the same run plus --gear typical; its report is not committed)
dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --gear typical --write-difficulty content/data/Encounters/encounter-difficulty.json
```

## Library kits

The default, `--skill-kit library`, fields each beast's authored `DefaultLoadout` from
`content/data/Skills/skill-library.json` (`SkillLibraryLoader.cs`), built through
`SkillLibraryBuilder` — the same DTO-to-`SkillSO` mapping as the Editor importer — and fielded as
`SkillInstance`s at `--skill-level` and the tier that level implies. In `neutral` mode every library
skill's element is forced to `None`. The report shows a "Library beast kits" table in place of the
standard kit and has no kit parity table (it measures the standard kit's Strike / Shot / Blast
balance; library kits differ by design). PvP uses the library kits too. The default `--avatar
library` fields the library's default avatar (first three actives, `AvatarDefaultPassives`) on the
same skill level. The avatar's stats (either preset) are a fixture block read the way the game reads
one, `AvatarStatsSO.GetStatsAtLevel(level)`: 100 in every combat stat and Speed 100 at max level,
scaled by the roster's growth curve like a beast's (15 at level 1, 57 at level 50), so its shields
(a percent of its Defense) and heals (a percent of its SpecialAttack) are the same share of a
beast's HP at every level. Its level is `--avatar-level`, by default the encounter level.

**The avatar's gauge.** The avatar joins the `TurnManager` beside the beasts (never the targeting
roster) and fills its own ATB gauge from its Speed; when it comes up the loop runs
`BattleTurnExecutor.ExecuteAvatarTurn` (its passives' internal cooldowns, then its actives),
exactly as `RunBattle` does, which `--self-check` verifies. Its turns count in the battle's action
total like any unit's, and the "Avatar passives" section reports its turns and active casts per
battle.

**Team bonds** (`--bonds on|off`, default on) come from the same file's `TeamBonds` array, built
through `SkillLibraryBuilder.ApplyTeamBond` like the importer's. Each team's active bonds are resolved
once per run (`TeamBondResolver`, from its species' stances and elements); a `TeamBondLoadout`
applies their battle-start effects through `BattleTurnExecutor.BeginBattle` / `RunBattle`, before the
avatar's passives, and the simulator's loop passes it into every `ExecuteTurn` / `ExecuteAvatarTurn`
so the behaviour bonds react all battle (each reaction is counted per bond on `PveBattle.BondReactions`;
in the `neutral` kit mode a reaction strikes without an element, `TeamBondLoadout.ReactionElementOverride`);
enemies never get bonds. Bonds are part of the library setup, so `--skill-kit standard`
ignores them. The report's header says whether they were on, and "PvE team bonds" (see "PvE: team vs
encounter") shows what they did.

This is the real game setup and the committed report's. `--skill-level 10` is the sanity run the
tuning log reports beside it. For a quick check:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --self-check --mode pve --levels 50 --compositions 2
```

## The standard kit

With `--skill-kit standard` (add `--avatar none` for the pre-retune setting), every beast fights
with the same kit, and its stat line is what gets measured. This was the default until the
authored-kits retune; the kit parity table and the power derivation below apply only to it. Fire priority is Blast, the physical single-target skill, then Burst. Every beast skill
aims at the nearest enemy. The physical skill depends on the beast's combat stance (see "Runtime
rules" below): Vanguard and Skirmisher beasts carry Strike (range 1), which walks them into melee;
Ranged beasts, which never walk into melee, carry Shot (range 3) instead.

| Skill | Category | Shape | Range | Power | Cooldown |
| --- | --- | --- | ---: | ---: | ---: |
| Blast | Special | SingleTarget | 3 | 68 | 1 |
| Strike (Vanguard, Skirmisher) | Physical | SingleTarget | 1 | 93 | 1 |
| Shot (Ranged) | Physical | SingleTarget | 3 | 70 | 1 |
| Burst, physical half | Physical | AreaBurst, radius 2 around the caster | 2 | 37 | 2 |
| Burst, special half | Special | AreaBurst, radius 2 around the caster | 2 | 37 | 2 |

**Power is a percent of the attacking stat** (`DamageFormula`: `Power / 100 x A x A / (A + D)`, then
element, crit and variance; adopted from Sword x Staff, see
`docs/balance/research-sword-x-staff.md`). The powers were rescaled from the old level-term formula
(Blast 40, Strike 57, Shot 41, Burst 20) so a neutral hit between two average level-50 roster beasts
takes the same share of HP as before; enemy powers (then in `encounters.json`, now the game's
`enemy-library.json`) were rescaled the same way (`P' = round(1.52 P + 7)`).

The goal is that `Attack` and `SpecialAttack` (and `Defense` / `SpecialDefense`) carry equal weight
for every stance. The first baseline did not manage that: Strike at cooldown 1 against Blast at
cooldown 2 weighted `Attack` about twice as heavily.

- **Same cooldown.** The physical skill and Blast both fire every turn once in range.
- **Power offsets how often each fires.** Strike needs a free tile next to its target, so it fires
  less often than Blast: while the beast is still closing, and when the target is crowded. Shot has
  Blast's range but fires after it, so it occasionally finds that Blast has just felled the only
  enemy in reach. Measured over the default PvE run after the square-root speed / mitigation formula
  change, Strike fires 0.71-0.72x as often as Blast for Vanguard beasts and 0.81-0.82x for
  Skirmishers (0.733x pooled, weighted by fires) and Shot about 0.965x for Ranged beasts, so
  Strike's power is 68 / 0.733 = 93 and Shot's 68 / 0.965 = 70. One Strike serves two stances, so
  Vanguards land a little under 50% and Skirmishers a little over. The report's **kit parity** table
  checks the result per stance and per shape, and a stance outside 50 +/- 5 is flagged.
  **Why Shot:** before it, a Ranged beast only fired Strike at an enemy that was already adjacent,
  and the physical share fell to 37-47% (the tuning log's "After combat stances"), so `Attack` was
  under-weighted and the three Ranged beasts under-measured. Re-derive `StrikePower` / `ShotPower`
  if the kit, the enemies or the movement rules change.
- **Burst is split.** It is two skills, a physical and a special half, with the same power, radius
  and cooldown, fired together. A single-category area skill would reintroduce the bias. The
  physical half fires first, but that does not bias outcomes: a target dies iff the two halves
  together deal its HP, whichever one lands the blow. Burst has lower power and a longer cooldown
  than the single-target pair, so it only pays off when several enemies are close (swarms, packs).
  Cooldown 2 brings it up on the beast's second turn; at cooldown 3 the swarm was mostly dead before
  it fired (measured under the old round-based turn order).
  `AreaBurst` is a disc around the caster's own tile and never moves the caster, so Burst sits last
  in the fire order and goes off from the tile Strike just walked the beast to. A Ranged beast
  keeps its distance, so its Burst rarely catches anyone; the kit does not compensate for that.
- **Kit modes.** `elemental` gives every kit skill the beast's first element; `neutral` makes them
  `Element.None`, which isolates the stat lines from the element chart.

## PvE: team vs encounter

- **Teams.** Every combination of `--team-size` distinct beasts (210 teams of 4, format
  `SmallGroup`; each beast is in 84). No gear; the `--avatar` preset (by default the library avatar)
  fights beside every team.
- **Encounters.** Two sets:
  - **Generated (default): game content.** The enemy library
    (`content/data/Encounters/enemy-library.json`) and the encounter shapes
    (`encounter-library.json` beside it), read through the game's own `EnemyLibraryValidator` and
    `EncounterLibraryValidator` (against the roster and `drop-tables.json`) and fielded through the
    game's `EnemyCatalog`, so the simulator fights exactly the enemies the game fields. The
    compositions come from the game's `EncounterGenerator` (see "Generated encounters" below).
    Until the "Encounters as game content" change these were simulator fixtures in `encounters.json`;
    the move changed no number (the regenerated reports differ only in their provenance lines).
  - **Fixed (`--encounter-set fixed`): simulator fixtures, not game content.** The three
    hand-authored encounters used before the generator (`boss`: one Colossus; `swarm`: 12 biters +
    12 stingers on a Large arena; `pack`: 3 direwolves + 3 wisps), still in `encounters.json`
    beside this file (schema 3: each group is an enemy in the enemy-library shape plus `Count` and
    `Elements`, cycled over its units). No game code reads them. The wisps and stingers aim at the
    beast with the least current HP (`CurrentHp`).

  Either way each enemy becomes an in-memory `CreatureSpeciesSO` (one per enemy and element,
  `EnemyCatalog`) on the roster's `medium` growth curve, so it is built by the same
  `BattleUnitFactory.CreateBeast` and scales with level the same way as a beast; its kit is
  `SkillLibraryBuilder.ApplySkill` over the authored skills, every skill in the unit's element
  (`Element.None` in `neutral` mode).

  Enemy skills use the skill library's `SkillData` shape. Each enemy has a `Stance` (a
  `CombatStance` name; missing = `Vanguard`), and each skill an optional `TargetingCriterion`
  (`Distance`, the default, `Stat`, `CurrentHp` or `HpFraction`; `Random` is refused so battles
  never draw targets from the rng), `TargetingOrder` (default `Lowest`) and `TargetingStat`
  (default `HP`, read only by `Stat`). `Stat` + `HP` compares the stat block, i.e. **maximum** HP;
  `CurrentHp` compares the HP a beast has left, so `CurrentHp` + `Lowest` is "pick off the weakest"
  (`SkillTargetResolver`). A skill's power is its `Damage` effect's `Magnitude`; a skill `Element`
  is refused (every skill takes the unit's element). The validators reject a Ranged enemy without an
  approaching skill (`SingleTarget` or `Line`) of range 2 or more (a Ranged unit never walks into
  melee), an enemy without an approaching skill (the only shapes that walk), move 0, an enemy id
  that is a roster species id, a shape id missing from the drop tables, and a composition that
  cannot fit its deployment zone.

  Each enemy also has an optional `Footprint` (a `UnitFootprint` name; missing = `Single`, one
  tile): the giant and the colossus are `Hex7` (seven tiles, the boss size) and the champion
  `Triangle` (three tiles, the mini-boss size); see "Unit footprints" in
  `docs/design/battle-system.md`. Every range to or from a large enemy is measured between nearest
  tiles, an area hits it once, and a large caster's area grows from all its tiles, so the `Hex7`
  bosses author their ranges one lower than their one-tile values were (gaze 2, quake and roar 1: a
  radius-1 burst from a `Hex7` is the radius-2 disc around its centre) to keep their reach; the
  champion's shockwave stays at 2. The validators pack every lineup a shape can draw (every count
  and type mix, in the generator's front-to-back order, `EncounterFit.ComparePlacement`: greedy
  packing is order-sensitive, so a large Ranged enemy behind a Vanguard screen can fail where
  largest-first fits), every template and every fixed encounter into the enemy zone the way a battle
  does (`EncounterFit`), and refuse what does not fit: a `Hex7`
  enemy never fits a `Small` arena (a two-row zone). The generator also refuses a draw that would not
  fit (a safety net; no valid file produces one).

  The advanced effect fields are optional, and every one is inert when missing. See "Status effects
  and advanced skill effects" in `docs/design/battle-system.md`.
  - Per enemy: `StatusResist` (0–100, `BattleUnit.StatusResist`).
  - Per skill: `InitialCooldown` (−1 means the ordinary cooldown) and `MaxUsesPerBattle`.
  - Per skill: `Effects`, the skill library's `EffectData` list (the damage effect with its
    `HitCount` and `ExecuteBonusPercent`, then any further effects: `EffectType`, `Status`,
    `AffectedStat`, `Magnitude`, `DurationTurns`, `Chance`, `MaxStacks`, `IsPercent`), checked by
    the skill library's own effect rules. The enum-valued fields take the runtime enum names.

  The bosses (giant, champion and the fixed colossus) carry `StatusResist` 50. No fixture skill uses
  the other fields yet, so the reports are unchanged.
- **Placement.** Each side takes the front-most tiles of its own deployment zone (front row first,
  then outward from the centre line). Enemies are placed in lineup order by `DeploymentPacker`: each
  takes the front-most anchor where its whole footprint fits the zone on free tiles, so one-tile
  enemies take exactly the front-most tiles and a `Hex7` boss on a Medium board sits centred on the
  middle row of the enemy zone, its escort filling the tiles around it (the layout is worked out once
  per composition). The team is committed
  through `PlacementValidator.TryPlaceAll`. Which member gets which slot is a fixed seeded shuffle
  per team, because the slot fixes the unit id, and ids break initiative ties and equal-distance target
  ties within the team. Pinning slots to roster order would always expose the first species.
  (Initiative ties *between* the sides are split separately; see below.) Enemies are placed front to
  back in composition order: Vanguard types first, then Skirmishers, then Ranged. Placing every enemy
  is checked on every battle, and the loader rejects a shape whose largest draw does not fit its zone.
- **Difficulty calibration.** One multiplier per (kit mode, shape, level), shared by all the shape's
  compositions (per fixed encounter with `--encounter-set fixed`), scales every enemy's
  HP, Atk, Def, SpA and SpD. Speed and Move stay unscaled: Speed is how many turns a unit gets, so
  scaling it would change the enemies' action economy, not just their toughness. **The target** is
  the shape's `TargetClear` in `encounter-library.json` (tiered: `squad` and `horde` 80%, `elite` 60%,
  `solo` 50%; 50% for a fixed-set encounter) unless `--target-clear` sets one for every shape (the
  guard's `--target-clear 50`) or per shape. The multiplier starts at 1 and doubles or halves until the target clear rate is
  bracketed (between 1/64 and 64), then bisects 8 times. The evaluated multiplier closest to the
  target wins (first evaluated on a tie), and its battles are the ones reported (they are kept, not
  re-run). The process is deterministic because each clear rate is. A step whose multiplier scales
  every enemy of the shape to exactly the stats an earlier step did (late bisection steps at level
  1, where stats are small) plays identical battles, so it reuses them instead of re-running.
  **What is aimed at the target (`--calibrate-on`).** The game expects the player to scout
  (`EncounterPreview`) and counter-pick, so by default (`bonds`) the calibrated clear rate is that
  of the team the bond-aware scouted picker fields against each composition (see "Scouted
  picking"; the picks depend on the preview alone, so they are worked out once per shape and are
  the same in both kit modes and every level). Each search step runs only those picked teams,
  `--calibrate-samples` (16) times per composition: 8 x 16 = 128 battles per step, a binomial SE of
  about 4.4 points. The picked battles are the ones the every-team run would play for that team
  (same seed; sample 0 is exactly its every-team battle). At the chosen multiplier every team then
  fights every composition once (210 x 8 = 1680 battles), and every metric, section and the
  **no-scouting** rate (the mean over every team: the player who brings any team without looking)
  come from that run. "Calibrated difficulty" shows both rates and their gap per cell and per
  shape. `heuristic` aims the plain element counter-pick instead; `mean` aims the mean of every
  team (every step runs all 1680 battles), the calibration before scouting, and reproduces that
  report byte for byte. The report lists each composition's own clear rate (over every team) at
  the shape's multiplier, and the range per cell.
  Where a step in the clear-rate curve cannot be split (for example level 1, where enemy stats
  round to 1-2), the closest rate is reported; a miss beyond 10 points is flagged. The picked teams
  are few and react to one stat rounding alike, so steps are more common on the scouted pick (in
  the default run: `neutral` `solo` L1, 35.9%); `--self-check` fails a miss unless the search saw the
  rate jump across the target within 1% of multiplier (a genuine step).
- **Metrics**, all at the calibrated multiplier:
  - **Marginal clear rate**, the primary number: the clear rate of teams containing the beast minus
    teams without it, in points.
  - **Normalized marginal** (scouted-pick calibration only): the marginal x 0.25 / (p (1 - p)) per
    cell, p = the cell's no-scouting clear rate, then averaged like the raw one. A marginal scales
    with the binomial variance p (1 - p), largest at 50%; calibrated on the scouted pick, the
    average team clears well under 50% in the boss shapes (10-22% in `elemental` `solo` and
    `elite`), which shrinks every raw marginal there. The normalized figure is what the cell would
    show at 50%, so it stays comparable with the pre-scouting marginals and between shapes. It is
    the "Overall normalized" column, what the flags read, and what the multi-seed balance guard
    reads (every beast's 3-seed mean within +/-4 `elemental`, +/-7 `neutral`,
    `SimOptions.GuardElemental` / `GuardNeutral`). It amplifies noise by the same factor (about 2x
    at 13%).
  - **Damage share / taken share**: the beast's share of its team's damage dealt and taken (HP
    actually removed, so overkill is not counted).
  - **Survival** (standing at the end), **time to clear** (normalized time of the clears it was
    in) and **turns / time** (its turns per unit of time over its battles: sqrt(Speed / 100) while
    it stands). The stat table also lists each beast's **turn rate** at the matrix level.
  - Per shape, per level, and overall (every shape and level weighted equally), plus a per-shape
    ranking that shows niches, and the crit table.
  - **Element matchups** (`elemental` mode, every shape and level pooled): each beast's marginal over
    the compositions whose dominant element (the element carrying at least 50% of the composition's
    threat) its kit element hits super-effectively, neutrally or resisted, and over the compositions
    with no dominant element. A regrouping of battles already run, so it costs nothing; the buckets
    are small (a few compositions each), so read it as direction, not measurement.
- **Team composition** (`TeamReport.cs`; "PvE team composition: does the lineup matter?"). The
  marginals judge beasts one at a time; this judges whole teams, from the same battles:
  - **Does composition matter?** One line per kit mode and shape (and overall): best-to-worst and
    p10–p90 team spread, and the teams' SD against the SD damage rolls alone would give.
  - **Team clear-rate spread**: min, p10, median, p90, max and SD of the 210 teams' clear rates per
    kit mode, shape and level, levels pooled, and overall; **noise SD** = root mean binomial
    variance, p(1 - p) / (N - 1) per cell (an upper bound: a team's chance differs between
    compositions), and **beyond noise** = sqrt(SD² - noise SD²), a lower bound on the lineup's own
    spread. A single level is only 8 battles per team by default, so read the pooled rows. Plus a
    10-point histogram of the teams.
  - **Best and worst lineups** (`elemental`, the primary mode): the top and bottom 5 teams per shape
    and overall.
  - **Pair synergy** (`elemental`): for each of the 45 pairs, the clear rate of the 28 teams holding
    both against the no-interaction prediction from the two marginals, baseline + w x (mA + mB),
    and the difference with its binomial SE; top and bottom 5 per shape and overall. **w is not 1**:
    with every k-of-n team fielded, a marginal is n / (n - 1) times the beast's additive effect and
    a pair's teams carry (n - k) / (n - 2) of the pair's effects, so w = (n - 1)(n - k) / (n (n - 2))
    = 0.675 for 4 of 10; baseline + mA + mB would show every pair of strong beasts as anti-synergy.
    With 45 pairs a few |synergy / SE| near 2.5 are expected from noise; one seed cannot separate
    them, `--seeds` can (see "Multi-seed runs").
- **Team bonds** (`BondReport.cs`; "PvE team bonds", only with bonds on): every bond with its
  condition, scope, tier effects and reaction and how many of the 210 teams have it (per tier), which
  bonds each beast belongs to, and how many bonds the teams activate; per kit mode and shape, each
  behaviour bond's **reactions per battle** of an active team. Then per kit mode a **bond marginal**
  table: per shape and overall, **Δ** = clear rate of the teams with the bond active minus the teams
  without, and **excess** = the active teams' rate over the additive prediction from their members'
  marginals (baseline + (n - 1) / n x the members' centred marginals, the pair synergy model per
  team). Δ mixes the bond with its members' own strength (only teams holding them can have it); the
  excess is the part the lineup earns, since the members' marginals already carry the bond's
  average. A **scaling** (`PerCount`) bond's frequency is per stack count (x1, x2, …), and per kit
  mode a "Scaling bonds by stacks" table gives, by condition count, the stacks applied (0 when none
  applies: below `MinCount`, or an `Others` bond on a team made only of its members), the teams,
  their clear rate and excess, and the bond's **per-stack slope** (least-squares points of clear rate
  per applied stack over every team). And the primary mode's clear rate by number of active bonds. The `--seeds` aggregate
  repeats the bond marginal and the scaling tables averaged over seeds ("PvE team bonds over
  seeds"; the slope with its SD over seeds). For the whole effect
  of bonds, compare a `--bonds off` run: its "PvE team composition over seeds" spread and pair
  synergy tables are the bond-free baseline.
- **Scouted picking** (`ScoutingReport.cs`; "PvE scouted picking", on by default, off with
  `--scouted none`): what seeing the encounter and counter-picking a team for it is worth, per
  shape and kit mode, with each strategy's pick rates. See "Scouted picking" below.
- **Flags.** Overall marginal (normalized, with a scouted-pick calibration) outside +/-5 points; **no niche** (bottom 3 in every shape);
  **no weakness** (top 3 in every shape); a stance whose kit parity is outside 50 +/- 5%;
  stalemates; calibration misses (on the calibrated rate: the scouted pick's by default).
- **Battle loop.** The PvE loop reproduces `BattleTurnExecutor.RunBattle` statement for statement,
  with an HP snapshot around each `ExecuteTurn` so damage can be attributed to the acting unit.
  `--self-check` replays sample battles (sample 0, same seed) through the real `RunBattle` and
  requires identical outcomes, battle time, turn counts, HP and positions — damage rolls included.
  The loop adds no rules of its own.

### Generated encounters

The user's direction is PvE with enemy sides from one giant to about two dozen small enemies, mixed
enemy types, and elements that vary across battles and sometimes within one enemy team. The default
encounter set simulates that.

All of this is game content (`content/data/Encounters/`), and the draw is the
game's own `EncounterGenerator` (`src/BeastCraft.Core/Encounters`): the simulator makes one generator per run
from `--seed` and draws `--compositions` lineups per shape through it (`GeneratedEncounters.cs`), so
the compositions it calibrates are exactly what the game can field. The file readmes list every
field.

**Enemy library** (`enemy-library.json`, `Enemies`; max-level base stats, speeds in a narrow 95-105
band). Powers are on the current percent-of-stat scale (the file is the source of truth; the
`_readme` there lists every field):

| Type | Role | Threat | Stance | Kit (power, targeting) |
| --- | --- | ---: | --- | --- |
| Giant (7 tiles) | boss, HP 1600 | 12 | Vanguard | crush (Physical, r1, 113) and gaze (Special, r2, 113), nearest; quake + roar (Physical + Special AreaBurst, r1 from its footprint = r2 from its centre, 60, cd 3) |
| Champion (3 tiles) | mini-boss, HP 800 | 6 | Vanguard | cleave (Physical, r1, 90, nearest); hex (Special, r2, 90, cd 2, lowest current HP); shockwave (Special AreaBurst, r2 from its footprint, 52, cd 3) |
| Brute | melee tank, HP 190 | 2.75 | Vanguard | smash (Physical, r1, 83, nearest) |
| Stalker | fast melee hunter, HP 125 (Speed 105, Move 4, crit 10) | 2 | Skirmisher | shadow claw (Special, r1, 90, lowest current HP) |
| Archer | ranged physical, HP 90 | 2 | Ranged | arrow (Physical, r3, 71, nearest) |
| Caster | ranged special, HP 85 | 2 | Ranged | bolt (Special, r3, 71, lowest current HP) |
| Shaman | area special, HP 130 | 2 | Vanguard | staff (Physical, r1, 60, nearest); storm (Special AreaBurst, r2, 49, cd 2) |
| Swarmling | swarm, HP 30 | 0.45 | Vanguard | bite (Physical, r1, 52, nearest) |
| Stingling | swarm, HP 30 | 0.45 | Vanguard | sting (Special, r1, 52, nearest) |

The shaman is a Vanguard: its storm is a disc around itself, so a Ranged shaman that keeps its
distance would rarely catch anyone. The swarm is two single-skill types rather than one type with a
physical and a special skill because, under the old level-term formula, every hit dealt at least its
+2 offset: two hits per swarm unit doubled the floor damage, and at level 1 the horde could not be
calibrated below 35% clear even at the minimum multiplier. (The current formula has no offset, only
the 1-damage floor.)

**Shapes** (`encounter-library.json`, `Shapes`; `ThreatMin`-`ThreatMax`):

| Shape | Arena | Recipe | Threat budget |
| --- | --- | --- | --- |
| `solo` | Medium | one giant | 12 |
| `elite` | Medium | a giant + 2 escorts (weight 2), or 2 champions + 1-2 escorts (weight 1); escorts from brute / stalker / archer / caster / shaman; at least 2 types | 14-17 |
| `squad` | Medium | 4-6 from brute / stalker / archer / caster / shaman, at least 3 types | 10-12 |
| `horde` | Large | 14-20 swarmlings / stinglings + 2-4 archers / casters, at least 3 types (16-24 enemies) | 12.5-14 |

**Generator rules** (the runtime `EncounterGenerator`, seeded from `--seed` alone):

- For each shape in file order, `--compositions` times: pick a variant by weight, draw each slot's
  count uniformly in [Min, Max] and each unit's type uniformly from the slot's types, and keep the
  draw only if its summed threat is inside the budget and it has at least `MinDistinctTypes` types
  (up to 2000 redraws; the first 200 also reject a repeat of an earlier composition of the shape).
  Every shape is generated whatever `--encounters` selects, so a shape's compositions do not depend
  on the filter.
- Each composition then draws an element scheme by the library's `SchemeWeights`: **one element**
  for the whole team (30%), **one per type** (30%), **one per unit** (fully mixed, 25%) or **none**
  (15%). `--enemy-element` overrides the elements after the draw, so it never changes what is
  drawn. Elements are dealt from a
  shuffled deck of the ten elements, reshuffled when empty and shared by the whole generation, so
  every element is dealt before any is dealt twice; the default run deals far more than ten, so all
  ten always appear. The report lists every composition (types, counts, elements, scheme, dominant
  element, threat and clear rate per kit mode) and the element distribution.
- **Threat** is a hand-set weight per type, fitted to per-composition clear rates (a least-squares
  fit of logit clear rate on type counts, `neutral` mode, level 50, 24 compositions per shape) and
  rounded: brute 2.75 vs 2 for the other standard types, a swarm unit about a fifth of an archer or
  caster, two champions about a giant. The stalker (buffed to HP 125 and power 55, 90 on the
  current scale) and champion (HP 800) were raised until they pulled their weight. The budget only
  keeps a shape's compositions comparably hard; the calibration still sets one multiplier per shape. Compositions still differ:
  at the defaults a single composition's clear rate at its shape's multiplier ranges over roughly
  15% to 80%, most within 30-70% (the report's "Composition clear range"). That spread is intended variety, not a
  calibration miss; every battle still counts toward the marginal.

### Runtime rules that shape the PvE numbers

The simulator emulates nothing: every movement rule is the Runtime's own, inside
`BattleTurnExecutor.ExecuteTurn`. Two of them matter a great deal at encounter scale, and both used
to be simulator-side workarounds before they became Runtime rules:

- **Defeated units leave the grid** the moment they fall, so a swarm's front rank no longer walls
  off the rest with its own dead. (Before this rule, 206 of 210 `neutral` swarm battles at level 50
  stalemated, and no difficulty multiplier could fix it; the simulator used to lift the dead itself,
  with a `--keep-defeated` flag to show the old behaviour. Both are gone.)
- **Partial approach.** A unit that cannot reach range of its target this turn still walks its
  remaining move along the cheapest route toward it and holds the skill (no fire, cooldown kept).
  Without it, sides whose move plus range fell short of the gap between the deployment zones (8 hexes
  on a Large board) waited forever, and the fixture enemies had to be given board-spanning movement
  (boss 7, direwolves 9, wisps 7, swarm 13). They now move like beasts (roster band 2-5): 3 for the
  giant, champion, brute, archer, caster and shaman, 4 for the stalker and the swarm. The validator
  only requires move >= 1 and at least one approaching skill (`SingleTarget` or `Line`, the only
  shapes that walk; of range 2 or more for a Ranged enemy).
- **Combat stances** (`CombatStance`, per species in `beast-roster.json` and per enemy in
  `enemy-library.json`). A Vanguard moves exactly as above, except that among equally short approaches
  it stops nearest its closest Ranged or Skirmisher ally (screening it). A Ranged unit never walks
  into melee (a range-1 slot fires only at an adjacent enemy, otherwise it holds without moving).
  Ranged and Skirmisher units prefer stop tiles with fewer adjacent enemies and spend whatever
  movement is left after their skills backing away from the nearest enemy, but never beyond their
  longest single-target range. Roster stances: Ranged = Phoenix, Kirin, Basilisk; Skirmisher =
  Thunderbird, Griffin; the rest Vanguard.

The default run has no stalemates, PvE or PvP.

- **Turn order is the Runtime's ATB gauge.** `TurnManager` fills every unit's gauge by
  `round(100 x sqrt(Speed))` per tick and hands a turn to whoever reaches 100000, overflow carried,
  so turns grow with the square root of Speed (four times the Speed is twice the turns).
  There are no rounds: battle length is **normalized time**, 1.0 = one turn of a Speed-100 unit.
  Speed scales with level, so the same fight takes longer at level 1 than at level 100; compare
  times within a level. The cap (`--max-time`, default 2000, `BattleTurnExecutor.DefaultMaxTime`)
  is a hang guard, not a game rule.
- **Initiative ties between the sides are split evenly.** `TurnManager` breaks a tie between equally
  full, equally fast gauges on the ordinal unit id (units of equal Speed fill in lockstep, so that is
  every turn they share), and with raw ids (`e01`... against `p1`...) every cross-side tie went to the
  enemies. Each battle now prefixes one side's ids with `a` and the other's with `b`; in every
  (kit mode, composition, level) a seeded shuffle of the team indices picks exactly half the teams
  to win ties (`PveSimulator.PlayersWinTies`). The prefix is side-wide, so the order within a side,
  and every targeting tie (targeting only ever compares units of one side), is unchanged.

### Difficulty table for the game

`--write-difficulty <path>` writes the run's calibrated multipliers, one per (kit mode, shape,
level), as the game's `encounter-difficulty.json` (`DifficultyWriter.cs`; round-trip numbers, the
same bytes for the same run; schema 2: `Targets` and a `TargetClear` per cell). The committed file
is the documented table command's (`--panel 16x4 --avatar-value --gear typical --write-difficulty
...`, above; the panel and avatar-value replay do not touch the calibration: `--mode pve --gear
typical --write-difficulty` writes the same bytes): seed 12345, 8 compositions, levels 1 / 50 / 100,
the player team in **typical gear** (the shipping assumption, a user decision; its `_readme` names
the gear), calibrated on the bond-aware scouted pick at each shape's **tiered target**: `squad` and `horde` 80%, `elite` 60%,
`solo` 50%, the shapes' `TargetClear`, a user decision). The game reads its `elemental` cells through
`EncounterDifficultyTable` (linear between calibrated levels, clamped outside them) and multiplies by
`encounter-library.json`'s `DifficultyScale` (1.0, a global producer factor). The table is
calibrated for a player who scouts and counter-picks: an unscouted team clears far less (the
report's "Difficulty by shape": about 7-10% of `solo` and `elite`, 50-60% of `squad` and `horde`).
A table written at another target than the library's still loads, but the importer warns
(`EncounterDifficultyTable.Warnings`). The balance guard is judged at `--target-clear 50`, never on
the shipping table, and the committed report is gearless. Only single-seed generated PvE runs can write it (`--seeds` and
`--encounter-set fixed` are refused).

## Level gap

`--level-gap` answers "what does being under-levelled cost, and over-levelling buy?". Every (kit mode, shape, level) cell is
first calibrated as usual, at equal levels; then, at the cell's multiplier, it is replayed with the
enemies `g` levels above the team (`PveSimulator.RunLevelGaps`, `RunBattle(mode, teamLevel,
enemyLevel, ...)`): the picked team per composition `--calibrate-samples` times (the **scouted**
rate, the one the targets read) and a seeded subset of `--level-gap-teams` teams once per
composition (the **no-scouting** rate). The seed and the initiative tie split are the team level's,
so the gaps replay the same damage-roll streams (common random numbers) and gap 0 is read from the
calibration's own battles. Two things change with the gap: the enemies' stats (their growth curve at
their own level) and the damage formula's level-difference multiplier on every hit, both ways
(`DamageFormula.GetLevelMultiplier`; see `docs/design/battle-system.md`, "Damage formula").

The section's table has one row per shape and level plus an **All shapes** mean per level, one
column per gap, cells `scouted (no-scouting)`. Bands for the scouted rate are relative to the cell's
calibration target T, its shape's `TargetClear` (`SimOptions.LevelGap*`, `LevelGapReport.Band`):
gap 0 T +/- 5; +2 and +3 (a couple of levels under) 0.4 T to 0.7 T; +5 and beyond under 0.2 T; -2 and
-3 (a couple of levels over) at least T + 0.4 (100 - T); -5 and beyond at least T + 0.8 (100 - T), so
over-levelling must make every fight easier. At T = 50 that is 45-55, 20-35, under 10, at least 70 and at
least 90; at T = 80, 75-85, 32-56, under 16, at least 88 and at least 96. **All shapes** rows are held to
the bands of the mean target. `!` marks a miss, and a line per mode counts the targets met. With
`--self-check`, the loop-parity replay also runs each level at its widest in-range gap.

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --levels 10,30,50,70,90 --level-gap -5,-3,-2,0,2,3,5 --out docs/balance/level-gap-report.md
```

Cost: each nonzero gap adds about 128 + 336 battles per cell (a sixth of a calibration); the
command above takes about a minute. The committed `docs/balance/level-gap-report.md` is that
command's output; the default report has no level-gap section and is unchanged by the option.

## Level-gap mix

The player does not always fight at their own level: a map node can sit a few levels above the team
(under-levelled) or below it (over-levelled). Judging beasts only on equal-level fights would tune
them for a case that is under half of play, so by default (`--gap-mix`, a user decision on its
shape: "battles 1-3 levels above and below") the balance sections are judged over a **mix of level
gaps**, while the difficulty table keeps its meaning (calibrated at gap 0).

| Gap (enemy level - team level) | -3 | -2 | -1 | 0 | +1 | +2 | +3 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Share | 5% | 10% | 15% | 40% | 15% | 10% | 5% |

Symmetric (under- and over-levelled equally likely), peaked at the equal-level fight and tapering
with distance: a starting point, not measured play data (`SimOptions.DefaultGapMix`).

- **How.** After a cell is calibrated (at gap 0, as always) its every-team run is replayed at the
  calibrated multiplier with each (composition, team, sample) battle **dealt** one gap
  (`PveSimulator.RunGapMix`): per composition a seeded shuffle of the team slots takes the gaps in
  exact proportion (slot p of N gets the gap whose cumulative weight covers (p + 0.5) / N, the table
  walked from alternate ends on alternate compositions so the rounding evens out). A gap-0 battle is
  the calibration's own battle; any other is `RunBattle(teamLevel, enemyLevel)` as in `--level-gap`
  (the same seed, so common random numbers). Where a gap would put the enemies outside 1-100 (L1 with
  enemies below, L100 with enemies above) the enemies stay at the bound and the team moves (L1, gap
  -3: team L4 against enemies L1), so the gap is always what it says.
- **What reads the mix.** Per-beast marginals (raw and normalized, per shape and level, overall), the
  niche rankings, the flags on them, the element matchups, the role metrics, team composition (spread,
  best / worst lineups, pair synergy), the bond marginals, scaling and reactions, and the `--seeds`
  aggregate of all of them (so the balance guard is judged on the mix). The normalization uses the
  cell's clear rate over the mix.
- **What stays at gap 0.** The calibration and the difficulty table (`--write-difficulty` writes the
  same file with or without the mix), "Calibrated difficulty", scouted picking, the composition
  panel, the avatar value, the kit parity and crit tables, stalemate flags, `--level-gap`, and the
  self-check invariants.
- **Side by side.** "Level-gap mix" gives, per kit mode and shape, the no-scouting clear rate at gap 0
  and over the mix, and per gap over the battles the mix dealt it; each "Marginal clear rate by shape"
  table adds **Gap 0 overall** and **Gap 0 normalized** columns; the `--seeds` aggregate adds a **Gap 0
  normalized** column and a gap-0 guard / niche line. All of it comes from battles already run.
- **Cost.** About 60% of one every-team run per cell (the gap-0 share is reused): the default run
  went from about 13 s to 16 s, the tuned-report command from about 30 s to 33 s; five seeds
  (`--mode pve --seeds`, no panel) take about 80 s.
- **Noise.** A dealt gap makes each battle a single draw at the mix's clear rate, so the marginals'
  roll noise is what it was at gap 0 (the same number of battles); the per-gap columns of the section
  are small (5% of a cell for a +/-3 gap) and only indicative. For a clean per-gap curve use
  `--level-gap`.

`--gap-mix 0` turns it off and reproduces the pre-mix report byte for byte.

## Avatar value

`--avatar-value` measures what the avatar is worth. At every cell's calibrated multiplier the picked
teams' battles (the calibration's own, `--calibrate-samples` per composition) are replayed with **no
avatar**, seed for seed, so the two runs differ only by the avatar. Its **value** is the scouted rate
with it (the calibrated rate, about 50%) minus the rate without it, in points. Beside it the section
gives the avatar's **direct share**: its damage, healing and shield soak as a percent of the whole
team's (beasts and avatar) over the with-avatar battles, and how much of that its passives produced.
Buffs, debuffs and auras have no direct output (they act through the beasts), so the share is a
floor on the avatar's role and the value is the whole of it. Accounting, per turn, from HP and shield
snapshots and the turn's hits: enemy HP lost on the avatar's own turns and to its passives' hits is
the avatar's damage, the rest the beasts' (damage over time ticking on an enemy's turn included);
team HP restored on the avatar's turns is its healing; a hit a shield soaks is credited to whoever
cast that shield (damage over time soaked by a shield is not counted). None of it changes a battle.

`--turn-detail` is the matching diagnostic for beasts ("PvE beast turns"): how often each beast's turn
fires nothing, and why (a skill held by its stance, no target in reach, stunned), and how its damage
splits between large enemies (bosses) and the rest.

## Scouted picking

The game shows the player each encounter before the team is placed (`EncounterPreview`, see
`docs/design/battle-system.md`, "Encounter preview"): the enemy groups with their elements, stances
and counts. "PvE scouted picking" (`ScoutedPicker.cs`, `ScoutingReport.cs`) measures what that is
worth. Every team already fights every composition, so a scouted pick needs no new battle: each
strategy names one of the 210 teams per composition, and that team's recorded result against the
composition is the outcome. The section is on by default (`--scouted all`) and costs well under a
second; `--scouted none` drops it and its header line, and the rest of the report is byte-identical.

- **Calibration.** By default each shape's multiplier aims the **Heuristic + bonds** pick at the shape's target
  (`--calibrate-on bonds`, see "Difficulty calibration"), so that column sits near the target (within its
  noise: here it rests on one battle per composition and level, the calibration on 16), and the
  **No scouting** column (the mean over every team: the unscouted player) sits below it; every
  strategy's gain over no scouting reads as **uplift** in points. With `--calibrate-on mean` the
  multiplier aims the average team at 50% instead, and the column is headed **Baseline** as before.
- **Random**: a seeded random team per composition. Its expected uplift is 0; its actual gap is the
  noise scale of the table.
- **Heuristic**: an element counter-pick from the preview alone (`--scouted-detail` sets how much it
  sees). Each beast scores, per enemy,
  `sum over groups of Count x (1.0 x chart(beast element -> group element) - 0.5 x chart(group element -> beast elements)) / enemies`,
  the attack using the beast's first element (its kit element). The team is the 4 best scores, ties
  to roster order; if it has fewer than `--scouted-vanguard-min` (default 1) Vanguards, its
  lowest-scored non-Vanguard is swapped for the best-scored unpicked Vanguard. It ignores stats,
  kits, levels and bonds, so its picks are the same in every kit mode and level.
- **Heuristic + bonds** (bonds on only): every team meeting the Vanguard minimum scores its members'
  heuristic scores plus, per tier of each tiered bond it activates, that bond's weight
  (`TeamSuggester.BondWeights`, fitted at 0.1 per point of the bond's pooled `elemental` panel excess;
  0.5, `TeamSuggester.BondWeight`, for a bond not listed; nothing for a bond that answers only afflicted
  allies when no enemy can stun or burn, `TeamSuggester.CanAfflict`) and 0.125 per stack of each
  scaling bond (`TeamSuggester.ScalingBondWeight`; nothing for an `Others` bond no teammate receives);
  the best
  team is fielded, ties to the lower team index. 0.5 is the gap between a neutral and a strong
  matchup against one enemy in half the lineup's weight: a starting knob, not tuned.
  **The game's own picker.** The scores, the bond weights and the choice rule are the Runtime's
  `TeamSuggester` (`BeastCraft.Battle.Scouting`), the team the game suggests after repeated losses
  (`docs/design/battle-system.md`, "Encounter preview"); `ScoutedPicker` only adapts the simulator's
  fixtures and team list to it. Every PvE run also asks `TeamSuggester.Suggest` for each composition
  with the whole roster (one level) as the owned beasts and prints `TeamSuggester parity: n of n
  compositions ...` to stderr; anything short of 100% fails the run (exit 3). The report is unchanged
  by the port (byte-identical).
- **Best team** (with `oracle`): the one lineup with the best clear rate in the same mode and shape
  at the *other* levels, scored at this level (ties: the other levels over every shape, then the lower
  index). It knows which team is strong but not what it faces, and it is held out, so the damage-roll
  luck it was chosen on does not count: it is the bar counter-picking has to clear to matter.
- **Oracle**: per composition, the team that did best against it (ties: the team's clear rate over
  the whole cell, then the lower index). An upper bound. With one battle per team and composition it
  is also a luck bound: among 210 coin flips one nearly always wins, so it reads close to 100%.
- **Neutral mode is the control.** With every skill `None` the chart the heuristic reads does
  nothing, so its `neutral` uplift is what its picks are worth as lineups; the gap between the
  `elemental` and `neutral` uplift is what the counter-pick itself earns.
- **Pick rates**: per kit mode and shape, the percent of picks (one per composition and level) that
  field each beast, for the heuristic (H), the bond-aware heuristic (B) and the oracle (O); each
  column sums to 400 (4 beasts per pick). **0** / **100** flag a beast never / always fielded, and
  the bullets under the table list them.
- **Self-check invariants** (every run with scouting on; a failure exits 3): the oracle is at least
  the baseline, the best team and every other strategy in every cell; every pick is a real team; the
  heuristic pickers meet the Vanguard minimum whenever the roster has that many Vanguards; and each
  strategy's pick counts sum to picks x team size per shape. For a scouted-pick calibration (every
  run, scouting section or not; `ScoutedPicker.CheckCalibration`): each cell's picks equal the
  picker's own for its shape, recomputed, are real teams meeting the Vanguard minimum, and are the
  same in every mode and level; the picked battles are complete and clear exactly the reported
  scouted rate; each picked team's first sample equals its every-team battle (outcome, time, turns);
  and, with `--self-check`, no scouted rate misses the target by more than 10 points except at a
  genuine step.
- **Noise.** A shape's figure rests on one pick per composition and level (24 battles by default),
  a binomial SE of about 10 points; judge on the `--seeds` aggregate (and more `--compositions` for
  a tighter figure), not on one seed. Findings are in `docs/balance/tuning-log.md`, "Scouting and
  counter-picking".

### Worked example of the heuristic

A composition of one Fire Giant, three Water Archers and two Metal Brutes (6 enemies) at
`--scouted-detail full` previews as three groups: Fire x1, Water x3, Metal x2. A beast's score is
`(1 x (off(Fire) - 0.5 def(Fire)) + 3 x (off(Water) - 0.5 def(Water)) + 2 x (off(Metal) - 0.5 def(Metal))) / 6`,
with off = the chart multiplier of the beast's element into the group's, def = the group's into the
beast's:

| Beast | Element | Fire x1 | Water x3 | Metal x2 | Score |
| --- | --- | --- | --- | --- | ---: |
| Leviathan | Water | 2 - 0.5 x 0.5 | 1 - 0.5 x 1 | 2 - 0.5 x 1 | 1.042 |
| Treant | Nature | 1 - 0.5 x 2 | 2 - 0.5 x 0.5 | 1 - 0.5 x 1 | 1.042 |
| Thunderbird | Lightning | 1 - 0.5 x 1 | 2 - 0.5 x 0.5 | 0.5 - 0.5 x 2 | 0.792 |
| Griffin | Air | 2 - 0.5 x 1 | 1 - 0.5 x 1 | 1 - 0.5 x 0.5 | 0.750 |
| Basilisk | Dark | 1.25 - 0.5 x 1 | 1 - 0.5 x 1 | 1.25 - 0.5 x 0.5 | 0.708 |
| Kirin | Light | 1 - 0.5 x 1 | 1.25 - 0.5 x 1 | 1 - 0.5 x 2 | 0.458 |
| Phoenix | Fire | 1 - 0.5 x 1 | 0.5 - 0.5 x 2 | 2 - 0.5 x 0.5 | 0.417 |
| Golem | Earth | 1 - 0.5 x 0.5 | 0.5 - 0.5 x 1 | 1 - 0.5 x 1 | 0.292 |
| Frost Wyrm | Ice | 0.5 - 0.5 x 1 | 1 - 0.5 x 1 | 0.5 - 0.5 x 2 | 0.083 |
| Tarasque | Metal | 0.5 - 0.5 x 2 | 1 - 0.5 x 2 | 1 - 0.5 x 1 | 0.083 |

The top four are Leviathan and Treant (tied; roster order puts Leviathan first), Thunderbird and
Griffin, so the heuristic fields **Leviathan, Griffin, Thunderbird, Treant** (in roster order).
Leviathan and Treant are Vanguards, so no swap is needed; had the four been, say, Thunderbird,
Griffin, Basilisk and Kirin, the lowest of them (Kirin) would have made way for the best-scored
Vanguard. Basilisk misses by 0.04: the Fire Giant is a single enemy, so Griffin's 2x into it
outweighs Basilisk's mild 1.25x into two Metal Brutes. At `dominant-element` the preview is one
line, Water x6, and the scores become the Water column alone: Thunderbird and Treant (1.75), Kirin
(0.75), then Leviathan, Griffin and Frost Wyrm tied at 0.5, so the team is Thunderbird, Treant,
Kirin and Leviathan.

## PvP: 1v1 round-robin (secondary)

Every pair of distinct species is played twice per level and kit mode with the sides swapped (each
game `--samples` times), on a
Medium board, one beast per side: the most central tile of the player zone against its point
mirror. Mirror matches are skipped. `TurnManager` breaks initiative ties on the ordinal unit id,
and the ids are fixed per side (`p` / `e`), so playing each pairing both ways gives each beast the
tie-break exactly once. Battle length is reported in normalized time and total turns. Stalemates and mutual defeats count as games but not wins.

## Sampling

Damage has a uniform 90-110% variance roll and each beast (and enemy) a crit chance
(`CritChance`, x1.5), both drawn from the battle's rng (design doc, "Variance and critical hits"),
so one battle per team is a single draw. At the calibrated multiplier every PvE team fights every
composition `--samples` times (with `--calibrate-on mean`, at every calibration step too; the
default scouted-pick search instead runs each picked team `--calibrate-samples` times per
composition), and every PvP game is played `--samples` times, each with its
own seed (the sample index is part of the seed). With generated encounters the default is **1 sample
per team and composition**: the eight compositions already vary the fight, so each team fights each
shape 8 times (more than the fixed set's 5), and the variety is spent on different fights
rather than on re-rolling the same one. The every-team run is 1680 battles; a beast's metrics in one cell
rest on 672 battles with it (84 teams x 8), and its overall marginal on 8064. The fixed set keeps 5
samples, and PvP 5. The report's "Critical hits and damage rolls" table checks the plumbing: each
beast's observed crit rate and average roll multiplier against its authored chance.

**Noise.** Rerunning the default PvE run with `--seed 777` (which also draws different compositions,
so it measures composition sampling as well as roll noise) moves a beast's overall marginal by
2.2 points on average and at most 4.2 (`elemental`; 2.0 and 6.2 in `neutral`), and a single
shape cell by up to 16.5 (library setup, after the authored-kits retune; the standard kit measured
2.0 / 4.8 / 12.6); see the tuning log. Raise `--compositions` or
`--samples` to shrink it (the run time grows in proportion), or judge on the mean of several seeds
(`--seeds`, see "Multi-seed runs").

## Multi-seed runs

A single seed's marginals carry a couple of points of noise (above), so balance passes judge
candidates on the mean of 3-5 base seeds. `--seeds` does that in one process:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --mode pve --seeds 12345,777,4242 --out out/candidate.md
```

- Each seed's run is exactly the `--seed <n>` run with the same other arguments (it loads its own
  generated compositions); with `--out`, its full report is written as `out/candidate.seed<n>.md`,
  byte-identical to `--seed <n> --out`. `--self-check` applies to every seed.
- stdout and `--out` get the **aggregate**: first **calibration over seeds** (per kit mode and
  shape, the mean multiplier and its range, and the scouted and no-scouting clear rates with their
  gap; with `--calibrate-on mean` only the no-scouting rate, the calibrated one). Then per kit mode,
  every beast's marginal clear rate per
  shape (mean over seeds, the rank of that mean and in how many seeds it was top 3 there) and
  overall (mean, sample standard deviation, range and each seed's value), the **normalized**
  overall marginal (mean and SD over seeds; see "Metrics") with **!** outside the balance guard
  (+/-4 `elemental`, +/-7 `neutral`), plus the range of the raw and normalized means, the beasts
  outside the `--marginal-threshold` band (raw) and outside the guard (normalized), and the beasts
  with no top-3 shape on the means. With the level-gap mix on (the default) all of these are over the
  mix, and a **Gap 0 normalized** column and a gap-0 guard / niche line give the equal-level reading
  beside them. Then **team composition over seeds**: each team's clear rate averaged over the seeds,
  with the teams' spread within a seed (per-seed SD), how much one team moves between seeds
  (seed-to-seed SD: damage rolls and each seed's composition draw) and the **persistent SD**,
  sqrt(per-seed SD² - seed-to-seed SD²), the spread that is the lineup's own; the percentiles,
  histogram and best / worst lineups of the seed means; and each pair's synergy per seed with the
  mean, SD over seeds and a noise estimate (`*` = mean beyond 2 x noise). It is deterministic like
  the reports.
- With scouting on (the default), **scouted picking over seeds**: each strategy's clear rate and
  uplift per shape averaged over the seeds, the SD of the heuristic's uplift over seeds, and the
  pick rates averaged over seeds (**0** / **100** = never / always in every seed).
- The seeds run one after another, each using every core, so the wall clock is about the sum of
  single-seed runs (loading and JIT are a second or two of an 11 s run). What it replaces is the
  bookkeeping: one process per seed and scripts parsing the Markdown back; the aggregate comes
  straight from the simulator's numbers, unrounded.

## Composition panel

`--panel KxS` measures how much the lineup matters, and how much *counter-picking* matters, on a
fixed set of opponents (`PanelReport.cs`). The run's own compositions change with `--seed`, so the
older multi-seed "persistent SD" folded each seed's composition draw into the lineup's spread; the
panel holds the compositions still:

- K compositions per shape are drawn by the game's generator from the constant
  `SimOptions.PanelSeed` (ids `panel-<shape>-NN`), identical in every run and every seed.
- All 210 teams fight every panel composition S times at `--panel-level` (default 50), at that
  cell's calibrated multiplier; each battle is seeded like any other (the panel id is in the seed).
- Per kit mode and shape, a two-way ANOVA of the 0/1 outcomes (teams x compositions, S replicates):
  **team main-effect SD** = sqrt(Var(team means) - noise²), noise² = the mean cell variance
  p(1 - p) / (S - 1) divided by K (the roll noise of a team's panel mean); **interaction SD** =
  sqrt((MS_int - MS_err) / S), how much a team's clear rate depends on which composition it faces
  beyond the two means. Pooled = root mean square over the shapes.
- Clear rate by stance mix (Vanguard / Ranged / Skirmisher counts), per shape.
- Per library bond: **excess** = the active teams' mean panel rate minus the additive prediction
  from the beasts' panel marginals (a lower bound on the bond's value, since part of it is absorbed
  into its members' marginals), and **reactions per battle** for a behaviour bond.

`16x4` is 53,760 battles per kit mode (about 14 s on 8 cores); it is part of the committed
tuned-report command. With `--seeds`, "PvE composition panel over seeds" lists every seed's SDs.

## Pacing (`--mode pacing`)

A Monte Carlo model of skill progression and the material economy (`PacingSimulator.cs`), separate
from the PvE and PvP runs (it loads only `skill-library.json` and `drop-tables.json`, never the
roster or encounters, and leaves the default report untouched):

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --mode pacing --self-check --out docs/balance/pacing-report.md
```

- Each **campaign** is `--battles` (500) battles. Battle `i` is at encounter level
  `min(100, 1 + i / 5)`; its shape is drawn solo 15 / elite 20 / squad 35 / horde 30; it is cleared
  with probability 0.8; the focus skill fires 3-9 times (uniform) and a secondary skill the same.
  Practice goes through `SkillProgression.AwardPractice` win or lose; a clear rolls
  `LootRoller.RollClear` against the drop tables. No battle is fought: this measures the economy,
  not combat.
- **Policy**: after each battle the focus skill passes a gate it waits at with the lowest adequate
  material held, then is fed materials lowest tier first while below its cap, keeping one material
  per tier its later gates need; anything it cannot use spills to the secondary skill.
- `--runs` (1000) campaigns per base seed; `--seeds a,b,c` pools every seed's campaigns. Campaign `r`
  of seed `s` is seeded `LootRoller.DeriveSeed(s, r)`, each battle `DeriveSeed(campaign, i)`.
- The report gives p10 / p50 / p90 battles for the focus skill to reach levels 5, 10, 15 and 20,
  the level by battle, material income and first-drop timing, the practice / material XP split
  and the spill-over skill's final level. **Targets** (`PacingSimulator.Gates`, on the median): L5
  15-20, L10 70-90, L15 160-200, L20 295-325. `--self-check` runs twice, demands identical reports
  and fails (exit 3) when a median misses its band. About 1.5 s.
- **Avatar level**: every campaign also levels an `AvatarProgress` with
  `AvatarProgression.AwardBattle` (win or loss by the same clear roll, at the battle's encounter
  level); the report tabulates its p10 / p50 / p90 level every 50 battles, and `--self-check` fails
  when the median strays more than `AvatarLevelTolerance` (3) levels from the encounter level. It
  draws no random numbers, so it never shifts the skill-pacing results.
- **Beast level**: every campaign also levels one `BeastProgress` with
  `BeastProgression.AwardBattle`, the beast fielded in every battle and knocked out in
  `KnockoutChance` (20%) of them (a modelling assumption, not measured from the PvE runs; a
  knocked-out beast earns participation XP only). Same checkpoints, p10 / p50 / p90, and
  `--self-check` fails when the median strays more than `BeastLevelTolerance` (3) levels from the
  encounter level. The knockout roll is drawn last each battle, so it leaves every earlier draw (and
  the skill-pacing results) unchanged. The XP rules are in
  [`docs/design/progression-and-saves.md`](../../docs/design/progression-and-saves.md), "Beast and
  avatar level".
- The model's constants (level ramp, shape weights, clear chance, uses per battle) are at the top of
  `PacingSimulator.cs`; the drop numbers are data. See the design doc, "Material economy", and
  `docs/balance/tuning-log.md`, "Material economy".

## Performance

The default run took 210 s before the performance pass and about 50 s after it (8-core Intel Core
Ultra 7 258V, .NET 10), with a **byte-identical report**; the scouted-pick calibration then cut it to
about 12 s (below). What was measured (a `dotnet-trace` CPU sample and the `--timings` breakdown)
and what was done about it (the pass itself, measured under `--calibrate-on mean`):

- **All the time is PvE calibration.** PvP takes 0.06 s and the report 0.1 s. PvE is 24 cells
  (2 kit modes x 4 shapes x 3 levels), each about 10 calibration steps of 1680 battles; the final
  step's battles are the reported ones, so nothing is run twice. The `horde` cells (16-24 enemies
  on a Large board) were two thirds of the time.
- **Path finding was 60% of the CPU**, almost all of it hash-set and dictionary work in
  `HexPathfinder.FindPath` (`TryPlanApproach` runs up to seven searches per approach, about 190
  searches per horde battle), and the run allocated 297 GB (51,700 gen0 GCs). The Runtime now keeps
  a search's per-tile state in flat arrays indexed by `HexGrid.TileIndex` and reused per thread,
  the open set is a binary heap on exactly the old expansion order (f, then distance to the goal,
  then queue order), `HexGrid` answers bounds, occupancy and blocking from arrays, and the
  stance search (`ReachableTiles`) works the same way. Paths, tie-breaks and every battle are
  unchanged: a differential test of the old and new grid, pathfinder and reachability on 40,000
  random boards (1.6 million path queries) found no difference, and every report below is
  byte-identical. Smaller Runtime cuts: a precomputed ATB fill-rate table, no status snapshot when
  a unit has no damage-over-time, and a pre-sized target list.
- **Server GC** (`BalanceSim.csproj`): allocation is now 51 GB, and per-core heaps cut the rest of
  the GC cost (58 s to 49 s).
- **Identical calibration steps are reused** (see "Difficulty calibration"): about one step per
  level-1 cell.
- Tried and dropped: running several cells at once (no gain: the machine is already saturated;
  two concurrent runs take twice as long), and stopping a calibration step early once its clear
  rate provably cannot become the best (at most about 5% of the work, because the steps that
  matter land close to the target).

| Run | Before | After |
| --- | ---: | ---: |
| Default | 211 s | 51 s |
| Default with `--self-check` | 410 s | 94 s |
| 3 seeds (12345, 777, 4242): before, three processes; after, `--seeds` | 606 s | 141 s |
| 5 seeds (+ 1, 2) | 1012 s | 231 s |
| 3 seeds with `--calibrate-sample 30` (opt-in, changes results) | - | 36 s |

**`--calibrate-sample <n>` (opt-in).** The calibration search is 9 of every cell's 10 steps. With
`--calibrate-sample 30` the search evaluates a seeded subset of 30 of the 210 teams, and only the
chosen multiplier runs with every team (so every number in the report is still over all 1680
battles per cell). It is about 3.5x faster, but it moves each multiplier slightly, so the report is
**not** identical: on the default run, beasts' overall marginals moved by 0.2-0.3 points on
average and at most 0.7, and a shape cell by at most 2.0 (with 60 teams: 0.1-0.2, 0.7 and 2.3),
where changing the seed (777) moves them by about 2 on average, up to 6.7, and a shape cell by up
to 18. The report says so in its PvE
configuration. Use it for quick iteration, and the default for anything committed.

**Unit footprints** (multi-hex bosses) cost about a tenth: the default run went from 47.9 s to
52.5 s on the same machine, most of it in the `solo` and `elite` cells (+1.8 s each: a seven-tile
giant's moves test every tile of its footprint, and one-tile units closing on it aim at twelve goal
tiles instead of seven). A battle of one-tile units runs the same code as before (one enum compare
per distance): the `squad` and `horde` cells moved by run-to-run noise only, and their results are
byte-identical.

**The scouted-pick calibration** (`--calibrate-on bonds`, the default) is also the largest speed-up
since the performance pass: each search step runs only the picked team per composition, 16 times
(128 battles instead of 1680), and only the chosen multiplier runs every team once. The default run
went from about 52 s to about 11 s (PvE about 10 s), `--self-check` from about 95 s to 22 s, and three
seeds (`--seeds 12345,777,4242`) from about 150 s to 33 s. `--calibrate-on mean` costs what the
default did before (3 seeds: 166 s).

Measure before optimizing further: `--timings` prints the per-cell and per-step breakdown.

## Determinism

Every battle gets its own `System.Random`, seeded from the base seed and the battle's inputs (never
from the calibration multiplier), and the sample index; the generated compositions come from their
own rng (the game's `EncounterGenerator`), seeded from the base seed alone. That rng is
`System.Random`'s seeded (legacy) algorithm, which Mono and Unity share; an EditMode test pins its
first draws. The kits target by distance, a stat or current HP, never
at random, so a battle's rng is consulted only for damage rolls: crit then variance, two draws per
damage effect that lands, in the Runtime's fixed order. Parallel results are stored by composition,
team and sample index and aggregated in a fixed order. The report
contains no timestamps, machine paths or timings, and always uses LF line endings. The same roster,
content, fixtures, code and arguments produce a byte-identical report.

## Design assumptions (and what they bias)

- **Default loadouts only.** Every beast fields its three authored default skills at one skill level
  (default 1) for the whole run; the other learnable skills and passives are never fielded, and the
  player's loadout choices are not modelled. (With `--skill-kit standard`, one kit for everyone: no
  beast is played to its strengths.)
- **Simple targeting, stance-only tactics.** Beasts and most enemies hit the nearest enemy, so
  whoever is in front takes most of the hits; the stalker, the caster, the champion's hex (and, in
  the fixed set, the wisps and stingers) pick off the beast with the least current HP. Beyond the
  combat stances there is no AI: nobody retreats when hurt, spreads out against area skills or
  focuses fire deliberately. Those are Runtime rules, not simulator choices, but they colour every
  number.
- **The enemy library.** Its stat ratios, kits, threat weights and the element-scheme weights decide
  which beasts look good. The calibration removes overall difficulty, but not shape. The library is
  small and hand-made (it came over unchanged from the old simulator fixtures): which types exist,
  and how often each is drawn, is itself a bias.
- **Large creatures are simple shapes.** The giant and the colossus cover seven tiles and the
  champion three (no rotation, no terrain interaction beyond fitting), measured nearest tile to
  nearest tile; a giant cannot be knocked back and a champion moves at most one tile. Before the
  footprints (see the tuning log, "Large enemies (footprints)") every boss was one hex and could be
  surrounded by six attackers; a giant now has twelve tiles around it.
- **No gear,** a fixture avatar stat block, and only species base stats, the growth curve and the
  level.

All tunables (kit numbers, calibration bounds, flag thresholds, CLI defaults) are constants at the
top of `SimOptions.cs`; the enemies, shapes and element-scheme weights are game content in
`content/data/Encounters/`, and the legacy fixed encounters are in
`encounters.json`.

## Build and format

```sh
dotnet format Tooling/BalanceSim/BalanceSim.csproj --verify-no-changes
dotnet build  Tooling/BalanceSim/BalanceSim.csproj -c Release
```

It targets `net10.0` (the installed LTS SDK) and compiles with C# 9 and nullable disabled, like the
game runtime it references (`src/BeastCraft.Core`, netstandard2.1). It reads the authored data JSON
under `content/data/` directly with System.Text.Json (found by walking up), never
through Unity assets.

## Economy (`--gear`, `--economy-probe`, and `--mode campaign`'s economy)

- `--gear none|common|rare|epic|typical` (default `none`): the PvE player team wears three pieces of
  the encounter level's band from `gear-library.json` (`GearKits`: a fang or focus stone by the
  beast's higher attack stat, barding or warding mantle by its higher defence, a keen collar for a
  base crit of 8+ else a wind charm). `typical` is what a player normally wears at the level (the
  shipping difficulty's assumption). The default run, its report and the balance guard stay gearless
  (byte-identical); a gear run adds one header line.
- `--economy-probe` (alias `--consumables`): appends "PvE economy probe": every cell replayed at its
  calibrated multiplier by every team with each gear profile and each consumable of
  `consumable-library.json`, as clear-rate points and levels-equivalent against the team one level
  above the enemies. About 50-75 s on top of the default PvE run.
- `--gear-library`, `--consumable-library`: the files (default: found by walking up).
- `--mode campaign` includes the economy (`CampaignEconomyModel`: gold, drops, the Trader at trading
  posts and camps, a greedy shopper) on its own random stream, with its gates in the report's
  "Economy" section. See docs/design/economy-and-shop.md.

## Idle rewards (`--mode campaign`'s idle model)

`--mode campaign` also plays the idle (AFK) rewards (`CampaignIdleModel`): the game's
`IdleRewardCalculator` and `idle-rewards.json` on the model's save, at a claim cadence. The player
fights `--battles-per-day` (25) battles a day and is away `--idle-hours-per-day` (16, 0-24; 0 turns
idle off) hours a day, claiming `--idle-claims-per-day` (2) times a day, evenly spaced: a claim every
`battles / claims` battles, each crediting `hours / claims` hours (paid up to the data's 8-hour cap).
The claim pays the fielded beasts as the party and the rest as the bench, and its gold, XP and
materials feed back into the campaign. Every idle roll comes from the save's idle seed
(`DeriveSeed(campaign seed, CampaignIdleModel.Stream)`), so idle never moves the campaign's own draws:
with `--idle-hours-per-day 0` the report is the pre-idle report plus an "Idle rewards: off" line.
The report's "Idle rewards" section gives the rates, idle's share of each region's income and the
campaign gates: idle at most 15% of all gold and of all materials (by XP value) and at most 10% of all
beast XP (p50; the lead / user ceilings). See docs/design/progression-and-saves.md, "Idle rewards".
