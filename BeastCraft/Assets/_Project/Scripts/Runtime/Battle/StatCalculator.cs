using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Assembles a unit's final stat block from where it comes from: the species' base at the
    /// unit's level, then the modifiers on its equipped gear.
    /// <para>
    /// <strong>The order, per stat axis:</strong>
    /// <list type="number">
    /// <item><description>
    /// Base at level — <see cref="CreatureSpeciesSO.GetStatAtLevel"/>, which scales every axis by
    /// the species' growth curve except <see cref="StatType.MoveRange"/>, which it returns as
    /// authored.
    /// </description></item>
    /// <item><description>
    /// Plus the sum of every <see cref="StatModifier.FlatBonus"/> on that axis.
    /// </description></item>
    /// <item><description>
    /// Times <c>(1 + the sum of every <see cref="StatModifier.PercentBonus"/> on that axis)</c>,
    /// applied once. Percentages add to each other rather than compounding, so two +10% items are
    /// worth +20%, not +21%, and the order gear is listed in cannot change the result. They apply
    /// after the flat bonuses, so a percentage also scales what the gear added.
    /// </description></item>
    /// <item><description>
    /// Rounded to the nearest whole number (<see cref="Mathf.RoundToInt"/>, the same rounding
    /// the growth curve uses), then clamped: every stat at least 0, and <see cref="StatType.HP"/>
    /// at least 1, so no amount of cursed gear can field a unit that enters a battle already at
    /// 0 HP.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This is stat assembly only, and it produces the <em>starting</em> block a unit enters a
    /// battle with. Timed buffs and debuffs are not part of it: <see cref="SkillEffectApplier"/>
    /// folds those into <see cref="BattleUnit.Stats"/> during the fight and takes them back out
    /// again, on top of whatever this produced. Nor is it a damage formula — skill magnitudes are
    /// still flat, and nothing here changes that.
    /// </para>
    /// <para>
    /// The player avatar goes through the same steps 2 to 4 from an authored base instead of a
    /// species: <see cref="BattleAvatar"/> calls the <see cref="StatBlock"/> overload of
    /// <see cref="ComputeStats(StatBlock, IEnumerable{StatModifier})"/> with the modifiers
    /// <see cref="CollectModifiers(IEnumerable{AvatarGearSO})"/> gathers from its
    /// <see cref="AvatarGearSO"/>.
    /// </para>
    /// <para>
    /// Pure and static, like <see cref="SkillTargetResolver"/> and
    /// <see cref="SkillEffectApplier"/>: a function of the data passed in, holding nothing. It
    /// never mutates the species, the gear or their modifier lists. Non-throwing, matching the
    /// rest of the namespace: null gear lists, null gear entries, null modifier lists and null
    /// modifiers are all skipped quietly.
    /// </para>
    /// </summary>
    public static class StatCalculator
    {
        /// <summary>
        /// Every <see cref="StatType"/> axis, in enum order. <see cref="StatType"/> is append-only
        /// and contiguous from 0, so the axis value doubles as an index into the per-axis sums
        /// below; a new axis must be added here as well as to <see cref="StatBlock"/>.
        /// </summary>
        private static readonly StatType[] AllStats =
        {
            StatType.HP,
            StatType.Attack,
            StatType.Defense,
            StatType.SpecialAttack,
            StatType.SpecialDefense,
            StatType.Speed,
            StatType.MoveRange
        };

        /// <summary>
        /// The final stat block for a creature of <paramref name="species"/> at
        /// <paramref name="level"/> wearing <paramref name="equipped"/>, assembled in the order this
        /// class documents.
        /// <para>
        /// <strong>Gear the creature is too low-level for contributes nothing.</strong> A piece
        /// whose <see cref="GearSO.MinimumLevel"/> is above <paramref name="level"/> is skipped
        /// rather than rejected. Refusing the equip is the equipment screen's job, and this pass
        /// cannot tell a stale save from a mistake; what it can guarantee is that an under-levelled
        /// item never grants stats, whatever state the loadout arrived in.
        /// </para>
        /// <para>
        /// A null <paramref name="species"/> is an error in the caller, logged and treated as an
        /// all-zero base so the result is still a usable block (at the 1 HP floor) rather than a
        /// null reference halfway through a battle setup.
        /// </para>
        /// </summary>
        public static StatBlock ComputeStats(CreatureSpeciesSO species, int level, IEnumerable<GearSO> equipped)
        {
            return ComputeStats(GetBaseStatsAtLevel(species, level), CollectModifiers(equipped, level));
        }

        /// <summary>
        /// The same assembly starting from an explicit <paramref name="baseStats"/> instead of a
        /// species — steps 2 to 4 of the order this class documents, applied to
        /// <paramref name="modifiers"/>. For a participant with a stat block but no species behind
        /// it, such as the avatar. <see cref="CollectModifiers(IEnumerable{GearSO}, int)"/> turns an
        /// equipped gear list into the modifier list this takes, with the same minimum-level rule;
        /// <see cref="CollectModifiers(IEnumerable{AvatarGearSO})"/> does the same for avatar gear.
        /// </summary>
        public static StatBlock ComputeStats(StatBlock baseStats, IEnumerable<StatModifier> modifiers)
        {
            int[] flat = new int[AllStats.Length];
            float[] percent = new float[AllStats.Length];

            if (modifiers != null)
            {
                foreach (StatModifier modifier in modifiers)
                {
                    int index = modifier == null ? -1 : (int)modifier.Stat;

                    // A modifier naming no axis this build knows (a null entry, or a value cast
                    // past the end of the enum) has nowhere to land, so it is skipped.
                    if (index < 0 || index >= AllStats.Length)
                    {
                        continue;
                    }

                    flat[index] += modifier.FlatBonus;
                    percent[index] += modifier.PercentBonus;
                }
            }

            StatBlock result = default(StatBlock);

            for (int i = 0; i < AllStats.Length; i++)
            {
                StatType stat = AllStats[i];
                int value = Mathf.RoundToInt((baseStats.GetStat(stat) + flat[i]) * (1f + percent[i]));
                int floor = stat == StatType.HP ? 1 : 0;

                result.SetStat(stat, value < floor ? floor : value);
            }

            return result;
        }

        /// <summary>
        /// Step 1 on its own: every axis of <paramref name="species"/> at <paramref name="level"/>,
        /// via <see cref="CreatureSpeciesSO.GetStatAtLevel"/>. Unclamped — the floors belong to the
        /// final result, not to the intermediate base. A null species logs an error and yields an
        /// all-zero block.
        /// </summary>
        public static StatBlock GetBaseStatsAtLevel(CreatureSpeciesSO species, int level)
        {
            StatBlock result = default(StatBlock);

            if (species == null)
            {
                Debug.LogError("[Battle] StatCalculator was given a null species; using an all-zero base.");
                return result;
            }

            for (int i = 0; i < AllStats.Length; i++)
            {
                result.SetStat(AllStats[i], species.GetStatAtLevel(AllStats[i], level));
            }

            return result;
        }

        /// <summary>
        /// Flattens an equipped gear list into the modifiers it grants a creature at
        /// <paramref name="level"/>: every non-null modifier on every non-null piece whose
        /// <see cref="GearSO.MinimumLevel"/> is at most <paramref name="level"/>. A null list yields
        /// an empty one. The modifiers are the gear's own instances, not copies; nothing here
        /// writes to them.
        /// </summary>
        public static List<StatModifier> CollectModifiers(IEnumerable<GearSO> equipped, int level)
        {
            List<StatModifier> modifiers = new List<StatModifier>();

            if (equipped == null)
            {
                return modifiers;
            }

            foreach (GearSO gear in equipped)
            {
                if (gear == null || gear.MinimumLevel > level || gear.Modifiers == null)
                {
                    continue;
                }

                for (int i = 0; i < gear.Modifiers.Count; i++)
                {
                    if (gear.Modifiers[i] != null)
                    {
                        modifiers.Add(gear.Modifiers[i]);
                    }
                }
            }

            return modifiers;
        }

        /// <summary>
        /// The avatar counterpart of <see cref="CollectModifiers(IEnumerable{GearSO}, int)"/>:
        /// every non-null modifier on every non-null piece of <paramref name="equipped"/>. There is
        /// no level gate, because <see cref="AvatarGearSO"/> has no minimum level (the avatar has no
        /// level). Nor is there a slot check — two pieces in the same <see cref="AvatarGearSlot"/>
        /// both count; keeping one item per slot is the equipment screen's job, exactly as it is for
        /// beast gear. A null list yields an empty one, and the modifiers are the gear's own
        /// instances, never written to.
        /// </summary>
        public static List<StatModifier> CollectModifiers(IEnumerable<AvatarGearSO> equipped)
        {
            List<StatModifier> modifiers = new List<StatModifier>();

            if (equipped == null)
            {
                return modifiers;
            }

            foreach (AvatarGearSO gear in equipped)
            {
                if (gear == null || gear.Modifiers == null)
                {
                    continue;
                }

                for (int i = 0; i < gear.Modifiers.Count; i++)
                {
                    if (gear.Modifiers[i] != null)
                    {
                        modifiers.Add(gear.Modifiers[i]);
                    }
                }
            }

            return modifiers;
        }
    }
}
