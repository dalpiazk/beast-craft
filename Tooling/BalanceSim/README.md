# Tooling/BalanceSim

A headless balance simulator for Beast Craft. It runs the **real** battle code (the game's `Runtime`
scripts, compiled outside Unity against the committed `Tooling/CiStubs/UnityStub`) and prints a
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
| `--kit <k>` | `both`, `standard` | Two axes on one flag; pass it twice to set both. Element axis: `elemental`, `neutral` or `both` (see below). Skill axis: `standard` (the standard kit, the committed report's setting) or `library` (each beast's authored `DefaultLoadout` from `skill-library.json`; see "Library kits"). |
| `--skill-level <n>` | `1` | Skill level (1-20) for library skills and library avatar skills; the tier is the gates below that level (16+ = all three passed). |
| `--skill-library <path>` | found by walking up | Path to `skill-library.json` (read only with `--kit library` or `--avatar library`). |
| `--levels <list>` | `1,50,100` | Comma-separated levels; beasts and enemies fight at the same level. |
| `--encounter-set <s>` | `generated` | `generated`: random compositions per shape (see "Generated encounters"). `fixed`: the three hand-authored encounters (`boss`, `swarm`, `pack`). |
| `--compositions <n>` | `8` | Generated compositions per shape. |
| `--encounters <list>` | all | Comma-separated shape ids (`solo`, `elite`, `squad`, `horde`) or, with `--encounter-set fixed`, encounter ids (`boss`, `swarm`, `pack`). |
| `--team-size <n>` | `4` | Beasts per PvE team, 1-6. Every combination of the roster is fielded (C(10,4) = 210). |
| `--target-clear <pct>` | `50` | Clear rate the difficulty calibration aims for. |
| `--marginal-threshold <x>` | `5` | Flag a beast whose overall marginal clear rate is outside +/-x points. |
| `--enemy-element <e>` | `authored` | `authored` (as generated, or as authored in the fixed set), `None` or an element name: override every enemy's element. |
| `--max-time <n>` | `2000` | Battle-time cap (normalized, see below); a battle that reaches it is a stalemate. |
| `--seed <n>` | `12345` | Base seed; each battle derives its own seed from it. |
| `--samples <n>` | PvE 1 (generated) or 5 (fixed); PvP 5 | Battles per PvE team and composition (every calibration step) and per PvP game, each with its own seed. Damage variance and crits make battles random; see "Sampling" below. |
| `--matrix-level <n>` | `50` | Level of the PvP win matrix and the stat table (falls back to the highest simulated level). |
| `--roster <path>` | found by walking up | Path to `beast-roster.json`. |
| `--encounters-file <path>` | found by walking up | Path to `encounters.json`. |
| `--avatar <preset>` | `none` | PvE only. `none` fields no avatar (the committed report's setting). `support` fields a fixture avatar with three passive skills beside every player team (`AvatarPresets.cs`; not authored content). `library` fields the skill library's default avatar: its first three actives and `AvatarDefaultPassives`, at `--skill-level`. Either adds an "Avatar passives" section with firings per battle. See `docs/design/battle-system.md`, "Avatar passives" and "Beast skill kits". |
| `--out <path>` | none | Also write the report to this file (it always goes to stdout). |
| `--self-check` | off | Run everything twice and fail unless both reports are identical; also replay sample PvE battles through `BattleTurnExecutor.RunBattle` and fail if the simulator's loop disagrees. |

Exit codes: `0` success, `1` bad arguments, `2` missing or invalid roster, skill library or encounters
(the roster is checked with `BeastRosterValidator` and the library with `SkillLibraryValidator`
first, exactly as the Editor importers do), `3` a self-check failed. The run time goes to stderr, never into the report. The default run (both modes, both kits,
three levels, four shapes x 8 compositions, 1 sample per team and composition) takes about
150 s on an 8-thread machine (about 5.5 minutes with `--self-check`, which runs
everything twice and replays two teams per composition through `RunBattle`). PvE battles run in
parallel, and the output is identical whatever the thread count.

Two reports are committed, both the default arguments:

- `docs/balance/baseline-report.md` — the "before" picture, on the roster's first-draft stats. It is
  kept as a record and is **not** regenerated (a fresh run now reads the tuned roster). It predates
  the ATB turn order, so its battle lengths are in rounds.
- `docs/balance/tuned-report.md` — the current roster after the second tuning pass (see
  `docs/balance/tuning-log.md`, "Retune for ATB + stances + crits + mixed encounters"), under the
  current Runtime (the square-root ATB turn order, the mitigation damage formula, combat stances,
  variance and crits) and the generated encounters. It was regenerated after the formula change on
  the **unchanged** roster, so it shows the balance shift the next retune has to absorb. Regenerate it
  whenever the roster, fixtures, simulator or Runtime change:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --out docs/balance/tuned-report.md
```

## Library kits

`--kit library` replaces the standard kit with each beast's authored `DefaultLoadout` from
`BeastCraft/Assets/_Project/Data/Skills/skill-library.json` (`SkillLibraryLoader.cs`), built through
`SkillLibraryBuilder` — the same DTO-to-`SkillSO` mapping as the Editor importer — and fielded as
`SkillInstance`s at `--skill-level` and the tier that level implies. In `neutral` mode every library
skill's element is forced to `None`. The report then shows a "Library beast kits" table in place of
the standard kit and drops the kit parity table (it measures the standard kit's Strike / Shot / Blast
balance; library kits differ by design). PvP uses the library kits too. `--avatar library` fields the
library's default avatar (first three actives, `AvatarDefaultPassives`) on the same skill level; the
avatar's stats are the same level-scaled fixture block as `support`'s.

The default stays `--kit standard --avatar none`, so the committed report is unchanged until the
retune that adopts the library kits. For a quick check:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --kit library --avatar library --self-check --mode pve --levels 50 --compositions 2
```

## The standard kit

With the default `--kit standard`, every beast fights with the same kit, and its stat line is what gets
measured. Fire priority is Blast, the physical single-target skill, then Burst. Every beast skill
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
takes the same share of HP as before; enemy powers in `encounters.json` were rescaled the same way
(`P' = round(1.52 P + 7)`).

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
  `SmallGroup`; each beast is in 84). No gear, no avatar.
