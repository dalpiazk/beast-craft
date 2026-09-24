using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Progression;

namespace BeastCraft.Skills
{
    /// <summary>
    /// Copies <see cref="SkillLibraryData"/> entries onto the runtime objects: the one field mapping
    /// shared by the Editor importer (which applies it to existing or new assets, keeping their
    /// GUIDs) and the balance simulator (which applies it to fresh in-memory instances). Only the
    /// authored-in-JSON fields are written (the icon's art key included: <c>ArtKey</c>).
    /// <para>
    /// Expects data that passed <see cref="SkillLibraryValidator.Validate(SkillLibraryData)"/>: an enum
    /// string that does not parse falls back to the field's default rather than throwing, in keeping
    /// with the battle namespace's non-throwing stance, but a validated file never hits that.
    /// </para>
    /// </summary>
    public static class SkillLibraryBuilder
    {
        /// <summary>Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="skill"/>.</summary>
        public static void ApplySkill(SkillData data, SkillSO skill)
        {
            skill.SkillId = data.SkillId;
            skill.DisplayName = data.DisplayName;
            skill.Description = data.Description;
            skill.ArtKey = string.IsNullOrEmpty(data.ArtKey) ? null : data.ArtKey;
            skill.ResourceCost = data.ResourceCost;
            skill.Cooldown = data.Cooldown;
            skill.InitialCooldown = data.InitialCooldown < 0 ? SkillSO.UseCooldownAsInitial : data.InitialCooldown;
            skill.MaxUsesPerBattle = data.MaxUsesPerBattle;
            skill.TargetShape = SkillLibraryValidator.ParseOr(data.TargetShape, SkillTargetShape.SingleTarget);
            skill.Range = data.Range;
            skill.TargetSide = SkillLibraryValidator.ParseOr(data.TargetSide, SkillTargetSide.Enemy);
            skill.TargetingCriterion = SkillLibraryValidator.ParseOr(data.TargetingCriterion, SkillTargetingCriterion.Distance);
            skill.TargetingOrder = SkillLibraryValidator.ParseOr(data.TargetingOrder, SkillTargetingOrder.Lowest);
            skill.TargetingStat = SkillLibraryValidator.ParseOr(data.TargetingStat, StatType.HP);
            skill.Element = SkillLibraryValidator.ParseOr(data.Element, Element.None);
            skill.Category = SkillLibraryValidator.ParseOr(data.Category, DamageCategory.Physical);
            skill.Effects = BuildEffects(data.Effects);
            skill.Progression = BuildProgression(data.Progression);
        }

        /// <summary>Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="passive"/>.</summary>
        public static void ApplyPassive(PassiveData data, PassiveSkillSO passive)
        {
            passive.PassiveId = data.PassiveId;
            passive.DisplayName = data.DisplayName;
            passive.Description = data.Description;
            passive.ArtKey = string.IsNullOrEmpty(data.ArtKey) ? null : data.ArtKey;
            passive.Trigger = SkillLibraryValidator.ParseOr(data.Trigger, PassiveTrigger.Aura);
            passive.HpThresholdPercent = data.HpThresholdPercent;
            passive.ProcChance = data.ProcChance;
            passive.MaxTriggersPerBattle = data.MaxTriggersPerBattle;
            passive.InternalCooldown = data.InternalCooldown;
            passive.TargetScope = SkillLibraryValidator.ParseOr(data.TargetScope, PassiveTarget.AllAllies);
            passive.Element = SkillLibraryValidator.ParseOr(data.Element, Element.None);
            passive.Category = SkillLibraryValidator.ParseOr(data.Category, DamageCategory.Physical);
            passive.Effects = BuildEffects(data.Effects);
            passive.Progression = BuildProgression(data.Progression);
        }

        /// <summary>Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="material"/>.</summary>
        public static void ApplyMaterial(SkillMaterialData data, SkillMaterialSO material)
        {
            material.MaterialId = data.MaterialId;
            material.DisplayName = data.DisplayName;
            material.Description = data.Description;
            material.Tier = data.Tier;
            material.XpValue = data.XpValue;
        }

        /// <summary>Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="bond"/> (lists replaced outright).</summary>
        public static void ApplyTeamBond(TeamBondData data, TeamBondSO bond)
        {
            bond.BondId = data.BondId;
            bond.DisplayName = data.DisplayName;
            bond.Description = data.Description;
            bond.Condition = SkillLibraryValidator.ParseOr(data.Condition, TeamBondCondition.Stance);
            bond.Stance = SkillLibraryValidator.ParseOr(data.Stance, CombatStance.Vanguard);
            bond.Scope = SkillLibraryValidator.ParseOr(data.Scope, TeamBondScope.Members);
            bond.PerCount = data.PerCount;
            bond.MaxCount = data.PerCount ? data.MaxCount : 0;
            bond.Elements = new List<Element>();
            foreach (string element in data.Elements ?? new string[0])
            {
                bond.Elements.Add(SkillLibraryValidator.ParseOr(element, Element.None));
            }

            bond.SpeciesIds = new List<string>(data.Species ?? new string[0]);
            bond.Tiers = new List<TeamBondTier>();
            foreach (TeamBondTierData tier in data.Tiers ?? new TeamBondTierData[0])
            {
                if (tier != null)
                {
                    bond.Tiers.Add(new TeamBondTier { MinCount = tier.MinCount, Effects = BuildEffects(tier.Effects), Reaction = BuildReaction(tier.Reaction) });
                }
            }
        }

