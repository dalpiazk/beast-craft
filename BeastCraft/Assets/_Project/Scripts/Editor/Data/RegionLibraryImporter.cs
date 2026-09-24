using System.Collections.Generic;
using System.IO;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Copies <c>Data/Campaign/regions.json</c> and <c>location-names.json</c> into the project's single
    /// <see cref="RegionLibrarySO"/> (menu: Beast Craft/Data/Import Regions), after validating the
    /// regions against <c>encounter-library.json</c> (every Battle shape and gate and boss template
    /// exists) and the location names against the regions. Run it whenever any of the files changes
    /// (after Import Encounters when templates change).
    /// <para>
    /// Idempotent and all-or-nothing, like <see cref="EncounterLibraryImporter"/>: an existing
    /// <see cref="RegionLibrarySO"/> anywhere in the project is updated in place (keeping its GUID);
    /// only when there is none is one created at <see cref="AssetPath"/>. An invalid file imports
    /// nothing. The JSON owns <see cref="RegionLibrarySO.Data"/> outright.
    /// </para>
    /// </summary>
    public static class RegionLibraryImporter
    {
        public const string AssetFolder = "Assets/_Project/Data/Campaign";
        public const string AssetPath = AssetFolder + "/RegionLibrary.asset";

        private const string LogPrefix = "[Region Import] ";

        [MenuItem("Beast Craft/Data/Import Regions")]
        public static void ImportFromMenu()
        {
            Import(RegionLibraryData.ProjectRelativePath, EncounterLibraryData.ProjectRelativePath, LocationNameTableData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the regions at a project-relative path, cross-checked against the encounter
        /// library at <paramref name="encountersPath"/> (required), with the location names at their
        /// default path. Returns false if nothing was imported.
        /// </summary>
        public static bool Import(string regionsPath, string encountersPath)
        {
            return Import(regionsPath, encountersPath, LocationNameTableData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the regions at a project-relative path, cross-checked against the encounter
        /// library at <paramref name="encountersPath"/>, and the location names at
        /// <paramref name="locationNamesPath"/>, checked against the regions (all required). Returns
        /// false if nothing was imported.
        /// </summary>
        public static bool Import(string regionsPath, string encountersPath, string locationNamesPath)
        {
            if (!File.Exists(regionsPath) || !File.Exists(encountersPath) || !File.Exists(locationNamesPath))
            {
                Debug.LogError(LogPrefix + "Not found: '" + regionsPath + "', '" + encountersPath + "' or '" + locationNamesPath + "'.");
                return false;
            }

            RegionLibraryData regions = JsonUtility.FromJson<RegionLibraryData>(File.ReadAllText(regionsPath));
            EncounterLibraryData encounters = JsonUtility.FromJson<EncounterLibraryData>(File.ReadAllText(encountersPath));
            List<string> errors = RegionLibraryValidator.Validate(regions, encounters);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + regionsPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            LocationNameTableData names = JsonUtility.FromJson<LocationNameTableData>(File.ReadAllText(locationNamesPath));
            List<string> nameErrors = LocationNameTableValidator.Validate(names, regions);
            if (nameErrors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + locationNamesPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", nameErrors));
                return false;
            }

            RegionLibrarySO asset = FindExisting();
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(AssetFolder))
                {
                    AssetDatabase.CreateFolder("Assets/_Project/Data", "Campaign");
                }

                asset = ScriptableObject.CreateInstance<RegionLibrarySO>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.Data = regions;
            asset.LocationNames = names;
            asset.ResetRuntimeCaches();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + regions.Regions.Length + " regions and " + regions.Seals.Length + " seals from '" + regionsPath +
                      "', with location names from '" + locationNamesPath + "'.");
            return true;
        }

        private static RegionLibrarySO FindExisting()
        {
            RegionLibrarySO found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RegionLibrarySO)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                RegionLibrarySO asset = AssetDatabase.LoadAssetAtPath<RegionLibrarySO>(path);
                if (asset == null)
                {
                    continue;
                }

                if (found != null)
                {
                    Debug.LogWarning(LogPrefix + "More than one RegionLibrarySO; only the first is updated. Extra at '" + path + "'.");
                    continue;
                }

                found = asset;
            }

            return found;
        }
    }
}
