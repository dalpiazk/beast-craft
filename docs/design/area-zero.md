# Area zero: Hearthglen, the onboarding region (r00)

**Status: BUILT.** Names, locations, dialogue and hint text are **DRAFT pending producer review**
(the text lives in the data files listed below; the content bible holds the flavour). The producer
decisions this build follows are at the end.

Hearthglen is the first thing a new player plays: a short, fixed, hand-authored region below the
tree line where Verdant Hollow begins. It teaches the battle, the map and the systems around them
in about 20-25 minutes, and it is how a new player gets their first three beasts, one stance at a
time. Returning players never see it.

## World and tone

A sheltered vale of warm hedgerows, an old mill and orchards, with a shrine whose keeper — the
**Grove Keeper**, who later tends the Grove hub — remembers the first Beastbinders. The Gloam is
only a faint shimmer at the far hedge. The arc: the Keeper bonds the player to their first beast at
the shrine (their first binder art, a small mending light), a villager half-remembers the Seals as a
story, and at the vale's edge the player calms their first big gloamed creature and is sent on:
"Verdant Hollow starts past this ridge, and the Gloam is thicker there." No Seal is claimed here; the
binding limit stays at its starting 12. Tone follows the content bible: wonder first, nobody dies
(creatures are defeated, knocked out, gloamed).

## Structure

One fixed map (`regions.json` `TutorialRegions`, `FixedNodes`), played once, no `NodeMapGenerator`
and no map seed (the seed only draws each fight's battle seeds). Twelve locations in a line:

| # | Location (DRAFT) | Type | Lv | What it teaches |
| ---: | --- | --- | ---: | --- |
| 0 | Keeper's Shrine | Story | 1 | The mentor; the bond; 2 Fury Draughts |
| 1 | Clover Meadow | Battle (open board) | 1 | Scouting, the party, skills, binder arts |
| 2 | Hedgerow Lane | Battle (open board) | 1 | Elements, turn order |
| 3 | Tumbledown Wall | Battle | 2 | Obstacles (Hollow layouts from here on) |
| 4 | Bramble Stone | Trial | 2 | The kinship trial: the 2nd beast joins |
| 5 | Brookside Mill | Battle | 2 | Two beasts; Team Bonds exist |
| 6 | Old Orchard | Battle | 2 | Consumables |
| 7 | Grove Stone | Trial | 3 | The 3rd beast joins: a full team, Combined Arms |
| 8 | Lantern Camp | Rest | 3 | Camp training, the catch-up, idle rewards |
| 9 | Ridge Path | Battle | 3 | The full team |
| 10 | The Far Hedge | Battle (den) | 3 | The finale: a gloamed champion |
| 11 | Hollow's Edge | Story | 3 | The Grove and Kinship teases; the way on |

Nine fights at levels 1-3. Each is a fixed `encounter-library.json` template (`hg_*`) at its own
`DifficultyOverride`: never the calibrated table, never the early-region easing (the validator holds
both). The first two fights stand on the open board (`OpenBoard`; a placeholder meadow until the
vale's painted backdrop lands); from the obstacle beat on, fights stand on Verdant Hollow's painted
battlefields and obstacle layouts (`BattlefieldRegionId: r01`). The finale's enemies take the
element neutral against the player's three beasts (`AdaptiveElements`, below). Hearthglen's clears
keep their own first-clear keys (`r00/{shape}`), so they never use up Verdant Hollow's first-clear
bonuses. Clearing the last location completes Hearthglen: it is locked again (played once) and
Verdant Hollow unlocks.

## The first three beasts

- **1st:** a free pick of all ten at New Game (`StarterPickScreen`: illustrated portraits, stance
  and element, a personality blurb, the stance explainer).
- **2nd:** won at the first trial: any beast of the **next** stance in the cycle
  Vanguard → Ranged → Skirmisher → Vanguard after the 1st pick's.
- **3rd:** won at the second trial: any beast of the remaining stance.
- Every pick joins at **level 1** with its species' default loadout. No species twice; every team
  is one of each stance, so Combined Arms (`DistinctStances`, `MinCount 3`) fires for every legal
  trio. That makes the finale a guaranteed "your team bonds" moment — a dependency to keep in mind in
  any Team Bonds rebalance.
- A won trial's pick waits (`StarterPicks.PendingStep`): the map offers it before anything else,
  and no location can be entered until it is made; a pick left unmade (the app closed) is offered
  again on Continue.
- **Skip the tutorial** (on the New Game picker): the same picker three times in a row, one stance at
  a time, then Hearthglen's completion rewards (the items its story locations hand over: 2 Fury
  Draughts) and straight to Verdant Hollow. No fights, no XP, no hints.

**Camp catch-up.** Lantern Camp trains one beast (the camp rule every region has) and then raises
every beast below the leader to the leader's level (Hearthglen's camps only). The picks joined at
different times; after the camp they stand level, so the order the stances were picked in does not
decide the finale.

## Tutorial beats and hints

`content/data/Tutorial/hints.json` holds the beats as data (`HintData`: `HintId`, `Trigger`,
`Conditions`, `Title`, `Text`, `Anchor`, `Priority`). A hint shows once, over the screen its
trigger belongs to (map, encounter, battle start, first critical hit, first status, results, pick,
camp), pointing at its anchor widget; ready hints of one moment follow one another; a hint pauses a
battle while it shows. Dismissed hints are saved (`PlayerSave.Tutorial.SeenHintIds`); every hint
has "Turn hints off", and the settings have a "Tutorial hints" row (`PlayerSettings.TutorialHints`).
Eighteen hints cover the design's sixteen beats (placement and preview, elements, skills and
cooldowns, turn order, crits and statuses, obstacles, binder arts, the second beast and Team
Bonds, the full team, consumables, results, camp, idle rewards, the Gloam); the Grove and Kinship
teases are the Keeper's lines.

