using System.Collections.Generic;
using System.IO;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Copies the encounter content — <c>Data/Encounters/enemy-library.json</c>,
    /// <c>encounter-library.json</c> and the simulator-written <c>encounter-difficulty.json</c> —
    /// into the project's single <see cref="EncounterLibrarySO"/> (menu: Beast Craft/Data/Import
    /// Encounters), linking the enemies' <see cref="GrowthRateCurve"/> asset. Run it after Import
    /// Beast Roster (for the curve asset) and whenever any of the three files changes.
    /// <para>
    /// Validates all three files first — the enemies against the roster (growth curve, id
    /// collisions), the encounters against the enemies and <c>drop-tables.json</c> (shape ids, arena
    /// fit), the difficulty against the encounters — and imports nothing if any check fails. Like
    /// <see cref="DropTableImporter"/>, an existing <see cref="EncounterLibrarySO"/> anywhere in the
    /// project is updated in place (keeping its GUID); only when there is none is one created at
    /// <see cref="AssetPath"/>. The JSON owns the asset's data fields outright.
    /// </para>
    /// </summary>
    public static class EncounterLibraryImporter
    {
        public const string AssetFolder = "Assets/_Project/Data/Encounters";
        public const string AssetPath = AssetFolder + "/EncounterLibrary.asset";

        private const string LogPrefix = "[Encounter Import] ";

        [MenuItem("Beast Craft/Data/Import Encounters")]
        public static void ImportFromMenu()
        {
            Import(EnemyLibraryData.ProjectRelativePath, EncounterLibraryData.ProjectRelativePath, EncounterDifficultyData.ProjectRelativePath,
                   BeastRosterData.ProjectRelativePath, DropTableData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the three encounter files at project-relative paths, cross-checked against the
        /// roster and drop tables at theirs. Returns false if nothing was imported.
        /// </summary>
        public static bool Import(string enemiesPath, string encountersPath, string difficultyPath, string rosterPath, string dropTablesPath)
        {
            List<string> errors = new List<string>();
            EnemyLibraryData enemies = Read<EnemyLibraryData>(enemiesPath, errors);
            EncounterLibraryData encounters = Read<EncounterLibraryData>(encountersPath, errors);
            EncounterDifficultyData difficulty = Read<EncounterDifficultyData>(difficultyPath, errors);
            BeastRosterData roster = Read<BeastRosterData>(rosterPath, errors);
            DropTableData dropTables = Read<DropTableData>(dropTablesPath, errors);

            if (errors.Count == 0)
            {
                Prefix(errors, "enemy-library.json: ", EnemyLibraryValidator.Validate(enemies, roster));
                Prefix(errors, "encounter-library.json: ", EncounterLibraryValidator.Validate(encounters, enemies, dropTables));
                Prefix(errors, "encounter-difficulty.json: ", EncounterDifficultyTable.Validate(difficulty, encounters));
            }

            GrowthRateCurve curve = null;
            if (errors.Count == 0)
            {
                curve = FindCurve(enemies.GrowthCurveId);
                if (curve == null)
                {
                    errors.Add("No GrowthRateCurve asset has CurveId '" + enemies.GrowthCurveId + "'; run Beast Craft/Data/Import Beast Roster first.");
                }
            }

            if (errors.Count == 0)
            {
                foreach (string warning in EncounterDifficultyTable.Warnings(difficulty, encounters))
                {
                    Debug.LogWarning(LogPrefix + "encounter-difficulty.json: " + warning);
                }
            }

            if (errors.Count > 0)
            {
                // All-or-nothing: a half-imported encounter set is worse than the previous good one.
                Debug.LogError(LogPrefix + "Nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EncounterLibrarySO asset = FindExisting();
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<EncounterLibrarySO>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.Enemies = enemies;
            asset.Encounters = encounters;
            asset.Difficulty = difficulty;
            asset.GrowthRate = curve;
            asset.ResetRuntimeCaches();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + enemies.Enemies.Length + " enemies, " + encounters.Shapes.Length + " shapes, " +
                      (encounters.Templates == null ? 0 : encounters.Templates.Length) + " templates and " + difficulty.Cells.Length + " difficulty cells.");
            return true;
        }

        private static T Read<T>(string path, List<string> errors) where T : class
        {
            if (!File.Exists(path))
            {
                errors.Add("File not found: '" + path + "'.");
                return null;
            }

            T data = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (data == null)
            {
                errors.Add("'" + path + "' did not parse.");
            }

            return data;
        }

        private static void Prefix(List<string> into, string prefix, List<string> errors)
        {
            foreach (string error in errors)
            {
                into.Add(prefix + error);
            }
        }

        private static GrowthRateCurve FindCurve(string curveId)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GrowthRateCurve)))
            {
                GrowthRateCurve curve = AssetDatabase.LoadAssetAtPath<GrowthRateCurve>(AssetDatabase.GUIDToAssetPath(guid));
                if (curve != null && curve.CurveId == curveId)
                {
                    return curve;
                }
            }

            return null;
        }

        private static EncounterLibrarySO FindExisting()
        {
            EncounterLibrarySO found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(EncounterLibrarySO)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EncounterLibrarySO asset = AssetDatabase.LoadAssetAtPath<EncounterLibrarySO>(path);
                if (asset == null)
                {
                    continue;
                }

                if (found != null)
                {
                    Debug.LogWarning(LogPrefix + "More than one EncounterLibrarySO; only the first is updated. Extra at '" + path + "'.");
                    continue;
                }

                found = asset;
            }

            return found;
        }
    }
}
