using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Builds the skills the simulator fights with: the standard beast kit (every beast gets the
    /// same one, so stats are what is measured) and the enemy kits authored in
    /// <c>encounters.json</c>. Every beast skill aims at the nearest eligible unit
    /// (<see cref="SkillTargetingCriterion.Distance"/> / <see cref="SkillTargetingOrder.Lowest"/>);
    /// an enemy skill uses its fixture's targeting (nearest by default, the least current HP, or a
    /// stat extreme). Neither ever uses <see cref="SkillTargetingCriterion.Random"/>, so no battle
    /// consumes the rng for targeting.
    /// </summary>
    public static class Kit
    {
        /// <summary>
        /// The standard beast kit in fire-priority order: Blast, the physical single-target skill,
        /// then the two Burst halves. Blast fires first from wherever the beast stands (range 3).
        /// The physical skill is Strike (range 1), which walks a Vanguard or Skirmisher into melee,
        /// or, for a <see cref="CombatStance.Ranged"/> beast, which never walks into melee, Shot
        /// (range 3, like Blast). The Burst halves fire last so they go off from the beast's
        /// post-move tile, where the enemies it just closed on are. The kit's element is the beast's
        /// first element in <see cref="KitMode.Elemental"/> and <c>Element.None</c> in
        /// <see cref="KitMode.Neutral"/>.
        /// </summary>
        public static SkillSO[] BuildBeastKit(CreatureSpeciesSO species, KitMode mode)
        {
            Element element = Element.None;
            if (mode == KitMode.Elemental && species.Elements != null && species.Elements.Length > 0)
            {
                element = species.Elements[0];
            }

            SkillSO physical = species.Stance == CombatStance.Ranged
                ? BuildSkill(SimOptions.ShotId, SimOptions.ShotCategory, SkillTargetShape.SingleTarget, SimOptions.ShotPower, SimOptions.ShotRange,
                             SimOptions.ShotCooldown, element)
                : BuildSkill(SimOptions.StrikeId, SimOptions.StrikeCategory, SkillTargetShape.SingleTarget, SimOptions.StrikePower,
                             SimOptions.StrikeRange, SimOptions.StrikeCooldown, element);

            return new[]
            {
                BuildSkill(SimOptions.BlastId, SimOptions.BlastCategory, SkillTargetShape.SingleTarget, SimOptions.BlastPower,
                           SimOptions.BlastRange, SimOptions.BlastCooldown, element),
                physical,
                BuildSkill(SimOptions.BurstPhysicalId, DamageCategory.Physical, SkillTargetShape.AreaBurst, SimOptions.BurstPower,
                           SimOptions.BurstRadius, SimOptions.BurstCooldown, element),
                BuildSkill(SimOptions.BurstSpecialId, DamageCategory.Special, SkillTargetShape.AreaBurst, SimOptions.BurstPower,
                           SimOptions.BurstRadius, SimOptions.BurstCooldown, element)
            };
        }

        /// <summary>
        /// An enemy kit from its fixture definition: every skill in the given element, with its
        /// authored targeting, its optional advanced fields (hit count, execute bonus, initial
        /// cooldown, use limit) and any extra effects after the damage effect. The extra effects are
        /// shared, read-only <see cref="SkillEffect"/> instances, like the skills themselves.
        /// </summary>
        public static SkillSO[] BuildEnemyKit(IReadOnlyList<EnemySkillData> skills, Element element)
        {
            SkillSO[] kit = new SkillSO[skills.Count];
            for (int i = 0; i < skills.Count; i++)
            {
                EnemySkillData data = skills[i];
                kit[i] = BuildSkill(data.SkillId, data.ParsedCategory, data.ParsedShape, data.Power, data.Range, data.Cooldown, element);
                kit[i].TargetingCriterion = data.ParsedTargeting;
                kit[i].TargetingOrder = data.ParsedTargetingOrder;
                kit[i].TargetingStat = data.ParsedTargetingStat;
                kit[i].InitialCooldown = data.InitialCooldown;
                kit[i].MaxUsesPerBattle = data.MaxUsesPerBattle;
                kit[i].Effects[0].HitCount = data.HitCount;
                kit[i].Effects[0].ExecuteBonusPercent = data.ExecuteBonusPercent;

                foreach (EnemyEffectData extra in data.Effects ?? new EnemyEffectData[0])
                {
                    if (extra.Parsed != null)
                    {
                        kit[i].Effects.Add(extra.Parsed);
                    }
                }
            }

            return kit;
        }

        /// <summary>A fresh loadout (fresh cooldown counters) over shared, read-only skill assets.</summary>
        public static SkillLoadout Loadout(SkillSO[] kit)
        {
            return new SkillLoadout(kit);
        }

        /// <summary>The kit's physical single-target skill: Strike, or Shot for a Ranged beast.</summary>
        public static bool IsPhysicalSingle(SkillSO skill)
        {
            return skill != null && (skill.SkillId == SimOptions.StrikeId || skill.SkillId == SimOptions.ShotId);
        }

        /// <summary>The power of the physical single-target skill a beast of this stance carries.</summary>
        public static float PhysicalSinglePower(CombatStance stance)
        {
            return stance == CombatStance.Ranged ? SimOptions.ShotPower : SimOptions.StrikePower;
        }

        public static bool IsBlast(SkillSO skill)
        {
            return skill != null && skill.SkillId == SimOptions.BlastId;
        }

        /// <summary>
        /// The Burst's first-firing (physical) half: one fire per Burst use. Its target count is the
        /// number of enemies inside the radius; the special half, firing second, can find some of
        /// them already defeated, so it would under-count.
        /// </summary>
        public static bool IsBurstUse(SkillSO skill)
        {
            return skill != null && skill.SkillId == SimOptions.BurstPhysicalId;
        }

        private static SkillSO BuildSkill(string id, DamageCategory category, SkillTargetShape shape, float power, int range, int cooldown, Element element)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            skill.name = id;
            skill.SkillId = id;
            skill.DisplayName = id;
            skill.TargetShape = shape;
            skill.Range = range;
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Lowest;
            skill.Cooldown = cooldown;
            skill.Element = element;
            skill.Category = category;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power });
            return skill;
        }
    }
}
