# Audio and haptics (#56)

How Beast Craft sounds and buzzes: the cues, where their files go and what they are called, how loud
they are, what format they ship in, how battle music layers, and which events pulse the phone. The
code is in place and plays nothing yet: no audio files exist. Each cue plays as soon as its file lands
under `content/audio/`, with no code change.

Decisions this follows (docs/design/decisions.md): **haptics are on by default, with a settings
toggle; light haptics on hits, knockouts and key UI confirms.** No AI at runtime: every sound is a
static file made offline (Pipeline/README.md).

## Cue list

Every cue is listed in `content/data/Audio/audio-cues.json` by id. The code asks for ids only; the
file maps each id to its files, gain and haptic.

| Cue id | Kind | When it plays | Files |
| --- | --- | --- | --- |
| `music.title` | music | the title and save slots | `mus_title_loop.ogg` |
| `music.battle.r00` ... `music.battle.r11` | music, 4 stems | a battle in that region (one theme per region) | `mus_battle_<region>_{base,pulse,lead,peak}_loop.ogg` |
| `ambient.r00` ... `ambient.r11` | ambient | the map and the region's other screens | `amb_region_<region>_loop.ogg` |
| `ambient.grove` | ambient | the Grove | `amb_grove_loop.ogg` |
| `sfx.ui.tap` | sfx | a tap on any button, tab or map spot | `sfx_ui_tap_oneshot.wav` |
| `sfx.ui.confirm` | sfx | a tap on a primary button (Start Battle, Continue, Choose...) | `sfx_ui_confirm_oneshot.wav` |
| `sfx.skill.cast` | sfx | a skill starts (each beat of a turn) | `sfx_skill_cast_oneshot.wav` |
| `sfx.hit.<element>` | sfx | a skill's damage lands, in the skill's element (`fire`, `water`, `earth`, `air`, `lightning`, `ice`, `nature`, `metal`, `light`, `dark`; `neutral` for none) | `sfx_hit_<element>_oneshot.wav` |
| `sfx.hit.crit` | sfx | layered on a hit when one of its targets took a critical hit | `sfx_hit_crit_oneshot.wav` |
| `sfx.ko` | sfx | a unit is knocked out | `sfx_ko_oneshot.wav` |
| `sfx.rewards` | sfx | the results of a won battle open | `sfx_rewards_oneshot.wav` |
| `sfx.level_up` | sfx | the results show a beast or the Beastbinder levelling up | `sfx_level_up_oneshot.wav` |

The title theme is one addition to the cue list in the brief: the title needed a track of its own.

**Which track a screen plays** (`AudioDirector.TrackFor`): the title theme on the title and save slots;
the region's battle theme in battle; the Grove's ambience in the Grove; the region's ambience on every
other screen of an expedition. The results keep the battle's music playing. A screen before any
region is chosen (the starter pick) keeps the title theme. Tracks crossfade over 1.2 s.