## The Grove Keeper and dialogue

`content/data/Npc/dialogue.json` is the NPC dialogue layer the Grove design proposes, first used
here: `Npcs`, `Lines` exactly as the Grove's `DialogueLineData { NpcId, Conditions, Text, Priority }`
(resolved by `DialogueBook.Resolve`, highest priority, then the more specific), plus ordered
`Scenes` a story location plays (its lines carry `scene:{id}` so free resolution never picks
them). The dialogue box is minimal: a placeholder portrait (the speaker's initial on a disc) until
the Keeper's portrait lands, their name, one line at a time.

## Balance (verified)

`dotnet run --project Tooling/BalanceSim -c Release -- --mode hearthglen --compositions 16 --samples 2`
fights every fixed fight against every legal pick combination in pick order — the 10 solo picks, the
31 (1st, 2nd) pairs, the 90 ordered trios (each one-per-stance trio from each stance it can start
from) — at the level profile a player who wins every fight reaches (normal XP; the camp's catch-up),
on the battlefields and elements the game uses. Targets (producer, round 2): at least 90% for every
combination in every regular fight; the finale's weakest trio at about 60% or more (a high mean is
fine). Current: every regular fight 100% for every combination; the finale (x0.930) weakest 65.6%,
mean 97.9% (66-84% weakest across three seeds). The element adaptation lifted the finale's weakest
trio from 31% to 53% at the untuned multiplier (x1.02); it is neutral at the tuned one (65.6% either
way), because the weakest trios are weak for reasons other than the chart.
`docs/balance/hearthglen-report.md` is the committed report.

Hearthglen's normal XP lands players in Verdant Hollow around level 3. `--mode newplayer
--start-level 3` measures that arrival: r01 stage 1 squad 86.8% (from 78.6% at level 1), horde
91.0%, solo 68.1%; the first node squad 96.8%, horde 97.8%; stages 2-4 unchanged (their levels are
already above 3). No easing changed (squad stays under 97%); see the tuning log.

## Idle rewards

A played Hearthglen counts toward the idle rewards' progress level (`CampaignRules.ProgressLevel`):
its cleared fights while it is played, then its max level (3) once behind the player
(`HearthglenCleared` and not `Skipped`), so the idle chip pays right after the tutorial. A skip
counts nothing; the campaign tools' blank saves are unaffected (the campaign pacing report
regenerates byte-identical).

## Save (schema 7)

`PlayerSave.Tutorial` (`TutorialProgress`): `HearthglenCleared`, `Skipped`, `SeenHintIds`. The
`AddTutorial` migration (6 → 7) counts any save that owns a beast as past Hearthglen — never
offered, no pick pending, Verdant Hollow unlocked, no beast touched; a beast-less save starts
Hearthglen like a new game. See progression-and-saves.md.

## Code map

- Core: `Campaign/RegionLibraryData.cs` (`TutorialRegions`, `FixedNodeData`), `CampaignRules`
  (`FixedMap`, `Visit`, `CompleteTutorial`, `SkipTutorial`, the camp catch-up, `PlanFor` with the
  save for adaptive elements, `RewardModifiersFor(run, …)` for the first-clear scope),
  `RegionLibraryValidator.ValidateTutorialRegions`, `Tutorial/` (`StarterPicks`,
  `TutorialProgress`, `ElementAdaptation`, `Hints`, `Dialogue`).
- Presentation: `Screens/TutorialViewModels.cs` (`StarterPickViewModel`, `StoryViewModel`,
  `CampViewModel`, `HintService`), `GameSession.NewGame(first pick)`, `NewGameSkippingTutorial`,
  `Pick`, `LocationName`.
- Game: `Screens/TutorialScreens.cs` (`StarterPickScreen`, `TrialPickModal`, `StoryModal`,
  `CampModal`, `HintModal`, `RegionCardModal`); the walkthrough (`--walkthrough DIR`) captures the
  whole first session.
- Tools: `Tooling/BalanceSim/HearthglenReport.cs` (`--mode hearthglen`), `--mode newplayer --start-level`.

## Art slots (PR C, no logic change)

A Hearthglen region-map background (gentler than the Hollow's); a home-vale meadow battle backdrop
for the open-board fights (today a placeholder meadow); the Grove Keeper's portrait and idle pose
(shared with the Grove hub); the ten starter portraits already exist.

## Producer decisions

Round 1 (2026-09-28):
- 2nd beast: any beast of ONE missing stance, never both — the next stance in the cycle
  Vanguard → Ranged → Skirmisher → Vanguard; the 3rd any beast of the remaining stance. Hearthglen
  must be easy enough that stance order does not matter.
- Name "Hearthglen" (DRAFT), the Grove Keeper as mentor. Beasts join at level 1. A skip-tutorial
  option that still makes the stance-by-stance picks. 8-10 fights. Supersedes "pick 3 starters at
  New Game" in the kinship design. `CampaignRules.SuggestionFor` uses the eased plan.

Round 2 (2026-09-28):
- Finale: a level catch-up at Hearthglen's camp; tune so the WEAKEST legal trio wins about 60% or
  more (a mean of about 95% is fine); regular fights stay at 90%+.
- Hearthglen XP is normal; measure Verdant Hollow for a trio arriving around level 3; change no
  easing unless squad is over 97%, and then only report it.
- Hearthglen keeps its own first-clear keys. Fights before the obstacle beat use open boards.
- A minimal reusable Camp screen. The StarterPickScreen replaces the temporary New Game pick.
- The finale's enemy elements adapt to the player's trio: neutral against all three, deterministic.
