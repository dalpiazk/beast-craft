# Performance and memory (#64)

What Beast Craft must hold to on its minimum-spec phone, where the heavy moments are, how to measure
them, and what has been measured so far. Decision this follows (docs/design/decisions.md): **the
minimum-spec device is the Samsung Galaxy A35 class** (Exynos 1380: 4x Cortex-A78 and 4x Cortex-A55,
Mali-G68 MP5 GPU, 6-8 GB RAM, a 1080x2340 screen at up to 120 Hz). Performance and memory are budgeted
and tested against it.

Nothing in this document has been measured on a device yet. The numbers under "Desktop baseline" come
from a laptop and are **not** the device; they show where the game's own work stands, as a starting
point for the device pass.

## Budgets (Galaxy A35 class)

| What | Target | Floor or cap | Notes |
| --- | --- | --- | --- |
| Frame time | 16.6 ms (60 fps) | 33 ms (30 fps), never below in a worst-case battle | p95 over a battle, not the mean. The game draws at 60 fps; it does not ask for 120 Hz. |
| CPU per frame (update + draw submit) | 8 ms at p95 | 12 ms at p99 | Leaves room for the GPU, the audio thread and thermal throttling over a long session. |
| Draw calls (batches) per frame | 150 | 300 | Mali-G68 handles a few hundred GL draws a frame at this fill rate; the sprite batcher keeps them low by atlas. |
| Texture memory resident | 192 MB | 256 MB | Art is loaded as uncompressed RGBA (4 bytes a pixel, a third more with mipmaps). |
| Managed allocation in a battle | 16 KB a frame | 64 KB a frame | Fewer gen0 collections, and no gen2 collection mid-battle. |
| App memory (total PSS) | 450 MB | 600 MB | Stays clear of the low-memory killer on a 6 GB phone with other apps open. |
| Cold start to title | 3 s | 5 s | Content JSON, the atlas and the font. |

The budgets are this pass's working numbers. The device pass confirms or moves them.

## Worst cases

These are the scenes to measure, heaviest first.

1. **The biggest battle.** 24 enemies (the lineup limit) plus a team of 6: 30 units on a Large arena
   (11 x 15 hexes, 165 cells) with obstacles and its painted backdrop, every unit with its health bar,
   status icons and turn-order portrait. A post-game horde (r11) or `--lineup` with 24 entries.
2. **Battle VFX at speed x3.** Skill flipbooks, hit-stop, screen shake, hit flashes, floating numbers
   and area highlights, with turns three times as fast. Effects on Full.
3. **The region map.** Every node, path dot, label, fog cell and point of interest is its own sprite
   (about 2,000 sprites a frame).
4. **Scrolling lists with art.** The roster at ten species, the Inventory and the wardrobe while they
   scroll.
5. **The first frame of a battle.** Backdrops load on first use (the `backdrop` category of the atlas),
   so a battle's first frame pays for decoding one large image.

## Measuring

### In the game: the frame-time overlay

A debug overlay in the top-left corner (`BeastCraft.Game.Diagnostics.PerfOverlay`, numbers from
`BeastCraft.Presentation.Diagnostics.FrameStats`):

- `frame`: the time between frames, what the player sees (vsync included), with its rolling p95 over
  the last 240 frames (about four seconds) and the worst of them.
- `cpu`: the time from the frame's first update to the end of its draw, with its rolling p95. The
  headroom: how much of the frame the game's own code uses.
- `gc0`, `gc2`: garbage collections since the start, and the bytes allocated in the last frame.
- `draws`, `sprites`, `tex binds`: the GPU device's own counts for the last frame
  (`GraphicsDevice.Metrics`).
- `art textures`: the atlas's loaded art in MB, as uploaded.

It is compiled into Debug builds, and into a Release build only when asked, so a shipping build never
has it:

```
dotnet build src/BeastCraft.Desktop/BeastCraft.Desktop.csproj -c Release -p:PerfOverlay=true
```

Then `--perf-overlay` shows it from the start and **F3** toggles it. `--perf-seconds S` prints a
summary of the whole run (frame and CPU mean, p50, p95, p99, worst, and the shares over 16.6 ms and
33 ms) to the console after S seconds and exits; a battle started that way plays by itself, and the
run keeps full speed when its window is not focused. Scripted screenshots and the walkthrough show
it only when `--perf-overlay` is given, so the usual renders never change.

On Android the overlay is in a Debug build (or a Release build with `-p:PerfOverlay=true`), but there
is no switch for it yet: no command line and no F3. A debug gesture to show it is a follow-up. This
part has not been built or run on Android.

### On a device: Perfetto

System-wide traces: CPU scheduling and frequency per core (to see the big cores throttle), the GPU
frequency, the render thread, and memory counters over time.

1. Enable developer options and USB debugging on the phone; connect it and check `adb devices`.
2. Open ui.perfetto.dev in Chrome and choose **Record new trace**, or use the `record_android_trace`
   script from the Perfetto docs.
3. Turn on CPU scheduling details, CPU frequency, GPU frequency, "Frame timeline" and the memory
   counters (process stats). Ten to thirty seconds is enough.