- **Encounters.** `encounters.json` beside this file. **These are simulator fixtures, not game
  content:** synthetic enemies that are not roster beasts, and no game code reads them. Each enemy
  becomes an in-memory `CreatureSpeciesSO` on the roster's `medium` growth curve, so it is built by
  the same `BattleUnitFactory.CreateBeast` and scales with level the same way as a beast. Enemy kits
  are authored in the file and are `Element.None` in `neutral` mode. Two sets:
  - **Generated (default).** An enemy type pool and four encounter shapes; the simulator draws
    random compositions per shape (see "Generated encounters" below).
  - **Fixed (`--encounter-set fixed`).** The three hand-authored encounters used before the
    generator (`boss`: one Colossus; `swarm`: 12 biters + 12 stingers on a Large arena; `pack`: 3
    direwolves + 3 wisps). Elements are listed per group and cycled over its units. The wisps and
    stingers now aim at the beast with the least current HP (`CurrentHp`), no longer the lowest
    maximum HP.

  Each enemy has a `Stance` (a `CombatStance` name; missing = `Vanguard`), and each skill an optional
  `Targeting` (`Distance`, the default, `Stat`, `CurrentHp` or `HpFraction`; `Random` is refused so battles never
  draw targets from the rng), `TargetingOrder` (default `Lowest`) and `TargetingStat` (default `HP`,
  read only by `Stat`). `Stat` + `HP` compares the stat block, i.e. **maximum** HP; `CurrentHp`
  compares the HP a beast has left, so `CurrentHp` + `Lowest` is "pick off the weakest"
  (`SkillTargetResolver`). The loader rejects a Ranged enemy without a `SingleTarget` skill of range
  2 or more (a Ranged unit never walks into melee), an enemy without a `SingleTarget` skill (the only
  shape that walks), move 0, and a composition that cannot fit its deployment zone.

  The advanced effect fields are optional, and every one is inert when missing. See "Status effects
  and advanced skill effects" in `docs/design/battle-system.md`.
  - Per enemy: `StatusResist` (0–100, `BattleUnit.StatusResist`).
  - Per skill: `HitCount`, `ExecuteBonusPercent`, `InitialCooldown` (−1 means the ordinary
    cooldown) and `MaxUsesPerBattle`.
  - Per skill: `Effects`, a list of further `SkillEffect`s applied after the damage effect. Each
    entry has `Type`, `Status`, `Stat`, `Magnitude`, `DurationTurns`, `Chance`, `MaxStacks`,
    `IsPercent`, `HitCount` and `ExecuteBonusPercent`. The enum-valued fields take the runtime enum
    names.
  - `Targeting` also accepts `HpFraction`.

  The bosses (giant, champion and the fixed colossus) carry `StatusResist` 50. No fixture skill uses
  the other fields yet, so the reports are unchanged.
