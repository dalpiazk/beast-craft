using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BeastCraft.Encounters;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// <c>--write-difficulty</c>: writes a run's calibrated multipliers as the game's
    /// <c>encounter-difficulty.json</c> (<see cref="EncounterDifficultyData"/>), one cell per
    /// (kit mode, shape, level), in kit-mode, shape (file order) and level order. Deterministic text:
    /// the same run writes the same bytes. The report is not touched.
    /// </summary>
    public static class DifficultyWriter
    {
        public static void Write(string path, SimOptions options, EncounterCatalog encounters, List<PveCell> cells)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"_readme\": ").Append(Quote(Readme(options, encounters))).Append(",\n");
            json.Append("  \"SchemaVersion\": ").Append(EncounterDifficultyData.CurrentSchemaVersion).Append(",\n");
            json.Append("  \"Seed\": ").Append(options.Seed.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"CalibratedOn\": ").Append(Quote(SimOptions.CalibrationName(options.EffectiveCalibrateOn))).Append(",\n");
            json.Append("  \"Compositions\": ").Append(options.Compositions.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"Targets\": [\n");
            List<string> targets = new List<string>();
            foreach (EncounterShape shape in encounters.Shapes)
            {
                targets.Add("    { \"Shape\": " + Quote(shape.Id) + ", \"TargetClear\": " + Number(options.TargetFor(shape)) + " }");
            }

            json.Append(string.Join(",\n", targets)).Append("\n  ],\n");
            json.Append("  \"Cells\": [\n");

            List<string> rows = new List<string>();
            foreach (KitMode mode in options.Modes)
            {
                foreach (EncounterShape shape in encounters.Shapes)
                {
                    List<int> levels = new List<int>(options.Levels);
                    levels.Sort();
                    foreach (int level in levels)
                    {
                        foreach (PveCell cell in cells)
                        {
                            if (cell.Mode == mode && cell.Level == level && cell.Shape == shape)
                            {
                                rows.Add("    { \"KitMode\": " + Quote(SimOptions.ModeName(mode)) + ", \"Shape\": " + Quote(shape.Id) + ", \"Level\": " +
                                         level.ToString(CultureInfo.InvariantCulture) + ", \"Multiplier\": " + Number(cell.Multiplier) + ", \"TargetClear\": " +
                                         Number(options.TargetFor(shape)) + " }");
                            }
                        }
                    }
                }
            }

            json.Append(string.Join(",\n", rows)).Append("\n  ]\n}\n");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// The documented command that writes the committed (shipping) table: the tuned report's command
        /// with <c>--gear typical</c> (user decision: the shipping difficulty assumes the gear a player
        /// normally wears) and no <c>--out</c>. The committed tuned report itself stays gearless
        /// (<c>-- --panel 16x4 --avatar-value --out docs/balance/tuned-report.md</c>).
        /// </summary>
        public const string Command = "dotnet run --project Tooling/BalanceSim -c Release -- --panel 16x4 --avatar-value --gear typical " +
                                      "--write-difficulty content/data/Encounters/encounter-difficulty.json";

        private static string Readme(SimOptions options, EncounterCatalog encounters)
        {
            return "WRITTEN BY Tooling/BalanceSim --write-difficulty; never hand-edit (re-run the simulator after changing the roster, skills, " +
                   "enemies or shapes, including a shape's TargetClear). One calibrated difficulty multiplier per (kit mode, encounter shape, level): " +
                   "every enemy's HP, Attack, Defense, SpecialAttack and SpecialDefense are scaled by it (EnemyScaling) so that the calibration target " +
                   "clears its shape's TargetClear percent (Targets; per cell too) of the shape's generated encounters at that level. CalibratedOn '" +
                   SimOptions.CalibrationName(options.EffectiveCalibrateOn) + "' = " + CalibrationMeaning(options.EffectiveCalibrateOn) + ". Targets: " +
                   options.TargetSummary(encounters.Shapes).Replace("`", string.Empty) +
                   (options.UniformTarget ? " (uniform, --target-clear)" : " (encounter-library.json, tiered by the kind of fight: trash cleared most of the time, bosses about half)") +
                   ". Player gear: " + GearMeaning(options.Gear) +
                   ". The game reads the elemental cells (EncounterDifficultyTable: linear between calibrated levels, clamped outside them) and " +
                   "multiplies by encounter-library.json's DifficultyScale, a global producer factor (1.0 = as calibrated). Schema 1 files " +
                   "(one uniform TargetClear) still load. Regenerate with: " + Command;
        }

        private static string GearMeaning(GearProfile gear)
        {
            switch (gear)
            {
                case GearProfile.None:
                    return "none (--gear none)";
                case GearProfile.Typical:
                    return "typical (--gear typical: what a player normally wears at the level, three pieces of its band from gear-library.json; the shipping assumption, a user decision)";
                default:
                    string name = gear.ToString().ToLowerInvariant();
                    return name + " (--gear " + name + ": three pieces of the level's band from gear-library.json)";
            }
        }

        private static string CalibrationMeaning(CalibrationTarget target)
        {
            switch (target)
            {
                case CalibrationTarget.Mean:
                    return "the average of every team (a player who does not scout)";
                case CalibrationTarget.Heuristic:
                    return "the team a scouting player's heuristic pick fields per encounter";
                default:
                    return "the team a scouting player's bond-aware heuristic pick fields per encounter";
            }
        }

        /// <summary>Round-trip text for a double (shortest form that parses back to the same value).</summary>
        private static string Number(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string Quote(string text)
        {
            return JsonSerializer.Serialize(text, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
    }
}
