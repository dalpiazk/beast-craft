using System;
using System.Collections.Generic;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;

namespace BeastCraft.Save
{
    /// <summary>
    /// An <see cref="ISaveContentCatalog"/> over plain id sets (ordinal, case-sensitive, like every
    /// id comparison in the codebase). Build it from lists, or from the authored roster and skill
    /// library with <see cref="FromData"/>. Null and empty ids are never known.
    /// </summary>
    public class SaveContentCatalog : ISaveContentCatalog
    {
        private readonly HashSet<string> _species;
        private readonly HashSet<string> _skills;
        private readonly HashSet<string> _passives;
        private readonly HashSet<string> _materials;

        /// <summary>A catalog of exactly these ids. A null sequence is empty.</summary>
        public SaveContentCatalog(IEnumerable<string> speciesIds, IEnumerable<string> skillIds, IEnumerable<string> passiveIds, IEnumerable<string> materialIds)
        {
            _species = ToSet(speciesIds);
            _skills = ToSet(skillIds);
            _passives = ToSet(passiveIds);
            _materials = ToSet(materialIds);
        }

        /// <summary>
        /// Every species in <paramref name="roster"/>, and every beast skill, avatar active, passive
        /// and material in <paramref name="library"/>. Either may be null (its ids are then empty).
        /// </summary>
        public static SaveContentCatalog FromData(BeastRosterData roster, SkillLibraryData library)
        {
            List<string> species = new List<string>();
            List<string> skills = new List<string>();
            List<string> passives = new List<string>();
            List<string> materials = new List<string>();

            if (roster != null && roster.Species != null)
            {
                foreach (SpeciesData entry in roster.Species)
                {
                    species.Add(entry == null ? null : entry.SpeciesId);
                }
            }

            if (library != null)
            {
                AddSkillIds(skills, library.BeastSkills);
                AddSkillIds(skills, library.AvatarActives);

                if (library.AvatarPassives != null)
                {
                    foreach (PassiveData entry in library.AvatarPassives)
                    {
                        passives.Add(entry == null ? null : entry.PassiveId);
                    }
                }

                if (library.Materials != null)
                {
                    foreach (SkillMaterialData entry in library.Materials)
                    {
                        materials.Add(entry == null ? null : entry.MaterialId);
                    }
                }
            }

            return new SaveContentCatalog(species, skills, passives, materials);
        }

        public bool IsKnownSpecies(string speciesId)
        {
            return Contains(_species, speciesId);
        }

        public bool IsKnownSkill(string skillId)
        {
            return Contains(_skills, skillId);
        }

        public bool IsKnownPassive(string passiveId)
        {
            return Contains(_passives, passiveId);
        }

        public bool IsKnownMaterial(string materialId)
        {
            return Contains(_materials, materialId);
        }

        private static void AddSkillIds(List<string> into, SkillData[] skills)
        {
            if (skills == null)
            {
                return;
            }

            foreach (SkillData entry in skills)
            {
                into.Add(entry == null ? null : entry.SkillId);
            }
        }

        private static HashSet<string> ToSet(IEnumerable<string> ids)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);

            if (ids != null)
            {
                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        set.Add(id);
                    }
                }
            }

            return set;
        }

        private static bool Contains(HashSet<string> set, string id)
        {
            return !string.IsNullOrEmpty(id) && set.Contains(id);
        }
    }
}
