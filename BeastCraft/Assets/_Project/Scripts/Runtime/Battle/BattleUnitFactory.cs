using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Progression;

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
    /// record. That choice lives in a <see cref="BeastSkillBook"/>, which the overload taking one
    /// turns into the loadout through <see cref="BuildLoadout"/>.
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
        /// max HP), with no timed modifiers, and holds a copy of the species' elements and its
        /// <see cref="CreatureSpeciesSO.Stance"/>, so later edits to the species asset do not reach
        /// it mid-battle. A null species gives the default <see cref="CombatStance.Vanguard"/>.
        /// </para>
        /// <para>
        /// <paramref name="level"/> is recorded as <see cref="BattleUnit.Level"/> as well as used to
        /// assemble the stats, so the recorded level and the stats always come from the same level
        /// (the damage formula reads only the stats). A level below 1 is stored as 1 by the unit, the same floor
        /// the growth curve clamps to.
        /// </para>
        /// </summary>
        public static BattleUnit CreateBeast(string id, BattleTeam team, CreatureSpeciesSO species, int level,
                                             IEnumerable<GearSO> equipped, HexCoordinate position, SkillLoadout skills = null)
        {
            StatBlock stats = StatCalculator.ComputeStats(species, level, equipped);
            Element[] elements = species == null ? null : species.Elements;
            CombatStance stance = species == null ? CombatStance.Vanguard : species.Stance;

            return new BattleUnit(id, team, stats, position, skills, elements, level, stance);
        }

        /// <summary>
        /// Builds one beast whose loadout comes from its <see cref="BeastSkillBook"/>: exactly
        /// <see cref="CreateBeast(string, BattleTeam, CreatureSpeciesSO, int, IEnumerable{GearSO}, HexCoordinate, SkillLoadout)"/>
        /// with the loadout <see cref="BuildLoadout"/> makes from <paramref name="skillBook"/> and
        /// <paramref name="skillLookup"/>. A null book or lookup gives an empty loadout.
        /// </summary>
        public static BattleUnit CreateBeast(string id, BattleTeam team, CreatureSpeciesSO species, int level,
                                             IEnumerable<GearSO> equipped, HexCoordinate position,
                                             BeastSkillBook skillBook, Func<string, SkillSO> skillLookup)
        {
            return CreateBeast(id, team, species, level, equipped, position, BuildLoadout(skillBook, skillLookup));
        }

        /// <summary>
        /// The battle loadout for a skill book: each equipped slot, in slot order (slot order is
        /// fire priority), resolved to its <see cref="SkillSO"/> by <paramref name="skillLookup"/>
        /// (skill id to asset) and put in at the level and tier its <see cref="SkillProgress"/>
        /// records (<see cref="SkillInstance.FromProgress"/>).
        /// <para>
        /// Empty slots are skipped, so the stack closes up: with slot 1 empty, slot 2's skill is the
        /// loadout's second entry and still fires after slot 0's. A slot naming a skill the book
        /// does not know (only possible in hand-edited or corrupt data) or one the lookup cannot
        /// resolve is skipped the same way. A null book or lookup gives an empty loadout. Never
        /// throws, never returns <c>null</c>.
        /// </para>
        /// </summary>
        public static SkillLoadout BuildLoadout(BeastSkillBook skillBook, Func<string, SkillSO> skillLookup)
        {
            List<SkillInstance> instances = new List<SkillInstance>();

            if (skillBook != null && skillLookup != null)
            {
                for (int slot = 0; slot < BeastSkillBook.EquipSlotCount; slot++)
                {
                    string skillId = skillBook.GetEquipped(slot);
                    SkillProgress progress = skillBook.GetProgress(skillId);

                    if (progress == null)
                    {
                        continue;
                    }

                    SkillSO skill = skillLookup(skillId);

                    if (skill != null)
                    {
                        instances.Add(SkillInstance.FromProgress(skill, progress));
                    }
                }
            }

            return SkillLoadout.FromInstances(instances);
        }
    }
}
