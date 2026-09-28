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
  │                             │  (Roster, Grove, Avatar, Inventory:              ▼
  └──────── Back ───────────────┘   "coming soon")                               Battle ──decided──▶ Results
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
  Avatar, Inventory** (only Map works yet) — over the region map. The map is *spatial*: the stage's
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
  `grove`, `avatar`, `inventory`, `settings`; `demo` is the battle demo); with `--screenshot PATH`
  it renders it and exits, on a throwaway in-memory save.
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
- Roster, Grove, Avatar and Inventory are placeholders; Trader locations are "coming soon" (Camp
  locations open the minimal camp: train a beast, the idle chip).
- One save slot; no region list (the next region starts automatically after a boss).
