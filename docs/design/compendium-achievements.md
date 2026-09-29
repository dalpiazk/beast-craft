# Compendium, achievements and look tokens (PR B)

**Status: BUILT — Core, content, save (deliverable 1) and the screens (deliverable 2)**: the
compendium screen, the achievements list and title picker, and the look-token shop are all built; see
"The screens" below (what "Seams for the screens step" became). The producer decisions this build
follows are at the end.

Builds on [kinship-discovery.md](kinship-discovery.md) ("Seams for PR B") and
[progression-and-saves.md](progression-and-saves.md) ("Schema 9"). The Collector persona: a
compendium (a pure derived view over save state) plus **deterministic achievements — no RNG
anywhere** — that award **text titles** (display only, equipped or shown, never a stat), and **look
tokens** that turn an otherwise-wasted duplicate reward into a small, spendable currency for an
explicit, data-driven pool of looks. **No combat power from any of this.**

## Compendium

`BeastCraft.Discovery.CompendiumRules`, over `DiscoveryContent` (the same bundle
`DiscoveryRules`/`KinshipRules` read: the discovery library, the region library, the roster). Nothing
new is stored for it beyond what Kinship already needed (below): it is computed fresh from
`PlayerSave` every time, never cached in the save.

- **Beast entries** (`CompendiumRules.BeastEntries`): one per roster species, in roster order.
  `CompendiumBeastState` is `Unknown` (never owned, not currently offered), `Offered` (a live preview:
  one of the species a **pending** Kinship choice offers right now — never stored; the beast not
  chosen goes back to `Unknown` once the choice is made) or `Owned`. An owned entry also carries
  `JoinedThroughKinship` and, when true, `KinshipSiteId` — **which** site it joined at.
  - This needed one small, justified addition to the save: `DiscoveryProgress.KinshipJoins`
    (`List<KinshipJoinRecord> {SpeciesId, SiteId}`), appended once by `KinshipRules.Choose` when a
    beast actually joins (never on a fallback claim, which adds no beast). Without it, "found
    through Kinship, at this site" cannot be told apart from a Hearthglen pick or an older save's
    starting six after the fact — the roster order alone does not carry that, and a fabricated rule
    ("the first three are always the Hearthglen trio") breaks for a pre-Kinship save that already
    owned six. The record is species -> site only (no level, no date): the smallest fact that answers
    the compendium's question, and it can never gain a second entry for one species (a site never
    offers a species already owned).
- **Lore entries** (`CompendiumRules.LoreEntries`): every `discovery.json` `Lore` entry, in file
  order, each with `Found` (`DiscoveryProgress.LoreIds`). The title and text travel with the entry
  regardless of `Found`; a screen redacting an unfound entry's text is presentation's call, not
  Core's.
- **Enemies encountered: not part of the compendium.** The save tracks no per-enemy "seen" state
  today — only points of interest, lore and Kinship sites. Adding one would be new tracking (a save
  field plus a hook on every battle) this deliverable has no cheap, save-derivable seam for, unlike
  the beast and lore entries above (both already exist as save state PR A wrote). Flagged for the
  producer to decide whether it is worth a dedicated feature later.
