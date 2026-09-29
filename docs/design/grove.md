# Grove: Beast Grove, Wildgarden, Expeditions

**Status: D1, D2 and D3 BUILT (Core, content, save, sim, tests); D4 not started — see §11.**
**DRAFT — every DisplayName, Title and Text pending producer review.** This is the design pass
reconciled against `main` at commit `8829dbf` (the draft this replaces was written against an older
`main`, schema 7, before the Kinship/discovery layer, the compendium and look tokens existed). It
supersedes that draft; every "producer decision"/"producer addition" from it is carried forward
verbatim-in-substance below, and every place current code already had something the draft assumed
didn't exist is called out. No combat power anywhere in this feature; the balance sim is untouched
(`docs/balance/tuned-report.md` and `campaign-pacing-report.md` are byte-identical before and after
this PR). Local play only, offline-safe clock. Tone: warm, painterly, wonder-first
(`content-bible.md`), never grim.

## What already existed that this builds on

The draft assumed a green field. Reconciled against the real `main`:

- **The Grove tab already exists**, as a placeholder: `HomeTab.Grove` and `HomeViewModel.TabNames`/
  `ComingSoonText` (`src/BeastCraft.Presentation/Screens/MenuViewModels.cs`) already name it "Grove"
  and describe it as "your beasts' habitat, a garden and expeditions" — the draft's proposal to
  rename a "Camp" tab to "Grove" is moot; there never was a Camp tab. D4 fills this placeholder in;
  D1 does not touch it (no screens).
- **`DiscoveryProgress.GroveUnlockIds`** (schema 8) is exactly the seam the draft expected: a shrine
  (`discovery.json`) grants a `GroveUnlockId`, held in this list, never consumed/removed — this PR's
  `GroveRules.RefreshUnlocks` reads it live (`Contains`, not a pop), so the check is idempotent and
  nothing already earned by a shrine visit is ever lost. The 12 shrines already authored in
  `discovery.json` already carry exactly the ids this PR's content needed: 3 habitats
  (`grove_habitat_mossy_glade`, `_tide_pool`, `_snow_hollow`), 6 decor pieces and 3 seeds — this PR's
  `grove-library.json`/`garden-library.json` use those ids as-is; no change to `discovery.json`.
- **A "Camp" node / camp rest already exists** on the region map (`LocationKind`/`MapNodeType`) —
  unrelated to the Grove hub; it is a map location kind (a rest stop on an expedition), not this
  feature's home screen. No collision, just a shared English word; not touched by this PR.
- **The compendium, achievements and look tokens** (schema 9) exist. This PR does **not** wire Grove
  progress into achievements or the compendium (no new achievements, no new titles) — that is a
  natural D2/D3 follow-up once NPC requests and side stories exist to react to; wiring it now would
  be scope creep on a "no screens" PR. `CosmeticRules`/`CosmeticLibrary` gained a `SourceGrove`
  constant (`"grove"`) so a `"look"` reward is mechanically real end-to-end (`GroveLibraryValidator`
  and friends validate it, `GroveRules`/`GardenRules`/`ExpeditionRules` grant it via
  `CosmeticRules.UnlockOrRefund`), but **no `"look"` reward is authored in this PR's content** — see
  "Deferred: cosmetic look rewards" below.

### Naming: two different "expeditions"

The instructions flagged `GameSession.EnsureExpedition()` as a possible existing "expedition"
concept to build on. It is not the same thing: that method (and `CampaignProgress.ActiveRun`, a
`MapRun`) is flavour language for one stage's traversal of a region map — "an expedition into the
region" — used throughout `progression-and-saves.md`, `kinship-discovery.md` and the campaign rules
tests. The Grove's Board (this PR's `Expeditions.ExpeditionProgress`/`ExpeditionRules`) is an
unrelated feature: sending beasts to a destination for a timed outcome roll. There is no code
collision (different namespace, different types: `Expeditions.ActiveExpedition` vs. `Campaign.MapRun`,
`PlayerSave.Expeditions` vs. `PlayerSave.Campaign.ActiveRun`), but the shared English word is a real
risk for design docs, UI copy and content text — every doc comment and design note in this PR that
could be ambiguous says "a Grove expedition" or "the region campaign's expedition" explicitly. D4
should keep doing the same in UI copy and in-fiction text.

