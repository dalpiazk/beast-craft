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
- **Does the avatar get its own gauge?** The avatar still ticks once per player-beast turn
  (decision 6). Under the ATB gauge (decision 3) that ties its cadence to how fast the player's
  beasts are: a fast team cycles its avatar faster. The alternative is an avatar gauge filled by
  the avatar's own Speed, which would make that stat (currently unused) matter and decouple the
  avatar from team composition. Open; the rule is unchanged until it is decided.
- **Avatar progression.** The avatar now has stats (decision 6, amended), but no progression
  level and no growth: its base is a flat authored block and only avatar gear moves it. Whether and
  how the avatar levels up is undesigned. The damage formula still needs a caster level for it, so
  `BattleAvatar.Create` takes a per-battle level (default 1) that the battle setup is expected to
  pick sensibly, e.g. the player team's level — a stopgap input, not a design for progression.

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
- **Large creatures are single-tile today — open item.** `HexGrid` tracks exactly one tile per unit;
  there is no multi-hex footprint, so a "huge" enemy occupies one hex like everything else and can be
  surrounded by six attackers. Multi-hex units would touch occupancy, pathfinding (a footprint has to
  fit along the route), targeting (distance to a footprint, not a point) and area-skill overlap.
  Not designed or implemented; listed so encounter design does not assume it exists.
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
  unit, or none) so all ten elements appear. These are simulator fixtures, not game content or
  encounter design; the pool, shapes and generator rules are in `Tooling/BalanceSim/README.md`.
- **Most of what the simulator fields targets the nearest enemy.** Whoever stands in front takes
  most of the hits, so front-line bulk and placement matter a great deal. The simulator's "pick off
  the weakest" enemies (stalker, caster and the champion's hex; the fixed set's wisps and stingers)
  aim at the beast with the least **current** HP (`SkillTargetingCriterion.CurrentHp`, decision 4);
  before that criterion existed they compared maximum HP, which never tracks damage taken. Every
  enemy type has a stance of its own (decision 8).

## Data-driven foundation already in place

The authored data this combat model needs is already committed as ScriptableObject schemas under
`BeastCraft/Assets/_Project/Scripts/Runtime/Battle/`:

- **`SkillSO`** — target shape (`SingleTarget`, `Line`, `Cross`, `AreaBurst`, `AllEnemies`,
  `AllAllies`, `Self`), range, resource cost, cooldown, a list of effects, and the targeting fields
  added by decision 4 (side, criterion, order, targeting stat), the attacking `Element` (see
  "Element system" below), and the damage `Category`, physical or special (see "Damage formula"
  below).
- **`GearSO`** — slot, stat modifiers, rarity tier, and minimum creature level. Beast gear only.
- **`AvatarGearSO`** / **`AvatarStatsSO`** (under `Runtime/Avatar/`, namespace `BeastCraft.Avatar`)
  — the avatar's own stat gear (slot, stat modifiers, rarity; no visuals) and its authored base
  stats. See decision 6.

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
  faster. The avatar is the exception still being discussed; see decision 6.

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

### 6. The avatar's skill loadout — DECIDED (timing and targeting); stats AMENDED

**The avatar has skills too, on the same rotation mechanic**, intended to support and buff the
player's own beasts rather than to attack. Per decision 2 the avatar is **not a piece on the grid**
and has no meaningful `HexCoordinate` position, and it correspondingly gets **no turn of its own in
the turn order** (no gauge — but see the open item below).

**Confirmed timing:** the avatar's loadout **ticks once every time one of the player's own beasts
takes its turn.** Not on enemy turns — once per player-side beast-turn. With three player beasts
deployed, the avatar's counters therefore tick three times for every turn a typical one of them
takes, and an avatar skill on a 3-turn cooldown fires about as often as one beast acts.

