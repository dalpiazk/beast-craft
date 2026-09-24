using System.Collections.Generic;
using System.IO;
using System.Text;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// Generates the starter roster's Unity assets from <c>Data/Creatures/beast-roster.json</c>.
    /// <para>
    /// Idempotent: a <see cref="GrowthRateCurve"/> is matched by <see cref="GrowthRateCurve.CurveId"/>
    /// and a <see cref="CreatureSpeciesSO"/> by <see cref="CreatureSpeciesSO.SpeciesId"/>, anywhere
    /// in the project. A match is updated in place (keeping its GUID, so references to it survive);
    /// only a missing one is created. Running it twice changes nothing the second time.
    /// </para>
    /// <para>
    /// The JSON owns identity, text, elements, base stats, stance, footprint and the growth-curve
    /// link on a species, and the whole of a growth curve; the field mapping is
    /// <see cref="BeastRosterBuilder"/>'s, shared with everything that builds the roster outside the Editor. It does NOT touch a species' icon, evolution options, learnable
    /// skills or customization schema — those are authored on the asset and survive a re-import.
    /// Nothing is ever deleted: a species removed from the JSON leaves its asset behind, logged.
    /// </para>
    /// </summary>
    public static class BeastRosterImporter
    {
        public const string SpeciesFolder = "Assets/_Project/Data/Creatures";
        public const string CurvesFolder = "Assets/_Project/Data/Creatures/GrowthRates";

        private const string LogPrefix = "[Roster Import] ";

        [MenuItem("Beast Craft/Data/Import Beast Roster")]
        public static void ImportFromMenu()
        {
            Import(BeastRosterData.ProjectRelativePath);
        }

        /// <summary>Imports the roster at a project-relative path. Returns false if nothing was imported.</summary>
        public static bool Import(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                Debug.LogError(LogPrefix + "Roster file not found at '" + jsonPath + "'.");
                return false;
            }

            BeastRosterData roster = JsonUtility.FromJson<BeastRosterData>(File.ReadAllText(jsonPath));
            List<string> errors = BeastRosterValidator.Validate(roster);
            if (errors.Count > 0)
            {
                // All-or-nothing: a half-imported roster is worse than the previous good one.
                Debug.LogError(LogPrefix + "'" + jsonPath + "' is invalid; nothing was imported:\n  " +
                               string.Join("\n  ", errors));
                return false;
            }

            EnsureFolder(SpeciesFolder);
            EnsureFolder(CurvesFolder);

            Dictionary<string, GrowthRateCurve> curves = ImportCurves(roster.GrowthCurves);
            ImportSpecies(roster.Species, curves);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Imported " + roster.GrowthCurves.Length + " growth curves and " +
                      roster.Species.Length + " species from '" + jsonPath + "'.");
            return true;
        }

        private static Dictionary<string, GrowthRateCurve> ImportCurves(GrowthCurveData[] data)
        {
            Dictionary<string, GrowthRateCurve> existing = FindExisting<GrowthRateCurve>(c => c.CurveId);
            Dictionary<string, GrowthRateCurve> result = new Dictionary<string, GrowthRateCurve>();

            foreach (GrowthCurveData curveData in data)
            {
                if (!existing.TryGetValue(curveData.CurveId, out GrowthRateCurve curve))
                {
                    curve = ScriptableObject.CreateInstance<GrowthRateCurve>();
                    curve.CurveId = curveData.CurveId;
                    AssetDatabase.CreateAsset(curve, CurvesFolder + "/" + ToPascalCase(curveData.CurveId) + ".asset");
                }

                BeastRosterBuilder.ApplyCurve(curveData, curve);
                EditorUtility.SetDirty(curve);
                result[curveData.CurveId] = curve;
            }

            return result;
        }

        private static void ImportSpecies(SpeciesData[] data, Dictionary<string, GrowthRateCurve> curves)
        {
            Dictionary<string, CreatureSpeciesSO> existing = FindExisting<CreatureSpeciesSO>(s => s.SpeciesId);
            HashSet<string> imported = new HashSet<string>();

            foreach (SpeciesData speciesData in data)
            {
                if (!existing.TryGetValue(speciesData.SpeciesId, out CreatureSpeciesSO species))
                {
                    species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
                    species.SpeciesId = speciesData.SpeciesId;
                    AssetDatabase.CreateAsset(species, SpeciesFolder + "/" + ToPascalCase(speciesData.SpeciesId) + ".asset");
                }

                BeastRosterBuilder.ApplySpecies(speciesData, species, curves[speciesData.GrowthCurveId]);
                EditorUtility.SetDirty(species);
                imported.Add(speciesData.SpeciesId);
            }

            foreach (string orphan in existing.Keys)
            {
                if (!imported.Contains(orphan))
                {
                    Debug.LogWarning(LogPrefix + "Species asset '" + orphan +
                                     "' is not in the roster JSON; left untouched (delete it by hand if intended).");
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
                    Debug.LogError(LogPrefix + "Duplicate id '" + getId(asset) + "' on " + typeof(T).Name +
                                   " at '" + path + "'; only the first asset found is updated.");
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

        /// <summary><c>frost_wyrm</c> to <c>FrostWyrm</c>, for asset file names.</summary>
        public static string ToPascalCase(string snakeCaseId)
        {
            StringBuilder builder = new StringBuilder(snakeCaseId.Length);
            bool upper = true;

            foreach (char c in snakeCaseId)
            {
                if (c == '_')
                {
                    upper = true;
                    continue;
                }

                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }

            return builder.ToString();
        }
    }
}
