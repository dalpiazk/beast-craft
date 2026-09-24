using System.Collections.Generic;
using System.IO;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Customization;
using BeastCraft.Customization.Avatar;
using BeastCraft.Customization.Creature;
using BeastCraft.Economy;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Imports <c>Data/Cosmetics/cosmetic-library.json</c> (menu: Beast Craft/Data/Import Cosmetics):
    /// one <see cref="CustomizationCategoryDefinition"/> per category (options with no art yet: the
    /// option's source, unlock id, region, rarity and art key go into its <c>UnlockTags</c> as
    /// <c>source:</c>, <c>unlock:</c>, <c>minregion:</c>, <c>rarity:</c> and <c>art:</c> tags), the
    /// single <see cref="AvatarCustomizationSchema"/> (every avatar category) and one
    /// <see cref="CreatureCustomizationSchema"/> per species (its own categories: looks are per
    /// species), assigned to each <see cref="CreatureSpeciesSO.CustomizationSchema"/>; and the single
    /// <see cref="CosmeticLibrarySO"/>. Validated against the roster and <c>regions.json</c> first;
    /// all-or-nothing. Run after Import Beast Roster.
    /// </summary>
    public static class CosmeticLibraryImporter
    {
        public const string CategoryFolder = "Assets/_Project/Data/Customization/Categories";
        public const string AvatarSchemaPath = "Assets/_Project/Data/Customization/Avatar/AvatarCustomizationSchema.asset";
        public const string CreatureSchemaFolder = "Assets/_Project/Data/Customization/Creature";
        public const string LibraryPath = "Assets/_Project/Data/Cosmetics/CosmeticLibrary.asset";

        private const string LogPrefix = "[Cosmetic Import] ";

        [MenuItem("Beast Craft/Data/Import Cosmetics")]
        public static void ImportFromMenu()
        {
            Import(CosmeticLibraryData.ProjectRelativePath, BeastRosterData.ProjectRelativePath, RegionLibraryData.ProjectRelativePath);
        }

        /// <summary>Imports the library, checked against the roster and regions (both required). False if nothing was imported.</summary>
        public static bool Import(string jsonPath, string rosterPath, string regionsPath)
        {
            if (!File.Exists(jsonPath) || !File.Exists(rosterPath) || !File.Exists(regionsPath))
            {
                Debug.LogError(LogPrefix + "Not found: '" + jsonPath + "', '" + rosterPath + "' or '" + regionsPath + "'.");
                return false;
            }

            CosmeticLibraryData data = JsonUtility.FromJson<CosmeticLibraryData>(File.ReadAllText(jsonPath));
            BeastRosterData roster = JsonUtility.FromJson<BeastRosterData>(File.ReadAllText(rosterPath));
            RegionLibraryData regions = JsonUtility.FromJson<RegionLibraryData>(File.ReadAllText(regionsPath));
            List<string> speciesIds = new List<string>();
            foreach (SpeciesData entry in roster.Species)
            {
                speciesIds.Add(entry.SpeciesId);
            }

            List<string> regionIds = new List<string>();
            foreach (RegionData region in regions.Regions)
            {
                regionIds.Add(region.RegionId);
            }

            List<string> errors = CosmeticLibraryValidator.Validate(data, speciesIds, regionIds);
            if (errors.Count > 0)
            {
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " + string.Join("\n  ", errors));
                return false;
            }

            EconomyImportUtil.EnsureFolder(CategoryFolder);
            EconomyImportUtil.EnsureFolder(CreatureSchemaFolder);
            EconomyImportUtil.EnsureFolder("Assets/_Project/Data/Customization/Avatar");
            EconomyImportUtil.EnsureFolder("Assets/_Project/Data/Cosmetics");

            Dictionary<string, CustomizationCategoryDefinition> existing = EconomyImportUtil.FindExisting<CustomizationCategoryDefinition>(c => c.CategoryId, LogPrefix);
            Dictionary<string, List<CustomizationCategoryDefinition>> byScope = new Dictionary<string, List<CustomizationCategoryDefinition>>();
            foreach (CosmeticCategoryData c in data.Categories)
            {
                CustomizationCategoryDefinition asset = EconomyImportUtil.FindOrCreate(existing, c.CategoryId, CategoryFolder);
                Apply(c, asset);
                EditorUtility.SetDirty(asset);
                if (!byScope.TryGetValue(c.Scope, out List<CustomizationCategoryDefinition> list))
                {
                    list = new List<CustomizationCategoryDefinition>();
                    byScope[c.Scope] = list;
                }

                list.Add(asset);
            }

            AvatarCustomizationSchema avatarSchema = EconomyImportUtil.FindOrCreateSingle<AvatarCustomizationSchema>(AvatarSchemaPath, LogPrefix);
            avatarSchema.Categories = byScope[CosmeticLibrary.AvatarScope];
            EditorUtility.SetDirty(avatarSchema);

            Dictionary<string, CreatureSpeciesSO> species = EconomyImportUtil.FindExisting<CreatureSpeciesSO>(s => s.SpeciesId, LogPrefix);
            Dictionary<string, CreatureCustomizationSchema> schemas = EconomyImportUtil.FindExisting<CreatureCustomizationSchema>(s => s.name, LogPrefix);
            foreach (string speciesId in speciesIds)
            {
                string schemaName = BeastRosterImporter.ToPascalCase(speciesId) + "CustomizationSchema";
                CreatureCustomizationSchema schema = EconomyImportUtil.FindOrCreate(schemas, schemaName, CreatureSchemaFolder);
                schema.name = schemaName;
                schema.Categories = byScope[speciesId];
                EditorUtility.SetDirty(schema);
                if (species.TryGetValue(speciesId, out CreatureSpeciesSO asset))
                {
                    asset.CustomizationSchema = schema;
                    EditorUtility.SetDirty(asset);
                }
                else
                {
                    Debug.LogWarning(LogPrefix + "No species asset '" + speciesId + "' to assign its schema to; run Import Beast Roster first.");
                }
            }

            CosmeticLibrarySO library = EconomyImportUtil.FindOrCreateSingle<CosmeticLibrarySO>(LibraryPath, LogPrefix);
            library.Data = data;
            library.ResetRuntimeCaches();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + data.Categories.Length + " cosmetic categories from '" + jsonPath + "'.");
            return true;
        }

        /// <summary>Writes <paramref name="data"/> onto <paramref name="asset"/> (art left empty until it exists).</summary>
        public static void Apply(CosmeticCategoryData data, CustomizationCategoryDefinition asset)
        {
            asset.name = data.CategoryId;
            asset.CategoryId = data.CategoryId;
            asset.DisplayName = data.DisplayName;
            asset.ValueType = data.ValueType == "ColorPicker" ? CustomizationValueType.ColorPicker : CustomizationValueType.DiscreteOption;
            asset.ColorPicker = new ColorPickerDefinition { DefaultColor = CosmeticCategory.ParseColor(data.DefaultColor) };
            asset.Options = new List<CustomizationOptionDefinition>();
            foreach (CosmeticOptionData o in data.Options ?? new CosmeticOptionData[0])
            {
                asset.Options.Add(new CustomizationOptionDefinition
                {
                    OptionId = o.OptionId,
                    DisplayName = o.DisplayName,
                    SortOrder = o.SortOrder,
                    IsDefault = o.IsDefault,
                    SupportsPaletteShader = o.SupportsPaletteShader,
                    UnlockTags = new List<string>
                    {
                        "source:" + o.Source,
                        "unlock:" + o.UnlockId,
                        "minregion:" + o.MinRegion,
                        "rarity:" + o.Rarity,
                        "art:" + o.ArtKey
                    }
                });
            }
        }
    }
}
