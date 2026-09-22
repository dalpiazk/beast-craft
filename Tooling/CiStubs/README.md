# Tooling/CiStubs

A compile-and-format harness that lets GitHub Actions catch real C# errors in the Beast Craft Unity
scripts **without a Unity Editor install or a Unity licence**.

## What is here

| Project | Purpose |
| --- | --- |
| `UnityStub/` | A tiny fake `UnityEngine` assembly — just enough API surface (`Object`, `ScriptableObject`, `Sprite`, `Color`, `Mathf`, `AnimationCurve`, `Debug`, and the inspector attributes) for the game scripts to compile. |
| `CiLint/` | A source-less project that globs in `BeastCraft/Assets/_Project/Scripts/{Runtime,Editor}/**/*.cs` and compiles them against `UnityStub`. This is the project CI builds and formats. |

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

There are currently no `UnityEditor` stubs because no Editor scripts exist yet. Add a
`UnityEditorStub` (or a second namespace inside `UnityStub`) when the first one lands.

## Future: real Unity tests

Once a `UNITY_LICENSE` secret is provisioned for this repo, this stub-based check can be replaced
by — or, better, supplemented with — GameCI's real Unity Test Runner action, which runs the EditMode
and PlayMode suites in an actual Editor. Until then, this is the only automated C# validation in the
pipeline.

Adding that job is subject to this repo's CI-budget discipline: CI is a final gate, not a test loop,
and a Unity Editor job is dramatically more expensive in Actions minutes than this one.
