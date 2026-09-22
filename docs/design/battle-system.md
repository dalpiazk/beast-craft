# Battle System Design Proposal

Beast Craft is a narrative story-adventure with creature-collection and battle progression. It
draws its gameplay-depth inspiration from Sword x Staff's class/promotion/build systems and its
idle progression, but reframes all of it around a narrative core rather than a live-service idle
grind: the story is the spine, and the systems exist to give that story mechanical weight. This
document describes the **tactical grid-based combat** approach for that battle layer, how it sits on
top of the ScriptableObject data schemas already committed to the project, and the three
foundational decisions the producer has now confirmed. Those three decisions are settled; the first
runtime scaffolding built against them lands alongside this revision of the document.

## Core loop

The player's avatar **does not fight directly**. The avatar is a non-combatant commander: it selects
which creatures are deployed, and issues their actions each turn. The creatures do the fighting on
the grid. This matches the framing already baked into the project spec, where the avatar and the
creature systems are built and customized separately — avatar items are cosmetic, while gear and
combat stats live entirely on creatures.

Within a turn, a creature follows a standard tactics-game action economy:

1. **Move** — up to that unit's own move range, measured in grid steps.
2. **Act** — one action: a basic attack, a skill, or an item. The action is constrained by that
   skill's own range and target shape.

One action per unit per turn, move-then-act ordering. This is deliberately conventional: it is the
action economy players already understand from the tactics genre, it tutorializes in a single
encounter, and it keeps the first playable slice small enough that the narrative content around it
stays the focus.

## Data-driven foundation already in place

The authored data this combat model needs is already committed as ScriptableObject schemas under
`BeastCraft/Assets/_Project/Scripts/Runtime/Battle/`:

- **`SkillSO`** — target shape (`SingleTarget`, `Line`, `Cross`, `AreaBurst`, `AllEnemies`,
  `AllAllies`, `Self`), range, resource cost, cooldown, and a list of effects.
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

The three questions this document previously left open have been **decided by the producer**. Each
is recorded below with the decision first and the original tradeoff analysis retained underneath as
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

## Next steps

With the three decisions above confirmed, the grid and turn-manager runtime scaffolding lands in
this same change: a `BeastCraft.Battle.Grid` namespace holding the arena-size presets, axial hex
coordinates and a hexagon-shaped board with occupancy tracking, plus a `BattleFormat` enum for the
Solo/4/6 party sizes, a minimal `BattleUnit`, and a speed-sorted `TurnManager`.

That pass is deliberately **data structures and algorithms only** — no MonoBehaviours, no scene or
prefab wiring, no AI, no damage or skill resolution, and no authored `.asset` instances. The hex
radii backing each arena preset are placeholder implementation defaults chosen to be tunable, not
producer-confirmed balance numbers. Subsequent passes pick up movement/pathfinding over the hex
grid, skill targeting against `SkillTargetShape`, the encounter definition that selects an arena
preset and a battle format, and the presentation layer.
