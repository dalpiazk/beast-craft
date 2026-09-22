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
│   │   ├── Data/           ScriptableObject .asset instances (creatures, skills, gear, customization)
│   │   ├── Prefabs/
│   │   ├── Scenes/
│   │   └── Scripts/        Runtime/ (Core, Services, Narrative, Battle, Creatures,
│   │                       Avatar, Customization, Idle, IAP), Editor/, Tests/
│   ├── Packages/           package manifest
│   └── ProjectSettings/    editor version pin; Unity fills in the rest on first open
├── Pipeline/       OFFLINE, build-time-only asset generation. Never runs at runtime.
├── docs/           design/ and architecture/ notes
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

**Early scaffolding.** This repo currently contains the directory skeleton,
Unity project stub (version pin + package manifest), git/LFS configuration, and
the offline asset-pipeline structure and conventions. There is no gameplay code,
no ScriptableObject schemas, no scenes and no CI workflow yet — those land in
later steps. The Unity project has never been opened by the Editor, so the
generated `ProjectSettings/` YAML, `Library/` and solution files do not exist
yet; that is expected.
