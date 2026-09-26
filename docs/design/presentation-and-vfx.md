# Presentation and VFX (MonoGame desktop and Android spike)

How the game is drawn: which code owns what, how a battle becomes pictures, the portrait screen,
the art manifest, the skill VFX data, the placeholder art pipeline, and the hosts (desktop,
Android; iOS planned). Status: **spike**: one battle viewer on desktop and Android with
throwaway pixel-art placeholders, built so the final art can drop in; the final art of all ten
beasts (illustrated, AI-assisted) draws beside them (see "Illustrated sprites").

**Art direction (user decision):** the final art is hand-drawn chibi characters, Studio
Ghibli-inspired, at Sword x Staff's quality — illustrated, **not** pixel art. The recommended
stack is Spine for characters and hand-painted flipbook VFX layers composited additively.
Everything below is style-agnostic on purpose: the pixel placeholders go through the same
manifest, renderer and VFX schema that illustrated PNGs (and later Spine) will. Do not polish
the placeholders.

The art and animation brief: [`docs/art/art-brief.md`](../art/art-brief.md). Since 2026-09-25 the beast
art is made with an AI-assisted pipeline (`Tooling/ArtLab/`) with the producer as art director; the
brief's commissioning sections are superseded.

## Architecture: Core -> battle results -> presentation

```
src/BeastCraft.Core           rules only. Battles, content builders, saves. No drawing, no time.
      |                    (also: the VFX library and art manifest data + validators, BeastCraft.Vfx)
      |  BattleSession.Begin(setup) -> BattleSessionRun.Step() -> BattleTurnResult (per turn)
      v
src/BeastCraft.Presentation   engine-neutral "what to show" (netstandard2.1, no MonoGame types)
      |  GameContent       loads the data JSON through Core's validators and builders
      |  DemoBattle        the spike's battle setup (a save + an encounter template + a seed)
      |  BattlePlayback    steps the session, snapshots every unit (HP, position, statuses,
      |                    stat changes) before/after each turn
      |  SkillBeat         a turn's fired skills: caster, skill, element, targets, damage, VFX keys
      |  TurnAnimation     a turn as a function of time: walk, then each beat's VFX timeline and
      |                    on-apply overlays; shown HP drops as hits land; shown statuses switch
      |                    at the blow that applied them; knocked-out units fade out
      |  VfxTimeline       one effect as a pure function of time (seeded): v1 parts + v2 layers
      |  VfxArea           the affected hexes as a circle (what area layers centre on and size to)
      |  VfxAuraSampler    a lasting status's pulsing aura sprite
      |  ParticleBurst     closed-form ballistic particles (seeded)
      |  HexLayout         axial hex -> board pixels (pointy-top, 32 px columns)
      |  SkillFootprint    a skill's range and area as hex cells relative to its caster
      |  PortraitLayout    the 1080x1920 canvas: letterbox fit, safe insets, bands, board fit
      |  PixelFont         a 3x5 font defined in code
      |  IContentSource    where content files come from (FileContentSource: plain files)
      v
src/BeastCraft.Game           the shared MonoGame viewer (net10.0 library): BattleViewerGame
      |                    (drawing, keyboard/mouse/touch input), SpriteAtlas, SpriteRenderer,
      |                    ITextRenderer + PixelText, TitleContainerContentSource; ViewerHost
      v
src/BeastCraft.Desktop        DesktopGL host: Program.Main, command line, ViewerHost.Desktop().
src/BeastCraft.Android        Android host: MainActivity (portrait, cutout insets), APK assets.
```

Rules the spike keeps, and later hosts must keep:

- **The simulation never waits for, or hears from, the renderer.** Turns come from
  `BattleSession.Begin` + `Step`, which is the same loop `BattleSession.Run` and
  `BattleTurnExecutor.RunBattle` use (`BattleRun`), so a viewed battle and a headless one draw
  the same random numbers and end the same way (tests pin stepped == `Run`; BalanceSim output
  stays byte-identical). The viewer reads results; it never mutates units, and nothing
  presentation-side uses the battle's `System.Random`. Everything art-related in the data
  (`ArtKey`, the VFX library) is presentation-only.
- **Animation is a pure function of recorded results and time.** `TurnAnimation.ShownHp(unit, t)`,
  `ShownStatusKeys(unit, t)`, `VfxTimeline.Sample(t)`: no per-frame state, no wall clock inside
  them. That is what makes the `--screenshot` mode reproducible and lets a host scrub, skip or
  run at x1/x2/x3.
- **No engine types below the host.** Core and Presentation use their own `Vec2`, `Rect`, palette
  chars and plain data; MonoGame's `Color`/`Vector2`/`Texture2D` appear only in
  `BeastCraft.Game` and the hosts.
