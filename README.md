# Beast Craft

*(working title)*

A narrative story-adventure RPG where the story comes first and the creatures you
collect, raise and battle carry you through it — painterly, Ghibli-inspired 2D art,
entirely original IP, built for phones.

---

## Repo layout

```
beast-craft/
├── BeastCraft/     the Unity project (open THIS folder in Unity Hub, not the repo root)
│   ├── Assets/_Project/    all first-party content, namespaced under _Project/
│   │   ├── Art/            sprites, backdrops, UI, key art (Characters/Creatures/Environments/UI/KeyArt)
│   │   ├── Audio/          Music/, Ambient/, SFX/
│   │   ├── Data/           authored data: Creatures/beast-roster.json, Skills/skill-library.json
│   │   │                   and Skills/drop-tables.json (sources of truth) + generated .asset instances
│   │   ├── Prefabs/
│   │   ├── Scenes/
│   │   └── Scripts/        Runtime/ (Battle, Bonds, Creatures, Avatar, Skills, Progression,
│   │                       Save, Session, Customization; Core, Services, Narrative, Idle
│   │                       and IAP are empty placeholders), Editor/, Tests/
│   ├── Packages/           package manifest
│   └── ProjectSettings/    editor version pin; Unity fills in the rest on first open
├── Pipeline/       OFFLINE, build-time-only asset generation. Never runs at runtime.
├── Tooling/        CiStubs/: hand-written UnityEngine stub + csproj so CI compiles
│                   the game scripts without a Unity install. BalanceSim/: local-only
│                   headless balance simulator over the real battle code. EditModeTests/:
│                   local-only `dotnet test` runner for the EditMode suite. Never shipped.
├── docs/           design/ and balance/ (simulator reports, tuning log, research) notes;
│                   architecture/ is an empty placeholder
└── .github/        CI workflows
```

Third-party/store assets go in `Assets/` outside `_Project/`, so the boundary
between "ours" and "imported" stays obvious in the Project window and in diffs.

---

## Stack

- **Engine:** Unity 6 LTS, URP with the **2D Renderer**
- **Language:** C#
- **Targets:** iOS and Android from one codebase
- **Input:** Unity Input System
- **Tests:** Unity Test Framework (EditMode + PlayMode)
- **Backend — Unity Gaming Services:**
  - Authentication — player identity, anonymous + platform sign-in
  - Cloud Save — save state, cross-device continuity
  - Economy — currencies, inventory, virtual purchases
  - Cloud Code — server-authoritative logic for anything exploitable client-side
  - Remote Config — tuning, feature flags, live events

### No AI at runtime

Art and audio are generated **offline**, ahead of time, by the pipeline in
`Pipeline/`, then reviewed by a human and committed as static files. The shipped
build makes **zero AI calls** — it loads sprite sheets and audio clips like any
other game. See [`Pipeline/README.md`](Pipeline/README.md) for tooling, licensing
rules and the asset naming contract.

---

## Getting started

1. Install **Unity 6 LTS** (any `6000.0.x` LTS patch) via Unity Hub, with the
   **iOS** and **Android** build support modules.
2. `git lfs install` — binary art and audio are tracked via Git LFS (see
   `.gitattributes`).
3. In Unity Hub, **Add project from disk** and select the **`BeastCraft/`**
   folder, not the repo root.
4. First open will take a while: Unity resolves packages from
   `BeastCraft/Packages/manifest.json` and generates `Library/`, the remaining
   `ProjectSettings/` files, and the `.sln`/`.csproj` files — all of which are
   gitignored and are *supposed* to be absent from a fresh clone.

> **Unity version caveat.** `BeastCraft/ProjectSettings/ProjectVersion.txt` is
> pinned to `6000.0.35f1` as a **placeholder**. That exact patch is not
> load-bearing — let Unity Hub auto-switch and re-resolve to whichever `6000.0`
> LTS patch you actually have installed, and commit the resulting
> `ProjectVersion.txt` change. Package versions in `manifest.json` are likewise
> plausible starting points that Package Manager will resolve and update on
> first open.

---

## Status

**Pre-alpha: a headless, deterministic battle and progression core with its
data and tooling. No playable build — nothing is rendered, and the Unity
project has never been opened in the Editor.**

### What exists

**Runtime systems** (`BeastCraft/Assets/_Project/Scripts/Runtime/`, pure C#
with no scene or MonoBehaviour dependencies):

- **Battle** (`Battle/`) — a deterministic, seeded grid auto-battle: an ATB
  turn order (square-root-of-Speed initiative gauge, integer maths), the damage
  formula (attack/defence ratio, element chart, crits, variance, level-difference
  modifier), statuses and stat modifiers, per-species combat stances, a hex grid
  with pathfinding and multi-hex unit footprints, deployment/placement
  validation, encounter scouting previews, skill targeting and effects, and
  avatar passives.
- **Team bonds** (`Bonds/`) — tiered team-composition bonuses (by stance or
  element coverage), resolved purely from the team.