- **Placement.** Each side takes the front-most tiles of its own deployment zone (front row first,
  then outward from the centre line). Enemies are placed in fixture order; the team is committed
  through `PlacementValidator.TryPlaceAll`. Which member gets which slot is a fixed seeded shuffle
  per team, because the slot fixes the unit id, and ids break initiative ties and equal-distance target
  ties within the team. Pinning slots to roster order would always expose the first species.
  (Initiative ties *between* the sides are split separately; see below.) Enemies are placed front to
  back in composition order: Vanguard types first, then Skirmishers, then Ranged. Placing every enemy
  is checked on every battle, and the loader rejects a shape whose largest draw does not fit its zone.
- **Difficulty calibration.** One multiplier per (kit mode, shape, level), shared by all the shape's
  compositions (per fixed encounter with `--encounter-set fixed`), scales every enemy's
  HP, Atk, Def, SpA and SpD. Speed and Move stay unscaled: Speed is how many turns a unit gets, so
  scaling it would change the enemies' action economy, not just their toughness. The multiplier starts at 1 and doubles or halves until the target clear rate is
  bracketed (between 1/64 and 64), then bisects 8 times. The evaluated multiplier closest to the
  target wins (first evaluated on a tie). The process is deterministic because each clear rate is.
  Each clear rate is over every team's battles against every composition of the shape (210 teams x
  8 compositions x 1 sample = 1680 per evaluation at the defaults). The report lists each
  composition's own clear rate at the shape's multiplier, and the range per cell.
  Where a step in the clear-rate curve cannot be split (for example level 1, where enemy stats
  round to 1-2), the closest rate is reported; a miss beyond 10 points is flagged.
- **Metrics**, all at the calibrated multiplier:
  - **Marginal clear rate**, the primary number: the clear rate of teams containing the beast minus
    teams without it, in points.
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
- **Flags.** Overall marginal outside +/-5 points; **no niche** (bottom 3 in every shape);
  **no weakness** (top 3 in every shape); a stance whose kit parity is outside 50 +/- 5%;
  stalemates; calibration misses.
- **Battle loop.** The PvE loop reproduces `BattleTurnExecutor.RunBattle` statement for statement,
  with an HP snapshot around each `ExecuteTurn` so damage can be attributed to the acting unit.
  `--self-check` replays sample battles (sample 0, same seed) through the real `RunBattle` and
  requires identical outcomes, battle time, turn counts, HP and positions — damage rolls included.
  The loop adds no rules of its own.

### Generated encounters

The user's direction is PvE with enemy sides from one giant to about two dozen small enemies, mixed
enemy types, and elements that vary across battles and sometimes within one enemy team. The default
encounter set simulates that.

**Enemy type pool** (`EnemyTypes`; max-level base stats, speeds in a narrow 95-105 band):

