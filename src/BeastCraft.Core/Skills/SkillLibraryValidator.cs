using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;

namespace BeastCraft.Skills
{
    /// <summary>
    /// Structural integrity checks for <see cref="SkillLibraryData"/>: the rules every library file
    /// must satisfy to import at all. Ids are present, unique across the whole file and lowercase
    /// snake_case; references resolve; every enum name parses; numbers sit in sane bands (a chance is
    /// a percent, a knockback is a whole number of hexes, a timed status has a duration); avatar
    /// actives keep to the position-free shapes; every species kit is complete and usable (at least
    /// <see cref="MinLearnableSkills"/> learnable skills, a full default loadout learnable by level
    /// <see cref="MaxDefaultLearnLevel"/>, one skill in it that walks the beast toward its enemies);
    /// given the roster, every species has a kit and a <see cref="CombatStance.Ranged"/>
    /// beast's defaults hold no melee skill it would never walk in to use; and every team bond has a
    /// valid condition set, strictly rising tiers the set can reach, and only effects a bond may
    /// carry (a <c>BuffStat</c> other than HP, or a <c>Shield</c>, always landing).
    /// <para>
    /// Balance guidelines — the power budget, which beast carries which signature mechanic, five or
    /// six skills per beast — are deliberately NOT here; they are tests over the current library
    /// (<c>SkillLibraryTests</c>), and the balance simulator is free to move them.
    /// </para>
    /// </summary>
    public static class SkillLibraryValidator
    {
        /// <summary>The fewest skills a species may list as learnable.</summary>
        public const int MinLearnableSkills = 5;

        /// <summary>Every default-loadout skill must be learnable at or below this beast level.</summary>
        public const int MaxDefaultLearnLevel = 5;

        /// <summary>The highest beast level a skill may be learned at (the growth curves' max level).</summary>
        public const int MaxLearnLevel = 100;

        public const int MaxCooldown = 10;
        public const int MaxUses = 10;
        public const int MaxRange = 8;
        public const int MaxDuration = 10;
        public const int MaxStacksLimit = 10;
        public const int MaxHitCount = 8;
        public const int MaxExecuteBonus = 200;
        public const int MaxKnockback = 4;
        public const float MaxDamagePower = 400f;
        public const float MaxOtherMagnitude = 200f;
        public const int MaxSkillLevel = 100;
        public const float MaxGrowthPerLevel = 20f;

        /// <summary>Returns every problem found, without the roster cross-checks.</summary>
        public static List<string> Validate(SkillLibraryData library)
        {
            return Validate(library, null);
        }

        /// <summary>
        /// Returns every problem found; an empty list means the library is importable. With a
        /// <paramref name="roster"/>, also checks that the kits and the roster name the same species
        /// and that Ranged species' defaults are stance-consistent.
        /// </summary>
        public static List<string> Validate(SkillLibraryData library, BeastRosterData roster)
        {
            List<string> errors = new List<string>();

            if (library == null)
            {
                errors.Add("Skill library is null (the JSON did not parse).");
                return errors;
            }

            if (library.SchemaVersion != SkillLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + library.SchemaVersion + "; this code reads version " + SkillLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> allIds = new HashSet<string>();
            HashSet<int> materialTiers = ValidateMaterials(library.Materials, allIds, errors);

            Dictionary<string, SkillData> beastSkills = ValidateSkills(library.BeastSkills, "Beast skill", false, allIds, materialTiers, errors);
            ValidateSkills(library.AvatarActives, "Avatar active", true, allIds, materialTiers, errors);
            Dictionary<string, PassiveData> passives = ValidatePassives(library.AvatarPassives, allIds, materialTiers, errors);

            if (library.AvatarActives == null || library.AvatarActives.Length < SkillLibraryData.AvatarDefaultActiveCount)
            {
                errors.Add("AvatarActives needs at least " + SkillLibraryData.AvatarDefaultActiveCount + " skills (the first ones are the default loadout).");
            }

            if (library.AvatarDefaultPassives == null || library.AvatarDefaultPassives.Length == 0)
            {
                errors.Add("AvatarDefaultPassives is empty.");
            }

            ValidateIdList(library.AvatarDefaultPassives, "AvatarDefaultPassives", AvatarSkillBook.PassiveSlotCount, passives, errors);

            ValidateKits(library.SpeciesKits, beastSkills, roster, errors);
            ValidateTeamBonds(library.TeamBonds, allIds, roster, errors);
            return errors;
        }

        /// <summary>
        /// Parses an enum member name exactly as written (case-sensitive, names only — a numeric
        /// string such as <c>"3"</c> is rejected, since the JSON is meant to be read by people). A
        /// null or empty string is the field's default and parses successfully.
        /// </summary>
        public static bool TryParse<T>(string name, T fallback, out T value) where T : struct
        {
            value = fallback;

            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!Enum.IsDefined(typeof(T), name))
            {
                return false;
            }

            value = (T)Enum.Parse(typeof(T), name);
            return true;
        }

        /// <summary><see cref="TryParse{T}"/>'s value, or <paramref name="fallback"/> when it does not parse.</summary>
        public static T ParseOr<T>(string name, T fallback) where T : struct
        {
            return TryParse(name, fallback, out T value) ? value : fallback;
        }

        /// <summary>
        /// Whether a skill of this shape picks a focus and walks toward it when out of range
        /// (<c>SingleTarget</c> and <c>Line</c>, as <c>BattleTurnExecutor</c> does): the only kind of
        /// skill that moves a beast.
        /// </summary>
        public static bool Approaches(SkillTargetShape shape)
        {
            return shape == SkillTargetShape.SingleTarget || shape == SkillTargetShape.Line;
        }

