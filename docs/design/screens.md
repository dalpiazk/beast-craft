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
  │                             │  (Roster ─▶ beast detail, compendium;            ▼
  └──────── Back ───────────────┘   Grove ─▶ Glade/Garden/Board/Npc;           Battle ──decided──▶ Results
                                     Avatar ─▶ Overview/Skills/Gear/Wardrobe;                         │
                                     Inventory ─▶ Gear/Materials/Looks)                                │
                                ▲                                                                     │
                                └───────────────────── Continue (or auto-advance) ────────────────────┘
```

- **Starter pick** (`StarterPickScreen`, `StarterPickViewModel`): New Game's first beast, any of the
  ten (illustrated portraits, stance and element, a personality blurb, the stance explainer), then
  Hearthglen; or "Skip the tutorial": the same picker three times, one stance at a time, then Verdant
  Hollow. Hearthglen's own screens — the trial pick (`TrialPickModal`), the Keeper's dialogue box
  (`StoryModal`), the camp (`CampModal`, every region), the tutorial hints (`HintModal`, anchored to a
  widget, pausing a battle) and the way on (`RegionCardModal`) — are in [area-zero.md](area-zero.md).
- **Title** (`TitleScreen`, `TitleViewModel`): Continue (primary when a save exists; it claims the
  idle rewards and toasts them; the most recently played slot), Save slots (when a save exists), New Game (in the first free slot, then the starter pick; the slot list when all three are full), Settings. A save that
  could only be restored from its `.bak` says so; one that cannot be loaded says why. Back asks
  before quitting.
- **Save slots** (`SaveSlotsScreen`, `SaveSlotsViewModel`): one card per slot (three) with the save's
  summary and Continue or New game, Delete (asks first), and Export and Import where the host has an
  `ISaveTransfer` (desktop only for now). See `progression-and-saves.md`, "Save slots, backup and
  export".
- **Home** (`HomeScreen`, `HomeViewModel`, `MapViewModel`): the bottom nav — **Map, Roster, Grove,
  Avatar, Inventory** — over the region map. Map and Roster show inline (see "Roster and visibility"
  below); Grove, Avatar and Inventory each push their own full screen (`GroveScreen`, `AvatarScreen`,
  `InventoryScreen`) instead of showing inline (`HomeScreen.SelectTab`), the same way the Roster
  tab's "Compendium" chip pushes a screen without changing the Roster tab's own selection — so the
  nav bar keeps showing whichever of Map/Roster was selected underneath. The map is *spatial*: the
  stage's node map (rows and lanes, the internal pacing model) is laid out by `MapLayout` as places
  on a painted-style meadow (placeholder: a soft gradient and blobs), joined by winding trails; it is
  never drawn as a graph. Locations show their type (Battle, Den, Pass, Lair, Trader, Camp) and
  state (reachable, cleared, where you stand, locked, bypassed). The header shows the region, its
  level band, the stage, the seal's progress and the binding limit. The idle chip claims idle
  rewards in one tap; **Next battle** opens the recommended location with the last team picked.
  Camp locations open the camp (`CampModal`, which also has its own "Trade" button to the travelling
  trader); Trader locations open the Trader (`ShopScreen`) — see "Avatar, Inventory and the Trader"
  below.
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
  "Meet it at a Kinship site" (the compendium's seed). A card opens the beast's detail screen.
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

## Fog, points of interest and Kinship (the discovery layer)

Rules and data: [kinship-discovery.md](kinship-discovery.md). Everything here reads Core
(`MapFog`, `DiscoveryRules`, `KinshipRules`); the battle rules and the calibration are untouched.

- **Map fog** (`MapViewModel.HasFog`, `FogCells`; `HomeScreen.DrawFog`): in a discovery region
  (r01-r06) soft painted mist (placeholder: layered soft discs, a shaded underlayer, a pale body and a
  light top, overlapping into a bank) over every fog cell not yet seen; a locked or bypassed location
  on unseen ground is not drawn (`MapNodeView.Hidden`). The trailhead row and the pass or lair are
  always in sight. Hearthglen and r07 on are shown fully revealed.
- **Points of interest** (`MapViewModel.Pois`, `PoiView`): rounded badges (never a location's round
  disc) in their kind's colour and glyph (`shrine`, `lore`, `cache`, `vista`, `kinship`), glowing while
  unvisited, a check once found, with a name tag. A tap opens `PoiModal` (`PoiViewModel`: name, kind,
  words, one action — rest a while, read the stone, open it, look out — or what it held); a Kinship
  site with a beast to offer opens its trial instead.
- **Kinship trial**: the encounter screen (`EncounterViewModel.ForKinship`, `NodeBattle.ForKinship`)
  with the site's words and its bond condition as a banner and no team suggestion; results
  (`ResultsViewModel.BuildTrial`) pay nothing and name the beasts offered. A won trial's choice of two
  is the Hearthglen trial's popup (`TrialPickModal.Kinship` over `KinshipPickViewModel`; the popup now
  takes an `IBeastPicker`), offered before anything else on the map, like Hearthglen's pending pick.
  Taking the pass or lair with an unclaimed site on the map asks first (`MapTapKind.ConfirmLeave`).
- **Explored N%** (`RegionHeaderView.CompletionText`): a chip under the header; its tap opens
  `RegionProgressModal` (`RegionProgressViewModel`: the 100% reward, per stage rows walked and places
  found, **Revisit** a stage already reached: `GameSession.ReplayStage`). The 100% reward's toast
  (`GameSession.CheckCompletion` → `PendingToasts`, drained when the map shows).
- **Normal or Hard** (post-game regions only, `RegionHeaderView.HardAvailable`; #60): a chip under the header
  showing the difficulty now; its tap asks, then plays the stage on the map again on the other difficulty
  (`MapViewModel.SwitchDifficulty` → `GameSession.ReplayStage(stage, difficulty)`). On Hard a berry **Hard**
  badge leads the header's level line and ends the encounter preview's subtitle (`EncounterViewModel.IsHard`).
  Replays and the next stage keep the difficulty. Screenshots: `--screen map-hard | encounter-hard`.
- Screenshots: `--screen kinship-map | kinship-poi | kinship-trial | kinship-choice | region-progress`
  (a scripted walk up to the first stage's Kinship site, every fight on the way counted won).

## Compendium, achievements and look tokens (the Collector persona)

Rules and content: [compendium-achievements.md](compendium-achievements.md). Everything here reads
Core (`CompendiumRules`, `AchievementRules`, `CosmeticLibrary.TokenPool`); the battle rules and the
calibration are untouched (achievements award a text title only, never a stat or a look with combat
power).

- **Compendium screen** (`CompendiumScreen`, `CompendiumViewModel`): reached from the Roster tab's
  "Compendium" chip. Every roster species as a card — an unknown silhouette (dimmed art, "???", the
  roster's silhouette hint), a live Kinship offer (highlighted, its real name and art: a preview, never
  stored), or owned (and, through Kinship, "Found through Kinship at &lt;site&gt;") — then every lore
  entry (found: its title and text; not found: a locked "???" placeholder), and the combined
  completion percent as a header progress bar (`CompendiumRules.Completion`).
- **Achievements screen** (`AchievementsScreen`, `AchievementsViewModel`): reached from the Avatar tab.
  The Beastbinder's level and equipped title at the top (`AchievementsViewModel.TitledName`), the title
  picker (every owned title plus "No title", one equipped at a time — set directly, autosaved), and
  every achievement, earned or not, with its condition in words and the title it awards. The equipped
  title also shows beside "Beastbinder" on the Results screen's avatar XP line (wherever the avatar's
  name is already shown, the same way a beast's name is never re-derived).
- **Look-token shop** (`LookTokenShopScreen`, `LookTokenShopViewModel`): the token balance
  (`PlayerSave.LookTokens`) and every token-purchasable look (`CosmeticLibrary.TokenPool`), owned,
  affordable or not, bought directly with `CosmeticRules.SpendLookToken` — disabled when it is already
  owned or the balance falls short. Reached from the Avatar tab and from a beast's Gear & bonds tab
  ("Look shop", beside its worn looks — the existing, display-only cosmetics UI).
- **Avatar tab**: pushes the full `AvatarScreen` (Overview, Skills, Gear, Wardrobe — see "Avatar,
  Inventory and the Trader" below), whose Overview tab carries the achievements/title and look-token
  shop buttons the old minimal identity card had.
- **Toasts**: `DiscoveryResult.TitlesEarned` / `LookTokens` (a point of interest's visit toast),
  `CompletionReward.TitlesEarned` / `LookTokens` (the region 100% toast), `KinshipResult.TitlesEarned`
  (a Kinship join's toast) and `CampaignResult.TitlesEarned` (a Results screen note) each add
  `GameSession.ExtraRewardText`'s " You earned the title \"...\" and N look tokens." to the reward's own
  message — the same toast mechanism every discovery and campaign reward already used, never a new one.
  Achievements are also evaluated once at session start (New Game and Continue), so a save
  retroactively earns whatever it already meets (an older save, or a level or Kinship count reached
  between sessions) rather than only from a fresh trigger; newly earned titles queue one consolidated
  toast (`GameSession.PendingToasts`, shown when the map next appears) instead of nothing.
- **The Kinship "last beast chooses you" framing** (`KinshipPickViewModel`, over
  `KinshipResult.SoloOffer`): the seventh site's one-beast offer is titled "The last beast chooses you"
  and its subtitle says "the last beast joins you: no choice needed" instead of "choose who joins you".
- **Wiring**: `NodeBattle.cs` passes `content.Achievements` into `CampaignRules.ResolveBattle`, so a
  region's first boss clear (and, since this PR, every resolved campaign battle — see
  compendium-achievements.md, "Evaluating") evaluates achievements in real play.
- Screenshots: `--screen compendium | achievements | look-tokens`.

## The Grove (D4)

Rules, content and save: [grove.md](grove.md). Everything here reads Core (`Grove.GroveRules`,
`Garden.GardenRules`, `Expeditions.ExpeditionRules`, `Npc.NpcRules`) and, for soothing, the campaign's
own `CampaignRules.Soothe`; no combat power anywhere in this feature, the balance sim's default report
is unaffected.

- **`GroveScreen`** (`src/BeastCraft.Game/Screens/GroveScreens.cs`, `GroveHubViewModel` and its four
  children in `src/BeastCraft.Presentation/Screens/GroveViewModels.cs`): one screen, an inner `Tabs`
  strip (Glade / Garden / Board / Npc), reached by pushing from the Home tab bar's Grove slot
  (`HomeScreen.SelectTab`) rather than showing inline.
- **Glade**: habitat chips (locked ones dimmed) select which habitat's decor grid shows — a
  4-column slot grid to place and remove, and below it a habitat canvas where each piece is dragged to
  move it (#46; grove.md §5). Every
  owned beast is a card: portrait (its worn colour form's tint applied, see below), affinity tier and
  XP bar, Feed/Play (disabled on cooldown) and, once any gift is pending, Collect/Collect all.
- **Garden**: plots as a 2-column grid (empty → a seed-picker `ChoiceModal`; growing → a progress bar;
  ready → Harvest alone, or pick it and then another ready plot to cross-pollinate), the herbarium,
  the crafting recipes (Craft enabled only once every input is held) and the Grove item inventory.
- **Board**: destination cards (Locked / Available / Away, with the party's names and hours left /
  Ready); Send opens `SendPartyModal` (a multi-select party picker, up to the destination's
  `PartySize` — sending never locks a beast, so every owned beast is offered); Collect pays out and
  toasts the result.
- **Npc**: NPC chips, the selected NPC's resolved line (`Npc.NpcRules.ResolveAndMark`, marking it
  seen), its open requests (Fulfil enabled only once the item is held) and its side story's current
  chapter with a Continue button once it can advance.
- **Colour forms** live in the beast detail's looks area (`BeastDetailScreen`'s Gear & bonds tab,
  `BeastDetailViewModel.ColourForms`), not a Grove tab: locked (with the item's held/needed count),
  owned, or worn, with Unlock (`GroveRules.TryUnlockColourForm`) and Wear/Wear natural
  (`Economy.CosmeticRules.TrySetOption`) buttons.
- **The whole-sprite tint fallback** (`Presentation.Screens.ColourFormPresentation.WornTint`, a
  deterministic HSV hash of the colour form's id — no tint is authored anywhere, see grove.md §10):
  applied everywhere a specific owned beast's own sprite draws — the roster card, the beast detail
  portrait, the encounter screen's party portraits, the Glade's beast cards.
- **Soothing** shows on the ordinary encounter preview (`EncounterViewModel.CanSoothe`/
  `SoothingOptions`/`Soothe`, `EncounterScreen`'s "This place can be soothed" banner): a `ChoiceModal`
  over the region's soothing items held, a `ConfirmModal`, then `CampaignRules.Soothe` with the
  current team; success pops back to the map with a toast. Never shown for an Elite den, a Gate, a
  Boss, a Kinship trial or Hearthglen.
- **Session wiring**: `GameSession.RefreshGrove()` (unlocks, gift clocks, plot/expedition readiness)
  runs on `StartWith`, `Continue` and every `HomeScreen.Enter`, queuing one `PendingToasts` entry the
  first time something new is ready.
- Screenshots: `--screen grove | grove-glade | grove-garden | grove-board | grove-npc | soothe |
  colour-forms`.

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
nothing is silent. The optional "idle full" and "Grove ready" notifications go through a host seam
(`ILocalNotifier`, one channel and alarm each), both off by default, implemented on Android only.

## Avatar, Inventory and the Trader

Full design pass and the Core-rule survey it started from: [avatar-inventory-shop.md](avatar-inventory-shop.md).
Everything here is presentation over Core rules that already existed (`AvatarProgression`,
`SkillBook`, `GearRules`, `CosmeticRules`, `ShopService`); the tuned and campaign-pacing reports are
byte-identical. Built on the shared component layer below ("Shared components").

- **Avatar tab** (`AvatarScreen`, `AvatarHubViewModel` and its four children in
  `src/BeastCraft.Presentation/Screens/AvatarViewModels.cs`), reached by pushing from the Home tab
  bar's Avatar slot: one screen, an inner `Tabs` strip (Overview / Skills / Gear / Wardrobe).
  - *Overview*: level and XP bar, the titled display name (`AchievementsViewModel.TitledName`, reused
    rather than re-derived), the base-vs-total stat block (`CampaignAvatar.Profile(content)
    .GetStatsAtLevel(level)` through the same `StatCalculator.ComputeStats`/`CollectModifiers`
    pipeline `BattleAvatar.Create` uses in battle — WYSIWYG with what actually fights), and the
    Achievements/titles and Look-token shop buttons the old minimal identity card had.
  - *Skills*: the three equipped actives and three equipped passives (`AvatarSkillBook`), each a card
    (`SkillCard.Of` for actives — the same card a beast's skill slot uses, since avatar actives and
    beast skills share one `SkillSO` id space; a small passive summary, reusing `SkillCard.PowerLine`
    for its effect lines). *Change* opens a `ChoiceModal` of the avatar's other known actives/passives
    for that slot (`SkillBook.Equip`/`SwapSlots`; the last one in a book cannot be taken off). No
    "learn" here: avatar actives and passives are learned only from the Trader.
  - *Gear*: the three avatar slots (Weapon/Armor/Trinket), Equip/Take off through
    `GearRules.EquipAvatarGear`/`UnequipAvatarGear` against `_session.Content.Battle` — the same call
    shape the beast detail screen's Gear & bonds tab already uses, generalized to the avatar.
  - *Wardrobe*: every avatar cosmetic category (`CosmeticCategory.IsAvatar`) as owned/locked chips
    with Wear (`CosmeticRules.TrySetOption(save, null, categoryId, optionId, library)` — `beastId:
    null` is the established meaning "the avatar" throughout `CosmeticRules`), plus a curated
    six-swatch colour row per colour category (`AvatarWardrobeViewModel.Swatches`, UI-only presets
    over the always-free `TrySetColor`) and a Custom chip that opens the HSV picker (`ColourPickerModal`).
    New looks carry a dot until seen (schema 12).
- **Inventory tab** (`InventoryScreen`, `InventoryHubViewModel` and its three children in
  `src/BeastCraft.Presentation/Screens/InventoryViewModels.cs`): Gear / Materials / Looks.
  - *Gear*: every owned gear instance (beast and avatar), a filter chip row (All/Beast/Avatar) and a
    cycling Sort chip (Rarity/Level/Name, the Roster tab's own Sort-chip idiom). Equip opens a
    `BeastPickerModal` for beast gear (one `GearRules.EquipBeastGear` call per pick, the rule itself
    reports why not) or equips directly for avatar gear; Sell (unworn only) calls
    `ShopService.TrySellGear` — reachable from anywhere, since selling needs no `ShopContext`.
  - *Materials*: held skill materials, Grove items and consumables as three read-only counted lists —
    no Core rule exists for "using" any of them from here (materials are spent through skill
    training, Grove items through the Grove's own screens, consumables chosen at the Encounter
    screen), so no action is offered.
  - *Looks*: the look-token balance with a button to the existing Look-token shop screen, and a
    per-category owned/total count across every category (avatar and every species) — a small
    Collector "collection" summary.
- **The Trader** (`ShopScreen`, `ShopViewModel`): reached by tapping a Shop map node
  (`MapTapKind.Shop`, `MapViewModel.Tap` — no longer `ComingSoon`) and from the Camp modal's "Trade"
  button (the camp's travelling trader, economy-and-shop.md). Two tabs:
  - *Stock*: every current listing (`ShopService.GetStock`), grouped by category, each row's Buy
    button disabled with the reason read off the listing's own state (sold out, not enough gold,
    already known) rather than a second speculative `TryBuy` call. A `BeastSkill` (tome) listing
    opens a `BeastPickerModal` first; every other category buys directly
    (`ShopService.TryBuy(save, context, listingIndex, targetBeastId)`).
  - *Sell*: the same unworn-gear rows and rule the Inventory Gear tab's Sell uses.
  - **Opening**: a Shop node calls `CampaignRules.Trade(save, regions, nodeId, session.Content.Shop)`
    once (rolls/freezes stock, marks the node visited/cleared, reveals fog — unchanged Core
    behaviour); the camp trader calls `ShopService.Open` alone over the camp's own node
    (`CampaignRules.ShopContextFor(run, campNode)`), since `Trade` refuses a non-Shop node type by
    design and the camp itself is never cleared by trading. `GameContent.Shop` (a `ShopService` built
    from `shop-tables.json` over `Economy` and `SkillLibrary.SpeciesKits`) is new load-time wiring —
    `ShopService` was fully built and tested but never constructed anywhere before this PR.
- **"New" markers**: not built. A true "new since you last looked" marker needs a persisted "seen"
  set (no such save field or convention exists); flagged as a producer decision in the design doc
  rather than mislabelling "unworn" as "new".
- Screenshots: `--screen avatar | avatar-skills | avatar-gear | avatar-wardrobe | inventory |
  inventory-materials | inventory-looks | shop | shop-sell`.

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

### Shared components

Every hand-built page before Avatar/Inventory/Shop (the beast detail screen, Grove, Compendium)
re-implemented the same handful of composites for itself — a top-bar height constant, a private
`Card(y, height, style, draw)` plus its own `Dictionary<Widget, Action<Rect>>`, a hand-drawn beast or
item card, a chip row, a section heading, a stat table, an inner tab strip with its own guessed
height — and that duplication is what caused the layout bugs (clipped rows, header/divider
collisions, tab overlap) those screens hit. Avatar, Inventory and Shop were built first on a small
shared layer, `src/BeastCraft.Game/Screens/Components/ScreenComponents.cs` (namespace
`BeastCraft.Game.Screens.Components`), each with its own copy of the header and inner-tab-strip
wiring rather than a shared `TabStrip`; a follow-up refactor moved the beast detail screen, Grove,
Compendium/Achievements/Look-token shop and the Encounter/Glossary headers onto the layer too, added
`TabStrip` and put Avatar/Inventory/Shop on it as well, so it is now the whole app's one
out-of-combat layout layer (the battle screen and the map's own painted rendering stay their own
thing — a fixed-camera board with per-frame effects has nothing in common with a scrolling card
list):

| Component | What it is |
|---|---|
| `HeaderMetrics` | The standard header heights (`Compact` — back + title, no room to spare, the encounter/glossary shape; `Standard` — back + title + subtitle, most screens; `Roomy` — + two extra lines with more breathing room, Achievements' shape; `Tall` — + a stat/identity line, the beast detail screen's shape) and the page padding, as one source of truth. |
| `ScreenHeader` | Builds the back button; `Paint` draws the fixed wash + divider + title/subtitle over whatever a scroll view painted underneath, and repaints the back button (both live above the header line). A screen whose title sits somewhere other than the fixed position (Glossary's lower, centred title) calls `Paint` with an empty title for the wash alone and draws its own title over it, the same way the beast detail screen's "no such beast" case does. |
| `TabStrip` | The inner tab strip (Avatar's Overview/Skills/Gear/Wardrobe, Inventory's Gear/Materials/Looks, the Trader's Stock/Sell, Grove's Glade/Garden/Board/Npc) at one shared height (136px), sized so the icon reads clearly and the label sits with even air above and below it inside the selected pill — the same proportions the home bottom nav reads at (`HomeScreen.NavHeight`, 170px, the same `UiPainter.Tabs` painter). A first version of this height (96px) shipped too short: the icon shrank to a sliver and the label sat on the selected pill's bottom edge, the exact bug Grove's own strip had once been widened to 170px to work around, before `TabStrip` replaced that workaround with (at the time) too-short a shared height; 136px is the corrected value every strip above now shares. |
| `SectionHeader` | A card's heading line. |
| `CardList` | The `Card`/drawer-dictionary pair every page rebuilt for itself, now written once: wraps one `ScrollView`, `Begin()`/`Card(...)`/`End(y)` to rebuild it (keeping the scroll position), `TryDraw` for the screen's `DrawCustom`; `TrackDraw` registers a drawer for a widget the screen added itself (an irregular grid cell, e.g. the Grove Garden's plots) rather than through `Card`. Every screen on `CardList` now advances by its one fixed card gap (26px) rather than each screen's own hand-picked value (16-30px before) — wider than most, so it only ever adds air, never removes it. |
| `ChipRow` | A wrapping row of chip buttons (a filter, a sort cycle) from a label list. |
| `StatTable` | A name column plus up to two right-aligned numeric columns with headers (the Avatar Overview's base/total stats). |
| `ItemRow` | A list row's text (title, subtitle, a detail/disabled-reason line) — Inventory's and the Trader's listings. |
| `ActionRow` | One or more buttons stacked and right-aligned in an area, each bound to `Enabled`. |
| `BeastCard` / `BeastPickerModal` | A small portrait-or-placeholder beside a name and subtitle, and a titled modal list of them — equip gear to a beast, buy a tome for one. |

Left off this layer, deliberately: `ElementChartScreen` (no wash/divider at all — title text drawn
straight over the background, a genuinely different shape, not a duplicate of `ScreenHeader`'s);
`ResultsScreen` and `StarterPickScreen` (a full-bleed banner/gradient with a centred title, no back
button, no wash — their own shape, not `ScreenHeader`'s); the Discovery layer's `PoiModal` /
`RegionProgressModal`, `ChoiceModal`, `SettingsModal` and the other `GameModal` subclasses (each
already a small, self-contained centred card with its own one-off sizing, not a second copy of a
`CardList` or `ScreenHeader` shape); `HomeScreen`'s own map/nav chrome (already its own thing, not a
hand-rolled copy of these components); `BattleLogModal` (shared with the battle screen, which is out
of scope for this refactor, so left untouched to avoid any visual change rippling into it). New
full-page screens should build on this layer rather than hand-rolling their own
`Card`/chip-row/stat-table/tab-strip again.

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
  `element-chart`, `glossary`, `battle-log`, `results-log`; the discovery layer's
  `kinship-map`, `kinship-poi`, `kinship-trial`, `kinship-choice`, `region-progress`; the Collector
  persona's `compendium`, `achievements`, `look-tokens`; the Grove's `grove-glade`, `grove-garden`,
  `grove-board`, `grove-npc`, `soothe`, `colour-forms`; the Avatar/Inventory/Trader screens'
  `avatar-skills`, `avatar-gear`, `avatar-wardrobe`, `inventory-materials`, `inventory-looks`,
  `shop`, `shop-sell`); with `--screenshot PATH` it renders it and exits, on a throwaway in-memory
  save (`--starter-level 6` shows a level gap).
- `--walkthrough DIR` captures a new player's first session (the starter pick, Hearthglen with its
  hints, the trials, the camp, the finale, the way on to Verdant Hollow) as numbered PNGs on a fresh
  save in a temporary folder; `--screen starter-pick` and `--screen hearthglen` start there, and the
  other scripted screens start past Hearthglen (the skip: Golem, Phoenix, Griffin).
- `--map-seed N` fixes new expedition maps; `--starter-level L` starts a new game's beasts at level
  L; `--save-dir DIR` keeps the save elsewhere.
- Any battle-demo flag (`--turns`, `--skill`, `--team`, `--encounter`, …) runs the old battle
  viewer, as before.

## Decisions this follows

Bottom nav Map, Roster, Grove (the team base: the party and idle rewards, and — since D4 — the
beasts' habitat, a garden and expeditions; the map's Camp locations keep their name), Avatar, Inventory, with
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
- Grove is built (D4 — see "The Grove" above); Avatar, Inventory and the Trader are built (see
  "Avatar, Inventory and the Trader" above). The roster's own looks are still shown, not edited
  beyond the look-token shop's direct purchases and the avatar's own wardrobe (editing a *beast's*
  worn looks beyond its colour forms is a later PR). New gear and looks carry a "new" dot until
  seen (schema 12), and the wardrobe's colour categories have an HSV picker beside the six swatches (#46).
- No region list (the next region starts automatically after a boss).
