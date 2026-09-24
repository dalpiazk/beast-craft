using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
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
    /// does not happen at all for a unit that defeated itself.
    /// </description></item>
    /// </list>
    /// With a null grid nothing moves, whatever the stance.
    /// </para>
    /// <para>
    /// <strong>Large units</strong> (see <see cref="UnitFootprint"/>). A unit that covers several
    /// tiles is still one unit with one anchor (<see cref="BattleUnit.Position"/>), and every rule
    /// above holds for it with distances measured between nearest tiles
    /// (<see cref="FootprintMath"/>). A one-tile unit closing on a large one aims at the tiles
    /// around its whole footprint (see <see cref="ApproachGoals"/>) and a standoff unit keeps its
    /// distance from the nearest of them. A large unit moves by anchor: its whole footprint must fit
    /// at every step (<see cref="HexGrid.CanStand"/>), it approaches by a breadth-first search for
    /// the cheapest anchor in range (see <see cref="TryPlanLargeApproach"/>), and it counts each
    /// enemy next to any of its tiles once when it weighs crowding. A battle of one-tile units runs
    /// exactly the rules it always did.
    /// </para>
    /// <para>
    /// <strong>Statuses.</strong> The executor frames every turn with the status engine
    /// (<see cref="StatusEffects.BeginTurn"/> as it opens, <see cref="StatusEffects.EndTurn"/> as
    /// it closes): damage-over-time lands first and can end the turn, and a
    /// <see cref="StatusType.Stun"/> skips it. A <see cref="StatusType.Taunt"/> needs no code
    /// here — <see cref="SkillTargetResolver"/> makes the taunter the focus, so the ordinary
    /// approach walks toward it — and a <see cref="StatusType.Knockback"/> moves its target
    /// through the grid this passes to <see cref="SkillEffectApplier"/>.
    /// </para>
    /// <para>
    /// <strong>Still not built here.</strong> <c>SkillSO.ResourceCost</c> is not spent (deferred by
    /// the producer), and nothing in this file is a MonoBehaviour or knows a scene exists. Beyond the stances there is no AI: a unit does not
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
        /// turn can still afford it. The avatar is not ticked here: it takes turns of its own
        /// (<see cref="ExecuteAvatarTurn"/>), and handed the avatar as <paramref name="unit"/> this
        /// runs exactly that.
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
        /// <see cref="StatusEffects.BeginTurn"/>: this turn is counted off every status the unit
        /// carries and its damage-over-time stacks deal their damage (shield first). A unit they
        /// defeat stops here — no movement, no skills, since it took no turn.
        /// A unit that began the turn <see cref="StatusType.Stun">stunned</see> skips the next three
        /// steps: it does not move, fires nothing, and its cooldowns do <em>not</em> tick (a stun
        /// delays the rotation rather than burning it). Its gauge was spent as normal by
        /// <see cref="TurnManager"/>.
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
        /// <see cref="StatusEffects.EndTurn"/>: statuses with no turns left come off.
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
        /// Targeting draws from it (only for <see cref="SkillTargetingCriterion.Random"/>, and not at
        /// all when a taunt decides the pick), every damage hit that lands draws its crit roll then
        /// its variance roll from it, and a non-damage effect whose chance is below 100 draws its
        /// chance check (see <see cref="SkillEffectApplier"/>), all in the order
        /// the turn fires skills — the unit's ready slots in stack order — and
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
            return ExecuteTurn(unit, allUnits, grid, rng, avatar, null);
        }

        /// <summary>
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit)"/>
        /// with the avatar's equipped passives (see <see cref="PassiveLoadout"/> and the design doc,
        /// "Avatar passives"). With a null or empty loadout, or no avatar, it is exactly the
        /// passive-free turn. Otherwise the passive hooks run at these points, and everything they
        /// fire is recorded on <see cref="BattleTurnResult.PassiveActivations"/>:
        /// <list type="bullet">
        /// <item><description>
        /// If the loadout has not begun (a caller driving turns itself without
        /// <see cref="BeginBattle"/>), its battle-start hook runs first, as the turn opens.
        /// </description></item>
        /// <item><description>
        /// After <see cref="StatusEffects.BeginTurn"/>: the after-damage hook (the unit's own
        /// damage-over-time may have defeated it or dropped it below a threshold). Then, on a
        /// player beast's turn that it survived, stunned or not, the
        /// <see cref="BeastCraft.Avatar.PassiveTrigger.AllyTurnStart"/> passives, before any of its
        /// skills.
        /// </description></item>
        /// <item><description>
        /// After every skill the unit fires: the after-damage hook, with that activation's hits for
        /// <see cref="BeastCraft.Avatar.PassiveTrigger.AllyCrit"/>.
        /// </description></item>
        /// <item><description>
        /// On the avatar's own turn (<see cref="ExecuteAvatarTurn"/>): every passive's internal
        /// cooldown ticks once, before the avatar's actives; then the after-damage hook after each
        /// avatar activation (its crits are not ally crits).
        /// </description></item>
        /// </list>
        /// A passive whose proc chance is below 100 draws once from <paramref name="rng"/> for its
        /// roll, at the moment it is tried; nothing else about a passive draws, beyond what its
        /// effects draw as any skill's would.
        /// </summary>
        public static BattleTurnResult ExecuteTurn(BattleUnit unit, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                                   PassiveLoadout passives)
        {
            return ExecuteTurn(unit, allUnits, grid, rng, avatar, passives, null);
        }

        /// <summary>
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>
        /// with the player team's bonds, whose behaviour bonds react during the turn (see
        /// <see cref="TeamBondLoadout"/> and <see cref="BondReaction"/>); what they did is recorded on
        /// <see cref="BattleTurnResult.BondReactions"/>. With a null bond loadout, one not yet applied
        /// (<see cref="BeginBattle(IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, TeamBondLoadout, out IReadOnlyList{TeamBondActivation})"/>),
        /// or one with no reacting tier, it is exactly the bond-free turn, random draws included.
        /// Otherwise the bond hooks run at these points, each after the passive hook at the same point:
        /// <list type="bullet">
        /// <item><description>
        /// As a beast of the team's turn opens, after its timed modifiers tick and before its statuses
        /// do: its reaction cooldowns count down, and an ally stunned or burning may be cleansed (so
        /// it acts this turn and takes no damage-over-time).
        /// </description></item>
        /// <item><description>
        /// Between resolving a skill's targets and applying it: an enemy <c>SingleTarget</c> skill
        /// aimed at one of the team's beasts may be intercepted by a guardian, which then takes it.
        /// </description></item>
        /// <item><description>
        /// After every application, after the passive hook: the hit and crit reactions, then the
        /// defeat and HP-threshold ones.
        /// </description></item>
        /// </list>
        /// </summary>
        public static BattleTurnResult ExecuteTurn(BattleUnit unit, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                                   PassiveLoadout passives, TeamBondLoadout bonds)
        {
            if (unit != null && unit == avatar)
            {
                // The avatar's own gauge came up: it has a turn of its own, not a beast's.
                return ExecuteAvatarTurn(avatar, allUnits, grid, rng, passives, bonds);
            }

            List<BattleSkillOutcome> outcomes = new List<BattleSkillOutcome>();
            List<SkillActivation> avatarActivations = new List<SkillActivation>();
            List<PassiveActivation> passiveActivations = new List<PassiveActivation>();
            List<BondReactionRecord> bondReactions = new List<BondReactionRecord>();
            BattleHooks hooks = BattleHooks.For(passives, avatar, bonds, allUnits, grid, rng, passiveActivations, bondReactions);

            if (unit == null || unit.IsDefeated)
            {
                HexCoordinate idle = unit == null ? HexCoordinate.Zero : unit.Position;
                return new BattleTurnResult(unit, idle, idle, 0, 0, outcomes, avatarActivations);
            }

            HexCoordinate start = unit.Position;

            if (hooks != null && hooks.HasPassives && !passives.HasBegun)
            {
                passives.Begin(avatar, allUnits, grid, rng, passiveActivations);

                if (unit.IsDefeated)
                {
                    // A battle-start passive finished it before it could act.
                    return new BattleTurnResult(unit, start, unit.Position, 0, 0, outcomes, avatarActivations, 0, false, 0, passiveActivations, bondReactions);
                }
            }

            // Anything defeated outside this type must not keep obstructing this turn's routes.
            LiftDefeated(allUnits, grid);

            SkillEffectApplier.TickModifiers(unit);
            hooks?.BeforeAllyTurn(unit);

            bool stunned;
            int statusDamage = StatusEffects.BeginTurn(unit, out stunned);
            hooks?.AfterApplication(unit, null);

            if (unit.IsDefeated)
            {
                // Its own damage-over-time finished it as the turn opened: there is no turn to take.
                LiftDefeated(allUnits, grid);
                return new BattleTurnResult(unit, start, unit.Position, 0, 0, outcomes, avatarActivations, 0, stunned, statusDamage, passiveActivations,
                                            bondReactions);
            }

            if (hooks != null && hooks.HasPassives && unit.Team == avatar.Team)
            {
                passives.OnAllyTurnStart(unit, avatar, allUnits, grid, rng, passiveActivations);
            }

            // Read after the tick, not before: move range is a stat, so a move-range buff that
            // expires on this tick must already be gone from this turn's budget.
            int budget = unit.MoveRange < 0 ? 0 : unit.MoveRange;
            int remaining = budget;

            // A stunned unit's rotation does not advance: nothing is offered, so nothing fires.
            SkillLoadout loadout = unit.Skills;
            IReadOnlyList<int> ready = loadout == null || stunned ? new List<int>() : loadout.Tick();

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
                    outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, 0, hooks));
                    continue;
                }

                BattleUnit candidate = SkillTargetResolver.PickFocusIgnoringRange(skill, unit, allUnits, rng);

                if (candidate == null)
                {
                    outcomes.Add(new BattleSkillOutcome(slotIndex, skill, BattleSkillStatus.NoCandidate, null, 0));
                    continue;
                }

                if (FootprintMath.UnitDistance(unit, candidate) <= skill.Range)
                {
                    outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, 0, hooks));
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
                outcomes.Add(Fire(loadout, slotIndex, skill, unit, allUnits, grid, rng, steps, hooks));
            }

            // Leftover budget: a Ranged or Skirmisher unit backs away; a Vanguard keeps its ground.
            int retreatSteps = 0;
            if (!stunned && !unit.IsDefeated && remaining > 0 && RetreatsWithLeftover(unit.Stance))
            {
                retreatSteps = Retreat(unit, allUnits, grid, remaining);
                remaining -= retreatSteps;
            }

            StatusEffects.EndTurn(unit);

            return new BattleTurnResult(unit, start, unit.Position, budget, budget - remaining, outcomes, avatarActivations, retreatSteps, stunned,
                                        statusDamage, passiveActivations, bondReactions);
        }

        /// <summary>
        /// Runs the avatar's own turn: the avatar is in the <see cref="TurnManager"/> roster (it
        /// fills a gauge from its own Speed like any unit) but not in <paramref name="allUnits"/>,
        /// so it is never targeted and never counted by the win check. On its turn, in order:
        /// <list type="number">
        /// <item><description>
        /// If <paramref name="passives"/> has not begun (a caller driving turns itself without
        /// <see cref="BeginBattle"/>), its battle-start hook runs first.
        /// </description></item>
        /// <item><description>
        /// Anything defeated outside this type is lifted off the grid, and the avatar's own timed
        /// modifiers tick (a buff it gave itself with a <see cref="SkillTargetShape.Self"/> skill is
        /// counted in its own turns, like a beast's). It has no status engine step: nothing can put
        /// a status on a unit nobody can target.
        /// </description></item>
        /// <item><description>
        /// Every passive's internal cooldown ticks once (<see cref="PassiveLoadout"/> cooldowns are
        /// counted in avatar turns), before its actives.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillLoadout.TickAndResolve"/> on its actives; each activation is applied with
        /// the avatar as caster, the newly defeated are lifted off the grid, and the after-damage
        /// passive hook runs. The avatar's crits are not ally crits, and an enemy its skill defeats
        /// credits no beast (an <see cref="BeastCraft.Avatar.PassiveTrigger.EnemyDefeated"/> passive
        /// scoped to the triggering unit finds none).
        /// </description></item>
        /// </list>
        /// <see cref="BeastCraft.Avatar.PassiveTrigger.AllyTurnStart"/> passives do not fire here:
        /// they fire as each player <em>beast's</em> turn opens.
        /// The result is attributed to the avatar: no movement, no <see cref="BattleTurnResult.SkillOutcomes"/>,
        /// its casts on <see cref="BattleTurnResult.AvatarActivations"/> and its passives on
        /// <see cref="BattleTurnResult.PassiveActivations"/>.
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>
        /// routes here when handed the avatar as the unit, so a caller driving turns itself gets the
        /// same turn. A null avatar takes no turn (an empty result).
        /// </summary>
        public static BattleTurnResult ExecuteAvatarTurn(BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, PassiveLoadout passives)
        {
            return ExecuteAvatarTurn(avatar, allUnits, grid, rng, passives, null);
        }

        /// <summary>
        /// <see cref="ExecuteAvatarTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, PassiveLoadout)"/>
        /// with the player team's bonds: after each avatar activation (and its passive hook) the
        /// bonds' defeat and HP-threshold reactions are checked. The avatar's hits and crits are no
        /// beast's, so they trigger no hit or crit reaction. A null bond loadout is exactly the
        /// bond-free avatar turn.
        /// </summary>
        public static BattleTurnResult ExecuteAvatarTurn(BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, PassiveLoadout passives,
                                                         TeamBondLoadout bonds)
        {
            List<SkillActivation> avatarActivations = new List<SkillActivation>();
            List<PassiveActivation> passiveActivations = new List<PassiveActivation>();
            List<BondReactionRecord> bondReactions = new List<BondReactionRecord>();

            if (avatar == null)
            {
                return new BattleTurnResult(null, HexCoordinate.Zero, HexCoordinate.Zero, 0, 0, null, avatarActivations);
            }

            BattleHooks hooks = BattleHooks.For(passives, avatar, bonds, allUnits, grid, rng, passiveActivations, bondReactions);

            if (hooks != null && hooks.HasPassives && !passives.HasBegun)
            {
                passives.Begin(avatar, allUnits, grid, rng, passiveActivations);
            }

            LiftDefeated(allUnits, grid);
            SkillEffectApplier.TickModifiers(avatar);

            if (hooks != null && hooks.HasPassives)
            {
                passives.TickCooldowns();
            }

            if (avatar.Skills != null)
            {
                IReadOnlyList<SkillActivation> cast = avatar.Skills.TickAndResolve(avatar, allUnits, grid, rng);

                for (int i = 0; i < cast.Count; i++)
                {
                    SkillEffectApplier.Apply(cast[i], avatar, rng, grid);
                    LiftDefeated(allUnits, grid);
                    avatarActivations.Add(cast[i]);

                    // No beast is credited: this is nobody's turn but the avatar's.
                    hooks?.AfterApplication(null, null);
                }
            }

            return new BattleTurnResult(avatar, avatar.Position, avatar.Position, 0, 0, null, avatarActivations, 0, false, 0, passiveActivations, bondReactions);
        }

        /// <summary>
        /// The battle-start passive hook: every <see cref="BeastCraft.Avatar.PassiveTrigger.Aura"/>
        /// passive in slot order, then every <see cref="BeastCraft.Avatar.PassiveTrigger.BattleStart"/>
        /// one, applied with the avatar as caster (see <see cref="PassiveLoadout"/>). Returns what
        /// fired, in order.
        /// <para>
        /// <see cref="RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, int)"/>
        /// calls it before the first turn and reports the result as
        /// <see cref="BattleResult.OpeningPassiveActivations"/>. A caller driving
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>
        /// itself should call it once with the same roster before its first turn; if it does not,
        /// the first turn runs it. Runs once per loadout: later calls return an empty list. A null
        /// or empty loadout, or no avatar, does nothing.
        /// </para>
        /// </summary>
        public static IReadOnlyList<PassiveActivation> BeginBattle(IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                                                   PassiveLoadout passives)
        {
            List<PassiveActivation> fired = new List<PassiveActivation>();

            if (passives != null && passives.Count > 0 && avatar != null)
            {
                passives.Begin(avatar, allUnits, grid, rng, fired);
            }

            return fired;
        }

        /// <summary>
        /// The battle-start hook with the player team's bonds: every active bond in
        /// <paramref name="bonds"/> is applied first (see <see cref="TeamBondLoadout"/>; what was
        /// applied is <paramref name="bondActivations"/>), then the passives exactly as
        /// <see cref="BeginBattle(IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>,
        /// so an avatar aura or battle-start passive sees the bonded stats. A loadout of bonds is
        /// applied once; later calls apply nothing. A null bond loadout is exactly the bond-free
        /// hook. Unlike the passives, bonds are not applied by a first turn that runs without this
        /// hook: a caller driving turns itself must call it.
        /// </summary>
        public static IReadOnlyList<PassiveActivation> BeginBattle(IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                                                   PassiveLoadout passives, TeamBondLoadout bonds,
                                                                   out IReadOnlyList<TeamBondActivation> bondActivations)
        {
            bondActivations = bonds == null ? new List<TeamBondActivation>() : bonds.Apply(grid, rng);
            return BeginBattle(allUnits, grid, rng, avatar, passives);
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
        /// is not retained. It is expected to hold the same beasts <paramref name="turnManager"/> was
        /// built from; if it does not, the loop still terminates — a turn order that runs dry while
        /// both sides look alive reports <see cref="BattleOutcome.Stalemate"/> rather than spinning.
        /// </para>
        /// <para>
        /// <strong>The avatar's gauge.</strong> With an avatar, build <paramref name="turnManager"/>
        /// from the beasts <em>and</em> the avatar: it fills a gauge from its own Speed like any
        /// unit, and when <see cref="TurnManager.CurrentUnit"/> is the avatar the loop runs
        /// <see cref="ExecuteAvatarTurn"/> instead of a beast's turn. Its turns are in
        /// <see cref="BattleResult.Turns"/> (and <see cref="BattleResult.ActionCount"/>) like any
        /// other. A turn manager built without it simply never gives the avatar a turn.
        /// </para>
        /// <para>
        /// Non-throwing: a null turn manager or an empty roster returns a result rather than
        /// failing.
        /// </para>
        /// </summary>
        public static BattleResult RunBattle(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar, int maxTime = DefaultMaxTime)
        {
            return RunBattle(turnManager, allUnits, grid, rng, avatar, null, maxTime);
        }

        /// <summary>
        /// <see cref="RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, int)"/>
        /// with the avatar's equipped passives: <see cref="BeginBattle"/> runs on the roster before
        /// the first turn (its firings are <see cref="BattleResult.OpeningPassiveActivations"/>),
        /// and every turn is
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout)"/>
        /// with them. A null or empty loadout is exactly the passive-free battle.
        /// </summary>
        public static BattleResult RunBattle(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                             PassiveLoadout passives, int maxTime = DefaultMaxTime)
        {
            return RunBattle(turnManager, allUnits, grid, rng, avatar, passives, null, maxTime);
        }

        /// <summary>
        /// <see cref="RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, int)"/>
        /// with the player team's bonds: the battle-start hook is
        /// <see cref="BeginBattle(IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, TeamBondLoadout, out IReadOnlyList{TeamBondActivation})"/>
        /// (bonds, then passives), and what the bonds applied is
        /// <see cref="BattleResult.BondActivations"/>; every turn is then
        /// <see cref="ExecuteTurn(BattleUnit, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, TeamBondLoadout)"/>
        /// (or the avatar's) with the bonds, so a behaviour bond reacts all battle long (its firings
        /// are on each turn's <see cref="BattleTurnResult.BondReactions"/>). A null bond loadout is
        /// exactly the bond-free battle, and a loadout whose bonds only act at battle start plays
        /// every turn, and every random draw, exactly as the bond-free one.
        /// </summary>
        public static BattleResult RunBattle(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar,
                                             PassiveLoadout passives, TeamBondLoadout bonds, int maxTime = DefaultMaxTime)
        {
            List<BattleUnit> roster = CopyRoster(allUnits);
            IReadOnlyList<PassiveActivation> opening = BeginBattle(roster, grid, rng, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> bonded);
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
                    turns.Add(current == avatar
                                  ? ExecuteAvatarTurn(avatar, roster, grid, rng, passives, bonds)
                                  : ExecuteTurn(current, roster, grid, rng, avatar, passives, bonds));
                }

                turnManager.AdvanceTurn();
            }

            return new BattleResult(outcome, lastTurnTicks, turns, opening, bonded);
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
        /// Resolves one slot from where the caster is standing right now, lets a guardian bond
        /// intercept it (an enemy single-target skill aimed at one of the bonded team's beasts; see
        /// <see cref="TeamBondLoadout"/>), applies what it does, lifts anyone it defeated (the caster
        /// included) off the grid, re-arms the slot, and runs the after-damage hooks (passives, then
        /// bonds). The one place in a beast's turn that a cooldown is reset, so a skill cannot be
        /// marked fired without having actually resolved.
        /// </summary>
        private static BattleSkillOutcome Fire(SkillLoadout loadout, int slotIndex, SkillSO skill, BattleUnit caster, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, int movementSpent,
                                               BattleHooks hooks)
        {
            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(skill, caster, allUnits, grid, rng);

            if (hooks != null)
            {
                targets = hooks.RedirectTargets(skill, caster, targets);
            }

            SkillActivation activation = new SkillActivation(loadout.GetInstance(slotIndex), targets);

            SkillEffectApplier.Apply(activation, caster, rng, grid);
            LiftDefeated(allUnits, grid);
            loadout.MarkFired(slotIndex);
            hooks?.AfterApplication(caster, activation);

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
        /// <strong>Large units.</strong> Against a large candidate the goals ring its whole footprint
        /// (<see cref="ApproachGoals"/>) and range is measured to its nearest tile. A large
        /// <em>mover</em> plans differently altogether: see <see cref="TryPlanLargeApproach"/>.
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

            if (mover.Footprint != UnitFootprint.Single)
            {
                return TryPlanLargeApproach(mover, candidate, skill, allUnits, grid, out route, out steps);
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
                    if (FootprintMath.DistanceTo(path[step], candidate.Position, candidate.Footprint) > skill.Range)
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
        /// <para>
        /// A large candidate is ringed instead: after its anchor (when nothing stands there), every
        /// tile next to any of its tiles and not one of them — twelve around a
        /// <see cref="UnitFootprint.Hex7"/>, nine around a <see cref="UnitFootprint.Triangle"/> —
        /// walked footprint tile by footprint tile (<see cref="Footprints.Offsets"/>' order), each in
        /// <see cref="HexCoordinate.AxialDirections"/>' order, first sighting kept.
        /// </para>
        /// </summary>
        private static List<HexCoordinate> ApproachGoals(HexGrid grid, BattleUnit mover, BattleUnit candidate)
        {
            List<HexCoordinate> goals = new List<HexCoordinate>();

            if (grid.IsPassable(candidate.Position, mover.Id))
            {
                goals.Add(candidate.Position);
            }

            IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;

            if (candidate.Footprint != UnitFootprint.Single)
            {
                IReadOnlyList<HexCoordinate> offsets = Footprints.Offsets(candidate.Footprint);

                for (int o = 0; o < offsets.Count; o++)
                {
                    for (int i = 0; i < directions.Count; i++)
                    {
                        HexCoordinate ring = candidate.Position + offsets[o] + directions[i];

                        if (FootprintMath.DistanceTo(ring, candidate.Position, candidate.Footprint) == 1 && grid.IsPassable(ring, mover.Id) &&
                            !goals.Contains(ring))
                        {
                            goals.Add(ring);
                        }
                    }
                }

                return goals;
            }

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
        /// <see cref="TryPlanApproach"/> for a large mover (any footprint but
        /// <see cref="UnitFootprint.Single"/>). Rather than aiming A* at goal tiles, it walks every
        /// anchor it can reach (<see cref="ReachableTiles"/>, where the whole footprint must fit at
        /// every step) and takes the cheapest anchor from which the candidate is in range, measured
        /// footprint to footprint. Among anchors of that cost: a <see cref="CombatStance.Vanguard"/>
        /// with fragile allies prefers the one nearest the closest of them (screening, judged at the
        /// in-range anchor); a <see cref="CombatStance.Ranged"/> or
        /// <see cref="CombatStance.Skirmisher"/> unit the one farthest from the candidate, then the
        /// one with the fewest enemies next to any of its tiles; then breadth-first discovery order.
        /// The route is then <see cref="HexPathfinder.FindPath(HexGrid, HexCoordinate, HexCoordinate, string, UnitFootprint)"/>
        /// to that anchor, which has exactly the breadth-first cost, so a partial approach walks a
        /// prefix of it as usual. False when no reachable anchor is in range.
        /// </summary>
        private static bool TryPlanLargeApproach(BattleUnit mover, BattleUnit candidate, SkillSO skill, IEnumerable<BattleUnit> allUnits, HexGrid grid,
                                                 out IReadOnlyList<HexCoordinate> route, out int steps)
        {
            route = null;
            steps = 0;

            List<BattleUnit> screened = mover.Stance == CombatStance.Vanguard ? FragileAllies(mover, allUnits) : null;
            bool screening = screened != null && screened.Count > 0;
            bool standoff = mover.Stance == CombatStance.Ranged || mover.Stance == CombatStance.Skirmisher;
            List<BattleUnit> enemies = standoff ? LivingEnemies(mover, allUnits) : null;

            ReachMap costs = ReachableTiles(grid, mover, int.MaxValue);
            List<HexCoordinate> order = costs.Order;

            bool found = false;
            HexCoordinate best = mover.Position;
            int bestCost = 0;
            int bestDistance = 0;
            int bestCrowd = 0;
            int bestScreen = 0;

            // Index 0 is the mover's own anchor, already known to be out of range.
            for (int i = 1; i < order.Count; i++)
            {
                HexCoordinate anchor = order[i];
                int cost;
                costs.TryGetCost(anchor, out cost);

                if (found && cost > bestCost)
                {
                    // Breadth-first order: nothing further on is as cheap.
                    break;
                }

                int distance = FootprintMath.UnitDistance(anchor, mover.Footprint, candidate.Position, candidate.Footprint);

                if (distance > skill.Range)
                {
                    continue;
                }

                int crowd = standoff ? AdjacentCount(anchor, mover.Footprint, enemies) : 0;
                int screen = screening ? ScreeningDistance(anchor, screened) : 0;

                bool better = !found
                    || (standoff && (distance > bestDistance || (distance == bestDistance && crowd < bestCrowd)))
                    || (screening && screen < bestScreen);

                if (better)
                {
                    found = true;
                    best = anchor;
                    bestCost = cost;
                    bestDistance = distance;
                    bestCrowd = crowd;
                    bestScreen = screen;
                }
            }

            if (!found)
            {
                return false;
            }

            IReadOnlyList<HexCoordinate> path = HexPathfinder.FindPath(grid, mover.Position, best, mover.Id, mover.Footprint);

            if (path.Count < 2)
            {
                return false;
            }

            route = path;
            steps = path.Count - 1;
            return true;
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
            ReachMap costs = ReachableTiles(grid, mover, int.MaxValue);
            List<BattleUnit> enemies = LivingEnemies(mover, allUnits);

            // A large candidate's in-range tiles all lie within Range + 1 of its anchor; the
            // distance filter below keeps only those within Range of its nearest tile.
            bool large = candidate.Footprint != UnitFootprint.Single;
            IReadOnlyList<HexCoordinate> tiles = grid.GetTilesInRange(candidate.Position, large ? skill.Range + 1 : skill.Range);

            bool found = false;
            int bestCost = 0;
            int bestDistance = 0;
            int bestCrowd = 0;
            bool bestIsPlain = false;

            for (int i = 0; i < tiles.Count; i++)
            {
                HexCoordinate tile = tiles[i];
                int cost;

                if (tile == mover.Position || !costs.TryGetCost(tile, out cost))
                {
                    continue;
                }

                int distance = FootprintMath.DistanceTo(tile, candidate.Position, candidate.Footprint);

                if (large && distance > skill.Range)
                {
                    continue;
                }

                int crowd = AdjacentCount(tile, UnitFootprint.Single, enemies);
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

            ReachMap costs = ReachableTiles(grid, unit, budget);
            List<HexCoordinate> order = costs.Order;

            HexCoordinate best = unit.Position;
            int bestNearest = NearestDistance(best, unit.Footprint, enemies);
            int bestCrowd = AdjacentCount(best, unit.Footprint, enemies);
            int bestCost = 0;

            for (int i = 0; i < order.Count; i++)
            {
                HexCoordinate tile = order[i];

                if (tile == unit.Position)
                {
                    continue;
                }

                int nearest = NearestDistance(tile, unit.Footprint, enemies);

                if (nearest > cap)
                {
                    continue;
                }

                int crowd = AdjacentCount(tile, unit.Footprint, enemies);
                int cost;
                costs.TryGetCost(tile, out cost);

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
        /// order; <see cref="ReachMap.Order"/> holds the tiles in discovery order. For a large mover
        /// the tiles are anchors and each must satisfy <see cref="HexGrid.CanStand"/> for its
        /// footprint, exactly as the footprint-aware pathfinder requires.
        /// <para>
        /// The result is this thread's reusable <see cref="ReachMap"/>, valid until the next call
        /// on the same thread: read it before searching again. (Only
        /// <see cref="TryPickStandoffTile"/>, <see cref="TryPlanLargeApproach"/> and <see cref="Retreat"/>
        /// call this, and each reads its map to the end before anything else searches.)
        /// </para>
        /// </summary>
        private static ReachMap ReachableTiles(HexGrid grid, BattleUnit mover, int maxSteps)
        {
            ReachMap costs = ReachMap.Begin(grid, mover.Position);
            List<HexCoordinate> order = costs.Order;
            HexCoordinate[] directions = HexPathfinder.Directions;
            UnitFootprint footprint = mover.Footprint;
            bool single = footprint == UnitFootprint.Single;

            // Breadth-first: every tile is queued exactly when it is appended to the discovery
            // order, so walking that list from the front is the FIFO queue.
            for (int head = 0; head < order.Count; head++)
            {
                HexCoordinate current = order[head];
                int cost;
                costs.TryGetCost(current, out cost);

                if (cost >= maxSteps)
                {
                    continue;
                }

                for (int i = 0; i < directions.Length; i++)
                {
                    HexCoordinate next = current + directions[i];
                    int index = grid.TileIndex(next);

                    bool passable = single ? grid.IsPassableAt(index, mover.Id) : grid.CanStandAt(index, next, footprint, mover.Id);

                    if (!passable || costs.Contains(next, index))
                    {
                        continue;
                    }

                    costs.Add(next, index, cost + 1);
                }
            }

            return costs;
        }

        /// <summary>
        /// The tiles <see cref="ReachableTiles"/> found and their step costs: flat arrays indexed
        /// by <see cref="HexGrid.TileIndex"/> and stamped with a generation, one instance reused
        /// per thread, so a search allocates nothing. The start tile is held apart so a mover
        /// recorded off the board still reads as reachable at 0, as it always has.
        /// </summary>
        private sealed class ReachMap
        {
            [ThreadStatic]
            private static ReachMap _current;

            private HexGrid _grid;
            private HexCoordinate _start;
            private int _generation;
            private int[] _stamp = new int[0];
            private int[] _cost = new int[0];

            /// <summary>Every tile found, the start first, in discovery order.</summary>
            public readonly List<HexCoordinate> Order = new List<HexCoordinate>();

            public static ReachMap Begin(HexGrid grid, HexCoordinate start)
            {
                ReachMap map = _current;
                if (map == null)
                {
                    map = new ReachMap();
                    _current = map;
                }

                int capacity = grid.TileIndexCapacity;
                if (map._stamp.Length < capacity)
                {
                    map._stamp = new int[capacity];
                    map._cost = new int[capacity];
                    map._generation = 0;
                }

                if (map._generation == int.MaxValue)
                {
                    Array.Clear(map._stamp, 0, map._stamp.Length);
                    map._generation = 0;
                }

                map._generation++;
                map._grid = grid;
                map._start = start;
                map.Order.Clear();
                map.Add(start, grid.TileIndex(start), 0);
                return map;
            }

            /// <summary>Whether a tile has been found; <paramref name="index"/> is its <see cref="HexGrid.TileIndex"/>.</summary>
            public bool Contains(HexCoordinate tile, int index)
            {
                return tile == _start || (index >= 0 && _stamp[index] == _generation);
            }

            public bool TryGetCost(HexCoordinate tile, out int cost)
            {
                if (tile == _start)
                {
                    cost = 0;
                    return true;
                }

                int index = _grid.TileIndex(tile);
                if (index >= 0 && _stamp[index] == _generation)
                {
                    cost = _cost[index];
                    return true;
                }

                cost = 0;
                return false;
            }

            public void Add(HexCoordinate tile, int index, int cost)
            {
                if (index >= 0)
                {
                    _stamp[index] = _generation;
                    _cost[index] = cost;
                }

                Order.Add(tile);
            }
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
            return NearestDistance(tile, UnitFootprint.Single, allies);
        }

        /// <summary>
        /// The hex distance from a unit of footprint <paramref name="footprint"/> anchored on
        /// <paramref name="tile"/> to the nearest of the given units, nearest tile to nearest tile
        /// (<see cref="FootprintMath.UnitDistance(HexCoordinate, UnitFootprint, HexCoordinate, UnitFootprint)"/>;
        /// <c>int.MaxValue</c> when there are none).
        /// </summary>
        private static int NearestDistance(HexCoordinate tile, UnitFootprint footprint, List<BattleUnit> units)
        {
            int nearest = int.MaxValue;

            for (int i = 0; i < units.Count; i++)
            {
                int distance = FootprintMath.UnitDistance(tile, footprint, units[i].Position, units[i].Footprint);

                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// How many of the given units stand next to a unit of footprint <paramref name="footprint"/>
        /// anchored on <paramref name="tile"/> — for a one-tile unit, on a tile adjacent to it; for a
        /// large one, next to any of its tiles. Each unit counts once, however many tiles it touches.
        /// </summary>
        private static int AdjacentCount(HexCoordinate tile, UnitFootprint footprint, List<BattleUnit> units)
        {
            int count = 0;

            for (int i = 0; i < units.Count; i++)
            {
                if (FootprintMath.UnitDistance(tile, footprint, units[i].Position, units[i].Footprint) == 1)
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
        /// rather than teleporting a unit whose tile the board does not think it holds. A large unit
        /// moves its whole footprint with it (all or nothing).
        /// </para>
        /// </summary>
        private static bool TryMove(BattleUnit unit, HexGrid grid, HexCoordinate destination)
        {
            bool placed = unit.Footprint == UnitFootprint.Single
                ? grid.TryPlaceUnit(unit.Id, destination)
                : grid.TryPlaceUnit(unit.Id, destination, unit.Footprint);

            if (!placed)
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
        internal static void LiftDefeated(IEnumerable<BattleUnit> allUnits, HexGrid grid)
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

        /// <summary>
        /// The live hook context of one turn — the avatar's passives and the avatar casting them, the
        /// team's reacting bonds, the roster, grid and rng, and where firings are recorded — so each
        /// hook call inside a turn stays one line. Either half may be absent. <see cref="For"/>
        /// returns <c>null</c> when there is nothing to run (no avatar or no equipped passive, and no
        /// applied bond with a reaction), which is what keeps such a turn exactly as it was, random
        /// draws included.
        /// </summary>
        private sealed class BattleHooks
        {
            private readonly PassiveLoadout _passives;
            private readonly TeamBondLoadout _bonds;
            private readonly BattleUnit _avatar;
            private readonly IEnumerable<BattleUnit> _allUnits;
            private readonly HexGrid _grid;
            private readonly Random _rng;
            private readonly List<PassiveActivation> _sink;
            private readonly BondReactionContext _bondContext;

            private BattleHooks(PassiveLoadout passives, TeamBondLoadout bonds, BattleUnit avatar, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng,
                                List<PassiveActivation> sink, List<BondReactionRecord> bondSink)
            {
                _passives = passives;
                _bonds = bonds;
                _avatar = avatar;
                _allUnits = allUnits;
                _grid = grid;
                _rng = rng;
                _sink = sink;
                _bondContext = bonds == null ? null : new BondReactionContext(allUnits, grid, rng, bondSink, passives);
            }

            public static BattleHooks For(PassiveLoadout passives, BattleUnit avatar, TeamBondLoadout bonds, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng,
                                          List<PassiveActivation> sink, List<BondReactionRecord> bondSink)
            {
                PassiveLoadout activePassives = passives == null || passives.Count == 0 || avatar == null ? null : passives;
                TeamBondLoadout reactingBonds = bonds != null && bonds.HasApplied && bonds.HasReactions ? bonds : null;

                return activePassives == null && reactingBonds == null
                    ? null
                    : new BattleHooks(activePassives, reactingBonds, avatar, allUnits, grid, rng, sink, bondSink);
            }

            /// <summary>Whether the avatar's passives are live this turn (an avatar with at least one equipped).</summary>
            public bool HasPassives
            {
                get { return _passives != null; }
            }

            /// <summary>The after-damage hooks: the passives', then the bonds'.</summary>
            public void AfterApplication(BattleUnit turnUnit, SkillActivation beastActivation)
            {
                _passives?.AfterApplication(turnUnit, beastActivation, _avatar, _allUnits, _grid, _rng, _sink);
                _bonds?.AfterApplication(turnUnit, beastActivation, _bondContext);
            }

            /// <summary>The bonds' intercept check on a resolved skill (the targets unchanged without reacting bonds).</summary>
            public IReadOnlyList<BattleUnit> RedirectTargets(SkillSO skill, BattleUnit caster, IReadOnlyList<BattleUnit> targets)
            {
                return _bonds == null ? targets : _bonds.RedirectTargets(skill, caster, targets, _bondContext);
            }

            /// <summary>The bonds' turn-opening hook for <paramref name="unit"/> (cooldowns, cleanses).</summary>
            public void BeforeAllyTurn(BattleUnit unit)
            {
                _bonds?.BeforeAllyTurn(unit, _bondContext);
            }
        }
    }
}
