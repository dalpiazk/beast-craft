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
creatures do the fighting on the grid. This matches the framing already baked into the project spec,
where the avatar and the creature systems are built and customized separately — avatar items are
cosmetic, while gear and combat stats live entirely on creatures.

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

Once the battle starts there is **no player action menu**. Combatants take turns in the speed-stat
initiative queue (decision 3), one unit at a time, and on its own turn each beast:

1. **Moves**, up to its own move range, measured in grid steps.
2. **Fires whichever of its equipped skills come off cooldown this turn** — which may be none, one,
   or several at once — with targeting resolved automatically from each skill's own authored rules
   (decision 4). Nothing chooses *between* the equipped skills: the beast's skill stack is a fixed
   rotation driven by per-skill cooldown counters (decision 5).

The player's leverage over that is the build and the placement, not a per-turn command. This suits
the game's shape: the story is the spine, and a fight that resolves from a good team composition and
a good deployment keeps the narrative pacing intact rather than interrupting it with a tactics
puzzle every encounter.

### What is not settled yet

- **Move range does not exist in code.** `StatBlock` has no move-range stat, and nothing implements
  movement during a battle. `HexPathfinder` can already produce the route; what is missing is the
  stat that budgets it and the rule for where a beast chooses to move (toward the nearest enemy? to
  the nearest tile from which its equipped skill reaches something? away when hurt?). All of that
  is open.
- **Resource gating.** `SkillSO.ResourceCost` is authored but nothing spends it, and the resource
  itself ("mana" / "focus" / "stamina") is still unnamed. The cooldown rotation (decision 5) is now
  settled and implemented; how — or whether — a resource pool gates it on top is not. A skill that
  comes off cooldown currently always fires.
- **The turn executor.** Nothing yet drives `TurnManager` end to end: movement execution, the
  cooldown tick, and effect application each exist or don't as separate pieces, and the loop that
  orders them within a single beast's turn is a later integration pass.
- **The basic attack.** Earlier drafts listed "a basic attack, a skill, or an item" as the turn's
  options. With no player menu, whether a beast has a fallback attack at all — or simply always has
  at least one short-cooldown skill in its rotation — is open, and items in battle are out of scope
  until there is a mechanism that would use them.

## Data-driven foundation already in place

The authored data this combat model needs is already committed as ScriptableObject schemas under
`BeastCraft/Assets/_Project/Scripts/Runtime/Battle/`:

- **`SkillSO`** — target shape (`SingleTarget`, `Line`, `Cross`, `AreaBurst`, `AllEnemies`,
  `AllAllies`, `Self`), range, resource cost, cooldown, a list of effects, and the targeting fields
  added by decision 4 (side, criterion, order, targeting stat).
- **`GearSO`** — slot, stat modifiers, rarity tier, and minimum creature level.

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

### 3. Turn order model — DECIDED

**Turn order is a speed-stat initiative queue.** All combatants are sorted by `StatType.Speed`
(already present on `StatBlock` as `public int Speed`) into a single order, and **one unit acts at a
time**. This was the recommendation this document made, and it is confirmed.

*Background.* The initiative queue is classic JRPG/tactics pacing, it is the simplest thing to
build, and it is by far the easiest to tutorialize — the player always knows exactly whose turn it
is and what happens next. The alternative, a **simultaneous declare-then-resolve** model where both
sides choose all their units' actions up front each round and resolve them together, is meaningfully
more strategic (reads, baits, committed positioning) but costs significantly more in UI, AI, and
tutorial work, and it makes failure states harder for a new player to parse. Phase-based resolution
stays open as a *later* evolution if playtesting says the combat wants more depth; it is not part of
the first playable slice.

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
  - `SkillTargetingCriterion` — *what* to compare by: `Random`, `Stat`, or `Distance`.
  - `SkillTargetingOrder` — *which extreme* wins: `Lowest` or `Highest`.

  `Stat` compares one specific authored `StatType` (`SkillSO.TargetingStat`) — not current HP, not
  an aggregate of the stat block — so "hit the slowest enemy" is `Enemy` + `Stat` + `Lowest` +
  `Speed`, and "buff the ally with the highest Attack" is `Ally` + `Stat` + `Highest` + `Attack`.
  `Distance` compares hex steps from the caster, so `Lowest` is nearest and `Highest` is farthest.
  `Random` compares nothing and ignores both the order and the targeting stat.

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
- **Tick.** Every time it becomes the caster's own turn in the initiative queue (decision 3), *all*
  of its equipped skills' counters tick down by 1, clamped at 0 — never negative.
- **Fire and reset.** Any skill whose counter is exactly 0 after that tick fires this turn and
  immediately resets to its authored `Cooldown` to start counting down again. Firing and re-arming
  are a single step.
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

### 6. The avatar's skill loadout — DECIDED (timing and targeting)

