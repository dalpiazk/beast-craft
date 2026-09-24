using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// Reads <c>beast-roster.json</c> directly and rebuilds the species in memory.
    /// <para>
    /// Parsing and validation happen here; the field mapping is the Runtime
    /// <see cref="BeastRosterBuilder.BuildAll"/>, the same one the Editor's
    /// <c>BeastRosterImporter</c> applies, so the simulator fights exactly the beasts the Editor
    /// would generate. <c>JsonUtility</c> is unavailable outside Unity, hence System.Text.Json with
    /// public fields included (the case the roster doc already names).
    /// </para>
    /// </summary>
    public static class RosterLoader
    {
        /// <summary>The roster's path relative to the repo root.</summary>
        public const string RepoRelativePath = BeastRosterData.ProjectRelativePath;

        /// <summary>
        /// Resolves the roster file: the explicit path when given, otherwise the first match for
        /// <see cref="RepoRelativePath"/> walking up from the working directory, then from the
        /// executable's directory.
        /// </summary>
        public static string ResolvePath(string explicitPath)
        {
            return ResolveFile(explicitPath, RepoRelativePath);
        }

        /// <summary>
        /// The explicit path when given, otherwise the first match for <paramref name="repoRelativePath"/>
        /// walking up from the working directory, then from the executable's directory.
        /// </summary>
        public static string ResolveFile(string explicitPath, string repoRelativePath)
        {
            if (!string.IsNullOrEmpty(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string found = WalkUp(Directory.GetCurrentDirectory(), repoRelativePath);
            return found ?? WalkUp(AppContext.BaseDirectory, repoRelativePath);
        }

        private static string WalkUp(string start, string repoRelativePath)
        {
            DirectoryInfo directory = new DirectoryInfo(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, repoRelativePath);
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
        /// does not parse or fails <see cref="BeastRosterValidator.Validate"/>. The growth curves are
        /// returned by id too, so the enemies can scale on the same curves. The instances
        /// come from <see cref="BeastRosterBuilder.BuildAll"/>.
        /// </summary>
        public static List<CreatureSpeciesSO> Load(string path, List<string> errors, out Dictionary<string, GrowthRateCurve> curves)
        {
            curves = null;
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

            return BeastRosterBuilder.BuildAll(roster, out curves);
        }
    }
}
