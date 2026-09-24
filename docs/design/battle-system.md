# Battle System Design Proposal

Beast Craft is a narrative story-adventure with creature-collection and battle progression. It
draws its gameplay-depth inspiration from Sword x Staff's class/promotion/build systems and its
idle progression, but reframes all of it around a narrative core rather than a live-service idle
grind: the story is the spine, and the systems exist to give that story mechanical weight. This
document describes the **grid-based auto-battle** approach for that battle layer, how it sits on top
of the ScriptableObject data schemas already committed to the project, and the foundational
decisions the producer has now confirmed. Those decisions are settled; the runtime scaffolding built
against them lands alongside each revision of this document.

## Core loop

The player's avatar **does not fight directly**. The avatar is a non-combatant commander; the
creatures do the fighting on the grid. The avatar and the creature systems are built and customized
separately. The avatar's **appearance** is purely cosmetic and carries no stats; separately from
that, the avatar has **stats of its own** and equips non-cosmetic avatar gear that raises them, and
that gear is never drawn on the avatar (decision 6, amended). Beasts equip their own, separate kind
of gear.

Battles are an **auto-battler**, not a manual per-turn tactics game. The player's input is
front-loaded into two places — the build and the deployment — and the fight itself then plays out on
its own.

### Phase 1 — Placement (the player's turn to act)

Before the fight starts, the player **positions the deployed beasts on the grid**. This is the only
point at which the player moves a piece by hand. Party size is fixed by the battle format (1 / up to
4 / up to 6, see decision 2 below) and the board by the arena preset (see decision 1).

Positioning is therefore a real decision with real consequences, because everything afterwards is
resolved from where the beasts are standing: which enemies a short-range beast can reach on turn
one, whose area skills will catch which cluster, which flank a slow bruiser can actually cover.

### Phase 2 — Auto-resolution (the fight runs itself)

Once the battle starts there is **no player action menu**. Combatants take turns off the ATB
speed gauge (decision 3), one unit at a time, faster units more often, and on its own turn each
beast:

1. **Fires whichever of its equipped skills come off cooldown this turn** — which may be none, one,
   or several at once — with targeting resolved automatically from each skill's own authored rules
   (decision 4). Nothing chooses *between* the equipped skills: the beast's skill stack is a fixed
   rotation driven by per-skill cooldown counters (decision 5).
2. **Moves only as far as those skills need it to**, out of a single move-range budget shared by the
   whole turn (decision 7). Movement is not a separate step taken before the skills: a beast whose
   targets are already in reach does not move at all, and a skill that cannot reach anything even
   after spending the budget does not fire *and does not reset its cooldown* — it tries again next
   turn.

The player's leverage over that is the build and the placement, not a per-turn command. This suits
the game's shape: the story is the spine, and a fight that resolves from a good team composition and
a good deployment keeps the narrative pacing intact rather than interrupting it with a tactics
puzzle every encounter.

### What is not settled yet

- **Resource gating.** `SkillSO.ResourceCost` is authored but nothing spends it, and the resource
  itself ("mana" / "focus" / "stamina") is still unnamed. The cooldown rotation (decision 5) is now
  settled and implemented; how — or whether — a resource pool gates it on top is not. A skill that
  comes off cooldown and can reach a target currently always fires.
- **Tactical AI beyond combat stances.** Positioning is now decided at the level of *combat
  stances* (decision 8): each species is a Vanguard, Ranged or Skirmisher unit, which decides where
  its approaches stop, whether it walks into melee and whether it spends leftover movement backing
  off. Everything past that is still undesigned: a beast does not retreat *because it is hurt*,
  spread out against area skills, hold a choke point, guard or taunt, or coordinate focus fire with
  its team. Adding any of it would be a real design decision, not an implementation detail.
- **The basic attack.** Earlier drafts listed "a basic attack, a skill, or an item" as the turn's
  options. With no player menu, whether a beast has a fallback attack at all — or simply always has
  at least one short-cooldown skill in its rotation — is open, and items in battle are out of scope
  until there is a mechanism that would use them.
- **The avatar's gauge — settled (stage 3b).** The avatar now fills an ATB gauge of its own from
  its own Speed (decisions 3 and 6), so its cadence no longer depends on the team's size or speed.
  Still open: the avatar's authored base Speed and growth curve (the default is Speed 100, the
  reference Speed, at max level). The actives and passives were retuned for the slower cadence in
  milestone 2 (Rallying Cry and Mending Light to cooldown 2, buff and debuff durations 2 -> 3, the
  optional actives' cooldowns and Bloodlust / Storm Call's internal cooldowns shortened): measured
  with `--avatar-value`, the avatar is worth about 36.5 points of the scouted clear rate against
  41.6 on the old per-beast-turn cadence, with a 13.6% direct share of the team's output,
  three-quarters of it from its passives (tuning log, "Avatar retune").
- **Avatar level — progression exists and is wired into battle; its curve is not authored.** The avatar has its
  own level (TUNABLE STARTING DEFAULTS): `AvatarProgress` (save data: `Level`, `Xp`; persisted in
  `PlayerSave`, see [Progression, saves and the battle session](progression-and-saves.md), which
  also has the beast-level counterpart) and
  `AvatarProgression` (own constants, not the skill curve). A level costs `200 + 16 × level` XP
  (216 at level 1, 1,784 at level 99; max level 100). `AwardBattle(progress, outcome, enemyLevel)`
  pays **8 XP for any finished battle** plus a **clear bonus of `40 + 4 × enemyLevel`** on
  `PlayerVictory`. At an 80% clear rate that is one level per ~5 battles at every level, so the
  avatar **levels alongside the encounters** (the pacing target: median avatar level within 3 of
  the encounter level; measured within 0-1 over a 500-battle campaign, balance simulator
  `--mode pacing`); fighting below one's level pays less. Stats: `AvatarStatsSO.Growth` (a
  `GrowthRateCurve`, as a species has) with `GetStatAtLevel` / `GetStatsAtLevel(level)`, which
  scale `BaseStats` (then the max-level block) like `CreatureSpeciesSO.GetStatAtLevel` — MoveRange
  and CritChance exempt, Speed scales; no `Growth` keeps the block flat. Wired:
  `BattleAvatar.Create(book, activeLookup, passiveLookup, statsAsset, progress, gear, out passives)`
  builds the avatar from `statsAsset.GetStatsAtLevel(progress.Level)` at that level, and the
  simulator's avatar presets read their fixture block the same way (`--avatar-level`, default the
  encounter level). Still open: authoring the avatar's `AvatarStatsSO` and its growth curve.

## Encounter direction: PvE, not PvP — DIRECTION, NOT YET A CONFIRMED DECISION

**The game is expected to be PvE.** The player builds a team of beasts and fights **enemies that are
not the roster beasts**. An enemy side can be anything from **one large creature** to **two dozen
small ones**, with mixed mid-size groups in between. Beast-versus-beast (PvP) play is not the
target, so a beast's 1v1 record against the other roster beasts is at most a secondary signal. This
is the user's stated direction, recorded here so balance work targets it; it has not been through
the producer's confirmed-decision process yet.

What that implies for balance and for the systems:

- **Balance targets team contribution against encounters, not duels.** The useful question is how
  much a beast raises its team's chance of clearing a fight, and against which kinds of fight — not
  whether it beats another beast one on one. The balance simulator's primary mode now measures
  exactly that (see "Next steps").
- **Different encounter shapes reward different stat lines, and that is the point.** Against a
  **swarm**, area damage and bulk matter: many weak hits land on whoever is in front, and a
  single-target skill spends much of its power as overkill on small enemies. Against **one huge
  enemy**, sustained single-target damage and the bulk to survive its heavy hits matter, and area
  damage is mostly wasted. Mixed **packs** sit in between. A beast that is weak everywhere, or best
  everywhere, is the real balance problem; a beast that is best against one shape and middling
  elsewhere has a niche.
- **Area skills need something to hit.** `AreaBurst` is centred on the caster and never moves it, so
  in a skill stack it wants to come after something that walks the beast into the enemy cluster.
- **Large creatures cover several hexes — now built (was an open item).** A boss (giant) covers seven
  hexes and a mini-boss (champion) three; beasts stay one hex. Occupancy, pathfinding (the footprint
  has to fit along the route), targeting (distance to the nearest tile of a footprint) and area-skill
  overlap all follow; see "Unit footprints".
- **Two movement rules large PvE fights need — now built (were open items).** The simulator showed
  both gaps at encounter scale, and both are now Runtime rules in `BattleTurnExecutor` (see
  decision 7, "scaffold details"), so the simulator no longer works around either:
  - *Defeated units leave the grid* the moment they fall. Before, a fallen unit kept blocking its
    tile, a swarm's front rank died next to the beasts and walled off the rest, and almost every
    simulated swarm battle ended as a round-cap stalemate.
  - *Partial approach.* A unit that cannot reach range of its target this turn still walks its
    remaining move toward it. Before, it did not move at all, so two sides whose move plus range fell
    short of the gap between them (8 hexes between the zones on a Large board) waited forever, and
    the simulator's fixture enemies needed board-spanning movement. They now move like beasts.
- **Encounters mix enemy types, and enemy elements vary — now simulated.** The user's direction is
  that encounters should mix enemy types, and that enemy elements should vary across battles and
  sometimes within one enemy team. The balance simulator's default PvE run now reflects that: it
  draws random compositions per encounter shape — `solo` (one giant), `elite` (a giant or two
  champions with an escort), `squad` (4-6 mixed standard enemies) and `horde` (16-24, mostly swarm
  with a few archers and casters) — from a pool of simulator-only enemy types (giant, champion,
  brute, stalker, archer, caster, shaman, two swarm types), under a per-shape threat budget, and
  gives each composition an element scheme (one element for the whole team, one per type, one per
  unit, or none) so all ten elements appear. The pool, shapes and generator have since become game
  content and the game's own generator (see "Encounters as game content" below); the rules are in
  `Tooling/BalanceSim/README.md`.
- **Most of what the simulator fields targets the nearest enemy.** Whoever stands in front takes
  most of the hits, so front-line bulk and placement matter a great deal. The simulator's "pick off
  the weakest" enemies (stalker, caster and the champion's hex; the fixed set's wisps and stingers)
  aim at the beast with the least **current** HP (`SkillTargetingCriterion.CurrentHp`, decision 4);
  before that criterion existed they compared maximum HP, which never tracks damage taken. Every
  enemy type has a stance of its own (decision 8).

## Encounters as game content — BUILT; tiered difficulty targets DECIDED (user); the rest PENDING PRODUCER REVIEW

PvE encounters are game content, authored as JSON under `content/data/Encounters/`
and read by the game and the balance simulator alike, so the simulator calibrates exactly what the
game fields. The enemies, shapes and weights came over unchanged from the simulator's former
fixtures: the regenerated `tuned-report.md` differs from the one before the move only in its
provenance lines.

**Files.**

- `enemy-library.json` — every enemy type (giant, champion, brute, stalker, archer, caster, shaman,
  swarmling, stingling): id, name, role, `Threat`, stance, status resist, footprint, max-level base
  stats on a roster growth curve (`GrowthCurveId`, `medium`), and a kit in the skill library's
  `SkillData` shape. A skill's `Element` must be empty: every skill takes the unit's element, so one
  type serves every element. Enemy ids may not be roster species ids.
- `encounter-library.json` — the generated encounter `Shapes` (`solo`, `elite`, `squad`, `horde`:
  arena, `ThreatMin`-`ThreatMax`, `MinDistinctTypes`, the calibration's `TargetClear`, weighted
  variants of slots), the element-scheme
  weights (`SchemeWeights`: one element for the team 30, one per type 30, one per unit 25, none 15),
  authored fixed encounters (`Templates`: shape, arena, groups of enemy x count x elements, optional
  `DifficultyOverride`; the ten DRAFT region bosses, `boss_r01_hollow_warden` ... `boss_r10_apex_pair`,
  see `docs/design/progression-and-saves.md`, "Bosses (DRAFT)") and `DifficultyScale` (1.0). Shape ids must be exactly the
  drop tables' shapes: a cleared encounter pays out from its shape's cell.
- `encounter-difficulty.json` — **written by the simulator** (`--write-difficulty`), never by hand:
  the calibrated multiplier per (kit mode, shape, level 1 / 50 / 100). Schema 2 also carries each
  shape's target (`Targets`, and `TargetClear` per cell); a schema 1 file (one uniform target) still
  loads. `EncounterDifficultyTable.Warnings` (logged by the importer) flags a table calibrated to
  another target than the library's.

**Runtime** (`Runtime/Encounters`, namespace `BeastCraft.Encounters`): `EnemyLibraryValidator` and
`EncounterLibraryValidator` (the simulator's old fixture rules, plus id collisions, the drop-table
shape match and the arena fit of every lineup a shape can draw, placed in the generator's order, and
of every template, `EncounterFit`: a seven-tile enemy never fits a Small arena); `EnemyCatalog` (one cached in-memory species per enemy and
element through `BeastRosterBuilder.ApplySpecies`, one cached kit through
`SkillLibraryBuilder.ApplySkill` plus the element; flagged `DontUnloadUnusedAsset`);
`EncounterGenerator` (seeded draws of an `EncounterLineup` per shape: variant, counts, types, stance
order, arena fit, element scheme, a shared shuffled element deck); `EncounterDifficultyTable` (the
elemental cells, linear between calibrated levels, clamped outside); `EnemyScaling` (the multiplier
on HP, Attack, Defense, SpecialAttack and SpecialDefense, as the simulator applies it); `EncounterPlan`
(`Generate(library, enemies, shapeId, level, seed)` or `FromTemplate(library, enemies, templateId,
level)`, with `Preview(ScoutingDetail)` for scouting and `ToSetup()` for `BattleSession`); and
`EncounterLibrarySO`, filled by the Editor importer (Beast Craft/Data/Import Encounters, after Import
Beast Roster; all three files validated, all or nothing).

**Battle session.** `EnemySpec` takes an enemy-library id as its species (resolved through
`BattleContent.Enemies` when it is no roster species), an `Element` and a `StatMultiplier`; the
session fields the catalog kit in that element and scales the level-computed stats. `EncounterSetup`
carries `ShapeId` and `EncounterLevel` into `BattleSessionResult`, and
`BattleSession.ApplyRewards(save, result, content, dropTable)` pays out from them. See
`docs/design/progression-and-saves.md`, "Battle session".

**Seeds.** The caller derives the encounter's seed (for example `LootRoller.DeriveSeed` from the save
seed and the map node) and keeps it separate from the battle's seed. Everything rests on
`System.Random`'s seeded sequence, which .NET, Mono and Unity share; an EditMode test pins it.

**PENDING PRODUCER REVIEW — deliberately not decided here:**