- **Sizes are world units, not pixel multiples.** One world unit is one hex column step (32 board
  pixels). A sprite is placed by its manifest pivot and scaled by its `PixelsPerUnit`, so a 32 px
  placeholder and a 512 px illustration of the same beast draw the same size (see "Art
  manifest"). The board is drawn in board space and fitted to the screen; nothing assumes an
  integer scale any more.
- **No content pipeline, no shaders** (spike constraint). PNGs load with
  `Texture2D.FromStream` and are premultiplied on load when the manifest says straight alpha;
  text goes through `ITextRenderer` (the pixel font today); effects are alpha or additive
  SpriteBatch batches, and a hit flash is the target's sprite redrawn additively in the flash tint.

### What a turn looks like

1. If the acting unit moved, it slides from its start tile to its end tile (240 ms).
2. Each fired skill (`SkillBeat`) plays its effect in order, 90 ms apart: optional projectile
   (caster -> each target) -> impact -> hit-stop -> the effect's layers (flipbooks, decals,
   rings, bursts, particles, glyphs), screen shake, hit flash, floating damage number (the sum of
   the skill's `DamageHit.Roll.Amount` on that target; `!` on a critical). Each status or stat
   change that lands on a target (a burn, a stun, a shield, a buff...) and each knockback adds that
   effect type's on-apply overlay from the impact — every time it lands: a second skill, or the same
   one again, applying a status the unit already has plays the overlay again in full. What landed
   comes from the battle's own record (`SkillActivation.Applied`: every non-damage effect that
   passed its chance roll, a record nothing in the battle reads), carried on `SkillBeat.Applied`.
3. Each target's HP bar drops when that beat lands; its status auras and icons switch to the
   after-turn set at the same moment (the acting unit's own ticks happen as its turn begins); a
   unit felled this turn fades after its fatal hit and disappears when that beat ends.
   Everything settles on the after-snapshot when the turn ends.

Simplifications, on purpose: all skills of a turn fire from the unit's end tile; knock-backs and
pulls snap at the turn's start; heal numbers are not shown (heals settle at the end of the
turn); the avatar is left out (it has no tile).

### Effects settings (`VfxSettings`)