## 1. Core loop and cadence (unchanged from the draft)

The Grove tab, with three sub-tabs: **Glade** (Beast Grove), **Garden** (Wildgarden), **Board**
(Expeditions) — one screen, one `IScreen`, an inner tab strip like the existing `Tabs` widget (D4).
Session shape: a 30–90s visit feeds a beast, plants a seed, checks a plot, sends or collects an
expedition, then back to the map — same rhythm as an idle claim, never a time sink. Timers, short end
to long, so something is always close to ready:

- **Gifts**: 2–6h per beast, narrowed by affinity tier (`GroveLibraryData.GiftHoursByTier`, tier 1→6h
  down to tier 5→2h), capped at 3 unclaimed per beast. Banked time is never lost while capped — it
  waits for a free slot (see `GroveRules.RefreshGifts`'s remarks).
- **Plant growth**: 3–24h by seed (`SeedSpeciesData.GrowthHours`).
- **Expeditions**: 1–12h by destination.

All three are checked through the same shared `Common.OfflineClock` (see §3). Engaging = goal
ladders (affinity tiers, the 36-variety herbarium, the Grove's own lore codex, decor sets) plus small
deterministic surprises (a rare gift, a discovered hybrid, a story) — never a timer-only wait.

## 2. Data (Core: `Grove/`, `Garden/`, `Expeditions/`)

Three Core folders, each mirroring the `Discovery`/`Economy` pattern (one `*LibraryData` POCO file,
one `*Library` lookup, one `*Validator`, one `*Rules` static class, and the schema-10 save section).
One content file per domain, all under `content/data/Grove/`:

**`grove-library.json`** (`GroveLibraryData`): `HabitatData {HabitatId, DisplayName, UnlockSource
("shrine"|"start"), UnlockId, SlotCount (1-12), ArtKey}`; `DecorData {DecorId, DisplayName,
HabitatScope ("" = any), Source ("shrine"|"affinity"|"garden_craft"|"expedition"|"starter"), UnlockId,
ArtKey, SortOrder}`; `AffinityTierData` — a **flat** `(SpeciesId, Tier)` row per species per tier 1-5
(not nested per species, for the same flat-list JsonUtility-parity reason every other content file
uses flat lists), `XpThreshold` ascending, `RewardKind ("lore"|"decor"|"look"|"idle_anim"|"none")`,
`RewardId`; `GiftTableData {SpeciesId ("default" = fallback for every species without its own),
Entries[]: {ItemKind ("decor"|"lore"|"cosmetic"), ItemId, Weight, IsCommon}, PityAt}`; `Lore` — the
Grove's **own** small lore codex (`GroveLoreEntryData {LoreId, Title, Text}`), deliberately **not**
`discovery.json`'s `Lore` array (see "Why the Grove has its own lore list" below).

**`garden-library.json`** (`GardenLibraryData`): `PlotCount`; `SeedSpeciesData {SeedId, DisplayName,
GrowthHours (3-24), Source ("shrine"|"starter"), UnlockId, ArtKey}`; `CrossPollinationData {ParentA,
ParentB, ResultVarietyId}` — the curated, deterministic matrix, order-independent; a **self-pair**
(`ParentA == ParentB`) is what a lone plot of that seed harvests, so every seed needs exactly one;
`VarietyData {VarietyId, DisplayName, ArtKey, HerbariumEntry}`; `RecipeData {RecipeId, Inputs[]:
{VarietyId, Count}, Output ("decor"|"dye"|"cosmetic"), OutputId}`.

**`expedition-library.json`** (`ExpeditionLibraryData`): `DestinationData {DestinationId,
DisplayName, UnlockSource ("start"|"habitat"), UnlockId, DurationHours (1-12), PartySize (1-3),
ArtKey}`; `ExpeditionOutcomeTableData {DestinationId, Entries[]: {Kind ("story"|"trinket"|"look"),
Id, Weight, IsCommon}, PityAt}`; `LoreStoryData {StoryId, Text, CodexCategory}`.

Each `*Data` has a `*Validator` (unique snake_case ids, positive weights, ascending thresholds, every
cross-reference resolving) following `DiscoveryLibraryValidator`'s exact idiom, run at content load
(`GameContent.Load`) and in the EditMode tests. `GroveLibraryValidator` additionally cross-checks
every shrine-sourced habitat/decor's `UnlockId` against `discovery.json`'s real `ShrineData
.GroveUnlockId`s and refuses two different habitats/decor claiming the same one;
`GardenLibraryValidator` does the same for shrine-sourced seeds (and takes `GroveLibraryData` too, so
a seed cannot also claim a habitat's or decor's shrine id) and cross-checks `"decor"`-output recipes
against `grove-library.json`'s `garden_craft`-sourced decor (both directions: every such recipe
resolves, and every such decor piece has exactly one recipe); `ExpeditionLibraryValidator` cross-
checks a `"habitat"`-gated destination against `grove-library.json`'s habitats.

### Why the Grove has its own lore list

The draft's gift `"lore"` reward and this design's affinity `"lore"` reward could have reused
`discovery.json`'s `Lore`/`DiscoveryProgress.LoreIds` (the compendium's existing seed). Reconciled
decision: **don't** — reusing it would make `grove-library.json` validate against, and its authored
volume plan against, `discovery.json`'s own points-of-interest capacity math (`DiscoveryLibraryValidator`'s
"enough content that no stage's layout runs short" check), which is out of this PR's scope and risks
the discovery layer's existing goldens/tests. The Grove instead owns a small, self-contained lore
list (`GroveLibraryData.Lore` → `GroveProgress.LoreIds`), and the Board owns its own story list
(`ExpeditionLibraryData.Stories` → `ExpeditionProgress.StoriesUnlocked`). A later PR (the compendium
work already anticipated a codex) can present all three lore/story lists — discovery, Grove, Board —
as one reading list without any of them needing to be the same underlying save list.

