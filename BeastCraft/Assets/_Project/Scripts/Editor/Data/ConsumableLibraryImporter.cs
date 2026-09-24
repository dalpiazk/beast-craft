using System.Collections.Generic;
using System.IO;
using BeastCraft.Economy;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Imports <c>Data/Items/consumable-library.json</c> (menu: Beast Craft/Data/Import Consumables):
    /// one <see cref="ConsumableSO"/> per consumable (matched by id and updated in place, created when
    /// new) and the single <see cref="ConsumableLibrarySO"/>. Validated first with
    /// <see cref="ConsumableLibraryValidator"/>; all-or-nothing.
    /// </summary>
    public static class ConsumableLibraryImporter
    {
        public const string AssetFolder = "Assets/_Project/Data/Items/Consumables";
        public const string LibraryPath = "Assets/_Project/Data/Items/ConsumableLibrary.asset";

        private const string LogPrefix = "[Consumable Import] ";

        [MenuItem("Beast Craft/Data/Import Consumables")]
        public static void ImportFromMenu()
        {
            Import(ConsumableLibraryData.ProjectRelativePath);
        }

        /// <summary>Imports the library at a project-relative path. False if nothing was imported.</summary>
        public static bool Import(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogError(LogPrefix + "Not found: '" + jsonPath + "'.");
                return false;
            }

            ConsumableLibraryData data = JsonUtility.FromJson<ConsumableLibraryData>(File.ReadAllText(jsonPath));
            List<string> errors = ConsumableLibraryValidator.Validate(data);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EconomyImportUtil.EnsureFolder(AssetFolder);
            Dictionary<string, ConsumableSO> existing = EconomyImportUtil.FindExisting<ConsumableSO>(c => c.ConsumableId, LogPrefix);
            foreach (ConsumableData entry in data.Consumables)
            {
                ConsumableSO asset = EconomyImportUtil.FindOrCreate(existing, entry.ConsumableId, AssetFolder);
                ConsumableLibrary.Apply(entry, asset);
                EditorUtility.SetDirty(asset);
            }

            ConsumableLibrarySO library = EconomyImportUtil.FindOrCreateSingle<ConsumableLibrarySO>(LibraryPath, LogPrefix);
            library.Data = data;
            library.ResetRuntimeCaches();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + data.Consumables.Length + " consumables from '" + jsonPath + "'.");
            return true;
        }
    }
}
