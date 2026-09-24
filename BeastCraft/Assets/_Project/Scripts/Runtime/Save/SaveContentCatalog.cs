using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;

namespace BeastCraft.Save
{
    /// <summary>
    /// An <see cref="ISaveContentCatalog"/> over plain id sets (ordinal, case-sensitive, like every
    /// id comparison in the codebase). Build it from lists, or from the authored roster and skill
    /// library with <see cref="FromData(BeastRosterData, SkillLibraryData)"/>. Null and empty ids are
    /// never known.
    /// <para>
    /// Region and seal ids are checked only when the catalog was given them (the six-list
    /// constructor): a catalog built without campaign data answers every non-empty region or seal
    /// id as known, so a save validated against it gets no campaign-id issues.
    /// </para>
    /// </summary>
    public class SaveContentCatalog : ISaveContentCatalog
    {
        private readonly HashSet<string> _species;
        private readonly HashSet<string> _skills;
        private readonly HashSet<string> _passives;
        private readonly HashSet<string> _materials;
        private readonly HashSet<string> _regions;
        private readonly HashSet<string> _seals;

        /// <summary>A catalog of exactly these ids, which does not check region or seal ids. A null sequence is empty.</summary>
        public SaveContentCatalog(IEnumerable<string> speciesIds, IEnumerable<string> skillIds, IEnumerable<string> passiveIds, IEnumerable<string> materialIds)
            : this(speciesIds, skillIds, passiveIds, materialIds, null, null)
        {
        }

        /// <summary>
        /// A catalog of exactly these ids. A null species, skill, passive or material sequence is
        /// empty; a null region or seal sequence means those ids are not checked (every non-empty id
        /// is known).
        /// </summary>
        public SaveContentCatalog(IEnumerable<string> speciesIds, IEnumerable<string> skillIds, IEnumerable<string> passiveIds, IEnumerable<string> materialIds,
                                  IEnumerable<string> regionIds, IEnumerable<string> sealIds)
        {
            _species = ToSet(speciesIds);
            _skills = ToSet(skillIds);
            _passives = ToSet(passiveIds);
            _materials = ToSet(materialIds);
            _regions = regionIds == null ? null : ToSet(regionIds);
            _seals = sealIds == null ? null : ToSet(sealIds);
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

        /// <summary>
        /// <see cref="FromData(BeastRosterData, SkillLibraryData)"/> plus every region and seal id in
        /// <paramref name="regions"/> (<c>regions.json</c>), which are then checked. A null
        /// <paramref name="regions"/> leaves them unchecked.
        /// </summary>
        public static SaveContentCatalog FromData(BeastRosterData roster, SkillLibraryData library, RegionLibraryData regions)
        {
            SaveContentCatalog content = FromData(roster, library);
            if (regions == null)
            {
                return content;
            }

            List<string> regionIds = new List<string>();
            List<string> sealIds = new List<string>();
            foreach (RegionData region in regions.Regions ?? new RegionData[0])
            {
                regionIds.Add(region == null ? null : region.RegionId);
            }

            foreach (SealData seal in regions.Seals ?? new SealData[0])
            {
                sealIds.Add(seal == null ? null : seal.SealId);
            }

            return new SaveContentCatalog(content._species, content._skills, content._passives, content._materials, regionIds, sealIds);
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

        public bool IsKnownRegion(string regionId)
        {
            return _regions == null ? !string.IsNullOrEmpty(regionId) : Contains(_regions, regionId);
        }

        public bool IsKnownSeal(string sealId)
        {
            return _seals == null ? !string.IsNullOrEmpty(sealId) : Contains(_seals, sealId);
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