### Deferred: cosmetic look rewards

The draft's `AffinityTierData`/gift/expedition-outcome `"look"` reward kind is **mechanically wired
up** (the `RewardKind`/`ItemKind`/`Kind` value is valid, `GroveRules`/`GardenRules`/`ExpeditionRules`
call `CosmeticRules.UnlockOrRefund` when it fires, `CosmeticLibrary.SourceGrove` exists and is
validated the same way `SourceDiscovery` is) but **no content authors it in this PR**. Reason: doing
so would mean adding new avatar-scope cosmetic categories/options to `cosmetic-library.json`, which
is the *live* content behind the already-built Avatar screen's customization UI — changing it is a
content-only change to an existing screen's visible behaviour, which does not fit a "Core + content +
save + tests, no screens" deliverable even though no `.cs` screen file would change. Deferred to
whichever of D3/D4 adds the actual cosmetic art and reviews the Avatar screen's customization surface
together.

### Content volume shipped (v1, some trimmed from the draft — see "Producer-reviewable" below)

3 habitats, 13 decor (6 shrine, 3 starter, 2 affinity-shared, 2 garden-craft), 12 seeds → 36
varieties (12 self-pairs + 24 curated cross-pairs, exactly the matrix the draft wanted), 12 recipes
(10 dye, 2 decor), 8 destinations, 5 affinity tiers × 10 species (50 rows), 13 Grove lore entries (10
per-species affinity + 3 gift-only), 8 expedition stories (one per destination).

## 3. Save (schema 10) and offline clock

