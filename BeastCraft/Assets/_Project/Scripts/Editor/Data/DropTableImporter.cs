using System.Collections.Generic;
using System.IO;
using BeastCraft.Progression;
using BeastCraft.Skills;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Copies <c>Data/Skills/drop-tables.json</c> into the project's single <see cref="DropTableSO"/>
    /// (menu: Beast Craft/Data/Import Drop Tables), after validating it against the skill library's
    /// materials so every drop names a real material.
    /// <para>
    /// Idempotent, like <see cref="SkillLibraryImporter"/>: an existing <see cref="DropTableSO"/>
    /// anywhere in the project is updated in place (keeping its GUID); only when there is none is one
    /// created at <see cref="AssetPath"/>. All-or-nothing: an invalid file imports nothing. The JSON
    /// owns <see cref="DropTableSO.Data"/> outright.
    /// </para>
    /// </summary>
    public static class DropTableImporter
    {
        public const string AssetFolder = "Assets/_Project/Data/Skills";
        public const string AssetPath = AssetFolder + "/DropTables.asset";

        private const string LogPrefix = "[Drop Table Import] ";

        [MenuItem("Beast Craft/Data/Import Drop Tables")]
        public static void ImportFromMenu()
        {
            Import(DropTableData.ProjectRelativePath, SkillLibraryData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the tables at a project-relative path, cross-checked against the skill library at
        /// <paramref name="libraryPath"/> when that file exists. Returns false if nothing was imported.
        /// </summary>
        public static bool Import(string jsonPath, string libraryPath)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogError(LogPrefix + "Drop tables not found at '" + jsonPath + "'.");
                return false;
            }

            DropTableData tables = JsonUtility.FromJson<DropTableData>(File.ReadAllText(jsonPath));
            SkillLibraryData library = File.Exists(libraryPath) ? JsonUtility.FromJson<SkillLibraryData>(File.ReadAllText(libraryPath)) : null;
            List<string> errors = DropTableValidator.Validate(tables, library == null ? null : library.Materials);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            DropTableSO asset = FindExisting();
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<DropTableSO>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.Data = tables;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + tables.Bands.Length + " level bands x " + tables.Shapes.Length + " shapes from '" + jsonPath + "'.");
            return true;
        }

        private static DropTableSO FindExisting()
        {
            DropTableSO found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(DropTableSO)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                DropTableSO asset = AssetDatabase.LoadAssetAtPath<DropTableSO>(path);
                if (asset == null)
                {
                    continue;
                }

                if (found != null)
                {
                    Debug.LogWarning(LogPrefix + "More than one DropTableSO; only the first is updated. Extra at '" + path + "'.");
                    continue;
                }

                found = asset;
            }

            return found;
        }
    }
}