**Open item since the ATB amendment (decision 3).** Rounds are gone, so "once per player-beast turn"
now also means the avatar cycles faster the faster the player's beasts are. Whether the avatar
should instead fill a gauge of its own from its own Speed is undecided (listed under "What is not
settled yet"); until then the rule above stands and is what `BattleTurnExecutor` does.

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

- **The avatar is a caster, not a member of the roster.** It is passed as the `caster` argument and
  is *not* added to the `allUnits` roster or to `TurnManager`. That is what makes it a non-combatant
  in practice: it takes no initiative turn, an enemy `AllEnemies` sweep cannot reach it, and its own
  `AllAllies` buff lands on the player's beasts. A `Self` skill still works, since the resolver
  returns the caster directly without consulting the roster.
- **The avatar cannot be defeated.** `IsDefeated` stays `false` for the life of the battle; there
  is no rule in the design by which a commander could be defeated, and nothing can write the flag on
  a unit it cannot target. (This bullet originally also said the avatar had no stats — superseded,
  see below.)

**The timing half is now wired up.** `BattleTurnExecutor` ticks the avatar's loadout at the end of
every player-side beast's turn, exactly as this section describes: not on enemy turns, and not on a
clock of its own. The rotation engine that landed for decision 5 is deliberately owner-agnostic, so it
drives the avatar unchanged.

**Amendment — the avatar has stats and stat gear (supersedes "the avatar has no stats").** This
section previously decided that the avatar's `StatBlock` is all zeros because avatar items are
cosmetic. The producer has reversed that, and the rule is now two separate things:

- **Avatar cosmetics stay purely cosmetic.** The customization system (`AvatarCustomizationSchema`)
  is unchanged: it decides what the avatar looks like and grants no stats.
- **The avatar has real stats, raised by non-cosmetic avatar gear that is never rendered.** Its
  base is authored on an `AvatarStatsSO` (a flat `StatBlock`; there is no avatar level). Gear is
  `AvatarGearSO` — a stable `AvatarGearId`, display name, description, inventory icon, an
  `AvatarGearSlot` (`Weapon`, `Armor`, `Trinket`), a `StatModifier` list and a rarity tier, and no
  visual fields at all. It is a separate type from the beasts' `GearSO`, with its own slot enum, so
  beast gear and avatar gear cannot be cross-equipped. `BattleAvatar.Create(skills, baseStats,
  equipped)` assembles the avatar's stats through `StatCalculator` exactly like beast gear (flat,
  then summed percent, rounded, every stat at least 0 and `HP` at least 1); null gear and null
  modifiers are skipped, and one-item-per-slot is left to the equipment screen, as for beasts. The
  original `BattleAvatar.Create(skills)` still builds an all-zero avatar for callers with no stats
  authored.

Everything else above is unchanged: the avatar is still off the grid, still takes no initiative
turn, still ticks on player-beast turns, is still a caster outside the roster, and still cannot be
defeated. **The avatar's stats now feed the damage formula** exactly as a beast's do (see "Damage
formula"): a damaging avatar skill uses the avatar's `Attack` or `SpecialAttack`. Heals
and buffs are still flat for everyone, so an avatar buff still lands the same whatever the avatar's
stats are. Avatar leveling is out of scope and open (see "What is not settled yet"); the statful
`BattleAvatar.Create` takes a per-battle level (default 1), recorded on the unit (the damage formula
no longer reads level). The zero-stat `Create(skills)` avatar has no attacking stat, so every damage
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
  it) and stopping at the first tile on that route that is in range.

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
  plain rule's own pick, then a fixed board order. A Vanguard does not avoid crowds.
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
  null grid, or if the unit defeated itself. It happens before the avatar's activations, counts in
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
single-target range, retreating before the avatar acts) are engineering defaults chosen to be
deterministic and legible, and are cheap to revisit. The balance simulator's first look at them is
in the tuning log ("After combat stances"): with the one-size standard kit, Ranged beasts lose
Strike's damage and the physical/special parity the kit was tuned to, so the numbers say more about
that kit than about the stances.

## Effect application — SCAFFOLD ASSUMPTIONS, NOT CONFIRMED BALANCE

`SkillEffect`s are now actually applied: `SkillEffectApplier` takes a `SkillActivation` (the fired
skill plus the units it landed on) and resolves it into real state changes. Unlike decisions 1–6
above, **none of the rules in this section were confirmed by the producer.** They are engineering
defaults chosen so the system is complete rather than half-built, and they are expected to be
revisited when balance work starts.

