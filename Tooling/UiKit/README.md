# Journal UI kit (GitHub #52, direction D)

Procedural PNG textures for the shared UI toolkit's painted-parchment look ("painted world +
storybook page", producer decision 2026-10-01 on #52): a 9-slice panel, title plaque, three button
faces, two tab pills plus a ribbon accent, a slider rail, a slider knob, a toggle track, a toggle
knob, a chip and a card.

**Provenance: procedural, no AI.** Every pixel is drawn by `build_kit.py` and
`Tooling/ArtLab/scripts/texlib.py`'s generators (value noise for paper grain/mottle, a polygon-blob
watercolour wash, rounded/deckled masks, hand-coded ink-line corner flourishes) — Pillow + numpy
only, no model, no network call, no reference image. Deterministic: a fixed seed per asset, so a
rebuild with the same Pillow/numpy versions is byte-for-byte identical. Adapted from the direction-D
mocks' own generators (the UI kit review's session scratchpad: `settings_mock.py`, `texlib.py`,
`layout.py`, `icons.py`), which drew the same pieces composited directly onto one full settings
mock-up rather than as standalone, nine-sliceable textures.

## Regenerate
```
python Tooling/UiKit/build_kit.py
```
(Same Pillow/numpy as `Tooling/PixelArt`; see its `requirements.txt`.) Writes
`content/art/ui/kit/*.png` and prints each file's size and nine-slice insets. The insets are also
hand-kept in `Tooling/PixelArt/illustrated.json`'s `Painted` list (`ui_kit_*` entries); after
repainting a texture at a different size, update that entry's `NineSlice` to match what this script
prints, then rebuild the art manifest: `python Tooling/PixelArt/build.py`.

## Outputs

| File | Size | Nine-slice (L,T,R,B) | Used by |
| --- | --- | --- | --- |
| `panel.png` | 320x320 | 72,72,72,72 | `UiStyle` Panel look `panel` |
| `card.png` | 360x360 | 84,84,84,84 | `UiStyle` Panel look `card` |
| `title_plaque.png` | 260x104 | 76,38,76,38 | `ScreenHeader`'s title |
| `button_primary.png` | 240x160 | 60,54,60,54 | `UiStyle` Button look `primary` |
| `button_secondary.png` | 240x160 | 60,54,60,54 | `UiStyle` Button look `secondary` |
| `button_danger.png` | 240x160 | 60,54,60,54 | `UiStyle` Button look `danger` |
| `tab_unselected.png` | 220x150 | 64,58,64,58 | `UiStyle` Button look `nav` (`UnselectedTexture`) |
| `tab_selected.png` | 220x150 | 64,58,64,58 | `UiStyle` Button look `nav` (`Texture`) |
| `tab_ribbon.png` | 64x40 | none (fixed sprite) | `UiPainter.DrawTabRibbon` |
| `slider_rail.png` | 160x32 | 16,0,16,0 (3-slice) | `Slider.Draw` |
| `slider_knob.png` | 60x60 | none (fixed sprite) | `Slider.Draw` |
| `toggle_track.png` | 160x64 | 32,0,32,0 (3-slice) | `Toggle.Draw` |
| `toggle_knob.png` | 52x52 | none (fixed sprite) | `Toggle.Draw` |
| `chip.png` | 160x96 | 40,32,40,32 | `UiStyle` Button look `chip` |

`panel.png` is the one deckled (torn-edge) piece, per the producer's brief; every other piece has a
clean rounded edge so it reads crisply at the small sizes buttons, tabs and chips draw at.

## Renderer side

- `src/BeastCraft.Presentation/Ui/NineSlicePatch.cs` — the nine-slice math (engine-neutral, unit
  tests in `Tooling/EditModeTests/Presentation/NineSlicePatchTests.cs`).
- `src/BeastCraft.Game/Ui/UiPainter.cs`'s `NineSlice()` — draws an `ArtSprite` through that math.
- `src/BeastCraft.Presentation/Ui/UiKit.cs` — `UiKit.Enabled`, the one switch between this kit and
  the original code-drawn house style (both stay live in `content/data/Ui/ui-style.json`: every
  panel/button look still carries its old Fill/Outline/Radius, and only draws textured when both
  `UiKit.Enabled` is true and the look names a `Texture`).
- `content/data/Ui/ui-style.json` — the `Texture`/`UnselectedTexture` fields on the Panel/Button
  looks above; the slider/toggle/tab-ribbon pieces are wired directly (`UiKitArt` in `UiKit.cs`),
  since those widgets are not drawn through a named look.
