using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Economy;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Imports <c>Data/Items/gear-library.json</c> (menu: Beast Craft/Data/Import Gear Library): one
    /// <see cref="GearSO"/> per piece of beast gear and one <see cref="AvatarGearSO"/> per piece of
    /// avatar gear (matched by id and updated in place, created when new), plus the single
    /// <see cref="GearLibrarySO"/> holding the data for the drop, Trader and boss pools. Validated
    /// first with <see cref="GearLibraryValidator"/> against the roster (the power budget);
    /// all-or-nothing. Run after Import Beast Roster.
    /// </summary>
    public static class GearLibraryImporter
    {
        public const string BeastGearFolder = "Assets/_Project/Data/Gear";
        public const string AvatarGearFolder = "Assets/_Project/Data/AvatarGear";
        public const string LibraryPath = "Assets/_Project/Data/Items/GearLibrary.asset";

        private const string LogPrefix = "[Gear Import] ";

        [MenuItem("Beast Craft/Data/Import Gear Library")]
        public static void ImportFromMenu()
        {
            Import(GearLibraryData.ProjectRelativePath, BeastRosterData.ProjectRelativePath);
        }

        /// <summary>Imports the library, budget-checked against the roster at <paramref name="rosterPath"/> (required). False if nothing was imported.</summary>
        public static bool Import(string jsonPath, string rosterPath)
        {
            if (!File.Exists(jsonPath) || !File.Exists(rosterPath))
            {
                Debug.LogError(LogPrefix + "Not found: '" + jsonPath + "' or '" + rosterPath + "'.");
                return false;
            }

            GearLibraryData data = JsonUtility.FromJson<GearLibraryData>(File.ReadAllText(jsonPath));
            BeastRosterData roster = JsonUtility.FromJson<BeastRosterData>(File.ReadAllText(rosterPath));
            List<CreatureSpeciesSO> species = BeastRosterBuilder.BuildAll(roster, out Dictionary<string, GrowthRateCurve> _);
            List<string> errors = GearLibraryValidator.Validate(data, species);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EconomyImportUtil.EnsureFolder(BeastGearFolder);
            EconomyImportUtil.EnsureFolder(AvatarGearFolder);
            EconomyImportUtil.EnsureFolder("Assets/_Project/Data/Items");

            GearLibrary library = GearLibrary.Build(data);
            Dictionary<string, GearSO> beast = EconomyImportUtil.FindExisting<GearSO>(g => g.GearId, LogPrefix);
            Dictionary<string, AvatarGearSO> avatar = EconomyImportUtil.FindExisting<AvatarGearSO>(g => g.AvatarGearId, LogPrefix);
            foreach (GearItem item in library.Items)
            {
                if (item.IsAvatarGear)
                {
                    AvatarGearSO asset = EconomyImportUtil.FindOrCreate(avatar, item.GearId, AvatarGearFolder);
                    GearLibrary.Apply(item, asset);
                    EditorUtility.SetDirty(asset);
                }
                else
                {
                    GearSO asset = EconomyImportUtil.FindOrCreate(beast, item.GearId, BeastGearFolder);
                    GearLibrary.Apply(item, asset);
                    EditorUtility.SetDirty(asset);
                }
            }

            GearLibrarySO libraryAsset = EconomyImportUtil.FindOrCreateSingle<GearLibrarySO>(LibraryPath, LogPrefix);
            libraryAsset.Data = data;
            libraryAsset.ResetRuntimeCaches();
            EditorUtility.SetDirty(libraryAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + data.BeastGear.Length + " beast and " + data.AvatarGear.Length + " avatar gear pieces from '" + jsonPath + "'.");
            return true;
        }
    }
}
