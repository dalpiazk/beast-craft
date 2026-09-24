using System;
using System.Collections.Generic;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The calibrated difficulty multiplier of a generated encounter, by shape and level, from
    /// <see cref="EncounterDifficultyData"/>'s <c>elemental</c> cells (the game always fights with
    /// elements). Between two calibrated levels the multiplier is interpolated linearly; below the
    /// lowest or above the highest it is clamped to that level's value. A shape with no cells reads
    /// as 1 (uncalibrated). <see cref="EncounterLibraryData.DifficultyScale"/> and a template's
    /// override are applied by the caller (<c>EncounterPlan</c>), not here.
    /// </summary>
    public sealed class EncounterDifficultyTable
    {
        /// <summary>The least encounter level a cell may be calibrated at.</summary>
        public const int MinLevel = 1;

        /// <summary>The greatest encounter level a cell may be calibrated at.</summary>
        public const int MaxLevel = 100;

        private readonly Dictionary<string, SortedList<int, double>> _byShape = new Dictionary<string, SortedList<int, double>>(StringComparer.Ordinal);

        private EncounterDifficultyTable()
        {
        }

        /// <summary>
        /// A table over <paramref name="data"/>'s elemental cells. Null data, null cells, cells of
        /// another kit mode and non-positive multipliers are skipped; when two cells share a shape and
        /// level the first wins.
        /// </summary>
        public static EncounterDifficultyTable Build(EncounterDifficultyData data)
        {
            EncounterDifficultyTable table = new EncounterDifficultyTable();
            if (data == null || data.Cells == null)
            {
                return table;
            }

            foreach (DifficultyCellData cell in data.Cells)
            {
                if (cell == null || !string.Equals(cell.KitMode, EncounterDifficultyData.ElementalMode, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(cell.Shape) || !(cell.Multiplier > 0.0))
                {
                    continue;
                }

                if (!table._byShape.TryGetValue(cell.Shape, out SortedList<int, double> levels))
                {
                    levels = new SortedList<int, double>();
                    table._byShape.Add(cell.Shape, levels);
                }

                if (!levels.ContainsKey(cell.Level))
                {
                    levels.Add(cell.Level, cell.Multiplier);
                }
            }

            return table;
        }

        /// <summary>Whether <paramref name="shapeId"/> has at least one calibrated level.</summary>
        public bool HasShape(string shapeId)
        {
            return !string.IsNullOrEmpty(shapeId) && _byShape.ContainsKey(shapeId);
        }

        /// <summary>
        /// The calibrated multiplier of <paramref name="shapeId"/> at <paramref name="level"/>:
        /// linear between the calibrated levels either side, clamped outside them; 1 for a shape
        /// with no cells.
        /// </summary>
        public double Multiplier(string shapeId, int level)
        {
            if (!HasShape(shapeId))
            {
                return 1.0;
            }

            SortedList<int, double> levels = _byShape[shapeId];
            IList<int> keys = levels.Keys;
            IList<double> values = levels.Values;

            if (level <= keys[0])
            {
                return values[0];
            }

            int last = keys.Count - 1;
            if (level >= keys[last])
            {
                return values[last];
            }

            for (int i = 1; i <= last; i++)
            {
                if (level <= keys[i])
                {
                    double t = (double)(level - keys[i - 1]) / (keys[i] - keys[i - 1]);
                    return values[i - 1] + (t * (values[i] - values[i - 1]));
                }
            }

            return values[last];
        }

        /// <summary>
        /// Structural checks for a difficulty file against the encounter library: the schema version
        /// (1 or 2), cells with a known kit mode, a known shape, a level of 1-100 and a positive finite
        /// multiplier, no repeated (kit mode, shape, level), and at least one elemental cell for every
        /// shape of <paramref name="library"/> (a null library skips that). Schema 2 also: every
        /// <see cref="EncounterDifficultyData.Targets"/> entry names a known shape once, with a target
        /// strictly between 0 and 100, and every cell's <see cref="DifficultyCellData.TargetClear"/> is
        /// its shape's target. A target that no longer matches the library is only a warning
        /// (<see cref="Warnings"/>).
        /// </summary>
        public static List<string> Validate(EncounterDifficultyData data, EncounterLibraryData library)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Encounter difficulty is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != EncounterDifficultyData.CurrentSchemaVersion && data.SchemaVersion != EncounterDifficultyData.UniformTargetSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads versions " + EncounterDifficultyData.UniformTargetSchemaVersion + " and " +
                           EncounterDifficultyData.CurrentSchemaVersion + ".");
            }

            bool perShape = data.SchemaVersion == EncounterDifficultyData.CurrentSchemaVersion;

            HashSet<string> shapes = null;
            if (library != null)
            {
                shapes = new HashSet<string>(StringComparer.Ordinal);
                foreach (EncounterShapeData shape in library.Shapes ?? new EncounterShapeData[0])
                {
                    if (shape != null && !string.IsNullOrEmpty(shape.ShapeId))
                    {
                        shapes.Add(shape.ShapeId);
                    }
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> calibrated = new HashSet<string>(StringComparer.Ordinal);
            DifficultyCellData[] cells = data.Cells ?? new DifficultyCellData[0];
            for (int i = 0; i < cells.Length; i++)
            {
                DifficultyCellData cell = cells[i];
                string at = "Cells[" + i + "]";
                if (cell == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                if (cell.KitMode != EncounterDifficultyData.ElementalMode && cell.KitMode != "neutral")
                {
                    errors.Add(at + ": KitMode '" + cell.KitMode + "' is not elemental or neutral.");
                }

                if (string.IsNullOrEmpty(cell.Shape) || (shapes != null && !shapes.Contains(cell.Shape)))
                {
                    errors.Add(at + ": Shape '" + cell.Shape + "' is not a shape in the encounter library.");
                }

                if (cell.Level < MinLevel || cell.Level > MaxLevel)
                {
                    errors.Add(at + ": Level " + cell.Level + " is outside " + MinLevel + "-" + MaxLevel + ".");
                }

                if (!(cell.Multiplier > 0.0) || double.IsInfinity(cell.Multiplier))
                {
                    errors.Add(at + ": Multiplier must be a positive number.");
                }

                if (!seen.Add(cell.KitMode + "/" + cell.Shape + "/" + cell.Level))
                {
                    errors.Add(at + ": a second cell for " + cell.KitMode + " / " + cell.Shape + " / level " + cell.Level + ".");
                }

                if (cell.KitMode == EncounterDifficultyData.ElementalMode && cell.Shape != null)
                {
                    calibrated.Add(cell.Shape);
                }

                if (perShape && cell.Shape != null && cell.TargetClear != data.TargetFor(cell.Shape))
                {
                    errors.Add(at + ": TargetClear " + Number(cell.TargetClear) + " is not its shape's target (" + Number(data.TargetFor(cell.Shape)) + ").");
                }
            }

            if (perShape)
            {
                HashSet<string> targeted = new HashSet<string>(StringComparer.Ordinal);
                DifficultyTargetData[] targets = data.Targets ?? new DifficultyTargetData[0];
                for (int i = 0; i < targets.Length; i++)
                {
                    DifficultyTargetData target = targets[i];
                    string at = "Targets[" + i + "]";
                    if (target == null)
                    {
                        errors.Add(at + " is null.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(target.Shape) || (shapes != null && !shapes.Contains(target.Shape)))
                    {
                        errors.Add(at + ": Shape '" + target.Shape + "' is not a shape in the encounter library.");
                    }
                    else if (!targeted.Add(target.Shape))
                    {
                        errors.Add(at + ": a second target for '" + target.Shape + "'.");
                    }

                    if (!(target.TargetClear > 0.0 && target.TargetClear < 100.0))
                    {
                        errors.Add(at + ": TargetClear " + Number(target.TargetClear) + " must be strictly between 0 and 100.");
                    }
                }
            }

            if (shapes != null)
            {
                foreach (string shape in shapes)
                {
                    if (!calibrated.Contains(shape))
                    {
                        errors.Add("Shape '" + shape + "' has no elemental cell; re-run the balance simulator with --write-difficulty.");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// What is legal but stale: every shape of <paramref name="library"/> whose target in the
        /// difficulty file (<see cref="EncounterDifficultyData.TargetFor"/>; the uniform one for a
        /// schema 1 file) differs from the library's <see cref="EncounterShapeData.TargetClear"/>.
        /// The table still works; it was calibrated to another clear rate. Re-run the simulator's
        /// <c>--write-difficulty</c>. Empty for a null file or library.
        /// </summary>
        public static List<string> Warnings(EncounterDifficultyData data, EncounterLibraryData library)
        {
            List<string> warnings = new List<string>();
            if (data == null || library == null)
            {
                return warnings;
            }

            foreach (EncounterShapeData shape in library.Shapes ?? new EncounterShapeData[0])
            {
                if (shape == null || string.IsNullOrEmpty(shape.ShapeId))
                {
                    continue;
                }

                double calibrated = data.TargetFor(shape.ShapeId);
                if (Math.Abs(calibrated - shape.TargetClear) > 1e-9)
                {
                    warnings.Add("Shape '" + shape.ShapeId + "' was calibrated to a " + Number(calibrated) + "% clear rate but the encounter library now targets " +
                                 Number(shape.TargetClear) + "%; re-run the balance simulator with --write-difficulty.");
                }
            }

            return warnings;
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