- **Skills and progression** (`Skills/`, `Progression/`) — skill books with
  levels and level-5/10/15 breakthroughs, beast and avatar level/XP, material
  inventory, material drop tables and a loot roller with pity and first-clear
  grants.
- **Save system** (`Save/`, no UI or file IO yet) — a versioned `PlayerSave`
  aggregate (beasts, avatar, skill books, materials, beast and avatar gear),
  `SaveSerializer` with a migration chain and load-time validation that reports
  unknown ids instead of failing, storage behind the thin `ISaveStorage` seam.
- **Battle session** (`Session/`) — `BattleSession`, the single entry point a
  scene will call: builds a battle from a save plus an encounter setup, runs it
  (same setup and seed, same battle) and pays the rewards (skill practice XP,
  material drops, avatar and beast XP) back into the save.
- **Data schemas** — ScriptableObjects for creature species (stats, growth
  curves, skill learn tables, evolution requirements), skills, passives, team
  bonds, materials, drop tables, beast gear, avatar stats and avatar stat gear,
  and the shared avatar + creature cosmetic customization framework.

See the [battle-system design doc](docs/design/battle-system.md) and
[`docs/design/progression-and-saves.md`](docs/design/progression-and-saves.md).

**Authored data** (JSON is the source of truth, readable outside Unity; the
Unity `.asset` files are generated from it and none are committed yet):

- `Data/Creatures/beast-roster.json` — ten starter beasts, one per element,
  with stances and three growth curves (all ten currently use `medium`; `fast`
  and `slow` are kept for the simulator).
- `Data/Skills/skill-library.json` — 60 beast skills (six per beast) with each
  species' learnable skills and 3-skill default loadout, 6 avatar active
  skills, 10 avatar passives, 3 skill-training materials and 11 team bonds.
- `Data/Skills/drop-tables.json` — material drops by encounter shape x level
  band, with pity thresholds.

The numbers are simulator-tuned starting points, not confirmed balance — see
[`docs/balance/tuning-log.md`](docs/balance/tuning-log.md).

**Tooling:**

- **CI** (`.github/workflows/ci.yml`) — a format and compile check that builds
  the real game scripts against the hand-written UnityEngine stub in
  `Tooling/CiStubs/`. It needs no Unity install and runs no tests, so it proves
  the scripts parse, type-check and are formatted — nothing about whether the
  project opens or behaves correctly.
- A **headless balance simulator** ([`Tooling/BalanceSim/`](Tooling/BalanceSim/README.md))
  — local-only, not a CI job — that runs the real battle code outside Unity and
  writes Markdown reports: PvE against generated mixed encounters (solo, elite,
  squad, horde; hand-authored ones via `--encounter-set fixed`), a secondary
  1v1 PvP round-robin, a `--level-gap` sweep and `--mode pacing`. Committed
  reports live in [`docs/balance/`](docs/balance/) (baseline, tuned, level-gap
  and pacing reports plus research notes); their numbers inform design
  decisions and are not applied automatically.
- A **local EditMode test runner** (`Tooling/EditModeTests/`) — not a CI job —
  that compiles the Runtime, Editor and `Tests/EditMode` scripts against the
  UnityStub and runs the whole EditMode suite with NUnit, no Unity install
  needed. Run it from the repo root before pushing:

  ```sh
  dotnet test Tooling/EditModeTests
  ```

  It swaps the stub's compile-only `JsonUtility` for a System.Text.Json
  implementation that follows JsonUtility's field rules (see
  [`Tooling/CiStubs/README.md`](Tooling/CiStubs/README.md)); Unity's own Test
  Runner remains the authority on real serialization.

### What does not exist yet

- Anything the player sees or touches: no scenes, prefabs, UI, rendering,
  animation, audio playback or input handling (`Scenes/` and `Prefabs/` are
  empty, and no runtime script is a MonoBehaviour).
- Encounters as game data — the only encounters are the balance simulator's
  generator and its `Tooling/BalanceSim/encounters.json`.
- Gear content — the gear schemas and save support exist, but `Data/Gear/` and
  `Data/AvatarGear/` are empty.
- Narrative, idle, IAP and services code (those `Runtime/` folders are empty),
  the offline art compositor, and any UGS integration.
- PlayMode tests (the assembly exists, with no tests) and any Unity test run
  in CI.

### Running it in Unity

Nothing has been opened in an actual Unity Editor yet, so the generated
`ProjectSettings/` YAML, `Library/` and solution files do not exist; that is
expected. After the first open (see [Getting started](#getting-started)):

1. Run the importers in order from the **Beast Craft → Data** menu:
   **Import Beast Roster**, then **Import Skill Library** (wires each species'
   `LearnableSkills` and `DefaultLoadout`, and creates the team bonds), then
   **Import Drop Tables** (validated against the library's materials). Each
   creates or updates the assets in place by id, so re-running is safe.
2. Run the **EditMode** suite from **Window → General → Test Runner**. It has
   so far only been run through the stub-based `Tooling/EditModeTests` runner.
