# Beast Craft

*(working title)*

A narrative story-adventure RPG where the story comes first and the creatures you
collect, raise and battle carry you through it — painterly, Ghibli-inspired 2D art,
entirely original IP, built for phones.

---

## Repo layout

```
beast-craft/
├── src/
│   ├── BeastCraft.Core/    the game runtime: engine-neutral C# (netstandard2.1), no engine
│   │                       references (Battle, Bonds, Creatures, Avatar, Skills, Progression,
│   │                       Campaign, Economy, Encounters, Save, Session, Customization, Idle,
│   │                       Vfx data, Common)
│   ├── BeastCraft.Presentation/  engine-neutral presentation: content loading, hex layout,
│   │                       battle playback, VFX timeline/particles, pixel font (netstandard2.1)
│   ├── BeastCraft.Game/    the shared MonoGame battle viewer (drawing, input, scaling; net10.0)
│   ├── BeastCraft.Desktop/ the MonoGame DesktopGL host (desktop spike, net10.0)
│   └── BeastCraft.Android/ the MonoGame Android host (net10.0-android; local build, not in CI)
├── content/        the game's authored content, read by the hosts, the tests and the simulator
│   ├── data/               JSON sources of truth: Creatures/beast-roster.json,
│   │                       Skills/skill-library.json + drop-tables.json, Encounters/ (enemy and
│   │                       encounter libraries, the generated difficulty table), Campaign/,
│   │                       Items/, Economy/, Cosmetics/, Idle/, Vfx/vfx-library.json (skill VFX,
│   │                       presentation only)
│   └── art/pixel/          the generated placeholder pixel art (PNGs in Git LFS) + manifest
│                           (written by Tooling/PixelArt)
├── Pipeline/       OFFLINE, build-time-only asset generation. Never runs at runtime.
├── Tooling/        BalanceSim/: local-only headless balance simulator over the real
│                   battle code. EditModeTests/: the `dotnet test` runner and the test
│                   suite (Tests/, Goldens/, Presentation/; CI runs it). PixelArt/: text-grid
│                   sprites -> placeholder PNGs (Python + Pillow). Never shipped.
├── docs/           design/ and balance/ (simulator reports, tuning log, research) notes;
│                   architecture/ is an empty placeholder
└── .github/        CI workflows
```

Every data file is addressed by its repo-relative path (`ProjectRelativePath`,
e.g. `content/data/Creatures/beast-roster.json`). The hosts copy `content/` into
a `Content/` folder beside the executable (desktop) or into the APK's assets
(Android), keeping the same `data/` and `art/pixel/` layout.

---

## Stack

- **Engine:** [MonoGame](https://monogame.net/) 3.8.5 (NuGet packages; no content
  pipeline). The runtime (`src/BeastCraft.Core`) and the presentation layer
  (`src/BeastCraft.Presentation`) are engine-neutral C#; `src/BeastCraft.Game` is
  the shared MonoGame viewer, hosted by `src/BeastCraft.Desktop` (DesktopGL) and
  `src/BeastCraft.Android`.
- **Language:** C# (.NET SDK 10; the runtime targets netstandard2.1, C# 9)
- **Targets:** Android and iOS from one codebase (desktop for development)
- **Tests:** NUnit via `dotnet test` (`Tooling/EditModeTests`)
- **Play:** local only (saves on the device); no online backend

### No AI at runtime

Art and audio are generated **offline**, ahead of time, by the pipeline in
`Pipeline/`, then reviewed by a human and committed as static files. The shipped
build makes **zero AI calls** — it loads sprite sheets and audio clips like any
other game. See [`Pipeline/README.md`](Pipeline/README.md) for tooling, licensing
rules and the asset naming contract.

---

## Getting started

1. Install the **.NET SDK 10**. Nothing else is needed to build, test or run the
   desktop viewer.
2. `git lfs install && git lfs pull` — binary art and audio are tracked via Git
   LFS (see `.gitattributes`).