        /// <summary>Whether a shape's footprint is measured from the caster's tile (everything but Self / AllEnemies / AllAllies).</summary>
        public static bool IsPositional(SkillTargetShape shape)
        {
            return shape != SkillTargetShape.Self && shape != SkillTargetShape.AllEnemies && shape != SkillTargetShape.AllAllies;
        }

        /// <summary>
        /// Whether a <see cref="CombatStance.Ranged"/> beast would sit on this skill: an enemy-side
        /// positional skill of range 1 or less (a Ranged unit never walks into melee, and a range-1
        /// burst around a unit that keeps its distance rarely catches anyone).
        /// </summary>
        public static bool IsMeleeForRanged(SkillData skill)
        {
            SkillTargetShape shape = ParseOr(skill.TargetShape, SkillTargetShape.SingleTarget);
            SkillTargetSide side = ParseOr(skill.TargetSide, SkillTargetSide.Enemy);
            return IsPositional(shape) && side == SkillTargetSide.Enemy && skill.Range <= 1;
        }

        private static HashSet<int> ValidateMaterials(SkillMaterialData[] materials, HashSet<string> allIds, List<string> errors)
        {
            HashSet<int> tiers = new HashSet<int>();

            if (materials == null || materials.Length == 0)
            {
                errors.Add("No materials defined.");
                return tiers;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                SkillMaterialData m = materials[i];
                if (m == null)
                {
                    errors.Add("Material #" + i + " is null.");
                    continue;
                }

                string label = "Material '" + m.MaterialId + "'";
                CheckId(m.MaterialId, label, "MaterialId", allIds, errors);
                CheckText(m.DisplayName, m.Description, label, errors);

                if (m.Tier < 1)
                {
                    errors.Add(label + ": Tier is " + m.Tier + "; it must be at least 1.");
                }
                else
                {
                    tiers.Add(m.Tier);
                }

                if (m.XpValue < 1)
                {
                    errors.Add(label + ": XpValue is " + m.XpValue + "; it must be at least 1.");
                }
            }

            return tiers;
        }

        private static Dictionary<string, SkillData> ValidateSkills(SkillData[] skills, string kind, bool avatar, HashSet<string> allIds, HashSet<int> materialTiers,
                                                                    List<string> errors)
        {
            Dictionary<string, SkillData> byId = new Dictionary<string, SkillData>();

            if (skills == null || skills.Length == 0)
            {
                errors.Add("No " + kind.ToLowerInvariant() + "s defined.");
                return byId;
            }

            for (int i = 0; i < skills.Length; i++)
            {
                SkillData s = skills[i];
                if (s == null)
                {
                    errors.Add(kind + " #" + i + " is null.");
                    continue;
                }

                string label = kind + " '" + s.SkillId + "'";
                if (CheckId(s.SkillId, label, "SkillId", allIds, errors))
                {
                    byId[s.SkillId] = s;
                }

                CheckText(s.DisplayName, s.Description, label, errors);
                CheckArtKey(s.ArtKey, label, errors);
                CheckEnum<SkillTargetShape>(s.TargetShape, label, "TargetShape", errors);
                CheckEnum<SkillTargetSide>(s.TargetSide, label, "TargetSide", errors);
                CheckEnum<SkillTargetingCriterion>(s.TargetingCriterion, label, "TargetingCriterion", errors);
                CheckEnum<SkillTargetingOrder>(s.TargetingOrder, label, "TargetingOrder", errors);
                CheckEnum<StatType>(s.TargetingStat, label, "TargetingStat", errors);
                CheckEnum<Element>(s.Element, label, "Element", errors);
                CheckEnum<DamageCategory>(s.Category, label, "Category", errors);

                CheckBand(s.ResourceCost, 0, 1000, label, "ResourceCost", errors);
                CheckBand(s.Cooldown, 0, MaxCooldown, label, "Cooldown", errors);
                CheckBand(s.InitialCooldown, -1, MaxCooldown, label, "InitialCooldown", errors);
                CheckBand(s.MaxUsesPerBattle, 0, MaxUses, label, "MaxUsesPerBattle", errors);

                SkillTargetShape shape = ParseOr(s.TargetShape, SkillTargetShape.SingleTarget);
                if (IsPositional(shape))
                {
                    CheckBand(s.Range, 1, MaxRange, label, "Range", errors);
                }
                else
                {
                    CheckBand(s.Range, 0, MaxRange, label, "Range", errors);
                }

                if (avatar && IsPositional(shape))
                {
                    errors.Add(label + ": avatar skills must use Self, AllAllies or AllEnemies (the avatar is not on the grid), not " + shape + ".");
                }

                if (ParseOr(s.TargetingCriterion, SkillTargetingCriterion.Distance) == SkillTargetingCriterion.Random)
                {
                    errors.Add(label + ": TargetingCriterion Random is not used by authored skills (it spends the battle rng); pick a rule.");
                }

                CheckEffects(s.Effects, label, "Effects", avatar, errors);
                CheckProgression(s.Progression, s.Cooldown, label, avatar, false, materialTiers, errors);
            }

            return byId;
        }