- **Damage is stat-based; healing is flat.** A `Damage` effect's `SkillEffect.Magnitude` is its
  *power*, and the HP it takes comes from `DamageFormula` — caster level, caster attacking stat
  against target defending stat, the element multiplier, then a crit roll and a variance roll (see
  "Damage formula" below). This superseded the original rule, which applied every magnitude flat and
  deferred the formula to a balancing pass. Heals are still applied flat with no variance, and buffs
  and debuffs still move a stat by exactly their magnitude; whether and how healing should scale is
  deferred to the balance pass.
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

- **`ApplyStatus` is unimplemented and does nothing.** There is no status-effect system anywhere in
  the data model — no poison, stun or burn, and no field on `SkillEffect` naming *which* status,
  because the set of statuses has never been designed. Implementing the effect would mean inventing
  that design inside the effect applier, which is the wrong place for it. The switch arm exists and
  is documented as a deliberate gap rather than silently falling through: authoring an `ApplyStatus`
  effect today is a no-op. It is pending an actual status-effect design, at which point that arm is
  where it plugs in.

Resource cost is still not spent, per the open question above; effect application does not gate on
it.

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
only ever looked up in that direction; it is not forced to be symmetric. `2x` is strong, `0.5x` is
weak, and every pair not listed is `1x`:

| Attack | Strong against (2x) | Weak against (0.5x) |
| --- | --- | --- |
| Fire | Nature, Metal | Water, Earth |
| Water | Fire, Earth | Lightning, Nature |
| Earth | Lightning, Metal | Water, Air |
| Air | Earth, Nature | Ice, Lightning |
| Lightning | Water, Air | Earth, Metal |
| Ice | Nature, Air | Fire, Metal |
| Nature | Water, Earth, Dark | Fire, Ice |
| Metal | Ice, Light | Fire, Lightning |
| Light | Dark | — |
| Dark | Light | — |

**These values are a tunable starting default, not producer-confirmed balance** — the same standing
as the arena radii and the deployment-zone split. The set of elements is fixed; which pairs are
strong or weak, and whether 2x / 0.5x are the right sizes, are expected to move once balance work
has real fights to measure. `ElementChart` is the single place to change them.

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
damage = max(1, truncate(base × ElementChart multiplier × crit × roll / 100))
crit   = max(MinCritMultiplier, CritMultiplier) = 1.5 on a critical hit, else 1
roll   = a whole percent, uniform on [90, 110]
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
- **Level is not in the formula.** Stats already scale with level through the growth curve, and with
  `A`, `D` and HP on the same curve a hit between two equally levelled beasts takes the same share of
  HP at every level, up to integer rounding. (The old `(2 × Level / 5 + 2)` term only held that
  loosely: 20% at level 1 against 35% at level 100.) `BattleUnit.Level` is kept — it is recorded and
  other systems may read it — but `DamageFormula` ignores it, and the raw overloads no longer take a
  level: `Compute(power, attack, defense, element[, variancePercent, isCrit])` and
  `ComputeBase(power, attack, defense)`.
- The element multiplier is the skill's element against the target's elements, exactly as before.
  The caster's own elements still do nothing.
- **Arithmetic.** The base is computed in double precision (basic IEEE operations only, one division
  last, so an exactly whole hit never truncates to one less) and truncated once at the end, after the
  element, crit and variance multipliers (in that order). Crits and rolls are covered under
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
to 1). `BattleUnitFactory.CreateBeast` records the level it assembled the stats at. The avatar has no
progression level, so the statful `BattleAvatar.Create` takes a per-battle level (default 1) — see
"What is not settled yet". None of these feed damage any more; level reaches damage only through the
stats it assembled.