The player's effects settings live in Core's `PlayerSettings` (`EffectsIntensity` Full / Reduced /
Minimal, `ScreenShake`, `Flashes`; persisted by `PlayerSettingsStore`, and an older settings file
loads as Full, on, on). `VfxSettings.From(settings)` hands them to `TurnAnimation`, which passes
them to every `VfxTimeline` (beats and overlays): an input like the seed, so a turn stays a pure,
deterministic function of the turn, the library, the seed and the settings. They drop parts of an
effect (and the time those parts took) but never move its impact, and what still plays is drawn
exactly as at Full (a halved burst is the full burst's first half).

| Setting | What plays |
| --- | --- |
| Full | everything authored |
| Reduced | no `Glyphs` layers and no `Particles` layers (the secondary bursts); the effect's own particle burst at half its count |
| Minimal | only the damage number, the hit flash (if Flashes is on) and one simple burst (the effect's own particles, else its first particle layer's, at half the count); no projectile, flipbook, layers or shake |
| ScreenShake off | no shake at any intensity |
| Flashes off (accessibility) | no hit flash, and no bright additive bursts: additive flipbooks (the effect's own and `Flipbook` layers) and additive `RadialBurst` layers |

In the viewer, the **gear** at the header's right end (`PortraitLayout.SettingsButton`) opens a
small overlay (`SettingsPanel`, `SettingsRow`): tap Effects to cycle Full, Reduced, Minimal, tap
Screen shake or Flashes to toggle, Close (or tap outside) to close. A change is saved at once (the
default save folder's `settings` slot) and applies from the next turn played. The desktop host's
`--effects full|reduced|minimal`, `--no-shake`, `--no-flashes` and `--show-settings` set them for
screenshots (which never read or write the saved settings).

## The portrait screen (`PortraitLayout`)

The viewer draws on a fixed logical **1080x1920 canvas (9:16)**, scaled uniformly to the largest
size that fits the window or screen **inside its safe area** and centred, with black bars on the
rest (`CanvasFit`). Why 1080x1920: it is the common phone panel in portrait, so on the reference
device a canvas pixel is a screen pixel and illustrated UI authored at 1080p lands 1:1; 9:16 is
the narrowest aspect worth designing for (taller 19.5:9 phones get bars top and bottom, which is
where their notch and gesture bar sit anyway; 3:4 tablets get side bars), so nothing on the canvas
is ever cropped, and layout, hit-testing and screenshots are resolution-independent.

**Safe area.** `ViewerHost.SafeArea` returns the screen's insets (`SafeInsets`, back-buffer
pixels) every frame and the canvas is fitted inside what is left. Android reads the display
cutout (`DisplayCutout.SafeInset*`, API 28+) after the window lays out, with the window drawn
under the cutout (`LayoutInDisplayCutoutMode.ShortEdges`); desktop can fake one with
`--safe-inset L,T,R,B`.

Bands, top to bottom (all pure layout maths, tested headless):

| Band | Canvas rect | What |
| --- | --- | --- |
| Header | y 16-72 | title; turn and seed |
| Turn order | y 84-260 | up to 8 portrait tiles: the acting unit first (gold, "NOW"; "NEXT" between turns), then the forecast; team-coloured borders, HP bars |
| Board | y 276-1426 | the arena, framed by the auto camera (below); its fit-all view is `FitBoard(width, height)`, which scales the arena's frame (`BoardFrame`: its tiles plus 36 board px of sprite headroom above and a 6 px margin below and to the sides) to the band and centres it. Every preset is about as wide for its height as the band (0.89 against 0.90), so fit-all fills it top to bottom and 87-93% across: x4.79 Small, x3.30 Medium, x2.52 Large (the Large hexagon it replaced was drawn at x2.15) |
| Toast | y 1438-1502 | the log collapsed to its latest line (and a count) |
| Skill strip | y 1514-1758 | the acting unit's skills as cards: icon (the skill's `ArtKey`), name, shape and range, cooldown/READY/CAST; the firing and the selected one marked |
| Controls | y 1774-1894 | PLAY/PAUSE (auto-play), x1 / x2 / x3 speed, SKIP (resolve the rest of the battle and show the result) |

Input: desktop keys (Space step/finish turn, A auto, 1-3 speed, S skip, Tab cycle the selected
skill, Esc quit) and the mouse (click buttons and cards; click a skill for its detail card, hover to
preview it);
touch taps are hit-tested on the canvas (a button, a card, else the board steps), two fingers
toggle auto, Back quits. The desktop window opens at 540x960; `--screenshot` renders K x 540x960
(default 1080x1920).

**Text.** Every string goes through `ITextRenderer` (`Measure`, `LineHeight`, `Draw`, sized by
cap height in the current space, placed by the capitals' top). The UI typeface is **Fredoka
SemiBold** (`content/fonts/Fredoka-SemiBold.ttf`, SIL Open Font License 1.1, its `OFL.txt`
beside it; see `THIRD-PARTY-NOTICES.md`): rounded and warm, it suits the chibi tone and reads well
small. `TtfText` (BeastCraft.Game) rasterises it at run time with **FontStashSharp** (Zlib licence)
into a glyph atlas, at the size the text lands on screen (the current transform's scale, rounded to
a few sizes so the zooming camera does not fill the atlas), with linear filtering and a drop shadow;
the HUD, skill strip and cards use it. Both hosts ship the font and its licence (desktop:
`Content/fonts/` beside the executable; Android: APK assets `Content/fonts/`). `PixelText` (the
built-in 3x5 font) is only the fallback when the TTF cannot be loaded (missing, or an un-pulled Git
LFS pointer).

### Arena

Each arena is a rectangle of pointy-top hexes, `Width x Height` tiles (battle-system.md, "Arenas":
5 x 7, 8 x 11, 11 x 15). `HexLayout.BoardBounds(width, height)` is the box of its tile sprites in
board space (tile (0, 0)'s centre the origin; `Width + 1/2` columns wide, because odd rows sit half a
column right, so it is a quarter column off the origin), and `BoardSize` its size. The board is drawn
so the edge is a clean rectangle, not the zigzag of alternate rows: under the tiles a plate (dark
soil, the box plus `BoardEdgeMargin`, 6 px) on which the end rows' pointed tops and bottoms sit; then
the tiles (stone in the enemy's deployment rows, grass elsewhere); then, clipped to the box, one
dimmed (x0.47) half tile in each side notch (`HexLayout.EdgeNotches`: left of every odd row, right of
every even row, each centred on the box edge so exactly its inner half shows). The notch halves are
decoration, never on the board. The range diagram (below) keeps its own hex disc: it is a legend for
the skill's shape, not the arena.

## The auto camera (`CameraRig`, `TurnCamera`)

There is no manual zoom or pan: the camera frames the action by itself. `CameraRig`
(Presentation, pure and tested) holds the framing maths for one arena in the board band: a view
is a board-space centre and a zoom (a multiple of the fit-all scale, so zoom 1 is exactly the old
`FitBoard` view); `Frame(boxes)` fits board-space boxes plus `Padding` (28 board px), zoom clamped
between fit-all and `MaxZoom` (x2), itself capped at an absolute `MaxScale` (5.5 canvas px per
board px) so a small arena that already fits big is not blown up; `Clamp` keeps what the view
shows inside the arena's bounds (`PortraitLayout.BoardFrame`: tiles plus sprite headroom), centring on an axis where it shows
more than the arena; `Ease` moves between views with smoothstep. A unit's box is its sprite, HP
bar and icons (`UnitBox`: twice the size for a multi-hex unit); an effect's area is its circle.

`TurnCamera` is the camera over one turn, a pure function of the turn clock (the same played
clock as `TurnAnimation`, so x2/x3 speed it up): from the view the turn began with it eases
(`EaseMs` 360) to frame the acting unit's walk, then — `LeadMs` (150) before each beat — the
caster, every target and on-apply overlay unit, and the effect's area; it holds the last framing
to the turn's end. Between turns and when idle, the host eases back toward the fit-all view
(`ReturnMs` 700, on the played clock); skip cuts straight to fit-all, and finishing a turn early
(Space) jumps the camera with it. A hit that spreads over a horde, or an all-enemies skill, needs
the whole arena and so stays at fit-all. The renderer applies the view as its board transform
(`CameraRig.Fit`) and clips the board to its band with a scissor rectangle (`SpriteRenderer.SetClip`).
Screenshots start each shown turn from fit-all, so they stay reproducible.

## Art manifest v2 (`content/art/pixel/pixel-art-manifest.json`)

`ArtManifestData` / `ArtSpriteData` (Core, `BeastCraft.Vfx`), validated by
`ArtManifestValidator` on every content load. Per sprite:

| Field | Meaning |
| --- | --- |
| `Name`, `File` | unique name (VFX specs name sheets by it); the PNG, relative to the manifest's folder, forward slashes, `..` allowed but never out of the content root (`ArtManifestData.ResolveFile`: `../beasts/golem/golem.png` is `art/beasts/golem/golem.png`) |
| `Kind` | `sprite` (a PNG strip) or `spine` (reserved; see below) |
| `Category`, `Label` | what it is for (`beast`, `enemy`, `fx`, `hex`, `icon`...), a caption |
| `ArtKey` | the key game data names the art by (see "ArtKey") |
| `FrameWidth`, `FrameHeight`, `Frames`, `FrameMs` | a horizontal strip of equal frames |
| `PivotX`, `PivotY` | the anchor in source pixels: a character's feet (placed on the tile centre), an effect's centre |
| `PixelsPerUnit` | source pixels per world unit (one hex column step): 32 for the placeholders; for an illustrated beast, derived from its `WorldHeight` (300-437 for the ten beasts) |
| `Filter` | `point` (pixel art) or `linear` (illustrated art); the renderer picks the sampler per batch |
| `Premultiplied` | `false` = straight alpha, premultiplied on load (SpriteBatch blends premultiplied) |
| `Tint` | optional `#rrggbb` multiplier (alias entries) |
| `Animations` | optional named clips: `{ Name, Sheet (another strip, or this one), Frames[], Fps, Loop }` |

Schema v1 still reads: `ArtManifestData.Normalize` maps it to exactly what v1 meant (centre
pivot, 32 px per unit, point filter, straight alpha; v1's `Kind` becomes `Category`).
`Tooling/PixelArt` writes v2 for the placeholders (a beast's or enemy's pivot is its feet, three
quarters down; the phoenix's idle strip is its `idle` clip).

**Renderer.** `SpriteAtlas` loads every `sprite` entry once per file; `SpriteRenderer` keeps one
SpriteBatch open while blend, sampler and transform stay the same and restarts it when a sprite
needs another filter or blend (so point-filtered placeholders and linear illustrations can share a
frame), and draws every sprite by its pivot at `UnitSize / PixelsPerUnit x scale`. Large units
(Triangle, Hex7) draw at two units. A clip plays on the viewer's clock (the phoenix's `idle`).

## Illustrated sprites (linear filter)

The ten beasts' final art (AI-assisted, producer-approved: `Tooling/ArtLab/`, provenance in
`Tooling/ArtLab/provenance/`; the starter trio first, then the other seven) is the illustrated art in the game. It goes through the same
manifest, atlas and renderer as the placeholders; only the entry's numbers differ.

- **Files.** `content/art/beasts/<id>/<id>.png`, one 512x512 frame each: the transparent master
  (`content/art/source/<id>/character.png`, feet pivot at its bottom centre) downscaled with Lanczos
  in premultiplied space to 504 px on its longer side, padded 4 px, centred on a power-of-two square.
  Feet pivot (256, 508). Both hosts copy `content/art/beasts/**` (not the masters) beside the pixel art.
- **Why 512.** The closest camera zoom is 5.5 canvas px per board px (`CameraSettings.MaxScale`), so a
  world unit is at most 176 px on the 1080-wide canvas; the tallest beast (the Treant, 1.4 units) is about
  250 px there (about 330 px on a 1440-wide screen). 512 px is about twice that, the usual 2x headroom.
- **Filter and alpha.** `Filter: "linear"`, `Premultiplied: false`: the PNGs are straight alpha, and
  `SpriteAtlas` premultiplies them on load (SpriteBatch blends premultiplied). A linear sprite also
  gets a **mip chain**, box-filtered from the premultiplied pixels (transparent pixels add no colour,
  so no dark fringes), and `LinearClamp` samples it trilinearly: at fit-all a beast is 70-140 px tall,
  4-7x smaller than its PNG, and without mips its outline would shimmer and break up. The frames are
  power-of-two so the mips work on GLES 2 too; the pixel art keeps point sampling and no mips.
- **Outline at board size.** The master's bold plum contour is about 3 px at 504 px and thinned out at
  fit-all, so the in-game copies get a contour-only line pass (`export_ingame.py --line-px 6`); the
  masters are unchanged.
- **Size is data.** `Tooling/PixelArt/illustrated.json` gives each sprite a `WorldHeight` (world units
  from the feet to the top of the art) and `build.py` writes `PixelsPerUnit = art height / WorldHeight`
  into the manifest. Current values: Treant 1.4, Leviathan 1.3, Phoenix 1.25, Thunderbird 1.25, Griffin
  1.25, Frost Wyrm 1.2, Kirin 1.15, Tarasque 1.05, Golem 1.0, Basilisk 0.65. The Tarasque is the bulkiest
  (about 1.3 units wide; the Golem about 1.14, the Phoenix 0.86) and the Basilisk the lowest (about 1.2
  units long); all but the Basilisk stand taller than the 0.75-unit placeholders. Checked on the board in
  mixed teams (`--team`). Tune by editing the number and rebuilding.
- **Which art a species uses** is its `ArtKey`: every species names `beast/<id>/illustrated`; the pixel
  placeholders stay in the manifest under `beast/<id>`.
- **Portraits.** The turn-order portrait fits the whole frame, so the equal square frames give every
  beast an equal portrait box; a long, low beast (the Basilisk) fills it only in width and reads small.

## ArtKey (data -> art)

Every species (`beast-roster.json`) and enemy (`enemy-library.json`) carries an optional,
presentation-only `ArtKey` (`beast/phoenix/illustrated`, `beast/leviathan`, `enemy/giant`), carried through the DTOs, builders
and `EnemyCatalog` onto `CreatureSpeciesSO.ArtKey`. The viewer draws a unit with the manifest
entry whose `ArtKey` matches — no more `beast_<id>` naming convention. Every shipped entry has
one, and `ArtReferenceValidator` (on content load, and a test with keys required) holds each to
the manifest; the roster and enemy validators refuse a malformed key (lowercase snake_case
segments joined by `/`). An enemy without art of its own yet (archer, caster, shaman, stalker,
stingling) is an **alias** entry: `Tooling/PixelArt`'s `# alias:` + `# tint:` headers list it
under its own ArtKey with another sprite's `File` and a `Tint`, so the data already names the
final art and only the manifest changes when it arrives. Keys name a character, not a pose:
poses and actions are its clips (or its Spine animations).

Skills use the same mechanism for their icons: every beast skill, avatar active and avatar
passive in `skill-library.json` has an `ArtKey` (`skill/<id>`), carried by `SkillData` /
`PassiveData` and `SkillLibraryBuilder` onto `SkillSO.ArtKey` / `PassiveSkillSO.ArtKey` (the
old, unused `Icon` string is gone). `ArtReferenceValidator` holds every skill-library key to the
manifest (required for shipped content; an enemy-library skill's is checked when it has one, and
the viewer falls back to an element-coloured tile with the initial). The placeholders are
generated by `Tooling/PixelArt/build.py` from the skill library itself: a 24x24 disc in the
element's colours with the skill's initial.

## The VFX library (`content/data/Vfx/vfx-library.json`, schema v2)

Presentation data only — the battle never reads it. v1 files still read.

```jsonc
{
  "SchemaVersion": 2,
  "ElementDefaults": [                  // exactly one per Element, None included: damage by element
    { "Element": "Fire", "Effect": { /* effect */ } }
  ],
  "EffectDefaults": [                   // at most one per effect type
    { "Key": "Stun", "Effect": { /* on apply */ }, "Aura": { /* while it lasts */ } }
  ],
  "Skills": [                           // per-skill overrides, by skill id
    { "SkillId": "ember_shot", "Effect": { /* effect */ } }
  ]
}
```

**Resolution order** (`VfxLibrary.Resolve(skillId, element, primaryKey)`): the skill's own
entry → its **primary effect type**'s default → its **element**'s default → `None`'s. The
primary effect type (`VfxLibrary.PrimaryKey`) is null for any skill that deals damage (it looks
like its element), else its first effect's key: `Heal`, `Shield`, `Taunt`, `Stun`, `Burn` (damage
over time from a Fire source), `Poison` (from any other), `Cleanse`, `BuffStat`, `DebuffStat`,
`Knockback`. On top of the main effect, every status that newly lands on a target (and each
knockback) plays that key's on-apply `Effect` as an overlay from the impact (not repeated when it
is the beat's own primary key).

**Auras.** A lasting key (`Shield`, `Taunt`, `Stun`, `Burn`, `Poison`, `BuffStat`, `DebuffStat`)
may have an `Aura`: `{ Sheet, Tint, Scale, PulseMs, Blend, Depth (Ground at the feet | Over),
Icon, IconTint }`. While a unit's snapshot carries the status (`UnitSnapshot.Statuses` /
`Modifiers` -> `StatusKeys()`), its aura pulses and its icon sits above its HP bar; the set
switches at the blow that applied or removed it (`TurnAnimation.ShownStatusKeys`), so an aura
lasts exactly as long as the battle keeps the status (tested against the demo battle).

**An effect** (every part but `Motion` optional; ranges enforced by `VfxLibraryValidator`):

| Field | Meaning | Range |
| --- | --- | --- |
| `Motion` | `Projectile` (flies caster -> target) or `Instant` | |
| `TravelMs` | flight time; 0 for `Instant` | 1-2000 for a projectile |
| `Projectile` | `{ Sheet, Tint, Scale, Additive }`: the sprite that flies | scale 1-4 |
| `Flipbook`, `Particles` | v1 parts on each target (still supported) | as v1 |
| `ScreenShake` | `{ Amplitude, DurationMs }`, decaying (board px) | 0-8 px, 0-1000 ms |
| `HitFlash` | `{ Tint, DurationMs }` on each target | 0-500 ms |
| `HitStopMs` | freeze at impact | 0-250 |
| `DamageNumber` | `{ Color, CritColor, RiseMs, RisePx }` | 100-2000 ms, 0-40 px |
| `Layers` | v2: composited in order (below) | |

**A layer:** `{ Type, Anchor, StartMs, DurationMs, Blend, Depth, Sheet, Tint, Scale, EndScale,
FadeInMs, FadeOutMs, ... }`. `StartMs` counts from the hit-stop's release (negative = a charge-up
before the impact, -2000..3000); `DurationMs` 1-4000; `Blend` `Alpha` or `Additive`; `Anchor`
`Target` (each target), `Area` (once, at the centre of every hex the targets stand on) or
`Caster`; `Depth` `Ground` (under the units) or `Over` (default: a decal on the ground, the rest
over). Radii are in hexes, and an `EndRadius` of 0 means **the affected area's radius**
(`VfxArea`: the mean of the targets' tile centres, out to the farthest plus half a hex), so a
ring fits exactly the hexes a burst hit, on any arena and with any art.

| Type | Draws |
| --- | --- |
| `Flipbook` | `Frames` of `Sheet` at `Fps` (the last holds), `Scale` -> `EndScale` |
| `GroundDecal` | a scorch (frost, ooze...) sized to `EndRadius`, on the ground, fading over `FadeOutMs` |
| `Shockwave` | a ring growing `StartRadius` -> `EndRadius` (eased out), fading |
| `RadialBurst` | `Count` sprites flying out along their rays (rotated to them), fading |
| `Particles` | a burst (`Particles`: the v1 particle spec) from the anchor |
| `Glyphs` | `Count` rune frames circling at `StartRadius` on a ground ellipse, rising `RisePx`, turning `Spin` times |

The timeline turns layers into `VfxSprite`s (sheet, frame, board position, scale or size in
board px, rotation, alpha, tint, blend, ground) — the renderer draws those and nothing else, so
an illustrated flipbook set replaces the placeholders by changing sheet names in the data.
Everything is seeded (`DeterministicRandom`), closed-form and tested: timeline composition, the
area and ring radius, the resolution order, overlays, aura lifetime, the validator.

**Showcase.** The Phoenix's Ember Shot (projectile; charge-up glyphs at the caster, scorch decal,
glow, a ring on the area, the 8-frame fire burst, radial rays, embers), Flame Wave (area-sized
scorch, ring and rays along the line) and Rebirth Flame (glyphs, ring, glow, fire and rising
embers at the caster, plus its Shield overlay) carry the full stack. Every effect type has a
default, and the demo battle (Phoenix, Golem, Kirin, Frost Wyrm; seed 20260933, picked so it
happens) shows a burn, heal, taunt, shield, stun and buffs/debuffs with their auras and icons.