        private static Dictionary<string, PassiveData> ValidatePassives(PassiveData[] passives, HashSet<string> allIds, HashSet<int> materialTiers, List<string> errors)
        {
            Dictionary<string, PassiveData> byId = new Dictionary<string, PassiveData>();

            if (passives == null || passives.Length == 0)
            {
                errors.Add("No avatar passives defined.");
                return byId;
            }

            for (int i = 0; i < passives.Length; i++)
            {
                PassiveData p = passives[i];
                if (p == null)
                {
                    errors.Add("Avatar passive #" + i + " is null.");
                    continue;
                }

                string label = "Avatar passive '" + p.PassiveId + "'";
                if (CheckId(p.PassiveId, label, "PassiveId", allIds, errors))
                {
                    byId[p.PassiveId] = p;
                }

                CheckText(p.DisplayName, p.Description, label, errors);
                CheckArtKey(p.ArtKey, label, errors);
                CheckEnum<PassiveTrigger>(p.Trigger, label, "Trigger", errors);
                CheckEnum<PassiveTarget>(p.TargetScope, label, "TargetScope", errors);
                CheckEnum<Element>(p.Element, label, "Element", errors);
                CheckEnum<DamageCategory>(p.Category, label, "Category", errors);
                CheckBand(p.ProcChance, 1, 100, label, "ProcChance", errors);
                CheckBand(p.MaxTriggersPerBattle, 0, 100, label, "MaxTriggersPerBattle", errors);
                CheckBand(p.InternalCooldown, 0, MaxCooldown, label, "InternalCooldown", errors);

                PassiveTrigger trigger = ParseOr(p.Trigger, PassiveTrigger.Aura);
                PassiveTarget scope = ParseOr(p.TargetScope, PassiveTarget.AllAllies);

                if (trigger == PassiveTrigger.AllyBelowHpPercent)
                {
                    CheckBand(p.HpThresholdPercent, 1, 99, label, "HpThresholdPercent", errors);
                }

                if (scope == PassiveTarget.TriggeringUnit && (trigger == PassiveTrigger.AllyDefeated || trigger == PassiveTrigger.Aura ||
                                                              trigger == PassiveTrigger.BattleStart))
                {
                    errors.Add(label + ": a " + trigger + " passive has no living triggering unit; use AllAllies, AllEnemies or LowestHpFractionAlly.");
                }

                CheckEffects(p.Effects, label, "Effects", true, errors);

                if (trigger == PassiveTrigger.Aura && p.Effects != null)
                {
                    foreach (EffectData effect in p.Effects)
                    {
                        SkillEffectType type = effect == null ? SkillEffectType.Damage : ParseOr(effect.EffectType, SkillEffectType.Damage);
                        if (effect != null && (type == SkillEffectType.BuffStat || type == SkillEffectType.DebuffStat) && effect.DurationTurns != 0)
                        {
                            errors.Add(label + ": an Aura's stat changes must have DurationTurns 0 (it lasts the battle).");
                        }
                    }
                }

                CheckProgression(p.Progression, 0, label, true, true, materialTiers, errors);
            }

            return byId;
        }

