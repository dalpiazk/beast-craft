# Screens: architecture, UI toolkit, and how to add a screen

Screens PR 1 (the core loop) turned the battle viewer into a game: a stack of screens with modals
over it, a small retained UI toolkit, and view-models that drive the campaign's Core rules. This
note is the map of that code. The plan and the producer decisions behind it (bottom-nav tabs, the
spatial map, free full scouting, one results screen, a custom toolkit) are in the game-screens
design pass; the decisions are summarised at the end.

## The loop

```
Title ──Continue / New Game──▶ Home (Map tab) ──tap a location / Next battle──▶ Encounter
  ▲  Back: "Leave?"             │  idle chip, header, bottom nav                   │ Start Battle
  │                             │  (Roster ─▶ beast detail; Grove, Avatar,         ▼
  └──────── Back ───────────────┘   Inventory: "coming soon")                    Battle ──decided──▶ Results
                                ▲                                                                   │
                                └─────────────────────── Continue (or auto-advance) ────────────────┘
```

- **Starter pick** (`StarterPickScreen`, `StarterPickViewModel`): New Game's first beast, any of the
  ten (illustrated portraits, stance and element, a personality blurb, the stance explainer), then
  Hearthglen; or "Skip the tutorial": the same picker three times, one stance at a time, then Verdant
  Hollow. Hearthglen's own screens — the trial pick (`TrialPickModal`), the Keeper's dialogue box
  (`StoryModal`), the camp (`CampModal`, every region), the tutorial hints (`HintModal`, anchored to a
  widget, pausing a battle) and the way on (`RegionCardModal`) — are in [area-zero.md](area-zero.md).
- **Title** (`TitleScreen`, `TitleViewModel`): Continue (primary when a save exists; it claims the
  idle rewards and toasts them), New Game (asks before replacing a save; then the starter pick), Settings. A save that
  could only be restored from its `.bak` says so; one that cannot be loaded says why. Back asks
  before quitting.
- **Home** (`HomeScreen`, `HomeViewModel`, `MapViewModel`): the bottom nav — **Map, Roster, Grove,
  Avatar, Inventory** (Map and Roster work; see "Roster and visibility" below) — over the region map. The map is *spatial*: the stage's
  node map (rows and lanes, the internal pacing model) is laid out by `MapLayout` as places on a
  painted-style meadow (placeholder: a soft gradient and blobs), joined by winding trails; it is
  never drawn as a graph. Locations show their type (Battle, Den, Pass, Lair, Trader, Camp) and
  state (reachable, cleared, where you stand, locked, bypassed). The header shows the region, its
  level band, the stage, the seal's progress and the binding limit. The idle chip claims idle
  rewards in one tap; **Next battle** opens the recommended location with the last team picked.
  Trader and Camp locations say "coming soon" (Shop/Camp/Idle PR).
- **Encounter** (`EncounterScreen`, `EncounterViewModel`): the full preview, free — every enemy
  (type, element, stance, level, count), the arena and the battlefield it will really be fought on
  (the same seeded layout pick the session makes) — then the party (up to 3 beside the Beastbinder, deployment order), one
  consumable, the team-suggestion banner after 3 losses there (dismissible; the settings toggle is
  respected), and Start Battle.
