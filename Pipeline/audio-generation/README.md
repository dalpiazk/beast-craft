# audio-generation

The offline audio pipeline for Beast Craft's shipped music, ambient beds and sound effects: what the
cues are and how loud they must be is in [`docs/design/audio.md`](../../docs/design/audio.md); this
folder turns approved masters into the shipped files and records where they came from. Build-time only:
nothing here ships, runs in the game, or calls a generation API.

**Sources.** Stable Audio (Creator tier) is the *only* sanctioned source for shipped audio: its
explicit commercial terms cover distribution in a paid mobile title. **Re-read the current Stable Audio
Creator terms before generating any audio meant to ship**, and record the tier in its provenance.
**Google Lyria is for free prototyping and temp tracks only and must never reach a shipped build**: it
has no published commercial terms, so any Lyria temp cue is regenerated in Stable Audio before it enters
`content/audio/`. The provenance script refuses a Lyria source and a non-Creator Stable Audio one.

**Masters** are 48 kHz `.wav`, named by the contract (`<category>_<name>_<loop|oneshot>.wav`,
Pipeline/README.md), and stay out of the repository's `content/`. A layered battle theme is one master
per stem, cut from one arrangement at the same length: `mus_battle_<region>_{base,pulse,lead,peak}_loop.wav`.

## Scripts (Windows PowerShell 5.1 or later; ffmpeg with libvorbis on the PATH)

| Script | What it does |
| --- | --- |
| `Normalize-Audio.ps1 -InputDir <masters>` | Writes the shipped files into `content/audio/`: music and ambience loudness normalised to -16 LUFS (two-pass `loudnorm`, true peak -1.5 dBTP) and encoded to OGG Vorbis (`-Quality 4` or `5`); a layered track's stems measured as their full mix and given one shared gain; sound effects peak normalised to -1 dBFS as 16-bit mono 48 kHz WAV. |
| `Write-AudioProvenance.ps1 -File <shipped file> -Source ... -Licence ... -Tool ... -Tier ...` | Records the file's source, licence, tool and tier, prompt, date, approver and SHA-256 in `provenance/<file>.json`. |
| `Test-AudioLoudness.ps1` | Checks every file under `content/audio/`: the naming contract and folder, a cue in `audio-cues.json` for it, 16-bit mono WAV for sound effects (under 5 s, peak -1 dBFS), -16 +/- 1 LUFS and true peak at most -1 dBTP for music and ambience (a layered track as its mix), and a layered track's stems the same length, rate and channels. Exit 0 pass, 1 a failure, 2 when ffmpeg is missing and files needed measuring; with no files it passes. Ready for CI once audio lands. |

The flow: generate (Stable Audio, Creator tier) -> review by ear -> `Normalize-Audio.ps1` -> listen
again -> `Write-AudioProvenance.ps1` per file -> add or check the cue in
`content/data/Audio/audio-cues.json` -> `Test-AudioLoudness.ps1` -> commit (audio is stored with Git LFS).

Prompt templates and per-cue configs (mood, instrumentation, tempo, length) land here when generation
starts.
