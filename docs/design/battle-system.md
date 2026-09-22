# Battle System Design Proposal

Beast Craft is a narrative story-adventure with creature-collection and battle progression. It
draws its gameplay-depth inspiration from Sword x Staff's class/promotion/build systems and its
idle progression, but reframes all of it around a narrative core rather than a live-service idle
grind: the story is the spine, and the systems exist to give that story mechanical weight. This
document proposes a **tactical grid-based combat** approach for that battle layer, describes how it
sits on top of the ScriptableObject data schemas already committed to the project, and flags the
specific decisions that still need producer confirmation before implementation starts. Nothing here
is built yet — this is a proposal to review and confirm, not a spec to implement.

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
is **grid-agnostic**: it remains valid and re-authorable whichever way the grid-shape and turn-model
questions below are resolved. No content authored against these schemas has to be thrown away by
those decisions.

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

## Open questions for confirmation

### 1. Grid shape and arena size

A **square grid** is simpler on every axis: tile art authors as a single repeated quad, movement and
line-of-sight use standard 4- or 8-directional rules, and pathfinding is textbook. A **hexagonal
grid** is more tactically interesting — flanking and positioning read better because there are no
"corner" adjacency edge cases — but it roughly doubles the tile-art workload and adds real
complexity to pathfinding, range rendering, and UI affordances for a first vertical slice. Separately:
is the battlefield a **fixed size across all encounters** (a single shared arena, cheapest to build
and balance), or does it **scale per encounter type** (boss arenas noticeably larger than random
encounters, which costs more layout authoring but gives set-piece fights their own identity)?

### 2. Active party size and the avatar's on-grid presence

How many creatures are actively deployed per side at once — a tight **3v3**, or something larger?
Party size drives almost everything downstream: encounter pacing, how much screen real estate the
grid needs on a phone, AI cost per turn, and how quickly a turn cycles. Smaller keeps each unit's
decision meaningful and readable on a small screen; larger allows more composition and role play at
the cost of turn length. Related: does the **avatar occupy a grid tile** as a non-attacking support
unit — physically present, able to spend its turn on consumables or avatar-specific commands, and
therefore positionable and possibly targetable — or does it stay **off-grid** as a menu-only
commander, issuing orders without ever being a piece on the board?

### 3. Turn order model

A **speed-stat initiative queue** uses `StatType.Speed`, which already exists in `StatBlock`: every
unit is sorted into a single turn order and acts individually on its own turn. This is the classic
JRPG/tactics pacing, it is the simplest thing to build, and it is by far the easiest to tutorialize —
the player always knows exactly whose turn it is and what happens next. A **simultaneous
declare-then-resolve** model instead has both sides choose all their units' actions up front each
round and then resolves them together. That is meaningfully more strategic (reads, baits, committed
positioning) but costs significantly more in UI, AI, and tutorial work, and it makes failure states
harder for a new player to parse. **Recommendation: start with the initiative-queue model for the
first playable slice**, and leave phase-based resolution open as a later evolution if playtesting
says the combat wants more depth.

## Next steps

Once these three questions are confirmed, the next implementation pass will scaffold the actual
grid and turn-manager runtime code. That work is explicitly out of scope for this document.
