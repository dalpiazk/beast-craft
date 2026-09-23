# Tooling/BalanceSim

A headless balance simulator for Beast Craft. It runs the **real** battle code (the game's `Runtime`
scripts, compiled outside Unity against the committed `Tooling/CiStubs/UnityStub`) and prints a
Markdown report of how much each beast in the starter roster helps a team.

- **Primary mode, PvE (team vs encounter).** The game is expected to be PvE (see "Encounter
  direction" in `docs/design/battle-system.md`), so every team of beasts fights synthetic enemy
  encounters (one huge boss, a swarm of 24, a mixed pack). Each beast is scored by how much it moves
  its team's clear rate.
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
| `--kit <k>` | `both` | `elemental`, `neutral` or `both` (see below). |
| `--levels <list>` | `1,50,100` | Comma-separated levels; beasts and enemies fight at the same level. |
| `--encounters <list>` | all | Comma-separated encounter ids from `encounters.json` (`boss`, `swarm`, `pack`). |
| `--team-size <n>` | `4` | Beasts per PvE team, 1-6. Every combination of the roster is fielded (C(10,4) = 210). |
| `--target-clear <pct>` | `50` | Clear rate the difficulty calibration aims for. |
| `--marginal-threshold <x>` | `5` | Flag a beast whose overall marginal clear rate is outside +/-x points. |
| `--enemy-element <e>` | `authored` | `authored`, `None` or an element name: override every enemy's element. |
| `--max-time <n>` | `2000` | Battle-time cap (normalized, see below); a battle that reaches it is a stalemate. |
| `--seed <n>` | `12345` | Base seed; each battle derives its own seed from it. |
| `--matrix-level <n>` | `50` | Level of the PvP win matrix and the stat table (falls back to the highest simulated level). |
| `--roster <path>` | found by walking up | Path to `beast-roster.json`. |
| `--encounters-file <path>` | found by walking up | Path to `encounters.json`. |
| `--out <path>` | none | Also write the report to this file (it always goes to stdout). |
| `--self-check` | off | Run everything twice and fail unless both reports are identical; also replay sample PvE battles through `BattleTurnExecutor.RunBattle` and fail if the simulator's loop disagrees. |

Exit codes: `0` success, `1` bad arguments, `2` missing or invalid roster or encounters (the roster
is checked with `BeastRosterValidator` first, exactly as the Editor importer does), `3` a self-check
failed. The run time goes to stderr, never into the report. The default run (both modes, both kits,
three levels, three encounters) takes about 15 s on an 8-thread machine (about 35 s with
`--self-check`). PvE battles run in parallel, and the output is identical whatever the thread count.

Two reports are committed, both the default arguments:

- `docs/balance/baseline-report.md` — the "before" picture, on the roster's first-draft stats. It is
  kept as a record and is **not** regenerated (a fresh run now reads the tuned roster). It predates
  the ATB turn order, so its battle lengths are in rounds.
- `docs/balance/tuned-report.md` — the current roster after the first tuning pass (see
  `docs/balance/tuning-log.md`), under the current Runtime (the ATB turn order and combat stances;
  the roster has not been re-tuned for either). Regenerate it
  whenever the roster, fixtures, simulator or Runtime change:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --out docs/balance/tuned-report.md
```

## The standard kit

No skills are authored yet, so every beast fights with the same kit, and its stat line is what gets
measured. Fire priority is Blast, Strike, then Burst. Every beast skill aims at the nearest enemy. How
the kit is used depends on the beast's combat stance (see "Runtime rules" below): a Ranged beast
never walks in for Strike, so it only fires Strike at an enemy that is already adjacent.

| Skill | Category | Shape | Range | Power | Cooldown |
| --- | --- | --- | ---: | ---: | ---: |
| Blast | Special | SingleTarget | 3 | 40 | 1 |
| Strike | Physical | SingleTarget | 1 | 55 | 1 |
| Burst, physical half | Physical | AreaBurst, radius 2 around the caster | 2 | 20 | 2 |
| Burst, special half | Special | AreaBurst, radius 2 around the caster | 2 | 20 | 2 |

The goal is that `Attack` and `SpecialAttack` (and `Defense` / `SpecialDefense`) carry equal weight.
The first baseline did not manage that: Strike at cooldown 1 against Blast at cooldown 2 weighted
`Attack` about twice as heavily.

- **Same cooldown.** Strike and Blast both fire every turn once the beast is in melee.
- **Power offsets range.** Strike needs a free tile next to its target, so it fires less often than
  Blast: while the beast is still closing, and when the target is crowded. With equal power, Strike
  fired 0.73x as often as Blast across the default PvE run (0.70 boss, 0.75-0.77 swarm, 0.72-0.76
  pack), so Strike's power is 40 / 0.731 = 55 (rounded). The report's **kit parity** table checks
  the result: the physical share of single-target power delivered is 49-51% in every encounter and
  50.1% overall (54 gave 49.7%). This was re-derived when the Runtime gained its defeated-unit and
  partial-approach rules and the fixtures' move ranges came down (it was 54 before). Re-derive
  `StrikePower` again if the kit, the fixtures or the movement rules change. **Combat stances broke
  it and it has not been re-derived yet:** with three Ranged beasts that never walk in for Strike,
  the physical share is now 37-47% per encounter (see the tuning log's "After combat stances").
- **Burst is split.** It is two skills, a physical and a special half, with the same power, radius
  and cooldown, fired together. A single-category area skill would reintroduce the bias. The
  physical half fires first, but that does not bias outcomes: a target dies iff the two halves
  together deal its HP, whichever one lands the blow. Burst has lower power and a longer cooldown
  than the single-target pair, so it only pays off when several enemies are close (swarms, packs).
  Cooldown 2 brings it up on the beast's second turn; at cooldown 3 the swarm was mostly dead before
  it fired (measured under the old round-based turn order).
  `AreaBurst` is a disc around the caster's own tile and never moves the caster, so Burst sits last
  in the fire order and goes off from the tile Strike just walked the beast to.
- **Kit modes.** `elemental` gives every kit skill the beast's first element; `neutral` makes them
  `Element.None`, which isolates the stat lines from the element chart.

## PvE: team vs encounter

- **Teams.** Every combination of `--team-size` distinct beasts (210 teams of 4, format
  `SmallGroup`; each beast is in 84). No gear, no avatar.
- **Encounters.** `encounters.json` beside this file. **These are simulator fixtures, not game
  content:** synthetic enemies that are not roster beasts, and no game code reads them. Each enemy
  becomes an in-memory `CreatureSpeciesSO` on the roster's `medium` growth curve, so it is built by
  the same `BattleUnitFactory.CreateBeast` and scales with level the same way as a beast. Elements
  are listed per group and cycled over its units. Enemy kits are authored in the file and are
  `Element.None` in `neutral` mode.

  | Id | Arena | Enemies |
  | --- | --- | --- |
  | `boss` | Medium | 1 Colossus (Vanguard): HP 1600 base, Physical and Special hits of power 70 every turn (ranges 1 and 3) at the nearest beast, and a physical plus special area slam (radius 2, power 35 each, cooldown 3). Move 3. Elementless by default. |
  | `swarm` | Large | 12 Biters (Vanguard, Physical bite at the nearest beast) + 12 Stingers (Skirmisher, Special sting at the lowest-HP beast): HP 30, weak range-1 attacks of power 30. Move 4. Elements cycle through all ten. |
  | `pack` | Medium | 3 Direwolves (Vanguard, Physical melee, power 45, nearest beast) in front, 3 Wisps (Ranged, Special, range 3, power 45, lowest-HP beast) behind. Move 4 / 3. Six different elements. |

  Each group has a `Stance` (a `CombatStance` name; missing = `Vanguard`), and each skill an optional
  `Targeting` (`Distance`, the default, or `Stat`; `Random` is refused so battles never draw targets
  from the rng), `TargetingOrder` (default `Lowest`) and `TargetingStat` (default `HP`). "Lowest HP"
  compares the stat block, i.e. **maximum** HP, not current HP (`SkillTargetResolver`'s `Stat`
  criterion). The stingers are Skirmishers rather than Ranged because their sting is range 1: a
  Ranged unit never walks into melee, so a Ranged stinger would never engage, and the loader rejects
  a Ranged group without a `SingleTarget` skill of range 2 or more. The boss stays a nearest-target
  Vanguard.

  Physical and special pressure is balanced within each encounter, so neither `Defense` nor
  `SpecialDefense` is favoured.
- **Placement.** Each side takes the front-most tiles of its own deployment zone (front row first,
  then outward from the centre line). Enemies are placed in fixture order; the team is committed
  through `PlacementValidator.TryPlaceAll`. Which member gets which slot is a fixed seeded shuffle
  per team, because the slot fixes the unit id, and ids break initiative ties and equal-distance target
  ties within the team. Pinning slots to roster order would always expose the first species.
  (Initiative ties *between* the sides are split separately; see below.) Placing all 24 swarm enemies is
  checked on every battle, and the loader rejects an encounter that does not fit its zone.
- **Difficulty calibration.** One multiplier per (kit mode, encounter, level) scales every enemy's
  HP, Atk, Def, SpA and SpD. Speed and Move stay unscaled: Speed is how many turns a unit gets, so
  scaling it would change the enemies' action economy, not just their toughness. The multiplier starts at 1 and doubles or halves until the target clear rate is
  bracketed (between 1/64 and 64), then bisects 8 times. The evaluated multiplier closest to the
  target wins (first evaluated on a tie). The process is deterministic because each clear rate is.
  Where a step in the clear-rate curve cannot be split (for example level 1, where enemy stats
  round to 1-2), the closest rate is reported; a miss beyond 10 points is flagged.
- **Metrics**, all at the calibrated multiplier:
  - **Marginal clear rate**, the primary number: the clear rate of teams containing the beast minus
    teams without it, in points.
  - **Damage share / taken share**: the beast's share of its team's damage dealt and taken (HP
    actually removed, so overkill is not counted).
  - **Survival** (standing at the end), **time to clear** (normalized time of the clears it was
    in) and **turns / time** (its turns per unit of time over its battles: Speed / 100 while it
    stands).
  - Per encounter, per level, and overall (every encounter and level weighted equally), plus a
    per-encounter ranking that shows niches.
- **Flags.** Overall marginal outside +/-5 points; **no niche** (bottom 3 in every encounter);
  **no weakness** (top 3 in every encounter); stalemates; calibration misses.
- **Battle loop.** The PvE loop reproduces `BattleTurnExecutor.RunBattle` statement for statement,
  with an HP snapshot around each `ExecuteTurn` so damage can be attributed to the acting unit.
  `--self-check` replays sample battles through the real `RunBattle` and requires identical
  outcomes, battle time, turn counts, HP and positions. The loop adds no rules of its own.

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
  (boss 7, direwolves 9, wisps 7, swarm 13). They now move like beasts (roster band 2-5): boss 3,
  direwolves 4, wisps 3, biters and stingers 4. The loader only requires move >= 1 and at least one
  `SingleTarget` skill, the only shape that walks (of range 2 or more for a Ranged group).
- **Combat stances** (`CombatStance`, per species in `beast-roster.json` and per group in
  `encounters.json`). A Vanguard moves exactly as above, except that among equally short approaches
  it stops nearest its closest Ranged or Skirmisher ally (screening it). A Ranged unit never walks
  into melee (a range-1 slot fires only at an adjacent enemy, otherwise it holds without moving).
  Ranged and Skirmisher units prefer stop tiles with fewer adjacent enemies and spend whatever
  movement is left after their skills backing away from the nearest enemy, but never beyond their
  longest single-target range. Roster stances: Ranged = Phoenix, Kirin, Basilisk; Skirmisher =
  Thunderbird, Griffin; the rest Vanguard.

The default run has no stalemates, PvE or PvP.

- **Turn order is the Runtime's ATB gauge.** `TurnManager` fills every unit's gauge by its Speed
  and hands a turn to whoever reaches 1000, overflow carried, so twice the Speed is twice the turns.
  There are no rounds: battle length is **normalized time**, 1.0 = one turn of a Speed-100 unit.
  Speed scales with level, so the same fight takes longer at level 1 than at level 100; compare
  times within a level. The cap (`--max-time`, default 2000, `BattleTurnExecutor.DefaultMaxTime`)
  is a hang guard, not a game rule.
- **Initiative ties between the sides are split evenly.** `TurnManager` breaks a tie between equally
  full, equally fast gauges on the ordinal unit id (units of equal Speed fill in lockstep, so that is
  every turn they share), and with raw ids (`e01`... against `p1`...) every cross-side tie went to the
  enemies. Each battle now prefixes one side's ids with `a` and the other's with `b`; in every
  (kit mode, encounter, level) cell a seeded shuffle of the team indices picks exactly half the teams
  to win ties (`PveSimulator.PlayersWinTies`). The prefix is side-wide, so the order within a side,
  and every targeting tie (targeting only ever compares units of one side), is unchanged.

## PvP: 1v1 round-robin (secondary)

Every pair of distinct species is played twice per level and kit mode with the sides swapped, on a
Medium board, one beast per side: the most central tile of the player zone against its point
mirror. Mirror matches are skipped. `TurnManager` breaks initiative ties on the ordinal unit id,
and the ids are fixed per side (`p` / `e`), so playing each pairing both ways gives each beast the
tie-break exactly once. Battle length is reported in normalized time and total turns. Stalemates and mutual defeats count as games but not wins.

## Determinism

Every battle gets its own `System.Random`, seeded from the base seed and the battle's inputs (never
from the calibration multiplier). The kits target by distance or by a stat, never at random, so the
rng is not consulted for targeting. Parallel results are stored by team index and aggregated in a fixed order. The report
contains no timestamps, machine paths or timings, and always uses LF line endings. The same roster,
fixtures, code and arguments produce a byte-identical report.

## Design assumptions (and what they bias)

- **One kit for everyone.** Real beasts will have authored skills. A special attacker is no longer
  under-rated relative to a physical one, but no beast is played to its strengths either.
- **Simple targeting, stance-only tactics.** Beasts, the boss, the biters and the direwolves hit the
  nearest enemy, so whoever is in front takes most of the hits; the wisps and stingers pick the
  lowest maximum HP, which on a roster where four beasts share HP 100 is a sharp threshold (see the
  tuning log). Beyond the combat stances there is no AI: nobody retreats when hurt, spreads out
  against area skills or focuses fire deliberately. Those are Runtime rules, not simulator choices,
  but they colour every number.
- **Fixture enemies.** Their stat ratios, kits and elements decide which beasts look good. The
  calibration removes overall difficulty, but not shape. The boss is elementless by default, so its
  `elemental` and `neutral` columns are identical; use `--enemy-element` for elemental variants.
- **Large creatures are one hex.** `HexGrid` has no multi-hex footprint, so the boss can be
  surrounded by six attackers. This is an open item in the design doc.
- **No gear, no avatar,** and only species base stats, the growth curve and the level.

All tunables (kit numbers, calibration bounds, flag thresholds, CLI defaults) are constants at the
top of `SimOptions.cs`; enemy definitions are in `encounters.json`.

## Build and format

```sh
dotnet format Tooling/BalanceSim/BalanceSim.csproj --verify-no-changes
dotnet build  Tooling/BalanceSim/BalanceSim.csproj -c Release
```

It targets `net10.0` (the installed LTS SDK) and, like `CiLint`, compiles with C# 9 and nullable
disabled so the Unity scripts build unchanged. Only `Scripts/Runtime/**` is compiled: no Editor
scripts and no tests.