        internal static void CheckEffects(EffectData[] effects, string label, string field, bool avatar, List<string> errors)
        {
            if (effects == null || effects.Length == 0)
            {
                if (field == "Effects")
                {
                    errors.Add(label + ": has no effects.");
                }

                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                EffectData e = effects[i];
                string at = label + " " + field + "[" + i + "]";
                if (e == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                bool typeOk = CheckEnum<SkillEffectType>(e.EffectType, at, "EffectType", errors);
                CheckEnum<StatType>(e.AffectedStat, at, "AffectedStat", errors);
                bool statusOk = CheckEnum<StatusType>(e.Status, at, "Status", errors);
                CheckBand(e.Chance, 1, 100, at, "Chance", errors);
                CheckBand(e.MaxStacks, 1, MaxStacksLimit, at, "MaxStacks", errors);
                CheckBand(e.HitCount, 1, MaxHitCount, at, "HitCount", errors);
                CheckBand(e.ExecuteBonusPercent, 0, MaxExecuteBonus, at, "ExecuteBonusPercent", errors);
                CheckBand(e.DurationTurns, 0, MaxDuration, at, "DurationTurns", errors);

                if (!typeOk || !statusOk)
                {
                    continue;
                }

                SkillEffectType type = ParseOr(e.EffectType, SkillEffectType.Damage);
                StatusType status = ParseOr(e.Status, StatusType.None);

                if (type == SkillEffectType.ApplyStatus && status == StatusType.None)
                {
                    errors.Add(at + ": an ApplyStatus effect needs a Status.");
                }

                if (type != SkillEffectType.ApplyStatus && status != StatusType.None)
                {
                    errors.Add(at + ": Status is only read by ApplyStatus effects.");
                }

                if (type != SkillEffectType.Damage && (e.HitCount != 1 || e.ExecuteBonusPercent != 0))
                {
                    errors.Add(at + ": HitCount and ExecuteBonusPercent are only read by Damage effects.");
                }

                if ((type == SkillEffectType.Damage || type == SkillEffectType.Heal || type == SkillEffectType.Cleanse) && e.DurationTurns != 0)
                {
                    errors.Add(at + ": a " + type + " effect is instant; DurationTurns must be 0.");
                }

                if ((status == StatusType.Taunt || status == StatusType.Stun) && e.Magnitude < 0f)
                {
                    errors.Add(at + ": Magnitude must not be negative.");
                }

                if (type == SkillEffectType.Damage && (e.Magnitude <= 0f || e.Magnitude > MaxDamagePower))
                {
                    errors.Add(at + ": damage power is " + e.Magnitude + "; it must be above 0 and at most " + MaxDamagePower + ".");
                }
                else if (type != SkillEffectType.Damage && type != SkillEffectType.Cleanse && status != StatusType.Taunt && status != StatusType.Stun &&
                         (e.Magnitude <= 0f || e.Magnitude > MaxOtherMagnitude))
                {
                    errors.Add(at + ": Magnitude is " + e.Magnitude + "; it must be above 0 and at most " + MaxOtherMagnitude + ".");
                }

                if ((type == SkillEffectType.BuffStat || type == SkillEffectType.DebuffStat) && e.IsPercent && e.Magnitude > 100f)
                {
                    errors.Add(at + ": a percent stat change above 100 is not a sane value.");
                }

                if (type == SkillEffectType.ApplyStatus)
                {
                    if (status == StatusType.Knockback)
                    {
                        if (avatar)
                        {
                            errors.Add(at + ": Knockback pushes away from the caster's tile and must not be authored on the avatar.");
                        }

                        if (e.Magnitude < 1f || e.Magnitude > MaxKnockback || e.Magnitude != (float)Math.Floor(e.Magnitude))
                        {
                            errors.Add(at + ": a knockback's Magnitude is whole hexes, 1 to " + MaxKnockback + ".");
                        }

                        if (e.DurationTurns != 0)
                        {
                            errors.Add(at + ": a knockback is instant; DurationTurns must be 0.");
                        }
                    }
                    else if (e.DurationTurns < 1)
                    {
                        errors.Add(at + ": a " + status + " needs DurationTurns of at least 1.");
                    }
                }
            }
        }

        private static void CheckProgression(ProgressionData progression, int cooldown, string label, bool avatar, bool passive, HashSet<int> materialTiers,
                                             List<string> errors)
        {
            if (progression == null)
            {
                errors.Add(label + ": has no Progression block.");
                return;
            }

            CheckBand(progression.MaxLevel, 1, MaxSkillLevel, label, "Progression.MaxLevel", errors);
            if (progression.MagnitudeGrowthPerLevel < 0f || progression.MagnitudeGrowthPerLevel > MaxGrowthPerLevel)
            {
                errors.Add(label + ": Progression.MagnitudeGrowthPerLevel is " + progression.MagnitudeGrowthPerLevel + "; it must be 0 to " + MaxGrowthPerLevel + ".");
            }

            TierData[] tiers = progression.Tiers ?? new TierData[0];
            int previous = 0;
            int totalReduction = 0;
            for (int i = 0; i < tiers.Length; i++)
            {
                TierData tier = tiers[i];
                string at = label + " Progression.Tiers[" + i + "]";
                if (tier == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (tier.ThresholdLevel <= previous || tier.ThresholdLevel < 2 || tier.ThresholdLevel > progression.MaxLevel)
                {
                    errors.Add(at + ": ThresholdLevel " + tier.ThresholdLevel + " must rise strictly, from 2 up to MaxLevel.");
                }

                previous = tier.ThresholdLevel;

                if (!materialTiers.Contains(tier.RequiredMaterialTier))
                {
                    errors.Add(at + ": no material has RequiredMaterialTier " + tier.RequiredMaterialTier + ".");
                }

                if (tier.CooldownReduction < 0)
                {
                    errors.Add(at + ": CooldownReduction must not be negative.");
                }
                else if (passive && tier.CooldownReduction > 0)
                {
                    errors.Add(at + ": CooldownReduction means nothing to a passive.");
                }

                totalReduction += tier.CooldownReduction;
                CheckEffects(tier.BonusEffects, at, "BonusEffects", avatar, errors);
            }

            if (totalReduction > 0 && cooldown - totalReduction < 1)
            {
                errors.Add(label + ": the tiers' CooldownReduction (" + totalReduction + ") takes Cooldown " + cooldown +
                           " below 1, where it no longer changes anything.");
            }
        }

        private static void ValidateKits(SpeciesKitData[] kits, Dictionary<string, SkillData> beastSkills, BeastRosterData roster, List<string> errors)
        {
            Dictionary<string, SpeciesData> rosterById = new Dictionary<string, SpeciesData>();
            if (roster != null && roster.Species != null)
            {
                foreach (SpeciesData species in roster.Species)
                {
                    if (species != null && !string.IsNullOrEmpty(species.SpeciesId))
                    {
                        rosterById[species.SpeciesId] = species;
                    }
                }
            }

            if (kits == null || kits.Length == 0)
            {
                errors.Add("No species kits defined.");
                return;
            }

            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < kits.Length; i++)
            {
                SpeciesKitData kit = kits[i];
                if (kit == null)
                {
                    errors.Add("Species kit #" + i + " is null.");
                    continue;
                }

                string label = "Species kit '" + kit.SpeciesId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(kit.SpeciesId))
                {
                    errors.Add(label + ": SpeciesId must be lowercase snake_case.");
                }
                else if (!seen.Add(kit.SpeciesId))
                {
                    errors.Add(label + ": duplicate SpeciesId.");
                }

                if (roster != null && !string.IsNullOrEmpty(kit.SpeciesId) && !rosterById.ContainsKey(kit.SpeciesId))
                {
                    errors.Add(label + ": not a species in the roster.");
                }

                Dictionary<string, int> learnLevel = new Dictionary<string, int>();
                LearnEntryData[] entries = kit.LearnableSkills ?? new LearnEntryData[0];
                for (int e = 0; e < entries.Length; e++)
                {
                    LearnEntryData entry = entries[e];
                    if (entry == null)
                    {
                        errors.Add(label + ": LearnableSkills[" + e + "] is null.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(entry.SkillId) || !beastSkills.ContainsKey(entry.SkillId))
                    {
                        errors.Add(label + ": learnable skill '" + entry.SkillId + "' is not a beast skill.");
                    }
                    else if (learnLevel.ContainsKey(entry.SkillId))
                    {
                        errors.Add(label + ": learns '" + entry.SkillId + "' twice.");
                    }
                    else
                    {
                        learnLevel[entry.SkillId] = entry.Level;
                    }

                    CheckBand(entry.Level, 1, MaxLearnLevel, label + " '" + entry.SkillId + "'", "Level", errors);
                }

                if (entries.Length < MinLearnableSkills)
                {
                    errors.Add(label + ": learns " + entries.Length + " skills; every species needs at least " + MinLearnableSkills + ".");
                }

                string[] defaults = kit.DefaultLoadout ?? new string[0];
                if (defaults.Length != BeastSkillBook.EquipSlotCount)
                {
                    errors.Add(label + ": DefaultLoadout has " + defaults.Length + " skills; it needs exactly " + BeastSkillBook.EquipSlotCount + ".");
                }

                HashSet<string> defaultSet = new HashSet<string>();
                bool approaches = false;
                SpeciesData rosterSpecies;
                rosterById.TryGetValue(kit.SpeciesId ?? string.Empty, out rosterSpecies);
                CombatStance stance = CombatStance.Vanguard;
                if (rosterSpecies != null)
                {
                    BeastRosterValidator.TryParseStance(rosterSpecies.Stance, out stance);
                }

                foreach (string id in defaults)
                {
                    if (!defaultSet.Add(id ?? string.Empty))
                    {
                        errors.Add(label + ": DefaultLoadout lists '" + id + "' twice.");
                    }

                    if (id == null || !learnLevel.TryGetValue(id, out int level))
                    {
                        errors.Add(label + ": default skill '" + id + "' is not in its LearnableSkills.");
                        continue;
                    }

                    if (level > MaxDefaultLearnLevel)
                    {
                        errors.Add(label + ": default skill '" + id + "' is learned at level " + level + "; defaults must be learnable by level " +
                                   MaxDefaultLearnLevel + ".");
                    }

                    SkillData skill = beastSkills[id];
                    SkillTargetShape shape = ParseOr(skill.TargetShape, SkillTargetShape.SingleTarget);
                    if (Approaches(shape) && ParseOr(skill.TargetSide, SkillTargetSide.Enemy) == SkillTargetSide.Enemy &&
                        !(stance == CombatStance.Ranged && skill.Range <= 1))
                    {
                        approaches = true;
                    }

                    if (stance == CombatStance.Ranged && IsMeleeForRanged(skill))
                    {
                        errors.Add(label + ": a Ranged beast's default '" + id + "' is a range-" + skill.Range + " enemy skill it would never walk in to use.");
                    }
                }

                if (defaults.Length > 0 && !approaches)
                {
                    errors.Add(label + ": no default skill is an enemy-side SingleTarget or Line skill it can walk in to use, so the beast would never move.");
                }
            }

            if (roster != null)
            {
                foreach (string speciesId in rosterById.Keys)
                {
                    if (!seen.Contains(speciesId))
                    {
                        errors.Add("Roster species '" + speciesId + "' has no species kit.");
                    }
                }
            }
        }

        /// <summary>A scaling bond's worst case (magnitude x MaxCount): at most this percent of a stat.</summary>
        public const float MaxScalingPercentStat = 20f;

        /// <summary>A scaling bond's worst case: at most this much flat <c>CritChance</c> (points).</summary>
        public const float MaxScalingCritChance = 15f;

        /// <summary>A scaling bond's worst case: a shield of at most this percent of the recipient's Defense.</summary>
        public const float MaxScalingShield = 60f;

        /// <summary>A scaling bond's worst case: at most this much <c>MoveRange</c>.</summary>
        public const float MaxScalingMoveRange = 1f;

        /// <summary>
        /// Team bonds (optional: none is fine). Ids share the library's id space; the condition's
        /// set is present and valid for its kind (a stance; two or more distinct elements; two or
        /// more distinct species, which must be roster species when the roster is given); tiers rise
        /// strictly from a MinCount of at least 2 up to what the set can reach; and every effect is
        /// one a bond may carry: it lands on the bond's own team at battle start, so only a
        /// <c>BuffStat</c> or a <c>Shield</c> status, always landing (Chance 100). A scaling bond
        /// (<c>PerCount</c>) has exactly one tier with 1 &lt;= MinCount &lt;= MaxCount &lt;= what the set
        /// can reach, and its worst case (each magnitude x MaxCount) stays within the caps
        /// (<see cref="MaxScalingPercentStat"/> and the rest); a tiered bond sets no MaxCount.
        /// </summary>
        private static void ValidateTeamBonds(TeamBondData[] bonds, HashSet<string> allIds, BeastRosterData roster, List<string> errors)
        {
            if (bonds == null)
            {
                return;
            }

            HashSet<string> rosterIds = new HashSet<string>();
            if (roster != null && roster.Species != null)
            {
                foreach (SpeciesData species in roster.Species)
                {
                    if (species != null && !string.IsNullOrEmpty(species.SpeciesId))
                    {
                        rosterIds.Add(species.SpeciesId);
                    }
                }
            }

            for (int i = 0; i < bonds.Length; i++)
            {
                TeamBondData b = bonds[i];
                if (b == null)
                {
                    errors.Add("Team bond #" + i + " is null.");
                    continue;
                }

                string label = "Team bond '" + b.BondId + "'";
                CheckId(b.BondId, label, "BondId", allIds, errors);
                CheckText(b.DisplayName, b.Description, label, errors);
                bool conditionOk = CheckEnum<TeamBondCondition>(b.Condition, label, "Condition", errors);
                CheckEnum<TeamBondScope>(b.Scope, label, "Scope", errors);
                TeamBondCondition condition = ParseOr(b.Condition, TeamBondCondition.Stance);
                string[] elements = b.Elements ?? new string[0];
                string[] species = b.Species ?? new string[0];

                // The most the condition can count: unbounded for a stance (any team size), the
                // set's size for elements and species.
                int reach = int.MaxValue;
                if (conditionOk)
                {
                    switch (condition)
                    {
                        case TeamBondCondition.Stance:
                            CheckEnum<CombatStance>(b.Stance, label, "Stance", errors);
                            if (elements.Length > 0 || species.Length > 0)
                            {
                                errors.Add(label + ": a Stance bond lists no Elements or Species.");
                            }

                            break;

                        case TeamBondCondition.Elements:
                            reach = CheckSet(elements, label, "Elements", errors, name =>
                            {
                                if (!TryParse(name, Element.None, out Element element) || string.IsNullOrEmpty(name) || element == Element.None)
                                {
                                    return "'" + name + "' is not an Element name (None is not allowed).";
                                }

                                return null;
                            });
                            if (species.Length > 0 || !string.IsNullOrEmpty(b.Stance))
                            {
                                errors.Add(label + ": an Elements bond lists no Species or Stance.");
                            }

                            break;

                        case TeamBondCondition.DistinctStances:
                            reach = Enum.GetValues(typeof(CombatStance)).Length;
                            if (elements.Length > 0 || species.Length > 0 || !string.IsNullOrEmpty(b.Stance))
                            {
                                errors.Add(label + ": a DistinctStances bond lists no Elements, Species or Stance.");
                            }

                            break;

                        case TeamBondCondition.Species:
                            reach = CheckSet(species, label, "Species", errors, id =>
                            {
                                if (!BeastRosterValidator.IsSnakeCaseId(id))
                                {
                                    return "'" + id + "' must be a lowercase snake_case SpeciesId.";
                                }

                                return roster != null && !rosterIds.Contains(id) ? "'" + id + "' is not a species in the roster." : null;
                            });
                            if (elements.Length > 0 || !string.IsNullOrEmpty(b.Stance))
                            {
                                errors.Add(label + ": a Species bond lists no Elements or Stance.");
                            }

                            break;
                    }
                }

                TeamBondTierData[] tiers = b.Tiers ?? new TeamBondTierData[0];
                if (tiers.Length == 0)
                {
                    errors.Add(label + ": has no Tiers.");
                }

                if (b.PerCount)
                {
                    ValidateScalingBond(b, tiers, reach, label, errors);
                    continue;
                }

                if (b.MaxCount != 0)
                {
                    errors.Add(label + ": MaxCount " + b.MaxCount + " is only for a PerCount (scaling) bond; a tiered bond leaves it 0.");
                }

                int previous = 1;
                for (int t = 0; t < tiers.Length; t++)
                {
                    TeamBondTierData tier = tiers[t];
                    string at = label + " Tiers[" + t + "]";
                    if (tier == null)
                    {
                        errors.Add(at + " is null.");
                        continue;
                    }

                    if (tier.MinCount <= previous || tier.MinCount > reach)
                    {
                        errors.Add(at + ": MinCount " + tier.MinCount + " must rise strictly, from 2" +
                                   (reach == int.MaxValue ? string.Empty : " up to the set's size (" + reach + ")") + ".");
                    }

                    previous = Math.Max(previous, tier.MinCount);
                    bool reacts = tier.Reaction != null && tier.Reaction.IsSet;
                    if (!reacts || (tier.Effects != null && tier.Effects.Length > 0))
                    {
                        CheckEffects(tier.Effects, at, "Effects", true, errors);
                    }

                    CheckBondEffects(tier.Effects, at, errors);
                    if (reacts)
                    {
                        CheckReaction(tier.Reaction, at + " Reaction", errors);
                    }
                }
            }
        }

        /// <summary>
        /// A scaling bond: exactly one tier, 1 &lt;= MinCount &lt;= MaxCount &lt;= <paramref name="reach"/>,
        /// the usual bond-effect rules, and each effect's worst case (magnitude x MaxCount) within
        /// the caps. Only a percent stat buff, a flat CritChance or MoveRange buff, or a Shield scales.
        /// </summary>
        private static void ValidateScalingBond(TeamBondData b, TeamBondTierData[] tiers, int reach, string label, List<string> errors)
        {
            if (tiers.Length != 1)
            {
                errors.Add(label + ": a PerCount (scaling) bond has exactly one tier, not " + tiers.Length + ".");
            }

            if (b.MaxCount < 1 || b.MaxCount > reach)
            {
                errors.Add(label + ": MaxCount " + b.MaxCount + " must be at least 1" +
                           (reach == int.MaxValue ? string.Empty : " and at most the set's size (" + reach + ")") + ".");
            }

            for (int t = 0; t < tiers.Length; t++)
            {
                TeamBondTierData tier = tiers[t];
                string at = label + " Tiers[" + t + "]";
                if (tier == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (tier.MinCount < 1 || tier.MinCount > b.MaxCount)
                {
                    errors.Add(at + ": MinCount " + tier.MinCount + " of a PerCount bond must be from 1 up to its MaxCount (" + b.MaxCount + ").");
                }

                CheckEffects(tier.Effects, at, "Effects", true, errors);
                CheckBondEffects(tier.Effects, at, errors);
                CheckScalingCaps(tier.Effects, Math.Max(1, b.MaxCount), at, errors);
                if (tier.Reaction != null && tier.Reaction.IsSet)
                {
                    errors.Add(at + ": a PerCount (scaling) bond has no Reaction; make it a tiered bond.");
                }
            }
        }

        /// <summary>A reaction's damage effect is at most this power (a reaction is a bonus hit, never a skill's worth).</summary>
        public const float MaxReactionDamagePower = 60f;

        /// <summary>A reaction's stun lands at most this percent of the time (reaction chance x effect chance) and lasts exactly one turn.</summary>
        public const int MaxReactionStunChance = 50;

        /// <summary>
        /// A behaviour bond's reaction: its enums parse; Chance 1-100; Cooldown, caps and Range in
        /// band; a hit or crit trigger has a Cooldown of at least 1 (a reaction per hit would scale
        /// with multi-hit skills); <c>Intercept</c> goes with <c>EnemyTargetsAlly</c> and only with
        /// it; the target is one the trigger has; effects are general-valid, land on the side they
        /// are for (hostile ones on enemies, friendly ones on the team), deal at most
        /// <see cref="MaxReactionDamagePower"/> power, and a stun is a one-turn stun landing at most
        /// <see cref="MaxReactionStunChance"/>% of the time; no knockback (a reaction has no facing).
        /// </summary>
        private static void CheckReaction(BondReactionData r, string label, List<string> errors)
        {
            bool triggerOk = CheckEnum<BondTrigger>(r.Trigger, label, "Trigger", errors);
            bool actionOk = CheckEnum<BondAction>(r.Action, label, "Action", errors);
            bool targetOk = CheckEnum<BondReactionTarget>(r.Target, label, "Target", errors);
            CheckEnum<BondTriggerFilter>(r.TriggerFilter, label, "TriggerFilter", errors);
            CheckEnum<BondReactorOrder>(r.ReactorOrder, label, "ReactorOrder", errors);
            CheckBand(r.Chance, 1, 100, label, "Chance", errors);
            CheckBand(r.Cooldown, 0, MaxCooldown, label, "Cooldown", errors);
            CheckBand(r.MaxPerMember, 0, MaxUses, label, "MaxPerMember", errors);
            CheckBand(r.MaxPerTriggerUnit, 0, MaxUses, label, "MaxPerTriggerUnit", errors);
            CheckBand(r.MaxPerBattle, 0, MaxUses, label, "MaxPerBattle", errors);
            CheckBand(r.Range, 0, MaxRange, label, "Range", errors);

            if (!triggerOk || !actionOk || !targetOk)
            {
                return;
            }

            BondTrigger trigger = ParseOr(r.Trigger, BondTrigger.None);
            BondAction action = ParseOr(r.Action, BondAction.Apply);
            BondReactionTarget target = ParseOr(r.Target, BondReactionTarget.TriggerTarget);
            bool hitTrigger = trigger == BondTrigger.MemberHit || trigger == BondTrigger.MemberCrit || trigger == BondTrigger.AllyCrit ||
                              trigger == BondTrigger.AllyHitByEnemy;

            if (trigger == BondTrigger.None)
            {
                errors.Add(label + ": Trigger None is no reaction; leave the Reaction out instead.");
                return;
            }

            if (hitTrigger && r.Cooldown < 1)
            {
                errors.Add(label + ": a " + trigger + " reaction needs a Cooldown of at least 1 (one per the reactor's turn).");
            }

            if ((action == BondAction.Intercept) != (trigger == BondTrigger.EnemyTargetsAlly))
            {
                errors.Add(label + ": Intercept is the EnemyTargetsAlly reaction and EnemyTargetsAlly only intercepts.");
            }

            if (trigger == BondTrigger.AllyBelowHpPercent)
            {
                CheckBand(r.HpThresholdPercent, 1, 99, label, "HpThresholdPercent", errors);
            }

            bool hasTriggerTarget = trigger == BondTrigger.MemberHit || trigger == BondTrigger.MemberCrit || trigger == BondTrigger.AllyCrit;
            bool hasAttacker = trigger == BondTrigger.AllyHitByEnemy || trigger == BondTrigger.EnemyTargetsAlly;
            if (action == BondAction.Apply && ((target == BondReactionTarget.TriggerTarget && !hasTriggerTarget) || (target == BondReactionTarget.Attacker && !hasAttacker)))
            {
                errors.Add(label + ": a " + trigger + " reaction has no " + target + " to land on.");
            }

            if (action == BondAction.Apply && target == BondReactionTarget.EnemiesNearReactor && r.Range < 1)
            {
                errors.Add(label + ": EnemiesNearReactor needs a Range (radius) of at least 1.");
            }

            EffectData[] effects = r.Effects ?? new EffectData[0];
            if (action == BondAction.Apply && effects.Length == 0)
            {
                errors.Add(label + ": has no effects.");
            }

            if (effects.Length > 0)
            {
                CheckEffects(effects, label, "Effects", false, errors);
            }

            // An intercept's effects land on the guardian itself.
            bool onEnemies = action == BondAction.Apply &&
                             (target == BondReactionTarget.TriggerTarget || target == BondReactionTarget.Attacker || target == BondReactionTarget.EnemiesNearReactor);

            for (int i = 0; i < effects.Length; i++)
            {
                EffectData e = effects[i];
                if (e == null)
                {
                    continue;
                }

                string at = label + " Effects[" + i + "]";
                SkillEffectType type = ParseOr(e.EffectType, SkillEffectType.Damage);
                StatusType status = ParseOr(e.Status, StatusType.None);
                bool hostile = type == SkillEffectType.Damage || type == SkillEffectType.DebuffStat ||
                               (type == SkillEffectType.ApplyStatus && status != StatusType.Shield);

                if (hostile != onEnemies)
                {
                    errors.Add(at + ": a " + (type == SkillEffectType.ApplyStatus ? status.ToString() : type.ToString()) + " effect is for " +
                               (hostile ? "enemies" : "the team") + ", but this reaction lands on " + (onEnemies ? "enemies" : "the team") + ".");
                }

                if (type == SkillEffectType.Damage && e.Magnitude > MaxReactionDamagePower)
                {
                    errors.Add(at + ": a reaction's damage power is at most " + MaxReactionDamagePower + ", not " + e.Magnitude + ".");
                }

                if (type == SkillEffectType.ApplyStatus && status == StatusType.Knockback)
                {
                    errors.Add(at + ": a reaction cannot knock back (it has no direction of its own).");
                }

                if (type == SkillEffectType.ApplyStatus && status == StatusType.Stun)
                {
                    int chance = Math.Max(1, Math.Min(100, r.Chance)) * Math.Max(1, Math.Min(100, e.Chance)) / 100;
                    if (chance > MaxReactionStunChance || e.DurationTurns != 1)
                    {
                        errors.Add(at + ": a reaction's stun lasts exactly 1 turn and lands at most " + MaxReactionStunChance + "% of the time (Chance x effect Chance); this is " +
                                   chance + "% for " + e.DurationTurns + " turns.");
                    }
                }
            }
        }

        private static void CheckScalingCaps(EffectData[] effects, int maxCount, string label, List<string> errors)
        {
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                EffectData e = effects[i];
                if (e == null)
                {
                    continue;
                }

                string at = label + " Effects[" + i + "]";
                SkillEffectType type = ParseOr(e.EffectType, SkillEffectType.Damage);
                StatType stat = ParseOr(e.AffectedStat, StatType.Attack);
                float worst = e.Magnitude * maxCount;
                float cap;
                string what;
                if (type == SkillEffectType.ApplyStatus)
                {
                    cap = MaxScalingShield;
                    what = "a shield of " + MaxScalingShield + "% of Defense";
                }
                else if (type != SkillEffectType.BuffStat)
                {
                    continue;
                }
                else if (stat == StatType.CritChance && !e.IsPercent)
                {
                    cap = MaxScalingCritChance;
                    what = MaxScalingCritChance + " CritChance";
                }
                else if (stat == StatType.MoveRange && !e.IsPercent)
                {
                    cap = MaxScalingMoveRange;
                    what = MaxScalingMoveRange + " MoveRange";
                }
                else if (e.IsPercent && stat != StatType.CritChance && stat != StatType.MoveRange)
                {
                    cap = MaxScalingPercentStat;
                    what = MaxScalingPercentStat + "% of the stat";
                }
                else
                {
                    errors.Add(at + ": a PerCount bond scales a percent stat buff, a flat CritChance or MoveRange buff, or a Shield; not a " +
                               (e.IsPercent ? "percent " : "flat ") + stat + " buff.");
                    continue;
                }

                if (worst > cap)
                {
                    errors.Add(at + ": at MaxCount " + maxCount + " this scales to " + worst + ", over the cap of " + what + ".");
                }
            }
        }

        /// <summary>Checks a condition's set (at least two distinct valid entries); returns its distinct size.</summary>
        private static int CheckSet(string[] values, string label, string field, List<string> errors, Func<string, string> problem)
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (string value in values)
            {
                string issue = problem(value);
                if (issue != null)
                {
                    errors.Add(label + ": " + field + " " + issue);
                }
                else if (!seen.Add(value))
                {
                    errors.Add(label + ": " + field + " lists '" + value + "' twice.");
                }
            }

            if (values.Length < 2)
            {
                errors.Add(label + ": " + field + " needs at least two entries (a bond is between beasts).");
            }

            return seen.Count;
        }

        private static void CheckBondEffects(EffectData[] effects, string label, List<string> errors)
        {
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                EffectData e = effects[i];
                if (e == null)
                {
                    continue;
                }

                string at = label + " Effects[" + i + "]";
                SkillEffectType type = ParseOr(e.EffectType, SkillEffectType.Damage);
                StatusType status = ParseOr(e.Status, StatusType.None);
                if (type != SkillEffectType.BuffStat && !(type == SkillEffectType.ApplyStatus && status == StatusType.Shield))
                {
                    errors.Add(at + ": a bond effect lands on its own team at battle start; only BuffStat or a Shield status, not " +
                               (type == SkillEffectType.ApplyStatus ? status.ToString() : type.ToString()) + ".");
                }

                if (e.Chance != SkillEffect.AlwaysChance)
                {
                    errors.Add(at + ": a bond effect always lands; Chance must be 100.");
                }

                if (type == SkillEffectType.BuffStat && ParseOr(e.AffectedStat, StatType.Attack) == StatType.HP)
                {
                    errors.Add(at + ": an HP buff raises only the maximum (it heals nothing); buff another stat or add a Shield.");
                }
            }
        }

