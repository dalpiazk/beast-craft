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
        /// Structural checks for a difficulty file against the encounter library: the schema version,
        /// cells with a known kit mode, a known shape, a level of 1-100 and a positive finite
        /// multiplier, no repeated (kit mode, shape, level), and at least one elemental cell for every
        /// shape of <paramref name="library"/> (a null library skips that).
        /// </summary>
        public static List<string> Validate(EncounterDifficultyData data, EncounterLibraryData library)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Encounter difficulty is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != EncounterDifficultyData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + EncounterDifficultyData.CurrentSchemaVersion + ".");
            }

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
    }
}