| Type | Role | Threat | Stance | Kit (targeting) |
| --- | --- | ---: | --- | --- |
| Giant | boss, HP 1600 | 12 | Vanguard | crush (Physical, r1) and gaze (Special, r3), power 70, nearest; quake + roar (Physical + Special AreaBurst, r2, power 35, cd 3) |
| Champion | mini-boss, HP 800 | 6 | Vanguard | cleave (Physical, r1, power 55, nearest); hex (Special, r2, power 55, cd 2, lowest current HP); shockwave (Special AreaBurst, r2, power 30, cd 3) |
| Brute | melee tank | 2.75 | Vanguard | smash (Physical, r1, power 50, nearest) |
| Stalker | fast melee hunter (Speed 105, Move 4, crit 10) | 2 | Skirmisher | shadow claw (Special, r1, power 55, lowest current HP) |
| Archer | ranged physical | 2 | Ranged | arrow (Physical, r3, power 42, nearest) |
| Caster | ranged special | 2 | Ranged | bolt (Special, r3, power 42, lowest current HP) |
| Shaman | area special | 2 | Vanguard | staff (Physical, r1, power 35, nearest); storm (Special AreaBurst, r2, power 28, cd 2) |
| Swarmling | swarm, HP 30 | 0.45 | Vanguard | bite (Physical, r1, power 30, nearest) |
| Stingling | swarm, HP 30 | 0.45 | Vanguard | sting (Special, r1, power 30, nearest) |

The shaman is a Vanguard: its storm is a disc around itself, so a Ranged shaman that keeps its
distance would rarely catch anyone. The swarm is two single-skill types rather than one type with a
physical and a special skill because, under the old level-term formula, every hit dealt at least its
+2 offset: two hits per swarm unit doubled the floor damage, and at level 1 the horde could not be
calibrated below 35% clear even at the minimum multiplier. (The current formula has no offset, only
the 1-damage floor.)

**Shapes** (`Shapes`):

| Shape | Arena | Recipe | Threat budget |
| --- | --- | --- | --- |
| `solo` | Medium | one giant | 12 |
| `elite` | Medium | a giant + 2 escorts (weight 2), or 2 champions + 1-2 escorts (weight 1); escorts from brute / stalker / archer / caster / shaman; at least 2 types | 14-17 |
| `squad` | Medium | 4-6 from brute / stalker / archer / caster / shaman, at least 3 types | 10-12 |
| `horde` | Large | 14-20 swarmlings / stinglings + 2-4 archers / casters, at least 3 types (16-24 enemies) | 12.5-14 |

**Generator rules** (`EncounterGenerator`, seeded from `--seed` alone):

- For each shape in file order, `--compositions` times: pick a variant by weight, draw each slot's
  count uniformly in [Min, Max] and each unit's type uniformly from the slot's types, and keep the
  draw only if its summed threat is inside the budget and it has at least `MinDistinctTypes` types
  (up to 2000 redraws; the first 200 also reject a repeat of an earlier composition of the shape).
  Every shape is generated whatever `--encounters` selects, so a shape's compositions do not depend
  on the filter.
- Each composition then draws an element scheme: **one element** for the whole team (30%), **one per
  type** (30%), **one per unit** (fully mixed, 25%) or **none** (15%). Elements are dealt from a
  shuffled deck of the ten elements, reshuffled when empty and shared by the whole generation, so
  every element is dealt before any is dealt twice; the default run deals far more than ten, so all
  ten always appear. The report lists every composition (types, counts, elements, scheme, dominant
  element, threat and clear rate per kit mode) and the element distribution.
- **Threat** is a hand-set weight per type, fitted to per-composition clear rates (a least-squares
  fit of logit clear rate on type counts, `neutral` mode, level 50, 24 compositions per shape) and
  rounded: brute 2.75 vs 2 for the other standard types, a swarm unit about a fifth of an archer or
  caster, two champions about a giant. The stalker (buffed to HP 125 and power 55) and champion
  (HP 800) were raised until they pulled their weight. The budget only keeps a shape's compositions
  comparably hard; the calibration still sets one multiplier per shape. Compositions still differ:
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
  giant, champion, brute, archer, caster and shaman, 4 for the stalker and the swarm. The loader only
  requires move >= 1 and at least one `SingleTarget` skill, the only shape that walks (of range 2 or
  more for a Ranged enemy).
