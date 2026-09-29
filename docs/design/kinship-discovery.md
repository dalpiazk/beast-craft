# Kinship, fog and points of interest (PR A)

**Status: BUILT (PR A).** Every name, lore entry, shrine and cache text, trial and look is **DRAFT
pending producer review** (the text lives in the data files below; the content bible holds the
flavour). PR B (the compendium, achievements and look tokens; Core/content/save/tests done, screens
next) builds on the seams listed at the end — see [compendium-achievements.md](compendium-achievements.md).
The producer decisions this build follows are at the end; Hearthglen (area-zero.md) supersedes the
original "pick 3 starters at New Game".

A player leaves Hearthglen with a trio, one beast per stance. Seven **Kinship sites** over the first
six regions let the other seven beasts join, one choice at a time, so the whole roster is owned by the
end of Frostmere whatever the trio. Each region's stage maps gain **fog** and an off-path layer of
**points of interest** (shrines, lore stones, caches, vistas and the Kinship sites) to find, and a
region **explored to 100%** pays a look.

## Kinship sites

`content/data/Campaign/discovery.json` `KinshipSites` (rules: `BeastCraft.Discovery.KinshipRules`):

| Site (DRAFT) | Region | Stage | Theme first (`Preferred`) | Bond condition (flavour) | Trial |
| --- | --- | ---: | --- | --- | --- |
| Mossbound Stone | r01 Verdant Hollow | 1 | treant, golem, griffin, kirin | a Vanguard fought | `kin_trial_r01` |
| Cinderglow Stone | r02 Emberreach | 1 | phoenix, kirin, golem, basilisk | no beast knocked out | `kin_trial_r02` |
| Tidewrack Stone | r03 Tidefall | 1 | leviathan, frost_wyrm, basilisk, kirin | a Ranged beast fought | `kin_trial_r03` |
| Galecrest Stone | r04 Stormcrag | 2 | thunderbird, griffin, kirin, phoenix | a Skirmisher fought | `kin_trial_r04` |
| Ironroot Stone | r05 Rustwood | 2 | tarasque, golem, treant, basilisk | no beast knocked out | `kin_trial_r05` |
| Rimeglass Stone | r06 Frostmere | 1 | frost_wyrm, leviathan, kirin, griffin | a Vanguard fought | `kin_trial_r06_rime` |
| Deepshade Stone | r06 Frostmere | 3 | basilisk, leviathan, thunderbird, tarasque | no beast knocked out | `kin_trial_r06_shade` |