- **Completion** (`CompendiumRules.Completion`): beasts owned, lore found and Kinship sites claimed,
  each over its total, and one combined `Percent` (done / total across all three, matching
  `DiscoveryRules.Completion`'s own style: 100 only when every count is complete). Feeds the
  `CompendiumPercent` achievement kind (below) and, later, a compendium progress bar.

## Achievements

`content/data/Progression/achievements.json` (`AchievementLibraryData`; validator
`AchievementLibraryValidator`; rules `BeastCraft.Progression.AchievementRules`). ~20 achievements
(the shipped file has 20), each:

- a stable `AchievementId` and a DRAFT `DisplayName` (the list entry's own name);
- a `Kind` (`AchievementKinds`): `BossCleared` / `RegionExplored` / `RegionLoreComplete` need a
  `RegionId`; `KinshipSitesClaimed` / `BeastsOwned` / `CompendiumPercent` / `AvatarLevel` /
  `BeastLevel` need a `Threshold`; `AllBossesCleared` / `AllRegionsExplored` / `AllKinshipClaimed` /
  `AllLoreFound` need neither (the validator enforces which);
- a `TitleId` (stable) and DRAFT `TitleText` — the title text the achievement awards.

Every condition is read straight off existing save and content state: `RegionProgress.BossCleared`,
`DiscoveryRules.Completion(...).IsComplete`, `DiscoveryProgress.ClaimedKinshipIds`, distinct species
owned (`KinshipRules.OwnedSpecies`), lore found, `CompendiumRules.Completion(...).Percent`,
`Avatar.Level`, a beast's `Progress.Level`. **No new state was added for achievements themselves**
beyond the save section that records which are earned (below) — every condition was already
answerable from PR A's save shape (plus the one Kinship-join record the compendium needed, above).

### Evaluating (`AchievementRules.Evaluate`)

Pure and idempotent: given a save and an `AchievementContent` (the library plus the same
`DiscoveryContent` the compendium reads), it checks every **not-yet-earned** achievement's condition,
records newly met ones (`PlayerSave.Achievements.EarnedIds`) and adds their titles
(`OwnedTitleIds`), and returns the list newly earned (for a toast). Calling it again with the same
save state earns nothing twice; calling it on an unrelated save never touches a stat, a beast, gold
or a look — the only things it ever writes are `Achievements.EarnedIds` and `.OwnedTitleIds`.

**Hooked at the same places PR A awards discovery rewards**, so it fires automatically wherever those
already do (no new call sites needed in the screens layer for these three):

- `DiscoveryRules.Visit` (a shrine, lore stone, cache or Kinship-fallback visit) — `DiscoveryContent`
  now carries an optional `Achievements` field; when set, `Visit` evaluates it after its own reward
  and returns newly earned achievements on `DiscoveryResult.TitlesEarned`.
- `DiscoveryRules.TryComplete` (a region's 100%) — same field, `CompletionReward.TitlesEarned`.
- `KinshipRules.Choose` (a beast joins) — same field (it takes the same `DiscoveryContent`),
  `KinshipResult.TitlesEarned`.
- `CampaignRules.ResolveBattle` — a new **optional** `AchievementContent achievements = null`
  parameter (mirroring how it already takes `EconomyContent economy` for gear and cosmetic rewards),
  evaluated into `CampaignResult.TitlesEarned`. Optional and defaulted to keep every existing call site
  source-compatible; `null` (unset) means "achievements are not wired up here" exactly as before this
  parameter existed. **Evaluated after every resolved battle** (a plain node clear, a stage's pass, a
  region's lair, a Hearthglen trial — not only a boss clear, screens deliverable): every victory branch
  of `ResolveBattle` now returns through one `Finish` step that evaluates achievements when
  `achievements` is given, so `AvatarLevel` / `BeastLevel` / `BeastsOwned` (and any other
  save-state-only kind) fire the moment they are met rather than waiting for a boss. Idempotent either
  way (`AchievementRules.Evaluate` skips an already-earned achievement), so calling it this often costs
  nothing beyond the check itself.
- `GameContent.Discovery` (content loading, `BeastCraft.Presentation.Content`) always wires
  `DiscoveryContent.Achievements` up from the loaded `achievements.json`, so all four hooks are live in
  the real game and in every test that uses the shared test content; `NodeBattle.cs` passes
  `content.Achievements` as `ResolveBattle`'s 6th argument (the screens deliverable's one-line wire-up).
- **Session start** (`GameSession.StartWith` / `Continue`): achievements are also evaluated once when a
  session begins, so a save retroactively earns whatever it already meets — an older save, or a level
  or Kinship count reached between sessions — rather than only from a fresh trigger. Newly earned
  titles queue **one consolidated toast** (`GameSession.PendingToasts`, shown when the map next appears)
  instead of firing silently or one toast per achievement; a producer call, see "Decisions" below.

### "The last beast chooses you" (open question, resolved)

kinship-discovery.md's open question — the seventh Kinship site can only offer the last unowned
beast (7 sites, 7 unowned after Hearthglen) — is resolved as flavour, not a new species:
`KinshipResult.SoloOffer` (`Offer.Count == 1`) is a computed flag a screen can read to phrase the
choice as "the last beast chooses you" instead of "choose one of two". `KinshipRules.Offer` already
returns fewer than two beasts when fewer are left (unchanged, tested exhaustively in
`KinshipTests.EveryTrio_...`); nothing about the offer or the choice call itself changes.
`KinshipPickViewModel` (the screens deliverable) reads `SoloOffer` directly: its `Title` becomes "The
last beast chooses you" and its `Subtitle` "...the last beast joins you: no choice needed" instead of
"choose who joins you".

## Look tokens

Per the seam: `CosmeticRules.Unlock` of an already-owned, non-free look does nothing today — the
reward is silently lost. Two additive pieces close that, both deterministic (no RNG):

- **Duplicate conversion** (`CosmeticRules.UnlockOrRefund`): tries to unlock a look; if it is
  unknown or free it does nothing (unchanged); if it is **already owned**, it grants
  `CosmeticLibrary.DuplicateLookTokens` (`cosmetic-library.json`, a small fixed amount — 5 — the same
  for every duplicate) look tokens (`PlayerSave.LookTokens`) instead of wasting the reward. Wired into
  the two places the seam names: `DiscoveryRules.GrantCache` (a cache's look) and
  `DiscoveryRules.TryComplete` (a region's 100% look). Boss, milestone and battle-drop unlocks are
  **not** touched — `PickDrop` already never rolls a look the save has, and touching the boss/milestone
  paths would risk the balance/economy numbers the gate requires stay byte-identical; the two discovery
  paths above are exactly what the seam text calls out.
- **Spending** (`CosmeticRules.SpendLookToken`, `LookTokenResult`): unlocks a look directly from an
  **explicit, data-driven pool** — `CosmeticOptionData.TokenPurchasable` + `TokenPrice`, validated to
  need a positive price and a `shop` or `drop` `Source` (never a `boss`, `milestone` or `premium`
  look). The shipped pool is every `drop` look (24 of them, all `Rarity` 0/common today, so every one
  is priced 80): a deterministic way to buy a look the game would otherwise only ever hand out on a
  battle-drop roll, without touching the roll itself. `TokenPrice` is authored per option, not derived
  from `Rarity` by any rule — `CosmeticLibraryValidator` only requires it positive on a
  `TokenPurchasable` look, so a rare drop look added later could ship at 160 (or any other price)
  without a code change; nothing ships at that price yet because no `drop` look is rare yet.
  `CosmeticLibrary.TokenPool()` lists it (sorted by key, stable). Refuses on an unknown or non-pool
  look, an already-usable one, or insufficient tokens; never partially spends.

Both are pure `PlayerSave` state (`LookTokens`, an `int`, never negative) and pure library data —
no new content file.

## Save (schema 9)

`PlayerSave.Achievements` (`Progression.AchievementProgress`): `EarnedIds`, `OwnedTitleIds`,
`EquippedTitleId` ("" = none, always one of `OwnedTitleIds` when set — `SaveValidator` reports it
otherwise, never silently drops it: `PlayerSave.EnsureInitialized` never corrects ids or
cross-references, only fills missing collections, matching its documented contract). `PlayerSave.LookTokens`
(`int`, never negative). `DiscoveryProgress.KinshipJoins` (`List<KinshipJoinRecord>`, above). The
`AddCompendium` migration (8 -> 9) is purely additive (empty achievements, no titles, no tokens,
nothing earned by the migration itself); see progression-and-saves.md, "Schema 9".

## Content

- `content/data/Progression/achievements.json`: 20 DRAFT achievements (names and title text pending
  producer review); validated by `AchievementLibraryValidator`, hooked into `GameContent.Load` (content
  load) and the EditMode tests, exactly like `DiscoveryLibraryValidator`.
- `content/data/Cosmetics/cosmetic-library.json`: `DuplicateLookTokens` (top level, 5) and
  `TokenPurchasable` / `TokenPrice` on the 24 `drop` looks; validated by the extended
  `CosmeticLibraryValidator`.

## Decisions for the producer to review

- The look-token pool is every `drop` look (a deterministic alternative to the battle-drop roll for
  cosmetics only); the producer may prefer a different explicit pool (e.g. `shop` looks too, or a
  curated subset) — the data model (`TokenPurchasable` + `TokenPrice`) supports either without a code
  change.
- `DuplicateLookTokens` = 5 and the 80-token price on every shipped look are placeholder economy
  numbers, not calibrated against anything (there is no existing "look token" economy to anchor them
  to); 160 was the intended price for a rare drop look, but none is rare today, so it does not ship.
- Enemies encountered are explicitly **not** in the compendium (see "Compendium", above) — flagged,
  not decided against; a producer call for a later pass.
- The 20 achievements, their DisplayName/TitleText and thresholds are a first DRAFT pass (mirroring
  the region and Kinship content's own DRAFT status), not a final content bible entry.
- **`glossary.json` was not touched.** It is scoped to skill-text terminology only (`Status` / `Combat`
  / `Stance` / `Passive`, each validated against `[[...]]` marks in skill and status descriptions —
  see `GlossaryValidator.ValidateText`); "Title", "Look token" and "Compendium" never appear in skill
  text, so adding them would misuse the file (and likely fail its own validator). No other
  player-facing term glossary exists in the codebase today.
- **Session-start retroactive earning toasts silently into one consolidated line**, not one toast per
  achievement and not fully silent: `GameSession.PendingToasts` gets "New title(s) earned: ..." once,
  shown when the map next appears (the same drain the region-100% toast already uses). A producer may
  prefer fully silent (no toast at all for something the player did not just do) or per-achievement
  toasts; the one-line choice here was to acknowledge it without a barrage on an old save's first load
  after this PR ships.
- **The Avatar tab is a minimal identity card for this deliverable** (level, equipped title, the way to
  the achievements/title screen and the look-token shop), not the full Beastbinder screen (skills,
  gear, wardrobe editing) screens.md's "Known gaps" already called out as a later PR; a producer call
  on how much of that to fold into this tab versus a dedicated one.

## The screens

Built this deliverable (`src/BeastCraft.Presentation/Screens/CompendiumViewModel.cs`,
`AchievementsViewModel.cs`, `LookTokenShopViewModel.cs`; `src/BeastCraft.Game/Screens/CompendiumScreens.cs`).
Full detail: [screens.md](screens.md#compendium-achievements-and-look-tokens-the-collector-persona).

- **Compendium screen** (`CompendiumScreen`, reached from the Roster tab): `CompendiumRules.BeastEntries`
  / `LoreEntries` / `Completion`, read against `GameSession.Content.Discovery`. The roster's existing
  silhouette hint (`RosterViewModel.SilhouetteHint`, "Meet it at a Kinship site") is made precise per owned
  beast via `DiscoveryProgress.FindKinshipJoin` ("Found through Kinship at &lt;site&gt;").
- **Achievement list and title picker** (`AchievementsScreen`, reached from the Avatar tab):
  `PlayerSave.Achievements.EarnedIds` / `OwnedTitleIds` / `EquippedTitleId`; `AchievementLibrary.All` /
  `Get` for each entry's `DisplayName` and `TitleText`; `EquippedTitleId` is set directly (any owned id,
  or "" to clear — there is no dedicated rule call, the same way `AvatarAppearance` fields are set
  directly through `CosmeticRules.TrySetOption` for cosmetics, but a title has no unlock/lock state to
  check, only ownership). The equipped title also shows beside "Beastbinder" on the Results screen's
  avatar XP line.
- **Toasts**: `DiscoveryResult.TitlesEarned` / `LookTokens`, `CompletionReward.TitlesEarned` /
  `LookTokens`, `KinshipResult.TitlesEarned`, `CampaignResult.TitlesEarned` each feed
  `GameSession.ExtraRewardText` (" You earned the title \"...\" and N look tokens."), appended to the
  reward's own toast or Results note — the existing toast mechanism, no new one.
- **The wire-up**: `NodeBattle.cs` passes `content.Achievements` as `ResolveBattle`'s 6th argument, and
  `ResolveBattle` itself now evaluates achievements after every resolved battle (see "Evaluating",
  above), not only a boss clear.
- **Look-token shop** (`LookTokenShopScreen`, reached from the Avatar tab and from a beast's Gear &amp;
  bonds tab): `CosmeticLibrary.TokenPool()` for the list, `CosmeticRules.SpendLookToken` to buy,
  `PlayerSave.LookTokens` for the balance.
