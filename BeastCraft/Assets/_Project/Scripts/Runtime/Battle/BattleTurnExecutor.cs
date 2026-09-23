using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Drives a battle: what happens on one unit's turn, and the loop that takes every unit through
    /// its turns until one side is gone.
    /// <para>
    /// The integration layer every pass before this deliberately stopped short of.
    /// <see cref="SkillLoadout"/> knows cooldowns and nothing else,
    /// <see cref="SkillTargetResolver"/> knows targeting and nothing else,
    /// <see cref="SkillEffectApplier"/> knows state changes and nothing else,
    /// <see cref="HexPathfinder"/> knows routes and nothing else, and
    /// <see cref="TurnManager"/> knows whose turn it is and nothing else. Each of those refuses to
    /// look at the others on purpose, which leaves exactly one thing missing: something allowed to
    /// know about all of them at once and decide the order they run in. This is that thing, and it
    /// is the only type in the namespace that reaches across the board, the roster, the rotation and
    /// the effects together.
    /// </para>
    /// <para>
    /// <strong>The confirmed movement rule.</strong> Movement is spent <em>per skill, as needed</em>,
    /// not as an up-front "walk toward the nearest enemy" step:
    /// <list type="bullet">
    /// <item><description>
    /// A turn's movement budget is the unit's <see cref="BattleUnit.MoveRange"/>, and it is
    /// <em>shared across the whole turn</em>. Spending three quarters of it getting the first skill
    /// into range leaves the remaining quarter for everything after it.
    /// </description></item>
    /// <item><description>
    /// Every ready slot is attempted in stack order. A skill whose target is already in range fires
    /// from where the unit stands and spends nothing — a unit may well move not at all.
    /// </description></item>
    /// <item><description>
    /// A skill that comes off cooldown but cannot reach any eligible target, even after spending
    /// everything it has left, <strong>does not fire and does not reset</strong>. Its counter stays
    /// at 0 and it is offered again, with a fresh full budget, on this unit's next turn. That is why
    /// <see cref="SkillLoadout.Tick"/> no longer re-arms what it offers, and why
    /// <see cref="SkillLoadout.MarkFired"/> exists.
    /// </description></item>
    /// <item><description>
    /// <strong>Partial approach.</strong> When a route to within range exists but is longer than
    /// what is left of the budget, the unit still <em>advances</em>: it walks that same cheapest
    /// route (the one <see cref="TryPlanApproach"/> would have used, with the same tie-breaks) for
    /// exactly the steps it has left, spending all of them, and the slot is held as above
    /// (<see cref="BattleSkillStatus.OutOfMovement"/>: it does not fire, and its cooldown is not
    /// spent). Next turn it starts that much closer. Only a unit with <em>no</em> route at all
    /// (<see cref="BattleSkillStatus.Unreachable"/> — walled off by terrain or bodies, or no grid)
    /// stays where it is. Because the budget is shared and slots are attempted in stack order, the
    /// earliest held slot decides where the leftover movement goes; a later slot then has nothing
    /// left to walk with, but it is still attempted from the new tile and fires if its focus is now
    /// in range — and a shape that needs no approach fires regardless.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Only two shapes ever approach.</strong> <see cref="SkillTargetShape.SingleTarget"/> and
    /// <see cref="SkillTargetShape.Line"/> pick a single focus and are therefore the only shapes
    /// with something concrete to walk toward, and the only ones that can end a turn stuck at 0.
    /// The other five (<see cref="SkillTargetShape.Self"/>,
    /// <see cref="SkillTargetShape.AllAllies"/>, <see cref="SkillTargetShape.AllEnemies"/>,
    /// <see cref="SkillTargetShape.Cross"/>, <see cref="SkillTargetShape.AreaBurst"/>) resolve from
    /// wherever the caster already stands, so they fire the moment they are ready and never consult
    /// the budget at all. A <c>Cross</c> or <c>AreaBurst</c> that catches nobody is a whiff, exactly
    /// as it was before this pass: it was never gated on reaching anyone, so it still fires and
    /// still re-arms. (A stance retreat, below, is movement too, but it belongs to no skill.)
    /// </para>
    /// <para>
    /// <strong>Defeated units leave the grid.</strong> The moment a unit is defeated its tile is
    /// freed: after every beast skill that fires and every avatar activation that is applied, each
    /// defeated unit in the roster is lifted off the board with <see cref="HexGrid.RemoveUnit"/>
    /// (and once more as each turn opens, which catches a unit defeated by anything outside this
    /// type). So a later slot in the same turn, and every later turn, can path through or stand on
    /// the tile a fallen unit held. It happens here rather than in
    /// <see cref="SkillEffectApplier"/>, which deliberately has no board. The defeated unit
    /// <em>keeps</em> its <see cref="BattleUnit.Position"/> — the tile it fell on — for logs and
    /// results; that value is no longer backed by grid occupancy, and nothing reads it for play,
    /// because <see cref="SkillTargetResolver"/> only ever considers living units and
    /// <see cref="TurnManager"/> hands a defeated unit no turn. A null grid is tolerated as before:
    /// there is simply nothing to lift anything off.
    /// </para>
    /// <para>
    /// <strong>Combat stances.</strong> The rule above is the whole of a
    /// <see cref="CombatStance.Vanguard"/>'s movement (the default stance), and every stance keeps
    /// it: one budget, spent by slots in stack order. A unit's <see cref="BattleUnit.Stance"/> adds
    /// only this, all of it deterministic, integer and board-derived:
    /// <list type="bullet">
    /// <item><description>
    /// <strong>Where an approach stops</strong> among routes of equal length (never a longer one):
    /// a Vanguard ends the turn nearest its closest living Ranged or Skirmisher ally, screening
    /// it; a Ranged or Skirmisher unit prefers the in-range tile farthest from its target and then
    /// the one with the fewest enemies adjacent (anti-surround). See <see cref="TryPlanApproach"/>.
    /// </description></item>
    /// <item><description>
    /// <strong>A Ranged unit never walks into melee.</strong> An enemy-side picking slot with
    /// <c>Range &lt;= 1</c> fires if a target is already adjacent and otherwise holds without
    /// moving (<see cref="BattleSkillStatus.HeldByStance"/>), leaving the budget to later slots.
    /// </description></item>
    /// <item><description>
    /// <strong>Leftover budget.</strong> What is left once every ready slot has been attempted is
    /// discarded by a Vanguard, as it always was. A Ranged or Skirmisher unit spends it retreating:
    /// to the reachable tile farthest from the nearest living enemy that still keeps that enemy
    /// within its longest single-target reach, never closer than it already stands (see
    /// <see cref="Retreat"/>; reported as <see cref="BattleTurnResult.RetreatSteps"/>). It
    /// happens before the avatar's activations, and not at all for a unit that defeated itself.
    /// </description></item>
    /// </list>
    /// With a null grid nothing moves, whatever the stance.
    /// </para>
    /// <para>
    /// <strong>Still not built here.</strong> <c>SkillSO.ResourceCost</c> is not spent (deferred by
    /// the producer), no status-effect system exists for
    /// <see cref="SkillEffectType.ApplyStatus"/> to hang off, and nothing in this file is a
    /// MonoBehaviour or knows a scene exists. Beyond the stances there is no AI: a unit does not
    /// retreat when hurt, spread out against area skills, hold a choke point or coordinate a focus,
    /// because none of that has been designed.
    /// </para>
    /// </summary>
    public static class BattleTurnExecutor
    {
        /// <summary>
        /// The battle time, in normalized units (see <see cref="TurnManager.Time"/>), past which
        /// <see cref="RunBattle"/> gives up and reports <see cref="BattleOutcome.Stalemate"/>.
        /// <para>
        /// <strong>A scaffold safety net, not a game rule.</strong> Nothing in the design says a
        /// battle ends after some amount of time, and this must not be read as a timer that
        /// encounter balance is allowed to lean on. It exists because a battle genuinely can be
        /// unable to end — two units whose skills can never reach each other across blocked terrain,
        /// a loadout of nothing but out-of-range skills, or a pair of healers out-healing each
        /// other — and a loop that cannot end is a hang, not a gameplay outcome.
        /// </para>
        /// <para>
        /// Deliberately generous rather than tuned. It replaced a 200-<em>round</em> cap when turn
        /// order moved to the ATB gauge, and is sized to keep that net at every level: 2000 is 200
        /// turns of a Speed-10 unit (about the speed of a level-1 beast; Speed scales with level,
        /// and normalized time does not), and 2000 turns of a Speed-100 one. A real fight is over
        /// long before either, so this is far out of the way of anything that is actually going to
        /// finish; picking a tighter number would be a balance decision, and this is not one. An
        /// integer, so the cap converts to ticks exactly (<see cref="TurnManager.TicksPerTimeUnit"/>
        /// per unit) and the stopping point never depends on floating-point rounding.
        /// </para>
        /// </summary>
        public const int DefaultMaxTime = 2000;

        /// <summary>
        /// Runs one unit's whole turn: expire its timed modifiers, tick its rotation, then attempt
        /// every ready slot in stack order — moving toward a target when a skill needs it and the
        /// turn can still afford it — and finally tick the avatar if this was a player beast.
        /// <para>
        /// The steps, in order:
        /// <list type="number">
        /// <item><description>
        /// <see cref="SkillEffectApplier.TickModifiers"/> on the unit, since a timed buff is counted
        /// in the affected unit's <em>own</em> turns and this is one of them. Done first so a
        /// modifier with one turn left has already expired before this turn's skills read the stat,
        /// rather than lingering for one cast longer than it was authored to. That includes the
        /// movement budget: it is taken from <see cref="BattleUnit.MoveRange"/> only after this
        /// step, so an expiring move-range buff does not pay for one more turn of walking.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillLoadout.Tick"/>, which counts every slot down and reports the ones now at
        /// 0 without re-arming any of them.
        /// </description></item>
        /// <item><description>
        /// Each ready slot in turn. A shape that needs no approach resolves and fires immediately. A
        /// picking shape looks for its best candidate <em>ignoring range</em>
        /// (<see cref="SkillTargetResolver.PickFocusIgnoringRange"/>); with no candidate anywhere it
        /// is left at 0 and no movement is attempted, and with one it either already reaches it,
        /// walks the cheapest route to a tile that does if the turn's remaining budget covers it, or
        /// — when the budget falls short — advances along that route by what is left and holds.
        /// Every skill that fires is followed by lifting the newly defeated off the grid. A Ranged
        /// unit's melee slot with no adjacent target holds without walking.
        /// </description></item>
        /// <item><description>
        /// A Ranged or Skirmisher unit that is still standing spends any budget left over on a
        /// retreat (see the stance notes on this class). A Vanguard's leftover is discarded.
        /// </description></item>
        /// <item><description>
        /// On a <see cref="BattleTeam.Player"/> unit's turn only, the avatar's loadout is ticked and
        /// resolved through <see cref="SkillLoadout.TickAndResolve"/>, per the confirmed rule that
        /// it ticks once per player-side beast turn. Its effects are applied too. Under the ATB
        /// gauge this means a faster team also cycles its avatar faster; whether the avatar should
        /// instead fill a gauge of its own from its own Speed is an open design item (battle-system
        /// design doc, §6), and the rule is unchanged until it is decided.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// <strong>Whatever fires resolves from the position the unit is standing on at that
        /// moment</strong>, through the ordinary <see cref="SkillTargetResolver.ResolveTargets"/>.
        /// The candidate a move was aimed at is not assumed to be who gets hit: a <c>Line</c> sweeps
        /// its whole beam, and a re-ask from the new tile is free and is the single source of truth.
        /// </para>
        /// <para>
        /// <strong>A unit that defeats itself mid-turn stops there.</strong> An <c>Ally</c>-side
        /// <c>AreaBurst</c> catches its own caster, so this is reachable; the remaining ready slots
        /// are simply not attempted, and they keep their 0 like any other slot that did not fire.
        /// </para>
        /// <para>
        /// <strong>The rng.</strong> <paramref name="rng"/> is the battle's one random stream.
        /// Targeting draws from it (only for <see cref="SkillTargetingCriterion.Random"/>), and every
        /// damage effect that lands draws its crit roll then its variance roll from it, in the order
        /// the turn fires skills — the unit's ready slots in stack order, then the avatar's — and
        /// within each skill in <see cref="SkillEffectApplier"/>'s target-major, authored-effect
        /// order. A null rng is the deterministic fallback: no variance, no crits (see
        /// <see cref="DamageFormula"/>).
        /// </para>
        /// <para>
        /// Non-throwing, like the rest of the namespace: a null or defeated unit takes no turn at
        /// all (and, deliberately, does not tick — a unit out of the fight must not advance its own
        /// rotation behind its back), and a null grid simply means nothing can move.
        /// </para>
        /// </summary>
        public static BattleTurnResult ExecuteTurn(BattleUnit unit, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar)
        {
            List<BattleSkillOutcome> outcomes = new List<BattleSkillOutcome>();
            List<SkillActivation> avatarActivations = new List<SkillActivation>();

            if (unit == null || unit.IsDefeated)
            {
                HexCoordinate idle = unit == null ? HexCoordinate.Zero : unit.Position;
                return new BattleTurnResult(unit, idle, idle, 0, 0, outcomes, avatarActivations);
            }

            HexCoordinate start = unit.Position;

            // Anything defeated outside this type must not keep obstructing this turn's routes.
            LiftDefeated(allUnits, grid);

            SkillEffectApplier.TickModifiers(unit);

            // Read after the tick, not before: move range is a stat, so a move-range buff that
            // expires on this tick must already be gone from this turn's budget.
            int budget = unit.MoveRange < 0 ? 0 : unit.MoveRange;
            int remaining = budget;

            SkillLoadout loadout = unit.Skills;
            IReadOnlyList<int> ready = loadout == null ? new List<int>() : loadout.Tick();

            for (int i = 0; i < ready.Count; i++)
            {
                if (unit.IsDefeated)
                {
                    break;
                }

                int slotIndex = ready[i];
                SkillSO skill = loadout.Skills[slotIndex];

                if (skill == null)
                {
                    continue;
                }

                if (!NeedsApproach(skill.TargetShape))
                {
                    outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, 0));
                    continue;
                }

                BattleUnit candidate = SkillTargetResolver.PickFocusIgnoringRange(skill, unit, allUnits, rng);

                if (candidate == null)
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.NoCandidate, null, 0));
                    continue;
                }

                if (unit.Position.Distance(candidate.Position) <= skill.Range)
                {
                    outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, 0));
                    continue;
                }

                if (HoldsInsteadOfClosing(unit, skill))
                {
                    // A Ranged unit never walks into melee: the slot waits for a target to come to it.
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.HeldByStance, null, 0));
                    continue;
                }

                IReadOnlyList<HexCoordinate> route;
                int steps;
                if (!TryPlanApproach(unit, candidate, skill, allUnits, grid, remaining, out route, out steps))
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Unreachable, null, 0));
                    continue;
                }

                if (steps > remaining)
                {
                    // Partial approach: close the distance by whatever is left, then hold the slot.
                    int advanced = remaining > 0 && TryMove(unit, grid, route[remaining]) ? remaining : 0;
                    remaining -= advanced;
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.OutOfMovement, null, advanced));
                    continue;
                }

                if (!TryMove(unit, grid, route[steps]))
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Unreachable, null, 0));
                    continue;
                }

                remaining -= steps;
                outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, steps));
            }

            // Leftover budget: a Ranged or Skirmisher unit backs away; a Vanguard keeps its ground.
            int retreatSteps = 0;
            if (!unit.IsDefeated && remaining > 0 && RetreatsWithLeftover(unit.Stance))
            {
                retreatSteps = Retreat(unit, allUnits, grid, remaining);
                remaining -= retreatSteps;
            }

            if (unit.Team == BattleTeam.Player && avatar != null && avatar.Skills != null)
            {
                IReadOnlyList<SkillActivation> cast = avatar.Skills.TickAndResolve(avatar, allUnits, grid, rng);

                for (int i = 0; i < cast.Count; i++)
                {
                    SkillEffectApplier.Apply(cast[i], avatar, rng);
                    LiftDefeated(allUnits, grid);
                    avatarActivations.Add(cast[i]);
                }
            }

            return new BattleTurnResult(unit, start, unit.Position, budget, budget - remaining, outcomes, avatarActivations, retreatSteps);
        }

        /// <summary>
        /// Plays the battle out: take <see cref="TurnManager.CurrentUnit"/>, run its turn, advance,
        /// and keep going until one side is gone.
        /// <para>
        /// <strong>Why the loop does not stop on <see cref="TurnManager.IsComplete"/>.</strong> That
        /// property answers a different question than this loop is asking. It is true once
        /// <em>every</em> unit in the roster is defeated — it is the turn manager saying "there is
        /// nobody left to hand a turn to", which is all the turn manager has any business knowing.
        /// A battle, though, is over the moment one <em>side</em> is wiped out, and at that moment
        /// the winners are all still standing, so <c>IsComplete</c> is emphatically <c>false</c>.
        /// Looping on it would march the victors around an empty board for as long as the time cap
        /// allowed and then report a stalemate. So the win condition lives here, where the concept
        /// of a side belongs, and is exactly "do living units remain on more than one team". The two
        /// are not redundant and neither should be rewritten in terms of the other:
        /// <c>IsComplete</c> stays the guard on handing out turns, and this stays the guard on the
        /// battle continuing.
        /// </para>
        /// <para>
        /// <strong>The time cap.</strong> <paramref name="maxTime"/> stops a battle that cannot
        /// end — see <see cref="DefaultMaxTime"/>, which explains why it is a safety net against a
        /// hang rather than a designed time limit. It is tested at the top of each iteration against
        /// <see cref="TurnManager.ElapsedTicks"/> (the moment the next turn would be taken), so every
        /// turn that falls at or before <paramref name="maxTime"/> is played and the battle stops at
        /// the first one scheduled after it. Values below 1 are treated as 1.
        /// </para>
        /// <para>
        /// <strong>What the result reports.</strong> <see cref="BattleResult.ElapsedTicks"/> is the
        /// time of the last turn actually executed — for a decided battle, the turn that decided it —
        /// rather than wherever the turn manager had advanced to when the loop noticed, which would
        /// overshoot by one wait.
        /// </para>
        /// <para>
        /// <paramref name="allUnits"/> is the roster: the beasts on both sides, and not the avatar,
        /// which is a caster only (see <see cref="BattleAvatar"/>). It is copied once up front, so
        /// the win check and every turn's targeting read the same list, and the caller's collection
        /// is not retained. It is expected to hold the same units <paramref name="turnManager"/> was
        /// built from; if it does not, the loop still terminates — a turn order that runs dry while
        /// both sides look alive reports <see cref="BattleOutcome.Stalemate"/> rather than spinning.
        /// </para>
        /// <para>
        /// Non-throwing: a null turn manager or an empty roster returns a result rather than
        /// failing.
        /// </para>
        /// </summary>
        public static BattleResult RunBattle(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar, int maxTime = DefaultMaxTime)
        {
            List<BattleUnit> roster = CopyRoster(allUnits);
            List<BattleTurnResult> turns = new List<BattleTurnResult>();
            long capTicks = (long)(maxTime < 1 ? 1 : maxTime) * TurnManager.TicksPerTimeUnit;
            long lastTurnTicks = 0;
            BattleOutcome outcome;

            while (true)
            {
                if (TryConclude(roster, out outcome))
                {
                    break;
                }

                if (turnManager == null || turnManager.ElapsedTicks > capTicks)
                {
                    outcome = BattleOutcome.Stalemate;
                    break;
                }

                BattleUnit current = turnManager.CurrentUnit;

                if (current == null)
                {
                    outcome = BattleOutcome.Stalemate;
                    break;
                }

                if (!current.IsDefeated)
                {
                    lastTurnTicks = turnManager.ElapsedTicks;
                    turns.Add(ExecuteTurn(current, roster, grid, rng, avatar));
                }

                turnManager.AdvanceTurn();
            }

            return new BattleResult(outcome, lastTurnTicks, turns);
        }

        /// <summary>
        /// Whether a shape has a single focus target it could walk toward, and therefore whether a
        /// skill of that shape consults the movement budget at all. True only for
        /// <see cref="SkillTargetShape.SingleTarget"/> and <see cref="SkillTargetShape.Line"/>,
        /// which are exactly the two shapes <see cref="SkillTargetResolver"/> picks a focus for.
        /// <para>
        /// Written as an allow-list of the two rather than a deny-list of the other five so that a
        /// shape added to the enum later defaults to the safe behaviour — firing from where the
        /// caster stands, as every shape did before this pass — instead of silently becoming
        /// something units try to chase with a focus the resolver never picks for it.
        /// </para>
        /// </summary>
        private static bool NeedsApproach(SkillTargetShape shape)
        {
            return shape == SkillTargetShape.SingleTarget || shape == SkillTargetShape.Line;
        }

        /// <summary>
        /// Resolves one slot from where the caster is standing right now, applies what it does,
        /// lifts anyone it defeated (the caster included) off the grid, and re-arms the slot. The
        /// one place in a beast's turn that a cooldown is reset, so a skill cannot be marked fired
        /// without having actually resolved.
        /// </summary>
        private static BattleSkillOutcome Fire(SkillLoadout loadout, int slotIndex, SkillSO skill, BattleUnit caster, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, int movementSpent)
        {
            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(skill, caster, allUnits, grid, rng);
            SkillActivation activation = new SkillActivation(skill, targets);

            SkillEffectApplier.Apply(activation, caster, rng);
            LiftDefeated(allUnits, grid);
            loadout.MarkFired(slotIndex);

            return new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Fired, activation, movementSpent);
        }

        /// <summary>
        /// The cheapest walk that puts <paramref name="mover"/> within the skill's range of
        /// <paramref name="candidate"/>: the route (start tile first) and how many steps along it the
        /// first in-range tile is, so <c>route[steps]</c> is the tile to stop on. False when no route
        /// reaches range at all. The whole route is returned rather than just the stop because a
        /// partial approach walks a prefix of it.
        /// <para>
        /// <strong>Why it does not simply path to the candidate's tile.</strong> That tile is
        /// occupied — by the candidate — and <see cref="HexGrid.IsPassable"/> makes every other
        /// unit an obstruction, so <see cref="HexPathfinder.FindPath"/> to it returns an empty path
        /// every single time. Pathing at the candidate and stopping short is therefore not something
        /// that can be expressed as one search. What this does instead is aim at each of the tiles
        /// <em>adjacent</em> to the candidate (plus the candidate's own tile, for the case where
        /// nothing is standing on it — a roster that was never placed on the grid), walk each
        /// resulting route outward, and keep the first tile on it that is within range. The best of
        /// those is the answer.
        /// </para>
        /// <para>
        /// All seven goals are evaluated rather than the first that works, because the first one
        /// that happens to produce a path can be a long way around an obstacle that a different
        /// approach walks straight into range of. Ties keep the earlier goal — the candidate's tile,
        /// then its neighbours in <see cref="HexCoordinate.AxialDirections"/>' fixed order — which
        /// is what makes the route a pure function of the board, the same determinism rule the
        /// pathfinder and the resolver already follow.
        /// </para>
        /// <para>
        /// <strong>Stance tie-breaks</strong> (see <see cref="CombatStance"/>). The number of steps
        /// always decides first: a stance never makes a unit walk further to reach range.
        /// <list type="bullet">
        /// <item><description>
        /// <see cref="CombatStance.Vanguard"/> — among goals that reach range in the same number of
        /// steps, the one whose route ends this turn (at the in-range tile, or where a partial
        /// approach runs out of <paramref name="remaining"/>) nearest the mover's closest living
        /// <see cref="CombatStance.Ranged"/> or <see cref="CombatStance.Skirmisher"/> ally wins,
        /// then the earlier goal. With no such ally there is nothing to compare and the result is
        /// exactly the plain rule's. See <see cref="ScreeningDistance"/>.
        /// </description></item>
        /// <item><description>
        /// <see cref="CombatStance.Ranged"/> and <see cref="CombatStance.Skirmisher"/> — the plain
        /// plan is refined by <see cref="TryPickStandoffTile"/>, which looks at <em>every</em>
        /// in-range tile the mover can reach at that lowest cost (not only the ones on the seven
        /// goal routes) and prefers, in order, the one farthest from the candidate, then the one
        /// with the fewest living enemies next to it, then the plain plan's own stop tile, then
        /// <see cref="HexGrid.GetTilesInRange"/>' fixed order.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// The search starts at index 1 of each route: index 0 is the tile the mover is already on,
        /// and that tile is known to be out of range because the caller checked before asking. A
        /// route that never comes within range — possible for a <c>Range</c> of 0, which wants a
        /// tile nothing can stand on — contributes nothing.
        /// </para>
        /// <para>
        /// The budget decides nothing here — <paramref name="remaining"/> is read only to know which
        /// tile a Vanguard's partial approach would stop on. Whether the walk is affordable is the
        /// caller's decision because the answer depends on what earlier skills in the same turn
        /// already spent, and a plan that is too expensive this turn is still worth reporting as a
        /// plan rather than as "unreachable" — the two are different outcomes to the unit: an
        /// unaffordable plan is still walked part of the way, an unreachable one not at all.
        /// </para>
        /// </summary>
        private static bool TryPlanApproach(BattleUnit mover, BattleUnit candidate, SkillSO skill, IEnumerable<BattleUnit> allUnits, HexGrid grid,
                                            int remaining, out IReadOnlyList<HexCoordinate> route, out int steps)
        {
            route = null;
            steps = 0;

            if (grid == null)
            {
                return false;
            }

            List<BattleUnit> screened = mover.Stance == CombatStance.Vanguard ? FragileAllies(mover, allUnits) : null;
            bool screening = screened != null && screened.Count > 0;

            bool found = false;
            int bestSteps = int.MaxValue;
            int bestScreen = int.MaxValue;
            IReadOnlyList<HexCoordinate> bestRoute = null;
            List<HexCoordinate> goals = ApproachGoals(grid, mover, candidate);

            for (int g = 0; g < goals.Count; g++)
            {
                IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, mover.Position, goals[g], mover.Id);

                for (int step = 1; step < path.Count; step++)
                {
                    if (path[step].Distance(candidate.Position) > skill.Range)
                    {
                        continue;
                    }

                    int screen = screening ? ScreeningDistance(path[Math.Min(step, Math.Max(remaining, 0))], screened) : 0;

                    if (step < bestSteps || (step == bestSteps && screen < bestScreen))
                    {
                        bestSteps = step;
                        bestScreen = screen;
                        bestRoute = path;
                        found = true;
                    }

                    break;
                }
            }

            if (mover.Stance == CombatStance.Ranged || mover.Stance == CombatStance.Skirmisher)
            {
                HexCoordinate? plainStop = found ? bestRoute[bestSteps] : (HexCoordinate?)null;
                HexCoordinate standoff;

                if (TryPickStandoffTile(mover, candidate, skill, allUnits, grid, plainStop, out standoff)
                    && (!plainStop.HasValue || standoff != plainStop.Value))
                {
                    IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, mover.Position, standoff, mover.Id);

                    if (path.Count > 1)
                    {
                        bestRoute = path;
                        bestSteps = path.Count - 1;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            route = bestRoute;
            steps = bestSteps;
            return true;
        }

        /// <summary>
        /// The tiles worth aiming a route at when closing on a candidate: the candidate's own tile
        /// when nothing stands on it, then each of its six neighbours, in
        /// <see cref="HexCoordinate.AxialDirections"/>' fixed order. Impassable goals are dropped
        /// here rather than searched for and failed on.
        /// </summary>
        private static List<HexCoordinate> ApproachGoals(HexGrid grid, BattleUnit mover, BattleUnit candidate)
        {
            List<HexCoordinate> goals = new List<HexCoordinate>();

            if (grid.IsPassable(candidate.Position, mover.Id))
            {
                goals.Add(candidate.Position);
            }

            IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;

            for (int i = 0; i < directions.Count; i++)
            {
                HexCoordinate tile = candidate.Position + directions[i];

                if (grid.IsPassable(tile, mover.Id))
                {
                    goals.Add(tile);
                }
            }

            return goals;
        }

        /// <summary>
        /// The approach stop a <see cref="CombatStance.Ranged"/> or
        /// <see cref="CombatStance.Skirmisher"/> unit prefers: of every tile within the skill's range
        /// of <paramref name="candidate"/> that the mover can walk to, the one it reaches in the
        /// fewest steps; among those, the one <em>farthest</em> from the candidate; then the one with
        /// the fewest living enemies adjacent to it (the anti-surround preference); then
        /// <paramref name="plainStop"/>, the plain rule's own pick, so that when nothing separates
        /// them the stance changes nothing; then the first in <see cref="HexGrid.GetTilesInRange"/>'
        /// fixed order. False when no in-range tile is reachable at all.
        /// <para>
        /// <strong>Farthest, not nearest.</strong> A standoff unit wants to stand at the edge of its
        /// reach, not step closer than it must. Because the caller only asks when the mover is out of
        /// range, and one hex step changes a distance by at most one, the cheapest in-range tiles are
        /// in practice always at exactly <c>Range</c> — so the key is a guarantee written down, not
        /// usually a choice. The steps still come first: a stance never walks further to reach range.
        /// </para>
        /// <para>
        /// Costs come from <see cref="ReachableTiles"/>, a breadth-first search over the same tiles
        /// <see cref="HexPathfinder"/> may use, so a tile's cost here is exactly the length of the
        /// route the pathfinder then returns to it.
        /// </para>
        /// </summary>
        private static bool TryPickStandoffTile(BattleUnit mover, BattleUnit candidate, SkillSO skill, IEnumerable<BattleUnit> allUnits, HexGrid grid,
                                                HexCoordinate? plainStop, out HexCoordinate pick)
        {
            pick = mover.Position;
            Dictionary<HexCoordinate, int> costs = ReachableTiles(grid, mover, int.MaxValue, null);
            List<BattleUnit> enemies = LivingEnemies(mover, allUnits);
            IReadOnlyList<HexCoordinate> tiles = grid.GetTilesInRange(candidate.Position, skill.Range);

            bool found = false;
            int bestCost = 0;
            int bestDistance = 0;
            int bestCrowd = 0;
            bool bestIsPlain = false;

            for (int i = 0; i < tiles.Count; i++)
            {
                HexCoordinate tile = tiles[i];
                int cost;

                if (tile == mover.Position || !costs.TryGetValue(tile, out cost))
                {
                    continue;
                }

                int distance = tile.Distance(candidate.Position);
                int crowd = AdjacentCount(tile, enemies);
                bool isPlain = plainStop.HasValue && plainStop.Value == tile;

                bool better = !found
                    || cost < bestCost
                    || (cost == bestCost && distance > bestDistance)
                    || (cost == bestCost && distance == bestDistance && crowd < bestCrowd)
                    || (cost == bestCost && distance == bestDistance && crowd == bestCrowd && isPlain && !bestIsPlain);

                if (better)
                {
                    found = true;
                    pick = tile;
                    bestCost = cost;
                    bestDistance = distance;
                    bestCrowd = crowd;
                    bestIsPlain = isPlain;
                }
            }

            return found;
        }

        /// <summary>
        /// Spends a <see cref="CombatStance.Ranged"/> or <see cref="CombatStance.Skirmisher"/>
        /// unit's leftover movement, once every ready slot has been attempted, on backing away.
        /// Returns the steps walked (0 when it stays).
        /// <para>
        /// The destination is the tile, among those reachable within <paramref name="budget"/>
        /// steps (the unit's own tile included, at cost 0), that is <strong>farthest from the
        /// nearest living enemy</strong> — but only counting tiles where that distance is still at
        /// most the unit's longest enemy-side <see cref="SkillTargetShape.SingleTarget"/> or
        /// <see cref="SkillTargetShape.Line"/> range (<see cref="LongestPickingRange"/>), so the unit
        /// can fire again next turn without moving. Ties go to the fewest adjacent living enemies,
        /// then the fewest steps (so a unit already on a best tile stays put), then breadth-first
        /// discovery order, which is fixed by <see cref="HexCoordinate.AxialDirections"/>.
        /// </para>
        /// <para>
        /// <strong>It never closes distance.</strong> The unit's own tile is always in the running,
        /// whatever its distance, and nearest-enemy distance is the first key, so no tile nearer the
        /// enemy can beat it: a unit already beyond its reach (say, one whose approach fell short)
        /// stays where it is, and approaching remains the skills' job alone. Nothing happens with
        /// no grid, no enemies left, or no enemy-side picking skill to keep in range of.
        /// </para>
        /// </summary>
        private static int Retreat(BattleUnit unit, IEnumerable<BattleUnit> allUnits, HexGrid grid, int budget)
        {
            if (grid == null || budget <= 0)
            {
                return 0;
            }

            int cap = LongestPickingRange(unit.Skills);
            List<BattleUnit> enemies = LivingEnemies(unit, allUnits);

            if (cap < 1 || enemies.Count == 0)
            {
                return 0;
            }

            List<HexCoordinate> order = new List<HexCoordinate>();
            Dictionary<HexCoordinate, int> costs = ReachableTiles(grid, unit, budget, order);

            HexCoordinate best = unit.Position;
            int bestNearest = NearestDistance(best, enemies);
            int bestCrowd = AdjacentCount(best, enemies);
            int bestCost = 0;

            for (int i = 0; i < order.Count; i++)
            {
                HexCoordinate tile = order[i];

                if (tile == unit.Position)
                {
                    continue;
                }

                int nearest = NearestDistance(tile, enemies);

                if (nearest > cap)
                {
                    continue;
                }

                int crowd = AdjacentCount(tile, enemies);
                int cost = costs[tile];

                bool better = nearest > bestNearest
                    || (nearest == bestNearest && crowd < bestCrowd)
                    || (nearest == bestNearest && crowd == bestCrowd && cost < bestCost);

                if (better)
                {
                    best = tile;
                    bestNearest = nearest;
                    bestCrowd = crowd;
                    bestCost = cost;
                }
            }

            if (best == unit.Position || !TryMove(unit, grid, best))
            {
                return 0;
            }

            return bestCost;
        }

        /// <summary>
        /// Whether a unit declines to walk toward an out-of-range candidate for this skill: true
        /// only for a <see cref="CombatStance.Ranged"/> unit and an enemy-side skill with
        /// <c>Range &lt;= 1</c>. An ally-side skill is exempt — stepping next to a friend is not
        /// walking into melee.
        /// </summary>
        private static bool HoldsInsteadOfClosing(BattleUnit unit, SkillSO skill)
        {
            return unit.Stance == CombatStance.Ranged && skill.TargetSide == SkillTargetSide.Enemy && skill.Range <= 1;
        }

        /// <summary>Whether a stance spends leftover movement retreating: Ranged and Skirmisher do, Vanguard does not.</summary>
        private static bool RetreatsWithLeftover(CombatStance stance)
        {
            return stance == CombatStance.Ranged || stance == CombatStance.Skirmisher;
        }

        /// <summary>
        /// Every tile <paramref name="mover"/> can walk to in at most <paramref name="maxSteps"/>
        /// steps, with its step cost, the mover's own tile included at 0. Breadth-first over
        /// <see cref="HexGrid.IsPassable"/> tiles (other units obstruct, as they do for the
        /// pathfinder), expanding neighbours in <see cref="HexCoordinate.AxialDirections"/>' fixed
        /// order; when <paramref name="order"/> is given it receives the tiles in discovery order.
        /// </summary>
        private static Dictionary<HexCoordinate, int> ReachableTiles(HexGrid grid, BattleUnit mover, int maxSteps, List<HexCoordinate> order)
        {
            Dictionary<HexCoordinate, int> costs = new Dictionary<HexCoordinate, int>();
            Queue<HexCoordinate> frontier = new Queue<HexCoordinate>();
            IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;

            costs[mover.Position] = 0;
            frontier.Enqueue(mover.Position);
            order?.Add(mover.Position);

            while (frontier.Count > 0)
            {
                HexCoordinate current = frontier.Dequeue();
                int cost = costs[current];

                if (cost >= maxSteps)
                {
                    continue;
                }

                for (int i = 0; i < directions.Count; i++)
                {
                    HexCoordinate next = current + directions[i];

                    if (costs.ContainsKey(next) || !grid.IsPassable(next, mover.Id))
                    {
                        continue;
                    }

                    costs[next] = cost + 1;
                    frontier.Enqueue(next);
                    order?.Add(next);
                }
            }

            return costs;
        }

        /// <summary>
        /// The longest <c>Range</c> among the loadout's enemy-side
        /// <see cref="SkillTargetShape.SingleTarget"/> and <see cref="SkillTargetShape.Line"/>
        /// skills, whatever their cooldown: the reach a retreating unit keeps its nearest enemy
        /// inside. 0 when it has none.
        /// </summary>
        private static int LongestPickingRange(SkillLoadout loadout)
        {
            int longest = 0;

            if (loadout == null)
            {
                return longest;
            }

            for (int i = 0; i < loadout.Skills.Count; i++)
            {
                SkillSO skill = loadout.Skills[i];

                if (skill != null && NeedsApproach(skill.TargetShape) && skill.TargetSide == SkillTargetSide.Enemy && skill.Range > longest)
                {
                    longest = skill.Range;
                }
            }

            return longest;
        }

        /// <summary>The living units on the other team from <paramref name="unit"/>, in roster order.</summary>
        private static List<BattleUnit> LivingEnemies(BattleUnit unit, IEnumerable<BattleUnit> allUnits)
        {
            List<BattleUnit> enemies = new List<BattleUnit>();

            if (allUnits == null)
            {
                return enemies;
            }

            foreach (BattleUnit other in allUnits)
            {
                if (other != null && !other.IsDefeated && other.Team != unit.Team)
                {
                    enemies.Add(other);
                }
            }

            return enemies;
        }

        /// <summary>
        /// The living allies a <see cref="CombatStance.Vanguard"/> screens: every living unit on its
        /// team, other than itself, whose stance is <see cref="CombatStance.Ranged"/> or
        /// <see cref="CombatStance.Skirmisher"/>.
        /// </summary>
        private static List<BattleUnit> FragileAllies(BattleUnit unit, IEnumerable<BattleUnit> allUnits)
        {
            List<BattleUnit> allies = new List<BattleUnit>();

            if (allUnits == null)
            {
                return allies;
            }

            foreach (BattleUnit other in allUnits)
            {
                if (other != null && other != unit && !other.IsDefeated && other.Team == unit.Team && other.Stance != CombatStance.Vanguard)
                {
                    allies.Add(other);
                }
            }

            return allies;
        }

        /// <summary>
        /// The Vanguard screening score of a tile: its hex distance to the nearest of
        /// <paramref name="allies"/>, lower being better. Deliberately the simplest deterministic
        /// reading of "stand in front of the back line": among equally short approaches, finish
        /// the turn as close as possible to a fragile team-mate, which on a board where both sides
        /// close head-on puts the Vanguard on that team-mate's side of the fight.
        /// </summary>
        private static int ScreeningDistance(HexCoordinate tile, List<BattleUnit> allies)
        {
            return NearestDistance(tile, allies);
        }

        /// <summary>The hex distance from a tile to the nearest of the given units (<c>int.MaxValue</c> when there are none).</summary>
        private static int NearestDistance(HexCoordinate tile, List<BattleUnit> units)
        {
            int nearest = int.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                int distance = tile.Distance(units[i].Position);

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        /// <summary>How many of the given units stand on a tile adjacent to <paramref name="tile"/>.</summary>
        private static int AdjacentCount(HexCoordinate tile, List<BattleUnit> units)
        {
            int count = 0;

            for (int i = 0; i < units.Count; i++)
            {
                if (tile.Distance(units[i].Position) == 1)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Moves a unit, writing both halves of its position: the grid's occupancy record first,
        /// then <see cref="BattleUnit.Position"/> only if the grid accepted it.
        /// <para>
        /// This is the one place in the codebase that moves anything, and the order matters.
        /// <see cref="BattleUnit.Position"/>'s own documentation names the grid as the authority on
        /// which tile is taken, so the grid is asked first and its answer is what decides: a
        /// rejected placement leaves the unit exactly where it was, with both halves still agreeing,
        /// rather than teleporting a unit whose tile the board does not think it holds.
        /// </para>
        /// </summary>
        private static bool TryMove(BattleUnit unit, HexGrid grid, HexCoordinate destination)
        {
            if (!grid.TryPlaceUnit(unit.Id, destination))
            {
                return false;
            }

            unit.Position = destination;
            return true;
        }

        /// <summary>
        /// Takes every defeated unit in the roster off the board, freeing its tile for movement and
        /// placement. Idempotent and cheap: a unit the grid does not hold (already lifted, or never
        /// placed) is a no-op in <see cref="HexGrid.RemoveUnit"/>. <see cref="BattleUnit.Position"/>
        /// is deliberately left alone, so a defeated unit still says where it fell. A null grid or
        /// roster lifts nothing.
        /// </summary>
        private static void LiftDefeated(IEnumerable<BattleUnit> allUnits, HexGrid grid)
        {
            if (grid == null || allUnits == null)
            {
                return;
            }

            foreach (BattleUnit unit in allUnits)
            {
                if (unit != null && unit.IsDefeated)
                {
                    grid.RemoveUnit(unit.Id);
                }
            }
        }

        /// <summary>
        /// Whether the battle is over, and how. Over means living units remain on fewer than two
        /// teams: one team left wins, and no team left is a mutual defeat (which an empty roster
        /// also reports). See <see cref="RunBattle"/> for why this is not
        /// <see cref="TurnManager.IsComplete"/>.
        /// </summary>
        private static bool TryConclude(List<BattleUnit> roster, out BattleOutcome outcome)
        {
            bool player = false;
            bool enemy = false;

            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].IsDefeated)
                {
                    continue;
                }

                if (roster[i].Team == BattleTeam.Player)
                {
                    player = true;
                }
                else
                {
                    enemy = true;
                }
            }

            if (player && enemy)
            {
                outcome = BattleOutcome.Stalemate;
                return false;
            }

            outcome = player
                ? BattleOutcome.PlayerVictory
                : enemy ? BattleOutcome.EnemyVictory : BattleOutcome.MutualDefeat;

            return true;
        }

        /// <summary>
        /// A private copy of the roster with null entries dropped, matching
        /// <see cref="TurnManager"/>'s own handling. Taken once so that the win check and every
        /// turn's targeting see the same list, and so a caller's collection is neither retained nor
        /// re-enumerated once per skill for the length of a battle.
        /// </summary>
        private static List<BattleUnit> CopyRoster(IEnumerable<BattleUnit> allUnits)
        {
            List<BattleUnit> roster = new List<BattleUnit>();

            if (allUnits == null)
            {
                return roster;
            }

            foreach (BattleUnit unit in allUnits)
            {
                if (unit != null)
                {
                    roster.Add(unit);
                }
            }

            return roster;
        }
    }
}