`OfflineClock` (`src/BeastCraft.Core/Common/OfflineClock.cs`) is `IdleRewardCalculator.Elapsed`'s
wall-plus-monotonic reconciliation, extracted byte-for-byte (`IdleRewardTests`'s full suite passes
unchanged — idle behaviour is provably identical) into `ElapsedMs(lastUtcTicks, lastMonoMs,
nowUtcTicks, nowMonoMs, out clamped)`, plus `UtcTicks`/`MonotonicMs` helpers. `IdleRewardCalculator
.Elapsed` now delegates to it. Every Grove/Garden/Expedition timer (Feed/Play's daily cooldown, the
gift clock, plot growth, an expedition's remaining time) reads through it: the same anti-tamper
stance everywhere (a clock moved back or forward is silently clamped to the provable elapsed time, no
penalty, no bonus).

`PlayerSave` gains three schema-10 fields (after `LookTokens`, additive, mirroring exactly how schema
9 added `Achievements`/`LookTokens` — commit `2573124`'s `AddCompendium`):

- **`Grove`** (`Grove.GroveProgress`): `HabitatsUnlocked`, `UnlockedDecorIds`, `PlacedDecor`,
  `Affinity` (per beast: XP, tier, cooldown/gift anchors, pity, `PendingGifts`), `LoreIds`, `Items`
  (`GroveItemInventory` — the generic Grove item pool: grown/crafted/found items keyed by id with
  counts, exactly the seam the producer additions asked for).
- **`Garden`** (`Garden.GardenProgress`): `Plots`, `VarietiesDiscovered`.
- **`Expeditions`** (`Expeditions.ExpeditionProgress`): `Active`, `StoriesUnlocked`, `Pity`.

`SaveMigrations.AddGrove` (9 → 10) fills all three at their empty defaults — the same "nothing to
backfill" stance as `AddIdle`/`AddCompendium`; a returning player's shrine-granted `GroveUnlockIds`
are untouched and read live by `GroveRules.RefreshUnlocks` the first time the Grove opens, so nothing
already earned is lost. Goldens v1–v10 all round-trip (`rich-v9.input.json` frozen, `rich-v10`/`min-v10`
added; see `docs/design/progression-and-saves.md`, "Schema 10"). `SaveValidator` gained a `ValidateGrove`
pass (ranges, the gift cap, duplicate/negative item counts, malformed plots/expeditions).

## 4. Rules code

`Grove.GroveRules` (Core, static, non-throwing): `RefreshUnlocks` (shrine/start conditions →
`HabitatsUnlocked`/`UnlockedDecorIds`, idempotent), `PlaceDecor`/`RemoveDecor` (scope, slot bounds,
single placement), `Feed`/`Play` (daily cooldown via `OfflineClock`, flat XP, no decay, tier-up +
reward), `RefreshGifts`/`CollectGift`/`CollectAllGifts` (the cap, the seeded-roll-plus-pity idiom).
`Garden.GardenRules`: `Plant`, `GrowthProgress`/`IsReady` (via `OfflineClock`), `Harvest` (a lone
plot's self-pair) / `HarvestPair` (two ready plots' curated hybrid — both deposit into
`GroveItemInventory` and record the herbarium find), `Craft` (consumes Grove items, deterministic
output). `Expeditions.ExpeditionRules`: `Send` (party-size and one-active-per-destination checks —
**does not touch the sent beasts at all**, per the producer decision below),
`TimeRemainingHours`/`IsReturned`, `Collect` (seeded roll + pity; a `"trinket"` outcome deposits a
Grove item, a `"story"` outcome unlocks a story). All refuse-and-no-op on bad input (the
`CosmeticRules`/`IdleRewardCalculator` idiom). Full unit coverage: growth math, pity guarantees
(forced-non-common-at-the-threshold tests for both gifts and expedition outcomes), matrix
determinism, `OfflineClock`-clamp behaviour (shared `OfflineClockTests` plus per-domain cooldown/
growth/timer tests), cooldowns, the gift cap's "never loses banked time" behaviour, migration/golden
round-trips, every validator's authoring-mistake catches.

## 5. Screens (D4 — not built in this PR)

Unchanged from the draft's plan: Grove hub (`GroveScreen : GameScreen`, inner `Tabs` for Glade/
Garden/Board — the `Tabs` widget and the `HomeTab.Grove` slot already exist, see "What already
existed" above), a painted glade background per habitat, beasts as wander sprites, drag-to-place
decor via the existing `Hotspot`/drag primitives. Glade: tap a beast → Feed/Play/affinity-bar panel.
Garden: plots as tappable slots (empty → plant picker; growing → progress ring; ready → one-tap
harvest, or select two ready plots to cross-pollinate), plus **Collect All** for the idle-first
player. Board: destination cards (locked/available/away/ready); tap ready to collect, tap available
to open the bench-*and-party* picker (beasts are never locked away, so the picker is not bench-only —
see the producer decision) sized to `PartySize`. Notifications: the existing Android local-
notification hook from the screens PR, off by default, plus an in-app toast on entering Home when
something is ready.

## 6. NPCs (D2 — status: BUILT)

Built as planned, with one reconciliation against real code found while implementing it: the file is
**not** a new `npc-dialogue.json` — `content/data/Npc/dialogue.json` already existed (Hearthglen's
tutorial dialogue layer) and already used exactly this shape
(`Tutorial.DialogueLineData {NpcId, Conditions[], Text, Priority}`, `DialogueBook.Resolve` = highest-
priority line whose conditions hold, ties by specificity), because it was built anticipating this PR
(its own readme said so). D2 extends that one file and its `DialogueBook`/`DialogueValidator` rather
than creating a second dialogue system. Cast v1 = 4: the Grove Keeper (already existed, now also
tends the Grove hub), the Trader, the Wandering Scholar, Forest Folk — ~10-15 condition-resolved lines
each, plus each NPC's existing/added no-condition default line.

**Conditions are `"kind:params"` strings** (`Tutorial.NpcConditionKinds`), built live from account
state every time by `Npc.NpcRules.BuildFacts` — affinity tier (any beast, and per species, cascading:
a beast at tier 3 also emits its tier-1 and tier-2 facts so a "tier 2+" line still matches), herbarium
variety found/count, decor placed count, habitat unlocked, an expedition story unlocked, a region's
boss cleared, Grove or dialogue-layer lore found, a Grove item held, and a side-story chapter/story
complete. Deliberately one small, growable list, exactly per the design's extensibility ask: D3 adds
`colour_form_owned`/`location_soothed` the same way, a new kind constant plus a new fact emitted from
`BuildFacts`, with `DialogueBook.Resolve` itself untouched.

**Producer addition, delivered**: Grove items drive NPC interactions — `Tutorial.RequestData
{RequestId, NpcId, Conditions[], ItemId, Count, RewardKind, RewardId, Text}`, checked with
`GroveItemInventory.GetCount` and consumed with `TryConsume` through `Npc.NpcRules
.IsRequestAvailable`/`CanFulfillRequest`/`FulfillRequest`, exactly the seam D1 built. 4 requests
shipped (one per NPC), each granting the dialogue layer's own lore.

**Producer addition, delivered**: NPC side stories — `Tutorial.SideStoryData {StoryId, NpcId,
DisplayName, Chapters[]}`, each chapter (`SideStoryChapterData {ChapterId, Conditions[], Text,
RequestItemId, RequestCount, RewardKind, RewardId, LoreId, NextChapterId}`) chained by
`NextChapterId` (validated acyclic, exactly one entry chapter, every chapter reachable) rather than
by array order, so the validator can catch a broken chain. Progress is its own small save list, per
the design's own steer: `PlayerSave.Npc.SideStories` (`List<Npc.SideStoryState> {StoryId,
ChaptersCompleted}`), not folded into `DialogueState.LinesSeen`. 3 arcs shipped, one each for the
Grove Keeper, the Wandering Scholar and the Trader (3-4 chapters each), never required for the main
campaign. Completing an arc is surfaced as an achievement (`AchievementKinds.SideStoryComplete`,
`AchievementData.StoryId` naming the arc); found dialogue-layer lore is surfaced through
`Discovery.CompendiumRules.NpcLoreEntries(save, dialogue)` (cheap — a pure listing over
`DialogueBook.AllLore` and `PlayerSave.Npc.LoreIds`, deliberately **not** folded into
`CompendiumRules.Completion`/its `Percent`, which stays scoped to what it already covered). No
compendium *screen* change is in this PR (no screens); D4 wires the method up. **No title, decor or look
reward is ever granted directly by a request or a chapter's `RewardKind`** — v1 content authors only
`"lore"` (requests) and `"none"` (chapters, which instead always carry their own `LoreId`), the same
"wire it, don't author it yet" stance D1 took for `"look"` rewards; `RewardKind` still mechanically
supports `"decor"`/`"look"` (validated, `Npc.NpcRules.ApplyReward` grants them) for a later pass once
there is a reason to spend new decor/look content on an NPC reward specifically rather than the
existing Grove/Garden/Board unlock paths.

**Save.** `PlayerSave.Npc` (`Npc.NpcProgress`) was added to the **same** schema-10 shape in place
(schema 10 had not shipped on any other branch — see `progression-and-saves.md`, "Schema 10, extended
in place"), not a new schema 11.

**Validator.** `Tutorial.DialogueValidator.ValidateRequestsAndSideStories`, hooked into
`GameContent.Load` beside the existing dialogue/scene checks: every request's/chapter's item id
resolves to a real `GroveItemInventory` id (a Wildgarden variety, a crafted dye or an expedition
trinket — cross-checked against `garden-library.json`/`expedition-library.json`), every reward id
resolves for its kind, every condition's kind is known, ids are unique, ids are snake_case, text
follows the content bible's length and tone rules, and every side story's chapter chain is acyclic.

## 7. Peaceful clears ("soothing") and colour evolutions (D3 — status: BUILT)

**Peaceful clears.** `Campaign.CampaignRules.Soothe` (Core, next to `ResolveBattle`, whose clear path
it reuses): refused for anything but a plain `MapNodeType.Battle` location — never an Elite den, a
Gate, a Boss, a Kinship trial or a tutorial Story/Trial (Hearthglen's fights are refused twice over:
the tutorial-region check, and Hearthglen's own fights are `Trial`-typed, so the node-type check alone
already excludes them) — or for a region with no soothing item set, an item not in it, or a team
naming no owned beast. On success it consumes one item from `Grove.GroveItemInventory.TryConsume` (the
same seam D2's NPC requests read) and pays out **exactly** what a combat win at that node grants — the
named team's full beast XP (the clear bonus, as if none were knocked out; every other owned beast's
bench XP), gold, material and gear/cosmetic drops, first-clear bonuses — by building a synthetic,
already-won `Session.BattleSessionResult` (a `BattleResult(PlayerVictory, …)`, one not-defeated
`Battle.BattleUnit` per named beast) and handing it to the very same `Session.BattleSession
.ApplyRewards` a fought battle pays through, seeded with `CampaignRules.BattleSeed(node,
LossesAt(run, node))` — this node's *current-attempt* seed, the same one a real attempt right now
would use — so a soothe can never reroll or improve on what fighting would have paid (RNG stream
parity, the producer's own ask). No battle is simulated, so no skill practice XP is credited and the
avatar does not take part (a deliberate D3 scope decision: the ask was "full beast XP", not avatar
XP). The node then clears exactly as a win does (`Clear`, `RevealAround`: reveal, and a loss streak at
that node resets), `Campaign.CampaignProgress.LocationsSoothed` counts it (never decreases, account-
wide, replays included — the `location_soothed` NPC fact, cascading like `decor_placed_count`), and
`achievements` (when given) evaluates as usual.

Data-driven, per region (`Grove.GroveLibraryData.Soothing`, `SoothingRegionData {RegionId, ItemIds[]}`
— a few Wildgarden varieties per region, so the player has a choice; cross-validated by
`GroveLibraryValidator.ValidateSoothingAndColourForms` against every mainline and post-game region of
`regions.json` (full coverage required) and every item id against `garden-library.json`/
`expedition-library.json`, the same two-pass shape as D2's `DialogueValidator
.ValidateRequestsAndSideStories`). **Producer-reviewable: per-region, not per-enemy-family** — simpler
to author and to look up at the one call site that has `run.RegionId` on hand; a family-scoped table
would need the encounter shape too, for no clear player-facing benefit v1. Content: 3 Wildgarden
varieties per region, 11 regions (10 mainline + the post-game region — soothing works there too;
nothing in the design restricted it to mainline). One achievement, `LocationsSoothed` (threshold 3,
"Peacekeeper" title); one Grove Keeper dialogue line reacting to the first soothe.

**Colour evolutions.** **Producer-reviewable, the cleanest model found**: reuses the *existing*
per-species cosmetic system (`Economy.CosmeticLibrary`/`CosmeticRules`, already live behind the Avatar
screen's sibling — every roster species already has its own `cosmetic-library.json` categories, e.g.
`kirin_horn`) end to end, adding **no new save shape at all**. A colour form
(`Grove.GroveLibraryData.ColourForms`, `ColourFormData {ColourFormId, SpeciesId, CosmeticCategoryId,
CosmeticOptionId, ItemId, ItemCount}`) names a species-scoped `DiscreteOption` cosmetic category/option
pair (`Source = "grove"`, `UnlockId` = the row's own `ColourFormId` — cross-validated by
`ValidateSoothingAndColourForms`, the same `CheckLook` idiom D1/D2 already used for a Grove-sourced
look) and the Grove item (a grown variety or a crafted dye) it costs. `Grove.GroveRules
.TryUnlockColourForm` spends the item (`GroveItemInventory.TryConsume`) and unlocks the option
account-wide through the **existing** `CosmeticRules.UnlockOrRefund` (already-owned grants look tokens
instead of wasting the item, exactly D1's "don't waste a found reward" stance); *applying* it to a
specific beast, and switching an owned beast back to its default colour or to another owned form, is
the **existing** `CosmeticRules.TrySetOption`/`OwnedBeast.Appearance` — no new method, since it already
does precisely "owned once per species, worn per beast instance, switchable freely". This is why no
save field was needed: `PlayerSave.Cosmetics` (ownership) and `OwnedBeast.Appearance` (which owned
form each beast wears) already had the exact shape "owned colour forms per species + which is applied
per instance" needs. `Npc.NpcRules.BuildFacts` emits `colour_form_owned:{id}` by checking
`save.Cosmetics.Has` against each authored form's key — no new save list to maintain either. Content:
one collectible colour form per roster species (10; each its species' existing cosmetic categories
gain a third: `{species}_colour_form`, `natural` default + a free `dusk` starter alongside the
grove-locked dye form — a `DiscreteOption` category needs both per `CosmeticLibraryValidator`, and a
free starter gives the player something to try in that category before ever visiting the Wildgarden).
One Wandering Scholar NPC request (`colour_form_owned:phoenix_colour_duskrose` as a condition) asks to
see an owned colour form.

**Rendering (a tiny presentation seam, not a screen).** No accent-mask pipeline exists for roster
beasts today (`docs/design/presentation-and-vfx.md`, "Element accents": the base+overlay split is an
*enemy*-only technique, built for element accents, not shipped for any of the ten roster beasts) — the
art dependency the design flagged. A whole-sprite tint fallback (multiplying the beast's existing plain
sprite by a per-form colour, the same `Tint` mechanic the art manifest already uses for alias sprites)
is the documented seam for D4: read the applied form via `CosmeticRules.Worn(save, beastId,
"{species}_colour_form", cosmetics)`, and if its `ArtKey`/data carries a tint colour, multiply the
beast's draw call by it; per-species accent masks remain the better long-term answer once that art
exists. Nothing in D3 depends on this — it is Core/content/save only, with a documented, not-yet-taken
hook for whichever of D4's screens draws beasts.

**Pacing (`Tooling/BalanceSim`).** `--mode campaign --soothe-fraction <f>` (0-1, default 0 = off, the
default `campaign-pacing-report.md` untouched — confirmed byte-identical): that fraction of ordinary
Battle-node fights is resolved as a guaranteed, never-knocked-out clear (the same XP/gold/loot payout
the model already pays a win) instead of the clear-chance roll; Elites, Gates and Bosses are always the
existing roll. A probe run (`--soothe-fraction 0.5`) confirmed the producer's ask directly: every
"levels at every gate and boss" row stays "ok", identical to the default report — see
`docs/balance/tuning-log.md`, "Grove D3: peaceful clears (soothing) — pacing probe".

**Filed away, not decided** (conflicts with "no combat power"): Grove items granting a battle bonus
(e.g. a full speed bar at the start). Revisit with a balance plan; nothing in this PR or its data
shapes assumes it will happen.

## 8. Map influence — still "needs more thought" (unchanged from the draft)

No combat-power or balance-axis effects in any option. Producer decision carried forward: **option C
(Trader cosmetics gated by Grove/Garden milestones) only for v1**; defer ambient region dressing and
new POIs to whenever the map layer they depend on is settled. Not touched by this PR.

## 9. Art needs (slots, unchanged in shape from the draft)

3 habitat glade backgrounds; 13 decor sprites; 12 seed icons × ~4 growth-stage frames; 36 variety
icons (share bases + tint, like the existing element-tinting pipeline); 8 expedition destination
cards; per-species affinity emotes (5 tiers × 10 species, an overlay on existing beast art); 4 NPC
portraits/idle poses (D2). All `ArtKey`s shipped in this PR's content are placeholder strings only
(never validated against the art manifest — `ArtReferenceValidator` only checks roster/enemy/skill
art, confirmed by reading its call site in `GameContent.Load`).

## 10. Producer-reviewable decisions from this reconciliation pass

Beyond the draft's own "Producer decisions"/"Producer additions" (both carried forward verbatim
above and in §5-8), this pass made these calls where the draft under-specified or where current code
constrained the choice — flagged for review, not hidden:

1. **Decor trimmed from 20 to 13.** The 6 shrine-fixed + a handful of starter/affinity/garden-craft
   pieces covers every `Source` value with real content; the rest is pure authoring volume, an easy
   follow-up whenever art exists for more.
2. **Recipes trimmed from 15 to 12** (10 dye, 2 decor) for the same reason.
3. **Destinations unlock `"start"` or `"habitat"`, never `"shrine"`.** The draft's default was shrine-
   gated; no shrine ids were reserved for destinations in the shipped `discovery.json` (its 12
   shrines exactly cover the 3 habitats + 6 decor + 3 seeds this PR needed), and reserving new ones
   would touch `discovery.json`'s own points-of-interest capacity validation and goldens — out of
   this PR's scope. Instead, 3 of the 8 destinations gate behind a Grove habitat being unlocked
   (which is itself shrine-gated), giving the same "explore to unlock" arc one hop removed.
4. **Feed and Play have independent daily cooldowns**, not one shared "affinity action" cooldown —
   the draft said "no stacking" without saying whether the two actions share a clock; independent
   cooldowns double the daily engagement surface without changing the tone.
5. **Gift interval is a single value per tier** (`GiftHoursByTier`, 6h→2h), not a randomized band
   within "2-6h" as the draft's prose suggested. A fixed-per-tier interval keeps the offline-accrual
   math exact (`elapsed / interval = gifts owed`) with no loss of the "narrows with tier" feel; a
   randomized band would need to either re-roll on every check (breaking determinism/offline banking)
   or store a rolled-but-not-yet-consumed interval (a fourth clock field for no player-visible gain).
6. **Cross-pollination's "self-pair" mechanic** — a lone plot's harvest — is an addition this pass
   made to give the matrix a defined result for every seed grown alone (the draft didn't specify one).
   Every seed has exactly one self-pair entry, validated.
7. **A beast may be sent on more than one expedition at once.** The producer decision that sending
   never locks a beast implies no natural reason to block it either; `ExpeditionRules.Send` only
   refuses a *second send to the same destination* while one is already away there, never a repeat
   beast across different destinations. Flagged in case the producer wants a "one destination at a
   time per beast" rule for flavour reasons even though nothing mechanical requires it.

## 11. Sequencing

1. **D1 — Data + Rules + Save 10. Status: BUILT.** `*LibraryData`/`*Validator`/`*Rules`/save sections,
   `OfflineClock` extraction, migration + goldens, full unit coverage. No screens.
2. **D2 — NPC dialogue, requests and side stories. Status: BUILT.** Data (extending
   `content/data/Npc/dialogue.json`), validator, resolution (`Npc.NpcRules.BuildFacts`/
   `ResolveAndMark`), save (`PlayerSave.Npc`, folded into schema 10 in place), request fulfilment and
   chapter chains reading `GroveItemInventory`, `AchievementKinds.SideStoryComplete`. No screens — see
   §6.
3. **D3 — Soothing and colour evolutions. Status: BUILT.** `CampaignRules.Soothe` (ordinary locations
   only, reward parity with a combat win via `BattleSession.ApplyRewards`), `Grove.GroveLibraryData
   .Soothing`/`ColourForms` + validator, `Grove.GroveRules.TryUnlockColourForm` (reusing the existing
   `Economy.CosmeticRules` ownership/selection shape — no new save field), `CampaignProgress
   .LocationsSoothed` (schema 10, extended in place; goldens regenerated), two new NPC condition kinds
   (`location_soothed`, `colour_form_owned`), one achievement, the `Tooling/BalanceSim
   --soothe-fraction` pacing probe (default report unchanged). No screens — see §5. The whole-sprite
   tint fallback for colour forms (no accent-mask art exists for roster beasts yet) is a documented,
   not-yet-taken seam for D4.
4. **D4 — Grove screens.** Hub, Glade, Garden, Board, the Grove tab's real content (replacing the
   placeholder), toasts, the Android local-notification hook, the colour-form tint hook (§7). Art
   integration once assets exist.

Risks carried forward: JsonUtility's list-only constraint on nested data (mitigated throughout by the
existing flat-list idiom, including the flat `(SpeciesId, Tier)` affinity table); the "two
expeditions" naming collision (mitigated by explicit qualification everywhere, §"Naming" above); the
deferred cosmetic-look content touching the live Avatar screen (mitigated by not authoring it yet,
§2 "Deferred").