        /// <summary>A fresh <see cref="BondReaction"/> from <paramref name="data"/>; an inert one (<see cref="BondTrigger.None"/>) when it is missing or names no trigger.</summary>
        public static BondReaction BuildReaction(BondReactionData data)
        {
            if (data == null || !data.IsSet)
            {
                return new BondReaction();
            }

            return new BondReaction
            {
                Trigger = SkillLibraryValidator.ParseOr(data.Trigger, BondTrigger.None),
                Action = SkillLibraryValidator.ParseOr(data.Action, BondAction.Apply),
                Target = SkillLibraryValidator.ParseOr(data.Target, BondReactionTarget.TriggerTarget),
                TriggerFilter = SkillLibraryValidator.ParseOr(data.TriggerFilter, BondTriggerFilter.Any),
                ReactorOrder = SkillLibraryValidator.ParseOr(data.ReactorOrder, BondReactorOrder.TeamOrder),
                Chance = data.Chance,
                Cooldown = data.Cooldown,
                MaxPerMember = data.MaxPerMember,
                MaxPerTriggerUnit = data.MaxPerTriggerUnit,
                MaxPerBattle = data.MaxPerBattle,
                Range = data.Range,
                HpThresholdPercent = data.HpThresholdPercent,
                Effects = BuildEffects(data.Effects),
            };
        }

        /// <summary>A fresh <see cref="SkillEffect"/> for each entry, in order (null entries skipped).</summary>
        public static List<SkillEffect> BuildEffects(EffectData[] effects)
        {
            List<SkillEffect> result = new List<SkillEffect>();
            if (effects == null)
            {
                return result;
            }

            foreach (EffectData data in effects)
            {
                if (data != null)
                {
                    result.Add(BuildEffect(data));
                }
            }

            return result;
        }

        /// <summary>A fresh <see cref="SkillEffect"/> with every field copied from <paramref name="data"/>.</summary>
        public static SkillEffect BuildEffect(EffectData data)
        {
            return new SkillEffect
            {
                EffectType = SkillLibraryValidator.ParseOr(data.EffectType, SkillEffectType.Damage),
                AffectedStat = SkillLibraryValidator.ParseOr(data.AffectedStat, StatType.Attack),
                Magnitude = data.Magnitude,
                DurationTurns = data.DurationTurns,
                Status = SkillLibraryValidator.ParseOr(data.Status, StatusType.None),
                Chance = data.Chance,
                MaxStacks = data.MaxStacks,
                IsPercent = data.IsPercent,
                HitCount = data.HitCount,
                ExecuteBonusPercent = data.ExecuteBonusPercent,
            };
        }

        /// <summary>
        /// A fresh <see cref="SkillProgressionDefinition"/> from <paramref name="data"/>. The tier
        /// list is replaced outright (an authored empty list means no gates), never merged with the
        /// class's default gates. A null block reads as the definition's defaults.
        /// </summary>
        public static SkillProgressionDefinition BuildProgression(ProgressionData data)
        {
            SkillProgressionDefinition definition = new SkillProgressionDefinition();
            if (data == null)
            {
                return definition;
            }

            definition.MaxLevel = data.MaxLevel;
            definition.MagnitudeGrowthPerLevel = data.MagnitudeGrowthPerLevel;
            definition.Tiers = new List<SkillTierDefinition>();

            foreach (TierData tier in data.Tiers ?? new TierData[0])
            {
                if (tier == null)
                {
                    continue;
                }

                definition.Tiers.Add(new SkillTierDefinition
                {
                    ThresholdLevel = tier.ThresholdLevel,
                    RequiredMaterialTier = tier.RequiredMaterialTier,
                    CooldownReduction = tier.CooldownReduction,
                    BonusEffects = BuildEffects(tier.BonusEffects),
                });
            }

            return definition;
        }

        /// <summary>
        /// The tier a skill has necessarily reached to stand at <paramref name="level"/>: the number
        /// of gates whose threshold is below it (a skill at a gate's threshold level has not passed
        /// that gate yet; one above it must have). For tooling that fields a skill "at level N"
        /// without a save file, such as the balance simulator's <c>--skill-level</c>.
        /// </summary>
        public static int TierForLevel(SkillProgressionDefinition definition, int level)
        {
            if (definition == null || definition.Tiers == null)
            {
                return 0;
            }

            int clamped = definition.ClampLevel(level);
            int tier = 0;
            for (int i = 0; i < definition.Tiers.Count; i++)
            {
                SkillTierDefinition gate = definition.Tiers[i];
                if (gate != null && definition.ClampLevel(gate.ThresholdLevel) < clamped)
                {
                    tier = i + 1;
                }
            }

            return tier;
        }
    }
}
