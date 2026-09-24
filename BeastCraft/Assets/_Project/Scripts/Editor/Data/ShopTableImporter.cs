using System.Collections.Generic;
using System.IO;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Skills;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Copies <c>Data/Economy/shop-tables.json</c> into the project's single <see cref="ShopTableSO"/>
    /// (menu: Beast Craft/Data/Import Shop Tables), validated against the skill library (material
    /// and avatar skill ids) and the drop tables (no material tier before its first-clear band).
    /// All-or-nothing; the JSON owns <see cref="ShopTableSO.Data"/> outright.
    /// </summary>
    public static class ShopTableImporter
    {
        public const string AssetPath = "Assets/_Project/Data/Economy/ShopTables.asset";

        private const string LogPrefix = "[Shop Table Import] ";

        [MenuItem("Beast Craft/Data/Import Shop Tables")]
        public static void ImportFromMenu()
        {
            Import(ShopTableData.ProjectRelativePath, SkillLibraryData.ProjectRelativePath, DropTableData.ProjectRelativePath);
        }

        /// <summary>Imports the tables, cross-checked against the skill library and drop tables (both required). False if nothing was imported.</summary>
        public static bool Import(string jsonPath, string libraryPath, string dropTablesPath)
        {
            if (!File.Exists(jsonPath) || !File.Exists(libraryPath) || !File.Exists(dropTablesPath))
            {
                Debug.LogError(LogPrefix + "Not found: '" + jsonPath + "', '" + libraryPath + "' or '" + dropTablesPath + "'.");
                return false;
            }

            ShopTableData data = JsonUtility.FromJson<ShopTableData>(File.ReadAllText(jsonPath));
            SkillLibraryData library = JsonUtility.FromJson<SkillLibraryData>(File.ReadAllText(libraryPath));
            DropTableData drops = JsonUtility.FromJson<DropTableData>(File.ReadAllText(dropTablesPath));
            List<string> errors = ShopTableValidator.Validate(data, library, drops);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EconomyImportUtil.EnsureFolder("Assets/_Project/Data/Economy");
            ShopTableSO asset = EconomyImportUtil.FindOrCreateSingle<ShopTableSO>(AssetPath, LogPrefix);
            asset.Data = data;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + data.Bands.Length + " Trader bands from '" + jsonPath + "'.");
            return true;
        }
    }
}
