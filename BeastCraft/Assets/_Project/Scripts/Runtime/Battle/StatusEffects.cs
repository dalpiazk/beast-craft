using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle
{
    /// <summary>
    /// The status engine: how each <see cref="StatusType"/> is put on a unit, what it does while it
    /// rides there, and how it runs out. The statuses themselves live on the affected unit
    /// (<see cref="BattleUnit.Statuses"/>); this class holds every rule that reads or writes them.
    /// <para>
    /// <strong>Who calls what.</strong> <see cref="SkillEffectApplier"/> applies a status when an
    /// <see cref="SkillEffectType.ApplyStatus"/> effect lands (after its chance roll) and spends a
    /// shield when damage lands. <see cref="BattleTurnExecutor"/> calls <see cref="BeginTurn"/> and
    /// <see cref="EndTurn"/> around every unit's own turn, and <see cref="SkillTargetResolver"/>
    /// asks <see cref="GetTaunter"/> when a picking skill chooses its focus.
    /// </para>
    /// <para>
    /// <strong>Durations</strong> are counted in the <em>affected</em> unit's own turns, like a timed
    /// stat modifier, and mean "in force for that many of its turns": each status still present
    /// when one of the unit's turns begins has that turn counted off (<see cref="BeginTurn"/>), and
    /// is removed when a turn ends with nothing left (<see cref="EndTurn"/>). A status applied
    /// during the unit's own turn — a self-shield — therefore does not lose that turn. A
    /// <see cref="SkillEffect.DurationTurns"/> below 1 is read as 1 for a stored status.
    /// </para>
    /// <para>
    /// <strong>Stacking</strong>, per status:
    /// <list type="bullet">
    /// <item><description><see cref="StatusType.Taunt"/> — one per unit; a new taunt replaces the old one (latest wins).</description></item>
    /// <item><description><see cref="StatusType.Stun"/> — one per unit; a new stun keeps whichever has more turns left.</description></item>
    /// <item><description><see cref="StatusType.Shield"/> — one per unit; the larger shield is kept, and on a tie the existing one (its duration is not refreshed).</description></item>
    /// <item><description><see cref="StatusType.DamageOverTime"/> — up to <see cref="SkillEffect.MaxStacks"/> per authored effect, each on its own clock; at the cap the stack with the fewest turns left is replaced.</description></item>
    /// <item><description><see cref="StatusType.Knockback"/> — instant; never stored.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Deterministic and draw-free: nothing here touches the rng (the chance roll that decides
    /// whether a status lands belongs to <see cref="SkillEffectApplier"/>), and nothing uses floating
    /// point to decide an order. Non-throwing: null units and effects are quiet no-ops.
    /// </para>
    /// </summary>
    public static class StatusEffects
    {
        /// <summary>
        /// What a status's <see cref="SkillEffect.Magnitude"/> is a percent of, for a shield: the
        /// caster's <c>Defense</c>. A tunable divisor so the shield curve can be retuned in one place.
        /// </summary>
        public const double ShieldPercentDivisor = 100.0;

        /// <summary>
        /// The unit <paramref name="unit"/> is taunted by, or <c>null</c>: the source of its taunt
        /// when that source is still alive and on the other team — a legal enemy-side target. A
        /// taunt whose source has fallen (or is an ally, or the unit itself) forces nothing.
        /// </summary>
        public static BattleUnit GetTaunter(BattleUnit unit)
        {
            if (unit == null)
            {
                return null;
            }

            IReadOnlyList<ActiveStatus> statuses = unit.Statuses;

            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                ActiveStatus status = statuses[i];

                if (status.Type != StatusType.Taunt)
                {
                    continue;
                }

                BattleUnit source = status.Source;
                return source != null && !source.IsDefeated && source.Team != unit.Team ? source : null;
            }

            return null;
        }

        /// <summary>Whether the unit carries a <see cref="StatusType.Stun"/> right now.</summary>
        public static bool IsStunned(BattleUnit unit)
        {
            return Find(unit, StatusType.Stun) != null;
        }

        /// <summary>The shield points the unit has left (0 with no shield).</summary>
        public static int ShieldPoints(BattleUnit unit)
        {
            ActiveStatus shield = Find(unit, StatusType.Shield);
            return shield == null ? 0 : shield.Amount;
        }

        /// <summary>How many statuses of <paramref name="type"/> the unit carries (stacks count separately).</summary>
        public static int Count(BattleUnit unit, StatusType type)
        {
            int count = 0;

            if (unit == null)
            {
                return count;
            }

            IReadOnlyList<ActiveStatus> statuses = unit.Statuses;

            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Type == type)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Opens one of the unit's own turns: reports whether it is stunned (a stun present as the
        /// turn begins skips it), counts this turn off every status it carries, then deals each
        /// damage-over-time stack in the order applied — through its shield first, like any damage —
        /// stopping if the unit falls. Returns the HP the stacks took. The executor calls this after
        /// ticking the unit's stat modifiers and before anything else in the turn.
        /// </summary>
        public static int BeginTurn(BattleUnit unit, out bool stunned)
        {
            stunned = false;

            if (unit == null || unit.IsDefeated)
            {
                return 0;
            }

            List<ActiveStatus> statuses = unit.StatusList;
            stunned = IsStunned(unit);

            bool anyDamageOverTime = false;

            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].RemainingTurns > 0)
                {
                    statuses[i].RemainingTurns -= 1;
                }

                anyDamageOverTime |= statuses[i].Type == StatusType.DamageOverTime;
            }

            int dealt = 0;

            if (!anyDamageOverTime)
            {
                // Nothing below would fire; skip the snapshot (this runs on every turn of every unit).
                return dealt;
            }

            // Snapshot: a shield broken by the first stack is removed from the live list mid-walk.
            ActiveStatus[] snapshot = statuses.ToArray();

            for (int i = 0; i < snapshot.Length && !unit.IsDefeated; i++)
            {
                if (snapshot[i].Type == StatusType.DamageOverTime)
                {
                    int before = unit.CurrentHp;
                    SkillEffectApplier.SpendHp(unit, snapshot[i].Amount);
                    dealt += before - unit.CurrentHp;
                }
            }

            return dealt;
        }

        /// <summary>
        /// Closes one of the unit's own turns: every status with no turns left is removed. Statuses
        /// applied during this turn were not counted by <see cref="BeginTurn"/>, so they stay.
        /// </summary>
        public static void EndTurn(BattleUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            List<ActiveStatus> statuses = unit.StatusList;

            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                if (statuses[i].RemainingTurns <= 0)
                {
                    statuses.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Soaks up to <paramref name="amount"/> damage with the unit's shield and returns how much
        /// it took; a shield brought to 0 is removed. The rest is the caller's to spend on HP.
        /// </summary>
        internal static int Absorb(BattleUnit unit, int amount)
        {
            ActiveStatus shield = Find(unit, StatusType.Shield);

            if (shield == null || amount <= 0)
            {
                return 0;
            }

            int absorbed = amount < shield.Amount ? amount : shield.Amount;
            shield.Amount -= absorbed;

            if (shield.Amount <= 0)
            {
                unit.StatusList.Remove(shield);
            }

            return absorbed;
        }

        /// <summary>
        /// Puts <paramref name="effect"/>'s status on <paramref name="target"/>. The chance roll has
        /// already succeeded. <paramref name="magnitude"/> is the level-scaled magnitude (a shield's
        /// percent, a damage-over-time stack's power); a knockback reads the authored, unscaled
        /// <see cref="SkillEffect.Magnitude"/> as whole hexes. <paramref name="grid"/> is needed only
        /// by a knockback, which does nothing without one.
        /// </summary>
        internal static void Apply(SkillSO skill, BattleUnit caster, BattleUnit target, SkillEffect effect, float magnitude, HexGrid grid)
        {
            int duration = effect.DurationTurns < 1 ? 1 : effect.DurationTurns;
            List<ActiveStatus> statuses = target.StatusList;

            switch (effect.Status)
            {
                case StatusType.Taunt:
                    RemoveAll(target, StatusType.Taunt);
                    statuses.Add(new ActiveStatus(StatusType.Taunt, caster, duration, 0, effect));
                    break;

                case StatusType.Stun:
                    ActiveStatus stun = Find(target, StatusType.Stun);

                    if (stun == null)
                    {
                        statuses.Add(new ActiveStatus(StatusType.Stun, caster, duration, 0, effect));
                    }
                    else if (duration > stun.RemainingTurns)
                    {
                        stun.RemainingTurns = duration;
                    }

                    break;

                case StatusType.Shield:
                    int points = (int)(magnitude * (double)caster.Stats.Defense / ShieldPercentDivisor);
                    ActiveStatus shield = Find(target, StatusType.Shield);

                    if (points <= 0 || (shield != null && shield.Amount >= points))
                    {
                        break;
                    }

                    if (shield != null)
                    {
                        statuses.Remove(shield);
                    }

                    statuses.Add(new ActiveStatus(StatusType.Shield, caster, duration, points, effect));
                    break;

                case StatusType.DamageOverTime:
                    int perTurn = DamageFormula.Compute(caster, target, skill, magnitude);

                    if (perTurn <= 0)
                    {
                        break;
                    }

                    int cap = effect.MaxStacks < 1 ? 1 : effect.MaxStacks;
                    RemoveWeakestAtCap(statuses, effect, cap);
                    statuses.Add(new ActiveStatus(StatusType.DamageOverTime, caster, duration, perTurn, effect));
                    break;

                case StatusType.Knockback:
                    Knockback(caster, target, (int)effect.Magnitude, grid);
                    break;

                default:
                    // StatusType.None, or a value added to the enum without an arm here: nothing.
                    break;
            }
        }

        /// <summary>
        /// Pushes <paramref name="target"/> up to <paramref name="hexes"/> tiles directly away from
        /// <paramref name="caster"/>, one tile at a time, stopping at the first tile that is off the
        /// board, blocked or occupied. "Directly away" is the axial direction whose vector has the
        /// largest dot product with the caster-to-target vector, computed exactly in integers
        /// (<see cref="AwayDirection"/>); ties go to the earlier direction in
        /// <see cref="HexCoordinate.AxialDirections"/>. No grid, a target not on it, a caster on the
        /// target's own tile, or a distance below 1 does nothing. Moves the grid first and
        /// <see cref="BattleUnit.Position"/> only once the grid accepted, like the executor.
        /// </summary>
        internal static void Knockback(BattleUnit caster, BattleUnit target, int hexes, HexGrid grid)
        {
            if (grid == null || hexes < 1 || !grid.TryGetPosition(target.Id, out HexCoordinate position))
            {
                return;
            }

            HexCoordinate? away = AwayDirection(caster.Position, position);

            if (!away.HasValue)
            {
                return;
            }

            HexCoordinate destination = position;

            for (int step = 0; step < hexes; step++)
            {
                HexCoordinate next = destination + away.Value;

                if (!grid.IsInBounds(next) || !grid.IsPassable(next, target.Id))
                {
                    break;
                }

                destination = next;
            }

            if (destination != position && grid.TryPlaceUnit(target.Id, destination))
            {
                target.Position = destination;
            }
        }

        /// <summary>
        /// The axial direction pointing most directly from <paramref name="from"/> to
        /// <paramref name="to"/>, or <c>null</c> when they are the same tile. Scored by the
        /// Cartesian dot product of the two axial vectors, which for <c>(q1, r1)·(q2, r2)</c> is
        /// proportional to <c>2·q1·q2 + q1·r2 + r1·q2 + 2·r1·r2</c> — an exact integer, so the pick
        /// never depends on floating point. Ties keep the earlier direction.
        /// </summary>
        public static HexCoordinate? AwayDirection(HexCoordinate from, HexCoordinate to)
        {
            int vq = to.Q - from.Q;
            int vr = to.R - from.R;

            if (vq == 0 && vr == 0)
            {
                return null;
            }

            IReadOnlyList<HexCoordinate> directions = HexCoordinate.AxialDirections;
            HexCoordinate best = directions[0];
            long bestScore = long.MinValue;

            for (int i = 0; i < directions.Count; i++)
            {
                HexCoordinate d = directions[i];
                long score = (2L * d.Q * vq) + ((long)d.Q * vr) + ((long)d.R * vq) + (2L * d.R * vr);

                if (score > bestScore)
                {
                    best = d;
                    bestScore = score;
                }
            }

            return best;
        }

        /// <summary>The first status of <paramref name="type"/> on the unit, or <c>null</c>.</summary>
        private static ActiveStatus Find(BattleUnit unit, StatusType type)
        {
            if (unit == null)
            {
                return null;
            }

            IReadOnlyList<ActiveStatus> statuses = unit.Statuses;

            for (int i = 0; i < statuses.Count; i++)
            {
                if (statuses[i].Type == type)
                {
                    return statuses[i];
                }
            }

            return null;
        }

        private static void RemoveAll(BattleUnit unit, StatusType type)
        {
            unit.StatusList.RemoveAll(status => status.Type == type);
        }

        /// <summary>
        /// Makes room for one more stack of <paramref name="origin"/>: while the unit already holds
        /// <paramref name="cap"/> or more, the one with the fewest turns left (the earliest applied
        /// on a tie) is removed.
        /// </summary>
        private static void RemoveWeakestAtCap(List<ActiveStatus> statuses, SkillEffect origin, int cap)
        {
            while (true)
            {
                int count = 0;
                int weakest = -1;

                for (int i = 0; i < statuses.Count; i++)
                {
                    if (statuses[i].Origin != origin)
                    {
                        continue;
                    }

                    count++;

                    if (weakest < 0 || statuses[i].RemainingTurns < statuses[weakest].RemainingTurns)
                    {
                        weakest = i;
                    }
                }

                if (count < cap)
                {
                    return;
                }

                statuses.RemoveAt(weakest);
            }
        }
    }
}