## Range diagrams (`SkillFootprint`)

`SkillFootprint.Of(skill, casterFootprint)` is a pure function of the Core skill data
(`TargetShape`, `Range`, `TargetSide`) returning hex cells relative to the caster's anchor
(0, 0): `Caster` (its tiles), `Reach` (where it may pick a target: every tile within `Range` of
the caster's nearest tile, for SingleTarget and Line), `Area` (what it hits: the example target
for SingleTarget, the line from the caster's nearest tile along the axis to that target for
Line, the six arms for Cross, the disc(s) for AreaBurst, the caster for Self) and `Focus`, plus
`IsGlobal` for AllEnemies/AllAllies. Multi-hex casters follow the battle's rules (bursts and
crosses from every tile, ranges from the nearest), and a test holds the fixed shapes to
`SkillTargetResolver` on a board full of units. It is unbounded (a battle clips to the board).
The skill detail card (below) draws it: caster gold, reach blue, area in the side's colour, the
example target outlined, on a hex-of-hexes backdrop (`PortraitLayout.FitDisc`) whatever the
arena's shape: the diagram teaches the shape and range, not the board.

## Skill detail card and glossary

Tapping (or clicking; hovering previews) a skill in the strip opens its **detail card** over the
lower board: `SkillCardLayout` places it on the board's foot (inside `PortraitLayout.SkillDetail`),
as tall as its content, with the stats, power and scaling lines word-wrapped to the card's full
width above the split, then the range diagram beside the wrapped description (a word wider than a
line is broken between characters). `SkillCard.Of(skill, glossary)` (Presentation, pure) works out everything it
shows from the skill's data: the icon (`ArtKey`), name, element and damage-category tags, target
tags (Ally, Enemy, Self: the shape and side, a burst or whole-team skill covering its caster),
cooldown ("Every turn" at 0 or 1), range ("Melee", "Range 3", "Line 4", "Burst 2 around self",
"Whole field", "Self") and uses (once per battle, ready at once), one power line per effect from
the data (damage and heals as a percent of the attacking stat, a shield of Defense, a burn or
poison per turn, a knockback in hexes, a buff or debuff with its duration, stacks and chance) and
the level scaling (`MagnitudeGrowthPerLevel`, `MaxLevel`); then the range diagram beside the
description as rich text.

