using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;
using BeastCraft.Skills;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Generates the skill assets from <c>Data/Skills/skill-library.json</c> and wires each species'
    /// learnable skills and default loadout.
    /// <para>
    /// Idempotent, like <see cref="BeastRosterImporter"/>: a <see cref="SkillSO"/> is matched by
    /// <see cref="SkillSO.SkillId"/>, a <see cref="PassiveSkillSO"/> by
    /// <see cref="PassiveSkillSO.PassiveId"/> and a <see cref="SkillMaterialSO"/> by
    /// <see cref="SkillMaterialSO.MaterialId"/>, anywhere in the project. A match is updated in place
    /// (keeping its GUID, so references to it survive); only a missing one is created. Running it
    /// twice changes nothing the second time.
    /// </para>
    /// <para>
    /// The JSON owns every field it names (see <see cref="SkillLibraryBuilder"/>); an asset's
    /// <c>Icon</c> is authored on the asset and survives a re-import. On each
    /// <see cref="CreatureSpeciesSO"/> named by a species kit, the JSON owns
    /// <see cref="CreatureSpeciesSO.LearnableSkills"/> and <see cref="CreatureSpeciesSO.DefaultLoadout"/>,
    /// which are replaced outright. Species assets come from the roster importer, so run
    /// "Import Beast Roster" first; a kit whose species asset does not exist is skipped with an error.
    /// Nothing is ever deleted: an asset whose id left the JSON stays behind, logged.
    /// </para>
    /// </summary>
    public static class SkillLibraryImporter
    {
        public const string BeastSkillsFolder = "Assets/_Project/Data/Skills/Beast";
        public const string AvatarActivesFolder = "Assets/_Project/Data/Skills/AvatarActive";
        public const string AvatarPassivesFolder = "Assets/_Project/Data/Skills/AvatarPassive";
        public const string MaterialsFolder = "Assets/_Project/Data/Skills/Materials";

        private const string LogPrefix = "[Skill Library Import] ";

        [MenuItem("Beast Craft/Data/Import Skill Library")]
        public static void ImportFromMenu()
        {
            Import(SkillLibraryData.ProjectRelativePath, BeastRosterData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the library at a project-relative path, cross-checked against the roster at
        /// <paramref name="rosterPath"/> when that file exists. Returns false if nothing was imported.
        /// </summary>
        public static bool Import(string jsonPath, string rosterPath)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogError(LogPrefix + "Skill library not found at '" + jsonPath + "'.");
                return false;
            }

            SkillLibraryData library = JsonUtility.FromJson<SkillLibraryData>(File.ReadAllText(jsonPath));
            BeastRosterData roster = File.Exists(rosterPath) ? JsonUtility.FromJson<BeastRosterData>(File.ReadAllText(rosterPath)) : null;
            List<string> errors = SkillLibraryValidator.Validate(library, roster);
            if (errors.Count > 0)
            {
                // All-or-nothing: a half-imported library is worse than the previous good one.
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EnsureFolder(BeastSkillsFolder);
            EnsureFolder(AvatarActivesFolder);
            EnsureFolder(AvatarPassivesFolder);
            EnsureFolder(MaterialsFolder);

            Dictionary<string, SkillSO> existingSkills = FindExisting<SkillSO>(s => s.SkillId);
            HashSet<string> importedSkills = new HashSet<string>();
            Dictionary<string, SkillSO> beastSkills = ImportSkills(library.BeastSkills, BeastSkillsFolder, existingSkills, importedSkills);
            ImportSkills(library.AvatarActives, AvatarActivesFolder, existingSkills, importedSkills);
            WarnOrphans(existingSkills.Keys, importedSkills, "Skill");

            Dictionary<string, PassiveSkillSO> existingPassives = FindExisting<PassiveSkillSO>(p => p.PassiveId);
            HashSet<string> importedPassives = new HashSet<string>();
            foreach (PassiveData data in library.AvatarPassives)
            {
                PassiveSkillSO passive = FindOrCreate(existingPassives, data.PassiveId, AvatarPassivesFolder, p => p.PassiveId = data.PassiveId);
                SkillLibraryBuilder.ApplyPassive(data, passive);
                EditorUtility.SetDirty(passive);
                importedPassives.Add(data.PassiveId);
            }

            WarnOrphans(existingPassives.Keys, importedPassives, "Passive");

            Dictionary<string, SkillMaterialSO> existingMaterials = FindExisting<SkillMaterialSO>(m => m.MaterialId);
            HashSet<string> importedMaterials = new HashSet<string>();
            foreach (SkillMaterialData data in library.Materials)
            {
                SkillMaterialSO material = FindOrCreate(existingMaterials, data.MaterialId, MaterialsFolder, m => m.MaterialId = data.MaterialId);
                SkillLibraryBuilder.ApplyMaterial(data, material);
                EditorUtility.SetDirty(material);
                importedMaterials.Add(data.MaterialId);
            }

            WarnOrphans(existingMaterials.Keys, importedMaterials, "Material");

            int wired = WireSpecies(library.SpeciesKits, beastSkills);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + library.BeastSkills.Length + " beast skills, " + library.AvatarActives.Length + " avatar actives, " +
                      library.AvatarPassives.Length + " avatar passives and " + library.Materials.Length + " materials, and wired " + wired +
                      " species from '" + jsonPath + "'.");
            return true;
        }

        private static Dictionary<string, SkillSO> ImportSkills(SkillData[] data, string folder, Dictionary<string, SkillSO> existing, HashSet<string> imported)
        {
            Dictionary<string, SkillSO> result = new Dictionary<string, SkillSO>();

            foreach (SkillData skillData in data)
            {
                SkillSO skill = FindOrCreate(existing, skillData.SkillId, folder, s => s.SkillId = skillData.SkillId);
                SkillLibraryBuilder.ApplySkill(skillData, skill);
                EditorUtility.SetDirty(skill);
                imported.Add(skillData.SkillId);
                result[skillData.SkillId] = skill;
            }

            return result;
        }

        private static int WireSpecies(SpeciesKitData[] kits, Dictionary<string, SkillSO> beastSkills)
        {
            Dictionary<string, CreatureSpeciesSO> species = FindExisting<CreatureSpeciesSO>(s => s.SpeciesId);
            int wired = 0;

            foreach (SpeciesKitData kit in kits)
            {
                if (!species.TryGetValue(kit.SpeciesId, out CreatureSpeciesSO asset))
                {
                    Debug.LogError(LogPrefix + "No CreatureSpeciesSO with SpeciesId '" + kit.SpeciesId +
                                   "'; its kit was not wired (run Beast Craft/Data/Import Beast Roster first).");
                    continue;
                }

                asset.LearnableSkills = new List<SkillLearnEntry>();
                foreach (LearnEntryData entry in kit.LearnableSkills)
                {
                    asset.LearnableSkills.Add(new SkillLearnEntry { Level = entry.Level, Skill = beastSkills[entry.SkillId] });
                }

                asset.DefaultLoadout = new List<SkillSO>();
                foreach (string id in kit.DefaultLoadout)
                {
                    asset.DefaultLoadout.Add(beastSkills[id]);
                }

                EditorUtility.SetDirty(asset);
                wired++;
            }

            return wired;
        }

        private static T FindOrCreate<T>(Dictionary<string, T> existing, string id, string folder, System.Action<T> setId) where T : ScriptableObject
        {
            if (existing.TryGetValue(id, out T asset))
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            setId(asset);
            AssetDatabase.CreateAsset(asset, folder + "/" + BeastRosterImporter.ToPascalCase(id) + ".asset");
            existing[id] = asset;
            return asset;
        }

        private static void WarnOrphans(IEnumerable<string> existing, HashSet<string> imported, string kind)
        {
            foreach (string id in existing)
            {
                if (!imported.Contains(id))
                {
                    Debug.LogWarning(LogPrefix + kind + " asset '" + id + "' is not in the skill library JSON; left untouched (delete it by hand if intended).");
                }
            }
        }

        /// <summary>
        /// Every asset of type <typeparamref name="T"/> in the project, keyed by its id. Assets with an
        /// empty id are ignored; a duplicated id keeps the first and logs the rest, which the importer
        /// then never touches.
        /// </summary>
        private static Dictionary<string, T> FindExisting<T>(System.Func<T, string> getId) where T : ScriptableObject
        {
            Dictionary<string, T> byId = new Dictionary<string, T>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || string.IsNullOrEmpty(getId(asset)))
                {
                    continue;
                }

                if (byId.ContainsKey(getId(asset)))
                {
                    Debug.LogError(LogPrefix + "Duplicate id '" + getId(asset) + "' on " + typeof(T).Name + " at '" + path +
                                   "'; only the first asset found is updated.");
                    continue;
                }

                byId.Add(getId(asset), asset);
            }

            return byId;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
