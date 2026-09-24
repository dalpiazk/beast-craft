# Presentation and VFX (MonoGame desktop spike)

How the game is drawn: which code owns what, how a battle becomes pictures, the skill VFX
data, the placeholder art pipeline, and the plan for Android. Status: **spike** — one battle
viewer on desktop, placeholder art, everything below is deliberately small.

## Architecture: Core -> battle results -> presentation

```
src/BeastCraft.Core           rules only. Battles, content builders, saves. No drawing, no time.
      |  BattleSession.Begin(setup) -> BattleSessionRun.Step() -> BattleTurnResult (per turn)
      v
src/BeastCraft.Presentation   engine-neutral "what to show" (netstandard2.1, no MonoGame types)
      |  GameContent       loads the data JSON through Core's validators and builders
      |  DemoBattle        the spike's battle setup (a save + an encounter template + a seed)
      |  BattlePlayback    steps the session, snapshots every unit before/after each turn
      |  SkillBeat         a turn's fired skills: caster, skill, element, targets, damage
      |  TurnAnimation     a turn as a function of time: walk, then each beat's VFX timeline;
      |                    shown HP drops as hits land; knocked-out units fade out
      |  VfxTimeline       one effect as a pure function of time (seeded)
      |  ParticleBurst     closed-form ballistic particles (seeded)
      |  HexLayout         axial hex -> virtual pixels (pointy-top, whole pixels)
      |  PixelFont         a 3x5 font defined in code
      v
src/BeastCraft.Desktop        MonoGame DesktopGL host: textures, SpriteBatch, input, window.
```

Rules the spike keeps, and later hosts must keep:

- **The simulation never waits for, or hears from, the renderer.** Turns come from
  `BattleSession.Begin` + `Step`, which is the same loop `BattleSession.Run` and
  `BattleTurnExecutor.RunBattle` use (`BattleRun`), so a viewed battle and a headless one draw
  the same random numbers and end the same way (tests pin stepped == `Run`; BalanceSim output
  is byte-identical before and after the refactor). The viewer reads results; it never mutates
  units, and nothing presentation-side uses the battle's `System.Random`.
- **Animation is a pure function of recorded results and time.** `TurnAnimation.ShownHp(unit, t)`,
  `VfxTimeline.Sample(t)`: no per-frame state, no wall clock inside them. That is what makes the
  `--screenshot` mode reproducible and lets a host scrub, skip (Space finishes a turn) or run at
  any frame rate.
- **No engine types below the host.** Core and Presentation use their own `Vec2`, palette chars
  and plain data; MonoGame's `Color`/`Vector2`/`Texture2D` appear only in `BeastCraft.Desktop`.
- **Pixel-perfect.** Everything renders into a 640x360 target, scaled to the window by the
  largest whole number that fits (letterboxed), `SamplerState.PointClamp`. Sprites sit on whole
  pixels; big units are drawn at x2, never fractional scales.
- **No content pipeline, no shaders** (spike constraint). PNGs load with
  `Texture2D.FromStream` and are premultiplied on load; text is the built-in pixel font; effects
  are alpha or additive SpriteBatch passes, and a hit flash is the target's sprite redrawn
  additively in the flash tint.

### What a turn looks like

1. If the acting unit moved, it slides from its start tile to its end tile (240 ms).
2. Each fired skill (`SkillBeat`) plays its effect in order, 90 ms apart:
   projectile (caster -> each target) -> impact -> hit-stop -> flipbook + particle burst on each
   target, screen shake, hit flash, floating damage number (the sum of the skill's
   `DamageHit.Roll.Amount` on that target; `!` on a critical).
3. Each target's HP bar drops when that beat lands (a shield or a later heal in the same turn can
   hold it up); a unit felled this turn fades after its fatal hit and disappears when that beat
   ends. Everything settles on the after-snapshot when the turn ends.

Simplifications, on purpose: all skills of a turn fire from the unit's end tile; knock-backs and
pulls snap at the turn's start; damage over time, heals, buffs and statuses have no VFX of their
own yet (their HP changes settle at the end of the turn); the avatar is left out (it has no
tile).

## The VFX library (`BeastCraft/Assets/_Project/Data/Vfx/vfx-library.json`)

Kept beside the other data files rather than in a new top-level `content/`: every loader, test
and the desktop copy step already work from the `Assets/_Project/Data` layout
(`ProjectRelativePath`), and moving *all* content out of the legacy Unity tree is a separate,
repo-wide change. It is presentation data only — the battle never reads it, so no edit to it can
change an outcome or the balance reports.

```jsonc
{
  "SchemaVersion": 1,
  "ElementDefaults": [                  // exactly one per Element, None included
    { "Element": "Fire", "Effect": { /* effect */ } }
  ],
  "Skills": [                           // per-skill overrides, by skill id
    { "SkillId": "ember_shot", "Effect": { /* effect */ } }
  ]
}
```

An effect (every part but `Motion` optional; ranges enforced by `VfxLibraryValidator`):