1. **The campaign's difficulty target — DECIDED (user): tiered by the kind of fight.** Each shape
   carries its own `TargetClear`: trash is meant to be cleared most of the time (`squad` and `horde`
   **80%**), an elite **60%**, a boss about half (`solo` **50%**). The shipped table is calibrated so
   the team a scouting, counter-picking player fields (the simulator's bond-aware pick), wearing
   **typical gear** (`--gear typical`, user decision), clears that
   share of each shape's generated encounters at each calibrated level; `DifficultyScale` stays a
   global producer factor (**1.0** = as calibrated). A player who does not scout clears far less at
   the same multipliers (the tuned report's "Difficulty by shape": about 7-10% of `solo` and
   `elite`, 50-60% of `squad` and `horde`, `elemental`). Being over-levelled must make every fight
   easier, and the simulator checks it (`--level-gap`, bands relative to the target). Changing a
   target means re-running the simulator's `--write-difficulty`.
2. **Which authored encounters exist.** `Templates` holds the ten DRAFT region bosses (placeholders
   pending producer review); the tests also build example templates in memory.
3. **How a map node picks a shape and a level.** `EncounterPlan` takes both from its caller.
4. **Between calibrated levels the multiplier is interpolated linearly** (calibrated at 1, 50 and
   100 only). A tunable default, not a measured curve.

## Region campaign — BUILT; regions, maps and bosses are DRAFT CONTENT PENDING PRODUCER REVIEW

How encounters reach the player (answering "how a map node picks a shape and a level" above): ten
regions cover levels 1-100 (`r01` 1-10 … `r10` 91-100). **The player explores each region as a map**
(the open world is TBD) of locations — wilds, beast dens, camps, trading posts, a guarded pass and the
boss's lair — over **four expeditions**. Behind that map sit seeded, Slay-the-Spire-style node maps,
**an internal pacing model only** (each node carries its location kind, map position and label key
for the map UI) (`NodeMapGenerator`: 11 rows, 4 lanes, Battle / Elite / Shop /
Rest nodes, a Gate on top of stages 1-3 and the region's **Boss** on top of stage 4). A node's level
rises through the region (about 2.25 levels per stage); Battle nodes draw `squad` / `horde` / `solo`
by the region's weights, Elites (+1 level) and generated Gates the `elite` shape, the Boss an
authored template at the region's max level. A **lost battle is retried** at the same node with a
new battle seed (or the player takes another path); Rest ("Camp") trains one beast; Shop ("Trader")
opens a shop service that is a **stub** until the gold economy lands.

Beating a boss grants its **seal**, which raises the **beast level cap** (12 at the start, then 22,
32, … 92, 100): beasts at the cap bank XP (at most three levels' worth) and spend it when the cap
rises. The avatar has no cap. A **level-gap falloff** cuts the XP of anyone fighting below their
level (+1 60%, +2 25%, +3 10%, +4 5%, +5 nothing), and **benched beasts** earn a share of the battle's
XP that grows the further behind they are, so reserves stay about 6 levels behind.

The ten boss templates in `encounter-library.json` (`boss_r01_…` to `boss_r10_…`, shape `elite`)
supersede "none authored" above; they are **DRAFT placeholders** (names, elements, escorts), each
`DifficultyOverride` calibrated to about **50%** scouted clear at its level (the user's boss tier;
squad / horde ~80, elite ~60).

Full rules, save shape and pacing: `docs/design/progression-and-saves.md`, "Region campaign" and
"Beast and avatar level"; numbers: `docs/balance/campaign-pacing-report.md` (`--mode campaign`).

## Data-driven foundation already in place

The authored data this combat model needs is already committed as ScriptableObject schemas under
`src/BeastCraft.Core/Battle/`:

- **`SkillSO`** — target shape (`SingleTarget`, `Line`, `Cross`, `AreaBurst`, `AllEnemies`,
  `AllAllies`, `Self`), range, resource cost, cooldown, a list of effects, and the targeting fields
  added by decision 4 (side, criterion, order, targeting stat), the attacking `Element` (see
  "Element system" below), and the damage `Category`, physical or special (see "Damage formula"
  below).
- **`GearSO`** — slot, stat modifiers, rarity tier, and minimum creature level. Beast gear only.
- **`AvatarGearSO`** / **`AvatarStatsSO`** (under `Runtime/Avatar/`, namespace `BeastCraft.Avatar`)
  — the avatar's own stat gear (slot, stat modifiers, rarity; no visuals) and its authored base
  stats. See decision 6.
- **`PassiveSkillSO`** (also `Runtime/Avatar/`) — an avatar passive: trigger, gating, target scope
  and an ordinary `SkillEffect` list. See "Avatar passives".

These were deliberately authored at an abstract level. Range is an integer count of grid steps and
target shapes are named by their tactical intent rather than by a concrete tile layout, so the data
is **grid-agnostic**: it stayed valid whichever way the grid-shape and turn-model questions were
resolved, and it remains valid under the confirmed answers below. No content authored against these
schemas has to be thrown away by those decisions.

## Progression tie-in (Sword x Staff-inspired, narrative-first)

The Sword x Staff influence is about *depth of build expression*, and it maps onto the existing
creature data rather than requiring new systems:

- **Promotion / evolution tiers** — `CreatureSpeciesSO.EvolutionOptions` already models branching
  evolution as a level gate plus an optional required item. That is structurally the same shape as a
  class-promotion tree: a meaningful, player-chosen fork at a progression threshold, with divergent
  stat and skill outcomes down each branch.
- **Build diversity** — `GearSO` stat modifiers combined with per-species learnable skill lists
  (`CreatureSpeciesSO.LearnableSkills`, each entry gating a skill behind a level) give two
  independent axes of build variation on top of the species choice itself: what the creature *is*,
  what it has *learned*, and what it is *wearing*.
- **Idle / offline progression, reframed** — offline growth is retained, but as
  **server-authoritative background growth** (Unity Cloud Code) that advances between narrative
  sessions. It is a welcome-back bonus paced against the story, not the primary loop. Players are
  not intended to actively grind it, and it should never be the reason to open the game. Keeping it
  server-authoritative also means the reward curve is tunable against story pacing without shipping
  a client build, and it closes the obvious clock-tampering exploit.

## Confirmed design decisions

The questions this document previously left open have been **decided by the producer**. Each is
recorded below with the decision first and the original tradeoff analysis retained underneath as
background — the rationale is still useful when these systems are revisited, but none of it is an
open choice any more.

### 1. Grid shape and arena size — DECIDED

**The grid is hexagonal.** Arena size is **fixed per encounter and chosen from three presets:
Small, Medium, and Large.** There is no single global arena size; each encounter declares which of
the three presets it uses.

*Background.* A **square grid** would have been simpler on every axis: tile art authors as a single
repeated quad, movement and line-of-sight use standard 4- or 8-directional rules, and pathfinding is
textbook. A **hexagonal grid** is more tactically interesting — flanking and positioning read better
because there are no "corner" adjacency edge cases — at the cost of roughly doubling the tile-art
workload and adding real complexity to pathfinding, range rendering, and UI affordances. That
tactical read was judged worth the cost. On sizing, a single fixed arena across all encounters would
have been cheapest to build and balance, while fully per-encounter layouts would cost the most
authoring; the three-preset approach is the middle path, giving set-piece fights room to feel
different without opening up unbounded per-encounter layout work.

### 2. Active party size and the avatar's on-grid presence — DECIDED

**Beasts are placed on the grid** — they are pieces on the board, not commands issued from an
off-grid menu. **Active party size depends on the battle type:** a **Solo** battle deploys **1**
beast, a smaller **Group** battle deploys **up to 4**, and a larger **Group** battle deploys **up to
6**.

*Background.* Party size drives almost everything downstream: encounter pacing, how much screen real
estate the grid needs on a phone, AI cost per turn, and how quickly a turn cycles. A smaller party
keeps each unit's decision meaningful and readable on a small screen; a larger one allows more
composition and role play at the cost of turn length. Supporting all three formats rather than
picking one size means the encounter designer can choose the pacing per fight — a tight duel, a
standard squad fight, or a full set-piece — instead of the whole game being tuned to a single shape.

### 3. Turn order model — DECIDED — AMENDED: ATB speed gauge, square-root fill

**Turn order is an ATB-style speed gauge.** Every combatant fills its own gauge at a rate that grows
with the **square root** of its current `StatType.Speed` and takes a turn each time the gauge
reaches a fixed threshold, so **a unit four times as fast as another acts twice as often**, and
+21% Speed buys +10% turns. Units still act **one at a time**, and everything else about a turn is
unchanged. This amends the original decision, a speed-sorted initiative queue (everyone acts once
per round, fastest first); the amendment is the producer's.

*Second amendment: square-root fill (user-approved).* The first gauge filled at a rate equal to
Speed, so turns were linear in Speed. It now fills at `round(100 × sqrt(Speed))`, adopted from Sword
x Staff, whose turn interval is `100000 / sqrt(SPD × scale)` (see
[`docs/balance/research-sword-x-staff.md`](../balance/research-sword-x-staff.md)). Stacked Speed now
has diminishing returns: a Speed buff or Speed gear buys fewer extra turns the more Speed a unit
already has, so Speed is harder to snowball. **User decision:** the roster's "10–15% speed spread"
target now applies to **turns**, not to the Speed stat: the fastest beast should take about 10–15%
more turns than the slowest, which under the square root lets base Speed spread by about 20–30%.
The current roster (base Speed 92–105, a 14% stat spread) therefore spreads turns by only about 7%;
widening base Speed is left to the next roster retune (see the tuning log).

*Why it changed.* Under the round queue Speed only decided *order* within a round: a Speed-120 beast
got exactly as many turns as a Speed-40 one, it merely took them earlier. The balance simulator
showed what that does to the stat (see "Starter roster" and the tuning log): Speed bought little
beyond reaching the enemy first and taking its focus, so it was cheap, and the first tuning pass
spent the fragile beasts' Speed on bulk. With a gauge Speed is an action economy — more turns — and
has to be priced as one.

The rule, as built in `TurnManager` (all integer arithmetic, so a client and a server-authoritative
re-simulation always agree):

- **Gauge and threshold.** Every unit starts the battle at gauge 0. `TurnManager.ActionThreshold`
  is **100000**. A unit's fill rate is `TurnManager.FillRateForSpeed(Speed)` =
  `round(FillScale × sqrt(max(1, Speed)))` with `FillScale` = **100**, computed **exactly in
  integers** (the integer square root of `Speed × 100²`, rounded half up; no floating point, so every
  platform agrees). Speed 100 fills 1000 per tick, Speed 1 (the floor, so nothing can stall forever)
  100. The rate is read from the live `Stats.Speed` at every step, so a speed buff or debuff changes
  the unit's cadence from the moment it lands. The rate rises strictly with Speed up to Speed 2500,
  so the rounding never merges two speeds in any plausible range.
- **Event-driven time.** The next turn is found without stepping tick by tick: for every living
  unit, ticks needed = `ceil((100000 − gauge) / rate)` (0 when already full); time advances by the
  smallest of those, and every living unit's gauge gains `rate × elapsed`.
- **One unit acts per step.** Of the units now at or above the threshold, the actor is the one with
  the **highest gauge (most overflow)**, then the **higher fill rate** (the higher Speed), then the
  ordinal unit id (the shared `BattleUnitOrder` tie-break). Other full units act on the following
  steps, with no time passing in between.
- **Overflow carries.** After the actor's turn, 100000 is subtracted from its gauge and the
  remainder counts toward its next turn. That is what keeps the long-run rate exact when the
  threshold is not a multiple of the unit's rate (a Speed-225 unit gets exactly 3× the turns of a
  Speed-25 one).
- **Defeated units** never fill and are never handed a turn.
- **Per-turn counters are unchanged.** Cooldowns (decision 5), timed buffs and the movement budget
  (decision 7) were always counted in the unit's *own* turns, so a faster unit simply cycles them
  faster. The avatar is no exception: it fills its own gauge from its own Speed and counts its
  cooldowns (and its passives' internal cooldowns) in its own turns; see decision 6.

*Time.* There are no rounds any more. Battle time is counted in integer ticks
(`TurnManager.ElapsedTicks`) and reported **normalized**: 1.0 = one turn of a Speed-100 unit
(`TurnManager.ReferenceSpeed` = 100, whose rate is `ReferenceFillRate` = 1000, so
`TicksPerTimeUnit` = 100 ticks). Speed scales with level through the growth curve, so the same fight
takes longer in normalized time at low levels (less so than under the linear gauge: a level-1 beast
at Speed 15 now takes 0.39 turns per unit of time, not 0.15); compare times within a level.
`BattleResult` reports `ElapsedTicks` / `Time` (when the last turn was taken) and `ActionCount`
(turns executed). `TurnManager.PredictNextActors(n)` forecasts the next *n* turns assuming no speed
change or defeat, which is what a turn-order UI would show.

*Worked example.* Speeds 100 (a, rate 1000), 225 (b, rate 1500), 49 (c, rate 700) — the speeds
are 1 : 2.25 : 0.49, the turn rates 1 : 1.5 : 0.7. b fills first (t = 67, gauge 100500, carries
500); a at t = 100; b at t = 134 (101000, carries 1000); c at t = 143 (100100); at t = 200 a and b
are both at exactly 100000, so the faster goes first: b, then a; b again at t = 267.
`TurnManagerTests` pins this sequence.

*The time cap* (`BattleTurnExecutor.DefaultMaxTime` = 2000 normalized) is still a scaffold safety
net against a battle that cannot end, not a game rule. It replaced the 200-round cap and is sized so
the net survives low levels: 2000 is about 630 turns of a Speed-10 unit (rate 316; about a level-1
beast), up from 200 under the linear gauge.

*Tunable defaults, not balance.* The threshold and `FillScale` only set the gauge's resolution
(rounding the rate to a whole number moves a roster-band unit's turn rate by at most about 0.05%);
the reference speed is a reporting convention. None of them changes who acts how often: only the
ratios between the square roots of the speeds do.
Starting every gauge at 0 (rather than, say, a random or Speed-scaled head start) is also a default.

*Background, from the original decision.* A single, one-at-a-time order is classic JRPG/tactics
pacing, simple to build and easy to teach: the player always knows whose turn it is and, with the
forecast, what comes next. The alternative, a **simultaneous declare-then-resolve** model where both
sides choose all their units' actions up front and resolve them together, is more strategic (reads,
baits, committed positioning) but costs much more in UI, AI and tutorial work, and it makes failure
states harder for a new player to parse. Phase-based resolution stays open as a *later* evolution
if playtesting says the combat wants more depth; it is not part of the first playable slice.

### 4. Battle flow and skill targeting — DECIDED

**Battles are auto-resolved.** The player places beasts before the fight; during the fight each
beast moves and uses skills on its own, with no per-turn action menu. The full flow is written up
under "Core loop" above.

Two consequences for how skills are authored follow directly, and both are now settled:

- **A skill always aims from the caster's own live position.** There is no player- or AI-picked aim
  point anywhere in the model, so the origin of every target shape is simply the tile the caster is
  standing on when the skill fires. This resolves the question `SkillSO` previously left open as a
  TODO: **`Range` gates the whole footprint**, measured out from the caster, because with the origin
  pinned to the caster there is nothing else for it to be relative to.
- **Each skill authors its own targeting.** A skill declares which side it may land on
  (`SkillTargetSide`: `Enemy` or `Ally`, judged against the *caster's* team so one authored asset
  works for whichever side casts it) and how it picks among the candidates it is eligible to hit.
  That pick is split into two orthogonal fields, the same way `SkillEffect` splits its effect type
  from its affected stat:
  - `SkillTargetingCriterion` — *what* to compare by: `Random`, `Stat`, `Distance`, or `CurrentHp`.
  - `SkillTargetingOrder` — *which extreme* wins: `Lowest` or `Highest`.

  `Stat` compares one specific authored `StatType` (`SkillSO.TargetingStat`) — not current HP, not
  an aggregate of the stat block — so "hit the slowest enemy" is `Enemy` + `Stat` + `Lowest` +
  `Speed`, and "buff the ally with the highest Attack" is `Ally` + `Stat` + `Highest` + `Attack`.
  `Distance` compares hex steps from the caster, so `Lowest` is nearest and `Highest` is farthest.
  `CurrentHp` compares the HP each candidate has left *right now* (`BattleUnit.CurrentHp`, damage
  taken included), so "pick off the weakest" is `Enemy` + `CurrentHp` + `Lowest` and "heal the most
  wounded ally" would be `Ally` + `CurrentHp` + `Lowest`; `Highest` goes for the healthiest. It was
  added because `Stat` + `HP` compares the stat block's *maximum* HP, which never moves as a unit takes
  damage: a "lowest HP" skill kept hitting whichever beast was built with the least HP, however hurt
  the others were (the balance simulator showed a 105-max-HP beast almost never focused behind four
  at 100). `CurrentHp` ignores the targeting stat, is re-read at every pick (so the choice tracks
  damage as the battle goes), and is appended to the enum as value 3 (the existing values keep their
  numbers, since they are serialized into assets).
  `Random` compares nothing and ignores both the order and the targeting stat.
  Every comparing criterion breaks ties the same way, on the ordinal unit id
  (`BattleUnitOrder.CompareById`), and runs identically in both the range-limited pick that resolves a
  cast and the board-wide `PickFocusIgnoringRange` that decides where a mover walks.

  Not every shape consults every field: `Self`, `AllEnemies` and `AllAllies` ignore most or all of
  them, and the sweeping shapes (`Line`'s beam, `Cross`, `AreaBurst`) hit everything eligible in
  their footprint rather than picking one unit. `SkillTargetResolver`'s own documentation is the
  authority on which field each shape actually reads.

  *Amended by "Unit footprints":* against or from a large unit, range and the `Distance` criterion
  are measured between nearest tiles, an area hits a large unit once when any of its tiles is in it,
  and a large caster's areas grow from all of its tiles.

*Background.* The alternative — manual per-turn control with an action menu — is the conventional
tactics-game shape, and it is what earlier drafts of this document assumed. It gives the player more
moment-to-moment agency, but it costs a full action UI, a target picker, and a tutorial for both, on
every encounter in a game whose centre of gravity is its story. Auto-resolution moves the player's
decisions to team building and deployment, which are the decisions the creature-collection and gear
systems already exist to serve, and keeps encounters short enough to sit inside narrative pacing.
The cost is that skill authoring now carries the tactical intent that a player would otherwise
supply live — which is exactly why targeting is authored per skill rather than being one global AI
rule.

### 5. Equipped-skill rotation and cooldowns — DECIDED

**A caster equips several skills in a fixed, authored order — a "stack" — and does not choose
between them at runtime.** Each equipped skill carries its own cooldown counter, and the counters
alone decide what fires. This closes the question decision 4 deliberately left open ("which of a
beast's equipped skills fires this turn"), and it closes it *without* a priority rule, a per-skill
condition, or a target-scoring pass: the authored order and the authored cooldowns are the whole
answer.

The rule, in full:

- **Starting state.** At battle start every equipped skill's counter is set to that skill's own
  `SkillSO.Cooldown`. Nothing fires before it has counted down at least once, so there is no
  turn-one alpha strike.
- **Tick.** Every time it becomes the caster's own turn in the turn order (decision 3), *all*
  of its equipped skills' counters tick down by 1, clamped at 0 — never negative.
- **Fire and reset.** Any skill whose counter is exactly 0 after that tick fires this turn and
  immediately resets to its authored `Cooldown` to start counting down again. **Amended by decision
  7:** reaching 0 is an *offer*, not a guarantee. A skill that needs a target in range and cannot
  reach one does not fire and does not reset — it keeps its 0 and is offered again next turn. Only
  actually firing re-arms. Every skill that does fire still resets in the same step.
- **Multi-fire.** More than one equipped skill may fire on the same turn when more than one counter
  reaches 0 together. They are fired in **authored stack order** (list order), which is what makes a
  multi-skill turn deterministic and reproducible.
- **Cooldown 0 and 1 both mean "every turn."** A `Cooldown` of 0 starts already at zero; a
  `Cooldown` of 1 reaches zero on the first tick. Both then fire on every subsequent tick. This is
  intended behaviour and is not special-cased — it is how an always-available skill (the basic-attack
  role) is authored.
- **A defeated caster does not tick.** A unit that is out of the fight takes no turn, so its rotation
  does not advance and it produces no activations.

*Background.* The alternatives all put a decision back into the fight that this model has
deliberately moved out of it. A priority list ("fire the first usable skill") collapses the build to
its top entry most turns; per-skill firing conditions are a scripting language in disguise and a
tuning surface the player can't see; a scoring pass over candidate targets is an AI, with all the
authoring, tuning and legibility cost that implies. A cooldown rotation instead makes the *loadout
itself* the expressive choice: a stack of 2-turn and 5-turn skills has a readable rhythm, the
player can reason about it while building, and it needs no runtime decision-making at all. The cost
is that a beast can fire a skill at a moment when a human player wouldn't have — which is the same
trade auto-resolution already made everywhere else.

### 6. The avatar's skill loadout — DECIDED (timing and targeting); stats AMENDED; passives ADDED; timing AMENDED (own gauge)

**The avatar has skills too, on the same rotation mechanic**, intended to support and buff the
player's own beasts rather than to attack. **Amended (user):** those active skills stay, but the
avatar's *main* role is now its **3 passive slots** — passives that fire on battle events rather
than on a rotation — and both its actives and its passives are acquired and leveled slowly through
play on the same progression model as beast skills. See "Avatar passives" below for the passive
rules; everything in this section about the active loadout still holds. Per decision 2 the avatar is **not a piece on the grid**
and has no meaningful `HexCoordinate` position; it does, however, have **a turn of its own in the
turn order** (see the timing amendment below).

**Confirmed timing — amended (stage 3b): the avatar has its own gauge.** The avatar fills an ATB
gauge from **its own Speed**, exactly as a beast does (decision 3: square-root fill, the same
tie-breaks), and its loadout **ticks once per avatar turn**. On its turn
(`BattleTurnExecutor.ExecuteAvatarTurn`) its passives' internal cooldowns tick, then its actives
tick and fire; it has no movement and no status step. Its cadence therefore no longer depends on how
many beasts the player fields or how fast they are: a Speed-100 avatar acts once per unit of battle
time whether it supports one beast or six. The default Speed is 100 (`AvatarStatsSO.DefaultSpeed`,
the reference Speed and the middle of the roster's 88-110 band), scaled with the avatar's level like
its other stats.

*Superseded:* the avatar's loadout used to tick once every time one of the player's own beasts took
its turn (not on enemy turns). Under the ATB gauge that made a fast or large team cycle its avatar
faster, which is why it was listed as an open item until the gauge was chosen.

**Confirmed targeting:** **avatar skills are restricted to the position-free target shapes —
`Self`, `AllAllies`, `AllEnemies`.** This closes the question this section previously left open.
The problem was that every position-dependent shape (`SingleTarget`, `Line`, `Cross`, `AreaBurst`)
anchors its footprint on `caster.Position`, and a position-less avatar had nothing to anchor on; the
answer is that an avatar skill is simply never authored with one of those shapes, which suits the
supporting role the avatar was given anyway. The restriction is a **content-authoring convention,
not a runtime check** — this codebase trusts internally-authored data rather than defensively
validating it, so authoring a `Line` on an avatar skill is a content bug to be caught in content
review, not an exception at runtime.

Because of that restriction, **the avatar is represented as an ordinary `BattleUnit` with a
placeholder position** (`HexCoordinate.Zero`), built by the `BattleAvatar.Create` factory. Earlier
drafts of this section refused to park the avatar on a fake origin tile, and that refusal was
correct *at the time*: with position-dependent shapes still on the table, a fake tile would have
silently produced real, wrong footprints measured from the middle of the board. The shape
restriction removes that failure mode entirely — the placeholder is not a value that happens to be
unused, it is a value nothing the avatar casts can reach. Reusing `BattleUnit` rather than inventing
a parallel avatar type means the rotation, the resolver and the effect applier all take the avatar
unchanged.

Two consequences of that representation, both deliberate:

- **The avatar is a caster, not a member of the roster.** It is passed as the `caster` (or
  `avatar`) argument and is *not* added to the `allUnits` roster; it *is* added to `TurnManager`
  (its gauge). Keeping it out of the roster is what makes it a non-combatant in practice: the win
  check never counts it, an enemy `AllEnemies` sweep cannot reach it, and its own `AllAllies` buff
  lands on the player's beasts. A `Self` skill still works, since the resolver returns the caster
  directly without consulting the roster.
- **The avatar cannot be defeated.** `IsDefeated` stays `false` for the life of the battle; there
  is no rule in the design by which a commander could be defeated, and nothing can write the flag on
  a unit it cannot target. (This bullet originally also said the avatar had no stats — superseded,
  see below.)

**The timing half is wired up.** `RunBattle` hands the avatar's turns to
`BattleTurnExecutor.ExecuteAvatarTurn` (and `ExecuteTurn`, handed the avatar, does the same), and a
beast's turn no longer ticks the avatar. The rotation engine that landed for decision 5 is
deliberately owner-agnostic, so it drives the avatar unchanged. Without an avatar nothing changes.

**Amendment — the avatar has stats and stat gear (supersedes "the avatar has no stats").** This
section previously decided that the avatar's `StatBlock` is all zeros because avatar items are
cosmetic. The producer has reversed that, and the rule is now two separate things:

- **Avatar cosmetics stay purely cosmetic.** The customization system (`AvatarCustomizationSchema`)
  is unchanged: it decides what the avatar looks like and grants no stats.
- **The avatar has real stats, raised by non-cosmetic avatar gear that is never rendered.** Its
  base is authored on an `AvatarStatsSO` (a `StatBlock` of max-level values with a growth curve,
  scaled to the avatar's own level; see "What is not settled yet", avatar level). Gear is
  `AvatarGearSO` — a stable `AvatarGearId`, display name, description, inventory icon, an
  `AvatarGearSlot` (`Weapon`, `Armor`, `Trinket`), a `StatModifier` list and a rarity tier, and no
  visual fields at all. It is a separate type from the beasts' `GearSO`, with its own slot enum, so
  beast gear and avatar gear cannot be cross-equipped. `BattleAvatar.Create(skills, baseStats,
  equipped)` assembles the avatar's stats through `StatCalculator` exactly like beast gear (flat,
  then summed percent, rounded, every stat at least 0 and `HP` at least 1); null gear and null
  modifiers are skipped, and one-item-per-slot is left to the equipment screen, as for beasts. The
  original `BattleAvatar.Create(skills)` still builds a zero-stat avatar for callers with no stats
  authored, except for Speed 100 so that its gauge fills at the reference rate.

**Amendment — the avatar's skills progress, and it has passives.** The avatar's skills now live
in an `AvatarSkillBook` (save data): `Actives` (its active support skills, **3 slots**,
`AvatarSkillBook.ActiveSlotCount` — before this the avatar's loadout had no fixed size, and 3
matches a beast's) and `Passives` (**3 slots**, `PassiveSlotCount`). Both are acquired and leveled
exactly like beast skills (practice XP, materials, breakthroughs). `BattleAvatar.Create(book,
activeLookup, passiveLookup, baseStats, gear, level, out passives)` builds the avatar with its
equipped actives as its `SkillLoadout` (at their levels) and hands back its equipped passives as the
battle's `PassiveLoadout`. The passives are the subject of "Avatar passives".

Everything else above is unchanged: the avatar is still off the grid, takes its own turns from its
own gauge (the timing amendment), is still a caster outside the roster, and still cannot be
defeated. **The avatar's stats now feed the damage formula** exactly as a beast's do (see "Damage
formula"): a damaging avatar skill uses the avatar's `Attack` or `SpecialAttack`. Heals
scale with the caster's `SpecialAttack`, the avatar's included; buffs are still flat for everyone,
so an avatar buff still lands the same whatever the avatar's stats are. The avatar's level is its own
(`AvatarProgress.Level`, see "What is not settled yet"); the statful `BattleAvatar.Create` takes it
(default 1) and records it on the unit, where the damage formula's level-difference term reads it. The zero-stat `Create(skills)` avatar has no attacking stat, so every damage
effect it lands deals the formula's `MinimumDamage` floor of 1 — see "Damage formula".

### 7. Movement during a turn — DECIDED

**Movement is spent per skill, as needed, out of one budget shared by the whole turn.** This closes
the question decisions 4 and 5 left open — what a beast does with its move range — and it closes it
without an AI: the skills themselves decide where the beast goes.

The rule, in full, in the producer's own terms:

- **Movement serves the skill, not the other way round.** There is no up-front "walk toward the
  nearest enemy" step. Each skill that comes off cooldown is attempted in stack order, and the beast
  moves only if *that* skill needs it to reach a target. A beast whose targets are all already in
  range does not move at all.
- **The budget is per turn, not per skill.** A beast starts its turn with its full move range and
  every approach spends out of the same pool. Using three quarters of it getting the first skill into
  range leaves one quarter for everything after it.
- **A skill that cannot reach keeps its cooldown at 0.** If a ready skill has an eligible target but
  cannot get within range of it even after spending everything left in the budget — or has no
  eligible target anywhere at all — it does not fire, and **it does not reset**. Its counter stays at
  0 and it is offered again, with a fresh full budget, on that beast's next turn. This is the one
  place the decision-5 rule "firing and re-arming are a single step" no longer holds: coming off
  cooldown is now an *offer*, and only actually firing re-arms.
- **Only the two picking shapes are affected.** `SingleTarget` and `Line` choose a single focus
  target (decision 4) and so are the only shapes with something concrete to walk toward, and the only
  ones that can end a turn stuck at 0. `Self`, `AllAllies`, `AllEnemies`, `Cross` and `AreaBurst` all
  resolve from wherever the caster already stands, so they fire the moment they are ready and never
  consult the movement budget. A `Cross` or `AreaBurst` that catches nobody is a whiff that still
  fires and still re-arms, exactly as before — it was never gated on reaching anyone.

*Background.* The alternative shapes this could have taken are all worse fits for an auto-battler
whose decisions live in the build. Moving first and then seeing what happens to be in range makes
the move range and the skill ranges interact by accident rather than by design. Moving toward the
nearest enemy regardless of what is equipped punishes a long-range build for no reason a player could
read. And a scoring pass over "best tile to stand on" is an AI, with the authoring, tuning and
legibility costs decision 5 already rejected. Tying movement to the skill that needs it means a
beast's positioning is a direct, readable consequence of its loadout — a short-range bruiser closes,
a long-range caster stays put — with no extra tuning surface. The cost is that a beast can walk into
a bad spot to land one skill; that is the same trade auto-resolution has made everywhere else.
(Decision 8 later adds a deliberately narrow positioning layer on top — combat stances — that
changes tie-breaks, melee approaches and leftover movement, but not this rule.)

*Scaffold details, not confirmed balance.* Three implementation choices sit underneath this and are
cheap to revisit (the first two are lead engineering decisions made once the balance simulator
showed what their absence did to large PvE fights):

- **Partial approach.** A beast that cannot afford the whole approach **advances as far as its
  remaining budget allows** along the same cheapest route it would have taken, spending all of it,
  and the skill is held exactly as above: it does not fire, its cooldown stays at 0, and it is
  offered again next turn from the closer tile (`BattleSkillStatus.OutOfMovement`, with the steps
  walked in the outcome's `MovementSpent`). Only a beast with **no route at all** — walled off by
  terrain or bodies — stays where it is (`Unreachable`). The earlier rule stood still instead, on
  the grounds that a partial walk "spends the budget to accomplish nothing"; in practice standing
  still meant two sides whose move plus range fell short of the gap between them never engaged. With
  several ready slots the shared budget still goes in stack order: the earliest skill that cannot
  reach spends what is left walking, and later skills are attempted from the new tile with nothing
  left to walk with — one whose target is now in range fires, and shapes that need no approach fire
  regardless. Deterministic: the route and its tie-breaks are the full approach's.
- **Defeated units leave the grid** the moment they fall: after every skill that fires and every
  avatar activation that is applied, the executor lifts each defeated unit off the board, so a later
  skill in the same turn, and every later turn, can walk through or stand on its tile. (The effect
  applier has no board, so this sits in the executor.) The fallen unit keeps its `Position` — where it
  fell — for logs and results; nothing reads it for play, since targeting and the turn order already
  ignore the defeated.
- **The route** is the cheapest one that reaches *any* tile within range of the target, found by
  pathing at the tiles around the target (the target's own tile is occupied, so nothing can path onto
  it) and stopping at the first tile on that route that is in range. Around a large target the goals
  ring its whole footprint, and a large mover plans by anchor instead (its footprint must fit at
  every step): see "Unit footprints".

### 8. Combat stances — DECIDED (the three stances and their roles); the heuristics are SCAFFOLD

**Every species has a combat stance — Vanguard, Ranged or Skirmisher — that decides how it
positions itself.** It is authored per species (`CreatureSpeciesSO.Stance`, the roster JSON's
`Stance`, default Vanguard), copied onto the unit by `BattleUnitFactory` (`BattleUnit.Stance`), and
read by `BattleTurnExecutor`. `CombatStance` has explicit values (Vanguard 0, Ranged 1, Skirmisher 2)
that must never be renumbered, since Unity serializes the field by value. This closes the
"screen an ally" and "kite" parts of the old tactical-AI open item without an AI: a stance is one
readable label on the species, and everything it does is a deterministic, integer tie-break or
budget rule on top of decision 7, reusing the pathfinder.

What each stance does:

| Stance | Approach (SingleTarget / Line) | Melee (`Range <= 1`, enemy side) | Leftover budget | Crowds |
| --- | --- | --- | --- | --- |
| Vanguard (default) | Decision 7 exactly; among equally short routes, stop nearest a fragile ally | Walks in | Discarded | Ignored |
| Ranged | Fewest steps, then farthest from the target within range | Never walks in: fires only if a target is already adjacent, otherwise holds (`HeldByStance`) | Retreat | Avoided |
| Skirmisher | As Vanguard (melee included), with the crowd preference | Walks in | Retreat | Avoided |

The rules in full:

- **Decision 7 still holds for everyone.** One budget per turn, shared by the slots in stack order;
  a skill that cannot reach keeps its 0; partial approach. A stance never makes a unit walk further
  than the cheapest route to range — every stance preference below is a tie-break among routes of
  the same length, except the melee rule and the retreat. `Self`, `AllAllies`, `AllEnemies`,
  `Cross` and `AreaBurst` are unchanged: they fire from where the unit stands. A null grid still
  means nothing moves.
- **Anti-surround (Ranged and Skirmisher).** Among the in-range tiles the unit can reach in the
  fewest steps (every such tile, not only those on the plain rule's seven goal routes), it prefers
  the one farthest from the target, then the one with the fewest living enemies adjacent, then the
  plain rule's own pick, then a fixed board order. A Vanguard does not avoid crowds. Distances and
  adjacency respect footprints ("Unit footprints"): a large enemy counts once however many of its
  tiles touch the stop tile, and the farthest-in-range test measures to its nearest tile.
- **Ranged keeps its distance.** An already-in-range skill fires from where the unit stands, as
  before. The "farthest in-range tile" preference is, in practice, a guarantee rather than a change:
  a unit only approaches from out of range, one step changes a distance by at most one, so the
  cheapest in-range tiles are at exactly the skill's `Range`. The real difference is melee: an
  enemy-side `SingleTarget` or `Line` skill with `Range <= 1` is never walked for. It fires if an
  enemy is already adjacent; otherwise the slot holds without moving (`BattleSkillStatus.HeldByStance`,
  cooldown kept at 0, nothing spent) and later slots get the whole remaining budget. Ally-side
  melee skills are exempt — stepping next to a friend is not walking into melee.
- **Retreat with leftover budget (Ranged and Skirmisher).** Once every ready slot has been
  attempted, whatever budget is left is spent moving to the tile, reachable within it, that is
  farthest from the nearest living enemy — counting only tiles where that distance stays at most the
  unit's longest enemy-side `SingleTarget` / `Line` range (whatever its cooldown), so the unit can
  fire again next turn without moving. Ties: fewest adjacent enemies, then fewest steps (so a unit
  already on a best tile stays), then breadth-first order. The unit's own tile always competes, so
  a retreat never ends nearer the enemy than it started; a unit that is already beyond its reach
  stays put (approaching is the skills' job). No retreat with no enemy left, no picking skill, a
  null grid, or if the unit defeated itself. It counts in
  the turn's `MovementSpent`, and is reported as `BattleTurnResult.RetreatSteps`.
- **Vanguard screens.** Among routes that reach range in the same number of steps, a Vanguard picks
  the one whose end tile this turn (the in-range stop, or where a partial approach runs out) is
  nearest its closest living Ranged or Skirmisher ally; then the plain rule's goal order. With no
  such ally the result is exactly decision 7's, which is why every pre-stance movement test passes
  unchanged. This is the simplest deterministic reading of "stand between the enemy and the back
  line" on boards where both sides close head-on; it does not model a line or a threat zone.

**Roster stances.** Ranged: Phoenix, Kirin, Basilisk. Skirmisher: Thunderbird, Griffin. Vanguard:
Leviathan, Golem, Treant, Tarasque, Frost Wyrm (see "Starter roster").

*Scaffold details, not confirmed balance.* The three stances and who is which are decided; the
heuristics under them (the tie-break orders, the screening distance, the retreat cap at the longest
single-target range) are engineering defaults chosen to be
deterministic and legible, and are cheap to revisit. The balance simulator's first look at them is
in the tuning log ("After combat stances"): with the one-size standard kit, Ranged beasts lose
Strike's damage and the physical/special parity the kit was tuned to, so the numbers say more about
that kit than about the stances.

## Unit footprints — DECIDED (user: bosses cover several hexes); the geometry is SCAFFOLD

**Large enemies cover more than one hex.** The user's direction: giants are bosses and occupy more
than one hex; champions, the elite tier below them, are sized accordingly. Beasts stay one hex.
`UnitFootprint` (`Battle/Grid`) names three sizes, each a fixed set of offsets from the unit's
**anchor**, which is `BattleUnit.Position` and the tile the grid records it on:

| Footprint | Tiles | Offsets from the anchor | Used by |
| --- | ---: | --- | --- |
| `Single` (default) | 1 | the anchor | every beast, the avatar, every ordinary enemy |
| `Triangle` | 3 | anchor, `(1, 0)`, `(1, -1)` — the two-tile edge (the anchor's row) faces the player side | champion (mini-boss) |
| `Hex7` | 7 | anchor (the centre) and its six neighbours | giant, colossus (bosses) |

`Footprints.Offsets` lists them anchor first, then in `HexCoordinate.AxialDirections` order, so every
walk over a footprint is deterministic. There is no rotation: a large unit translates, it never
turns. `CreatureSpeciesSO.Footprint` (default `Single`) is copied onto the unit by
`BattleUnitFactory` (`BattleUnit.Footprint`, fixed at construction). The roster JSON has no footprint
field, and `BeastRosterValidator` refuses any value but `Single` — **beasts are always one tile**. The
only large units today are enemies: the enemy library's giant (`Hex7`) and champion (`Triangle`)
(`enemy-library.json`, optional `Footprint`) and the simulator's legacy fixed-set colossus.

**Board.** `HexGrid` records a large unit's anchor and footprint and names it on every tile it
covers (`GetOccupant`). `TryPlaceUnit(id, anchor, footprint)` is **all or nothing**: every tile must
be on the board, free of terrain and empty or already the unit's own, or nothing changes; on success
the old tiles are all released and the new ones all taken. The two-argument `TryPlaceUnit` keeps a
large unit's recorded footprint (so a move is an anchor move), `RemoveUnit` — and so lifting the
defeated — frees every tile, and `CanStand(anchor, footprint, mover)` asks whether a footprint fits
somewhere. A board of one-tile units runs exactly the code it always did.

**Distance.** Every range and adjacency rule is measured between the **nearest tiles** of the two
units (`FootprintMath`): to a `Hex7` it is `max(0, d - 1)` from the centre, to a `Triangle` the
minimum over its three tiles, between two large units the minimum over one's tiles. So a range-1
skill reaches a giant from any of the **twelve** tiles around it (nine around a champion), and a
giant's own range counts from its outer ring. For one-tile units every one of these is exactly
`HexCoordinate.Distance`.

**Targeting** (amends decision 4). Range and the `Distance` criterion use the footprint distance. An
area shape hits a unit when **any** of its tiles is in the area, and hits it **once** however many of
its tiles are covered. A large *caster's* areas grow from all of its tiles: an `AreaBurst` is the
union of the discs around each of them (for a `Hex7`, the disc of `Range + 1` around its centre), a
`Cross` the union of the arms from each, and a `Line` fires from its tile nearest the focus toward the
focus's tile nearest that one (ties in footprint order). The caster's own tiles are excluded from a
`Cross` or `Line` exactly as a one-tile caster's tile is. Taunt, the criteria and the id tie-break are
unchanged.

**Movement** (amends decisions 7 and 8).

- *A one-tile unit closing on a large one* aims its routes at the tiles around the whole footprint:
  the candidate's anchor (only if nothing stands there), then every tile next to any footprint tile
  and not one of them, walked footprint tile by footprint tile, each in axial order, first sighting
  kept (twelve goals around a `Hex7`, nine around a `Triangle`). A standoff (Ranged / Skirmisher) unit
  measures "farthest within range" to the footprint's nearest tile.
- *A large unit moves by anchor*, and its whole footprint must fit at every step
  (`HexPathfinder.FindPath(grid, start, goal, mover, footprint)`, the same A* with `CanStand` in place
  of `IsPassable`; its own tiles count as free). With `Single` that overload *is* the one-tile search.
  To approach it does not aim at goal tiles: it walks every reachable anchor breadth-first and takes
  the cheapest from which the candidate is in range; among equally cheap anchors a Vanguard screens
  (nearest a fragile ally, judged at that anchor), a Ranged or Skirmisher unit takes the farthest from
  the candidate and then the least crowded, then breadth-first order. The route is the pathfinder's to
  that anchor — the same length — so a partial approach walks a prefix of it as usual. Its retreat and
  crowd checks use the footprint distance too.
- *Anti-surround counts units, not tiles.* "Enemies adjacent" is the number of living enemies next to
  the tile — for a large unit, next to any of its tiles — each counted once, so a giant touching a
  stop tile along two of its tiles is still one enemy.

**Knockback.** A `Hex7` is immovable. A `Triangle` moves at most one tile, and only if its whole
footprint fits at the new anchor. When either unit is large, the push runs from the caster's tile
nearest the target toward the target's tile nearest the caster, so a beast is shoved straight off the
giant's face. One-tile against one-tile is unchanged.

**Deployment.** `DeploymentPacker.TryPack` seats a side's units front-most first (nearest the centre
line, then outward from the vertical centre line, then by `Q`): each takes the first anchor whose
whole footprint lies in the zone on free tiles. For one-tile units that is exactly the front-most
tiles. On a Medium board a `Hex7` boss sits centred on the middle row of the three-row zone; on a
Small board (a two-row zone) it fits nowhere. The balance simulator packs its enemies this way, and
its loader refuses any shape (any lineup it can draw, placed in the generator's front-to-back order)
or fixed encounter that does not fit. `PlacementValidator` is unchanged — beasts are one tile — and a
caller passing already-placed tiles passes every tile a large unit covers.

*Balance.* A `Hex7` boss is easier to reach (twelve tiles around it, every range to it one longer
from its centre), so melee and short-range beasts gain against it; it cannot be knocked back, so
knockback kits lose value against it. Its own reach would also have grown by one, so the fixtures'
giant and colossus author their ranges one lower than their one-tile values were (gaze 3 → 2, quake
and roar area radius 2 → 1: a radius-1 burst from a `Hex7` is the radius-2 disc around its centre,
exactly the old one); the champion's shockwave stays at 2. See the tuning log, "Large enemies
(footprints)".

*Scaffold details, not confirmed balance.* The shapes, the anchor convention, the triangle's
orientation, the nearest-tile distance, the large-mover approach and the knockback caps are
engineering defaults chosen to be deterministic and legible; none has been through encounter design.

## Effect application — SCAFFOLD ASSUMPTIONS, NOT CONFIRMED BALANCE

`SkillEffect`s are now actually applied: `SkillEffectApplier` takes a `SkillActivation` (the fired
skill plus the units it landed on) and resolves it into real state changes. Unlike decisions 1–6
above, **none of the rules in this section were confirmed by the producer.** They are engineering
defaults chosen so the system is complete rather than half-built, and they are expected to be
revisited when balance work starts.

- **Damage and healing are stat-based.** A `Damage` effect's `SkillEffect.Magnitude` is its
  *power*, and the HP it takes comes from `DamageFormula` — caster attacking stat against target
  defending stat, the element multiplier, a crit roll and a variance roll, then the execute bonus and
  the caster-vs-target level-difference multiplier (see
  "Damage formula" below). This superseded the original rule, which applied every magnitude flat and
  deferred the formula to a balancing pass. A `Heal` restores **`Magnitude / 100 × caster's
  SpecialAttack × HealScale`** HP (`SkillEffectApplier.HealScale`, a tunable constant, 1.0),
  rounded to the nearest whole HP and scaled by skill level like every magnitude: no defense term,
  no crit and no variance roll (so a heal takes no rng draws). The avatar is the caster of its
  passives, so a passive heal reads the avatar's `SpecialAttack`. (Heals were flat HP until the
  authored-kits retune: huge at level 1, negligible at level 100. `SpecialAttack` grows on the same
  curve as HP, so a heal now restores about the same share of HP at every level — see
  "Beast skill kits".) Buffs and debuffs still move a stat by exactly their magnitude.
  `BattleUnit` gained a `CurrentHp` alongside its `StatBlock` (whose
  `Hp` is now explicitly the *maximum*); every unit starts a battle at full health, since there is
  no persistent creature-instance model to carry damage in from a previous fight. Both damage and
  healing hold `0 <= CurrentHp <= Stats.Hp`.

- **Defeat is set explicitly by the code that spends the HP.** `BattleUnit` stays a passive data
  record: `CurrentHp` reaching 0 does not quietly flip `IsDefeated` from inside a setter. The
  applier writes the flag as a visible step at the one place it can happen.

- **A target defeated mid-skill is skipped for the rest of that skill's effect list — so healing
  cannot revive.** A skill authored as damage-then-heal that kills its target does not then heal it
  back up; the heal, and any buff after it, simply do not land on that target. Other targets in the
  same activation are unaffected. This extends the rule `SkillTargetResolver` already applies at
  selection time (only living units are eligible) to a unit that dies half a step later. The
  deciding argument is that **there is no revival mechanic in the design**: reviving is a real
  combat rule with real balance weight, and it should not fall out by accident from whatever order
  effects happen to be authored in. If revival is wanted later it should be designed as its own
  thing, not inherited from this.

- **`BuffStat` / `DebuffStat` carry the sign in the effect type, not in the magnitude.** Both are
  authored as *positive* numbers — a "-5 Attack" debuff is `DebuffStat` with a `Magnitude` of 5 —
  which is what splitting buff and debuff into two enum arms already implies. (The gear path
  differs: `StatModifier.FlatBonus` is a genuinely signed int with no accompanying type to carry
  the sign, so a cursed item authors a negative bonus directly. The two encode the same thing in
  different places and are not in conflict.) Stats are held at or above 0, so a debuff larger than
  the stat it drains takes it to 0 rather than negative.

- **Timed buffs/debuffs revert on the affected unit's own turns.** Per `SkillEffect.DurationTurns`,
  `0` means an instant, permanent change with nothing to track. Greater than 0 records an
  `ActiveStatModifier` on the affected unit — the stat, the signed delta *actually* applied, and
  the turns remaining — and `SkillEffectApplier.TickModifiers(unit)` counts it down and subtracts
  the delta back out when it runs out. Storing the applied delta rather than the authored magnitude
  is what stops a clamped debuff handing out free stats when it expires. Each modifier runs its own
  clock, so two buffs on the same stat with different durations expire independently. The countdown
  is deliberately measured in **the affected unit's** turns, not the caster's: a fast unit debuffing
  a slow one means the two clocks genuinely differ.

  `BattleTurnExecutor` calls that tick hook as the first step of each unit's own turn, before the
  turn reads any stat — the movement budget included — so a modifier on its last turn has already
  expired by the time that turn acts.

- **`ApplyStatus` applies a status.** It used to be a documented no-op, for want of a status
  design. `SkillEffect.Status` now names which `StatusType` it applies, and the rules are in
  "Status effects and advanced skill effects" below. An `ApplyStatus` effect left at
  `StatusType.None` still does nothing.

- **Re-applying the same timed buff or debuff refreshes it instead of stacking.** Each timed
  modifier remembers the authored effect it came from. `SkillEffect.MaxStacks` (default 1) caps how
  many copies of *that effect* a unit carries. At the cap, the copy with the fewest turns left is
  reverted and replaced. Before this, re-applying the same effect stacked without limit. No content
  relied on that. Two *different* effects on the same stat still stack independently, as before.

Resource cost is still not spent, per the open question above; effect application does not gate on
it.

## Status effects and advanced skill effects — TUNABLE STARTING DEFAULTS, NOT CONFIRMED BALANCE

This is the effect engine the per-beast skill kits need. It follows the user's reference, Sword x
Staff: chance-based taunt, multi-hit with per-hit rolls, stacking debuffs with a stack cap,
Defense-based shields, knockback, heal targeting by HP fraction, execute scaling and skills that
fire on turn one. **Every new field is inert at its default.** A skill authored before this pass
behaves, rolls and replays exactly as it did. The simulator's report is byte-identical. No content
uses any of this yet: authoring the per-beast kits is the next deliverable.

**New data** (explicit enum values, never renumbered):

- `SkillEffect.Status` (`StatusType`: `None = 0`, `Taunt = 1`, `Stun = 2`, `Shield = 3`,
  `DamageOverTime = 4`, `Knockback = 5`), `Chance` (percent, default 100), `MaxStacks` (default 1),
  `IsPercent` (default false), `HitCount` (default 1) and `ExecuteBonusPercent` (default 0). Unity
  zero-fills a list entry added in the inspector, so a `Chance`, `MaxStacks` or `HitCount` of 0 or
  below reads as its default. A freshly authored effect lands once, always. It never silently lands
  zero times.
- `SkillTargetingCriterion.HpFraction = 4`.
- `SkillEffectType.Cleanse = 5` (added with the behaviour bonds): removes every `Stun` and
  `DamageOverTime` on the target (`StatusEffects.Cleanse`); non-damage, so it passes the chance
  check like any other; `Magnitude` is not read. See "Team bonds".
- `SkillSO.InitialCooldown` (default −1, meaning the ordinary cooldown) and
  `SkillSO.MaxUsesPerBattle` (default 0, meaning unlimited).
- `BattleUnit.StatusResist`: percent, 0–100, default 0, fixed at construction. There is a
  `BattleUnitFactory.CreateBeast` overload that takes it. `BattleUnit.Statuses` is a read-only view
  of the statuses on the unit.
- `BattleTurnResult.Stunned` and `StatusDamage`, and `DamageHit.Absorbed`.
- `StatusEffects`, the status engine. `SkillEffectApplier.GetEffectiveChance` and `RollChance`.
  `DamageFormula.GetExecuteMultiplier` and a `Roll` / `Compute` overload with a bonus multiplier.

**Chance and resistance.** Every non-damage effect (heal, buff, debuff, status, knockback) rolls
its `Chance` separately for each target. Damage always lands. A hostile application, meaning one
onto a unit of the other team, is reduced by the target's resistance:
`effective = Chance × (100 − StatusResist) / 100`, in integers and truncated. So an 85% taunt on a
50%-resistant boss has a 42% chance. Effects on the caster's own side are never resisted. The roll
is `rng.Next(100) < effective`. It draws once, **and only when the effective chance is below 100**.
An effect that always lands draws nothing, which keeps existing content's draw sequence unchanged.
A null rng (the deterministic fallback) lands only certain effects. There is no luck either way,
just as the fallback never crits.

**The rng draw order** stays target-major and in authored effect order. Within that:

- A damage effect draws crit, then variance, for each hit in turn. It draws nothing for hits that
  never happen because the target fell.
- A non-damage effect draws its single chance roll, if it needs one, before it applies.
- Nothing else draws: not the statuses themselves, not a damage-over-time snapshot, not a
  knockback, and not a taunt-forced pick.

For example, `[Damage, 50% debuff]` on one target draws crit, variance, then chance.

**Statuses** live on the affected unit. Their durations count **the affected unit's own turns**, as
timed modifiers do, and mean "in force for this many of its turns":

- `StatusEffects.BeginTurn` counts the current turn off every status present as the turn opens.
- `StatusEffects.EndTurn` removes the statuses with nothing left when the turn closes.
- So a status applied during the unit's own turn (a self-shield) does not lose that turn.
- A `DurationTurns` below 1 reads as 1 for a stored status.

The turn order is:

1. Lift the defeated.
2. Tick the timed modifiers.
3. `BeginTurn`: damage-over-time lands, and the stun is read.
4. Skills, movement and retreat, unless stunned.
5. `EndTurn`.

The avatar's own turn has none of these steps beyond lifting the defeated and ticking its timed
modifiers: nothing can put a status on it (decision 6).

The statuses:

- **Taunt.** The taunted unit's enemy-side picking skills (`SingleTarget`, `Line`) must pick the
  taunter while it is alive and on the other team. The range-limited pick takes it when it is in
  range. The range-free focus pick always takes it, so `BattleTurnExecutor` walks toward it through
  the ordinary approach and fires once it is in range. While the taunter is out of reach, the
  range-limited pick falls back to the ordinary rule. The taunt decides the pick before any
  criterion, so a `Random` skill draws nothing when taunted. Ally-side skills are never taunted. A
  unit carries one taunt, and the latest replaces the earlier one. A taunt whose source has fallen
  forces nothing. The reference's "100% against non-character units" is expressed through
  resistance rather than a special case.
- **Stun** (also used for Freeze). A unit that begins its turn stunned skips it. It does not move,
  fires nothing, does not retreat, and **its cooldowns do not tick**: a stun delays the rotation
  rather than burning it. Its timed modifiers and statuses still tick, and its gauge is spent as
  normal. Stuns do not stack. A new stun
  keeps whichever of the two has more turns left.
- **Shield.** It absorbs damage before HP. It is worth `Magnitude`% of the **caster's** `Defense`
  (`StatusEffects.ShieldPercentDivisor`), level-scaled like every magnitude and truncated. Every
  damage hit and every damage-over-time tick is taken from the shield first. A shield brought to 0
  is removed, and one that outlasts its duration expires. A unit holds one shield: **the larger
  one wins**. On a tie the existing shield is kept and its duration is not refreshed.
- **Damage over time** (also used for Burn and Poison). At application it snapshots
  `DamageFormula` with `Magnitude` as the power: the caster's attacking stat against the target's
  defending stat (by the skill's category), with the element, no crit, no variance and a floor of
  1. Later buffs do not change it. The damage is dealt at the start of each of the affected unit's
  own turns, through its shield. A unit its stacks defeat takes no turn: no skills. `MaxStacks` copies per authored effect ride at once, each on its own clock. At the cap, the
  copy with the fewest turns left is replaced, so the default of 1 refreshes.
- **Knockback.** It pushes the target `Magnitude` whole hexes away from the caster, one tile at a
  time. The distance uses the authored magnitude and is not level-scaled. It stops at the first
  off-board, blocked or occupied tile. "Directly away" is the axial direction with the largest
  Cartesian dot product with the caster-to-target vector, computed exactly in integers as
  `2·q1·q2 + q1·r2 + r1·q2 + 2·r1·r2`. Ties go to the earlier direction. It needs the grid, which
  the executor passes to `SkillEffectApplier.Apply`. With no grid it does nothing. It is never
  stored. Large units ("Unit footprints"): a seven-hex target is immovable, a three-hex one moves at
  most one tile and only where its whole footprint fits, and the push runs between the two units'
  nearest tiles.

**Stacking stat buffs and debuffs.** `MaxStacks` caps the timed copies of one authored effect on a
unit, and each copy expires on its own clock. At the cap, the copy with the fewest turns left (the
oldest on a tie) is reverted and replaced. Instant changes (`DurationTurns` 0) are permanent and
uncapped, as before. With `IsPercent`, the change is `Magnitude`% of the unit's **current** value of
the stat at the moment it lands, truncated. That keeps the rule simple: `BattleUnit` holds no
separate base block. It also means percent stacks compound: two +10% buffs on 100 Attack give 121.
The applied delta is stored and reverted exactly, like any modifier.

**Multi-hit.** A damage effect with `HitCount` greater than 1 runs the whole pipeline once per hit:
its own crit and variance rolls, the execute bonus at the HP the target has at that moment, shield
absorption and the defeat check. It stops as soon as the target falls. Each hit is recorded in
`SkillActivation.Hits`.

**Execute.** Damage is multiplied by `1 + ExecuteBonusPercent / 100 × (max − current) / max`. The
multiplier is applied after the element, crit and variance, and before the single truncation. The
curve is linear in missing HP:

- ×1 at full health.
- ×1.5 at half HP for a 100% bonus.
- ×(1 + bonus/100) at 0 HP. A living target approaches this but never quite reaches it.

A multiplier of exactly 1 is skipped rather than multiplied, so it is bit-exact.

**Heal targeting.** `HpFraction` compares `CurrentHp / Stats.Hp` exactly by cross-multiplying in
64-bit integers (`a.cur × b.max` against `b.cur × a.max`), never as a float. It uses the same
id-order tie-break as every criterion. Use it with `Ally` and `Lowest` to heal whoever is worst off.
`CurrentHp` gets this wrong when maximums differ.

**Skill-level limits.**

- `InitialCooldown` below 0 (the default, −1) starts the counter at the ordinary cooldown, the
  instance's effective cooldown, exactly as before. A value of 0 or more is taken as authored, and a
  tier's cooldown reduction does not touch it. `0` fires on the owner's first turn.
- `MaxUsesPerBattle` is counted per slot by `SkillLoadout.MarkFired`. A slot that reaches it is
  spent (`IsSpent`, `UsesThisBattle`): its counter still ticks, but it is never offered again that
  battle. 0 means unlimited.
- Proc chances, per-battle caps and internal cooldowns for triggered effects live on the avatar's
  passives (see "Avatar passives"), not on individual effects.

**Simulator.** `encounters.json` (since then the game's `enemy-library.json`, whose skills use the
skill library's `SkillData` shape) can now express all of this for future content:

- `StatusResist` per enemy type.
- Per skill: `HitCount`, `ExecuteBonusPercent`, `InitialCooldown`, `MaxUsesPerBattle` and an
  `Effects` list of further effects after the damage effect, with names from the runtime enums. The
  loader validates them.
- `HpFraction` targeting.

The bosses (giant, champion and the fixed colossus) carry `StatusResist` 50. No fixture skill
applies a status, so the report is unchanged.

**Open questions.**

- Whether resistance should also shorten durations, as in some references, rather than only gating
  the chance.
- Whether a shield should scale off the caster's `Defense` or the target's.
- Whether damage over time should be able to crit.
- Whether taunt should also force area skills' positioning.
- Whether knockback into a unit should deal collision damage.
- Whether a stun should also freeze the gauge.
- The simulator attributes damage to the unit whose turn it is. Once content applies
  damage-over-time, that attribution will need the source recorded.

## Element system — TUNABLE STARTING CHART, NOT CONFIRMED BALANCE

Species and skills now carry real elements. The element *list* is settled and is the
`BeastCraft.Creatures.Element` enum: `None`, `Fire`, `Water`, `Earth`, `Air`, `Lightning`, `Ice`,
`Nature`, `Metal`, `Light`, `Dark`. Its values are explicit and serialized into assets by number, so
they are never renamed or renumbered after ship. `CreatureSpeciesSO.Elements` (an `Element[]`)
replaces the old free-text `ElementTags`, which were strings only because the list had not been
decided yet.

**Where the elements come from.**

- **The attacking element is the skill's**, `SkillSO.Element`. A Fire beast can carry a neutral or
  off-element skill, and the caster's own elements play no part in the damage it deals. The default,
  `None`, is a neutral skill.
- **The defending elements are the target's**, `BattleUnit.Elements` — normally its species'
  `Elements`, handed to the `BattleUnit` constructor (an optional parameter, defaulting to no
  affinity) and fixed for the battle. `BattleUnitFactory` (see "Stat assembly and move range"
  below) copies them from the species when it builds a beast.

**How the multiplier applies.** `ElementChart.GetMultiplier(attack, defenders)` is the product of
the attack's single matchup against each defending element, so a dual-element target that is weak
twice takes 4x and strong-plus-weak cancels to 1x. `None` on either side is always 1x. The result
multiplies the stat-based damage `DamageFormula` computes *before* it is truncated to whole HP, and
applies to **damage only** — heals, buffs and debuffs are never scaled. (It was introduced when
damage was still a flat magnitude; the formula now sits underneath it — see "Damage formula".)

**The chart is attacker-side.** Each row is read from the attacking element's point of view and is
only ever looked up in that direction; it is not forced to be symmetric. `2x` is strong, `1.25x`
(`ElementChart.Mild`) is a mild edge, `0.5x` is weak, and every pair not listed is `1x`. This is
**chart v2** (element chart v2, user-approved):

| Attack | Strong against (2x) | Mild against (1.25x) | Weak against (0.5x) |
| --- | --- | --- | --- |
| Fire | Nature, Metal | — | Water, Earth |
| Water | Fire, Metal | — | Lightning, Nature |
| Earth | Lightning, Ice | — | Water, Air |
| Air | Fire, Earth | — | Ice, Nature |
| Lightning | Water, Air | — | Earth, Metal, Light |
| Ice | Nature, Air | — | Fire, Metal |
| Nature | Water, Earth, Dark | — | Lightning, Ice |
| Metal | Lightning, Ice, Light | — | Fire, Air, Dark |
| Light | Dark | Water, Air, Ice, Earth | — |
| Dark | Light | Fire, Lightning, Nature, Metal | — |

**The main eight are normalized.** Among Fire, Water, Earth, Air, Lightning, Ice, Nature and Metal,
every attacking row is 2x against exactly two and 0.5x against exactly two, and every defending
column takes 2x from exactly two and 0.5x from exactly two (`ElementChartTests` pins this). No main
element is better or worse than another on offence or defence by count alone; which matchups come
up in a fight is what separates them.

**Light and Dark are generalists, not counters.** Each is 2x into the other and a mild 1.25x into
four main elements (Light: Water, Air, Ice, Earth; Dark: Fire, Lightning, Nature, Metal — the two
sets split the main eight), and never 0.5x on offence. Defensively each takes one 2x and one 0.5x
from the main eight: Nature 2x and Metal 0.5x into Dark, Metal 2x and Lightning 0.5x into Light.

**Why v2.** v1 was uneven on defence: within the main eight, Earth and Nature each took 2x from
three elements while Fire, Lightning and Ice took 2x from only one (and Metal's row had one 2x
target); Light and Dark each hit only the other. In the simulator (the `elemental` minus the
`neutral` overall marginal, three seeds) the element system gave the Lightning beast +12.5 points and
Fire, Metal and Ice +2 to +4, and cost Dark −8.9, Light −5.2 and Nature −4.0; under v2 every beast
is within −4.3 … +3.8 on the same roster (see `docs/balance/tuning-log.md`, "Element chart v2"). The user wanted Light and Dark as generalists and balanced defensive counts.
The changes from v1, each with its theme:

| Matchup | v1 | v2 | Why |
| --- | ---: | ---: | --- |
| Air → Fire | 1x | 2x | A gust snuffs flame |
| Water → Metal | 1x | 2x | Rust |
| Earth → Ice | 1x | 2x | Rock shatters ice |
| Metal → Lightning | 0.5x | 2x | The lightning rod (Lightning → Metal stays 0.5x) |
| Nature → Lightning | 1x | 0.5x | Wood insulates |
| Air → Nature | 2x | 0.5x | Forests withstand wind |
| Metal → Air | 1x | 0.5x | A blade can't cut wind |
| Metal → Dark | 1x | 0.5x | Dark resists Metal |
| Lightning → Light | 1x | 0.5x | Light resists Lightning |
| Light → Water, Air, Ice, Earth | 1x | 1.25x | Light as a generalist |
| Dark → Fire, Lightning, Nature, Metal | 1x | 1.25x | Dark as a generalist |
| Water → Earth | 2x | 1x | Now neutral |
| Earth → Metal | 2x | 1x | Now neutral |
| Air → Lightning | 0.5x | 1x | Now neutral |
| Nature → Fire | 0.5x | 1x | Now neutral |

**These values are a tunable starting default, not producer-confirmed balance** — the same standing
as the arena radii and the deployment-zone split. The set of elements is fixed; which pairs are
strong, mild or weak, and whether 2x / 1.25x / 0.5x are the right sizes, may still move as balance
work measures real fights. `ElementChart` (`Strong`, `Mild`, `Weak` and its rows) is the single
place to change them.

## Encounter preview — DECIDED (elements visible by default); partial scouting is a FUTURE KNOB

**Enemy elements are visible and counterable.** Before the player places a team, the game shows the
encounter: `EncounterPreview.Build(enemies, arena, detail)` (`BeastCraft.Battle.Scouting`) turns the
lineup into its arena, its total enemy count and a list of `EncounterPreviewGroup` lines, front to
back. Anything that holds a lineup implements `IEncounterPreviewSource` (element, stance, display
name) — game encounter data once it exists, the balance simulator's fixtures today — so the
preview never depends on how enemies are authored. It is pure (no randomness, no battle state), so
the team-building screen can show it and the suggested team (below) reads it.

**Game rule: the enemy elements are free, before every fight (user decision).** The pre-fight
screen always shows the `Full` preview — every line's name, element, stance and count — at no cost
and with no scouting action, before the team is placed; `EncounterPreview.Build`'s default detail,
`Full`, *is* that preview. Not having scouted is no longer a hidden-information penalty: the
difficulty table stays calibrated on the scouted counter-pick (the targets are unchanged), and the
information that pick needs is simply always on screen.

**Suggested team (`TeamSuggester`, `BeastCraft.Battle.Scouting`).** From the preview and the beasts
the player owns, `TeamSuggester.Suggest(TeamSuggestionRequest)` names a counter team:

- **Inputs.** `Preview` (the encounter's `EncounterPreview`), `Owned` (a `TeamSuggestionCandidate`
  per owned beast: species and level; the caller maps the save's `OwnedBeast`s through the species
  catalog), `TeamSize` (default 4), `MinVanguards` (default 1), `Bonds` (the skill library's team
  bonds) and `EncounterCanAfflict` (`TeamSuggester.CanAfflict` over the enemies' kits; default true).
- **Scoring.** Each beast scores, per enemy, its kit element's chart multiplier into the enemies minus
  half theirs into it; `LevelWeight` (0.1, an untuned starting knob) is taken off per level below
  the owner's highest-levelled candidate. A team scores its members plus, per active bond
  (`TeamBondResolver`), the bond's weight (`TeamSuggester.BondWeights`, fitted to the simulator's
  panel excess; 0.5 per tier for an unlisted bond; nothing for a bond that answers only afflicted
  allies when the encounter cannot stun or burn; 0.125 per stack of a scaling bond).
- **Choice.** Every combination of the team size over distinct species (a species owned twice counts
  once, as its highest-levelled copy) in lexicographic order of the owned list; the best team with
  at least `MinVanguards` Vanguards wins, ties to the earlier combination; with too few Vanguards
  owned, the best team overall (`TeamSuggestion.MeetsVanguardMin` false). Deterministic, no rng.
- **One implementation.** It *is* the balance simulator's bond-aware scouted picker, which the
  difficulty table is calibrated on: the simulator calls `TeamSuggester` for the scores, bond
  weights and choice rule, and every run checks that `Suggest`, given the whole roster at one level,
  names exactly the simulator's pick (stderr `TeamSuggester parity`; 100% of compositions, or the run
  fails).

**When the suggestion is shown (`TeamSuggestionPolicy`, user decision).** The suggested team is
offered only after the player has lost that battle **3** times (`MinLossesBeforeSuggestion`), and
never when the player has turned suggestions off in the game settings
(`PlayerSettings.TeamSuggestionsEnabled`, default on; see `docs/design/progression-and-saves.md`,
"Player settings"): `TeamSuggestionPolicy.ShouldSuggest(lossesOnThisEncounter, settings)`. The loss
count is per map location: the map run counts the losses at the location being retried
(`MapRun.NodeAttempts` at `MapRun.NodeAttemptsNodeId`; a loss elsewhere restarts the count, a clear
resets it), read by `CampaignRules.LossesAt(run, nodeId)`. The one call site is
`CampaignRules.SuggestionFor(save, nodeId, settings, encounters, content, teamSize)`: it applies the
policy and, when it holds, runs `TeamSuggester.Suggest` over every beast the save owns against the
node's `EncounterPlan` (its `Full` preview, `CanAfflict` over the enemies' kits, the content's team
bonds), returning the suggested beast ids (`CampaignTeamSuggestion`), or null. The pre-fight UI that
shows it is not built. The element preview itself is never gated.

- **Grouping.** Enemies with the same element, stance and display name (ordinal) are one line
  with a count; lines keep the order of their first enemy in the lineup.
- **Detail (`ScoutingDetail`).** `Full` — the default, and the only level the game uses now —
  shows every line's name, element, stance and count. `ElementsOnly` hides names and stances and
  merges the lines that share an element (so hidden fields do not leak through the number of lines).
  `DominantElementOnly` leaves one line: the element carried by the most enemies (a tie goes to the
  lowest `Element` value, so `None` wins a tie it is part of) with the total count. The partial
  levels exist for a later fog-of-war mechanic (an unscouted region, a "mysterious" encounter, a
  scouting skill that upgrades the detail); none is wired to anything yet, and the enum's values are
  explicit so a saved setting survives additions.
- **Why visible by default.** The element chart is a strategic layer only if the player can act
  on it. Hidden elements would make it a coin flip the player cannot influence, and the chart's
  normalization (every main element 2x into two and 0.5x from two) already means no single team is
  best against everything, which is what makes picking for the encounter a real decision.

**What it is worth (simulator, not confirmed balance).** The balance simulator's "PvE scouted
picking" section (`Tooling/BalanceSim`, README "Scouted picking") replays the choice against the
battles it already runs: a plain element counter-pick from the `Full` preview — each beast scored on
its chart multiplier into the enemies minus half theirs into it, the best four fielded with at least
one Vanguard — raises the clear rate at the calibrated difficulty from about 50% to about 70% (three
seeds; the same picks in the `neutral` control gain nothing, so the gain is the chart's). It is
largest against a lone giant or an elite group (+25 to +30 points); against squads and hordes,
whose mixed elements dilute any counter, it is +16 to +19 and simply bringing a strong lineup does
better. The counter-pick fields every beast in every shape (between about 13% and 63% of picks each); none
becomes a must-pick or a never-pick. See `docs/balance/tuning-log.md`,
"Scouting and counter-picking", for the numbers and pick rates.

**Difficulty assumes the player scouts (decision).** Encounter difficulty is tuned for a player who
reads the preview and counter-picks, not for the average of every possible team: the simulator now
calibrates each shape, level and kit mode so the team its bond-aware picker fields (the counter-pick
above plus active team bonds) clears about 50% (`--calibrate-on bonds`, the default; README
"Difficulty calibration"). Not scouting is then visibly worse, and by how much is itself a design
number the report tracks as the **no-scouting** rate: on three seeds the average team clears about
25% of `elemental` encounters so calibrated (about 10% against a lone giant, 22% against an elite
group, 26% against a squad and 43% against a horde) and about 42% in the `neutral` control, where
the chart gives the pick nothing to exploit. Per-beast balance is judged on marginals normalized to
a 50% cell, since the lower average-team clear rate shrinks raw marginals. See
`docs/balance/tuning-log.md`, "Scouting-based calibration".

## Damage formula — TUNABLE STARTING DEFAULTS, NOT CONFIRMED BALANCE

Damage is now stat-based, for beasts and the avatar alike. The formula's shape and constants are
lead engineering decisions made so the headless balance simulator has something real to measure;
**none of them are producer-confirmed balance**, and every number below is expected to move.

**Physical and special — `DamageCategory`.** Each skill carries a `SkillSO.Category`
(`DamageCategory.Physical = 0`, `Special = 1`; explicit values, serialized by number, never renamed
or renumbered — append only). Physical damage reads the caster's `Attack` against the target's
`Defense`; special damage reads `SpecialAttack` against `SpecialDefense`. The default is `Physical`.
The category belongs to the skill as a whole, like its element.

**The formula** (the static, pure `DamageFormula`, one place to tune) — **AMENDED: adopted from
Sword x Staff** (user-approved; see
[`docs/balance/research-sword-x-staff.md`](../balance/research-sword-x-staff.md)). It replaces the
first, Pokémon-style level-term formula:

```
base   = Power / 100 × A × A / (A + DefenseWeight × D) × GlobalScale
damage = max(1, truncate(base × ElementChart multiplier × crit × roll / 100 × execute × level))
crit   = max(MinCritMultiplier, CritMultiplier) = 1.5 on a critical hit, else 1
roll   = a whole percent, uniform on [90, 110]
level  = clamp(1 + k × d + q × d × |d|, 1 − cap, 1 + cap),  d = caster level − target level
         (k = 0.012, q = 0.009, cap = 0.4; exactly 1 between equal levels)
```

- **`Power` is a percent of the attacking stat.** It is the `Damage` effect's
  `SkillEffect.Magnitude`: Power 120 means 120% of the caster's `Attack` (physical) or
  `SpecialAttack` (special) before mitigation — the reference's "skill base = stat × skill
  coefficient". `A` / `D` are the category's stats, read from the units' **current effective**
  `Stats` at the moment the effect lands, so buffs and debuffs move damage.
- **Mitigation is `A / (A + DefenseWeight × D)`**, the reference's armor term: a share of the hit,
  never a subtraction. Defense has smooth diminishing returns — against Attack 100, Defense 0, 100,
  200, 300 lets through 100%, 50%, 33%, 25% — and never negates a hit. `A` appears twice (base and
  mitigation), so Attack is worth slightly more than linear: doubling Attack against equal Defense
  multiplies the hit by 2.67.
- **Level difference — AMENDED (milestone 2, user decision: an under-levelled team must not clear
  content).** Stats scale with level through the growth curve, and with `A`, `D` and HP on the same
  curve a hit between two *equally* levelled units takes the same share of HP at every level, up to
  integer rounding. (The old `(2 × Level / 5 + 2)` term only held that loosely: 20% at level 1
  against 35% at level 100.) But the curve flattens: from level 30 up, a level is only about 1-2% of
  stats, so on stats alone a team 5 levels under its encounters still cleared 26-36% of them
  (against 50% at level; balance simulator, `--level-gap`), and levelling barely mattered. So every hit
  now also carries a **level-difference multiplier** of the caster's level minus the target's,
  `d`: `clamp(1 + k d + q d |d|, 1 − cap, 1 + cap)` (`DamageFormula.GetLevelMultiplier`). It
  applies **both ways** (an under-levelled team hits softer *and* is hit harder), to beasts,
  enemies and the avatar alike (the avatar at its own level, `AvatarProgress.Level`). The convex
  `q` term keeps a small gap mild and makes a wide one decisive: ×1.02 / 1.06 / 1.12 / 1.29 / 1.4
  (cap) at 1 / 2 / 3 / 5 / 7+ levels over, ×0.98 / 0.94 / 0.88 / 0.72 / 0.6 under. Equal levels
  are exactly 1 and are **not multiplied at all**, so an equal-level battle is bit-identical to the
  formula without the term (the committed balance report did not move). **Damage over time**
  inherits it (its per-turn amount is computed through the formula when applied); **heals, shields,
  stat changes, status chance and knockback do not** read level. It takes **no random draws**.
  Raw overloads: `Compute(power, attack, defense, element[, variancePercent, isCrit[, execute[,
  levelMultiplier]]])` (the eight-argument form takes the level multiplier; exactly 1 is the
  identity) and `ComputeBase(power, attack, defense)`.
- **How k, q and cap were chosen** (tuning log, "Level-difference modifier"). Calibrated on the
  scouted pick at equal levels (50%), the targets were: 2-3 levels under ≈ 20-35%, 5+ under < 10%,
  at every level band. A `--level-gap` sweep of linear `k` ∈ {0.025, 0.03, 0.035, 0.04} × cap ∈
  {0.3, 0.4} missed "< 10% at 5 under" at levels 50-90 for every `k` that kept 3 under above 20%;
  the convex term fixed it (first k = 0.025, q = 0.005). The milestone-2 final retune (a stronger
  avatar, the beast retune) left 3 under at level 50 on the 20% edge and 5 under at level 70 at 10.6%,
  so the curve was made more convex at the same 3-level multiplier: **k = 0.012, q = 0.009**.
  Measured (3 seeds, `elemental`, every shape averaged, the scouted team): 2 under 30-31%, 3 under
  20-25%, 5 under 6-9% at levels 30-90. **Level 10** is steeper (25% / 17% / 3%): there a level is a
  large share of stats, so the stats already do most of the work. The `neutral` control mode is
  steeper throughout (3 under ≈ 9-14%). The cap binds from 7 levels apart (6 is ×1.396), where a
  clear is already rare (≤ 4%).
- The element multiplier is the skill's element against the target's elements, exactly as before.
  The caster's own elements still do nothing.
- **Arithmetic.** The base is computed in double precision (basic IEEE operations only, one division
  last, so an exactly whole hit never truncates to one less) and truncated once at the end, after the
  element, crit, variance, execute and level multipliers (in that order). Crits and rolls are covered under
  "Variance and critical hits" below.
- **Guards:** `Power <= 0` deals 0 (so a negative damage magnitude never reads as a heal); any
  positive `Power` deals at least 1; `A <= 0` makes the base exactly 0 (so the hit lands on the floor
  of 1); `D < 0` is treated as 0 (no mitigation, and no division by zero while `A > 0`).

| Constant | Value | Meaning |
| --- | --- | --- |
| `PowerPercent` | 100 | Power is authored in percent of the attacking stat. |
| `DefenseWeight` | 1.0 | Weight of `D` in the mitigation term: at 1, equal Attack and Defense halve a hit. |
| `GlobalScale` | 1.0 | A uniform multiplier on every hit, the lever for overall fight length. |
| `MinimumDamage` | 1 | The floor for any positive-power hit. |
| `CritMultiplier` / `MinCritMultiplier` | 1.5 / 1.3 | See "Variance and critical hits". |
| `LevelDifferencePerLevel` (k) | 0.012 | Linear part of the level-difference multiplier, per level of difference. |
| `LevelDifferenceConvex` (q) | 0.009 | Convex part: `q × d × |d|`, so a wide gap bites harder than a narrow one. |
| `LevelDifferenceCap` | 0.4 | The level multiplier stays within [0.6, 1.4]. |

**How the constants were chosen.** `DefenseWeight` and `GlobalScale` start at 1, as in the
reference, and the balance simulator's kit and enemy powers were **rescaled instead** so that a
neutral hit between two average level-50 roster beasts removes the same share of HP as under the old
formula. For the sim's Blast (special, old Power 40, new Power 68) between two average roster beasts:

| Level | Old damage / HP | New damage / HP |
| --- | --- | --- |
| 1 | 3 / 17 (17.6%) | 5 / 17 (29.4%) |
| 50 | 20 / 65 (30.8%) | 20 / 65 (30.8%) |
| 100 | 36 / 114 (31.6%) | 35 / 114 (30.7%) |

Level 50 and 100 match; level 1 rises to the same share as every other level, because the old level
term under-scaled low-level damage. The old formula's powers map to new ones by
`P' ≈ 1.52 × P + 7` (matching `0.44 × P + 2` against `P' / 100 × A / 2` at `A = D ≈ 58`); the tuning
log lists every rescaled power.

**Worked examples (starter roster, `medium` curve, Power 100; a 100% roll and no crit, i.e. the
deterministic fallback).** Phoenix (Fire) hitting Golem (Earth) with a Fire skill — Fire is weak
against Earth (0.5x) — with the same hit from a neutral skill in brackets:

| Level | Phoenix → Golem, physical | Phoenix → Golem, special | Golem HP |
| --- | --- | --- | --- |
| 1 | 3 (neutral: 6) | 5 (neutral: 10) | 22 |
| 50 | 12 (neutral: 24) | 18 (neutral: 37) | 83 |
| 100 | 21 (neutral: 43) | 33 (neutral: 66) | 146 |

The reverse, Golem hitting Phoenix with a neutral Power-100 skill: 8 / 31 / 54 physical and
4 / 15 / 27 special at levels 1 / 50 / 100, against Phoenix's 14 / 53 / 92 HP — the glass cannon and
the wall still reading as intended. `DamageFormulaTests` pins the Fire examples.

**A zero attacking stat deals the floor.** With `A = 0` the base is exactly 0, so a unit with no
attacking stat deals `MinimumDamage` (1) per damage effect regardless of power or element. This
matters for the zero-stat `BattleAvatar.Create(skills)` avatar, which under the old formula chipped
the +2 offset (2, or 4 on a strong matchup). An avatar meant to hit hard should be built with stats
via the statful overload.

**Levels.** `BattleUnit` carries a `Level` (at least 1; an optional constructor argument defaulting
to 1). `BattleUnitFactory.CreateBeast` records the level it assembled the stats at. The statful
`BattleAvatar.Create` takes the avatar's own level (`AvatarProgress.Level`, default 1) — see
"What is not settled yet". Level reaches damage through the stats it assembled and through the
level-difference multiplier above (caster against target), which is exactly 1 between equal levels.

**Still deferred:** a same-element attack bonus (STAB), a balance lever to add once there are
fights to measure it against; the reference's flat skill damage, damage boost / damage resistance and
skill damage reduction terms. (Healing now scales with the caster's `SpecialAttack`, with no
variance; see "Effect application".) Random
variance and critical hits were deferred here at first, because they make a battle random and the
balance simulator needed a fixed answer per seed; they are now in, reproducible from the battle's
seed (below).

### Variance and critical hits — DECIDED (user); the numbers are TUNABLE DEFAULTS

**User decisions:** hit damage is random with modest variance, and every beast has a
critical-hit chance of its own that skills and gear can raise. The numbers are lead defaults backed
by desk research — [`docs/balance/research-crit-variance-speed.md`](../balance/research-crit-variance-speed.md)
(variance and crit conventions in comparable games, expected-value and sample-size notes, and the
decisions taken for Beast Craft) — and are named constants on `DamageFormula`:

| Constant | Value | Meaning |
| --- | --- | --- |
| `VarianceMinPercent` / `VarianceMaxPercent` | 90 / 110 | The variance roll: a whole percent, uniform, both ends inclusive (`rng.Next(90, 111)`). Tighter than Pokémon's 85–100%, so a player can still plan on lethal thresholds. |
| `CritMultiplier` | 1.5 | What a critical hit multiplies the hit by. |
| `MinCritMultiplier` | 1.3 | Floor of the crit multiplier, from the reference's `max(1.3, 1 + critDamage − critDamageReduction)`. `DamageFormula.GetCritMultiplier(reduction)` = `max(1.3, 1.5 − reduction)`; the formula passes 0 today, so the floor never binds until a crit-damage or crit-resist stat exists. |
| `MinCritChance` / `MaxCritChance` | 0 / 100 | The crit chance is clamped into this range when it is rolled. |

- **Order of operations:** `base × element × crit × roll / 100`, truncated once, then floored at
  `MinimumDamage` (1). `Power <= 0` still deals 0 whatever the rolls. A 100% roll is applied as no
  multiplication at all, so the deterministic numbers are bit-exact. The 0x-immunity note above
  still holds: if an immunity is ever added, it must return 0 before the floor, and neither a crit
  nor a high roll may lift it.
- **Crit chance is a stat.** `StatType.CritChance` (value 7, appended; nothing renumbered) and
  `StatBlock.CritChance`, an integer percent. The roll is `rng.Next(100) < chance`, with the
  caster's *current effective* chance clamped into [0, 100], so 0 never crits and 100 (or more)
  always does. The target plays no part — there is no crit resistance.
- **It does not scale with level**, exactly like `MoveRange` (`GetStatAtLevel` returns the authored
  base): a chance authored in single digits would round to 0 or 1 under a 0.15 level-1 scale.
- **Skills and gear raise it through the existing paths.** `BuffStat` / `DebuffStat` with
  `AffectedStat = CritChance` move it like any stat (timed or instant, clamped at 0, reverted on the
  affected unit's own turns). A `StatModifier` on `CritChance` adds flat then percent in
  `StatCalculator`, with the ordinary 0 floor (not the HP floor of 1) and no ceiling — a block may
  hold more than 100, and the excess is simply wasted when the chance is clamped at roll time, which
  also keeps a timed buff's reversion exact.
- **Crit damage is not a stat** (deferred; if added, gear- and skill-only, per the research). When a
  crit-damage bonus or a crit-damage reduction arrives, it goes through
  `DamageFormula.GetCritMultiplier`, whose 1.3 floor (`MinCritMultiplier`) is already in place, so no
  amount of reduction can make a crit worth less than +30%.
- **Heals, buffs and debuffs never roll.** Variance and crits apply to damage only.

**The rng and the draw order.** The battle's one `System.Random` — the `rng` `BattleTurnExecutor`
already carries for `ExecuteTurn` / `RunBattle` — is threaded through
`SkillEffectApplier.Apply(activation, caster, rng)` into `DamageFormula.Roll(caster, target, skill,
power, rng)`. Each damage effect that lands on a target draws **exactly two numbers, crit first,
then variance**, always both — even at a 0% or 100% chance or zero power — so the number of draws
never depends on stats. Draws happen in the order the battle fires skills: the unit's ready slots in
stack order (on the avatar's own turn, its activations in slot order); within a skill, target-major and then in authored effect
order; an effect skipped because its target is already defeated draws nothing. Targeting draws from
the same stream only for `SkillTargetingCriterion.Random`. A battle is therefore random but
reproducible: the same seed replays it exactly.

**The deterministic fallback.** A `null` rng means a 100% roll and no crit, and draws nothing.
`DamageFormula.Compute(caster, target, skill, power)`, the raw `Compute(power, attack, defense,
element)` and the two-argument `SkillEffectApplier.Apply(activation, caster)` are that fallback, so
every exact-number example in this document and every exact-number test holds. Tests pin specific
rolls with `Compute(power, attack, defense, element, variancePercent, isCrit)`.

**Reporting.** `DamageFormula.Roll` returns a `DamageRoll` (amount, `IsCrit`, `VariancePercent`),
and `SkillEffectApplier` records one `DamageHit` (target plus roll) per landed damage effect on
`SkillActivation.Hits`, so a battle log or the simulator can tell a crit from an ordinary hit. It is a
record on the activation, not an event system.

**Roster values** (user-approved; `BaseStats.CritChance` in `beast-roster.json`): Thunderbird 15,
Basilisk 12, Phoenix 10, Griffin 8, Tarasque 6, Kirin 5, Frost Wyrm 5, Leviathan 3, Treant 3,
Golem 2 — the fast strikers and casters high, the tanks low. Crit chance is **outside** the six-stat
budget, like move range. At these values crits add 1–7.5% to a beast's average damage
(`1 + 0.5 × chance`); the variance roll averages 100% and adds none.

## Stat assembly and move range

**Move range is a stat — DECIDED.** `StatType.MoveRange` (value 6, appended; the existing axes are
never renumbered) and `StatBlock.MoveRange` sit alongside the six combat axes. (`StatType.CritChance`,
value 7, follows the same pattern — exempt from level scaling, moved by gear and buffs; see "Damage
formula".) Each species authors a
base move range in its `BaseStats`, and from there it behaves like any other stat with one
exception:

- **It does not scale with level.** `CreatureSpeciesSO.GetStatAtLevel(MoveRange, level)` returns the
  authored base at every level. Growth curves run from a small fraction at level 1 (0.10-0.20 for
  the authored curves; see "Starter roster") to 1 at max level, which suits stats in the tens and
  hundreds but would round a small integer like 3 down to 0 or 1 for much of the early game. Move range is a tactical constant of the species, not something that
  grows.
- **Gear modifies it.** A `StatModifier` on `MoveRange` adds to it (flat and percent) like any other
  axis — boots that grant +1 movement are ordinary gear data.
- **Buffs and debuffs modify it.** `BuffStat` / `DebuffStat` with `AffectedStat = MoveRange` move it
  mid-battle, timed or instant, through exactly the same path as any other stat, including the clamp
  at 0 and the revert on the affected unit's own turns.

`BattleUnit.MoveRange` is no longer an independent settable number: it is a read-only view of
`Stats.MoveRange`. Timed buffs are folded straight into `BattleUnit.Stats` (there is no separate
overlay), so that one field is always the effective value. `BattleTurnExecutor` reads it as the
turn's movement budget *after* expiring timed modifiers, and still treats a negative value as 0.

**Stat assembly — engineering default, not confirmed balance.** `StatCalculator` builds a unit's
starting stat block from its species, level and equipped gear. Per stat axis, in order:

1. **Base at level** — `CreatureSpeciesSO.GetStatAtLevel` (growth-curve scaled, except `MoveRange`
   and `CritChance`).
2. **Plus every `FlatBonus`** on that axis, summed across all equipped gear.
3. **Times `1 + (the sum of every PercentBonus)`** on that axis, applied once. Percentages add rather
   than compound (two +10% items are +20%), so gear order never matters, and they apply after the
   flat bonuses, so a percentage also scales what gear added.
4. **Rounded to the nearest integer and clamped** — every stat at least 0, `HP` at least 1.
   `CritChance` has no ceiling here; it is clamped to 100 when rolled (see "Variance and critical
   hits").

Gear whose `MinimumLevel` is above the creature's level contributes nothing; refusing the equip is
the equipment screen's job, but an under-levelled item never grants stats whatever state a loadout
arrives in. Null gear and null modifiers are skipped. A second overload takes an explicit base
`StatBlock` plus a modifier list (with `CollectModifiers` turning a gear list into one) for a
participant with no species behind it — which is how the avatar's stats are assembled from its
`AvatarStatsSO` base (at the avatar's level) and its `AvatarGearSO` (decision 6, amended). Avatar
gear has no minimum level.

`BattleUnitFactory.CreateBeast` is the pass that assembles a battle-ready `BattleUnit` from a
creature: stats from `StatCalculator`, elements copied from the species, and the equipped skill
loadout, id, team and position passed through. It takes species, level and gear directly because
there is still no persistent creature-instance type; when one exists, it is the natural input. The
battle then layers timed buffs and debuffs on top of the assembled block as before.

This is stat assembly only. Damage is computed separately, by `DamageFormula`, from the block this
produces as buffed or debuffed since (see "Damage formula").

## Pre-battle placement — DATA MODEL AND VALIDATION ONLY

Decision 2 settles that beasts are pieces placed on the grid before the fight, and the "Core loop"
section above calls placement the one point where the player moves a piece by hand. Nothing until
now defined what a *legal* starting layout actually is. This pass answers that and only that: which
tiles each side may deploy onto, and whether a proposed set of starting positions is allowed.

### Deployment zones — TUNABLE DEFAULT, NOT CONFIRMED BALANCE

The board is split into three bands along the axial **`R` axis**. The player owns the
`HexGrid.DeploymentZoneDepth` rows with the most positive `R`, the enemy owns the mirror-image rows
with the most negative `R`, and the rows between them are a **neutral no-deploy band** belonging to
neither side. Depth is `ceil(Radius / 2)`, so it scales with the arena preset rather than being
fixed:

| Preset | Radius | Depth | Tiles per zone | Neutral band |
| --- | --- | --- | --- | --- |
| Small | 3 | 2 | 9 | 19 |
| Medium | 5 | 3 | 21 | 49 |
| Large | 7 | 4 | 38 | 93 |

The smallest of those seats `BattleFormat.LargeGroup`'s six beasts with three tiles to spare, so
**every format fits on every arena preset** — checked against the radii the presets actually use
(row `R` of a hexagon of radius `n` holds `2n + 1 - |R|` tiles), not assumed.

**Why `R`.** All three cube axes split a hexagon into two congruent regions — negating every cube
component is a 180° rotation that maps the board onto itself and each zone exactly onto the other —
so symmetry alone does not pick between `Q`, `R` and the implied `S`. `R` wins because it is the
axis `HexCoordinate` already calls the "row" axis: a constant-`R` band is a single straight run of
tiles across the board, so the front line reads as a straight line and "your half / their half" is
legible without a diagram. `Q` or `S` would be geometrically identical but would land the front line
on a diagonal, which is harder to read and harder to describe to a player.

**This is a tunable implementation default, exactly like the hex radii backing each arena preset,
and for the same reason.** Nothing about deployment geometry has been through encounter design. What
the producer confirmed (decision 2) is how many beasts a format deploys, not where they may stand.
The three-band split and the half-the-board depth were picked so roughly half the board is contested
ground and each side still has real depth to arrange itself in; expect both to move once encounter
design gives them something to be balanced against. A zone query is deliberately a **pure shape
query** like `HexGrid.GetTilesInRange` — terrain and occupancy are ignored, because a blocked or
taken tile is still on its own side of the board.

### Validation

`PlacementValidator.Validate` answers whether a proposed layout for one team is legal against a
grid and a `BattleFormat`, checking every rule against every position rather than stopping at the
first failure: count within 1..`MaxPartySize` inclusive (a range, not an exact match — decision 2
says "up to 4" and "up to 6"), no tile repeated within the proposal, every tile in bounds, not
terrain-blocked, inside the validating team's own zone, and not colliding with a tile already
spoken for.

Results follow the same rich-result pattern as `BattleSkillOutcome` / `BattleResult`: a
`PlacementValidationResult` carrying a batch-level `PlacementCountStatus` and one `PlacementOutcome`
per proposed position, valid ones included, so a caller can line them up index for index with what
it drew. The per-position `PlacementStatus` is a **flags** enum rather than a single value — unlike
`BattleSkillStatus`, whose arms describe genuinely exclusive situations, one tile really can be in
the wrong half *and* buried under terrain *and* already taken at once, and a layout is only worth
validating if it hands back every reason it was rejected. The one exception is off-board tiles,
which report `OutOfBounds` alone because "also blocked" and "also outside the zone" are not
additional facts there.

**Large units** ("Unit footprints") deploy only where their whole footprint lies in their zone.
`DeploymentPacker` seats a side automatically, front-most first, units of any size (the balance
simulator uses it for its enemies); on a Small board, whose zones are two rows deep, a seven-hex
unit fits nowhere. Beasts are one tile, so the player's validation below is unchanged; the
already-placed tiles it checks against must include every tile a large enemy covers.

**Validation never touches the board**, so it is safe to call on every drag of a marker. Committing
is the separate `PlacementValidator.TryPlaceAll`, which validates first and is all-or-nothing: a
batch with one illegal tile places nobody rather than seating the legal five. It takes
`PlacementRequest` (unit id plus tile) rather than bare coordinates, because `HexGrid.TryPlaceUnit`
is keyed by unit id — a list of tiles says where somebody should stand but not who. Validation
itself needs no ids, which is why the coordinate-only `Validate` overload remains: a placement UI
wants to ask "is this layout legal" long before it has decided which beast fills which slot.

### There is no placement UI, and building one is a different kind of task

This pass is **validation only**. It checks a proposal; it does not generate one, and there is no
placement AI here or anywhere else. There is no UI, no scene, no MonoBehaviour and no input
handling — consistent with every pass so far, all of which have been data structures and algorithms
validated through the CI compile/format stub.

That is worth stating explicitly because the UI is not simply the next increment of this work. Every
system built so far is plain C# that a headless compile can fully exercise. A placement screen is
not: it is real Unity scene and prefab work, hex-tile hit-testing and screen-to-axial conversion,
drag-and-drop input, zone and validity highlighting, and it can only genuinely be tested by opening
the project in the Editor. It is a **separate, later, and materially different** task, and it should
be scoped as one rather than treated as the tail end of this one.

## Starter roster — SIMULATOR-TUNED DATA, NOT CONFIRMED BALANCE

The first ten beasts, one per element, are authored as data. Names, elements and archetypes are
approved. **The numbers below are the third simulator-tuned pass**: first-draft stats chosen to
express each archetype were tuned by hand against the headless balance simulator's PvE mode,
re-tuned for the ATB gauge, combat stances, variance and crits and the generated mixed encounters,
and then re-tuned a third time, together with the skill numbers, against the real game setup: each
beast's authored skill kit, the library avatar with its passives, the square-root speed gauge and
the mitigation damage formula, with base Speed widened so the fastest beast gets 10–15% more turns
than the slowest. A light follow-up pass re-fit it to element chart v2 (four stat lines and nine
skill numbers; see [`docs/balance/tuning-log.md`](../balance/tuning-log.md), which has the first
draft and every pass, "Element chart v2" and its "Thunderbird range vs move" experiment last). Nothing here is confirmed balance; the numbers are expected to move again
once real encounters exist.

| SpeciesId | Beast | Element | Archetype | Stance | Curve | HP | ATK | DEF | SpA | SpD | SPE | Six-stat total | Move | Crit |
| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `phoenix` | Phoenix | Fire | Glass cannon | Ranged | medium | 92 | 102 | 70 | 120 | 85 | 104 | 573 | 4 | 10% |
| `leviathan` | Leviathan | Water | Tank | Vanguard | medium | 132 | 86 | 126 | 86 | 100 | 94 | 624 | 3 | 3% |
| `golem` | Golem | Earth | Pure wall | Vanguard | medium | 150 | 109 | 137 | 50 | 96 | 88 | 630 | 2 | 2% |
| `griffin` | Griffin | Air | Fast skirmisher | Skirmisher | medium | 116 | 118 | 97 | 85 | 91 | 108 | 615 | 5 | 8% |
| `thunderbird` | Thunderbird | Lightning | Burst striker | Skirmisher | medium | 116 | 117 | 88 | 108 | 91 | 110 | 630 | 4 | 15% |
| `frost_wyrm` | Frost Wyrm | Ice | Control / attrition | Vanguard | medium | 98 | 74 | 124 | 103 | 118 | 99 | 616 | 3 | 5% |
| `treant` | Treant | Nature | Support-tank | Vanguard | medium | 134 | 77 | 98 | 105 | 124 | 92 | 630 | 3 | 3% |
| `tarasque` | Tarasque | Metal | Armored bruiser | Vanguard | medium | 112 | 130 | 127 | 54 | 78 | 97 | 598 | 3 | 6% |
| `kirin` | Kirin | Light | Support caster | Ranged | medium | 115 | 51 | 90 | 140 | 124 | 101 | 621 | 4 | 5% |
| `basilisk` | Basilisk | Dark | Ranged assassin | Ranged | medium | 103 | 94 | 79 | 110 | 94 | 105 | 585 | 5 | 12% |

Stats are max-level values (curve scale 1). **All ten beasts share the `medium` growth curve for
now, by user decision**; differentiating curves per beast is deferred to the headless balance
simulator. The drafting rules:

- **Shared budget.** Every beast's six combat stats sum to a shared budget of 600, and the roster
  tests allow ±5% (570–630). Archetype comes from how the budget is *distributed*, not from raw
  power. The first draft put every beast at exactly 600; every tuning pass used the ±5% band as a
  balance lever. After the third (authored kits) and the element chart v2 follow-up, totals run
  from 573 (Phoenix, whose kit and Ranged stance carry it) and 585 (Basilisk, whose power now comes
  from its kit: execute, poison and a 45% petrify) to 630 (Thunderbird, Treant and Golem on the
  ceiling). Stats a beast's kit
  never reads are no longer free budget either: Golem's and Tarasque's `SpecialAttack` and Phoenix's
  `Attack` are unused by their default kits, while every heal now reads the caster's
  `SpecialAttack`. If
  the simulator later gives some beasts a slower curve, whether they deserve a larger budget as
  payoff for a weak early game is a balance question for it, not something this pass assumes.
- **Speed is a narrow band of *turns* (user decision): the fastest beast gets 10–15% more turns than
  the slowest.** The roster uses base Speed 88–110 (1.25×), which under the square-root gauge is a
  **1.118× turn ratio** (`TurnManager.FillRateForSpeed` 1049 vs 938); `BeastRosterTests` pins that
  fill-rate ratio to 1.10–1.15, and the order below. The history of the rule:
  **Until the authored-kits retune, the band was on the Speed stat: the fastest base Speed at most 1.15× the slowest.**
  Under the ATB gauge (decision 3) Speed is an action economy (twice the Speed is twice the turns),
  so the first pass's 40–120 spread gave Thunderbird three turns for each of Golem's, and no amount
  of bulk made up for it: the slow Vanguards were bottom three nearly everywhere. The user expects
  the fastest and slowest beasts to differ by about 10–15%; the roster uses 92–105 (1.14×). This
  replaces the lead default in
  [`docs/balance/research-crit-variance-speed.md`](../balance/research-crit-variance-speed.md) §5
  ("target ~2.5–3× slowest:fastest"), which that research marked unsourced (no source gives a target
  ratio) and left for the simulator to validate. The simulator did not bear it out, and the genre's
  answer for slow tanks is taunt/threat and damage reduction rather than speed (same research, §4).
  Within the band the archetypes keep their **order** (now Thunderbird 110 > Griffin 108 > Basilisk
  105 > Phoenix 104 > Kirin 101 > Frost Wyrm 99 > Tarasque 97 > Leviathan 94 > Treant 92 > Golem
  88). **Superseded by the square-root gauge (decision 3, second amendment):** the 10–15% target
  applies to *turns*, which grow with `sqrt(Speed)`, so the old 1.14× Speed band (92–105) gave only
  about 1.07× turns. The authored-kits retune widened Speed to 1.25× (1.118× turns) and replaced the
  Speed-stat band test with the turn-ratio test above; the order test is unchanged (Golem strictly
  slowest), so widening or reordering either is a deliberate design change.
- **Move range in a small band (2–5)**, outside the budget. Griffin and Basilisk are the mobile
  ends (5); Golem is the only 2. Thunderbird moved from 4 to 3 in the element chart v2 follow-up
  (as the fastest beast with Move 4 it reached the enemy alone and took its focus) and back to 4
  when Thunder Talons went from range 1 to 2: at range 2 it fires from outside melee and, as a
  Skirmisher, retreats to its reach with the leftover budget (see the tuning log, "Thunderbird range
  vs move").
- **Crit chance in a small band (0–25%)**, also outside the budget and not level-scaled (user-approved
  values; see "Variance and critical hits"). The roster tests pin the ten values and the band; the
  validator only requires a percent (0–100). "Range" in the archetypes means move range, the per-turn hex
  movement budget — skill reach is authored per skill.
- **Telling the defensive beasts apart.** Golem absorbs (the highest HP and Defense, the lowest Speed
  and move range); Tarasque absorbs and hits back (Defense *and* the highest Attack); Leviathan
  is the physically bulky all-rounder (the next-highest Defense after Golem and Tarasque, the
  third-highest HP); Treant's bulk is HP and Special Defense for a support role;
  Frost Wyrm splits its bulk evenly across Defense and Special Defense.
- **Stances follow the archetypes** (decision 8): the artillery-style casters (Phoenix, Kirin,
  Basilisk) are Ranged, the fast strikers (Thunderbird, Griffin) Skirmishers, and the five tanks and
  bruisers hold the line as Vanguards. The roster tests pin all ten.
- **Evolutions and customization are empty.** Every species' `EvolutionOptions` is empty and
  `CustomizationSchema` and `Icon` are unset. Skills are authored in the skill library (see "Beast
  skill kits"), whose importer fills `LearnableSkills` and `DefaultLoadout`; the roster importer
  never touches any of those fields.

### Growth-curve semantics — DECIDED FOR AUTHORED DATA

`CreatureSpeciesSO.GetStatAtLevel` returns `round(BaseStats × curve scale)`, and a curve maps level
progress (0 at level 1, 1 at `MaxLevel`) to a scale. A curve that starts at 0 would make every
level-1 stat 0 (the fresh-asset default still does exactly that), so authored curves obey:

- **scale at max level is exactly 1** — `BaseStats` are the species' *max-level* stats;
- **scale at level 1 is a sensible fraction above 0** (0.10–0.20), so a level-1 beast is a weak but
  real version of its adult self; the roster tests check every stat is at least 1 at level 1
  (crit chance may be 0);
- scale never decreases, and the curve is **piecewise-linear** between its authored points (the
  importer sets linear tangents, so Unity evaluates exactly the numbers in the JSON).

Three curves are defined, all with `MaxLevel` 100. Only `medium` is in use; `fast` and `slow` stay
in the file, unused, as ready-made shapes for the balance simulator to assign:

| Curve | Shape | Key points (progress → scale) | Used by |
| --- | --- | --- | --- |
| `fast` | Front-loaded: strong early, flattens late | 0 → 0.20, 0.25 → 0.60, 0.5 → 0.85, 1 → 1 | — (unused) |
| `medium` | Linear | 0 → 0.15, 1 → 1 | All ten beasts |
| `slow` | Back-loaded: weak early, surges late | 0 → 0.10, 0.5 → 0.40, 0.75 → 0.65, 1 → 1 | — (unused) |

`MoveRange` and `CritChance` are exempt from curves (see "Stat assembly and move range" and
"Variance and critical hits").

### JSON is the source of truth; Unity assets are generated

The roster lives in **`content/data/Creatures/beast-roster.json`**, not in
hand-authored `.asset` files. A plain JSON file is readable and diffable outside Unity — the
headless balance simulator (`Tooling/BalanceSim`) reads it directly with `System.Text.Json` (`IncludeFields = true`;
keys are the C# field names exactly) — whereas `.asset` YAML references its scripts by `.meta` GUIDs
this repo does not track, and cannot be verified without an Editor.

The file holds `GrowthCurves` (`CurveId`, `MaxLevel`, `Keys` of `Progress`/`Scale`) and `Species`
(`SpeciesId`, `DisplayName`, `Description`, `Elements` as enum names, `GrowthCurveId`, `Stance` as a
`CombatStance` name — missing means Vanguard — and `BaseStats` including `MoveRange` and
`CritChance`). Its C# shape is `BeastCraft.Creatures.Roster.BeastRosterData` in the Runtime
assembly, and `BeastRosterValidator` holds the structural rules (well-formed unique snake_case ids,
parseable elements, resolvable curve ids, curve sanity, every stat at least 1 except `CritChance`,
which must be a percent from 0 to 100, and a stance that is a `CombatStance` name if given).

**Workflow:** edit the JSON, then open the project in Unity and run **Beast Craft → Data → Import
Beast Roster**. The importer (`BeastCraft.Editor.Data.BeastRosterImporter`):

- validates first and imports nothing if the file is invalid (all-or-nothing);
- creates or **updates in place** a `GrowthRateCurve` per curve under `Data/Creatures/GrowthRates/`,
  matched by its new `CurveId` field, and a `CreatureSpeciesSO` per species under `Data/Creatures/`,
  matched by `SpeciesId` — searched across the whole project, so a moved or renamed asset is still
  found and never duplicated, and its GUID (and every reference to it) survives;
- owns only the fields the JSON carries (stance included); icon, skills, evolutions and
  customization schema on the asset are left alone;
- never deletes: a species dropped from the JSON keeps its asset and is logged.

The generated assets (and their `.meta` files) are produced on the first Editor run; none are
committed yet. `SpeciesId` and `CurveId` follow the never-rename-after-ship rule; the roster tests pin
the ten approved species ids.

## Skill progression — TUNABLE STARTING DEFAULTS, NOT CONFIRMED BALANCE

**Decided by the user:** a beast equips **3 skills** chosen from a growing pool of skills it has
**acquired**, and the lineup can be changed between battles. Skills **improve slowly** through play,
by two routes together: **practice XP** from using the skill in battle, on a slow curve, and rarer
**materials** that add XP and are required to pass **tier breakthroughs**. The same model is meant
for the avatar's passive skills, so it is built generically (the avatar now uses it: see "Avatar
passives"). The numbers below are
engineering defaults, all constants or authored fields, and cheap to retune.

**Data model.** Everything generic lives in the `BeastCraft.Progression` namespace:

- `SkillProgressionDefinition` (serializable, embedded in `SkillSO.Progression`): `MaxLevel`
  (default 20), `MagnitudeGrowthPerLevel` (percent, default 3) and `Tiers`, a list of
  `SkillTierDefinition` gates in ascending order (defaults: levels 5 / 10 / 15 needing material tiers
  1 / 2 / 3). Each gate has a `ThresholdLevel`, a `RequiredMaterialTier`, and optional bonuses:
  `CooldownReduction` (turns) and `BonusEffects` (extra `SkillEffect`s). It is a separate block
  rather than loose fields on `SkillSO` so any skill-definition type can carry one and reuse the
  same rules.
- `SkillProgress` (serializable save data, like the roster DTOs): `SkillId`, `Level`, `Xp`, `Tier`
  (gates passed).
- `SkillMaterialSO` (asset): `MaterialId` (never rename after ship), `DisplayName`, `Description`,
  `Icon`, `Tier`, `XpValue`.
- `BeastSkillBook` (serializable save data): `Known` (one `SkillProgress` per acquired skill) and
  `Equipped`, `EquipSlotCount = 3` skill ids by slot. **Slot order is fire priority.** Its equip and
  practice rules live on the abstract `SkillBook` base, which the avatar's `AvatarActiveSkillBook`
  and `AvatarPassiveSkillBook` share (see "Avatar passives"); the saved fields are unchanged.
- `SkillProgression` (static rules), `SkillBreakthroughResult` and `SkillEquipResult` (explicit enum
  values, never renumbered).

**The XP curve.** `XpToNextLevel(level) = round(100 × level^1.5)`: 100 XP for level 1→2, 800 for
4→5, 3,162 for 10→11, 8,282 for 19→20. Cumulative, reaching level 5 takes 1,703 XP, level 10
11,106 XP and level 20 67,135 XP. Practice is a flat **10 XP per use**, where a use is a slot that
**fired** (whiffs count, held slots do not), with at most **20 uses credited per award**. Callers
award once per battle, so that is a per-battle cap that stops a long fight with a cooldown-0 skill
being farmed. On practice alone that is 171 uses to level 5, 1,111 to level 10 and 6,714 to level
20. That is deliberately slow, and materials are the accelerator. `SkillProgression.ApplyMaterial` adds
a material's `XpValue`; any tier of material can be fed.

**Gates.** A skill levels while its XP covers the next level, up to its **level cap**: the next
unpassed gate's threshold, or `MaxLevel` once every gate is passed. At a gate the skill stops. XP
keeps banking there but only up to one level's worth (`XpToNextLevel(level)`), and the rest is
discarded. That way practice done while waiting on a material is not wholly lost, but it cannot be
stockpiled. At `MaxLevel` XP is held at 0. `SkillProgression.TryBreakthrough(progress, definition,
material)` passes the gate when the skill is at its threshold and the material's tier is at least
the gate's `RequiredMaterialTier` (a higher tier also works). The tier then goes up by one and the
bank immediately buys the next level if it is full. A breakthrough does not add the material's XP.
On failure (`NoTierRemaining`, `BelowThreshold`, `MaterialTierTooLow`, `MissingInput`) nothing
changes and the caller keeps the material. A gate authored at or past `MaxLevel` blocks nothing but
still grants its bonuses: a "mastery" gate.

**Acquisition and equipping.** `BeastSkillBook.Learn(skill)` acquires a skill at level 1, tier 0
(re-learning never resets progress). `LearnAvailable(species, beastLevel)` learns every
`CreatureSpeciesSO.LearnableSkills` entry at or below the beast's level, which is the level-up
source. Drops and rewards will call `Learn` directly later. `Equip(slot, skillId)` refuses an
out-of-range slot, an unknown skill, or a skill already in another slot (a skill occupies at most one
slot). `Unequip` empties a slot, `SwapSlots` reorders priority, and slots may be empty.

**How a level reaches the battle.** `SkillInstance` (in `BeastCraft.Battle`) is a skill at a level
and tier. It is immutable, clamped to the definition, and captured when the loadout is built.
`SkillLoadout` slots hold instances, `SkillActivation.Instance` carries one, and
`SkillEffectApplier` reads its `Effects` (the authored effects, then each passed gate's
`BonusEffects`) and scales every magnitude by
`1 + MagnitudeGrowthPerLevel / 100 × (level − 1)` before use. That scaled number is the power handed
to `DamageFormula` (so a level-11 skill at the defaults hits for 1.3× the power, and 1.57× at level
20), the heal amount, or the buff/debuff size. Durations are not scaled. A slot's cooldown is the
authored `Cooldown` less every passed gate's `CooldownReduction`, clamped at 0.
`BattleUnitFactory.BuildLoadout(book, skillLookup)` and a `CreateBeast` overload build the loadout
from a skill book. Equipped slots come in slot order at their recorded level and tier, and empty or
unresolvable slots close up. **Level 1, tier 0 is the authored skill exactly**: the plain
`new SkillLoadout(SkillSO[])` path and `new SkillActivation(skill, targets)` still build level-1
instances, and the multiplier is not applied at all at level 1. Existing behaviour, tests and the
simulator (which still fights level-1 kits) are therefore unchanged.

**After a battle.** `BattleSkillUsage.CountFiredSkills(result, avatar)` reads a `BattleResult` into
unit id → skill id → uses. Only fired slots count. The avatar's casts are counted under its id only
when the avatar is passed in, because avatar activations carry no caster. `CountFiredSkillsFor(result,
unitId)` returns one unit's counts, and `BeastSkillBook.AwardPractice(uses, skillLookup)` credits them
to the book's known skills.

**Open questions.** Whether practice should need a hit (or scale with damage dealt) rather than a
fire. Whether enemy-side or defeated beasts earn practice. The material economy (drop rates, how
material XP compares with practice). Whether stat changes should scale per level like damage (they
truncate to whole points, so small buffs grow in steps). Whether the slow curve suits the narrative
pacing. The material economy (drop tables, the inventory and the pacing targets) is now in
"Material economy" below; consuming a material is still the caller's job
(`MaterialInventory.TryConsume`). No UI exists yet; skill books and the material inventory are
saved in `PlayerSave` (see [Progression, saves and the battle session](progression-and-saves.md)).

## Material economy — SIMULATOR-TUNED STARTING VALUES, NOT CONFIRMED BALANCE

The drop side of "Skill progression": where the materials come from, what the player holds, and
how fast a skill climbs as a result. Everything lives in `BeastCraft.Progression`; the numbers are
data (`content/data/Skills/drop-tables.json`) tuned against the balance simulator's pacing model
(`--mode pacing`, `docs/balance/pacing-report.md`), not confirmed balance.

**Drop tables.** `drop-tables.json` (DTOs `DropTableData`, checked by `DropTableValidator`, built by
`DropTableBuilder` into a runtime `DropTable`; the Editor importer, Beast Craft/Data/Import Drop
Tables, copies it into a `DropTableSO`) is keyed by **encounter shape** (`solo`, `elite`, `squad`,
`horde`) × **level band** (contiguous, covering levels 1-100). A cell is a list of entries
`{MaterialId, Chance 1-100, MinQty, MaxQty}`; material ids are the skill library's.

- **Drops only on a clear**, and every entry rolls **independently**: `rng.Next(100) < Chance`, then
  a uniform quantity. `LootRoller.RollClear` always takes both draws per entry, hit or miss, so a
  clear's draw count depends only on the cell (a seeded run is reproducible; retuning one chance
  does not reshuffle every later roll). Seed it per battle with `LootRoller.DeriveSeed(seed,
  battleIndex)` (a SplitMix64 mix, stable across runtimes).
- **Pity**, per (shape, material tier): after `Threshold` consecutive clears of a shape whose cell
  can drop that tier without dropping it, the next clear forces the cell's first entry of that tier
  at its `MinQty`. A drop of the tier (natural or forced) resets the counter; a cell that cannot
  drop the tier leaves it alone. Thresholds: tier 1 after 8, tier 2 after 15, tier 3 after 25.
- **First clear**: the first clear of each (shape, band) grants the band's `FirstClearMaterialId`
  once (the band's headline tier). It does not touch pity.

**Save data.** `MaterialInventory` holds `Materials` (id + quantity), `ClearedCells` (shape + band
`MinLevel`) and `Pity` (shape + tier + misses) as lists of `[Serializable]` entries, because
`JsonUtility` cannot write a dictionary. `TryConsume` is how a caller spends a material after
`SkillProgression.TryBreakthrough` / `ApplyMaterial` succeeds.

**After a battle.** `PostBattleAward.AwardPractice(result, beastBooksByUnitId, skillLookup, avatarBook,
...)` credits every player beast's fired skills (and the avatar's actives and passives) on any
finished battle; `PostBattleAward.AwardDrops(result, table, shape, level, inventory, rng)` rolls the
loot only on `PlayerVictory`.

**Pacing targets** (a dedicated player pushing one signature skill; median battles):

| Skill level | Cumulative XP | Target | Measured p10 / p50 / p90 |
| ---: | ---: | --- | --- |
| 5 (tier-1 gate) | 1,703 | 15-20 | 15 / 16 / 17 |
| 10 (tier-2 gate) | 11,106 | ~80 | 69 / 77 / 85 |
| 15 (tier-3 gate) | 31,998 | ~180 | 157 / 171 / 184 |
| 20 (max) | 67,135 | ~300-320 | 283 / 301 / 302 |

The model (Tooling/BalanceSim/README.md, "Pacing"): encounter level `1 + battle / 5`, shapes drawn
solo 15 / elite 20 / squad 35 / horde 30, 80% of battles cleared, the focus skill firing 3-9 times a
battle (the PvE report's 3-10 beast turns per battle; practice ~60 XP a battle), materials fed to
the focus skill (one kept per tier its later gates need) and the rest spilled to a second skill.

**What the targets force.** The XP constants are unchanged. At ~60 practice XP a battle, practice
alone would take 29 battles to level 5 and ~1,100 to level 20, so reaching level 20 in ~300 battles
means **materials supply about three quarters of the XP** (74% in the measured runs), not a 10-15%
nudge. The level-5 target caps early income: with 20 uses a battle (the per-battle cap) practice
alone reaches level 5 in 9 battles, so the two targets together pin practice at roughly 5-10 uses a
battle. The drop tables are shaped around the gates:

| Band | First clear | Mean material XP per clear | Role |
| --- | --- | ---: | --- |
| 1-3 | shard | 0 | tutorial: the four first-clear shards are the only drops; one opens the level-5 gate |
| 4-20 | shard | 104 | shards; crystals only from a solo boss (5%) and pity |
| 21-40 | crystal | 174 | the four first-clear crystals open the level-10 gate at ~battle 101 |
| 41-60 | core | 139 | the first-clear cores open the level-15 gate at ~battle 201; regular drops thin out |
| 61-80 | core | 512 | crystals and cores; finishes the focus skill and feeds the next ones |
| 81-100 | core | 1,442 | the late game's surplus goes to the rest of the team |

So the gates are paced by the **band a tier first appears in** (the first clears), and the levels
between gates by the band's regular drops. The "~300-320" level-20 median sits at ~301 because the
first band-61 clear's core usually completes it; the spread is mostly the order the shapes are met
in. A 500-battle campaign earns about 106 shards, 120 crystals and 27 cores (about 3.8 level-20
skills' worth of material XP), so the spill-over skill also reaches level 20 by the end: a
playthrough maxes a handful of signature skills.

**Open questions.** Whether first-clear bonuses should be per shape (four per band, as now) or per
band. Whether pity should also count losses. A better campaign schedule (the linear level ramp, the
shape mix and the 80% clear rate are assumptions, not content). How many uses a battle a real
focused skill gets (the simulator's library kits could measure it). No UI, no drop presentation.

## Avatar passives — TUNABLE STARTING DEFAULTS, NOT CONFIRMED BALANCE

**Decided by the user:** the avatar does not fight on the grid. It keeps its active support skills
(decision 6) and gains **3 passive slots**, which are its main role. Passives are acquired and
leveled slowly through play on **the same progression model as beast skills** (practice XP,
materials, tier breakthroughs), and so are the avatar's active skills. The trigger semantics, hook
points, gating order and scopes below are engineering defaults chosen by the lead. The first passive
content (10 passives across every trigger) is in the skill library; see "Beast skill kits".

**Data** (explicit enum values, never renumbered; ids never renamed after ship):

- `PassiveSkillSO` (`Runtime/Avatar`, namespace `BeastCraft.Avatar`): `PassiveId` (stable save key),
  `DisplayName`, `Description`, `Icon`, `Progression` (the shared `SkillProgressionDefinition`
  block), `Trigger`, `HpThresholdPercent` (default 50), `ProcChance` (percent, default 100),
  `MaxTriggersPerBattle` (0 = unlimited), `InternalCooldown` (avatar turns), `TargetScope`,
  `Element` and `Category` (for damage effects, as on `SkillSO`), and `Effects` (a
  `List<SkillEffect>`: the whole effect engine).
- `PassiveTrigger`: `Aura = 0`, `BattleStart = 1`, `EnemyDefeated = 2`, `AllyDefeated = 3`,
  `AllyCrit = 4`, `AllyTurnStart = 5`, `AllyBelowHpPercent = 6`.
- `PassiveTarget`: `AllAllies = 0`, `TriggeringUnit = 1`, `AllEnemies = 2`,
  `LowestHpFractionAlly = 3`.
- `AvatarSkillBook` (`BeastCraft.Progression`, save data): `Actives` (`AvatarActiveSkillBook`,
  `ActiveSlotCount = 3`) and `Passives` (`AvatarPassiveSkillBook`, `PassiveSlotCount = 3`). Both
  are `SkillBook`s, the abstract base `BeastSkillBook` now derives from, so the equip rules are
  written once: only known ids, no duplicates, empty slots allowed, slot order is priority.
- Battle: `PassiveInstance` (a passive at a level and tier, plus its per-battle trigger count,
  cooldown and threshold latches), `PassiveLoadout` (the equipped passives in slot order, and the
  trigger rules), `PassiveActivation` (one firing: passive, slot, trigger, triggering unit, and the
  `SkillActivation` its effects went through). `BattleTurnResult.PassiveActivations` and
  `BattleResult.OpeningPassiveActivations` record every firing.

**One engine.** A passive is applied exactly like a fired skill. Its instance wraps a private
carrier `SkillSO` (sharing the passive's effects, progression, element and category) in a
`SkillInstance` at the passive's level and tier, and `SkillEffectApplier` applies it **with the
avatar as the caster**: damage uses the avatar's attacking stat and crit chance, a shield the
avatar's `Defense`, a heal the avatar's `SpecialAttack`. So every magnitude scales with the passive's level by the same
`1 + MagnitudeGrowthPerLevel / 100 × (level − 1)`, each passed gate's `BonusEffects` are appended,
and chance, resistance, statuses and multi-hit behave as they do for skills. A gate's
`CooldownReduction` means nothing to a passive. Knockback on a passive pushes away from the
avatar's placeholder tile and should not be authored (a content convention, not a runtime check).

**Hook points** (in `BattleTurnExecutor`; nothing runs without an avatar and a non-empty loadout):

1. **Battle start** (`BattleTurnExecutor.BeginBattle`, which `RunBattle` calls before the first
   turn): every `Aura` passive in slot order, then every `BattleStart` passive in slot order. A
   caller driving `ExecuteTurn` itself should call `BeginBattle` once; if it does not, the first
   turn runs it.
2. **Each turn**, after `StatusEffects.BeginTurn` (damage-over-time): the after-damage check. Then,
   on a player beast's turn it survived (stunned or not), every `AllyTurnStart` passive with that
   beast as the triggering unit, **before its skills**.
3. **After every skill a beast fires**: the after-damage check, with that skill's hits.
4. **The avatar's own turn** (`ExecuteAvatarTurn`, whenever its gauge comes up): every passive's
   internal cooldown ticks down once, then the avatar's actives fire, each followed by the
   after-damage check. `AllyTurnStart` never fires on the avatar's turn; it stays per beast turn.

**The after-damage check** handles, in order, each trying its passives in slot order:

- `AllyCrit`: once per critical hit landed by the player beast whose skill it was (the crit-landing
  beast is the triggering unit). The avatar's own crits, active or passive, are not ally crits.
- Defeats: every unit defeated since the last check, in roster order. `AllyDefeated` for a player
  beast (the fallen beast is the triggering unit, so pair it with `AllAllies` or
  `LowestHpFractionAlly`, not `TriggeringUnit`); `EnemyDefeated` for an enemy (the triggering unit is
  the beast whose turn it is when that is a living player beast — it gets the credit — and
  otherwise none, as on an enemy's turn or the avatar's own, where an avatar active's kill credits
  nobody).
- `AllyBelowHpPercent`: every living player beast, in roster order, **strictly below** the passive's
  threshold (`CurrentHp × 100 < HpThresholdPercent × Stats.Hp`, integers). It fires **once per
  crossing**: the passive latches that beast on the attempt (whether or not the attempt fires) and
  re-arms it only when the beast is next seen at or above the threshold. Checking after every
  application, on any side's turn, rather than only at the beast's turn start was chosen so an
  emergency passive can answer the hit that caused the crossing.

Passives therefore react on enemy turns too (an ally falling, an ally dropping low, an enemy dying
to its own damage-over-time), but their cooldowns only tick on the avatar's own turns.

**Gating**, checked in this order each time a passive's trigger happens: not spent
(`MaxTriggersPerBattle`), off its internal cooldown, at least one target in its scope, then the
`ProcChance` roll. A blocked or failed attempt changes nothing (no count, no cooldown). A firing
counts toward the cap and sets the cooldown to `InternalCooldown`; each avatar turn takes one off.
So a passive with cooldown `N` is ready again after the avatar's `N`-th turn following the firing,
however many beast turns (either side's) fall in between: with a Speed-100 avatar, about `N` units
of battle time. `ProcChance` of 0 or below, or above 100, reads as 100, like
`SkillEffect.Chance`.

**No chaining.** A unit defeated by a passive's own effect is recorded silently: it never triggers
`EnemyDefeated` or `AllyDefeated`, and a passive's crits are not `AllyCrit`s. That bounds the
passives any one event can set off. (HP thresholds are state, not events, so a crossing caused by a
passive is seen at the next check.)

**Target scopes** are all position-free and draw-free: `AllAllies` and `AllEnemies` are the living
units of that side in id order (the resolver's global shapes); `LowestHpFractionAlly` is the living
player beast with the lowest `CurrentHp / Stats.Hp`, compared exactly by cross-multiplication with
the id tie-break (the resolver's `HpFraction` pick); `TriggeringUnit` is the trigger's unit when it
is alive. A passive with no one to land on does not fire.

**"Lasts the battle."** An `Aura` is applied once, at battle start, before anything else. Its stat
changes should be authored with `DurationTurns` 0: the effect engine applies such a change to the
stat block permanently and never reverts it, which is how an aura lasts the whole battle (a
"+5% crit to all allies" aura is `BuffStat` `CritChance` 5, duration 0, on `AllAllies`). A
`BattleStart` passive is the same one-shot but meant for timed effects (an opening shield, a
two-turn buff), which keep their authored durations.

**The rng.** A passive adds exactly one draw, its `ProcChance` roll, and only when the chance is
below 100 and every other check has passed; its effects then draw as any skill's would (crit and
variance per damage hit, a chance roll per uncertain non-damage effect). Draws happen at the hook
point, in the order above. With no passives nothing draws that did not before: existing battles, the
EditMode suite and the simulator's committed report are unchanged.

**Practice XP.** `BattleSkillUsage.CountPassiveTriggers(result)` counts firings per `PassiveId`
(opening firings included) and `CountAvatarActiveUses(result)` counts the avatar's active casts per
`SkillId`. `AvatarSkillBook.AwardPractice(activeUses, activeLookup, passiveTriggers, passiveLookup)`
credits both books on the ordinary rules: 10 XP per use, at most 20 uses per award. A passive's use
is a time it fired; blocked triggers and failed proc rolls are not uses.

**Simulator.** `--avatar library|support|none` (default `library`, the authored default avatar, see
"Beast skill kits"; the committed report uses it since the authored-kits retune).
`support` is a fixture, not content: a +5 crit aura, a two-turn Defense shield on a beast that
drops below 40% (cooldown 2), and a 50% chance of a two-turn +10% Attack surge for the team on each
enemy defeat. Either preset's avatar has a fixture stat block: 100 in every combat stat at max
level, scaled by the roster's `medium` growth curve like a beast's (15 at level 1, 57 at level 50),
so its shields and heals are the same share of a beast's HP at every level. (Until heals scaled it
was `10 + level`, which made level-1 avatar heals and shields relatively weak.) The report then gains an
"Avatar passives" section with firings per battle. The simulator's own loop calls `BeginBattle` and
passes the passives to every turn, so `--self-check` still compares it against `RunBattle`.
**Avatar gauge in the simulator:** the fixture block also carries Speed 100 at max level on the
same curve (`AvatarStatsSO.GetStatsAtLevel`, so 15 at level 1), the avatar joins the `TurnManager`
beside the beasts (never the targeting roster), and the simulator's loop runs `ExecuteAvatarTurn`
when its gauge comes up, mirroring `RunBattle` (the self-check compares them). `--avatar-level <n>`
fixes the avatar's level (stats and damage level); by default it is each battle's encounter level.
The "Avatar passives" section also reports the avatar's turns and active casts per battle.

**Open questions.** Whether passive-caused defeats should chain (currently never). Whether the
avatar's own crits should count as `AllyCrit`. Whether `EnemyDefeated` should credit the unit that
dealt the blow rather than the unit whose turn it is (they differ for damage-over-time and avatar
kills). Whether a failed proc roll should consume the threshold crossing. (Internal cooldowns now
run on the avatar's own clock, its gauge; decided with stage 3b.)
How passives are acquired (drops, quests, avatar milestones) and the passive material economy. No
passive UI exists yet; the avatar's skill book (actives and passives) is saved in `PlayerSave` (see
[Progression, saves and the battle session](progression-and-saves.md)).

## Team bonds — TUNABLE STARTING DEFAULTS, NOT CONFIRMED BALANCE

**Why.** The simulator's team-composition analysis (`docs/balance/tuning-log.md`, "Team
composition analysis") found that lineups matter per encounter but mostly additively: a team was
roughly the sum of its beasts. **Team bonds** make composition matter on purpose. The first bonds
were stat buffs applied at battle start; they are now **behaviour bonds**: one of the bond's members
*does something* in battle when its trigger happens — steps in front of a hit, returns fire,
cleanses a stunned ally (lead design, "Behaviour bonds and tiered difficulty" in the tuning log;
the user approved all nine bonds below, including `combined_arms`, and enemy statuses so `twilight`
has something to cleanse). Mechanism and content are the lead's design; the magnitudes are
simulator-tuned.

**Data** (explicit enum values, never renumbered; ids never renamed after ship). Authored in the
`TeamBonds` array of `content/data/Skills/skill-library.json` (`SkillLibraryData.CurrentSchemaVersion` 3;
DTOs `TeamBondData` / `TeamBondTierData` / `BondReactionData`, checked by `SkillLibraryValidator`,
mapped by `SkillLibraryBuilder.ApplyTeamBond`, imported as ScriptableObjects by the (retired) Unity
skill library importer, loaded the same way by the simulator):

- `TeamBondSO` (`Runtime/Bonds`, namespace `BeastCraft.Bonds`): `BondId`, `DisplayName`,
  `Description`, `Icon`, `Condition`, the condition's set (`Stance`, `Elements` or `SpeciesIds`),
  `Scope`, `Tiers` (`TeamBondTier`: `MinCount`, a battle-start `Effects` list of ordinary
  `SkillEffect`s, and a `Reaction`), and for a scaling bond `PerCount` and `MaxCount` (below).
- `TeamBondCondition`: `Stance = 0` (count = team members of that stance), `Elements = 1` (count =
  distinct elements of the set the team covers, capped at the number of members carrying one, so a
  pair bond needs both halves on two beasts), `Species = 2` (count = distinct listed species
  fielded), `DistinctStances = 3` (count = distinct stances fielded, 1-3; every beast is a member).
  A bond's **members** are the beasts that match.
- `TeamBondScope` (for the battle-start effects): `Members = 0`, `Team = 1`, `Others = 2` (every
  beast that is **not** a member).
- Tiers rise strictly by `MinCount`; the **highest tier reached applies, and tiers replace rather
  than stack**, so each tier is authored in full (its effects and its reaction). A tier needs
  battle-start effects, a reaction, or both.
- **Battle-start effects** land on the player's own team, so the validator allows only `BuffStat`
  (not `HP`) and a `Shield` status, always landing (`Chance` 100); `DurationTurns` 0 = the whole
  battle. **Scaling bonds** (`PerCount`: one tier, `Magnitude` x min(count, `MaxCount`) stacks, the
  worst case capped) are still supported but none is authored, and a scaling bond has no reaction.
- **`BondReaction`** (`TeamBondTier.Reaction`; inert at `Trigger` `None`, so a zero-filled one does
  nothing):
  - `Trigger` (`BondTrigger`): `EnemyTargetsAlly = 1` (an enemy `SingleTarget` skill has resolved
    to exactly one of the team's beasts, before it lands), `AllyCrit = 2` (a team beast crit on its
    own turn; the reactor is never the critter), `AllyHitByEnemy = 3` (an enemy `SingleTarget` skill
    hit a team beast; never the beast hit), `MemberHit = 4` / `MemberCrit = 5` (a member's own hit
    or crit on an enemy; the reactor is that member), `AllyBelowHpPercent = 6` (a beast is now
    strictly below `HpThresholdPercent`: one attempt per crossing, re-armed at or above it — the
    passive rule), `AllyTurnStartAfflicted = 7` (a beast is about to begin its turn stunned or
    carrying damage-over-time), `AllyDefeated = 8`.
  - `Action`: `Apply = 0` (cast `Effects` on `Target`) or `Intercept = 1` (`EnemyTargetsAlly` only:
    the reactor's `Effects` land on itself, then the enemy skill's target list is swapped for it; no
    range recheck; area skills are never intercepted).
  - `Target` (`BondReactionTarget`): `Self`, `TriggeringAlly`, `TriggerTarget` (the enemy hit or
    crit), `Attacker`, `EnemiesNearReactor` (living enemies within `Range`), `Team`.
  - `TriggerFilter` (`Any` / `Members` / `NonMembers`, on the triggering ally), `ReactorOrder`
    (`TeamOrder` / `HealthiestFirst`), `Chance`, `Cooldown` (the reactor's own turns),
    `MaxPerMember`, `MaxPerTriggerUnit`, `MaxPerBattle`, `Range` (reactor to the unit it acts on,
    nearest tile to nearest tile; 0 = unlimited), `HpThresholdPercent`, `Effects`.
  - Validator: `Chance` 1-100; a hit or crit trigger has `Cooldown` >= 1 (one per the reactor's
    turn, whatever the hit count); `Intercept` goes with `EnemyTargetsAlly` and only with it; the
    target must exist for the trigger; hostile effects land on enemies and friendly ones on the team;
    damage power at most 60; a stun lasts exactly 1 turn and lands at most 50% of the time (reaction
    chance x effect chance); no knockback.
- `SkillEffectType.Cleanse = 5`: removes every `Stun` and `DamageOverTime` the target carries
  (`StatusEffects.Cleanse`); shields and taunts stay. `Magnitude` is not read.

**Runtime.** `TeamBondResolver.Resolve(bonds, members)` is pure: given `TeamBondMember`s it returns
the `ActiveTeamBond`s in the bonds' order with tier, count, stacks and member indices.
`TeamBondLoadout` binds that to the team's battle units for one battle (`For(bonds, members,
team)`). `BattleTurnExecutor.BeginBattle(…, passives, bonds, out bondActivations)` /
`RunBattle(…, passives, bonds, maxTime)` apply the battle-start effects **once, before the avatar's
auras and battle-start passives** (each recipient applies them to itself through a private carrier;
`BattleResult.BondActivations` records them), and `RunBattle` — so `BattleSession` — then passes the
bonds into **every turn**: `ExecuteTurn(…, passives, bonds)` and `ExecuteAvatarTurn(…, passives,
bonds)` (the old overloads remain and mean "no bonds"). The executor's hook bundle
(`BattleHooks`: the avatar's passives and/or the reacting bonds) runs:

- as a team beast's turn opens, after its modifiers tick and **before its statuses tick**: its
  reaction cooldowns count down, then `AllyTurnStartAfflicted` (a stun cleansed here lets the beast
  act this turn; a cleansed burn deals no damage);
- **between resolving a skill's targets and applying it**: the `Intercept` check;
- after every application, **after the passive hook**: per damage hit in hit order the hit and crit
  triggers (or `AllyHitByEnemy` once per team beast an enemy single-target skill hit), then
  `AllyDefeated` for every newly fallen beast (team order), then the HP thresholds (team order); the
  last two also after damage-over-time opens a turn and after each avatar activation.

Within one event the bonds are tried in library order; **one reaction per bond per event**: the
first member (in `ReactorOrder`) that is alive, not stunned, has someone in range to act on, is off
cooldown and under its caps is chosen, and only then is `Chance` rolled — **at most one draw**, none
at 100. Effects are cast by the reactor through `SkillEffectApplier` with a carrier `SkillSO` cached
per (reaction, element, category): the reactor's first element and its stronger attacking category
(physical on a tie). **No chaining**: a reaction's hits, crits and defeats trigger nothing — no
bond, and no avatar passive (`PassiveLoadout.SyncDefeatedSilently` notes its kills); an intercepted
hit is still the enemy's own activation. The defeated are lifted off the grid after each reaction.
Every firing is recorded on `BattleTurnResult.BondReactions` (`BondReactionRecord`: bond, trigger,
reactor, triggering unit, activation, and for an intercept the beast it was aimed at). A loadout
with no reacting tier (or not yet applied) creates no hook, so such a battle plays every turn and
every random draw exactly as the bond-free one. **Enemies never get bonds** (for now).

**Enemy statuses.** So that `twilight` has something to cleanse, three enemy skills now apply
statuses (`enemy-library.json`): the giant's Quake stuns (20%, 1 turn, radius 1), the caster's Bolt
burns (damage-over-time 15, 30%, 2 turns, 2 stacks), the stingling's Sting poisons (10, 25%, 2 turns).

**Content** (every beast is in exactly one stance bond and one element bond, plus `combined_arms`):

| Bond | Condition | Reaction (tuned) |
| --- | --- | --- |
| `guardian` Guardian | Vanguard 2+ / 3+ | an enemy single-target attack aimed at a **non-Vanguard** within 2 hexes: the healthiest Vanguard intercepts it, 60% / 80%, cd 1, behind a Shield of 40% of its Defense (1 turn) |
| `pack_hunters` Pack Hunters | Skirmisher 2 | +3 CritChance at battle start; when another ally crits, a Skirmisher within 4 hexes strikes the same target (power 25), cd 1 |
| `crossfire` Crossfire | Ranged 2+ / 3+ | an enemy single-target hit on a **Ranged** member: another Ranged member within 3 hexes of the attacker returns fire (power 50), 45% / 60%, cd 1 |
| `wildfire` Wildfire | Fire + Air | a member's damage hit: 30% Burn (damage-over-time 20, 2 turns, 2 stacks; resisted), cd 1 |
| `storm_front` Storm Front | Lightning + Water | a member's crit: 30% Stun 1 turn (resisted), cd 2 |
| `bedrock` Bedrock | Earth + Metal | an ally below 60% HP: the healthiest member Taunts every enemy within 3 hexes for 3 turns; once per ally per battle |
| `winter_grove` Winter Grove | Ice + Nature | an ally below 35% HP: a member shields it (50% of the member's Defense, 2 turns) and heals it 15; once per ally per battle |
| `twilight` Twilight | Light + Dark | an ally starting its turn stunned or burning: a member cleanses it and heals it 15, so it acts; 3 per member per battle |
| `combined_arms` Combined Arms | 3 distinct stances | an ally defeated: the whole team +10% Attack and SpecialAttack for 3 turns; twice per battle |

The lead's draft values (guardian 40% / 60% within 1 hex, pack_hunters +6 crit and power 45,
crossfire 40% / 55% at power 40, storm_front 40%, bedrock below 50% within 2 hexes for 2 turns,
winter_grove Shield 60 + Heal 20, twilight cleanse only twice, combined_arms +15%) were retuned
against the balance guard; the guardian's range went from 1 to 2 because adjacent intercepts fired
0.2 times a battle. See the tuning log.

**Simulator.** `--bonds on|off` (default on, library kit only). "PvE team bonds" lists each bond
(tiers, reactions), how often it is active, the beasts' memberships, each bond's marginal per shape
(**Δ** and **excess** over the additive prediction) and **reactions per battle**; the composition
panel (`--panel`) adds each bond's panel excess and reactions. The bond-aware scouted picker weighs
each bond per tier by its panel excess (`ScoutedPicker.BondWeights`), and a bond that answers only
afflicted allies weighs nothing against an encounter that cannot stun or burn. The simulator's loop
passes the bonds into every turn, so `--self-check` still compares it against `RunBattle`.

**Open questions.** Whether enemies (bosses, packs) should get bonds of their own. How reactions are
shown to the player (the records carry everything a battle log needs). Species bonds are supported
but none is authored. Whether bonds should level or be unlocked through progression. Whether the
guardian's reach should stay at 2 hexes (the draft said adjacent).

## Beast skill kits — SIMULATOR-TUNED CONTENT, NOT CONFIRMED BALANCE

Every authored skill lives in **`content/data/Skills/skill-library.json`**, the
same JSON-as-source-of-truth pattern as the roster (see "JSON is the source of truth" above): 60 beast
skills (six per beast), 6 avatar actives, 10 avatar passives, 3 skill materials, and per species its
`LearnableSkills` (level → skill id) and `DefaultLoadout` (3 skill ids in slot, i.e. fire-priority,
order). The numbers started as a first draft that followed the budget rule below, and were then
**tuned against the balance simulator together with the roster** (the authored-kits retune; see
[`docs/balance/tuning-log.md`](../balance/tuning-log.md), "Retune with authored kits, avatar
passives, sqrt speed and mitigation", which lists every changed number). They are still not
confirmed balance.

**Data and tooling.** The C# shape is `BeastCraft.Skills.SkillLibraryData` (Runtime; enums written as
member names; a missing `TargetingCriterion` means `Distance` — the nearest unit — not `SkillSO`'s
`Random`, so authored skills never spend the rng on targeting, and the validator rejects `Random`).
`SkillLibraryBuilder` is the one DTO → `SkillSO` / `PassiveSkillSO` / `SkillMaterialSO` mapping,
shared by the Editor importer and the simulator. `SkillLibraryValidator` holds the structural rules:
ids unique across the whole file and snake_case; every reference resolves; every enum parses;
numbers in sane bands (power 0–400, chance 1–100, cooldown 0–10, hits 1–8, stacks 1–10, execute
0–200, knockback 1–4 whole hexes, a timed status needs a duration, damage and heals are instant);
avatar actives use only `Self` / `AllAllies` / `AllEnemies` and never knockback; tier gates rise
and name an existing material tier; cooldown reductions never take a cooldown below 1; every
species learns at least 5 skills, its 3 defaults are learnable by level 5, and at least one default is
an enemy-side `SingleTarget` or `Line` skill (the only kind that walks a beast forward — a kit
without one never moves). Given the roster it also checks that kits and species match one to one and
that a **Ranged** beast's defaults hold no enemy-side positional skill of range 1 (it would never walk
in to use it). The content guidelines — five or six skills per beast, each beast's signature
mechanics, the tier pattern, the power budget — are EditMode tests (`SkillLibraryTests`), not
validator rules, so the balance pass can move them.

**Workflow:** edit the JSON, then in Unity run **Beast Craft → Data → Import Beast Roster** and then
**Beast Craft → Data → Import Skill Library**. The importer
(`BeastCraft.Editor.Data.SkillLibraryImporter`) validates first (all-or-nothing), creates or updates
in place a `SkillSO` per beast skill (`Data/Skills/Beast/`) and avatar active
(`Data/Skills/AvatarActive/`), a `PassiveSkillSO` per passive (`Data/Skills/AvatarPassive/`) and a
`SkillMaterialSO` per material (`Data/Skills/Materials/`), matched by id anywhere in the project
(GUIDs survive), and replaces each species asset's `LearnableSkills` and new `DefaultLoadout` list.
It never touches an asset's `Icon` and never deletes. No generated assets are committed yet.

**Honest mechanics.** Every effect uses only what the engine has. There is no revive, cleanse,
evasion, pull or counter-attack mechanic, so the kits say so: Phoenix's "Rebirth Flame" is a
once-per-battle self heal and large shield; Kirin's "Purifying Ward" is a team shield and Special
Defense boost; Griffin's "evasion" is a Defense / Special Defense / MoveRange self-buff; Leviathan's
"Undertow" pull is a taunt plus slow; Tarasque's "counter" stance is a Defense / Attack fortify.
Freeze and root are `Stun`, Burn and Poison are `DamageOverTime`.

**Heals are a percent of the caster's `SpecialAttack`** (× `HealScale`, 1.0; see "Effect
application"), as shields are a percent of its `Defense`. Until the authored-kits retune they were
flat HP, sized for level 50, so a heal restored 50–90% of a beast's HP at level 1 and 7–14% at
level 100. When heals started scaling, every heal magnitude was rescaled so that it restores about
what it did at level 50 for its caster (`new = old × 100 / caster's level-50 SpecialAttack`), and
then retuned with the rest of the kits. Because `SpecialAttack` and HP grow on the same curve, a
beast's heal on itself now restores the same share of its HP at level 1, 50 and 100, up to
whole-HP rounding at level 1 (the test `Library_BeastHealsRestoreAboutTheSameShareOfHpAtEveryLevel`
holds the drift within 5 points; the tuning log has the table).

### The power budget (first draft)

Damage per cooldown turn, **DPT = Σ(power × hits × (1 + execute / 200)) + Σ(DoT power × turns × 0.5 ×
chance), × shape factor, ÷ max(1, cooldown)**. Shape factor: `SingleTarget` 1.0, `Line` 1.3, `Cross`
1.5, `AreaBurst` 1.5 at radius 1 and 2.0 at radius 2+, `AllEnemies` 2.5 (rough expected targets).
The execute term assumes the target is on average half dead; DoT counts half because it is delayed
and can be outlived.

- **Budget per slot:** a pure damage skill aims at **DPT ≈ 90 at range 1** and **≈ 70 at range 2+**
  — exactly the standard kit's Strike / Blast parity, so a library kit and the standard kit start in
  the same place.
- **Role scaling:** tanks and supports ≈ 0.8× (low damage by design), glass cannons and burst
  strikers ≈ 1.1×.
- **Utility trades damage:** a damage skill with a rider (a debuff, a knockback, a low-chance stun,
  a DoT) sits around 0.75× its budget; one with hard control (stun ≥ 30%) or a taunt around 0.3–0.5×;
  pure utility (heal, shield, buff, taunt) carries no damage.
- **Limited-use openers** (`MaxUsesPerBattle` > 0, often `InitialCooldown` 0) may exceed the per-turn
  budget per use (Storm Dive is 120 power once), since they cannot repeat.
- **Tiers stay modest:** gates at levels 5 / 10 / 15 needing material tiers 1 / 2 / 3; the level-10
  gate adds a small bonus effect and the level-15 gate either takes 1 off a cooldown of 3 or more or
  adds another small effect (never −1 on a cooldown-2 damage skill, which would double it). Levels
  add 3% magnitude each (1.57× at level 20), per the progression defaults.

The test `Library_UnlimitedDamageSkillsStayWithinTheFirstDraftPowerBudget` holds every unlimited
damage skill at or below 1.2× its budget. The "Dmg/turn" column below is this DPT.

**After the retune** (and the element chart v2 follow-up) the rule's numbers are unchanged, and
every unlimited damage skill still sits at or below 1.2× its budget. These defaults sit above their
role-scaled guide, deliberately:

- **Golem's Boulder Slam** is 90 (1.0× the melee budget, above a tank's ≈ 0.8×; 85 before chart
  v2). The slowest beast with Move 2 lands it less often than any other melee skill, and at 75 the
  Golem was bottom three in every shape.
- **Leviathan's Serpent Bite** is 94 (1.04×, above a tank's ≈ 0.8×; 82 before chart v2, 86 before
  the milestone-2 retune, which lifted it to give the Leviathan a top-3 shape back).
- **Thunderbird's Thunder Talons** is 25 × 3 = 75 at range 2 (1.07×) and targets the enemy with the
  **least HP left** in reach (milestone 2: with the multi-hex bosses in front, nearest-first poured
  85% of its `elite` damage into the boss while the escort kept firing; 26 × 3 before), and **Chain Lightning** 22 × 3 at radius 2, cooldown 2 = 66 (0.94×). Both were 28 × 3 =
  84 (the 1.2× ceiling) until the niche pass, which put the once-per-battle **Storm Dive** (120,
  was 230) in the default loadout in place of Static Charge; with a real third slot the ceiling
  numbers made the Thunderbird the strongest `neutral` beast, so both came down. Talons was
  36 × 3 = 108 at range 1 until "Thunderbird range vs move". See the tuning log, "Niche pass".
- **Tarasque's Iron Crush** is 160 on cooldown 2 (80, 0.89× the melee budget; 148 before the
  niche pass), **Basilisk's Coup de Grace** 115 with a 60% execute (75, 1.07×; 105 before), and
  **Kirin's Radiant Bolt** 70 (1.0×, above a support's ≈ 0.8×; 62 before the niche pass, 66 before
  milestone 2): small lifts, each well inside the ceiling.
- **Griffin's Wind Lance** is 122 on a line, cooldown 2 (79, 1.13×; 110 before milestone 2), **Gale
  Talon** 95 (1.06×; 89) and **Gust** 75 (0.54×; 55): the milestone-2 retune lifted the Griffin back
  inside the balance guard under the scouted calibration.

Two utility numbers moved past the first-draft guideline, both paid for in damage: Basilisk's
Petrifying Gaze stuns at 45% (hard control on 45 power at cooldown 3, 0.21× the ranged budget), and
Golem's Granite Bulwark shields every ally within 2 hexes for 85% of the Golem's Defense every 3
turns (70% before element chart v2; the shield carries the tank's value, see the tuning log).

### Kits

**Default loadouts** follow the role: tanks = damage (the approach skill, first so the others fire
from the tile it walked to) + taunt + mitigation; supports = heal + buff/shield + damage; damage
dealers = two damage skills + one utility (the Thunderbird's third slot is its once-per-battle
Storm Dive opener instead, since the niche pass). Learn levels spread from 1 to 60; every default is
learnable by level 5.

#### Phoenix — Fire, Ranged

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Ember Shot `ember_shot` | 1 | slot 1 | SingleTarget r3 | Special | 1 | Damage 60; DoT 14 3t, stacks x3 | 81 | L10: adds -5% SpecialDefense 2t; L15: adds Damage 15 |
| Flame Wave `flame_wave` | 1 | slot 2 | Line r4 | Special | 2 | Damage 90; DoT 10 2t (50%) | 62 | L10: adds -8% SpecialDefense 2t; L15: adds DoT 10 2t |
| Rebirth Flame `rebirth_flame` | 4 | slot 3 | Self | - | 4 (1/battle) | Heal 44; Shield 80% Def 3t | - | L10: adds +15% SpecialAttack 3t; L15: adds +10% Speed 3t |
| Blaze Bolt `blaze_bolt` | 12 |  | SingleTarget r4 | Special | 2 | Damage 150 | 75 | L10: adds DoT 15 2t; L15: adds -10% SpecialDefense 2t |
| Firestorm `firestorm` | 30 |  | AllEnemies | Special | 4 | Damage 40; DoT 12 2t, stacks x3 | 32 | L10: adds -8% SpecialDefense 2t; L15: -1 cd |
| Sunfire Nova `sunfire_nova` | 55 |  | Cross r3 | Special | 3 | Damage 110; DoT 15 3t | 66 | L10: adds -10% SpecialDefense 2t; L15: -1 cd |

#### Leviathan — Water, Vanguard

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Serpent Bite `serpent_bite` | 1 | slot 1 | SingleTarget r1 | Physical | 1 | Damage 94 | 94 | L10: adds -8% Attack 2t; L15: adds DoT 10 2t |
| Undertow `undertow` | 1 | slot 2 | AreaBurst r3 | - | 3 | Taunt 2t (85%); -10% Speed 2t | - | L10: adds -10% SpecialAttack 2t; L15: -1 cd |
| Deep Shell `deep_shell` | 3 | slot 3 | Self | - | 3 | Shield 65% Def 3t; Heal 24 | - | L10: adds +15% SpecialDefense 3t; L15: -1 cd |
| Tidal Wave `tidal_wave` | 8 |  | Line r3 | Special | 2 | Damage 90; Knockback 1 hex (50%) | 58 | L10: adds -10% Speed 2t; L15: adds -8% SpecialDefense 2t |
| Maelstrom `maelstrom` | 30 |  | AreaBurst r2 | Special | 3 | Damage 70; -15% SpecialDefense 2t | 47 | L10: adds DoT 10 2t; L15: -1 cd |
| Tidal Renewal `tidal_renewal` | 50 |  | AreaBurst (ally) r2 | - | 4 | Heal 29; +10% Defense 2t | - | L10: adds Shield 20% Def 2t; L15: -1 cd |

#### Golem — Earth, Vanguard

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Boulder Slam `boulder_slam` | 1 | slot 1 | SingleTarget r1 | Physical | 1 | Damage 90 | 90 | L10: adds -10% Speed 2t (50%); L15: adds Knockback 1 hex |
| Stone Challenge `stone_challenge` | 1 | slot 2 | AreaBurst r3 | - | 3 | Taunt 3t (90%) | - | L10: adds -10% Attack 2t; L15: -1 cd |
| Granite Bulwark `granite_bulwark` | 3 | slot 3 | AreaBurst (ally) r2 | - | 3 | Shield 85% Def 2t | - | L10: adds +10% SpecialDefense 2t; L15: -1 cd |
| Tectonic Shove `tectonic_shove` | 12 |  | SingleTarget r1 | Physical | 2 | Damage 70; Knockback 2 hex | 35 | L10: adds -10% Defense 2t; L15: adds Stun 1t (10%) |
| Quake `quake` | 25 |  | AreaBurst r2 | Physical | 3 | Damage 80; -15% Speed 2t (40%) | 53 | L10: adds Stun 1t (10%); L15: -1 cd |
| Stoneskin `stoneskin` | 40 |  | Self | - | 4 | +25% Defense 3t; +25% SpecialDefense 3t | - | L10: adds Shield 30% Def 2t; L15: -1 cd |

#### Griffin — Air, Skirmisher

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Gale Talon `gale_talon` | 1 | slot 2 | SingleTarget r1 | Physical | 1 | Damage 95 | 95 | L10: adds -8% Defense 2t; L15: adds Damage 20 |
| Wind Lance `wind_lance` | 1 | slot 1 | Line r3 | Physical | 2 | Damage 122 | 79 | L10: adds Knockback 1 hex; L15: adds -10% Speed 2t |
| Gust `gust` | 3 | slot 3 | AreaBurst r1 | Physical | 3 | Damage 75; Knockback 2 hex | 38 | L10: adds -10% Speed 2t; L15: -1 cd |
| Tailwind `tailwind` | 12 |  | Self | - | 4 | +20% Defense 2t; +20% SpecialDefense 2t; +1 MoveRange 2t | - | L10: adds +10% Speed 2t; L15: -1 cd |
| Updraft `updraft` | 28 |  | AllAllies | - | 5 | +10% Speed 2t; +1 MoveRange 2t | - | L10: adds +5% Attack 2t; L15: -1 cd |
| Sky Rend `sky_rend` | 50 |  | SingleTarget r1 | Physical | 2 | Damage 175; -10% Defense 2t | 88 | L10: adds DoT 12 2t; L15: adds Stun 1t (10%) |

#### Thunderbird — Lightning, Skirmisher

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Thunder Talons `thunder_talons` | 1 | slot 1 | SingleTarget r2, lowest HP | Physical | 1 | Damage 25 x3 hits | 75 | L10: adds -5% Defense 2t, stacks x3; L15: adds Damage 25 |
| Chain Lightning `chain_lightning` | 1 | slot 2 | AreaBurst r2 | Special | 2 | Damage 22 x3 hits | 66 | L10: adds Stun 1t (10%); L15: adds -8% SpecialDefense 2t |
| Static Charge `static_charge` | 3 |  | Self | - | 4 | +25 CritChance 3t; +10% Speed 3t | - | L10: adds +10% Attack 3t; L15: -1 cd |
| Storm Dive `storm_dive` | 5 | slot 3 | SingleTarget r3 | Physical | 4 (first turn, 1/battle) | Damage 120 | 30 | L10: adds Stun 1t (25%); L15: adds -15% Defense 2t |
| Thunderclap `thunderclap` | 25 |  | AreaBurst r1 | Special | 3 | Damage 70; Stun 1t (20%) | 35 | L10: adds -10% Speed 2t; L15: -1 cd |
| Plasma Barrage `plasma_barrage` | 50 |  | Line r4 | Special | 2 | Damage 26 x4 hits | 68 | L10: adds -8% Defense 2t; L15: adds -8% SpecialDefense 2t |

#### Frost Wyrm — Ice, Vanguard

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Rime Bolt `rime_bolt` | 1 | slot 1 | SingleTarget r2 | Special | 1 | Damage 52; -8% Speed 3t, stacks x3 | 52 | L10: adds -5% SpecialDefense 2t; L15: adds Stun 1t (10%) |
| Deep Freeze `deep_freeze` | 1 | slot 2 | SingleTarget r2 | Special | 3 | Damage 60; Stun 1t (35%) | 20 | L10: adds -10% Speed 2t; L15: -1 cd |
| Frost Breath `frost_breath` | 3 | slot 3 | AreaBurst r2 | Special | 2 | Damage 46; -10% Speed 2t (50%), stacks x3 | 46 | L10: adds Stun 1t (10%); L15: adds -8% SpecialDefense 2t |
| Blizzard `blizzard` | 18 |  | Cross r3 | Special | 3 | Damage 90; -10% Speed 2t | 45 | L10: adds Stun 1t (15%); L15: -1 cd |
| Ice Armor `ice_armor` | 35 |  | Self | - | 4 | Shield 45% Def 3t; +15% SpecialDefense 3t | - | L10: adds +10% Defense 3t; L15: -1 cd |
| Absolute Zero `absolute_zero` | 60 |  | AllEnemies | Special | 5 (1/battle) | Damage 50; Stun 1t (20%) | 25 | L10: adds -15% Speed 2t; L15: adds -10% SpecialDefense 2t |

#### Treant — Nature, Vanguard

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Thorn Lash `thorn_lash` | 1 | slot 1 | SingleTarget r2 | Special | 1 | Damage 40; DoT 10 3t, stacks x3 | 55 | L10: adds -8% Speed 2t (50%); L15: adds -8% SpecialDefense 2t |
| Verdant Mend `verdant_mend` | 1 | slot 2 | SingleTarget (ally) r3, lowest HP% | - | 2 | Heal 40 | - | L10: adds Shield 20% Def 2t; L15: adds +10% Defense 2t |
| Bark Ward `bark_ward` | 3 | slot 3 | AreaBurst (ally) r2 | - | 3 | Shield 30% Def 2t | - | L10: adds Heal 11; L15: -1 cd |
| Entangling Roots `entangling_roots` | 8 |  | SingleTarget r2 | Special | 3 | Damage 50; Stun 1t (25%) | 17 | L10: adds -15% Speed 2t; L15: -1 cd |
| Spore Cloud `spore_cloud` | 20 |  | AreaBurst r2 | Special | 3 | DoT 25 3t; -10% Attack 2t | 25 | L10: adds -10% SpecialAttack 2t; L15: -1 cd |
| Lifebloom `lifebloom` | 45 |  | AllAllies | - | 4 | Heal 19; +10% Defense 2t | - | L10: adds +10% SpecialDefense 2t; L15: -1 cd |

#### Tarasque — Metal, Vanguard

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Sunder `sunder` | 1 | slot 1 | SingleTarget r1 | Physical | 1 | Damage 65; -12% Defense 3t, stacks x3 | 65 | L10: adds -8% SpecialDefense 3t; L15: adds DoT 10 2t |
| Iron Crush `iron_crush` | 1 | slot 2 | SingleTarget r1 | Physical | 2 | Damage 160 | 80 | L10: adds Stun 1t (15%); L15: adds -10% Defense 2t |
| Iron Fortress `iron_fortress` | 4 | slot 3 | Self | - | 4 | +30% Defense 3t; +15% Attack 3t | - | L10: adds Shield 30% Def 2t; L15: -1 cd |
| Spiked Carapace `spiked_carapace` | 15 |  | Self | - | 3 | Shield 40% Def 2t; +20% SpecialDefense 2t | - | L10: adds +10% Attack 2t; L15: -1 cd |
| Shrapnel Burst `shrapnel_burst` | 30 |  | AreaBurst r1 | Physical | 2 | Damage 85; -8% Defense 2t (50%) | 64 | L10: adds DoT 10 2t; L15: adds -10% Speed 2t |
| Juggernaut Charge `juggernaut_charge` | 55 |  | Line r2 | Physical | 3 | Damage 150; Knockback 1 hex | 65 | L10: adds Stun 1t (20%); L15: -1 cd |

#### Kirin — Light, Ranged

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Sacred Spring `sacred_spring` | 1 | slot 1 | AllAllies | - | 3 | Heal 20 | - | L10: adds +8% SpecialDefense 2t; L15: -1 cd |
| Blessing `blessing` | 1 | slot 2 | AllAllies | - | 4 | +12% SpecialAttack 2t; +12% SpecialDefense 2t | - | L10: adds +5 CritChance 2t; L15: -1 cd |
| Radiant Bolt `radiant_bolt` | 3 | slot 3 | SingleTarget r3 | Special | 1 | Damage 70 | 70 | L10: adds -5% SpecialDefense 2t; L15: adds Damage 15 |
| Judgment `judgment` | 15 |  | SingleTarget r4 | Special | 3 | Damage 190 | 63 | L10: adds Stun 1t (15%); L15: -1 cd |
| Purifying Ward `purifying_ward` | 30 |  | AllAllies | - | 4 | Shield 25% Def 2t; +10% SpecialDefense 2t | - | L10: adds Heal 7; L15: -1 cd |
| Halo `halo` | 50 |  | AreaBurst (ally) r2 | - | 3 | Heal 19; +10% Defense 2t | - | L10: adds Shield 20% Def 2t; L15: -1 cd |

#### Basilisk — Dark, Ranged

| Skill | Learn | Default | Shape | Cat. | Cd | Effects | Dmg/turn | Tier bonuses |
| --- | ---: | :---: | --- | --- | ---: | --- | ---: | --- |
| Venom Spit `venom_spit` | 1 | slot 1 | SingleTarget r3 | Special | 1 | Damage 45; DoT 15 3t, stacks x3 | 68 | L10: adds -5% SpecialDefense 2t, stacks x3; L15: adds Damage 15 |
| Coup de Grace `coup_de_grace` | 1 | slot 2 | SingleTarget r3, lowest HP% | Special | 2 | Damage 115, execute +60% | 75 | L10: adds DoT 15 2t; L15: adds -10% Defense 2t |
| Petrifying Gaze `petrifying_gaze` | 4 | slot 3 | SingleTarget r3 | Special | 3 | Damage 45; Stun 1t (45%) | 15 | L10: adds -15% Speed 2t; L15: -1 cd |
| Eclipse Fang `eclipse_fang` | 12 |  | SingleTarget r3 | Special | 2 | Damage 32 x4 hits | 64 | L10: adds -8% SpecialDefense 2t; L15: adds Damage 20 |
| Predator Focus `predator_focus` | 25 |  | Self | - | 4 | +20 CritChance 3t; +10% SpecialAttack 3t | - | L10: adds +10% Speed 3t; L15: -1 cd |
| Miasma `miasma` | 40 |  | Cross r3 | Special | 3 | DoT 22 3t, stacks x2; -10% SpecialDefense 2t | 16 | L10: adds -10% Attack 2t; L15: -1 cd |

#### Avatar actives

| Skill | Default | Shape | Cd | Effects | Tier bonuses |
| --- | :---: | --- | ---: | --- | --- |
| Rallying Cry `rallying_cry` | slot 1 | AllAllies | 2 | +10% Attack 3t; +10% SpecialAttack 3t | L10: adds +5% Speed 2t; L15: -1 cd |
| Mending Light `mending_light` | slot 2 | AllAllies | 2 | Heal 13 | L10: adds +5% SpecialDefense 2t; L15: -1 cd |
| Aegis `aegis` | slot 3 | AllAllies | 4 | Shield 30% Def 2t | L10: adds +5% Defense 2t; L15: -1 cd |
| Hex of Frailty `hex_of_frailty` |  | AllEnemies | 2 | -10% Defense 3t; -10% SpecialDefense 3t | L10: adds -5% Attack 2t; L15: -1 cd |
| Battle Focus `battle_focus` |  | AllAllies | 3 | +8 CritChance 3t | L10: adds +5% Attack 2t; L15: -1 cd |
| Slowing Field `slowing_field` |  | AllEnemies | 3 | -12% Speed 3t | L10: adds -5% Attack 2t; L15: -1 cd |

#### Avatar passives

| Passive | Default | Trigger | Scope | Proc % | Max / battle | Int. cd | Effects | Tier bonuses |
| --- | :---: | --- | --- | ---: | ---: | ---: | --- | --- |
| Keen Eye `keen_eye` | slot 1 | Aura | AllAllies | 100 | - | 0 | +5 CritChance | L10: adds +2 CritChance; L15: adds +2 CritChance |
| Iron Will `iron_will` |  | Aura | AllAllies | 100 | - | 0 | +5% Defense; +5% SpecialDefense | L10: adds +2% Defense; L15: adds +2% SpecialDefense |
| Opening Ward `opening_ward` | slot 2 | BattleStart | AllAllies | 100 | - | 0 | Shield 40% Def 2t | L10: adds +5% Defense 2t; L15: adds +5% SpecialDefense 2t |
| Battle Hymn `battle_hymn` |  | BattleStart | AllAllies | 100 | - | 0 | +10% Speed 2t | L10: adds +5% Attack 2t; L15: adds +5% SpecialAttack 2t |
| Withering Curse `withering_curse` |  | BattleStart | AllEnemies | 100 | - | 0 | -10% Defense 3t; -10% SpecialDefense 3t | L10: adds -5% Speed 3t; L15: adds -5% Attack 3t |
| Bloodlust `bloodlust` |  | EnemyDefeated | AllAllies | 50 | - | 1 | +10% Attack 2t; +10% SpecialAttack 2t | L10: adds +5% Speed 2t; L15: adds +5 CritChance 2t |
| Vengeance `vengeance` |  | AllyDefeated | AllAllies | 100 | 2 | 0 | +15% Attack 3t; +15% SpecialAttack 3t | L10: adds Shield 20% Def 2t; L15: adds +10% Speed 3t |
| Storm Call `storm_call` |  | AllyCrit | AllEnemies | 30 | - | 1 | Damage 25 | L10: adds -5% Speed 1t; L15: adds -5% Defense 1t |
| Verdant Pulse `verdant_pulse` |  | AllyTurnStart | TriggeringUnit | 50 | - | 0 | Heal 5 | L10: adds +3% Defense 1t; L15: adds +3% SpecialDefense 1t |
| Last Stand `last_stand` | slot 3 | AllyBelowHpPercent < 40% | TriggeringUnit | 100 | - | 2 | Shield 80% Def 2t | L10: adds +10% Defense 2t; L15: adds +10% SpecialDefense 2t |

#### Materials

| Material | Tier | XP | Opens |
| --- | ---: | ---: | --- |
| Essence Shard `essence_shard` | 1 | 250 | the level-5 gate (and any lower) |
| Essence Crystal `essence_crystal` | 2 | 1000 | the level-10 gate (and any lower) |
| Essence Core `essence_core` | 3 | 4000 | the level-15 gate (and any lower) |

### Simulator

**The simulator's default is the real game setup:** `--skill-kit library` fields each beast's
`DefaultLoadout` at `--skill-level` (default 1; the tier is the gates below that level, so 16+ has
passed all three), in both element modes (`--kit neutral` forces the library skills' elements to
`None`), and `--avatar library` fields the library's default avatar — its first three actives and
`AvatarDefaultPassives` — at the same skill level. The committed `tuned-report.md` is that default.
`--skill-kit standard` (with `--avatar none` to match the old setting) still fields the standard kit
and its kit parity table, for measuring stat lines in isolation. (Before the retune the skill axis
was `--kit standard|library`; `--kit` now takes only `elemental|neutral|both`.)

**Open questions.** Whether
taunt needs a resist or diminishing returns (the Golem's 90% area taunt now reaches 3 hexes and
lasts 3 of the enemy's turns every 3 of its own, so a Golem that survives can keep a nearby enemy
taunted almost permanently; bosses resist half of it). Whether one skill should be allowed on several species (the library
allows it; this draft gives every skill exactly one owner). The resource cost (`ResourceCost`) is
0 everywhere until the resource itself is designed.

## Next steps

The grid and turn-manager scaffolding landed against decisions 1–3: a `BeastCraft.Battle.Grid`
namespace holding the arena-size presets, axial hex coordinates, a hexagon-shaped board with
occupancy tracking and A* pathfinding over it, plus a `BattleFormat` enum for the Solo/4/6 party
sizes, a minimal `BattleUnit`, and a speed-sorted `TurnManager` (since replaced by the ATB gauge,
decision 3 amended).

Decision 4 adds the targeting data model and its resolver: `SkillTargetSide`,
`SkillTargetingCriterion` and `SkillTargetingOrder`, the matching fields on `SkillSO`, and
`SkillTargetResolver`, which turns a skill plus a caster plus a roster into the list of units it
lands on. Its per-shape rules beyond the confirmed decisions above — which shapes exclude the
caster's own tile, that the global shapes ignore range, how a `Line` snaps its direction onto an
axis — are documented in that class as engineering defaults, not confirmed balance.

Decision 5 adds the rotation engine: `SkillLoadout` (an ordered skill stack plus one live cooldown
counter per *slot*, so the same skill equipped twice runs two independent counters), `SkillActivation`
(a fired skill paired with the units it landed on), a `Skills` loadout on `BattleUnit`, and
`SkillLoadout.TickAndResolve`, which ticks the rotation and runs each ready skill through
`SkillTargetResolver` in stack order. `SkillLoadout.Tick()` is deliberately separable and touches no
targeting at all, which is what keeps it usable for the avatar (decision 6) and for the movement
rule (decision 7), neither of which it knows anything about.

Decision 6's targeting half adds `BattleAvatar`, a factory that builds the avatar as an ordinary
`BattleUnit` — player team, a placeholder position the confirmed shape restriction guarantees
nothing reads, and its authored support loadout. It is a caster only and is not added to the roster
or the initiative queue. It originally gave the avatar zero stats; the amendment to decision 6 adds
the `BeastCraft.Avatar` namespace (`AvatarGearSO`, `AvatarGearSlot`, `AvatarStatsSO`), a
`StatCalculator.CollectModifiers` overload for avatar gear, and a `BattleAvatar.Create` overload that
takes base stats and equipped avatar gear. The zero-stat overload remains. EditMode tests cover the
avatar's stat assembly and that a statful avatar is still never hit or defeated.

Effect application (the section above) adds `SkillEffectApplier`, which resolves an activation into
state changes, a `CurrentHp` and an `ActiveStatModifiers` list on `BattleUnit`, and the
`ActiveStatModifier` record behind timed buffs.

Decision 7 adds the turn executor, which is also the pass that finally connects everything above to
everything else:

- `BattleUnit.MoveRange`, the per-turn movement budget. Since the stat-assembly pass it reads
  `Stats.MoveRange` rather than being set independently (see "Stat assembly and move range").
- `SkillLoadout.Tick()` now reports ready **slot indices** and no longer re-arms them;
  `SkillLoadout.MarkFired(slotIndex)` is the explicit re-arm. A slot offered and never marked fired
  simply stays at 0 and is offered again next turn, which is decision 7's "stuck skill" rule falling
  out of the clamp rather than being special-cased. `TickAndResolve` survives as the fire-everything
  path, which is still exactly right for the avatar's position-free kit.
- `SkillTargetResolver.PickFocusIgnoringRange`, the range-free half of the focus pick the resolver
  already did, so a beast can tell "no target anywhere" from "target too far away" — two situations
  that call for opposite behaviour. Same selection rule, one implementation, no change to how
  targeting resolves.
- `BattleTurnExecutor`, which owns one beast's turn (expire timed modifiers, tick the rotation,
  attempt each ready slot in stack order with the shared movement budget, then tick the avatar if the
  beast is player-side) and the loop that drives `TurnManager` from the first turn to the last. It is
  the one type allowed to know about the board, the roster, the rotation, the targeting and the
  effects at once, and it is what finally calls `SkillEffectApplier.Apply` and
  `TickModifiers`. Its results are reported as `BattleTurnResult` / `BattleSkillOutcome` /
  `BattleSkillStatus` per turn and `BattleResult` / `BattleOutcome` per battle.

Two things in that loop are worth calling out as engineering decisions rather than design ones. The
win condition is **"living units remain on more than one team"**, deliberately *not*
`TurnManager.IsComplete`: that property is true only once every unit in the roster is defeated, which
is the turn manager answering "is there anybody left to hand a turn to" — a battle won with survivors
still standing leaves it `false`, so a loop built on it would never stop. And the loop carries a
generous time cap (`BattleTurnExecutor.DefaultMaxTime`, a round cap before the ATB amendment) that
reports a stalemate; that is
a **safety net against a hang**, not a designed time limit, and encounter balance must not lean on
it.

Pre-battle placement (the section above) adds deployment zones on `HexGrid`
(`DeploymentZoneDepth`, `IsInDeploymentZone`, `GetDeploymentZone`) and a
`BeastCraft.Battle.Placement` namespace holding `PlacementValidator` and its result types —
`PlacementStatus`, `PlacementCountStatus`, `PlacementRequest`, `PlacementOutcome` and
`PlacementValidationResult`. Validation is non-mutating; `TryPlaceAll` is the separate, atomic
commit step.

The element system (the section above) adds the `Element` enum, `CreatureSpeciesSO.Elements`,
`SkillSO.Element`, `BattleUnit.Elements` and the static `ElementChart`, and makes
`SkillEffectApplier`'s damage arm scale by the chart. The first EditMode tests land with it, covering
the chart and the damage multiplier.

Stat assembly (the section above) adds `StatType.MoveRange` / `StatBlock.MoveRange`, makes
`BattleUnit.MoveRange` a read-through of `Stats`, and adds the static `StatCalculator` (species base
at level, then gear flat, then gear percent) and `BattleUnitFactory`, which builds a beast from its
species, level and gear. EditMode tests cover the stat block, the level-scaling exemption, the
assembly order, the factory, and move range under buffs.

The damage formula (the section above) adds `DamageCategory`, `SkillSO.Category`, `BattleUnit.Level`
(set by `BattleUnitFactory.CreateBeast` and by an optional level on the statful
`BattleAvatar.Create`) and the static `DamageFormula`, and threads the caster through
`SkillEffectApplier`'s damage arm so damage reads the caster's level and stats. EditMode tests cover
hand-computed values, physical/special stat selection, the element multiplier applied after the base,
the guards (minimum 1, zero or negative power, zero defence, negative attack), level scaling, buffs
and debuffs moving damage, avatar damage, and the loose level-invariance band against the roster's
`medium` curve; the existing element and avatar tests were rewritten against the formula.

The starter roster (the section above) adds `beast-roster.json`, the `BeastCraft.Creatures.Roster`
data types and validator, `GrowthRateCurve.CurveId` and `GrowthRateCurve.EvaluateScale`, the Editor
importer (the first Editor script, and the first `UnityEditor` stubs, in the since-retired `Tooling/CiStubs`), and
EditMode tests that check the JSON directly: structure, the ten pinned ids, one beast per element,
the stat-budget and move-range bands, and the curve semantics.

Two movement rules the simulator showed large PvE fights need are now built into
`BattleTurnExecutor` (decision 7, "scaffold details"): **defeated units leave the grid** the moment
they fall (lifted with `HexGrid.RemoveUnit` after every fired skill and applied avatar activation,
keeping their `Position` as a record), and a unit that cannot afford its whole approach makes a
**partial approach**, walking its remaining budget along the same route and holding the skill
(`OutOfMovement`, with the steps in `MovementSpent`). EditMode tests cover a freed tile being walked
onto in the same turn, a choke reopening for later turns, avatar kills, a partial approach walking
exactly the budget and firing a turn later, a fully blocked unit staying put, the shared budget
across slots, and a null grid.

The headless balance simulator (`Tooling/BalanceSim/`, see its README) is **local-only tooling, not a
CI job**. It references the engine-neutral runtime (`src/BeastCraft.Core`; it compiled the Unity
`Runtime` scripts against a UnityStub before the MonoGame move), reads
`beast-roster.json` with `System.Text.Json`, and fights through the real `BattleUnitFactory`,
`PlacementValidator`, `TurnManager` and `BattleTurnExecutor` on real `HexGrid`s. Since the
authored-kits retune its default is the real game setup — every beast's authored default loadout and
the library avatar (`--skill-kit library --avatar library`; see "Beast skill kits"). With
`--skill-kit standard` every beast fights with the same standard kit instead, rebalanced so `Attack` and
`SpecialAttack` weigh the same: Blast (special, power 40, range 3, cooldown 1), Strike (physical,
power 55, range 1, cooldown 1; the extra power offsets range 1 firing about 0.73x as often as
Blast) and a Burst split into equal physical and special halves (area, radius 2, power 20 each,
cooldown 2). It runs in an `elemental` mode (kit in the beast's element) and a `neutral` mode (kit
`Element.None`).

Its **primary mode is PvE, team versus encounter**, following the direction above. Every 4-beast
combination of the roster (210 teams) fights encounters generated from the game's encounter content
(`content/data/Encounters/`; simulator fixtures in
`Tooling/BalanceSim/encounters.json` until "Encounters as game content" above). Until the
mixed-encounter change below they were three fixed encounters: `boss` (one Colossus with very high
HP, heavy hits in both categories and a periodic area slam), `swarm` (24 small biters and stingers on
a Large arena) and `pack` (three melee direwolves and three ranged wisps), still available as
`--encounter-set fixed`. Each encounter's enemy stats are scaled by a multiplier calibrated per level
and kit mode so the average team clears it about half the time. Each beast is scored by its **marginal clear
rate** (clear rate of teams with it minus teams without it), with damage share, damage taken,
survival, time to clear and turns per unit of time alongside. The old 1v1 round-robin survives as a secondary
`--mode pvp` section.

The committed report at [`docs/balance/baseline-report.md`](../balance/baseline-report.md) is the
"before" picture for roster tuning, on the unchanged first-draft stats (levels 1/50/100), regenerated
under the Runtime's own movement rules (defeated units leave the grid, partial approach), fixture
enemies that move like beasts, and speed ties split evenly between the sides. In short: the bulky
sustain beasts and Kirin carry their teams. Kirin (+17.4 / +21.6 points overall, `elemental` /
`neutral`), Leviathan (+16.9 / +13.3) and Treant (+14.1 / +21.4) lead. The glass cannons drag their
teams down: Thunderbird (−23.4 / −31.9) and Phoenix (−16.8 / −21.1) are bottom three against every
encounter in both modes (no niche), with Basilisk (−12.6 / −16.0) close behind. Niches do show: Kirin
is the best beast against the boss (+33.9) but roughly neutral against the swarm (+4.8 / +0.1), and
Golem is the worst against the boss (−29.6) but third best against the swarm in `neutral` mode
(+14.7). No PvE or PvP battle stalemates. The PvP section, on the same kit, still rewards bulk
(`neutral`: Golem 89%; Leviathan, Griffin and Treant 78%).

The roster has since had **its first simulator-tuned pass** (base stats only; kit, fixtures,
formula, element chart and Runtime unchanged). The "after" picture is
[`docs/balance/tuned-report.md`](../balance/tuned-report.md), and
[`docs/balance/tuning-log.md`](../balance/tuning-log.md) has the stat changes, per-encounter
marginals before and after, the iteration log and the caveats. In `elemental` mode every beast's
overall marginal is now within ±5 points (−3.8 … +3.4, from −23.4 … +17.4), and in `neutral` mode
eight of ten are (−8.0 … +4.8, from −31.9 … +21.6). Roles still show per encounter: Tarasque, Kirin
and Frost Wyrm lead against the boss, where the slow tanks Leviathan and Treant are worst but lead
against the swarm and the pack. Three findings are design questions rather than stat problems:
the goal "every beast top-3 against some encounter" cannot hold for ten beasts and three encounters
(nine slots); in `neutral` mode the fastest fragile beast on a team walks into the pack first and
takes its focus, which leaves Thunderbird (−8.0) and Griffin (−7.4) just outside ±7 because their
approved identities make them the fastest; and a pure wall has no way to matter under
nearest-enemy targeting, so Golem's Attack rose from 80 to 105 rather than it staying a
low-attack wall — a taunt or guard mechanic would change that. The measurement noise between seeds
(about ±3 points overall) is close to the size of the target. These are inputs to the roster
discussion, not decisions. They depend on the simulator's assumptions listed in its README, above
all the fixture enemies, the one standard kit and nearest-enemy targeting.

**The turn order has since changed to the ATB gauge (decision 3), and the roster was not re-tuned
for it.** The baseline report and the tuning log's numbers were measured under the old round-based
queue (battle lengths in rounds); `tuned-report.md` has been regenerated under ATB and is the current
state (battle lengths in normalized time). As expected with Speed spanning 40–120 at max level (up
to 3× the turns), the marginals swung hard toward the fast beasts: in `elemental` mode the overall
range is now −35.5 … +17.4 (Basilisk, Griffin, Phoenix and Kirin +12 or more; Golem −35.5, Treant
−17.6 and Leviathan −15.4, all bottom three against every encounter), and `neutral` is similar
(−29.3 … +17.2). Pricing Speed as an action economy is the next roster question.

**Combat stances (decision 8) have since been added**, again without re-tuning the roster:
`CombatStance`, `CreatureSpeciesSO.Stance` / `BattleUnit.Stance` (set by `BattleUnitFactory`), the
roster JSON's `Stance` with its validator and importer support, `BattleSkillStatus.HeldByStance`,
`BattleTurnResult.RetreatSteps`, and the stance rules in `BattleTurnExecutor`; `ExecuteTurn` and
`RunBattle` keep their signatures. The simulator's fixtures gained a `Stance` per enemy group and
optional targeting per skill (wisps Ranged, stingers Skirmisher, both aiming at the lowest maximum
HP; the boss, biters and direwolves Vanguard and nearest-first). EditMode tests cover the plumbing,
the melee hold, the farthest in-range stop, retreats and their range cap, the crowd preference, the
Vanguard's screening and indifference to crowds, and determinism. In the regenerated tuned report the
three Ranged beasts fall from the top of the ATB table to around zero (the standard kit's Strike is
most of what they gave up), Griffin leads (+24.6 / +23.2 overall), Golem remains last but closer
(−25.0 / −15.8), and the `elemental` spread is −25.0 … +24.6; the tuning log has the before/after
tables and why the pack encounter's new lowest-HP targeting exaggerates Griffin and Thunderbird.

**Damage variance and critical hits have since been added** (user decision; "Variance and critical
hits" under "Damage formula"), again without re-tuning beyond the new crit values: `StatType.CritChance`
/ `StatBlock.CritChance` (curve-exempt, raised by `BuffStat` and gear), the variance and crit
constants and `Roll` / `RollCrit` / `RollVariance` / `ClampCritChance` on `DamageFormula`, the
`DamageRoll` and `DamageHit` records and `SkillActivation.Hits`, and the rng threaded from
`BattleTurnExecutor` through `SkillEffectApplier.Apply(activation, caster, rng)`; the two-argument
`Apply` and the rng-free `Compute` overloads remain as the deterministic fallback. The roster and
the simulator fixtures gained `CritChance`. Battles are now random but seeded, so the simulator runs
each team and fight `--samples` times (default 5) with distinct seeds and calibrates on the sampled
clear rate; its report adds a per-beast crit table (observed crit rates match the authored chances).
EditMode tests cover the explicit-roll formula, the order of operations and the floor, the null-rng
fallback, the two-draw order, clamping, the growth-curve exemption, crit buffs, debuffs and gear,
hit recording, flat heals and seeded determinism. The marginal clear rates move by less than the
seed-to-seed noise; the tuning log has the tables.

**Mixed encounters, `CurrentHp` targeting and a fair Ranged kit have since been added**, again
without re-tuning the roster. The Runtime gained `SkillTargetingCriterion.CurrentHp` (decision 4),
with EditMode tests for both orders, the id tie-break, both pick entry points, damage tracking and the
ignored targeting stat. The simulator's standard kit gives Ranged beasts **Shot** (Physical, range 3)
instead of Strike, which they never walked in for, and re-derives the physical powers (Strike 57,
Shot 41) so the physical share of single-target power is 49.7-50.6% in every stance (it had fallen
to 37-47%). Its default PvE run now fights generated mixed compositions with varied elements (see
"Encounter direction"): 4 shapes x 8 compositions, one multiplier calibrated per shape, level and kit
mode, 1 battle per team and composition, about 150 s on 8 threads; the report lists every composition
and adds per-stance kit parity and an element-matchup view. In the regenerated tuned report Phoenix
leads (+16.2 / +20.1 overall, `elemental` / `neutral`; Shot lets its Atk 125 count), Griffin's lead
is gone (+3.1 / +10.1, down from +26.3 / +22.6: most of it was the max-HP targeting threshold), and
the slow Vanguards remain last (Golem −15.2 / −21.8, Treant −13.8 / −16.8, Leviathan −9.7 / −15.0);
seed-to-seed noise is about 2 points overall. The tuning log has the tables and the split between
the kit and targeting change and the encounter change.

**The roster has since been re-tuned for the ATB gauge, stances, crits and mixed encounters**
(base stats only; stances, curves, crit chances, move ranges, kit, encounters, formula and Runtime
unchanged). By user decision base Speed now spans 92–105 (at most 1.15× from slowest to fastest,
down from 3×), with the archetypes' speed order kept; the roster tests pin both (see "Starter
roster"). On the mean of three base seeds (each seed also redraws the compositions), every beast's
overall marginal is within ±2.9 points in `elemental` mode (from −16.8 … +19.8) and ±5.8 in
`neutral` (from −22.6 … +19.4); no beast is top 3 in every shape; and nine of ten are top 3 in at
least one shape in `elemental` mode. The tenth, Treant, is fourth against the horde by 0.4 points,
well inside the noise; on any single seed one to three beasts miss, so the niche assignment is not
yet robust. What binds is structural: without a taunt or threat mechanic the four bulk
beasts (Leviathan, Golem, Treant and Griffin) earn their place almost only against the horde, which
has three top-3 slots. Thunderbird is the most polarized beast (first against the giant, last
against the horde). The tuning log has the before/after stats, the multi-seed tables, the fixed-set
sanity check and the iteration log.

**Square-root speed and the mitigation damage formula (Sword x Staff) have since replaced the linear
gauge and the level-term formula** (decision 3 and "Damage formula"; research in
[`docs/balance/research-sword-x-staff.md`](../balance/research-sword-x-staff.md)). The simulator's kit
and enemy powers were rescaled to the new "percent of the attacking stat" meaning (Blast 68, Strike
93, Shot 70, Burst 37), and the tuned report was regenerated on the **unchanged** roster; balance
shifted and a full retune (including widening base Speed for the turn-based 10–15% target) is the
next deliverable. The tuning log's "Sqrt speed + mitigation formula" section has the before/after
marginals and the per-beast turn rates.

**Skill progression has since been added** (the section above, by user decision): the
`BeastCraft.Progression` namespace (`SkillProgressionDefinition`, `SkillTierDefinition`,
`SkillProgress`, `SkillMaterialSO`, `BeastSkillBook`, `SkillProgression`, `SkillBreakthroughResult`,
`SkillEquipResult`), `SkillSO.Progression`, `SkillInstance` carried by `SkillLoadout` and
`SkillActivation` into `SkillEffectApplier`, `SkillLoadout.FromInstances` / `GetInstance`,
`BattleUnitFactory.BuildLoadout` with a skill-book `CreateBeast` overload, and `BattleSkillUsage`.
Level 1, tier 0 is the authored skill exactly, so the simulator and its reports are unchanged.
EditMode tests cover the curve, practice and material XP, gate blocking and banking, breakthroughs
with the right and wrong material tier, magnitude scaling in real damage, heals and buffs, tier
cooldown reduction and bonus effects in a battle turn, the equip rules, species acquisition, the
factory's slot order, and use counts read back from `RunBattle`.

The effect engine for per-beast kits has landed, as described in "Status effects and advanced skill
effects" above. It adds:

- Chance and `StatusResist`.
- `StatusType` statuses: taunt, stun, shield, damage over time and knockback.
- Stacking and percent stat changes.
- Multi-hit and execute.
- `HpFraction` targeting.
- `InitialCooldown` and `MaxUsesPerBattle`.

Every new field is inert at its default, so the simulator's report is unchanged. `encounters.json`
(now `enemy-library.json`) can express all of it, and the bosses carry 50% resistance. EditMode tests cover each rule,
including the draw order and seeded reproducibility.

**Element chart v2 has since replaced the first chart** (user-approved; see "Element system"):
the main eight are normalized to two 2x and two 0.5x per row and per column, Light and Dark became
generalists with a 1.25x `ElementChart.Mild` tier, and `ElementChartTests` pins all 121 pairs and the
normalization. The per-beast element effect (`elemental` minus `neutral` overall marginal, three
seeds) narrowed from −8.9 … +12.5 to −4.3 … +3.8 on the unchanged roster. That left Thunderbird at
−7.2 `elemental` (its old edge had hidden a weak neutral line), so a light retune followed (four
stat lines, nine skill numbers, six three-seed iterations); `tuned-report.md` is regenerated and the
tuning log's "Element chart v2" section has the tables. A follow-up experiment ("Thunderbird range
vs move") then gave Thunder Talons range 2 at 28 × 3 and restored the Thunderbird's Move 4. A
"niche pass" then swapped Storm Dive (now 120) into the Thunderbird's defaults for Static Charge,
trimmed Talons / Chain Lightning to 26 / 22 × 3, and made small lifts to Phoenix, Frost Wyrm,
Leviathan, Tarasque, Basilisk and Kirin skills (skill numbers only; the roster is unchanged).
Milestone 2's final retune (under the scouted-pick calibration) retuned the avatar for its own gauge,
made Thunder Talons target the weakest enemy in reach, lifted Griffin, Kirin and Leviathan skills and
made the level-difference curve more convex (k 0.012, q 0.009); skill numbers and two formula
constants only, the roster unchanged (tuning log, "Avatar retune" to "Level-gap re-check").

**Encounters have since become game content** (see "Encounters as game content"): the simulator's
enemy types, shapes and element-scheme weights moved into `content/data/Encounters/`, its generator into the
Runtime, and the calibrated multipliers into the simulator-written `encounter-difficulty.json`, with
an importer, `EncounterPlan` and the battle-session support to field them. No number changed (tuning
log, "Encounters as game content"); the campaign's difficulty target is pending producer review.

**Behaviour bonds and tiered difficulty** then replaced the stat bonds with bonds that act in battle
(see "Team bonds"), gave three enemy skills statuses, and calibrated each shape to its own target
(`squad` and `horde` 80%, `elite` 60%, `solo` 50%, a user decision); four beasts were retuned
within the stat budget to keep the balance guard (tuning log, "Behaviour bonds and tiered
difficulty").

Every pass so far is deliberately **data structures and algorithms only** — no MonoBehaviours, no
scene or prefab wiring, and no committed `.asset` instances (the roster's are generated in-Editor). The hex radii backing each arena preset
are placeholder implementation defaults chosen to be tunable, not producer-confirmed balance
numbers, and the deployment-zone split, the effect rules and the element chart above are the same
kind of default, as is the damage formula.
Still to come: confirming or revising the third tuning pass (and, if needed, the damage formula and
element chart), deciding the design questions it raised above — a design decision the reports inform
rather than make — and extending the simulator once authored skills, real encounters and the avatar
give it more than a standard kit and fixture enemies to measure; the starter roster's skills (none are
authored yet — the effect engine they need, statuses included, has landed), the avatar's passive
content (the passive engine has landed, see "Avatar passives"), resource gating on top of cooldowns,
the placement UI (a Unity
Editor task, not a continuation of the placement validation that just landed), the encounter
definition that selects an arena preset and a battle format, and the presentation layer.
