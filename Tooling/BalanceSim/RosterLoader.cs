using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using UnityEngine;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Reads <c>beast-roster.json</c> directly and rebuilds the species in memory.
    /// <para>
    /// Mirrors <c>BeastCraft.Editor.Data.BeastRosterImporter</c>'s field mapping (curves: CurveId,
    /// Curve from <see cref="GrowthCurveData.ToAnimationCurve"/>, MaxLevel; species: SpeciesId,
    /// DisplayName, Description, BaseStats, GrowthRate, Elements via
    /// <see cref="BeastRosterValidator.TryParseElement"/>) so the simulator fights the same beasts
    /// the Editor would generate. <c>JsonUtility</c> is unavailable outside Unity, hence
    /// System.Text.Json with public fields included — the case the roster doc already names.
    /// </para>
    /// </summary>
    public static class RosterLoader
    {
        /// <summary>The roster's path relative to the repo root.</summary>
        public const string RepoRelativePath = "BeastCraft/" + BeastRosterData.ProjectRelativePath;

        /// <summary>
        /// Resolves the roster file: the explicit path when given, otherwise the first match for
        /// <see cref="RepoRelativePath"/> walking up from the working directory, then from the
        /// executable's directory.
        /// </summary>
        public static string ResolvePath(string explicitPath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string found = WalkUp(Directory.GetCurrentDirectory());
            return found ?? WalkUp(AppContext.BaseDirectory);
        }

        private static string WalkUp(string start)
        {
            DirectoryInfo directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, RepoRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>
        /// Parses and validates the roster, then builds one <see cref="CreatureSpeciesSO"/> per
        /// species in file order. Returns null and fills <paramref name="errors"/> when the file
        /// does not parse or fails <see cref="BeastRosterValidator.Validate"/>.
        /// </summary>
        public static List<CreatureSpeciesSO> Load(string path, List<string> errors)
        {
            BeastRosterData roster;
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
                roster = JsonSerializer.Deserialize<BeastRosterData>(File.ReadAllText(path), options);
            }
            catch (Exception exception)
            {
                errors.Add("Could not read '" + path + "': " + exception.Message);
                return null;
            }

            errors.AddRange(BeastRosterValidator.Validate(roster));
            if (errors.Count > 0)
            {
                return null;
            }

            Dictionary<string, GrowthRateCurve> curves = new Dictionary<string, GrowthRateCurve>(StringComparer.Ordinal);
            foreach (GrowthCurveData curveData in roster.GrowthCurves)
            {
                GrowthRateCurve curve = ScriptableObject.CreateInstance<GrowthRateCurve>();
                curve.name = curveData.CurveId;
                curve.CurveId = curveData.CurveId;
                curve.Curve = curveData.ToAnimationCurve();
                curve.MaxLevel = curveData.MaxLevel;
                curves[curveData.CurveId] = curve;
            }

            List<CreatureSpeciesSO> species = new List<CreatureSpeciesSO>();
            foreach (SpeciesData speciesData in roster.Species)
            {
                CreatureSpeciesSO beast = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
                beast.name = speciesData.SpeciesId;
                beast.SpeciesId = speciesData.SpeciesId;
                beast.DisplayName = speciesData.DisplayName;
                beast.Description = speciesData.Description;
                beast.BaseStats = speciesData.BaseStats;
                beast.GrowthRate = curves[speciesData.GrowthCurveId];

                Element[] elements = new Element[speciesData.Elements.Length];
                for (int i = 0; i < elements.Length; i++)
                {
                    BeastRosterValidator.TryParseElement(speciesData.Elements[i], out elements[i]);
                }

                beast.Elements = elements;
                species.Add(beast);
            }

            return species;
        }
    }
}
