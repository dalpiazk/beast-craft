# Tooling/CiStubs

A compile-and-format harness that lets GitHub Actions catch real C# errors in the Beast Craft Unity
scripts **without a Unity Editor install or a Unity licence**.

## What is here

| Project | Purpose |
| --- | --- |
| `UnityStub/` | A tiny fake `UnityEngine` assembly — just enough API surface (`Object`, `ScriptableObject`, `Sprite`, `Color`, `Mathf`, `AnimationCurve`, `JsonUtility`, `Debug`, `Application.persistentDataPath` (a real temp directory), the inspector attributes, and a few `UnityEditor` types) for the game scripts to compile. |
| `CiLint/` | A source-less project that globs in `BeastCraft/Assets/_Project/Scripts/{Runtime,Editor}/**/*.cs` and compiles them against `UnityStub`. This is the project CI builds and formats. |

`UnityStub` has one other consumer: `Tooling/BalanceSim/`, the local-only headless balance simulator,
which compiles the `Runtime` scripts against it and actually *executes* them. That is why the rule
below about genuine behaviour matters beyond CI — `AnimationCurve.Evaluate`, `Mathf` and
`ScriptableObject.CreateInstance` are run for real by the simulator. It is not a CI job; see its own
README.

`UnityStub` also has a third, local-only consumer: `Tooling/EditModeTests/`, which runs the
`Tests/EditMode` suite with NUnit outside Unity (also not a CI job):

```sh
dotnet test Tooling/EditModeTests
dotnet format Tooling/EditModeTests --verify-no-changes
```

It compiles the stub's *source files* directly into the test assembly (not via a
`ProjectReference`) with the `UNITYSTUB_SYSTEM_TEXT_JSON` symbol defined. Under that symbol
`JsonUtility.FromJson` / `ToJson` are real, implemented over System.Text.Json and restricted to
JsonUtility's rules (public instance fields only — no properties, readonly or `[NonSerialized]`
fields — exact, case-sensitive names, enums as numbers). Without the symbol, which is how CiLint and
BalanceSim build it, they keep throwing `NotSupportedException` as described below.
`Object.DestroyImmediate`, which the tests call in teardown, is an honest no-op in every build.

Neither project is part of the Unity project (they live entirely outside `BeastCraft/`), neither is
ever shipped, and game code must never reference them.

## Running it locally

From the repo root, exactly as CI does:

```sh
dotnet format Tooling/CiStubs/CiLint/CiLint.csproj --verify-no-changes --verbosity diagnostic
dotnet build  Tooling/CiStubs/CiLint/CiLint.csproj --configuration Release
```

## What this does and does not prove

It **does** prove: the scripts parse, type-check, and are formatted per the repo `.editorconfig`.

It **does not** prove: that the project opens in Unity, that assembly-definition references are
correct, that serialization behaves, that assets resolve, or that anything works at runtime. It is a
cheap first gate, not a substitute for opening the project in the Editor.

## Extending it

**Extend `UnityStub`, never replace it.** When a game script starts using a `UnityEngine` or
`UnityEditor` API that is not stubbed yet, the CI build fails with a normal "type or namespace not
found" error — add a minimal stub for that API and move on.

Two rules when adding surface:

1. Only add what the real scripts actually reference. Unused stub surface rots.
2. If the type has behaviour the game logic depends on (as `Color`'s HSV conversions and
   `AnimationCurve.Evaluate` do), implement genuine, correct math. A stub that throws or logs makes
   the compile check pass while quietly lying about semantics; that is worse than no stub.

`UnityStub/UnityEditor.cs` holds the `UnityEditor` surface (`MenuItem`, `AssetDatabase`,
`EditorUtility`) used by the roster importer, as a second namespace in the same assembly. Those
members, and `JsonUtility`, cannot be honestly implemented without an Editor (or, for JSON, a
package reference), and CI never executes them, so they throw `NotSupportedException` if called
rather than pretending to succeed (JsonUtility's one exception is the `UNITYSTUB_SYSTEM_TEXT_JSON`
build used by `Tooling/EditModeTests`, above). One consequence of sharing the assembly: CiLint would not flag a
Runtime script that wrongly uses `UnityEditor`; Unity's assembly definitions still do.

## Future: real Unity tests

Once a `UNITY_LICENSE` secret is provisioned for this repo, this stub-based check can be replaced
by — or, better, supplemented with — GameCI's real Unity Test Runner action, which runs the EditMode
and PlayMode suites in an actual Editor. Until then, this is the only automated C# validation in the
pipeline.

Adding that job is subject to this repo's CI-budget discipline: CI is a final gate, not a test loop,
and a Unity Editor job is dramatically more expensive in Actions minutes than this one.