- **The offer** (`KinshipRules.Offer`): the first two beasts the player does not own, in the site's
  `Preferred` order (the region's theme first), then the roster's order. Deterministic: a pure
  function of the site and the species owned. Never an owned beast; two while two are left.
- **All ten by r06.** Ten species, three owned after Hearthglen, seven sites: every claim takes one
  of the unowned seven, so every beast is owned once every site is claimed, for every one of the 30
  one-per-stance trios and whichever of the two is chosen (tested exhaustively: 30 trios x both
  orders of r06's two sites x every choice at every site). The seventh site offers the last beast
  alone (see "Open questions").
- **The trial.** A fixed template (`encounter-library.json`, `kin_trial_*`, Draft: a themed champion
  and two escorts) at the site's map row's level plus its `LevelOffset` (1), at its own
  `DifficultyOverride` (never the calibrated table, never the early-region easing), on the region's
  battlefields. The optional **bond condition** is flavour only: the results say whether it was met.
  A trial pays **no XP, gold or loot** (a consumable taken is spent): its reward is the beast.
- **Retry on loss.** A loss changes nothing but `DiscoveryProgress.KinshipLosses`, which seeds the
  next attempt (`KinshipRules.BattleSeed`): a fresh battle every time, as at a map location.
- **The choice** (`KinshipRules.Pending`/`Choose`): a won trial sets `PendingKinshipId`; the map
  offers the choice before anything else (the Hearthglen trial's popup, `TrialPickModal.Kinship`), and
  again on Continue if the app closed. The chosen beast joins at the **fielded team's mean level − 3**
  (rounded down, at least 1; the team that fought the trial), knowing its species' default loadout at
  skill level 1 (`StarterPicks.AddBeast`). The site is claimed, its lore entry recorded, its point found.
- **When a site calls.** A Kinship site shines through the fog once the player has cleared a
  location on its row or above on that stage (`StageFog.DeepestLayer`), so every walk up the stage
  reaches it. Taking the stage's pass or lair while a site there is unclaimed asks first ("Leave the
  kinship stone?"); a site left behind waits for a revisit of the stage.
- **Existing saves.** A save that owned beasts before Kinship (six, from the old starter save) is
  never offered one it owns: its four missing species come from the next four sites it reaches, and
  a site with nothing left to offer becomes a **lore and cache stop** (`DiscoveryRules.Visit`: the
  site's own lore entry and its `FallbackCacheId`, and it counts as claimed).

## Fog

`BeastCraft.Campaign.MapFog`. Fog persists **per region** and never fogs again on a replay (producer
decision). It is kept on a fixed grid in the stage map's own logical coordinates, not on the nodes: a
replay draws a new map from a new seed, but ground already seen stays seen.

- **The grid** (`FogGrid`): a row for every map row and one between each pair (half-rows 0 to 20 on
  the shipped 11-row maps), a column for every lane and one between each pair and at each edge (9
  columns on the 4-lane maps). A location sits at an even half-row and an odd column; points of
  interest at an odd half-row and an even column, so they are near the trail, never on it.
- **Reveal rules.** An expedition's start lifts the trailhead band (half-rows 0-1) and the pass or
  lair's cell (the goal is always in sight). Clearing, camping at or trading at a location lifts an
  ellipse around it (2.6 half-rows by 3.2 columns: its neighbours and the points near it) and the cell
  of every location it leads to, and records the row reached. A **Vista** lifts a much larger ellipse
  (7 half-rows by 9 columns: a chunk of the map).
- **Save.** `RegionProgress.Fog`: one `StageFog` per stage visited, the seen cells as a hexadecimal
  bit set (`Cells`, compact and JsonUtility-safe) and `DeepestLayer`.
- **Where.** The six discovery regions (r01-r06). Hearthglen is shown fully revealed (its design has
  no fog beat); r07 onward and the post-game region show no fog (their rules still record it, harmlessly).
- **No pacing change.** No random draw anywhere in the fog: the campaign pacing report was
  byte-identical before the roster flow changed.

## Points of interest

An off-path discovery layer, never a `MapNodeType`: the pacing model (the node map the balance
simulator routes) never sees one. `BeastCraft.Discovery.PoiLayout` lays out a region's points, every
stage at once, from the region's own **discovery seed** (`RegionProgress.DiscoverySeed`, assigned
once by the region's first expedition, independent of any map seed), so a replay never moves a point
or changes what it holds.

- **Kinds**: **Shrine** (records a discovery and grants its `GroveUnlockId`, held in
  `DiscoveryProgress.GroveUnlockIds` for the Grove to consume later), **LoreStone** (a DRAFT lore
  entry, kept in `DiscoveryProgress.LoreIds` for the compendium), **Cache** (fixed gold, materials and
  sometimes a look: never rolled), **Vista** (lifts a chunk of fog), **KinshipSite** (the trial).
- **Density** (`discovery.json` `Regions`): 2-3 per stage map in r01-r02, 3 in r03, 3-4 in r04-r06,
  the Kinship sites included. The other kinds are drawn by weight (Shrine 2, LoreStone 3, Cache 3,
  Vista 2), at most one Vista a stage; lore entries, caches and shrines are dealt in file order, each
  once per region (the validator checks there are enough for every stage at its most).
- **Visiting**: a point can be visited while its stage's map is shown, once seen (`PoiModal`). A found
  point shows what it held.
- **Rewards** are non-combat or existing economy, within the pacing caps: caches pay 25-250 gold (one
  or two battles' worth) and at most two Essence Shards (the lowest material tier), or
  a DRAFT look; shrines and lore stones grant ids only. The campaign model visits every point it sees:
  the focus-skill gates hold with the caches' shards included (see "Balance").

## Region completion

`DiscoveryRules.Completion`: (map rows walked + points found) / (rows + points), per region, derived,
never stored. Rows walked: every row of a cleared stage, else `DeepestLayer` + 1. 100% only when
every row of every stage is walked and every point found. The map header shows **Explored N%**; tap
it for the region progress panel (per stage: rows walked, places found, and **Revisit** for a stage
already reached — a new map of it, the expedition in progress left, its stage progress kept, the fog
still lifted). The first 100% grants `CompletionLook` (a DRAFT avatar look, a new `discovery`
cosmetic source) and `CompletionGold`, with a toast (`DiscoveryRules.TryComplete`,
`GameSession.CheckCompletion`; `RegionProgress.Completed`).

| Region | 100% look (DRAFT) | Gold | Cache look (DRAFT) |
| --- | --- | ---: | --- |
| r01 | Mosstrail Cape | 60 | Fern Sprig |
| r02 | Emberlight Cap | 90 | Cinder Scarf |
| r03 | Tidewalker Coat | 120 | Shell Circlet |
| r04 | Galewander Cloak | 150 | Cloudrunner Vest |
| r05 | Copperleaf Crown | 180 | Rustleaf Mantle |
| r06 | Rimeweave Garb | 210 | Snowdrift Hood |

## Save (schema 8)

`PlayerSave.Discovery` (`DiscoveryProgress`): `ClaimedKinshipIds`, `PendingKinshipId`,
`PendingKinshipLevel`, `KinshipLosses`, `GroveUnlockIds`, `LoreIds`. `RegionProgress` gains
`DiscoverySeed`, `Fog`, `FoundPoiIds`, `Completed`. The `AddDiscovery` migration (7 → 8) seeds every
campaign region (the expedition in progress's map seed for its region, else one derived from the
region id), reveals every stage cleared before fog existed and the expedition in progress's trailhead
and cleared ground, and touches no beast. See progression-and-saves.md, "Schema 8".

## Balance

Full numbers: `docs/balance/campaign-pacing-report.md`, `docs/balance/new-player-report.md`, and the
tuning log's "Kinship: roster growth, trials and the r02 fall-off".

- **Campaign pacing** (`--mode campaign`, now the Kinship roster flow: the Hearthglen trio fielded,
  every point the fog reveals visited, each revealed site's trial fought until won and its first offer
  joining the bench at the fielded mean − 3): **every gate met** (self-check). Total battles p50 515
  (400-600; trials included, 1.1 per site); fielded and avatar within 1 of every gate and boss; beasts
  owned at r01-r06's bosses 4, 5, 6, 7, 8, 10; bench gap 5.0-5.7 from r03 (target 5-8; r03 sits on
  5.0 — the r03 site was moved to stage 1 for it); lowest bench beast 3.0-6.0 behind; focus skill 17 /
  77 / 173 / 301 battles to L5 / L10 / L15 / L20 with the caches' shards included (gates 15-20, 72-88,
  162-198, 270+). Caches pay about 940 gold and 9 shards over a campaign.
- **Kinship trials**: tuned so the weakest trio (with the recruits it has by then) wins at least half
  the time: trio means 93-97%, weakest 50-63% (`DifficultyOverride` 0.79-0.93).
- **The r02 fall-off — resolved by calibrating on the owned roster** (superseding the paragraph this
  once was: the r02 easing extension and its `BossScale` are gone; see `docs/balance/tuning-log.md`,
  "Never-blocked targets"). The open question below ("calibrate for the owned roster, or ease longer?")
  is answered: the shipping table is now calibrated on the TYPICAL team from the roster a player
  actually owns at that point (`Tooling/BalanceSim --mode typical`, a producer decision), not a
  scouted pick from the whole roster, so the Kinship roster-flow gap that easing used to paper over
  mostly does not exist any more. Only r01 keeps a small remainder (`EasingShapeScales` x0.88/0.83/
  0.92/0.93, fading to 0 by its own last stage); r02 and r03 need none. What gap is left — mainly a
  genuinely unlucky pick, which the typical-team model's own "weak" measure shows can still fall well
  short even of the new, softer targets — is adaptive assist's job instead: a per-location losing
  streak eases that specific fight, resetting on a win (see battle-system.md, "Adaptive assist and
  guidance"). The calibrated table and the tuned report are unaffected by any of this (a producer
  factor, not a roster question).

## Screens

- **Map** (`HomeScreen`, `MapViewModel`): soft painted fog over the unseen cells (placeholder: layered
  soft discs; locations under it are not drawn), the points seen as rounded badges in their kind's
  colour and glyph (glowing while unvisited, a check once found), their names, the **Explored N%**
  chip under the header.
- **Popups**: `PoiModal` (a point's name, kind, words and action), `RegionProgressModal`.
- **Kinship**: the trial's preview is the encounter screen (`EncounterViewModel.ForKinship`) with the
  site's words and its bond condition as a banner; the choice of two is the Hearthglen trial's popup
  (`TrialPickModal`, now over an `IBeastPicker`: `StarterPickViewModel` or `KinshipPickViewModel`).
- **Toasts**: a visit's findings; the 100% reward.
- Screenshots: `--screen kinship-map | kinship-poi | kinship-trial | kinship-choice | region-progress`.

## Seams for PR B

**Built (Core/content/save/tests); see [compendium-achievements.md](compendium-achievements.md) for the full design:**

- **Compendium**: `DiscoveryProgress.LoreIds` (lore found), `ClaimedKinshipIds` (sites claimed), the
  roster page's silhouettes ("Meet it at a Kinship site"); `LoreEntryData` carries a title and text.
  `CompendiumRules` is the pure derived view; `DiscoveryProgress.KinshipJoins` (schema 9) added the one
  fact needed beyond PR A's save shape (which site a beast joined through).
- **Achievements**: `RegionProgress.Completed` and `DiscoveryRules.Completion` per region (now also
  `AchievementRules.Evaluate`, `content/data/Progression/achievements.json`).
- **Look tokens**: caches and 100% looks unlock through `CosmeticRules.Unlock` (a look already owned
  unlocks nothing today: the place to convert it into tokens) — now `CosmeticRules.UnlockOrRefund`.

**Still open (a later feature):**

- **The Grove**: `DiscoveryProgress.GroveUnlockIds` holds the shrines' unlocks until it consumes them.

## Content

- `content/data/Campaign/discovery.json`: regions, Kinship sites, lore (DRAFT), caches, shrines;
  validated by `DiscoveryLibraryValidator` (content load and the EditMode tests).
- `encounter-library.json`: the seven `kin_trial_*` templates (Draft).
- `cosmetic-library.json`: twelve `discovery` looks (six 100% rewards, six cache looks; DRAFT names,
  placeholder art keys).

## Producer decisions

From the kinship-discovery design pass (2026-09-27), overriding its defaults:

- Starters: the player picks 3, one per stance — now Hearthglen's stance-cycle picks (area-zero.md).
- Kinship sites adapt: each offers a choice between two of the player's not-yet-owned beasts,
  preferring the region's theme. All ten beasts reachable by r06 (7 sites, r06 holding two).
- Recruit level: a few levels below the fielded team's average (about −3), skills at level 1; not level 1.
- Fog persists per region and never re-fogs on replay.
- POI density: 2-3 in r01-r02, rising to 3-4 in r04-r06.
- The pacing sim models a representative starter pick plus the recruit timing, and keeps the gates.

This build's calls (for review):

- A Kinship site calls once its row is reached (so no walk misses it); other points need the fog lifted.
- A trial pays no XP or loot; its difficulty is tuned so the weakest trio still wins about half the time.
- Hearthglen stays fully revealed; r07+ show no fog or points.
- Completion counts map rows walked (not individual locations: a replay's map changes them).
- The early-region easing is r01 only now, fading to nothing by its own last stage; r02 and r03 need
  none (see "Balance").

## Open questions

- ~~The seventh site can only offer the last beast (7 sites, 7 unowned): a choice of one. Add an
  eighth species, or accept it as "the last beast chooses you"?~~ **Resolved (PR B)**: "the last beast
  chooses you" — flavour only (`KinshipResult.SoloOffer`), no eighth species; see
  compendium-achievements.md, "'The last beast chooses you'".
- ~~The new player still sits below the calibrated tiers from r03 on without the easing... Calibrate
  for the owned roster, or ease longer?~~ **Resolved**: calibrate for the owned roster
  (`--mode typical`, a producer decision), plus adaptive assist for what is still left after a loss;
  see "Balance" and `docs/balance/tuning-log.md`, "Never-blocked targets".
- Bond conditions are flavour only; a small look for meeting them would be a PR B item.
- Kinship sites are placed on fixed stages; a stage left behind needs Revisit (the region progress panel).