| Field | Meaning | Range |
| --- | --- | --- |
| `Motion` | `Projectile` (flies caster -> target) or `Instant` | |
| `TravelMs` | flight time; 0 for `Instant` | 1-2000 for a projectile |
| `Projectile` | `{ Sheet, Tint, Scale, Additive }`: the sprite that flies | scale 1-4 |
| `Flipbook` | `{ Sheet, FrameWidth, FrameHeight, Frames, Fps, Tint, Scale, Additive }` on each target | frame size must equal the sheet's; frames <= the sheet's; fps 1-60 |
| `Particles` | `{ Sheet, Count, SpeedMin, SpeedMax, LifetimeMs, Colors[], Gravity, Additive }` | count 1-64; speed 0-400 px/s; life 50-3000 ms; gravity +-1000 px/s^2 (negative rises) |
| `ScreenShake` | `{ Amplitude, DurationMs }`, decaying | 0-8 px, 0-1000 ms |
| `HitFlash` | `{ Tint, DurationMs }` on each target | 0-500 ms |
| `HitStopMs` | freeze at impact (flipbook and particles hold; flash, shake and number run) | 0-250 |
| `DamageNumber` | `{ Color, CritColor, RiseMs, RisePx }` | 100-2000 ms, 0-40 px |

`Sheet` names a sprite of the pixel-art manifest; colours (`Tint`, `Colors`, `Color`) are
**palette chars** from the same manifest (`"o"` = the fire orange), so effects stay on-palette.
Tints multiply, so a sheet meant to be tinted per element is drawn white (`fx_hit_burst`,
`fx_spark`). The validator checks the schema version, one default per element, unique skill ids
that resolve (beast skills, avatar actives and enemy-library skills), sheets and frame sizes
against the manifest, palette chars, and the ranges above; the shipped file is validated by a
test and on every load of the content.

Lookup (`VfxLibrary.Resolve`): the skill's own effect, else its element's default, else `None`'s.
Today Ember Shot (projectile ember -> 8-frame fire burst at x2, rising embers, shake, hit-stop)
and Flame Wave (instant x2 burst, bigger shake) are authored; everything else uses an
element-coloured default (a tinted spark projectile and hit burst, element-ramp sparks).

Timing (`VfxTimeline`): impact at `TravelMs`; hit-stop until `impact + HitStopMs`; the flipbook
and particles run from the hit-stop's end; shake, flash and the damage number run from impact;
the effect ends when its last part does. Particles are drawn once, up front, from a seeded
`DeterministicRandom` (not `System.Random`), and move in closed form, so the same seed gives the
same frame at any frame rate.

## Art pipeline (`Tooling/PixelArt`)

Sprites are text grids (one char per palette colour) in `Tooling/PixelArt/sprites/*.txt`;
`build.py` (Python 3 + Pillow 12.3.0) adds the auto-outline and rim shading, runs the integer-only
generators (the `fx` burst flipbooks, the `hex` tiles made from a tile texture), and writes the
game's PNGs (Git LFS) plus `pixel-art-manifest.json` (every sprite's file, frame size, frame
count, ArtKey, and the palette) to `BeastCraft/Assets/_Project/Art/Pixel/`. With the pinned
Pillow it regenerates them byte for byte. The desktop project copies `Data/**/*.json` and
`Art/Pixel/*` into `Content/` beside the executable; `GameContent.FindRoot` looks there first and
falls back to the repo. Style rules: `Tooling/PixelArt/STYLE.md`.

Placeholder content: all ten roster beasts, four enemies (the others borrow the brute), 32x36
pointy-top hex tiles (columns 32 px apart, rows 27 px: whole pixels), the fire burst, a tintable
hit burst and two particles. Multi-hex enemies are drawn at x2 over their tinted footprint (the
Hex7 giant's real sprite should be authored at 64x64 later).

## Android, after this spike

1. **`src/BeastCraft.Android`**, a MonoGame Android host (`MonoGame.Framework.Android`,
   `net*-android`) referencing Core and Presentation unchanged. Nothing platform-specific lives in
   the shared projects today; keep it that way.
2. **Share the renderer.** Move the drawing code out of `BeastCraft.Desktop` into a shared
   MonoGame project (e.g. `BeastCraft.Game`) that both hosts reference; each host keeps only its
   entry point, window/activity setup and input mapping (touch: tap = step, a button for auto).
3. **Content on device.** Ship `Content/` as Android assets and open files through a small
   `IContentSource` (desktop: files; Android: `TitleContainer.OpenStream`), which `GameContent`
   and the atlas read through instead of `File`/`Directory`.
4. **Screens.** 640x360 already scales by whole numbers to 1280x720 and 1920x1080; phones with
   other aspect ratios letterbox, or widen the virtual width to the nearest whole-scale fit.
   Large arenas (radius 7, 414 px tall) need a camera or a smaller tile before they fit.
5. **Later:** the content pipeline (texture atlases, compression) only if load times demand it;
   a real font; audio; a shader-based hit flash.

## Open questions

- Keep generated pixel art in the legacy `BeastCraft/Assets/_Project/Art` tree, or move all
  content (data and art) to a top-level `content/` when Unity is removed?
- Should `ArtKey` become part of the species and enemy data (as the cosmetics already do) instead
  of the host mapping `beast_<id>` / `enemy_<id>` sprite names?
- Status effects, heals, buffs and the avatar's casts need their own VFX vocabulary (a "status
  applied" ring, a heal rise); extend the schema or add a second effect list per skill?