        private static void ValidateIdList<T>(string[] ids, string field, int maxCount, Dictionary<string, T> known, List<string> errors)
        {
            if (ids == null)
            {
                return;
            }

            if (ids.Length > maxCount)
            {
                errors.Add(field + " lists " + ids.Length + " ids; at most " + maxCount + " fit.");
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (string id in ids)
            {
                if (id == null || !known.ContainsKey(id))
                {
                    errors.Add(field + ": '" + id + "' does not resolve.");
                }
                else if (!seen.Add(id))
                {
                    errors.Add(field + ": lists '" + id + "' twice.");
                }
            }
        }

        private static bool CheckId(string id, string label, string field, HashSet<string> allIds, List<string> errors)
        {
            if (!BeastRosterValidator.IsSnakeCaseId(id))
            {
                errors.Add(label + ": " + field + " must be lowercase snake_case.");
                return false;
            }

            if (!allIds.Add(id))
            {
                errors.Add(label + ": duplicate id (ids are unique across the whole library).");
                return false;
            }

            return true;
        }

        private static void CheckText(string displayName, string description, string label, List<string> errors)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                errors.Add(label + ": DisplayName is empty.");
            }

            if (string.IsNullOrEmpty(description))
            {
                errors.Add(label + ": Description is empty.");
            }
        }

        /// <summary>
        /// An icon art key, when there is one, is well formed (lowercase snake_case segments joined
        /// by '/'); whether it names a sprite in the art manifest is <c>ArtReferenceValidator</c>'s
        /// check (this validator does not read the manifest).
        /// </summary>
        private static void CheckArtKey(string artKey, string label, List<string> errors)
        {
            if (!string.IsNullOrEmpty(artKey) && !Vfx.ArtReferenceValidator.IsWellFormed(artKey))
            {
                errors.Add(label + ": ArtKey '" + artKey + "' is not lowercase snake_case segments joined by '/'.");
            }
        }

        internal static bool CheckEnum<T>(string name, string label, string field, List<string> errors) where T : struct
        {
            if (TryParse(name, default(T), out T _))
            {
                return true;
            }

            errors.Add(label + ": " + field + " '" + name + "' is not a " + typeof(T).Name + " name.");
            return false;
        }

        internal static void CheckBand(int value, int min, int max, string label, string field, List<string> errors)
        {
            if (value < min || value > max)
            {
                errors.Add(label + ": " + field + " is " + value + "; it must be " + min + " to " + max + ".");
            }
        }
    }
}
