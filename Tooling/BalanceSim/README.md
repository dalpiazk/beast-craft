# Tooling/BalanceSim

A headless balance simulator for Beast Craft. It runs the **real** battle code — the game's
`Runtime` scripts, compiled outside Unity against the committed `Tooling/CiStubs/UnityStub` — over a
round-robin of the starter roster, and prints a Markdown report of win rates, battle lengths and
outliers.

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
| `--levels <list>` | `1,25,50,100` | Comma-separated beast levels to simulate. |
| `--mode <m>` | `both` | `elemental`, `neutral` or `both` (see below). |
| `--max-rounds <n>` | `200` | Round cap; a battle that reaches it is counted as a stalemate. |
| `--seed <n>` | `12345` | Base seed; each battle derives its own seed from it. |
| `--matrix-level <n>` | `50` | Level the win matrix is drawn at (falls back to the highest level if 50 is not simulated). |
| `--roster <path>` | found by walking up | Path to `beast-roster.json`. |
| `--out <path>` | none | Also write the report to this file (it always goes to stdout). |
| `--self-check` | off | Run the whole simulation twice and fail unless both reports are identical. |

Exit codes: `0` success, `1` bad arguments, `2` missing or invalid roster (it is checked with
`BeastRosterValidator` first, exactly as the Editor importer does), `3` a self-check failed. Every run
checks that each beast played the same number of games; `--self-check` adds the determinism check.

The committed baseline, `docs/balance/baseline-report.md`, is the default arguments:

```sh
dotnet run --project Tooling/BalanceSim -c Release -- --out docs/balance/baseline-report.md
```

## How it works

- **Roster.** `beast-roster.json` is read with `System.Text.Json` (`IncludeFields = true`) —
  `JsonUtility` does not exist outside Unity — then validated, and the growth curves and species are
  rebuilt in memory with the same field mapping as `BeastRosterImporter`.
- **Battles.** Each battle is a real `HexGrid` (the `Medium` preset), one beast per side, built with
  `BattleUnitFactory.CreateBeast` and run by `BattleTurnExecutor.RunBattle` with no avatar. The player
  starts on the most central tile of its deployment zone and the enemy on the point mirror of it, so
  neither side starts closer.
- **Round-robin.** Every pair of distinct species is played twice at every level and mode, with the
  sides swapped. Mirror matches are skipped.
- **Determinism.** Every battle gets its own `System.Random`, seeded from the base seed and the
  battle's inputs; the kit targets by distance, not at random; the report contains no timestamps or
  machine paths and always uses LF line endings. The same roster, code and arguments produce a
  byte-identical report.

## Design assumptions (and what they bias)

- **Standard kit.** The roster has no authored skills yet, so every beast fights with the same two:
  **Blast** (Special, power 40, range 3, cooldown 2) and **Strike** (Physical, power 40, range 1,
  cooldown 1), fired in that priority order, single target, nearest enemy. Strike comes up twice as
  often as Blast, so `Attack` is weighted roughly twice as heavily as `SpecialAttack`; a special
  attacker is under-rated relative to how it would play with a kit built for it.
- **Two modes.** `elemental` gives both skills the beast's first element; `neutral` makes them
  `Element.None`, which isolates the stat distribution from the element chart. The gap between the two
  is what the chart contributes.
- **No gear, no avatar, 1v1.** Only species base stats, the growth curve and the level. Party play,
  focus fire and avatar support are not modelled, and they change which stat lines are valuable.
- **Speed-tie mirroring.** `TurnManager` breaks speed ties on the ordinal unit id. The unit ids are
  fixed per side (`p` / `e`), so playing each pairing with the sides swapped gives each beast the
  tie-break exactly once instead of biasing the result.
- **Stalemates** are battles that hit `--max-rounds`; they count as games but not wins, and are
  listed separately. Mutual defeats are counted the same way.

All tunables (kit numbers, arena, unit ids, flag thresholds, CLI defaults) are constants at the top
of `SimOptions.cs`.

## Build and format

```sh
dotnet format Tooling/BalanceSim/BalanceSim.csproj --verify-no-changes
dotnet build  Tooling/BalanceSim/BalanceSim.csproj -c Release
```

It targets `net10.0` (the installed LTS SDK) and, like `CiLint`, compiles with C# 9 and nullable
disabled so the Unity scripts build unchanged. Only `Scripts/Runtime/**` is compiled — no Editor
scripts and no tests.