**Glossary.** `content/data/Glossary/glossary.json` (`GlossaryData`; `Glossary`) lists the terms:
the statuses (Stun, Shield, Taunt, Burn, Poison, Cleanse, Knockback, Slow), Crit, Heal, Aura and the
stances (Vanguard, Ranged, Skirmisher), each with its `Forms` and a `Definition`. `Glossary.Parse`
turns skill text into spans: explicit `[[shown words|Term]]` markup first (for a phrase that names a
term in other words: "[[turn the target to stone|Stun]]"), then every form standing as a whole word,
case-aware — a lowercase form also matches with a capital first letter, a form with a capital (a
name) only exactly — the longest form winning, then the earlier term: deterministic.
`RichTextLayout` wraps the spans to the text area (a phrase keeps its term across a line break;
words of one kind merge into one run) and hit-tests taps (`TermAt`). Terms are drawn in their
category's colour and underlined; tapping one opens its definition in a popup
(`PopupPlacement`, via `SkillCardLayout.PlacePopup`) that never covers the term or the description
block: below them when it fits inside the card, else flipped above, clamped into the card (which
lies inside the safe area), with a small pointer toward the term. Any tap — on the popup or
elsewhere — closes it, and a tap off the card closes the card.
`GlossaryValidator` (on every content load, and tested) checks the file (unique snake_case ids and
names, a category, a definition, no form in two terms) and that **every term used in skill text
resolves** (each mark in a beast skill's, avatar active's, passive's or enemy-library skill's name
or description names a term and is closed) and that every skill-library skill that applies a status
or a cleanse names it in its description. Desktop screenshots: `--select-skill N` opens the card,
`--glossary TERM` its definition.

