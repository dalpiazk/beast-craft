using System;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The plain-data shape of <c>Data/Encounters/encounter-difficulty.json</c>: the difficulty
    /// multiplier the balance simulator calibrated per (kit mode, encounter shape, level). Written by
    /// the simulator (<c>--write-difficulty</c>), never by hand; <see cref="EncounterDifficultyTable"/>
    /// reads it.
    /// <para>
    /// The multiplier scales every enemy's HP, Attack, Defense, SpecialAttack and SpecialDefense
    /// (<see cref="EnemyScaling"/>) so that the calibration target — by default, the team a scouting,
    /// counter-picking player fields — clears <see cref="TargetClear"/> percent of the shape's
    /// generated encounters at that level. Whether that is the right campaign difficulty is a
    /// producer decision (see <see cref="EncounterLibraryData.DifficultyScale"/>).
    /// </para>
    /// </summary>
    [Serializable]
    public class EncounterDifficultyData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Encounters/encounter-difficulty.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The <see cref="DifficultyCellData.KitMode"/> the game reads: skills carry their elements.</summary>
        public const string ElementalMode = "elemental";

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>The simulator's base seed for the calibration run.</summary>
        public int Seed;

        /// <summary>The clear rate, in percent, the calibration aimed at.</summary>
        public double TargetClear;

        /// <summary>What was calibrated to that rate (the simulator's <c>--calibrate-on</c>: <c>bonds</c> = the bond-aware scouted pick).</summary>
        public string CalibratedOn;

        /// <summary>Generated compositions per shape the calibration fought.</summary>
        public int Compositions;

        /// <summary>One calibrated multiplier per (kit mode, shape, level).</summary>
        public DifficultyCellData[] Cells = new DifficultyCellData[0];
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
    }
}
