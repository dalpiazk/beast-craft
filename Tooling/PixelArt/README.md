# Beast Craft pixel art (placeholder pipeline)

Code-generated pixel art: sprites are authored as text grids (one character per palette colour)
and built to PNG by `build.py`. LOCAL-ONLY TOOLING: CI never runs it; its output is committed.

## Regenerate
```
python -m venv Tooling/PixelArt/.venv
Tooling/PixelArt/.venv/Scripts/python -m pip install -r Tooling/PixelArt/requirements.txt   # once
Tooling/PixelArt/.venv/Scripts/python Tooling/PixelArt/build.py
```
(`bin/python` instead of `Scripts/python` on Linux/macOS.) Pillow is pinned to **12.3.0**: with
it, `build.py` regenerates the committed PNGs and manifest **byte for byte** (checked by building
twice and comparing hashes; `git status` stays clean). Another Pillow (or its bundled zlib) can
compress the same pixels to different bytes; the pixels themselves are integer-only and
platform-independent. `build.py` fails loudly on an unknown colour char or an over-wide row.

## Outputs
| Path | What | Committed |
| --- | --- | --- |
| `content/art/pixel/<name>.png` | 1x sprite; multi-frame = horizontal strip | yes (Git LFS) |
| `content/art/pixel/pixel-art-manifest.json` | the art manifest, schema v2: every sprite (file, frame size, frames, pivot, pixels per unit, filter, premultiplied, category, ArtKey, tint, clips) + the palette | yes |
| `Tooling/PixelArt/preview/` | x8 previews, GIFs, tiling checks, map mock, `contact_sheet.png` | no (ignored) |

The desktop app (`src/BeastCraft.Desktop`) copies `content/art/pixel` to `Content/art/pixel` beside its executable and
loads the PNGs at runtime (`Texture2D.FromStream`), indexed by the manifest. The VFX library
(`content/data/Vfx/vfx-library.json`) names sheets by sprite `Name` and colours by palette char, and
`VfxLibraryValidator` checks both against this manifest.

## Files
| Path | What |
| --- | --- |
| `palette.json` | 31 colours + transparent, shading ramps, element -> colours map |
| `STYLE.md` | style rules (outline, light, ramps, dithering, sizes, rarity) |
| `sprites/*.txt` | the source art (edit these) |
| `build.py` | text grid -> PNG, auto-outline, auto rim-shade, generators (fx bursts, hex tiles), manifest, previews |
| `mirror.py` | authoring helper: mirror a 16-col left half into a symmetric 32-col grid |
| `requirements.txt` | the pinned Pillow |

## Content
- Beasts (32x32), the whole roster: Phoenix (+ 2-frame idle), Leviathan, Golem, Griffin,
  Thunderbird, Frost Wyrm, Treant, Tarasque, Kirin, Basilisk. The seven after the style test are
  simple placeholders in the same style.
- Enemies (32x32): Gloamed Brute, Champion, Giant, Swarmling. The others (archer, caster, shaman,
  stalker, stingling) are **alias** entries (`# alias:` + `# tint:`, no PNG of their own): the
  manifest lists them under their own ArtKey with another sprite's File and a Tint, so the data
  (`enemy-library.json` ArtKey) already names final art that does not exist yet.
- Every species and enemy names its art by `ArtKey` in the data (`beast/<id>`, `enemy/<id>`);
  the viewer looks the key up in the manifest, and `ArtReferenceValidator` (run on content load
  and by the tests) holds every key to an entry.
- Hex tiles (32x36 pointy-top; columns 32 px apart, rows 27 px apart): grass, scorched rock, and a
  white mask and outline the game tints (team zones, highlights).
- FX: `fx_fire_burst` (8-frame flipbook), `fx_hit_burst` (6 frames, white, tinted per element),
  particles `fx_ember` and `fx_spark` (white, tinted).
- VFX layer textures (VFX schema v2; white/grey, tinted and sized in game): `fx_ring` (shockwave
  ring, generator `ring`), `fx_aura_ring` (ground aura ellipse), `fx_glow` (soft disc, generator
  `disc`), `fx_scorch` (ground decal, generator `blob`), `fx_ray` (radial-burst streak, pivot at
  its tail) and `fx_glyphs` (4 rune frames).
- Status icons (9x9): `icon_stun`, `icon_shield`, `icon_burn`, `icon_poison`, `icon_taunt`,
  `icon_buff`, `icon_debuff`, drawn above a unit's HP bar while the status lasts.
- Items, map tiles and the camp marker from the style test.

Generated kinds (`fx` bursts, `hex` tiles) have no grid rows: their header drives an
integer-only generator in `build.py` (see its docstring).
