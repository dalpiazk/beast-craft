using System;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The plain-data shape of <c>data/Encounters/encounter-difficulty.json</c>: the difficulty
    /// multiplier the balance simulator calibrated per (kit mode, encounter shape, level). Written by
    /// the simulator (<c>--write-difficulty</c>), never by hand; <see cref="EncounterDifficultyTable"/>
    /// reads it.
    /// <para>
    /// The multiplier scales every enemy's HP, Attack, Defense, SpecialAttack and SpecialDefense
    /// (<see cref="EnemyScaling"/>) so that the calibration target — by default, the team a scouting,
    /// counter-picking player fields — clears the shape's target percent of its generated
    /// encounters at that level (<see cref="TargetFor"/>: schema 2 carries one per shape, from
    /// <see cref="EncounterShapeData.TargetClear"/>; schema 1 had one for every shape).
    /// <see cref="EncounterLibraryData.DifficultyScale"/> stays a global producer factor on top.
    /// </para>
    /// </summary>
    [Serializable]
    public class EncounterDifficultyData
    {
        /// <summary>Path of the file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Encounters/encounter-difficulty.json";

        /// <summary>
        /// The <see cref="SchemaVersion"/> the simulator writes. 2 added per-shape targets
        /// (<see cref="Targets"/>, <see cref="DifficultyCellData.TargetClear"/>).
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>The older schema, still read: one <see cref="TargetClear"/> for every shape.</summary>
        public const int UniformTargetSchemaVersion = 1;

        /// <summary>The <see cref="DifficultyCellData.KitMode"/> the game reads: skills carry their elements.</summary>
        public const string ElementalMode = "elemental";

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>The simulator's base seed for the calibration run.</summary>
        public int Seed;

        /// <summary>
        /// Schema 1: the clear rate, in percent, every shape was calibrated to. Schema 2 leaves it 0
        /// and carries <see cref="Targets"/> instead.
        /// </summary>
        public double TargetClear;

        /// <summary>Schema 2: each shape's target clear rate, in percent (as the encounter library set it when the table was written).</summary>
        public DifficultyTargetData[] Targets = new DifficultyTargetData[0];

        /// <summary>What was calibrated to that rate (the simulator's <c>--calibrate-on</c>: <c>bonds</c> = the bond-aware scouted pick).</summary>
        public string CalibratedOn;

        /// <summary>Generated compositions per shape the calibration fought.</summary>
        public int Compositions;

        /// <summary>One calibrated multiplier per (kit mode, shape, level).</summary>
        public DifficultyCellData[] Cells = new DifficultyCellData[0];

        /// <summary>
        /// The clear rate, in percent, <paramref name="shapeId"/> was calibrated to: its
        /// <see cref="Targets"/> entry (schema 2), else the uniform <see cref="TargetClear"/> (schema 1,
        /// or a shape a schema 2 file does not list). 0 when neither is set.
        /// </summary>
        public double TargetFor(string shapeId)
        {
            if (Targets != null)
            {
                foreach (DifficultyTargetData target in Targets)
                {
                    if (target != null && string.Equals(target.Shape, shapeId, StringComparison.Ordinal))
                    {
                        return target.TargetClear;
                    }
                }
            }

            return TargetClear;
        }
    }

    /// <summary>Schema 2: one shape's target clear rate.</summary>
    [Serializable]
    public class DifficultyTargetData
    {
        /// <summary>An <see cref="EncounterShapeData.ShapeId"/>.</summary>
        public string Shape;

        /// <summary>Percent, strictly between 0 and 100.</summary>
        public double TargetClear;
    }

    /// <summary>One calibrated multiplier.</summary>
    [Serializable]
    public class DifficultyCellData
    {
        /// <summary><c>elemental</c> (the game) or <c>neutral</c> (every skill elementless; for comparison only).</summary>
        public string KitMode;

        /// <summary>An <see cref="EncounterShapeData.ShapeId"/>.</summary>
        public string Shape;

        /// <summary>The encounter level (1-100) the multiplier was calibrated at.</summary>
        public int Level;

        /// <summary>The stat multiplier; above 0.</summary>
        public double Multiplier;

        /// <summary>Schema 2: the clear rate, in percent, this cell was calibrated to (its shape's target). 0 in schema 1.</summary>
        public double TargetClear;
    }
}
