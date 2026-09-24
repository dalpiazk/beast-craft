using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One skill as a battle uses it: the authored <see cref="SkillSO"/> plus the level and
    /// breakthrough tier its owner has brought it to. This is what a <see cref="SkillLoadout"/> slot
    /// holds and what a <see cref="SkillActivation"/> carries, so the level reaches
    /// <see cref="SkillEffectApplier"/> — and through it <see cref="DamageFormula"/> — without the
    /// battle knowing anything about XP, materials or save data.
    /// <para>
    /// <strong>What the level and tier change.</strong>
    /// <list type="bullet">
    /// <item><description>
    /// Every effect magnitude is multiplied by
    /// <see cref="SkillProgressionDefinition.GetMagnitudeMultiplier"/> of the level
    /// (<see cref="ScaleMagnitude"/>): damage power, heal amount, buff and debuff size. Durations
    /// are not scaled.
    /// </description></item>
    /// <item><description>
    /// Each passed tier's <see cref="SkillTierDefinition.CooldownReduction"/> comes off
    /// <see cref="SkillSO.Cooldown"/> (<see cref="EffectiveCooldown"/>), and its
    /// <see cref="SkillTierDefinition.BonusEffects"/> are appended to the effect list
    /// (<see cref="Effects"/>).
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Level 1, tier 0 is the authored skill exactly.</strong> The multiplier is not
    /// applied at all (not even as a multiply by 1), the cooldown is the authored one, and
    /// <see cref="Effects"/> is the asset's own list — so every path that builds a loadout from bare
    /// <see cref="SkillSO"/>s behaves bit for bit as it did before progression existed.
    /// </para>
    /// <para>
    /// Immutable: the level, tier and multiplier are captured at construction (clamped to the
    /// skill's <see cref="SkillSO.Progression"/>), matching how <see cref="SkillLoadout"/> captures
    /// cooldowns so a battle cannot change pace or power if progress is written mid-fight. A null
    /// skill is tolerated — level 1, tier 0, no effects — in keeping with the namespace's
    /// non-throwing stance.
    /// </para>
    /// </summary>
    public sealed class SkillInstance
    {
        private readonly double _multiplier;

        /// <summary>
        /// The skill at <paramref name="level"/> and <paramref name="tier"/>, each clamped into the
        /// skill's <see cref="SkillSO.Progression"/> (level into [1, max level], tier into
        /// [0, gate count]; a null definition reads as the defaults).
        /// </summary>
        public SkillInstance(SkillSO skill, int level = 1, int tier = 0)
        {
            SkillProgressionDefinition definition = Definition(skill);

            Skill = skill;
            Level = definition.ClampLevel(level);
            Tier = definition.ClampTier(tier);
            _multiplier = definition.GetMagnitudeMultiplier(Level);
        }

        /// <summary>
        /// The skill at the level and tier recorded in <paramref name="progress"/>; level 1, tier 0
        /// for a null progress. The progress's XP plays no part in battle.
        /// </summary>
        public static SkillInstance FromProgress(SkillSO skill, SkillProgress progress)
        {
            return progress == null ? new SkillInstance(skill) : new SkillInstance(skill, progress.Level, progress.Tier);
        }

        /// <summary>The authored skill.</summary>
        public SkillSO Skill { get; }

        /// <summary>The skill's level, 1-based, already clamped.</summary>
        public int Level { get; }

        /// <summary>How many breakthrough gates are passed, already clamped.</summary>
        public int Tier { get; }

        /// <summary>
        /// The factor every effect magnitude is scaled by at <see cref="Level"/>; exactly 1 at level
        /// 1. See <see cref="SkillProgressionDefinition.GetMagnitudeMultiplier"/>.
        /// </summary>
        public double MagnitudeMultiplier
        {
            get { return _multiplier; }
        }

        /// <summary>
        /// <paramref name="magnitude"/> at this level: returned untouched at level 1, otherwise
        /// <c>(float)(magnitude * MagnitudeMultiplier)</c>.
        /// </summary>
        public float ScaleMagnitude(float magnitude)
        {
            return Level <= 1 ? magnitude : (float)(magnitude * _multiplier);
        }

        /// <summary>
        /// <see cref="SkillSO.Cooldown"/> less every passed tier's
        /// <see cref="SkillTierDefinition.CooldownReduction"/>, clamped at 0 (a cooldown of 0 fires
        /// every turn, see <see cref="SkillLoadout"/>). Read from the asset when asked;
        /// <see cref="SkillLoadout"/> captures it once, at construction.
        /// </summary>
        public int EffectiveCooldown
        {
            get
            {
                if (Skill == null)
                {
                    return 0;
                }

                int cooldown = Skill.Cooldown;
                SkillProgressionDefinition definition = Definition(Skill);

                for (int i = 0; i < Tier; i++)
                {
                    SkillTierDefinition gate = definition.GetTier(i);

                    if (gate != null)
                    {
                        cooldown -= gate.CooldownReduction;
                    }
                }

                return cooldown < 0 ? 0 : cooldown;
            }
        }

        /// <summary>
        /// The effects this skill applies, unscaled: <see cref="SkillSO.Effects"/>, then each passed
        /// tier's <see cref="SkillTierDefinition.BonusEffects"/> in tier order. At tier 0 this is
        /// the asset's own list, live; above it, a fresh list composed on each read (effects are
        /// shared references, not copies). Never <c>null</c>; may contain null entries, which the
        /// applier skips. Magnitudes are scaled when applied, through <see cref="ScaleMagnitude"/>.
        /// </summary>
        public IReadOnlyList<SkillEffect> Effects
        {
            get
            {
                if (Skill == null)
                {
                    return NoEffects;
                }

                List<SkillEffect> baseEffects = Skill.Effects;

                if (Tier == 0)
                {
                    return (IReadOnlyList<SkillEffect>)baseEffects ?? NoEffects;
                }

                List<SkillEffect> composed = baseEffects == null ? new List<SkillEffect>() : new List<SkillEffect>(baseEffects);
                SkillProgressionDefinition definition = Definition(Skill);

                for (int i = 0; i < Tier; i++)
                {
                    SkillTierDefinition gate = definition.GetTier(i);

                    if (gate != null && gate.BonusEffects != null)
                    {
                        composed.AddRange(gate.BonusEffects);
                    }
                }

                return composed;
            }
        }

        private static readonly SkillEffect[] NoEffects = new SkillEffect[0];

        private static SkillProgressionDefinition Definition(SkillSO skill)
        {
            return skill == null || skill.Progression == null ? SkillProgressionDefinition.Fallback : skill.Progression;
        }
    }
}