**Still deferred:** a same-element attack bonus (STAB), a balance lever to add once there are
fights to measure it against; the reference's flat skill damage, damage boost / damage resistance and
skill damage reduction terms; and stat-scaled healing — heals stay flat, with no variance. Random
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
stack order, then the avatar's activations; within a skill, target-major and then in authored effect
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
`AvatarStatsSO` base and its `AvatarGearSO` (decision 6, amended). Avatar gear has no minimum level,
because the avatar has no progression level.

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
approved. **The numbers below are the second simulator-tuned pass**: first-draft stats chosen to
express each archetype were tuned by hand against the headless balance simulator's PvE mode, then
re-tuned for the ATB gauge, combat stances, variance and crits and the generated mixed encounters,
with base Speed held to a 15% band (see "Next steps" and
[`docs/balance/tuning-log.md`](../balance/tuning-log.md), which has the first draft and both passes).
Nothing here is confirmed balance; the numbers are expected to move again once skills and real
encounters exist.

| SpeciesId | Beast | Element | Archetype | Stance | Curve | HP | ATK | DEF | SpA | SpD | SPE | Six-stat total | Move | Crit |
| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `phoenix` | Phoenix | Fire | Glass cannon | Ranged | medium | 92 | 102 | 70 | 120 | 85 | 101 | 570 | 4 | 10% |
| `leviathan` | Leviathan | Water | Tank | Vanguard | medium | 122 | 86 | 126 | 86 | 100 | 95 | 615 | 3 | 3% |
| `golem` | Golem | Earth | Pure wall | Vanguard | medium | 146 | 95 | 137 | 64 | 96 | 92 | 630 | 2 | 2% |
| `griffin` | Griffin | Air | Fast skirmisher | Skirmisher | medium | 106 | 112 | 91 | 91 | 91 | 104 | 595 | 5 | 8% |
| `thunderbird` | Thunderbird | Lightning | Burst striker | Skirmisher | medium | 110 | 117 | 88 | 114 | 91 | 105 | 625 | 4 | 15% |
| `frost_wyrm` | Frost Wyrm | Ice | Control / attrition | Vanguard | medium | 98 | 74 | 124 | 103 | 118 | 98 | 615 | 3 | 5% |
| `treant` | Treant | Nature | Support-tank | Vanguard | medium | 134 | 87 | 98 | 93 | 124 | 94 | 630 | 3 | 3% |
| `tarasque` | Tarasque | Metal | Armored bruiser | Vanguard | medium | 112 | 137 | 127 | 54 | 78 | 97 | 605 | 3 | 6% |
| `kirin` | Kirin | Light | Support caster | Ranged | medium | 115 | 51 | 90 | 150 | 124 | 100 | 630 | 4 | 5% |
| `basilisk` | Basilisk | Dark | Ranged assassin | Ranged | medium | 103 | 94 | 79 | 153 | 94 | 102 | 625 | 5 | 12% |

Stats are max-level values (curve scale 1). **All ten beasts share the `medium` growth curve for
now, by user decision**; differentiating curves per beast is deferred to the headless balance
simulator. The drafting rules:

- **Shared budget.** Every beast's six combat stats sum to a shared budget of 600, and the roster
  tests allow ±5% (570–630). Archetype comes from how the budget is *distributed*, not from raw
  power. The first draft put every beast at exactly 600; both tuning passes used the ±5% band as a
  balance lever. After the second, Phoenix sits on the 570 floor (its Fire kit and Ranged stance
  carry it; see the tuning log), Griffin is at 595 and the others at 605–630 (Kirin, Treant and
  Golem on the ceiling). If
  the simulator later gives some beasts a slower curve, whether they deserve a larger budget as
  payoff for a weak early game is a balance question for it, not something this pass assumes.
