# Grove: Beast Grove, Wildgarden, Expeditions

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

## 6. NPCs (D2 — not built in this PR)

Unchanged in shape from the draft, now explicitly scoped as **D2**: a light, data-driven dialogue
layer (`npc-dialogue.json`, `DialogueLineData {NpcId, Conditions[], Text, Priority}`, highest-priority-
line-whose-conditions-hold resolution, a `DialogueState {LinesSeen[]}` save section), cast v1 = 4
(Trader, a Grove Keeper, a Wandering Scholar, generic Forest Folk). **Producer addition, carried
forward**: Grove items drive NPC and map interactions — NPCs make requests for grown/crafted/found
Grove items; fulfilling them advances their story and gives rewards. This PR's `GroveItemInventory`
(`PlayerSave.Grove.Items`) is exactly the seam D2 reads and spends from: a request is "hold N of item
X", checked with `GetCount`, granted with `TryConsume`, no new inventory concept needed.
**Producer addition, carried forward**: NPC side stories — optional multi-step chapter chains
(condition → request → reward/lore → next chapter) per NPC, tracked in the save and the compendium,
never required for the main campaign. D2 should model a chapter chain as its own small save list
(`ChapterId` reached per NPC) rather than overloading `DialogueState.LinesSeen`.

## 7. Peaceful clears ("soothing") and colour evolutions (D3 — not built in this PR)

**Producer additions, carried forward verbatim:**

- **Peaceful clears.** ORDINARY battle locations only (never elites, gates, bosses or Kinship trials)
  can be cleared by giving the right Grove item: the location counts as cleared, the path opens, and
  the player gets FULL rewards including full beast XP. D3 reads `GroveItemInventory`/`TryConsume`
  the same way D2's NPC requests do — no new inventory. The pacing sim must account for peaceful
  clears (a soothed ordinary fight still pays full XP, so pacing is roughly unchanged) but must still
  require winning gates and bosses by combat; this is a `Tooling/BalanceSim` change for D3, not D1 —
  D1 makes no pacing change at all (confirmed identical reports, see the PR).
- **Colour evolutions.** Grove outputs (plant varieties, and especially the `"dye"` recipe outputs
  this PR's `garden-library.json` already ships — see §2) can permanently shift a beast into a
  collectible colour variant, reusing the accent-overlay tint technique; NPCs react to them and
  requests may ask to see one. D1 already gives D3 its raw material: dyes are ordinary
  `GroveItemInventory` entries, so D3 only needs the beast-side accent-mask/save-flag work, not a new
  economy.
- **Filed away, not decided** (conflicts with "no combat power"): Grove items granting a battle bonus
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

1. **D1 — Data + Rules + Save 10 (this PR).** `*LibraryData`/`*Validator`/`*Rules`/save sections,
   `OfflineClock` extraction, migration + goldens, full unit coverage. No screens.
2. **D2 — NPC dialogue, requests and side stories.** Data, validator, resolution, save flags, chapter
   chains reading `GroveItemInventory`.
3. **D3 — Soothing and colour evolutions.** Peaceful-clear rule (ordinary locations, full rewards),
   colour-variant save flags and accent masks, the `Tooling/BalanceSim` pacing check for soothed
   clears.
4. **D4 — Grove screens.** Hub, Glade, Garden, Board, the Grove tab's real content (replacing the
   placeholder), toasts, the Android local-notification hook. Art integration once assets exist.

Risks carried forward: JsonUtility's list-only constraint on nested data (mitigated throughout by the
existing flat-list idiom, including the flat `(SpeciesId, Tier)` affinity table); the "two
expeditions" naming collision (mitigated by explicit qualification everywhere, §"Naming" above); the
deferred cosmetic-look content touching the live Avatar screen (mitigated by not authoring it yet,
§2 "Deferred").
