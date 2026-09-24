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
            json.Append("  \"_readme\": ").Append(Quote(Readme(options))).Append(",\n");
            json.Append("  \"SchemaVersion\": ").Append(EncounterDifficultyData.CurrentSchemaVersion).Append(",\n");
            json.Append("  \"Seed\": ").Append(options.Seed.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"TargetClear\": ").Append(Number(options.TargetClearRate)).Append(",\n");
            json.Append("  \"CalibratedOn\": ").Append(Quote(SimOptions.CalibrationName(options.EffectiveCalibrateOn))).Append(",\n");
            json.Append("  \"Compositions\": ").Append(options.Compositions.ToString(CultureInfo.InvariantCulture)).Append(",\n");
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
                                         level.ToString(CultureInfo.InvariantCulture) + ", \"Multiplier\": " + Number(cell.Multiplier) + " }");
                            }
                        }
                    }
                }
            }

            json.Append(string.Join(",\n", rows)).Append("\n  ]\n}\n");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private static string Readme(SimOptions options)
        {
            return "WRITTEN BY Tooling/BalanceSim --write-difficulty; never hand-edit (re-run the simulator after changing the roster, skills, " +
                   "enemies or shapes). One calibrated difficulty multiplier per (kit mode, encounter shape, level): every enemy's HP, Attack, " +
                   "Defense, SpecialAttack and SpecialDefense are scaled by it (EnemyScaling) so that the calibration target clears TargetClear percent " +
                   "of the shape's generated encounters at that level. CalibratedOn '" + SimOptions.CalibrationName(options.EffectiveCalibrateOn) +
                   "' = " + CalibrationMeaning(options.EffectiveCalibrateOn) + ". The game reads the elemental cells (EncounterDifficultyTable: " +
                   "linear between calibrated levels, clamped outside them) and multiplies by encounter-library.json's DifficultyScale. " +
                   "PENDING PRODUCER REVIEW: this is calibrated for " + CalibrationMeaning(options.EffectiveCalibrateOn) + " to clear " +
                   Number(options.TargetClearRate) + "%; the campaign's intended difficulty (and DifficultyScale) is not decided. " +
                   "Regenerate with: dotnet run --project Tooling/BalanceSim -c Release -- " +
                   "--panel 16x4 --out docs/balance/tuned-report.md --write-difficulty BeastCraft/Assets/_Project/Data/Encounters/encounter-difficulty.json";
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