## Spine later (`Kind: "spine"`)

The manifest already reserves `Kind: "spine"` (`File` = the skeleton, with its atlas beside it).
Today it is validated for a name and a file, skipped by `SpriteAtlas`, and refused as a VFX
sheet. To plug Spine in: add a `SpineRenderer` next to `SpriteRenderer` in `BeastCraft.Game`
(the spine-runtimes MonoGame runtime; a dependency and a licence to take on deliberately) that
loads `spine` entries, and have `DrawCharacter` dispatch on the entry's kind: place the skeleton
at the same pivot and world scale (`PixelsPerUnit`), mirror it for the enemy side, and map the
viewer's moments to animations (`idle`, `move` during the walk, `attack` at a beat's start,
`hit` at an impact, `death` while fading). Nothing in Core, Presentation or the data changes —
species and enemies keep their `ArtKey`, and only the manifest entry behind it switches from a
sprite to a skeleton. VFX stay flipbook layers either way.

## Art pipeline (`Tooling/PixelArt`) — placeholders, and the manifest for all art

The illustrated beasts are made by `Tooling/ArtLab` (see "Illustrated sprites"); `Tooling/PixelArt`
still writes the one manifest, merging them in from its hand-kept `illustrated.json`.

Sprites are text grids (one char per palette colour) in `Tooling/PixelArt/sprites/*.txt`;
`build.py` (Python 3 + Pillow 12.3.0) adds the auto-outline and rim shading, runs the
integer-only generators (`fx` burst flipbooks; `ring`, `disc` and `blob` VFX textures; `hex`
tiles), resolves aliases, and writes the PNGs (Git LFS) plus the v2 manifest to
`content/art/pixel/`. With the pinned Pillow it regenerates them byte for byte. The hosts copy
`content/data/**/*.json`, `content/art/pixel/*` and `content/art/beasts/**/*.png` into `Content/` (desktop, beside the exe) or
the APK's assets; `GameContent.FindRoot` looks beside the exe first and falls back to the repo.

