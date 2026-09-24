using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// The validated VFX library as lookups. A skill's effect resolves in order: its own entry
    /// (<see cref="VfxLibraryData.Skills"/>), else the default of its primary effect type
    /// (<see cref="VfxLibraryData.EffectDefaults"/>, <see cref="PrimaryKey"/>: a heal, a taunt...),
    /// else its element's default (damage by element), else <c>None</c>'s. Built from data that
    /// passed <see cref="VfxLibraryValidator"/>; with an element default missing it falls back to
    /// <c>None</c>'s and then to null. Never throws.
    /// </summary>
    public sealed class VfxLibrary
    {
        private readonly Dictionary<string, VfxEffectData> _skills = new Dictionary<string, VfxEffectData>(StringComparer.Ordinal);
        private readonly Dictionary<Element, VfxEffectData> _defaults = new Dictionary<Element, VfxEffectData>();
        private readonly Dictionary<string, VfxEffectTypeDefaultData> _byType = new Dictionary<string, VfxEffectTypeDefaultData>(StringComparer.Ordinal);

        private VfxLibrary()
        {
        }

        /// <summary>The library over <paramref name="data"/> (null gives an empty library).</summary>
        public static VfxLibrary Build(VfxLibraryData data)
        {
            VfxLibrary library = new VfxLibrary();
            if (data == null)
            {
                return library;
            }

            foreach (VfxElementDefaultData entry in data.ElementDefaults ?? new VfxElementDefaultData[0])
            {
                if (entry != null && entry.Effect != null && BeastRosterValidator.TryParseElement(entry.Element, out Element element) &&
                    !library._defaults.ContainsKey(element))
                {
                    library._defaults.Add(element, entry.Effect);
                }
            }

            foreach (VfxEffectTypeDefaultData entry in data.EffectDefaults ?? new VfxEffectTypeDefaultData[0])
            {
                if (entry != null && VfxEffectKey.IsKey(entry.Key) && !library._byType.ContainsKey(entry.Key))
                {
                    library._byType.Add(entry.Key, entry);
                }
            }

            foreach (VfxSkillEffectData entry in data.Skills ?? new VfxSkillEffectData[0])
            {
                if (entry != null && entry.Effect != null && !string.IsNullOrEmpty(entry.SkillId) && !library._skills.ContainsKey(entry.SkillId))
                {
                    library._skills.Add(entry.SkillId, entry.Effect);
                }
            }

            return library;
        }

        /// <summary>Whether <paramref name="skillId"/> has an effect of its own (rather than its element's default).</summary>
        public bool HasOwnEffect(string skillId)
        {
            return !string.IsNullOrEmpty(skillId) && _skills.ContainsKey(skillId);
        }

        /// <summary>The effect a skill plays: its own, else its element's default, else <c>None</c>'s, else null.</summary>
        public VfxEffectData Resolve(string skillId, Element element)
        {
            return Resolve(skillId, element, null);
        }

        /// <summary>
        /// The effect a skill plays: its own, else the default of <paramref name="effectKey"/> (its
        /// primary effect type; null for a damage skill) when that has an on-apply effect, else its
        /// element's default, else <c>None</c>'s, else null.
        /// </summary>
        public VfxEffectData Resolve(string skillId, Element element, string effectKey)
        {
            if (!string.IsNullOrEmpty(skillId) && _skills.TryGetValue(skillId, out VfxEffectData own))
            {
                return own;
            }

            VfxEffectData byType = OnApply(effectKey);
            if (byType != null)
            {
                return byType;
            }

            if (_defaults.TryGetValue(element, out VfxEffectData byElement))
            {
                return byElement;
            }

            return _defaults.TryGetValue(Element.None, out VfxEffectData fallback) ? fallback : null;
        }

        /// <summary>The on-apply effect of effect type <paramref name="effectKey"/>, or null.</summary>
        public VfxEffectData OnApply(string effectKey)
        {
            return effectKey != null && _byType.TryGetValue(effectKey, out VfxEffectTypeDefaultData entry) ? entry.Effect : null;
        }

        /// <summary>The aura of lasting effect <paramref name="effectKey"/> (a status or a stat change), or null.</summary>
        public VfxAuraData Aura(string effectKey)
        {
            return effectKey != null && _byType.TryGetValue(effectKey, out VfxEffectTypeDefaultData entry) ? entry.Aura : null;
        }

        /// <summary>
        /// The key of a skill's primary effect type for VFX: null for a skill that deals damage
        /// (it looks like its element), else its first effect's (<see cref="KeyOf(SkillEffect, Element)"/>).
        /// </summary>
        public static string PrimaryKey(SkillSO skill)
        {
            if (skill == null || skill.Effects == null || skill.Effects.Count == 0)
            {
                return null;
            }

            foreach (SkillEffect effect in skill.Effects)
            {
                if (effect != null && effect.EffectType == SkillEffectType.Damage)
                {
                    return null;
                }
            }

            return KeyOf(skill.Effects[0], skill.Element);
        }

        /// <summary>
        /// An effect's <see cref="VfxEffectKey"/>: heal, buff/debuff, cleanse, or the status it
        /// applies (damage over time is a burn from a fire skill and a poison otherwise); null for
        /// damage or no status.
        /// </summary>
        public static string KeyOf(SkillEffect effect, Element element)
        {
            if (effect == null)
            {
                return null;
            }

            switch (effect.EffectType)
            {
                case SkillEffectType.Heal:
                    return VfxEffectKey.Heal;
                case SkillEffectType.BuffStat:
                    return VfxEffectKey.BuffStat;
                case SkillEffectType.DebuffStat:
                    return VfxEffectKey.DebuffStat;
                case SkillEffectType.Cleanse:
                    return VfxEffectKey.Cleanse;
                case SkillEffectType.ApplyStatus:
                    return KeyOf(effect.Status, element);
                default:
                    return null;
            }
        }

        /// <summary>A status's <see cref="VfxEffectKey"/> (<paramref name="element"/>: its source's, telling a burn from a poison); null for none.</summary>
        public static string KeyOf(StatusType status, Element element)
        {
            switch (status)
            {
                case StatusType.Taunt:
                    return VfxEffectKey.Taunt;
                case StatusType.Stun:
                    return VfxEffectKey.Stun;
                case StatusType.Shield:
                    return VfxEffectKey.Shield;
                case StatusType.DamageOverTime:
                    return element == Element.Fire ? VfxEffectKey.Burn : VfxEffectKey.Poison;
                case StatusType.Knockback:
                    return VfxEffectKey.Knockback;
                default:
                    return null;
            }
        }
    }
}