4. Start the recording, play the worst-case scene (a 24-enemy battle at x3), stop, and open the trace.
   Look at the app's main and GL threads: long slices, and gaps where it waited on the GPU.

### On a device: dotnet-trace through dsrouter

Managed CPU samples and GC events from the game's own code.

1. Install the tools: `dotnet tool install -g dotnet-trace` and `dotnet tool install -g dotnet-dsrouter`.
2. Build the Android app with profiling enabled (`-p:AndroidEnableProfiler=true`) and install it.
3. Run `dotnet-dsrouter android` (or `android-emu` for an emulator). It prints the `adb` commands
   that forward the diagnostics port and set the `debug.mono.profile` property on the phone; run
   them, then start the app.
4. In another terminal, `dotnet-trace collect -p <dsrouter's process id> --format speedscope`, play
   the scene, and stop with Enter. Open the `.speedscope.json` at speedscope.app, or the `.nettrace`
   in PerfView or Visual Studio.

### On a device: adb dumpsys gfxinfo

A quick first look, no setup:

```
adb shell dumpsys gfxinfo com.example.beastcraft reset
(play the scene for 10-20 seconds)
adb shell dumpsys gfxinfo com.example.beastcraft
```

It reports frame counts, janky frames and percentiles, plus the process's graphics memory. Its frame
numbers come from Android's own UI renderer, and the game draws through its own GL surface, so the
frame section may say little; the memory section is still useful. For the game's frames, prefer the
overlay or Perfetto. `adb shell dumpsys meminfo com.example.beastcraft` gives the total PSS against
the budget above. (The application id is still the template's `com.example.beastcraft`.)

## Desktop baseline (not the device)

Measured on 2026-09-29 on a laptop: AMD Ryzen 7 5825U, Radeon integrated graphics, 14 GB RAM,
Windows 11, a 60 Hz screen. Release build with `-p:PerfOverlay=true`, a window at its default size,
effects on Full, a 60-second `--perf-seconds` run.

The worst-case battle:

```
BeastCraft.Desktop --lineup <24 enemies> --arena Large --team phoenix,golem,kirin,frost_wyrm,griffin,basilisk
                   --level 100 --enemy-level 100 --speed 1 --perf-seconds 60
```

with the 24 as `swarmling:Fire,stingling:Water,brute:Earth,archer:Air,caster:Light,shaman:Dark,stalker:Fire,swarmling:Water`
three times over.

| Scene | CPU mean | CPU p95 | CPU p99 | Worst frame | Allocated a frame | gc0 / gc2 in the run | Peak draws, sprites a frame | Art textures |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| Worst-case battle, speed x1 | 1.48 ms | 1.82 ms | 4.55 ms | 242 ms | 55.0 KB | 15 / 2 | 178, 1,346 | 86.6 MB |
| Worst-case battle, speed x3 | 1.30 ms | 1.73 ms | 4.61 ms | 225 ms | 46.4 KB | 11 / 2 | 178, 1,346 | 86.6 MB |
| Default demo battle (4 v the r01 boss), x1, 45 s | 0.32 ms | 0.37 ms | 0.58 ms | 102 ms | 4.3 KB | 1 / 1 | 14, 312 | 83.9 MB |
| Region map (r01, fog), idle, 20 s | 1.22 ms | 1.02 ms | 1.74 ms | 335 ms | 18.7 KB | 1 / 1 | 41, 2,093 | 83.9 MB |

What it says:

- **The game's own work is small** on this machine: under 2 ms at p95 in the biggest battle. A
  Cortex-A78 core is a few times slower than a laptop core for this kind of code, so the device is
  likely still inside the 8 ms budget, but only the device can say.
- **The frame interval held at about 31 ms in every scene, the lightest included** (the small default
  demo too), with the CPU under 2 ms. That is this laptop session's presentation rate, not the game's
  cost (most likely its integrated graphics' power saving or the window being in the background). The
  desktop frame interval is not a usable number here; the GPU side is for the device pass.
- **The worst frame is the first**: 100-340 ms, content and a backdrop decoded on first use. A loading
  beat before a battle, or loading the backdrop while the encounter preview shows, would hide it.
  (On the map, that first frame is why the mean sits above the p95.)
- **Allocation is over the target** in the big battle (46-55 KB a frame against 16 KB), inside the
  64 KB cap: about one gen0 collection every four to five seconds. Worth a pass with dotnet-trace's GC events
  on the device before it becomes a hitch.
- **Draw calls peak a little over the target** in the biggest battle (178 against 150), well inside the
  cap of 300. New VFX or UI layers in battle should be checked against it.
- **Art textures are 84-87 MB** now, with a good part of the final art still to come. Every texture is
  uncompressed RGBA; if the finished art passes 192 MB, compressed textures (ETC2 or ASTC) or
  per-region loading are the levers.

## Follow-ups

- The device pass itself: a Galaxy A35 (or its class) with the scenes above, filling a "Device"
  table beside the desktop one.
- A debug gesture to show the overlay on Android.
- Allocation in battle: find and remove the per-frame garbage.
- The first-frame spike: preload the battle's backdrop while the encounter preview shows.
