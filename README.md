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
│   │   ├── Data/           authored data: Creatures/beast-roster.json (source of truth) + generated .asset instances
│   │   ├── Prefabs/
│   │   ├── Scenes/
│   │   └── Scripts/        Runtime/ (Core, Services, Narrative, Battle, Creatures,
│   │                       Avatar, Customization, Idle, IAP), Editor/, Tests/
│   ├── Packages/           package manifest
│   └── ProjectSettings/    editor version pin; Unity fills in the rest on first open
├── Pipeline/       OFFLINE, build-time-only asset generation. Never runs at runtime.
├── Tooling/        CiStubs/: hand-written UnityEngine stub + csproj so CI compiles
│                   the game scripts without a Unity install. BalanceSim/: local-only
│                   headless balance simulator over the real battle code. Never shipped.
├── docs/           design/, architecture/ and balance/ (simulator reports) notes
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

**Early scaffolding — data and tooling, no gameplay yet.**

What exists today:

- The directory skeleton, Unity project stub (version pin + package manifest),
  assembly definitions, git/LFS configuration, and the offline asset-pipeline
  structure and conventions.
- **ScriptableObject data schemas** under
  `BeastCraft/Assets/_Project/Scripts/Runtime/` — creature species (stats,
  growth curves, skill learn tables, evolution requirements), skills, beast gear,
  avatar stats and avatar stat gear (never rendered; separate from the purely
  cosmetic customization), and the shared avatar + creature customization
  framework.
- **Starter roster data** — ten beasts, one per element, plus three growth curves
  (all ten beasts currently share `medium`; `fast` and `slow` are kept for the
  balance simulator), in
  `BeastCraft/Assets/_Project/Data/Creatures/beast-roster.json`. The JSON is the
  source of truth (readable outside Unity, e.g. by the headless balance
  simulator); the Unity assets are generated from it by opening the project and
  running **Beast Craft → Data → Import Beast Roster**, which creates or updates
  them in place by id. No `.asset` files are committed yet. The numbers are a
  first simulator-tuned pass, not confirmed balance — see "Starter roster" in the
  [battle-system design doc](docs/design/battle-system.md) and
  [`docs/balance/tuning-log.md`](docs/balance/tuning-log.md).
- **CI** (`.github/workflows/ci.yml`) — a format and compile check that builds
  the real game scripts against the hand-written UnityEngine stub in
  `Tooling/CiStubs/`. It needs no Unity install and runs no Unity tests, so it
  proves the scripts parse, type-check and are formatted — nothing about
  whether the project opens or behaves correctly.
- A **headless balance simulator** ([`Tooling/BalanceSim/`](Tooling/BalanceSim/README.md))
  — local-only, not a CI job — that runs the real battle code outside Unity
  and writes a Markdown report. Its primary mode is PvE: every 4-beast team of
  the starter roster fights boss, swarm and pack encounters at calibrated
  difficulty; a 1v1 round-robin is kept as a secondary PvP section. The
  committed reports are [`docs/balance/baseline-report.md`](docs/balance/baseline-report.md)
  (first-draft stats, the "before") and [`docs/balance/tuned-report.md`](docs/balance/tuned-report.md)
  (the tuned roster); their numbers are inputs to design decisions, not applied automatically.
- A **battle-system design proposal** ([`docs/design/battle-system.md`](docs/design/battle-system.md))
  whose open questions are still awaiting producer confirmation.

What does not exist yet: any gameplay or runtime behaviour code — no
compositor, no battle, narrative or idle systems — no scenes, no prefabs, and
no UGS integration. The Unity project has also never been opened by an actual
Editor, so the generated `ProjectSettings/` YAML, `Library/` and solution files
do not exist yet; that is expected.