- **Combat stances** (`CombatStance`, per species in `beast-roster.json` and per group in
  `encounters.json`). A Vanguard moves exactly as above, except that among equally short approaches
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
so one battle per team is a single draw. Every PvE team fights every composition, at every
calibration step, `--samples` times, and every PvP game is played `--samples` times, each with its
own seed (the sample index is part of the seed). With generated encounters the default is **1 sample
per team and composition**: the eight compositions already vary the fight, so each team fights each
shape 8 times per step (more than the fixed set's 5), and the variety is spent on different fights
rather than on re-rolling the same one. One evaluation is 1680 battles; a beast's metrics in one cell
rest on 672 battles with it (84 teams x 8), and its overall marginal on 8064. The fixed set keeps 5
samples, and PvP 5. The report's "Critical hits and damage rolls" table checks the plumbing: each
beast's observed crit rate and average roll multiplier against its authored chance.

**Noise.** Rerunning the default PvE run with `--seed 777` (which also draws different compositions,
so it measures composition sampling as well as roll noise) moves a beast's overall marginal by
2.0 points on average and at most 4.8 (`elemental`; 1.2 and 3.2 in `neutral`), and a single
shape cell by up to 12.6 (`elemental`, where the drawn elements matter most); see the tuning log. Raise `--compositions` or
`--samples` to shrink it (the run time grows in proportion).

## Determinism

Every battle gets its own `System.Random`, seeded from the base seed and the battle's inputs (never
from the calibration multiplier), and the sample index; the generated compositions come from their
own rng, seeded from the base seed alone. The kits target by distance, a stat or current HP, never
at random, so a battle's rng is consulted only for damage rolls: crit then variance, two draws per
damage effect that lands, in the Runtime's fixed order. Parallel results are stored by composition,
team and sample index and aggregated in a fixed order. The report
contains no timestamps, machine paths or timings, and always uses LF line endings. The same roster,
fixtures, code and arguments produce a byte-identical report.

## Design assumptions (and what they bias)

- **One kit for everyone.** Real beasts will have authored skills. A special attacker is no longer
  under-rated relative to a physical one, but no beast is played to its strengths either.
- **Simple targeting, stance-only tactics.** Beasts and most enemies hit the nearest enemy, so
  whoever is in front takes most of the hits; the stalker, the caster, the champion's hex (and, in
  the fixed set, the wisps and stingers) pick off the beast with the least current HP. Beyond the
  combat stances there is no AI: nobody retreats when hurt, spreads out against area skills or
  focuses fire deliberately. Those are Runtime rules, not simulator choices, but they colour every
  number.
- **Fixture enemies.** Their stat ratios, kits, threat weights and the generator's element schemes
  decide which beasts look good. The calibration removes overall difficulty, but not shape. The type
  pool is small and hand-made: which types exist, and how often each is drawn, is itself a bias.
- **Large creatures are one hex.** `HexGrid` has no multi-hex footprint, so the boss can be
  surrounded by six attackers. This is an open item in the design doc.
- **No gear, no avatar,** and only species base stats, the growth curve and the level.

All tunables (kit numbers, calibration bounds, flag thresholds, element-scheme weights, CLI
defaults) are constants at the top of `SimOptions.cs`; enemy types, shapes and the fixed encounters
are in `encounters.json`.

## Build and format

```sh
dotnet format Tooling/BalanceSim/BalanceSim.csproj --verify-no-changes
dotnet build  Tooling/BalanceSim/BalanceSim.csproj -c Release
```

It targets `net10.0` (the installed LTS SDK) and, like `CiLint`, compiles with C# 9 and nullable
disabled so the Unity scripts build unchanged. Only `Scripts/Runtime/**` is compiled: no Editor
scripts and no tests.
