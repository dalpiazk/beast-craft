using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

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
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Only two shapes ever move.</strong> <see cref="SkillTargetShape.SingleTarget"/> and
    /// <see cref="SkillTargetShape.Line"/> pick a single focus and are therefore the only shapes
    /// with something concrete to walk toward, and the only ones that can end a turn stuck at 0.
    /// The other five (<see cref="SkillTargetShape.Self"/>,
    /// <see cref="SkillTargetShape.AllAllies"/>, <see cref="SkillTargetShape.AllEnemies"/>,
    /// <see cref="SkillTargetShape.Cross"/>, <see cref="SkillTargetShape.AreaBurst"/>) resolve from
    /// wherever the caster already stands, so they fire the moment they are ready and never consult
    /// the budget at all. A <c>Cross</c> or <c>AreaBurst</c> that catches nobody is a whiff, exactly
    /// as it was before this pass: it was never gated on reaching anyone, so it still fires and
    /// still re-arms.
    /// </para>
    /// <para>
    /// <strong>Still not built here.</strong> <c>SkillSO.ResourceCost</c> is not spent (deferred by
    /// the producer), no status-effect system exists for
    /// <see cref="SkillEffectType.ApplyStatus"/> to hang off, defeated units are not lifted off the
    /// grid, and nothing in this file is a MonoBehaviour or knows a scene exists. There is also no
    /// AI beyond the rule above: the unit does not reposition for safety, kite, spread out or retreat,
    /// because none of that has been designed.
    /// </para>
    /// </summary>
    public static class BattleTurnExecutor
    {
        /// <summary>
        /// The round at which <see cref="RunBattle"/> gives up and reports
        /// <see cref="BattleOutcome.Stalemate"/>.
        /// <para>
        /// <strong>A scaffold safety net, not a game rule.</strong> Nothing in the design says a
        /// battle ends after some number of rounds, and this must not be read as a timer that
        /// encounter balance is allowed to lean on. It exists because a battle genuinely can be
        /// unable to end — two units whose skills can never reach each other across blocked terrain,
        /// a loadout of nothing but out-of-range skills, or a pair of healers out-healing each
        /// other — and a loop that cannot end is a hang, not a gameplay outcome.
        /// </para>
        /// <para>
        /// Deliberately generous rather than tuned. A real fight resolves in a handful of rounds, so
        /// this is far out of the way of anything that is actually going to finish; picking a
        /// tighter number would be a balance decision, and this is not one.
        /// </para>
        /// </summary>
        public const int DefaultMaxRounds = 200;

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
        /// is left at 0 and no movement is attempted, and with one it either already reaches it, or
        /// walks the cheapest route to a tile that does — if the turn's remaining budget covers it.
        /// </description></item>
        /// <item><description>
        /// On a <see cref="BattleTeam.Player"/> unit's turn only, the avatar's loadout is ticked and
        /// resolved through <see cref="SkillLoadout.TickAndResolve"/>, per the confirmed rule that
        /// it ticks once per player-side beast turn. Its effects are applied too.
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

                HexCoordinate destination;
                int steps;
                if (!TryPlanApproach(unit, candidate, skill, grid, out destination, out steps))
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Unreachable, null, 0));
                    continue;
                }

                if (steps > remaining)
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.OutOfMovement, null, 0));
                    continue;
                }

                if (!TryMove(unit, grid, destination))
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Unreachable, null, 0));
                    continue;
                }

                remaining -= steps;
                outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, steps));
            }

            if (unit.Team == BattleTeam.Player && avatar != null && avatar.Skills != null)
            {
                IReadOnlyList<SkillActivation> cast = avatar.Skills.TickAndResolve(avatar, allUnits, grid, rng);

                for (int i = 0; i < cast.Count; i++)
                {
                    SkillEffectApplier.Apply(cast[i], avatar);
                    avatarActivations.Add(cast[i]);
                }
            }

            return new BattleTurnResult(unit, start, unit.Position, budget, budget - remaining, outcomes, avatarActivations);
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
        /// Looping on it would march the victors around an empty board for as long as the round cap
        /// allowed and then report a stalemate. So the win condition lives here, where the concept
        /// of a side belongs, and is exactly "do living units remain on more than one team". The two
        /// are not redundant and neither should be rewritten in terms of the other:
        /// <c>IsComplete</c> stays the guard on handing out turns, and this stays the guard on the
        /// battle continuing.
        /// </para>
        /// <para>
        /// <strong>The round cap.</strong> <paramref name="maxRounds"/> stops a battle that cannot
        /// end — see <see cref="DefaultMaxRounds"/>, which explains why it is a safety net against a
        /// hang rather than a designed time limit. It is tested at the top of each iteration against
        /// <see cref="TurnManager.Round"/>, so the battle plays rounds 1 through
        /// <paramref name="maxRounds"/> inclusive and stops as the next one opens.
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
        public static BattleResult RunBattle(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar, int maxRounds = DefaultMaxRounds)
        {
            List<BattleUnit> roster = CopyRoster(allUnits);
            List<BattleTurnResult> turns = new List<BattleTurnResult>();
            int cap = maxRounds < 1 ? 1 : maxRounds;
            BattleOutcome outcome;

            while (true)
            {
                if (TryConclude(roster, out outcome))
                {
                    break;
                }

                if (turnManager == null || turnManager.Round > cap)
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
                    turns.Add(ExecuteTurn(current, roster, grid, rng, avatar));
                }

                turnManager.AdvanceTurn();
            }

            return new BattleResult(outcome, turnManager == null ? 0 : turnManager.Round, turns);
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
        /// Resolves one slot from where the caster is standing right now, applies what it does, and
        /// re-arms the slot. The one place in a beast's turn that a cooldown is reset, so a skill
        /// cannot be marked fired without having actually resolved.
        /// </summary>
        private static BattleSkillOutcome Fire(SkillLoadout loadout, int slotIndex, SkillSO skill, BattleUnit caster, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, int movementSpent)
        {
            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(skill, caster, allUnits, grid, rng);
            SkillActivation activation = new SkillActivation(skill, targets);

            SkillEffectApplier.Apply(activation, caster);
            loadout.MarkFired(slotIndex);

            return new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.Fired, activation, movementSpent);
        }

        /// <summary>
        /// The cheapest walk that puts <paramref name="mover"/> within the skill's range of
        /// <paramref name="candidate"/>: the tile to stop on and how many steps it takes. False when
        /// no route reaches range at all.
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
        /// The search starts at index 1 of each route: index 0 is the tile the mover is already on,
        /// and that tile is known to be out of range because the caller checked before asking. A
        /// route that never comes within range — possible for a <c>Range</c> of 0, which wants a
        /// tile nothing can stand on — contributes nothing.
        /// </para>
        /// <para>
        /// The budget is deliberately not passed in. Whether the walk is affordable is the caller's
        /// decision because the answer depends on what earlier skills in the same turn already
        /// spent, and a plan that is too expensive this turn is still worth reporting as a plan
        /// rather than as "unreachable" — the two are different outcomes to the unit.
        /// </para>
        /// </summary>
        private static bool TryPlanApproach(BattleUnit mover, BattleUnit candidate, SkillSO skill, HexGrid grid, out HexCoordinate destination, out int steps)
        {
            destination = mover.Position;
            steps = 0;

            if (grid == null)
            {
                return false;
            }

            bool found = false;
            int bestSteps = int.MaxValue;
            HexCoordinate bestDestination = mover.Position;
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

                    if (step < bestSteps)
                    {
                        bestSteps = step;
                        bestDestination = path[step];
                        found = true;
                    }

                    break;
                }
            }

            if (!found)
            {
                return false;
            }

            destination = bestDestination;
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
