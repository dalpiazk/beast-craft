# Producer decisions

The standing product decisions that later work builds on, one line of rationale each. Newest first.
The detailed designs live in the other docs in this folder; this log only records what was decided,
when, and which issue carries the work. Change a stance by adding a new dated entry, not by editing an
old one.

## 2026-09-29

### Platform and product (#48)

- **Localisation.** Build the string-key layer (#50) and **ship English only**. Which languages to add
  is decided after launch. The full localisation pipeline (#57) is deferred.
- **Monetisation.** **Undecided, left open.** Nothing in the current build depends on it yet. The
  look-token economy keeps assuming no real-money purchases.
- **Telemetry.** **Opt-in anonymous analytics plus crash reports**, both behind a consent screen. Nothing
  is sent before the player agrees. The privacy policy and the Play data-safety form must declare both
  (#62).
  - **Built (#62 slice):** `PlayerSettings.AnalyticsConsent` and `CrashReportConsent`, both off by
    default; a one-time consent screen (`ConsentModal`) after the first starter pick and before play,
    shown until answered, with both choices starting off; a settings row for each. The code talks to
    engine-neutral `IAnalytics` and `ICrashReporter` (do-nothing `NullAnalytics` and
    `NullCrashReporter` for now) only through `TelemetryGate`, which initialises a provider only while
    its consent is on and drops everything otherwise; tests enforce it (`ConsentTests`).
  - **Provider: to be decided.** No analytics or crash-reporting SDK is chosen or included, so nothing
    leaves the device yet. Whichever is chosen, the privacy policy and the Play data-safety form must
    declare both kinds of data before it ships.
- **Haptics.** On by default, with a settings toggle. Light haptics on hits, KOs and key UI confirms
  (#56).
- **Gamepad.** Not supported. The game is touch-first; desktop uses mouse and keyboard.
- **Tablets.** Portrait stays locked on every device. Tablets get the portrait canvas letterboxed with
  painted side art (the side art is still to be made).
- **Minimum-spec device.** **Samsung Galaxy A35 class** (Exynos 1380, 6-8 GB RAM). Performance and
  memory are budgeted and tested against it (#64; the budgets, worst cases, profiling and a desktop baseline are in
  performance.md).
- **Playtesting.** The producer plays the builds for now. External testers come later, closer to
  release.

### Grove (#42, #43, #45)

- **Grove battle bonus: none.** The Grove stays strictly non-combat (#42). No Grove feature changes
  battle numbers.
- **Grove map influence: both A and B** (#43):
  - A: ambient region dressing, folded into the region template (#61).
  - B: non-combat points of interest revealed by expedition stories.
  - C, the Trader cosmetics, stays as it is.
- **Grove defaults:** every default in #45 is accepted as shipped.

### Art and presentation

- **Art direction (#49):** the five pillars and the palette are adopted for all new art. See
  [`docs/art/art-brief.md`](../art/art-brief.md), section 1.
- **3D: mini-spike only** (#55). No 3D work beyond a small feasibility spike.

### Post-game

- **Hard mode** is replayable across the regions after r11 (#60). The producer writes the narrative arc.

## Still open

- **Monetisation** (#48): premium, free with an unlock, or something else. Revisit before any store
  listing work.
- **Tinted line work** (#49): decided after a one-beast test pass (see the art brief).
- **Launch languages** (#48, #57): decided after launch.