**When battle sounds play** (`BattleAudioCues`): off the same timeline as the effects
(`TurnAnimation`), so a sound lands with its flash at any battle speed. Each beat casts as it starts;
one hit in the beat's element sounds at its impact when it damaged anyone (a multi-target beat is one
hit sound, not one per target), plus the crit layer when a target was crit; a knockout sounds at the
impact of the last beat that struck the unit (at the turn's start when burn or poison finished it).
Skipping to the end of a turn plays none of its sounds.

A cue id the file does not list, or a file that is missing, is logged once and plays nothing.

## Naming contract

Files follow the pipeline's contract (Pipeline/README.md, "Audio"): `<category>_<name>_<loop|oneshot>`,
lowercase `snake_case`, no dates or versions.

```
content/audio/music/     mus_<name>_loop.ogg        mus_title_loop.ogg, mus_battle_r01_base_loop.ogg
content/audio/ambient/   amb_<name>_loop.ogg        amb_region_r01_loop.ogg, amb_grove_loop.ogg
content/audio/sfx/       sfx_<name>_oneshot.wav     sfx_hit_fire_oneshot.wav, sfx_ui_confirm_oneshot.wav
```

- A region is named by its id (`r01`), not its display name, so a rename never breaks a file.
- A layered track's stems add the layer to the name: `base`, `pulse`, `lead`, `peak`, in that order.
- A sound effect may have variants for variety: list several files under one cue (`sfx_hit_fire_a_oneshot.wav`,
  `sfx_hit_fire_b_oneshot.wav`); one plays at random.
- `AudioCueLibrary.Validate` (run when the content loads, so a bad name fails the tests) checks every
  name against the contract.

The masters the pipeline starts from are 48 kHz WAV named the same way with a `.wav` extension
(Pipeline/README.md); they stay out of `content/`. The encode step (below) writes the shipped files.

## Loudness

- **Music and ambience: -16 LUFS integrated**, true peak at most -1.5 dBTP, by ffmpeg's `loudnorm` in two
  passes (measure, then apply the measured values with `linear=true`, so the dynamics are kept). The
  loudness check allows -16 +/- 1 LU.
- **A layered track is measured as its full mix.** The stems of one track get one shared gain (the
  gain that brings the all-stems mix to -16 LUFS), never a gain each, or the layers' balance would
  change. So at low intensity (base stem only) a battle theme sits a little below -16 LUFS: that is the
  intended build.
- **Sound effects: peak normalised to -1 dBFS.** Short one-shots have no meaningful integrated loudness;
  their relative level is set per cue by `GainDb` in the cue file (UI taps sit at -6 dB, hits at -3 dB).
- Players then scale everything by the settings: volume (master), music, sound effects, and mute.

## Formats

| Kind | Shipped as | Why |
| --- | --- | --- |
| Sound effects | 16-bit PCM mono WAV, 48 kHz, short (under about 2 s) | loads straight into `SoundEffect.FromStream`, no decode cost at play time |
| Music, ambience | OGG Vorbis, quality 4-5 (about 128-160 kb/s), 48 kHz, stereo | small, decoded as it streams (NVorbis) |

This refines Pipeline/README.md's "compression is a per-platform build step": the encode script
(`Pipeline/audio-generation/Normalize-Audio.ps1`) is that step, and it writes the one shipped format
above for every platform (the desktop and Android hosts both stream Vorbis through NVorbis).

## Layered stems and intensity

A battle theme is 3-4 stems cut from one arrangement, all the **same length, sample rate and channel
count**, looping on one clock:

1. `base`: always playing (pads, bass, the theme);
2. `pulse`: percussion and rhythm;
3. `lead`: the melody lead;
4. `peak`: the climax layer (brass, choir, full drums).

The player reads the same number of samples from every stem per buffer, so they stay sample-aligned;
each loops back to its start at its end, so stems of unequal length would drift (the loudness check
rejects them). `SetIntensity(0-1)` brings the upper stems in one after another, each over an equal
share of the range (`MusicMix.LayerWeights`: with four stems the second fades in over 0-1/3, the third
over 1/3-2/3, the fourth over 2/3-1), and every weight change fades over 1.5 s. In battle the intensity
is a quarter when the fight opens and rises with the share of the enemy side's HP lost
(`AudioDirector.BattleIntensity`), so the music builds towards the last blow. A single-stem track plays
whole at any intensity.

## Haptics

On by default; the settings' Vibration row turns them off (shown only where the host can vibrate).

| Event | Pulse | Android (API 29+) | Android 26-28 / 23-25 |
| --- | --- | --- | --- |
| A hit lands (`sfx.hit.*`) | Light | `EFFECT_CLICK` | 20 ms one-shot / legacy 20 ms |
| A unit is knocked out (`sfx.ko`) | Light | `EFFECT_CLICK` | 20 ms |
| A primary button confirms (`sfx.ui.confirm`) | Selection | `EFFECT_TICK` | 10 ms |
| (unused, reserved) | Medium | `EFFECT_HEAVY_CLICK` | 30 ms |

Other taps, casts, crits, rewards and level-ups do not buzz. A cue's pulse is set by `Haptic` in the
cue file, so the mapping can change without code. At most one pulse every 60 ms: an area hit or a
burst of hits is one buzz, not a rattle.

The Android host implements it (`AndroidHaptics`, with the `VIBRATE` permission); the desktop has no
haptics. **iOS** (no project yet, #63): the same `IHaptics` maps to `UISelectionFeedbackGenerator`
(Selection) and `UIImpactFeedbackGenerator` with `.light` and `.medium` styles, prepared a moment before
use; nothing needs a permission.

## Settings

`PlayerSettings` (docs/design/progression-and-saves.md, "Player settings"): `MasterVolume`,
`MusicVolume`, `SfxVolume` (0-100, 100 by default), `Muted` (off) and `Haptics` (on). Additive, so an
older settings file reads them as their defaults and the settings format stays at version 1. The
settings modal adds Volume, Music, Sound effects (each tap steps down a quarter, and from 0 back to
100), Mute, and Vibration (where the host has haptics). Music plays at master x music, sound effects at
master x effects, both silent when muted; the players follow the settings every frame.

## Code

- `BeastCraft.Presentation.Audio` (engine-neutral): the seams `IAudio` (sound effects by cue id),
  `IMusicPlayer` (one looping track, crossfades, layer intensity) and `IHaptics` (`Light`, `Medium`,
  `Selection`), each with a null implementation; `AudioCueLibrary` over `audio-cues.json` (registered in
  `PresentationJsonContext`); `AudioDirector`, the one place the game asks for sound (cues and their
  pulses, taps, the track per screen, the volumes); `BattleAudioCues` (a turn's timed cues);
  `MusicMix` (the volume, layer, fade and PCM arithmetic).
- `BeastCraft.Game.Audio` (MonoGame): `MonoGameAudio` (WAV through `SoundEffect.FromStream`, up to four
  pooled instances per file) and `MonoGameMusicPlayer` (OGG stems decoded by NVorbis into one
  `DynamicSoundEffectInstance` per track, mixed on the game thread). A device without sound, or a
  missing file, stays silent (logged once). Scripted runs (screenshots, the walkthrough) use the null
  players, so they stay deterministic.
- The game host creates the director, plays a tap's cue from the input routing, and changes the track
  when the screen stack's top changes; the battle screen plays its turn's cues and sets the intensity;
  the results play the reward and level-up stings.

## Making the files

`Pipeline/audio-generation/` holds the scripts (PowerShell, run by hand; ffmpeg must be on the PATH):

- `Normalize-Audio.ps1`: masters in, shipped files out (two-pass loudnorm to OGG for music and ambience,
  one shared gain for a track's stems; peak normalise to 16-bit mono WAV for sound effects).
- `Write-AudioProvenance.ps1`: records where each shipped file came from (source, licence, tool and
  tier, date, hash) in `Pipeline/audio-generation/provenance/`.
- `Test-AudioLoudness.ps1`: checks every shipped file (naming, format, loudness or peak, stem lengths);
  exits non-zero on a failure, so CI can run it once files exist.

**Before generating any shipped audio, re-read the Stable Audio Creator tier's current terms** (the only
sanctioned source for shipped audio). **Google Lyria output is prototype-only and never ships.** No
generation API is called by anything in this repository.
