using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Localization;
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

        /// <summary>Who the skill picks, in plain words, from its data (<see cref="TargetingRuleText"/>).</summary>
        public string TargetingRule { get; private set; }

        /// <summary>How a taunt changes that pick (<see cref="TauntRuleText"/>).</summary>
        public string TauntRule { get; private set; }
        /// <summary>
        /// The card for <paramref name="skill"/>, its text parsed with <paramref name="glossary"/> (null: plain text) and its
        /// own words from <paramref name="text"/> (<c>ui.skill_card.*</c>; <c>GameContent.Text</c>).
        /// </summary>
        public static SkillCard Of(SkillSO skill, Glossary glossary, StringTable text)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
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
                power.Add(PowerLine(effect, skill.Element, skill.Category, text));
            }

            return new SkillCard
            {
                Name = string.IsNullOrEmpty(skill.DisplayName) ? skill.SkillId : skill.DisplayName,
                ArtKey = skill.ArtKey,
                Element = skill.Element == Creatures.Element.None ? null : skill.Element.ToString(),
                Category = damage ? text.Get(skill.Category == DamageCategory.Special ? "ui.skill_card.special" : "ui.skill_card.physical") : null,
                Targets = TargetTags(skill.TargetShape, skill.TargetSide, text),
                Cooldown = skill.Cooldown <= 1 ? text.Get("ui.skill_card.every_turn") : text.Format("ui.skill_card.cooldown", Int(skill.Cooldown)),
                Range = RangeText(skill.TargetShape, skill.Range, text),
                Uses = UsesText(skill.MaxUsesPerBattle, skill.InitialCooldown, skill.Cooldown, text),
                Power = power,
                Scaling = ScalingText(skill.Progression, text),
                Description = glossary.Parse(skill.Description),
                TargetingRule = TargetingRuleText(skill, text),
                TauntRule = TauntRuleText(skill, text)
            };
        }

        /// <summary>
        /// The skill's targeting rule in plain words, generated from its data the way
        /// <see cref="SkillTargetResolver"/> reads it: the shape, the side, the pick rule
        /// (<see cref="SkillSO.TargetingCriterion"/>, <see cref="SkillSO.TargetingOrder"/> and, for a
        /// stat pick, <see cref="SkillSO.TargetingStat"/>) and the range, e.g. "Targets the enemy
        /// with the lowest HP% within 3 hexes." Range is measured from the caster's nearest tile to
        /// the target's; area and whole-field shapes hit everyone eligible and pick no one.
        /// </summary>
        public static string TargetingRuleText(SkillSO skill, StringTable text)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            bool ally = skill.TargetSide == SkillTargetSide.Ally;
            string side = text.Get(ally ? "ui.skill_card.side_ally" : "ui.skill_card.side_enemy");
            string sides = text.Get(ally ? "ui.skill_card.sides_ally" : "ui.skill_card.sides_enemy");
            switch (skill.TargetShape)
            {
                case SkillTargetShape.Self:
                    return text.Get("ui.skill_card.rule_self");
                case SkillTargetShape.AllEnemies:
                    return text.Get("ui.skill_card.rule_all_enemies");
                case SkillTargetShape.AllAllies:
                    return text.Get("ui.skill_card.rule_all_allies");
                case SkillTargetShape.AreaBurst:
                    return text.Format(ally ? "ui.skill_card.rule_burst_ally" : "ui.skill_card.rule_burst_enemy", side, Hexes(skill.Range, text));
                case SkillTargetShape.Cross:
                    return text.Format(ally ? "ui.skill_card.rule_cross_ally" : "ui.skill_card.rule_cross_enemy", side, Hexes(skill.Range, text));
                case SkillTargetShape.Line:
                    return text.Format("ui.skill_card.rule_line", Pick(skill, side, sides, text), Hexes(skill.Range, text), side);
                default:
                    return text.Format("ui.skill_card.rule_target", Pick(skill, side, sides, text), Hexes(skill.Range, text));
            }
        }

        /// <summary>
        /// How a taunt changes the skill's pick, from its data: an enemy-side picking skill
        /// (single target or line) must pick the taunter while it is alive and within range (and the
        /// caster walks toward it when it is not); area, whole-field, self and ally skills are not
        /// redirected.
        /// </summary>
        public static string TauntRuleText(SkillSO skill, StringTable text)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            bool picks = skill.TargetShape == SkillTargetShape.SingleTarget || skill.TargetShape == SkillTargetShape.Line;
            if (picks && skill.TargetSide == SkillTargetSide.Enemy)
            {
                return text.Get("ui.skill_card.taunt_overrides");
            }

            if (skill.TargetShape == SkillTargetShape.Self || skill.TargetSide == SkillTargetSide.Ally || skill.TargetShape == SkillTargetShape.AllAllies)
            {
                return text.Get("ui.skill_card.taunt_never_aims");
            }

            return text.Get("ui.skill_card.taunt_area");
        }

        /// <summary>The pick among the candidates, e.g. "the enemy with the lowest HP%", "a random ally (itself included)".</summary>
        private static string Pick(SkillSO skill, string side, string sides, StringTable text)
        {
            string who = skill.TargetSide == SkillTargetSide.Ally ? text.Format("ui.skill_card.itself_included", side) : side;
            bool lowest = skill.TargetingOrder == SkillTargetingOrder.Lowest;
            switch (skill.TargetingCriterion)
            {
                case SkillTargetingCriterion.Random:
                    return text.Format("ui.skill_card.pick_random", who);
                case SkillTargetingCriterion.Distance:
                    return text.Format(lowest ? "ui.skill_card.pick_nearest" : "ui.skill_card.pick_farthest", who);
                case SkillTargetingCriterion.CurrentHp:
                    return text.Format(lowest ? "ui.skill_card.pick_least_hp" : "ui.skill_card.pick_most_hp", who);
                case SkillTargetingCriterion.HpFraction:
                    return text.Format(lowest ? "ui.skill_card.pick_lowest_hp_pct" : "ui.skill_card.pick_highest_hp_pct", who);
                case SkillTargetingCriterion.Stat:
                    string stat = skill.TargetingStat == StatType.HP ? text.Get("ui.skill_card.max_hp") : StatName(skill.TargetingStat, text);
                    return text.Format(lowest ? "ui.skill_card.pick_lowest_stat" : "ui.skill_card.pick_highest_stat", who, stat);
                default:
                    return text.Format("ui.skill_card.pick_any", sides);
            }
        }

        private static string Hexes(int range, StringTable text)
        {
            return range == 1 ? text.Get("ui.skill_card.hex_one") : text.Format("ui.skill_card.hexes", Int(range));
        }

        /// <summary>
        /// Who a skill lands on: Self for a self skill; Ally (and Self: the whole team includes
        /// the caster) for all allies; Enemy for all enemies; else its side, plus Self for an
        /// ally burst (a burst is centred on the caster and covers it).
        /// </summary>
        public static List<string> TargetTags(SkillTargetShape shape, SkillTargetSide side, StringTable text)
        {
            string self = text.Get("ui.skill_card.tag_self");
            string ally = text.Get("ui.skill_card.tag_ally");
            string enemy = text.Get("ui.skill_card.tag_enemy");
            switch (shape)
            {
                case SkillTargetShape.Self:
                    return new List<string> { self };
                case SkillTargetShape.AllAllies:
                    return new List<string> { ally, self };
                case SkillTargetShape.AllEnemies:
                    return new List<string> { enemy };
                default:
                    if (side == SkillTargetSide.Ally)
                    {
                        return shape == SkillTargetShape.AreaBurst ? new List<string> { ally, self } : new List<string> { ally };
                    }

                    return new List<string> { enemy };
            }
        }

        /// <summary>The range text for a shape (see <see cref="Range"/>).</summary>
        public static string RangeText(SkillTargetShape shape, int range, StringTable text)
        {
            switch (shape)
            {
                case SkillTargetShape.Self:
                    return text.Get("ui.skill_card.range_self");
                case SkillTargetShape.AllAllies:
                case SkillTargetShape.AllEnemies:
                    return text.Get("ui.skill_card.range_whole_field");
                case SkillTargetShape.AreaBurst:
                    return text.Format("ui.skill_card.range_burst", Int(range));
                case SkillTargetShape.Cross:
                    return text.Format("ui.skill_card.range_cross", Int(range));
                case SkillTargetShape.Line:
                    return text.Format("ui.skill_card.range_line", Int(range));
                default:
                    return range <= 1 ? text.Get("ui.skill_card.range_melee") : text.Format("ui.skill_card.range", Int(range));
            }
        }

        /// <summary>
        /// One effect's power line. Damage and heals are a percent of the attacking stat (Attack
        /// or Special Attack; a heal, Special Attack), a shield of the caster's Defense, a damage
        /// over time a fixed power each turn, a knockback whole hexes, a buff or debuff a percent
        /// or flat change; then its duration, stacks and chance.
        /// </summary>
        public static string PowerLine(SkillEffect effect, Element element, DamageCategory category, StringTable text)
        {
            string magnitude = Number(effect.Magnitude);
            string line;
            switch (effect.EffectType)
            {
                case SkillEffectType.Damage:
                    line = text.Format(category == DamageCategory.Special ? "ui.skill_card.power_damage_special" : "ui.skill_card.power_damage", magnitude);
                    if (effect.HitCount > 1)
                    {
                        line += text.Format("ui.skill_card.power_hits", Int(effect.HitCount));
                    }

                    if (effect.ExecuteBonusPercent > 0)
                    {
                        line += text.Format("ui.skill_card.power_execute", Int(effect.ExecuteBonusPercent));
                    }

                    return line;
                case SkillEffectType.Heal:
                    line = text.Format("ui.skill_card.power_heal", magnitude);
                    break;
                case SkillEffectType.BuffStat:
                    line = text.Format(effect.IsPercent ? "ui.skill_card.power_buff_percent" : "ui.skill_card.power_buff", magnitude, StatName(effect.AffectedStat, text));
                    break;
                case SkillEffectType.DebuffStat:
                    line = text.Format(effect.IsPercent ? "ui.skill_card.power_debuff_percent" : "ui.skill_card.power_debuff", magnitude, StatName(effect.AffectedStat, text));
                    break;
                case SkillEffectType.Cleanse:
                    line = text.Get("ui.skill_card.power_cleanse");
                    break;
                case SkillEffectType.ApplyStatus:
                    switch (effect.Status)
                    {
                        case StatusType.Shield:
                            line = text.Format("ui.skill_card.power_shield", magnitude);
                            break;
                        case StatusType.DamageOverTime:
                            line = text.Format(element == Creatures.Element.Fire ? "ui.skill_card.power_burn" : "ui.skill_card.power_poison", magnitude);
                            break;
                        case StatusType.Knockback:
                            line = text.Format(effect.Magnitude == 1f ? "ui.skill_card.power_knockback_one" : "ui.skill_card.power_knockback", magnitude);
                            break;
                        case StatusType.Stun:
                            line = text.Get("ui.skill_card.power_stun");
                            break;
                        case StatusType.Taunt:
                            line = text.Get("ui.skill_card.power_taunt");
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
                line += text.Format(effect.DurationTurns == 1 ? "ui.skill_card.power_turn" : "ui.skill_card.power_turns", Int(effect.DurationTurns));
            }

            if (effect.MaxStacks > 1)
            {
                line += text.Format("ui.skill_card.power_stacks", Int(effect.MaxStacks));
            }

            if (effect.Chance > 0 && effect.Chance < SkillEffect.AlwaysChance)
            {
                line += text.Format("ui.skill_card.power_chance", Int(effect.Chance));
            }

            return line;
        }

        private static string UsesText(int maxUses, int initialCooldown, int cooldown, StringTable text)
        {
            string uses = maxUses == 1 ? text.Get("ui.skill_card.once_per_battle") : maxUses > 1 ? text.Format("ui.skill_card.times_per_battle", Int(maxUses)) : null;
            if (initialCooldown == 0 && cooldown > 1)
            {
                uses = uses == null ? text.Get("ui.skill_card.ready_at_once") : text.Format("ui.skill_card.uses_ready_at_once", uses);
            }

            return uses;
        }

        private static string ScalingText(Progression.SkillProgressionDefinition progression, StringTable text)
        {
            if (progression == null)
            {
                return null;
            }

            float growth = Math.Max(0f, progression.MagnitudeGrowthPerLevel);
            return text.Format("ui.skill_card.scaling", Number(growth), Int(progression.MaxLevel));
        }

        private static string StatName(StatType stat, StringTable text)
        {
            switch (stat)
            {
                case StatType.SpecialAttack:
                    return text.Get("ui.skill_card.stat_special_attack");
                case StatType.SpecialDefense:
                    return text.Get("ui.skill_card.stat_special_defense");
                case StatType.MoveRange:
                    return text.Get("ui.skill_card.stat_movement");
                case StatType.CritChance:
                    return text.Get("ui.skill_card.stat_crit_chance");
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