- **Battle** (`BattleScreen`): the old viewer, now one screen. A campaign battle
  (`NodeBattle`, through `CampaignRules` and `BattleSession`, fought in the node's region so its
  backdrops and obstacles apply) fields the chosen beasts **and the Beastbinder** (the avatar, as
  the difficulty was calibrated: `CampaignAvatar` mirrors the balance simulator's avatar — its
  default arts and passives, its stat fixture — off the board, in the turn order, its arts playing
  on their targets, shown as a portrait badge with its art cooldowns at the board's foot). It plays
  by itself at the saved speed; Back offers to skip to the
  result; once decided, Continue hands it back.
- **Results** (`ResultsScreen`, `ResultsViewModel`): one consolidated summary — XP bars (before →
  after, level-ups), the bench's share, gold, drops, the first-clear bonus, XP banked at the
  limit, and what the map made of it (cleared, a stage or region won with its seal, or the retry
  note after a loss). Continue returns to the map, or with *Auto next battle* on after a win,
  straight on to the next encounter.

## Roster and visibility (the Theorycrafter's screens)

From the "Theorycrafter" persona review: transparent stat matrices, predictable targeting rules,
visible mechanics. Everything here is presentation: every number is read from the Core rule it
describes (never re-derived), and battle rules and the balance simulator are unchanged (the tuned
report and the difficulty table regenerate byte-identical).

- **Roster tab** (`RosterPage`, `RosterViewModel`): every owned beast as an illustrated card (level,
  stance, element; a leaf check when it is in the last party), sortable (Joined, Level, Name,
  Element, Stance: the Sort chip cycles), then a dark silhouette for every species not yet found,
  "Found through Kinship" (the compendium's seed). A card opens the beast's detail screen.
- **Beast detail** (`BeastDetailScreen`, `BeastDetailViewModel`), three tabs:
  - *Stats*: HP, Atk, Def, SpA, SpD, Speed and Crit as base (the species at its level,
    `StatCalculator.GetBaseStatsAtLevel`), gear and total (`StatCalculator.ComputeStats`), each
    worn piece's bonuses; **turns per 100 gauge ticks** (`TurnManager.FillRateForSpeed` over
    `ActionThreshold`: the square-root ATB fill) for the beast, the rest of the party and the
    level-matched average enemy (the mean over the enemy library at the beast's level; Speed is
    never scaled by the difficulty multiplier); the **element chart both ways** (its attacking
    element, its first equipped damaging skill's, against each of the ten; each of the ten
    against it); **crits** (the clamped chance, the multiplier, the expected factor); the
    **level-gap curve** for enemies 5 levels below to 5 above, dealt and taken
    (`DamageFormula.GetLevelMultiplier`).
  - *Skills*: the three slots, each a card with its **targeting rule in words**
    (`SkillCard.TargetingRuleText`, generated from `TargetShape`, `TargetSide`,
    `TargetingCriterion`/`TargetingOrder`/`TargetingStat` and `Range`, e.g. "Targets the enemy
    with the lowest HP% within 3 hexes.") and how **taunt** overrides it (`TauntRuleText`: only an
    enemy-side single-target or line skill is redirected), its tags, cooldown, power lines, level,
    tier, XP and the next breakthrough's gate. *Change* puts another learned skill in the slot (a
    skill already in another slot swaps: `SkillBook.Equip`/`SwapSlots`; the last skill cannot be
    taken off); *Upgrade* lists every material with its tier and XP: *Train* feeds one
    (`SkillProgression.ApplyMaterial`, refused at the tier's cap), *Break through* at the gate
    needs the gate's material tier or better (`TryBreakthrough`), each spending one. The kit's
    other skills follow, learned (Equip) or taught by a skill tome from a level.
  - *Gear & bonds*: each slot's worn piece (Take off) and the player's other pieces for it (Equip,
    or why not: worn by another beast, level too low; `GearRules`); the stance's behaviour (its
    glossary definition); the bonds it takes part in, with the owned partners that count and the
    tier the current party reaches; the looks it wears (editing is the wardrobe's, later).
  Every change autosaves (`AutosaveReason.BeastEdit`).
- **Element chart** (`ElementChartScreen`, `ElementChartViewModel`): the 10x10 matrix of
  `ElementChart` (rows attack, columns defend), Strong x2, Mild x1.25, Weak x0.5, Neutral, the team's
  elements' rows and columns highlighted. From the glossary, the detail screen and the encounter.
- **Glossary** (`GlossaryScreen`): every term (combat first: ATB, gauge, level gap, execute,
  variance, element chart, crit, heal; then statuses, stances, passives), opened at a term by the
  screens' "?" buttons, with the way to the element chart. The terms are data
  (`content/data/Glossary/glossary.json`); a test keeps the level-gap, variance and gauge numbers
  true to the Core constants.
- **Encounter preview additions** (`EncounterInsightView`): a **level-gap indicator** on every
  enemy card (the gap to the chosen team's mean level, and "You x1.12 - It x0.88": the multiplier
  on the team's hits and on the enemy's), a colour-coded **element matrix** (team beasts by the
  enemies' distinct elements, each cell dealt over taken, green when it favours the team), and
  **each enemy type's skills** with their targeting rules.
- **Battle log** (`BattleLogViewModel`, `BattleLogModal`): rebuilt from the battle's own records —
  the turn results, each `DamageHit`'s `DamageBreakdown` and each `AppliedEffect`'s amount (both
  recorded by Core as presentation data: no draws, no outcome change; a fingerprint test pins whole
  battles against `main`) — as one line per event: turns, hits, heals, shields, statuses and stat
  changes, damage over time, bond activations and reactions, passives, the Beastbinder's arts,
  falls. A hit opens its **damage breakdown**: skill power, attack vs defence, the
  power x A²/(A+D) base, element, crit (and its multiplier), the variance roll, the level gap,
  execute, the final amount (and the shield's share); fed back to `DamageFormula.Compute` the
  components give the amount exactly (tested across seeds). Filter chips: All, each of the team,
  the Beastbinder, and one chip that steps through the enemies. In battle the log strip opens it
  (the battle waits while it is open); after, the Results screen's *Battle log*.
- **ATB display**: under every portrait of the turn-order bar (the predicted next actors), the
  gauge ticks until that turn ("+0": now, or queued at the same instant) and a thin bar of it
  relative to the farthest turn shown (`BattlePlayback.ForecastTimed` over
  `TurnManager.PredictNextTurns`, a read-only projection of `PredictNextActors` with timing).
- **Element badges**: every element badge shows a distinct two-letter code
  (`ElementChartViewModel.Code`: Fi, Wa, Ea, Ai, Lt, Ic, Na, Me, Li, Da), so Light and Lightning
  never read alike.
- **Help**: the Turns panel's "?" explains its columns and that the average enemy is the plain,
  unweighted mean over every enemy type at the beast's level.

**Saves.** `GameSession` owns the loaded `PlayerSave` and writes it to one slot through
`SaveStore` over an `ISaveStorage`: `FileSaveStorage` under `SaveLocations` in the game (the
per-user folder on desktop, the app's files directory on Android, or `--save-dir`),
`MemorySaveStorage` in tests and screenshot runs. It autosaves after New Game, when a battle
begins (the node is entered and its consumable spent), after results (a loss too: the retry count
must persist), after an idle claim that paid, and when the app goes to the background or closes.
Continue falls back to the `.bak` with a message when the main file is torn.

**Idle.** `GameSession.ClaimIdle` runs `IdleRewardCalculator.Claim` with the device's clocks
(`IGameClock`: the wall clock plus a monotonic one, Android's `elapsedRealtime`), on Continue, when
the app comes back (the map claims it when it next shows) and from the idle chip; a claim of
nothing is silent. The optional "idle full" notification is a host seam (`IIdleNotifier`), off by
default, implemented on Android only.

## Architecture

```
BeastCraft.Presentation (engine-neutral, unit-tested)      BeastCraft.Game (MonoGame)
─────────────────────────────────────────────────────      ──────────────────────────────────────
Ui/Widgets.cs   Widget tree, UiRoot input routing,         BeastCraftGame   the host: input, Back,
                ScrollView (drag, inertia), Tabs,                           draw order, saves,
                Button, Label, Panel, Icon,                                 scripted screenshots
                ProgressBar, Hotspot, ToastQueue           Screens/         Title, Home, Encounter,
Ui/UiStyle.cs   the house style (data)                                      Battle, Results;
Screens/        ScreenStack (+ modals, Back rule),                          GameModal, Confirm,
                GameSession, the view-models,                               Settings
                NodeBattle, MapLayout                      Ui/UiPainter.cs  draws the widgets
```

- **The screen stack** (`ScreenStack`): screens are pushed, popped, replaced, popped to by name or
  reset; modals sit on a second, lighter stack over the top screen. Each change starts a short
  transition (the host draws a fading veil). **Back** (Android Back, desktop Esc) closes the top
  modal first, then asks the top screen (`IScreen.HandleBack`: switch tab, ask to skip, ask to
  quit), then pops it; with only the root left it says Quit. The title answers Back itself (a
  "Leave?" modal), so the game never quits by accident.
- **View-models live in Presentation**: they call the Core rules and expose plain data for the
  screens (`MapNodeView`, `EnemyGroupView`, `BeastResultRow`, …). They know nothing of MonoGame and
  are tested headless (`Tooling/EditModeTests/Screens`). **Game** holds only layout, drawing and
  input.
- **Input** is read once a frame by the host (`FrameInput`): the mouse's left button or the first
  finger goes to the top modal's (or else the top screen's) `UiRoot` as press / move / release in
  canvas pixels (`CanvasFit.ToCanvas`, which also takes the safe-area insets out). The battle
  still reads its own raw input (its gestures and keys are unchanged).
- **Drawing**: the host fits the 1080x1920 canvas into the target inside the safe area, clears the
  bars, and draws the top screen, then each modal (scrim first), the toast and the transition
  veil. The battle draws exactly as the old viewer did: its `--screenshot` output is
  byte-identical.

## The UI toolkit

Small and custom (producer decision), retained, over `ITextRenderer`:

| Widget | What it is |
|---|---|
| `Panel` | A rounded box in a panel look (`panel`, `card`, `modal`, `toast`, `header`, `nav`, `banner`, `slot`). |
| `Label` | Text, one line cut to fit or wrapped; aligned; a colour key. |
| `Button` | Text and/or a glyph on a rounded face; pressed, disabled and selected looks; `Clicked`. |
| `Icon` | Manifest art by key, else a code-drawn glyph, optionally on a disc. |
| `ProgressBar` | A pill bar with a "before" part and the value (the XP bars, the seal progress). |
| `Tabs` | A row of items (the bottom nav); `Changed`. |
| `ScrollView` | Vertical scrolling with touch or mouse drag, release inertia, resistance past the ends and spring-back; clips its children. The mouse wheel scrolls it too. |
| `Hotspot` | An invisible tappable area (map locations, cards the screen draws itself). |
| `ToastQueue` | The short message at the bottom that fades in and out (host-wide). |

Widgets hold layout and state only. `UiRoot` routes a press to the interactive widget under it;
moving more than 18 canvas px inside a scroll view turns the press into a drag (and cancels the
tap); a release on the widget pressed is a click. The painter (`UiPainter`) draws each widget type,
nine-slicing one engine-made anti-aliased disc for every rounded shape (an outline is its shape with
the fill's inset on top), and calls the screen back (`DrawCustom`) for what the toolkit does not
draw — the map, portraits, cards — in the widget's own (scrolled, clipped) space.

**Style is data**: `content/data/Ui/ui-style.json` (validated on content load by
`UiStyleValidator`) names the colours — warm plum outlines, cream panels, the element and location
colours, the map's greens — the panel and button looks (fill, outline, radius) and the text sizes.
Until the UI art lands, icons are small code-drawn glyphs (battle, den, pass, lair, trader, camp,
the nav tabs, lock, check, …) in `UiPainter.Glyph`; an art key from the manifest can replace any of
them.

## How to add a screen

1. **The view-model** in `src/BeastCraft.Presentation/Screens`: what the screen shows, as plain
   data, and what its actions do, calling the Core rules through `GameSession` (its `Save`,
   `Content`, `Autosave`). No MonoGame types. Test it in `Tooling/EditModeTests/Screens` with a
   `MemorySaveStorage` (and a `ManualGameClock` for anything timed).
2. **The screen** in `src/BeastCraft.Game/Screens`: a `GameScreen` subclass. Build its widgets in
   the constructor (`AddButton`, `AddLabel`, `Ui.Add(new ScrollView …)`); give buttons an `Id` so a
   script can tap them. Refresh from the view-model in `Enter` (called whenever the screen comes
   to the top again). Override `Draw` for the background and anything drawn around the widgets,
   and `DrawCustom` for what the widgets stand for. Override `HandleBack` only when Back should do
   something other than pop (a tab switch, a confirm).
3. **Navigation**: `Ctx.Stack.Push(new MyScreen(Ctx, …))` from where it opens; `Replace` when the
   old screen should not come back (encounter → battle → results); `Pop`/`PopTo` to return.
   Modals: subclass `GameModal` (or use `ConfirmModal`) and `Ctx.Stack.PushModal`.
4. **Scripted runs**: add the screen to `ViewerOptions.ScreenNames` and to the steps in
   `BeastCraftGame.GoTo` (and the walkthrough, if it belongs to the loop), so
   `--screen name --screenshot shot.png` renders it.

## Debugging

- `--screen NAME` starts at a screen (`title`, `map`, `encounter`, `battle`, `results`, `roster`,
  `grove`, `avatar`, `inventory`, `settings`; `demo` is the battle demo; the roster-visibility
  screens `beast-detail`, `beast-derived`, `beast-skills`, `beast-gear`, `encounter-insight`,
  `element-chart`, `glossary`, `battle-log`, `results-log`); with `--screenshot PATH` it renders
  it and exits, on a throwaway in-memory save (`--starter-level 6` shows a level gap).
- `--walkthrough DIR` captures a new player's first session (the starter pick, Hearthglen with its
  hints, the trials, the camp, the finale, the way on to Verdant Hollow) as numbered PNGs on a fresh
  save in a temporary folder; `--screen starter-pick` and `--screen hearthglen` start there, and the
  other scripted screens start past Hearthglen (the skip: Golem, Phoenix, Griffin).
- `--map-seed N` fixes new expedition maps; `--starter-level L` starts a new game's beasts at level
  L; `--save-dir DIR` keeps the save elsewhere.
- Any battle-demo flag (`--turns`, `--skill`, `--team`, `--encounter`, …) runs the old battle
  viewer, as before.

## Decisions this follows

Bottom nav Map, Roster, Grove (the team base: the party and idle rewards now; the beasts' habitat,
a garden and expeditions later; the map's Camp locations keep their name), Avatar, Inventory, with
Map as home and Shop and Idle contextual; a painted spatial region map, never a graph; the full
scouting preview always, for free; one consolidated results screen; the team suggestion as a
dismissible banner after 3 losses, respecting its setting; a small custom toolkit; the r11
Normal/Hard choice made when starting a run. From the "Casual Idler" persona review: Continue is
primary, idle rewards are claimed on Continue and resume, the map has an idle chip and a one-tap
Next battle, auto-advance is a setting, the battle speed is a saved preference, and the idle-full
notification is an opt-in Android hook.

## Known gaps (for the next PRs)

- The starter team is the campaign pacing model's (six beasts at level 1, no gear); the first
  stage's battles are won about 40-50% of the time against the calibrated 80% (squad, horde) and
  50% (solo): the calibration assumes typical gear and a scouted pick from the full ten-species
  roster — a balance question, not a screens one.
- **The battle crash window.** `NodeBattle.Begin` spends the chosen consumable and autosaves (the
  Core's rule: a battle begun cannot hand the item back by being abandoned); `Complete` pays out
  and autosaves again. If the process dies in between — killed mid-battle, or before the results
  are applied — the consumable stays spent and the battle's rewards (and its loss count) are
  never recorded: on the next Continue the location is simply still there to fight. Nothing is
  duplicated. A refund would need a pending-battle marker in the save (a schema change), so it is
  left for a later PR.
- Grove, Avatar and Inventory are placeholders; Trader locations are "coming soon" (Camp
  locations open the minimal camp: train a beast, the idle chip). The roster's looks are shown,
  not edited (the wardrobe is a later PR). Skill tomes are only sold by the trader (coming soon),
  so a beast's kit beyond its default loadout stays locked for now; gear comes from drops and
  first clears.
- One save slot; no region list (the next region starts automatically after a boss).
