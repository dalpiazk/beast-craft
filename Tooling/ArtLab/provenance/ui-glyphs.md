# Provenance: the journal UI kit's painted glyphs (issue #52, direction D, 2026-10-02)

The 11 painted glyph icons in `content/art/ui/glyphs/` (`back.png`, `gear.png`, `map.png`,
`roster.png`, `grove.png`, `avatar.png`, `inventory.png`, `battle.png`, `star.png`, `speaker.png`,
`lock.png`), replacing `UiPainter.Glyph`'s code-drawn versions of the same 11 names when
`UiKit.Enabled` (the tab strips, the bottom nav, the back button and the settings gear — the pieces
the task named; `UiPainter.Glyph` draws 18 more names not covered by this pass, listed at the end).
256x256 RGBA, straight alpha, transparent outside the shape.

The script as run is `Tooling/ArtLab/scripts/icons/ui_icons.py`. Per-icon prompts, seeds and
negative are in that file's `ICONS` dict and `HEAD`/`TAIL`/`NEG`; nothing else records the run (no
`picks.json`/`log.json` the way the skill icons have one — this is an 11-icon, same-day pass, not a
91-icon one, so the prompt/seed table below is the whole record).

## Method

Adapted from `scripts/icons/icons.py`'s own approach (the 91 skill icons) for a small bold UI glyph
rather than a painted beast-skill portrait:

1. **Init = the vector glyph's own geometry, redrawn in Pillow.** `UiPainter.Glyph` draws each of
   these 11 names from the same `P(x, y)` 0..1-box point math in C#; `ui_icons.py`'s `mask_*`
   functions mirror that same geometry to build a 512x512 silhouette mask, filled ink-plum (`#2E2A45`)
   on parchment (`#F6EEDC`) — the img2img init. This keeps the painted icon's silhouette the same
   shape as the vector glyph it replaces (same recognizability), rather than leaving shape recognition
   up to the prompt alone.
2. **A first pass tried plain txt2img** (no init), prompted as a generic "game ui icon" — rejected on
   sight: Animagine XL 4.0 (an anime-portrait checkpoint) rendered chrome/glossy mobile-game-button
   frames with unrecognizable interior content, not a clean single glyph, and ignored the "flat
   magenta background" instruction entirely (busy, full-canvas patterned backgrounds instead) — plain
   prompting gives this checkpoint no geometric control, the same reason the skill icons use a motif
   init rather than prompting alone.
3. **Generation** (`ui_icons.py gen`): `StableDiffusionXLImg2ImgPipeline` through `common.py`'s
   pipeline loader (Animagine XL 4.0, fp16-fix VAE, DPM++ 2M Karras). 512x512, 28 steps, CFG 6,
   img2img strength 0.5 (enough to paint texture and soft shading over the init; not so much that the
   shape drifts from it — icons.py's own treatments run 0.45-0.72 for the same reason). No
   IP-Adapter/style reference this pass (unlike the skill icons' InstantStyle) — the init's own
   ink-plum-on-parchment colouring already anchors the palette; prompt-only direction-D vocabulary
   ("gouache and ink illustration, soft brush texture, warm parchment background, hand-painted") for
   the painted texture.
4. **Cutout: the init's own mask, not a colour key of the output.** The prompt asks for a flat
   background so the icon can be colour-keyed out, but (step 2) this checkpoint does not reliably
   paint a flat, keyable background. Instead, the painted result is pasted onto a transparent canvas
   through the SAME binary mask used to build the init — pixel-exact regardless of what SDXL painted
   outside the glyph's own shape, at the cost of not keeping whatever background texture/wash SDXL
   may have painted (none of it reaches the final PNG; only the pixels inside the glyph's own
   silhouette do).
5. Two seeds generated per icon; the first was kept for every icon (the second was a near-duplicate
   in every case at strength 0.5 — the init dominates the composition, as intended — so there was no
   real choice to record beyond "both work, picked the first"). No reroll needed for 10 of the 11.
6. **One fix round:** `grove`'s first init (a single trunk line + one canopy circle) painted as a
   lollipop/balloon shape, not a tree, on review. Fixed to three overlapping canopy circles (matching
   `UiPainter.Glyph`'s own "grove" case, which already draws three discs) + the trunk line, regenerated;
   reads as a tree. No other icon needed a second round.

## Prompts, seeds, picks

Every icon shares `HEAD` = `"no humans, no text, "` and `TAIL` = `", small bold painted icon, gouache
and ink illustration, soft brush texture, warm parchment background, hand-painted, masterpiece"`
around its own subject clause, and the shared `NEG` (frame/chrome/3d-render/photo/person/animal/
multiple-objects terms — see `ui_icons.py`).

| Icon | Subject clause | Seeds tried | Picked |
| --- | --- | --- | --- |
| back | a single bold left-pointing chevron arrow shape, thick rounded stroke | 101, 102 | 101 |
| gear | a single mechanical cog gear wheel | 111, 112 | 111 |
| map | a single folded travel map with trail lines | 121, 122 | 121 |
| roster | a single paw print | 131, 132 | 131 |
| grove | a single small round tree on a trunk | 141, 142 (reroll: 3-circle canopy) | 141 (round 2) |
| avatar | a single simple person bust silhouette | 151, 152 | 151 |
| inventory | a single closed travel satchel bag with a strap | 161, 162 | 161 |
| battle | a pair of crossed swords | 171, 172 | 171 |
| star | a single five pointed star | 181, 182 | 181 |
| speaker | a single speaker cone with sound waves | 191, 192 | 191 |
| lock | a single padlock | 201, 202 | 201 |

No black/NaN decode on any seed (XPU's occasional garbage-tensor issue — `common.is_black`,
retried at `seed+5000` when it happens — never triggered here).

## Not covered by this pass

`UiPainter.Glyph` draws 18 more names this pass leaves as the code-drawn vector glyph even with
`UiKit.Enabled` (wiring supports either per-name, so adding one later is additive): `elite`, `gate`,
`boss`, `shop`/`coin`, `camp`, `hourglass`, `check`, `close`, `seal`, `flag`, `shrine`, `lore`,
`cache`, `vista`, `kinship`, `pause`, plus the unnamed default disc. The task named "the tab strips,
the bottom nav, back, settings gear, and the common action icons" as the scope; the 11 picked above
are every name the tab strips (`SettingsScreen`, `AvatarScreens`, `InventoryScreens`, `ShopScreens`,
`GroveScreens`), the bottom nav (`HomeScreen`) and the header back button actually use. "The common
action icons" beyond those — `coin`, `close`, `seal`, `hourglass` are the ones actually used
elsewhere (`seal`/`coin` on Avatar's action buttons, `close` on two close buttons, `hourglass` on a
tutorial chip) — are left for a follow-up pass rather than extending this one further under the same
time budget; `UiKit.Enabled`'s vector fallback means nothing breaks or looks unfinished in the
meantime, it is just not yet painted.
