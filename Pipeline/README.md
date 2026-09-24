# Beast Craft — Offline Asset Pipeline

**This folder is build-time only. Nothing here ships, and nothing here is ever
invoked at runtime.**

The Beast Craft client makes **zero AI calls at runtime**. Every sprite sheet,
music loop, ambience bed and sound effect in the shipped build is a static file
that was generated *offline*, on a developer machine, reviewed by a human, and
then committed under `content/`. The pipeline in this folder is the set of
prompt templates, configs and (later) scripts used to produce those files. The
game has no dependency on it — you can delete this folder and the game
still builds and runs.

---

## Tooling and licensing

| Stage | Tool | Tier / license | Shipped? |
| --- | --- | --- | --- |
| Character / creature / environment / key art | **Gemini — Nano Banana 2** | Paid API tier | Yes |
| Music, ambient loops | **Stable Audio** | Creator tier — explicit commercial terms | Yes |
| Sprite cleanup (background removal, trim, atlasing prep) | **rembg** + **GIMP** | Open source, self-hosted | Yes (tooling only) |
| Throwaway audio prototyping | **Google Lyria** | Free prototyping only | **No** |

### Licensing rules (non-negotiable)

- **Stable Audio Creator tier is the only sanctioned source for shipped audio.**
  It carries explicit commercial terms that cover distribution in a paid mobile
  title.
- **Google Lyria is for free prototyping only. Never ship Lyria output.** There
  are no published commercial terms for it, so any Lyria-derived audio is a
  licensing liability. Use it to block out a mood or temp-track a scene, then
  regenerate the final asset in Stable Audio before it enters
  `content/`.
- Gemini image generation runs on the **paid API tier**, not the free tier, for
  the commercial-use terms.
- All art is **original IP**. Prompts must not name living artists, studios,
  franchises or characters. Describe the target look by its qualities
  (painterly, soft-edged watercolor, warm natural light, hand-inked linework),
  never by reference to a rights-holder.
- Every generated asset gets a human review pass before it is committed.

---

## Where output goes

Generated and cleaned assets land in the game's `content/` tree, never in `Pipeline/`:

```
content/art/characters/     player + NPC sprites
content/art/creatures/      collectible creature sprites
content/art/environments/   backdrops, tiles, props
content/art/ui/             icons, frames, buttons
content/art/keyart/         promo / store / title art

content/audio/music/        score, battle and theme loops
content/audio/ambient/      environmental beds
content/audio/sfx/          one-shots
```

Raw, unreviewed generations stay out of the repo (see `.gitignore`: `out/`,
`tmp/`, `_scratch/` under `Pipeline/` are ignored). Only the cleaned, approved
asset is committed.

---

## Naming conventions

These are the contract between the pipeline and the game. Loaders, manifests
and data references will assume them, so treat a
rename as a breaking change.

### Art

```
<category>_<subject>_<variant>_<state>.png
```

- `category` — `char` | `crt` | `env` | `ui` | `key`
- `subject` — the stable slug for the thing itself (`emberfox`, `mira`,
  `hollowmarsh`)
- `variant` — a visual variation: evolution stage, palette, costume, biome
  skin. Use `base` when there is only one.
- `state` — pose / animation state / UI state (`idle`, `attack`, `hurt`,
  `portrait`, `pressed`). Use `static` for art with no state axis.

All lowercase, `snake_case` within a segment, no spaces, no dates, no version
suffixes (git is the version history).

Examples:

```
crt_emberfox_stage1_idle.png
crt_emberfox_stage3_attack.png
char_mira_travelcloak_portrait.png
env_hollowmarsh_dusk_static.png
ui_skillframe_rare_pressed.png
key_titlescreen_base_static.png
```

Multi-frame animation ships as a sprite sheet under the single `_state` name
(`crt_emberfox_stage1_idle.png` is the whole idle strip); frame slicing is
metadata (a manifest), not a filename concern.

### Audio

```
<category>_<name>_<loop|oneshot>.wav
```

- `category` — `mus` | `amb` | `sfx`
- `name` — stable slug for the cue (`battle_tension`, `marsh_night`,
  `creature_capture`)
- final segment is literally `loop` or `oneshot` — it tells the importer
  whether the clip is seamless

Examples:

```
mus_battle_tension_loop.wav
mus_overworld_theme_loop.wav
amb_marsh_night_loop.wav
sfx_creature_capture_oneshot.wav
sfx_ui_confirm_oneshot.wav
```

Deliver audio as `.wav` (48 kHz, 16-bit or 24-bit). Compression to Vorbis/AAC is
a per-platform build step — do not pre-compress source files.

---

## Subfolders

- [`art-generation/`](art-generation/README.md) — Gemini prompt templates and
  per-category generation configs.
- [`audio-generation/`](audio-generation/README.md) — Stable Audio prompt
  templates and loop/one-shot configs.
- [`sprite-cleanup/`](sprite-cleanup/README.md) — rembg / GIMP post-processing
  recipes that turn a raw generation into an import-ready sprite.

## Status

Structure and conventions only. Generation scripts land in a later step; for now
these folders define *where things go and what they are called*, not *how they
are made*.
