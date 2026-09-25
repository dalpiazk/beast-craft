using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Text;

namespace BeastCraft.Presentation.Cards
{
    /// <summary>
    /// Everything the skill detail card shows for one skill, worked out from its authored data:
    /// the icon, name, element and damage-category tags, who it targets (Ally, Enemy, Self), its
    /// cooldown, range and uses, one line of power and scaling per effect, the progression line,
    /// and its description as rich text with the glossary terms marked
    /// (<see cref="Glossary.Parse"/>). Pure: no engine types, no state; the viewer draws it and
    /// the tests read it.
    /// </summary>
    public sealed class SkillCard
    {
        private SkillCard()
        {
        }

        public string Name { get; private set; }

        /// <summary>The icon's art key (<see cref="SkillSO.ArtKey"/>; null for none).</summary>
        public string ArtKey { get; private set; }

        /// <summary>The element, or null for an elementless skill.</summary>
        public string Element { get; private set; }

        /// <summary>"Physical" or "Special" for a skill that deals damage; null otherwise.</summary>
        public string Category { get; private set; }

        /// <summary>Who it lands on, in the order Ally, Enemy, Self (at least one).</summary>
        public IReadOnlyList<string> Targets { get; private set; }

        /// <summary>E.g. "Cooldown 2" or "Every turn".</summary>
        public string Cooldown { get; private set; }

        /// <summary>E.g. "Range 3", "Burst 2 around self", "Whole field", "Self".</summary>
        public string Range { get; private set; }

        /// <summary>E.g. "Once per battle", "Opens at once"; null when neither applies.</summary>
        public string Uses { get; private set; }

        /// <summary>One line per effect, in authored order: its power and what it scales with.</summary>
        public IReadOnlyList<string> Power { get; private set; }

        /// <summary>How levels grow it, e.g. "+3% power per level, to Lv 20".</summary>
        public string Scaling { get; private set; }

        /// <summary>The description as rich text.</summary>
        public IReadOnlyList<RichSpan> Description { get; private set; }

        /// <summary>The card for <paramref name="skill"/>, its text parsed with <paramref name="glossary"/> (null: plain text).</summary>
        public static SkillCard Of(SkillSO skill, Glossary glossary)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            glossary = glossary ?? Glossary.Empty;
            List<string> power = new List<string>();
            bool damage = false;
            foreach (SkillEffect effect in skill.Effects ?? new List<SkillEffect>())
            {
                if (effect == null)
                {
                    continue;
                }

                damage |= effect.EffectType == SkillEffectType.Damage;
                power.Add(PowerLine(effect, skill.Element, skill.Category));
            }

            return new SkillCard
            {
                Name = string.IsNullOrEmpty(skill.DisplayName) ? skill.SkillId : skill.DisplayName,
                ArtKey = skill.ArtKey,
                Element = skill.Element == Creatures.Element.None ? null : skill.Element.ToString(),
                Category = damage ? (skill.Category == DamageCategory.Special ? "Special" : "Physical") : null,
                Targets = TargetTags(skill.TargetShape, skill.TargetSide),
                Cooldown = skill.Cooldown <= 1 ? "Every turn" : "Cooldown " + Int(skill.Cooldown),
                Range = RangeText(skill.TargetShape, skill.Range),
                Uses = UsesText(skill.MaxUsesPerBattle, skill.InitialCooldown, skill.Cooldown),
                Power = power,
                Scaling = ScalingText(skill.Progression),
                Description = glossary.Parse(skill.Description)
            };
        }

        /// <summary>
        /// Who a skill lands on: Self for a self skill; Ally (and Self: the whole team includes
        /// the caster) for all allies; Enemy for all enemies; else its side, plus Self for an
        /// ally burst (a burst is centred on the caster and covers it).
        /// </summary>
        public static List<string> TargetTags(SkillTargetShape shape, SkillTargetSide side)
        {
            switch (shape)
            {
                case SkillTargetShape.Self:
                    return new List<string> { "Self" };
                case SkillTargetShape.AllAllies:
                    return new List<string> { "Ally", "Self" };
                case SkillTargetShape.AllEnemies:
                    return new List<string> { "Enemy" };
                default:
                    if (side == SkillTargetSide.Ally)
                    {
                        return shape == SkillTargetShape.AreaBurst ? new List<string> { "Ally", "Self" } : new List<string> { "Ally" };
                    }

                    return new List<string> { "Enemy" };
            }
        }