- **Speed is a narrow band (user decision): the fastest base Speed is at most 1.15× the slowest.**
  Under the ATB gauge (decision 3) Speed is an action economy (twice the Speed is twice the turns),
  so the first pass's 40–120 spread gave Thunderbird three turns for each of Golem's, and no amount
  of bulk made up for it: the slow Vanguards were bottom three nearly everywhere. The user expects
  the fastest and slowest beasts to differ by about 10–15%; the roster uses 92–105 (1.14×). This
  replaces the lead default in
  [`docs/balance/research-crit-variance-speed.md`](../balance/research-crit-variance-speed.md) §5
  ("target ~2.5–3× slowest:fastest"), which that research marked unsourced (no source gives a target
  ratio) and left for the simulator to validate. The simulator did not bear it out, and the genre's
  answer for slow tanks is taunt/threat and damage reduction rather than speed (same research, §4).
  Within the band the archetypes keep their **order** (Thunderbird 105 > Griffin 104 > Basilisk 102
  > Phoenix 101 > Kirin 100 > Frost Wyrm 98 > Tarasque 97 > Leviathan 95 > Treant 94 > Golem 92),
  and the budget Speed no longer takes went into the slow beasts' other stats. The roster tests pin
  the band and the order (Golem strictly slowest), so widening either is a deliberate design change.
  **Superseded in part by the square-root gauge (decision 3, second amendment):** the 10–15% target
  now applies to *turns*, which grow with `sqrt(Speed)`, so the 1.14× Speed band above gives only
  about 1.07× turns, and a 1.10–1.15× turn spread needs roughly a 1.21–1.32× Speed spread. The
  roster test still pins the Speed stat band; replacing it with a turn-ratio test is part of the next
  roster retune (tracked in the tuning log), not of the formula change.
- **Move range in a small band (2–5)**, outside the budget. Griffin and Basilisk are the mobile
  ends (5); Golem is the only 2.
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
- **Skills, evolutions and customization are empty.** No skills have been authored yet, so every
  species' `LearnableSkills` and `EvolutionOptions` are empty and `CustomizationSchema` and `Icon`
  are unset. The importer never touches those fields, so authoring them on the assets later is safe.

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

The roster lives in **`BeastCraft/Assets/_Project/Data/Creatures/beast-roster.json`**, not in
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
for the avatar's passive skills later, so it is built generically. The numbers below are
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
  `Equipped`, `EquipSlotCount = 3` skill ids by slot. **Slot order is fire priority.**
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
pacing. There is no inventory yet, so consuming a material is the caller's job. No UI, no save
system and no authored materials or tier bonuses exist yet.

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
importer (the first Editor script, and the first `UnityEditor` stubs in `Tooling/CiStubs`), and
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
CI job**. It compiles the `Runtime` scripts against the committed UnityStub, reads
`beast-roster.json` with `System.Text.Json`, and fights through the real `BattleUnitFactory`,
`PlacementValidator`, `TurnManager` and `BattleTurnExecutor` on real `HexGrid`s. No skills are
authored yet, so every beast fights with the same standard kit, rebalanced so `Attack` and
`SpecialAttack` weigh the same: Blast (special, power 40, range 3, cooldown 1), Strike (physical,
power 55, range 1, cooldown 1; the extra power offsets range 1 firing about 0.73x as often as
Blast) and a Burst split into equal physical and special halves (area, radius 2, power 20 each,
cooldown 2). It runs in an `elemental` mode (kit in the beast's element) and a `neutral` mode (kit
`Element.None`).

Its **primary mode is PvE, team versus encounter**, following the direction above. Every 4-beast
combination of the roster (210 teams) fights synthetic encounters defined in
`Tooling/BalanceSim/encounters.json`. These are simulator fixtures, not game content. Until the
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

Every pass so far is deliberately **data structures and algorithms only** — no MonoBehaviours, no
scene or prefab wiring, and no committed `.asset` instances (the roster's are generated in-Editor). The hex radii backing each arena preset
are placeholder implementation defaults chosen to be tunable, not producer-confirmed balance
numbers, and the deployment-zone split, the effect rules and the element chart above are the same
kind of default, as is the damage formula.
Still to come: confirming or revising the second tuning pass (and, if needed, the damage formula and
element chart), deciding the design questions it raised above — a design decision the reports inform
rather than make — and extending the simulator once authored skills, real encounters and the avatar
give it more than a standard kit and fixture enemies to measure; multi-hex large creatures, an open
item under "Encounter direction" above; stat-scaled healing; the starter roster's skills (none are
authored yet), the status-effect system behind `ApplyStatus`, resource gating on top of cooldowns,
the placement UI (a Unity
Editor task, not a continuation of the placement validation that just landed), the encounter
definition that selects an arena preset and a battle format, and the presentation layer.