Placeholder content: all ten roster beasts, four enemies (five more as tinted aliases), 32x36
pointy-top hex tiles, the fire and hit bursts, two particles, the VFX layer textures (ring,
aura ring, glow, scorch, ray, glyphs) and seven 9x9 status icons. They are throwaway: the final
art is illustrated, and new pixel enemies are not to be drawn.

## Hosts

### Layout: one shared viewer, thin hosts

`src/BeastCraft.Game` holds every line of MonoGame code the hosts share. It is MonoGame's own
pattern for shared game code (the 3.8.5 "2D Starter Kit" template's Core project): a plain
`net10.0` class library compiled against one MonoGame platform package with
`PrivateAssets="All"`, so the reference never flows to a host. Every platform package ships the
same `MonoGame.Framework` assembly (3.8.5.1, same identity on DesktopGL and Android), and each
host brings its own at runtime.

What differs per host is one small object, `ViewerHost`: window vs full screen, keyboard/mouse vs
touch, the HUD title, the content source and the safe-area provider. A host is an entry point
that builds a `ViewerHost` and runs `BattleViewerGame`; nothing else.

### Content: `IContentSource`

`GameContent.Load(IContentSource, errors)` and the sprite atlas open every file through
`IContentSource` (`Exists`, `Open`, `Describe`), with content-root-relative paths using forward
slashes. Desktop uses `FileContentSource`; Android uses `TitleContainerContentSource("Content")`
(the APK's assets; each file is copied into a `MemoryStream`, since asset streams cannot seek).

### Android (`src/BeastCraft.Android`)

- `MonoGame.Framework.Android` 3.8.5.1, `net10.0-android` (API 36 platform, min API 23); a
  `MainActivity : AndroidGameActivity`, **locked to portrait**, full screen, drawn under the
  display cutout with its safe insets passed to the viewer. The same data JSON and art the
  desktop copies to `Content/` are packaged as `AndroidAsset`s under `assets/Content/`.
- **Input:** tap a button or a skill card; tap the board to step (or finish the turn playing);
  two fingers toggle auto-play; Back quits (`Finish()` on `Game.Exiting`, since MonoGame's
  `Exit()` only moves the task to the back).
- **Builds are local only.** CI does not build Android (the workload, JDK and SDK would cost far
  more minutes than a compile check is worth); everything it shares with desktop is built and
  format-checked in CI through the desktop project. See the README's "Running on Android".

### iOS (planned, not started)

The same shape: `src/BeastCraft.iOS` with `MonoGame.Framework.iOS` (`net10.0-ios`), an app
delegate that runs `BattleViewerGame` with `ViewerHost.Mobile(...)` (safe area from
`UIView.SafeAreaInsets`), the content as bundle resources through `TitleContainerContentSource`.
Building needs a Mac with Xcode, out of reach of this setup and the Linux CI runner.

### Later

Spine characters (above); illustrated skill icons and UI; audio; the content pipeline (atlases, compression) only if load times demand it; a
shader-based hit flash; heal numbers.

## Open questions