        /// <summary>The range text for a shape (see <see cref="Range"/>).</summary>
        public static string RangeText(SkillTargetShape shape, int range)
        {
            switch (shape)
            {
                case SkillTargetShape.Self:
                    return "Self";
                case SkillTargetShape.AllAllies:
                case SkillTargetShape.AllEnemies:
                    return "Whole field";
                case SkillTargetShape.AreaBurst:
                    return "Burst " + Int(range) + " around self";
                case SkillTargetShape.Cross:
                    return "Cross " + Int(range);
                case SkillTargetShape.Line:
                    return "Line " + Int(range);
                default:
                    return range <= 1 ? "Melee" : "Range " + Int(range);
            }
        }

        /// <summary>
        /// One effect's power line. Damage and heals are a percent of the attacking stat (Attack
        /// or Special Attack; a heal, Special Attack), a shield of the caster's Defense, a damage
        /// over time a fixed power each turn, a knockback whole hexes, a buff or debuff a percent
        /// or flat change; then its duration, stacks and chance.
        /// </summary>
        public static string PowerLine(SkillEffect effect, Element element, DamageCategory category)
        {
            string magnitude = Number(effect.Magnitude);
            string line;
            switch (effect.EffectType)
            {
                case SkillEffectType.Damage:
                    line = "Damage " + magnitude + "% of " + (category == DamageCategory.Special ? "Special Attack" : "Attack");
                    if (effect.HitCount > 1)
                    {
                        line += ", " + Int(effect.HitCount) + " hits";
                    }

                    if (effect.ExecuteBonusPercent > 0)
                    {
                        line += ", up to +" + Int(effect.ExecuteBonusPercent) + "% on a worn-down target";
                    }

                    return line;
                case SkillEffectType.Heal:
                    line = "Heal " + magnitude + "% of Special Attack";
                    break;
                case SkillEffectType.BuffStat:
                    line = "+" + magnitude + (effect.IsPercent ? "% " : " ") + StatName(effect.AffectedStat);
                    break;
                case SkillEffectType.DebuffStat:
                    line = "-" + magnitude + (effect.IsPercent ? "% " : " ") + StatName(effect.AffectedStat);
                    break;
                case SkillEffectType.Cleanse:
                    line = "Cleanse";
                    break;
                case SkillEffectType.ApplyStatus:
                    switch (effect.Status)
                    {
                        case StatusType.Shield:
                            line = "Shield " + magnitude + "% of Defense";
                            break;
                        case StatusType.DamageOverTime:
                            line = (element == Creatures.Element.Fire ? "Burn " : "Poison ") + magnitude + " power a turn";
                            break;
                        case StatusType.Knockback:
                            line = "Knockback " + magnitude + (effect.Magnitude == 1f ? " hex" : " hexes");
                            break;
                        case StatusType.Stun:
                            line = "Stun";
                            break;
                        case StatusType.Taunt:
                            line = "Taunt";
                            break;
                        default:
                            line = effect.Status.ToString();
                            break;
                    }

                    break;
                default:
                    line = effect.EffectType.ToString();
                    break;
            }

            if (effect.DurationTurns > 0 && effect.EffectType != SkillEffectType.Heal && effect.EffectType != SkillEffectType.Cleanse &&
                !(effect.EffectType == SkillEffectType.ApplyStatus && effect.Status == StatusType.Knockback))
            {
                line += ", " + Int(effect.DurationTurns) + (effect.DurationTurns == 1 ? " turn" : " turns");
            }

            if (effect.MaxStacks > 1)
            {
                line += ", stacks x" + Int(effect.MaxStacks);
            }

            if (effect.Chance > 0 && effect.Chance < SkillEffect.AlwaysChance)
            {
                line += " (" + Int(effect.Chance) + "% chance)";
            }

            return line;
        }

        private static string UsesText(int maxUses, int initialCooldown, int cooldown)
        {
            string uses = maxUses == 1 ? "Once per battle" : maxUses > 1 ? Int(maxUses) + " times per battle" : null;
            if (initialCooldown == 0 && cooldown > 1)
            {
                uses = uses == null ? "Ready at once" : uses + ", ready at once";
            }

            return uses;
        }

        private static string ScalingText(Progression.SkillProgressionDefinition progression)
        {
            if (progression == null)
            {
                return null;
            }

            float growth = Math.Max(0f, progression.MagnitudeGrowthPerLevel);
            return "+" + Number(growth) + "% power per level, to Lv " + Int(progression.MaxLevel);
        }

        private static string StatName(StatType stat)
        {
            switch (stat)
            {
                case StatType.SpecialAttack:
                    return "Special Attack";
                case StatType.SpecialDefense:
                    return "Special Defense";
                case StatType.MoveRange:
                    return "movement";
                case StatType.CritChance:
                    return "crit chance";
                default:
                    return stat.ToString();
            }
        }

        private static string Number(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
