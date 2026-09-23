using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Builds a battle-ready beast from what it is made of: its species, its level and its
    /// equipped gear. This is the pass that assembles a <see cref="BattleUnit"/> from a creature
    /// rather than from a hand-written <see cref="StatBlock"/>.
    /// <para>
    /// The counterpart to <see cref="BattleAvatar"/> for the combatants: a one-method factory
    /// producing an ordinary <see cref="BattleUnit"/>, not a parallel type. Its stats come from
    /// <see cref="StatCalculator.ComputeStats(CreatureSpeciesSO, int, IEnumerable{GearSO})"/> —
    /// which is also where <see cref="BattleUnit.MoveRange"/> now comes from, since move range is
    /// a stat — and its elements from <see cref="CreatureSpeciesSO.Elements"/>.
    /// </para>
    /// <para>
    /// It takes the species, level and gear directly because there is still no persistent
    /// creature-instance type to take instead. When one exists, it is the natural input here, and
    /// this signature is what it unpacks into. The skill loadout is likewise passed in rather than
    /// derived from <see cref="CreatureSpeciesSO.LearnableSkills"/>: which learned skills a beast
    /// has <em>equipped</em>, and in what stack order, is a player choice the species does not
    /// record.
    /// </para>
    /// <para>
    /// Non-throwing, like the rest of the namespace. A null species is logged by
    /// <see cref="StatCalculator"/> and yields a unit on an all-zero base (1 HP, no move range, no
    /// elements) rather than an exception in the middle of battle setup.
    /// </para>
    /// </summary>
    public static class BattleUnitFactory
    {
        /// <summary>
        /// Builds one beast. <paramref name="id"/>, <paramref name="team"/>,
        /// <paramref name="position"/> and <paramref name="skills"/> are handed straight to the
        /// <see cref="BattleUnit"/> constructor, with its usual meaning — including a null
        /// <paramref name="skills"/> becoming an empty loadout. <paramref name="equipped"/> may be
        /// null or contain nulls; gear the beast is too low-level for is ignored, per
        /// <see cref="StatCalculator"/>.
        /// <para>
        /// The unit enters at full health (<see cref="BattleUnit.CurrentHp"/> equals the assembled
        /// max HP), with no timed modifiers, and holds a copy of the species' elements, so later
        /// edits to the species asset do not reach it mid-battle.
        /// </para>
        /// <para>
        /// <paramref name="level"/> is recorded as <see cref="BattleUnit.Level"/> as well as used to
        /// assemble the stats, so the damage formula's level term and the stats it divides always
        /// come from the same level. A level below 1 is stored as 1 by the unit, the same floor
        /// the growth curve clamps to.
        /// </para>
        /// </summary>
        public static BattleUnit CreateBeast(string id, BattleTeam team, CreatureSpeciesSO species, int level,
                                             IEnumerable<GearSO> equipped, HexCoordinate position, SkillLoadout skills = null)
        {
            StatBlock stats = StatCalculator.ComputeStats(species, level, equipped);
            Element[] elements = species == null ? null : species.Elements;

            return new BattleUnit(id, team, stats, position, skills, elements, level);
        }
    }
}