**The avatar has skills too, on the same rotation mechanic**, intended to support and buff the
player's own beasts rather than to attack. Per decision 2 the avatar is **not a piece on the grid**
and has no meaningful `HexCoordinate` position, and it correspondingly gets **no slot of its own in
the initiative queue**.

**Confirmed timing:** the avatar's loadout **ticks once every time one of the player's own beasts
takes its turn.** Not on enemy turns, and not once per round — once per player-side beast-turn. With
three player beasts deployed, the avatar's counters therefore tick three times per round, and an
avatar skill on a 3-turn cooldown fires roughly once a round rather than once every three.

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
- **The avatar has no stats and cannot be defeated.** Its `StatBlock` is all zeros, which is the
  honest value rather than a placeholder — the project spec puts combat stats entirely on creatures
  and makes avatar items cosmetic. `IsDefeated` stays `false` for the life of the battle; there is
  no rule in the design by which a commander could be defeated, and nothing can write the flag on a
  unit it cannot target.

**The timing half is still not wired to anything.** The rotation engine that landed for decision 5
is deliberately owner-agnostic (it ticks a list of skills and knows nothing about the board or the
roster), so it drives the avatar unchanged — but nothing calls it on the avatar yet. Ticking the
avatar once per player-side beast-turn is part of the turn-executor pass.

## Effect application — SCAFFOLD ASSUMPTIONS, NOT CONFIRMED BALANCE

`SkillEffect`s are now actually applied: `SkillEffectApplier` takes a `SkillActivation` (the fired
skill plus the units it landed on) and resolves it into real state changes. Unlike decisions 1–6
above, **none of the rules in this section were confirmed by the producer.** They are engineering
defaults chosen so the system is complete rather than half-built, and they are expected to be
revisited when balance work starts.

- **Damage and healing are flat. There is no damage formula.** `SkillEffect.Magnitude` is applied
  as a plain number straight against HP: no Attack-versus-Defense math, no stat scaling, no crit
  chance, no type chart, no variance. Designing that formula is a separate balancing pass, and it
  is deliberately not being guessed at here — a placeholder formula would be harder to displace
  later than no formula at all. `BattleUnit` gained a `CurrentHp` alongside its `StatBlock` (whose
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

  **That tick hook is not wired to anything.** No turn executor exists yet to call it (see "What is
  not settled yet" above), so today a timed modifier applies and never expires. The mechanism is
  provided now so the duration half of the effect model is complete; connecting it is part of the
  turn-executor pass, and it must be called once per turn of the unit passed in.

- **`ApplyStatus` is unimplemented and does nothing.** There is no status-effect system anywhere in
  the data model — no poison, stun or burn, and no field on `SkillEffect` naming *which* status,
  because the set of statuses has never been designed. Implementing the effect would mean inventing
  that design inside the effect applier, which is the wrong place for it. The switch arm exists and
  is documented as a deliberate gap rather than silently falling through: authoring an `ApplyStatus`
  effect today is a no-op. It is pending an actual status-effect design, at which point that arm is
  where it plugs in.

Resource cost is still not spent, per the open question above; effect application does not gate on
it.

## Next steps

The grid and turn-manager scaffolding landed against decisions 1–3: a `BeastCraft.Battle.Grid`
namespace holding the arena-size presets, axial hex coordinates, a hexagon-shaped board with
occupancy tracking and A* pathfinding over it, plus a `BattleFormat` enum for the Solo/4/6 party
sizes, a minimal `BattleUnit`, and a speed-sorted `TurnManager`.

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
targeting at all, which is what keeps it usable for the avatar (decision 6) later.

Decision 6's targeting half adds `BattleAvatar`, a one-method factory that builds the avatar as an
ordinary `BattleUnit` — player team, zero stats, a placeholder position the confirmed shape
restriction guarantees nothing reads, and its authored support loadout. It is a caster only and is
not added to the roster or the initiative queue. Nothing ticks it yet; the once-per-player-beast-turn
timing is the turn executor's job.

Effect application (the section above) adds `SkillEffectApplier`, which resolves an activation into
state changes, a `CurrentHp` and an `ActiveStatModifiers` list on `BattleUnit`, and the
`ActiveStatModifier` record behind timed buffs. Nothing calls either `Apply` or `TickModifiers` yet;
both are mechanism waiting on the turn executor.

Every pass so far is deliberately **data structures and algorithms only** — no MonoBehaviours, no
scene or prefab wiring, and no authored `.asset` instances. The hex radii backing each arena preset
are placeholder implementation defaults chosen to be tunable, not producer-confirmed balance
numbers, and the effect rules above are the same kind of default. Still to come: the move-range stat
and movement during battle, the turn executor that drives `TurnManager` end to end and calls the
rotation and the effect applier at the right points, the damage formula and stat scaling on top of
flat magnitudes, the status-effect system behind `ApplyStatus`, resource gating on top of cooldowns,
the avatar's once-per-player-beast-turn tick (decision 6), the pre-battle placement system and its
UI, the encounter definition that selects an arena preset and a battle format, and the presentation
layer.
