using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Works out which units a skill lands on, given who is casting it and where everyone stands.
    /// <para>
    /// Battles are auto-resolved: the player positions beasts before the fight, and from then on
    /// each beast acts on its own build with no per-turn action menu. So this is the whole of the
    /// "who gets hit" decision — there is no player pick and no AI aim point to blend with. Every
    /// shape is anchored on <see cref="BattleUnit.Position"/> as the caster stands at the moment
    /// the skill fires.
    /// </para>
    /// <para>
    /// Targeting only. This picks units; it does not apply <see cref="SkillEffect"/>s, spend
    /// <c>SkillSO.ResourceCost</c>, check <c>SkillSO.Cooldown</c>, move anybody, or decide which of
    /// a beast's equipped skills fires this turn. Those are separate passes.
    /// </para>
    /// <para>
    /// <strong>Scaffold assumptions.</strong> The producer confirmed that a skill aims from the
    /// caster and authors its own side and pick rule; the per-shape rules below (which shapes
    /// exclude the caster's own tile, that <see cref="SkillTargetShape.AllEnemies"/> and
    /// <see cref="SkillTargetShape.AllAllies"/> ignore range entirely, that
    /// <see cref="SkillTargetShape.Line"/> snaps its direction to the nearest axis, and that no
    /// shape tests line of sight) are reasonable engineering defaults chosen here, not confirmed
    /// balance. They are cheap to revisit and expected to be.
    /// </para>
    /// <para>
    /// No line-of-sight or pathfinding test is applied anywhere: reach is plain
    /// <see cref="HexCoordinate.Distance"/>, and footprints are pure shape queries that terrain and
    /// occupancy do not carve up. That matches <see cref="HexGrid.GetTilesInRange"/>'s existing
    /// stance, and leaves cover as a deliberate later addition rather than a half-built one.
    /// </para>
    /// </summary>
    public static class SkillTargetResolver
    {
        /// <summary>
        /// Every living unit the skill hits, in a stable order (ordinal by
        /// <see cref="BattleUnit.Id"/>).
        /// <para>
        /// Per shape:
        /// <list type="bullet">
        /// <item><description>
        /// <see cref="SkillTargetShape.Self"/> — just the caster. Side, criterion, order, targeting
        /// stat and range are all ignored.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillTargetShape.SingleTarget"/> — zero or one unit: the units on
        /// <c>TargetSide</c> within <c>Range</c> steps of the caster, narrowed by the skill's
        /// targeting criterion/order. Under <see cref="SkillTargetSide.Ally"/> the caster is itself
        /// a candidate, which is what makes a self-heal authorable (<c>Ally</c> + <c>Stat</c> +
        /// <c>Lowest</c> + <c>HP</c>).
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillTargetShape.Line"/> — a focus target is picked exactly as for
        /// <c>SingleTarget</c>, its direction from the caster is snapped to the nearest of the six
        /// axes, and the beam is the <c>Range</c> tiles outward along that axis (the caster's own
        /// tile excluded). Everyone on <c>TargetSide</c> standing on those tiles is hit, not just
        /// the focus. No focus means no line and no targets.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillTargetShape.Cross"/> — all six axial arms, <c>Range</c> steps each, the
        /// caster's own tile excluded because the pattern radiates <em>from</em> the origin. Hits
        /// everyone eligible in the footprint, so no criterion, order or rng is consulted.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillTargetShape.AreaBurst"/> — the disc from
        /// <see cref="HexGrid.GetTilesInRange"/>, which by that method's contract <em>includes</em>
        /// the caster's tile; so an <see cref="SkillTargetSide.Ally"/> burst does catch its own
        /// caster. Hits everyone eligible in the footprint; no criterion, order or rng.
        /// </description></item>
        /// <item><description>
        /// <see cref="SkillTargetShape.AllEnemies"/> / <see cref="SkillTargetShape.AllAllies"/> —
        /// every living unit off / on the caster's team, board-wide. <strong>Range is ignored</strong>,
        /// as are <c>TargetSide</c> (the shape name already fixes the side), the criterion, the
        /// order and the rng. <c>AllAllies</c> includes the caster. This global reach is a real
        /// design choice and worth a second look when encounter balance starts.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// Only living units (<c>!IsDefeated</c>) are ever eligible, on either side of the query:
        /// a defeated caster resolves to nothing.
        /// </para>
        /// <para>
        /// Returns an empty list — never <c>null</c>, and never throwing — whenever nothing
        /// qualifies: a skill is allowed to whiff. That also covers the degenerate inputs, so
        /// callers do not need to pre-validate: a null skill or caster, and a null
        /// <paramref name="grid"/> for the three shapes that need a board
        /// (<c>Line</c>, <c>Cross</c>, <c>AreaBurst</c>) all yield an empty list. The other four
        /// shapes need no board and work without one.
        /// </para>
        /// <para>
        /// <paramref name="rng"/> is supplied by the caller rather than created here so that
        /// <see cref="SkillTargetingCriterion.Random"/> is reproducible: a seeded generator makes a
        /// replay or a server-side re-simulation of a battle agree with the client, the same
        /// concern that drives <see cref="TurnManager"/>'s id-based tie-break. A null
        /// <paramref name="rng"/> does not throw — it degenerates to the first candidate in id
        /// order, which is wrong-feeling but deterministic, and is a caller bug rather than a
        /// runtime failure.
        /// </para>
        /// </summary>
        public static IReadOnlyList<BattleUnit> ResolveTargets(SkillSO skill, BattleUnit caster, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng)
        {
            List<BattleUnit> results = new List<BattleUnit>();

            if (skill == null || caster == null || caster.IsDefeated)
            {
                return results;
            }

            List<BattleUnit> living = CollectLiving(allUnits);

            switch (skill.TargetShape)
            {
                case SkillTargetShape.Self:
                    results.Add(caster);
                    return results;

                case SkillTargetShape.AllEnemies:
                    return CollectSide(living, caster, SkillTargetSide.Enemy);

                case SkillTargetShape.AllAllies:
                    return CollectSide(living, caster, SkillTargetSide.Ally);

                case SkillTargetShape.SingleTarget:
                    BattleUnit single = PickFocus(skill, caster, living, rng);
                    if (single != null)
                    {
                        results.Add(single);
                    }

                    return results;

                case SkillTargetShape.Line:
                    return ResolveLine(skill, caster, living, grid, rng);

                case SkillTargetShape.Cross:
                    return ResolveCross(skill, caster, living, grid);

                case SkillTargetShape.AreaBurst:
                    return ResolveAreaBurst(skill, caster, living, grid);

                default:
                    return results;
            }
        }

        /// <summary>
        /// The living, non-null entries of the roster, ordered ordinally by
        /// <see cref="BattleUnit.Id"/>.
        /// <para>
        /// Sorting here rather than trusting the caller's enumeration order is what makes the whole
        /// resolver deterministic. It fixes which candidate a
        /// <see cref="SkillTargetingCriterion.Random"/> draw maps onto, it fixes the tie-break for
        /// the comparing criteria, and it fixes the order of the returned list — none of which
        /// should depend on however the roster happened to be assembled. Same reasoning, and the
        /// same key, as <see cref="TurnManager"/>'s initiative tie-break, so the two agree.
        /// </para>
        /// </summary>
        private static List<BattleUnit> CollectLiving(IEnumerable<BattleUnit> allUnits)
        {
            List<BattleUnit> living = new List<BattleUnit>();

            if (allUnits == null)
            {
                return living;
            }

            foreach (BattleUnit unit in allUnits)
            {
                if (unit != null && !unit.IsDefeated)
                {
                    living.Add(unit);
                }
            }

            living.Sort(CompareById);
            return living;
        }

        private static int CompareById(BattleUnit a, BattleUnit b)
        {
            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// True when a candidate sits on the side the skill is allowed to hit, judged against the
        /// caster's own team. With exactly two teams, "enemy" is simply "not the caster's team".
        /// A candidate that is the caster itself therefore always reads as an ally, never an enemy.
        /// </summary>
        private static bool IsOnSide(BattleUnit candidate, BattleUnit caster, SkillTargetSide side)
        {
            return side == SkillTargetSide.Ally
                ? candidate.Team == caster.Team
                : candidate.Team != caster.Team;
        }

        /// <summary>Every living unit on the given side, board-wide and range-free.</summary>
        private static List<BattleUnit> CollectSide(List<BattleUnit> living, BattleUnit caster, SkillTargetSide side)
        {
            List<BattleUnit> results = new List<BattleUnit>();

            for (int i = 0; i < living.Count; i++)
            {
                if (IsOnSide(living[i], caster, side))
                {
                    results.Add(living[i]);
                }
            }

            return results;
        }

        /// <summary>Every living unit on the given side standing on one of the footprint's tiles.</summary>
        private static List<BattleUnit> CollectOnTiles(List<BattleUnit> living, BattleUnit caster, SkillTargetSide side, HashSet<HexCoordinate> footprint)
        {
            List<BattleUnit> results = new List<BattleUnit>();

            for (int i = 0; i < living.Count; i++)
            {
                if (IsOnSide(living[i], caster, side) && footprint.Contains(living[i].Position))
                {
                    results.Add(living[i]);
                }
            }

            return results;
        }

        /// <summary>
        /// The single unit a picking shape settles on: the eligible units of the skill's side
        /// within <c>Range</c> steps of the caster, narrowed by the skill's criterion and order.
        /// Returns <c>null</c> when nothing is eligible.
        /// <para>
        /// Ties are broken by taking the first candidate in id order — the comparison below only
        /// displaces the incumbent on a strict win. That is deliberately the same rule whatever the
        /// criterion measures, and it matches <see cref="TurnManager"/>: keying on the id makes the
        /// choice a pure function of the roster's contents, so a re-simulation of the same battle
        /// picks the same unit instead of inheriting whatever order the roster was built in.
        /// </para>
        /// </summary>
        private static BattleUnit PickFocus(SkillSO skill, BattleUnit caster, List<BattleUnit> living, Random rng)
        {
            List<BattleUnit> candidates = new List<BattleUnit>();

            for (int i = 0; i < living.Count; i++)
            {
                if (IsOnSide(living[i], caster, skill.TargetSide)
                    && caster.Position.Distance(living[i].Position) <= skill.Range)
                {
                    candidates.Add(living[i]);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            if (skill.TargetingCriterion == SkillTargetingCriterion.Random)
            {
                return rng == null ? candidates[0] : candidates[rng.Next(candidates.Count)];
            }

            BattleUnit best = candidates[0];
            int bestValue = CriterionValue(skill, caster, best);

            for (int i = 1; i < candidates.Count; i++)
            {
                int value = CriterionValue(skill, caster, candidates[i]);
                bool wins = skill.TargetingOrder == SkillTargetingOrder.Highest
                    ? value > bestValue
                    : value < bestValue;

                if (wins)
                {
                    best = candidates[i];
                    bestValue = value;
                }
            }

            return best;
        }

        /// <summary>
        /// The number a comparing criterion ranks a candidate by. Distance is measured from the
        /// caster's live position, like everything else a skill does.
        /// </summary>
        private static int CriterionValue(SkillSO skill, BattleUnit caster, BattleUnit candidate)
        {
            switch (skill.TargetingCriterion)
            {
                case SkillTargetingCriterion.Stat:
                    return candidate.Stats.GetStat(skill.TargetingStat);
                case SkillTargetingCriterion.Distance:
                    return caster.Position.Distance(candidate.Position);
                default:
                    // Random does not compare, so it never reaches here; a flat 0 keeps any future
                    // criterion that forgets an arm harmless rather than crashing mid-battle.
                    return 0;
            }
        }

        /// <summary>
        /// A beam: pick a focus the way <see cref="SkillTargetShape.SingleTarget"/> would, snap the
        /// caster-to-focus direction onto an axis, and sweep everyone eligible along it.
        /// </summary>
        private static List<BattleUnit> ResolveLine(SkillSO skill, BattleUnit caster, List<BattleUnit> living, HexGrid grid, Random rng)
        {
            List<BattleUnit> results = new List<BattleUnit>();

            if (grid == null)
            {
                return results;
            }

            BattleUnit focus = PickFocus(skill, caster, living, rng);
            if (focus == null)
            {
                return results;
            }

            HexCoordinate direction = SnapToAxis(caster.Position, focus.Position, skill.Range);
            HashSet<HexCoordinate> footprint = new HashSet<HexCoordinate>();
            AddArm(footprint, grid, caster.Position, direction, skill.Range);

            return CollectOnTiles(living, caster, skill.TargetSide, footprint);
        }

        /// <summary>
        /// All six arms out to <c>Range</c>, the caster's own tile excluded.
        /// </summary>
        private static List<BattleUnit> ResolveCross(SkillSO skill, BattleUnit caster, List<BattleUnit> living, HexGrid grid)
        {
            List<BattleUnit> results = new List<BattleUnit>();

            if (grid == null)
            {
                return results;
            }

            HashSet<HexCoordinate> footprint = new HashSet<HexCoordinate>();
            IReadOnlyList<HexCoordinate> directions = AxialDirections();

            for (int i = 0; i < directions.Count; i++)
            {
                AddArm(footprint, grid, caster.Position, directions[i], skill.Range);
            }

            return CollectOnTiles(living, caster, skill.TargetSide, footprint);
        }

        /// <summary>
        /// The disc around the caster, the caster's own tile included per
        /// <see cref="HexGrid.GetTilesInRange"/>'s contract.
        /// </summary>
        private static List<BattleUnit> ResolveAreaBurst(SkillSO skill, BattleUnit caster, List<BattleUnit> living, HexGrid grid)
        {
            if (grid == null)
            {
                return new List<BattleUnit>();
            }

            IReadOnlyList<HexCoordinate> tiles = grid.GetTilesInRange(caster.Position, skill.Range);
            HashSet<HexCoordinate> footprint = new HashSet<HexCoordinate>();

            for (int i = 0; i < tiles.Count; i++)
            {
                footprint.Add(tiles[i]);
            }

            return CollectOnTiles(living, caster, skill.TargetSide, footprint);
        }

        /// <summary>
        /// Walks one arm outward from the origin, adding each tile but the origin itself. Stops at
        /// the board edge: the board is a convex hexagon, so once an arm leaves it, it never
        /// re-enters and there is nothing further out to add.
        /// </summary>
        private static void AddArm(HashSet<HexCoordinate> footprint, HexGrid grid, HexCoordinate origin, HexCoordinate direction, int range)
        {
            for (int step = 1; step <= range; step++)
            {
                HexCoordinate tile = origin + new HexCoordinate(direction.Q * step, direction.R * step);
                if (!grid.IsInBounds(tile))
                {
                    return;
                }

                footprint.Add(tile);
            }
        }

        /// <summary>
        /// The axis a beam fires along, given where the caster is and which unit it settled on.
        /// <para>
        /// A target is usually <em>not</em> sitting neatly on one of the six axes, so the direction
        /// has to be snapped. The test used here is the most direct reading of "which axis points
        /// most at that unit": project the beam's own full length along each candidate axis and
        /// keep whichever endpoint lands closest to the target by
        /// <see cref="HexCoordinate.Distance"/>. Comparing endpoints at the beam's real length,
        /// rather than comparing unit direction vectors, means the snap is judged on where the beam
        /// actually reaches — a near target and a far one off the same bearing can legitimately
        /// prefer different axes.
        /// </para>
        /// <para>
        /// Ties go to the earlier axis in <see cref="HexCoordinate.Neighbors"/>'s fixed order,
        /// which that method documents as stable precisely so results reproduce. A degenerate
        /// focus — the caster having picked itself, only possible under
        /// <see cref="SkillTargetSide.Ally"/> — ties all six axes and so fires along the first;
        /// the caster's own tile is not on the beam either way.
        /// </para>
        /// </summary>
        private static HexCoordinate SnapToAxis(HexCoordinate origin, HexCoordinate focus, int range)
        {
            IReadOnlyList<HexCoordinate> directions = AxialDirections();

            HexCoordinate best = directions[0];
            int bestDistance = int.MaxValue;

            for (int i = 0; i < directions.Count; i++)
            {
                HexCoordinate projected = origin + new HexCoordinate(directions[i].Q * range, directions[i].R * range);
                int distance = projected.Distance(focus);

                if (distance < bestDistance)
                {
                    best = directions[i];
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// The six axial direction vectors, in <see cref="HexCoordinate"/>'s fixed order. Taken as
        /// the neighbours of the origin, which are exactly the direction vectors, so the table
        /// stays owned by <see cref="HexCoordinate"/> and is not duplicated here where the two
        /// copies could drift apart.
        /// </summary>
        private static IReadOnlyList<HexCoordinate> AxialDirections()
        {
            return HexCoordinate.Zero.Neighbors();
        }
    }
}