3. Build and test with the commands under **Building and testing** in
   [Status](#status), then see [Running the game](#running-the-game-desktop-spike)
   (desktop) or [Running on Android](#running-on-android).

---

## Status

**Pre-alpha: a headless, deterministic battle and progression core with its
data and tooling, and a MonoGame desktop spike that renders one real battle
with placeholder pixel art and skill VFX. No playable game yet.**

### What exists

**Runtime systems** (`src/BeastCraft.Core/`, engine-neutral C# with no engine
references):

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
- **Save system** (`Save/`, no UI yet) — a versioned `PlayerSave`
  aggregate (beasts, avatar, skill books, materials, beast and avatar gear),
  `SaveSerializer` with a migration chain and load-time validation that reports
  unknown ids instead of failing, storage behind the thin `ISaveStorage` seam:
  `FileSaveStorage` writes local slot files atomically with a one-generation
  backup that loads fall back to, rooted under the user's local app-data folder
  via `SaveLocations.Default()`.
- **Battle session** (`Session/`) — `BattleSession`, the single entry point a
  scene will call: builds a battle from a save plus an encounter setup, runs it
  (same setup and seed, same battle) and pays the rewards (skill practice XP,
  material drops, avatar and beast XP) back into the save.
- **Data schemas** — plain content classes (formerly ScriptableObjects, class
  names kept) for creature species (stats, growth
  curves, skill learn tables, evolution requirements), skills, passives, team
  bonds, materials, drop tables, beast gear, avatar stats and avatar stat gear,
  and the shared avatar + creature cosmetic customization framework.

See the [battle-system design doc](docs/design/battle-system.md) and
[`docs/design/progression-and-saves.md`](docs/design/progression-and-saves.md).

**Authored data** (`content/data/`; the JSON is the source of truth, loaded
through the game's own validators and builders), for example:

- `content/data/Creatures/beast-roster.json` — ten starter beasts, one per element,
  with stances and three growth curves (all ten currently use `medium`; `fast`
  and `slow` are kept for the simulator).
- `content/data/Skills/skill-library.json` — 60 beast skills (six per beast) with each
  species' learnable skills and 3-skill default loadout, 6 avatar active
  skills, 10 avatar passives, 3 skill-training materials and 11 team bonds.
- `content/data/Skills/drop-tables.json` — material drops by encounter shape x level
  band, with pity thresholds.

The numbers are simulator-tuned starting points, not confirmed balance — see
[`docs/balance/tuning-log.md`](docs/balance/tuning-log.md).

**Tooling:**

- **Building and testing** — plain .NET (SDK 10); no engine install needed.
  From the repo root, exactly what CI (`.github/workflows/ci.yml`) runs:

  ```sh
  dotnet format src/BeastCraft.Core/BeastCraft.Core.csproj --verify-no-changes
  dotnet build  src/BeastCraft.Core/BeastCraft.Core.csproj --configuration Release
  dotnet format src/BeastCraft.Presentation/BeastCraft.Presentation.csproj --verify-no-changes
  dotnet format src/BeastCraft.Game/BeastCraft.Game.csproj --verify-no-changes
  dotnet format src/BeastCraft.Desktop/BeastCraft.Desktop.csproj --verify-no-changes
  dotnet build  src/BeastCraft.Desktop/BeastCraft.Desktop.csproj --configuration Release
  dotnet format Tooling/EditModeTests --verify-no-changes
  dotnet test   Tooling/EditModeTests --configuration Release
  ```
- A **headless balance simulator** ([`Tooling/BalanceSim/`](Tooling/BalanceSim/README.md))
  — local-only, not a CI job — that runs the real battle code headless and
  writes Markdown reports: PvE against generated mixed encounters (solo, elite,
  squad, horde; hand-authored ones via `--encounter-set fixed`), a secondary
  1v1 PvP round-robin, a `--level-gap` sweep and `--mode pacing`. Committed
  reports live in [`docs/balance/`](docs/balance/) (baseline, tuned, level-gap
  and pacing reports plus research notes); their numbers inform design
  decisions and are not applied automatically.
- The **test runner** (`Tooling/EditModeTests/`) references
  `src/BeastCraft.Core` and runs the suite in `Tooling/EditModeTests/Tests`
  plus its own golden-value tests (`Tooling/EditModeTests/Goldens`: growth-curve
  samples and schema 1-5 save fixtures that must stay byte-identical;
  `BEASTCRAFT_UPDATE_GOLDENS=1` rewrites them for a reviewed behaviour change).
  CI runs it as a final gate; run it locally before pushing.

### What does not exist yet

- Anything the player sees or touches beyond the spike's battle viewer (desktop
  and Android): no menus, map, team building or audio; the spike's art is
  placeholder.
- Encounters as game data — the only encounters are the balance simulator's
  generator and its `Tooling/BalanceSim/encounters.json`.
- Gear content — the gear schemas and save support exist, but `content/data/Gear/` and
  `content/data/AvatarGear/` are empty.
- Narrative and IAP code (idle rewards have their rules in
  `src/BeastCraft.Core/Idle` but no UI), and the offline art compositor.
- Tests that drive the MonoGame viewer itself (the suite covers the core and
  the engine-neutral presentation layer).

### Running the game (desktop spike)

`src/BeastCraft.Desktop` is a MonoGame DesktopGL app (MonoGame 3.8.5.1, net10.0;
Windows first, and it builds on Linux). It fights one real PvE battle through the
game's own session code — Phoenix, Golem and Kirin (level 20) against the Hollow
Warden encounter (a champion and two brutes, level 10), seed 20260924 — and
draws it pixel-perfect: a 640x360 frame scaled up by a whole number.

```
git lfs install && git lfs pull          # the pixel art is in Git LFS
dotnet run --project src/BeastCraft.Desktop -c Release
```

**Space** plays the next turn (or finishes the one playing), **A** toggles
auto-play, **Esc** quits. Each fired skill plays its VFX from
`content/data/Vfx/vfx-library.json`; Phoenix's Ember Shot and Flame Wave are fully
authored, every other skill uses its element's default.

Screenshot mode renders one frame to a PNG and exits (it still opens a window
briefly, for the graphics device):

```
dotnet run --project src/BeastCraft.Desktop -c Release -- --screenshot shot.png --turns 1 --skill ember_shot
```

`--turns N` plays N turns and shows the Nth; `--skill ID` then carries on to the
first turn that fires that skill; `--at MS` picks the moment inside that turn
(default: the skill's VFX mid-play); `--scale K` (default 2), `--seed S`,
`--level L`, `--enemy-level L` and `--content DIR` adjust the rest. The design,
the VFX schema and the art pipeline are in
[`docs/design/presentation-and-vfx.md`](docs/design/presentation-and-vfx.md);
regenerating the art is in [`Tooling/PixelArt/README.md`](Tooling/PixelArt/README.md).

### Running on Android

`src/BeastCraft.Android` runs the same battle viewer (the shared
`src/BeastCraft.Game`) on Android: full screen in landscape, the 640x360 frame
scaled by a whole number and letterboxed. **Tap** plays the next turn (or
finishes the one playing), a **two-finger tap** or the on-screen **AUTO**
button toggles auto-play, **Back** quits. It is a **local build only**; CI does
not build it.
The package ID `com.example.beastcraft` is a temporary placeholder; the real
package ID is decided at release time.

Prerequisites (all user-level, no admin):

- .NET SDK 10 with the Android workload (`dotnet workload install android`;
  the `maui-android` workload includes it).
- A JDK 17, e.g. Microsoft OpenJDK 17, with `JAVA_HOME` pointing at it.
- The Android SDK with `platform-tools`, `platforms;android-36` and
  `build-tools;36.0.0` (Google's command-line tools; `sdkmanager`, or the newer
  `android sdk install platforms/android-36 build-tools/36.0.0 platform-tools`),
  with `ANDROID_HOME` pointing at it (e.g. `%LOCALAPPDATA%\Android\Sdk`).
- The pixel art from Git LFS (`git lfs pull`), as for desktop.

Build a debug APK (self-contained: the assemblies are embedded, so a plain
`adb install` works):

```
dotnet build src/BeastCraft.Android -c Debug
# -> src/BeastCraft.Android/bin/Debug/net10.0-android/com.example.beastcraft-Signed.apk
```

Run it on a phone: enable **Developer options** (tap *Build number* seven
times) and **USB debugging**, connect it over USB, accept the debugging prompt,
then:

```
adb devices                                  # the phone should be listed as "device"
adb install -r src/BeastCraft.Android/bin/Debug/net10.0-android/com.example.beastcraft-Signed.apk
adb shell monkey -p com.example.beastcraft -c android.intent.category.LAUNCHER 1
adb logcat -d | grep -iE "FATAL|monodroid"   # if it does not start
```

(`dotnet build src/BeastCraft.Android -c Debug -t:Run` builds, installs and
launches in one step.) An emulator works too (an x86_64 system image with
hardware acceleration); `adb exec-out screencap -p > shot.png` takes a
screenshot. The Android notes (content as APK assets, input, screens) and the
iOS plan are in
[`docs/design/presentation-and-vfx.md`](docs/design/presentation-and-vfx.md#hosts).
