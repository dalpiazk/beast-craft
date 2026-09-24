using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Vfx
{
    /// <summary>
    /// The validated VFX library as lookups: a skill's own effect when it has one, else its
    /// element's default (else <c>None</c>'s). Built from data that passed
    /// <see cref="VfxLibraryValidator"/>; with an element default missing it falls back to
    /// <c>None</c>'s and then to null. Never throws.
    /// </summary>
    public sealed class VfxLibrary
    {
        private readonly Dictionary<string, VfxEffectData> _skills = new Dictionary<string, VfxEffectData>(StringComparer.Ordinal);
        private readonly Dictionary<Element, VfxEffectData> _defaults = new Dictionary<Element, VfxEffectData>();

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
            if (!string.IsNullOrEmpty(skillId) && _skills.TryGetValue(skillId, out VfxEffectData own))
            {
                return own;
            }

            if (_defaults.TryGetValue(element, out VfxEffectData byElement))
            {
                return byElement;
            }

            return _defaults.TryGetValue(Element.None, out VfxEffectData fallback) ? fallback : null;
        }
    }
}
