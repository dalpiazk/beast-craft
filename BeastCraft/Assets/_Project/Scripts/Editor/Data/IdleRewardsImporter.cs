using System.Collections.Generic;
using System.IO;
using BeastCraft.Idle;
using BeastCraft.Progression;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Copies <c>Data/Idle/idle-rewards.json</c> into the project's single <see cref="IdleRewardsSO"/>
    /// (menu: Beast Craft/Data/Import Idle Rewards), after validating it, cross-checked against
    /// <c>drop-tables.json</c> when that file exists so the idle material shape is a real one. Run it
    /// whenever the rates change (after Import Drop Tables when the shapes change).
    /// <para>
    /// Idempotent and all-or-nothing, like <see cref="RegionLibraryImporter"/>: an existing
    /// <see cref="IdleRewardsSO"/> anywhere in the project is updated in place (keeping its GUID);
    /// only when there is none is one created at <see cref="AssetPath"/>. An invalid file imports
    /// nothing. The JSON owns <see cref="IdleRewardsSO.Data"/> outright.
    /// </para>
    /// </summary>
    public static class IdleRewardsImporter
    {
        public const string AssetFolder = "Assets/_Project/Data/Idle";
        public const string AssetPath = AssetFolder + "/IdleRewards.asset";

        private const string LogPrefix = "[Idle Rewards Import] ";

        [MenuItem("Beast Craft/Data/Import Idle Rewards")]
        public static void ImportFromMenu()
        {
            Import(IdleRewardsData.ProjectRelativePath, DropTableData.ProjectRelativePath);
        }

        /// <summary>
        /// Imports the rates at a project-relative path, cross-checked against the drop tables at
        /// <paramref name="dropTablesPath"/> when that file exists. Returns false if nothing was imported.
        /// </summary>
        public static bool Import(string jsonPath, string dropTablesPath)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogError(LogPrefix + "Idle rewards not found at '" + jsonPath + "'.");
                return false;
            }

            IdleRewardsData rewards = JsonUtility.FromJson<IdleRewardsData>(File.ReadAllText(jsonPath));
            DropTableData tables = File.Exists(dropTablesPath) ? JsonUtility.FromJson<DropTableData>(File.ReadAllText(dropTablesPath)) : null;
            List<string> errors = IdleRewardsValidator.Validate(rewards, tables);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EconomyImportUtil.EnsureFolder(AssetFolder);
            IdleRewardsSO asset = EconomyImportUtil.FindOrCreateSingle<IdleRewardsSO>(AssetPath, LogPrefix);
            asset.Data = rewards;
            asset.ResetRuntimeCaches();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + rewards.Bands.Length + " progress bands (cap " + rewards.CapHours + " h) from '" + jsonPath + "'.");
            return true;
        }
    }
}
