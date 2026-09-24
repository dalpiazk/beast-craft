using System;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The plain-data shape of <c>Data/Encounters/encounter-library.json</c>: how PvE encounters are
    /// put together from the enemy library. Two kinds of encounter:
    /// <list type="bullet">
    /// <item><b>Generated</b> (<see cref="Shapes"/>): an encounter shape is a recipe —
    /// variants of slots, each drawing a count and then each unit's type from a list — kept only
    /// inside the shape's threat budget and with enough distinct types. <c>EncounterGenerator</c>
    /// draws lineups from it, with an element scheme drawn by <see cref="SchemeWeights"/>.</item>
    /// <item><b>Authored</b> (<see cref="Templates"/>): a fixed lineup, for set pieces. Empty until
    /// the producer authors some.</item>
    /// </list>
    /// Shape ids are the drop tables' shape ids (<c>drop-tables.json</c>): a cleared encounter pays
    /// out from its shape's cell. How a map node picks a shape and a level is not decided here.
    /// <para>
    /// JsonUtility-compatible: arrays (never dictionaries), no nullable fields; enums are member
    /// names checked by <see cref="EncounterLibraryValidator"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class EncounterLibraryData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Encounters/encounter-library.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>
        /// Campaign-wide factor on every calibrated difficulty multiplier
        /// (<c>EncounterDifficultyTable</c>). 1 fields the calibrated difficulty as is, i.e. each
        /// shape at its own <see cref="EncounterShapeData.TargetClear"/> for a scouting,
        /// counter-picking player; a global producer knob on top of the tiered targets.
        /// </summary>
        public double DifficultyScale = 1.0;

        /// <summary>
        /// How often a generated encounter gets each <see cref="ElementScheme"/>, in draw order.
        /// Relative weights (need not sum to 100); a scheme with no entry is never drawn.
        /// </summary>
        public SchemeWeightData[] SchemeWeights = new SchemeWeightData[0];

        /// <summary>The generated encounter shapes.</summary>
        public EncounterShapeData[] Shapes = new EncounterShapeData[0];

        /// <summary>Authored fixed encounters.</summary>
        public EncounterTemplateData[] Templates = new EncounterTemplateData[0];
    }

    /// <summary>One element scheme's draw weight.</summary>
    [Serializable]
    public class SchemeWeightData
    {
        /// <summary>An <see cref="ElementScheme"/> name.</summary>
        public string Scheme;

        /// <summary>Relative weight, 0 or more.</summary>
        public int Weight;
    }

    /// <summary>An encounter shape: which enemy types may appear, how many, and the threat budget.</summary>
    [Serializable]
    public class EncounterShapeData
    {
        /// <summary>Stable lowercase snake_case id; the drop tables' shape id.</summary>
        public string ShapeId;

        public string DisplayName;

        public string Description;

        /// <summary>An <c>ArenaSize</c> name.</summary>
        public string Arena;

        /// <summary>Least total <see cref="EnemyData.Threat"/> a lineup may have (inclusive).</summary>
        public double ThreatMin;

        /// <summary>Most total <see cref="EnemyData.Threat"/> a lineup may have (inclusive).</summary>
        public double ThreatMax;

        /// <summary>A lineup needs at least this many distinct enemy types (the "mix" rule).</summary>
        public int MinDistinctTypes = 1;

        /// <summary>
        /// The clear rate, in percent (strictly between 0 and 100), this shape's difficulty is
        /// calibrated to: the balance simulator aims the team a scouting player's bond-aware pick
        /// fields at it (<c>--write-difficulty</c>, <see cref="EncounterDifficultyData"/>). Tiered by
        /// the kind of fight: trash (squad, horde) is meant to be cleared most of the time, a boss
        /// about half. A change here needs the difficulty table re-written.
        /// </summary>
        public double TargetClear;

        /// <summary>Alternative recipes; the generator picks one per draw, weighted by <see cref="EncounterVariantData.Weight"/>.</summary>
        public EncounterVariantData[] Variants = new EncounterVariantData[0];
    }

    /// <summary>One recipe of a shape.</summary>
    [Serializable]
    public class EncounterVariantData
    {
        /// <summary>Free text for tooling.</summary>
        public string Label;

        /// <summary>Relative weight, 1 or more.</summary>
        public int Weight = 1;

        public EncounterSlotData[] Slots = new EncounterSlotData[0];
    }

    /// <summary>Draw a count in [Min, Max], then each unit's type uniformly (with replacement) from Types.</summary>
    [Serializable]
    public class EncounterSlotData
    {
        /// <summary><see cref="EnemyData.EnemyId"/>s.</summary>
        public string[] Types = new string[0];

        public int Min;

        public int Max;
    }

    /// <summary>An authored fixed encounter.</summary>
    [Serializable]
    public class EncounterTemplateData
    {
        /// <summary>Stable lowercase snake_case id.</summary>
        public string EncounterId;

        public string DisplayName;

        public string Description;

        /// <summary>
        /// The <see cref="EncounterShapeData.ShapeId"/> it counts as: its difficulty row and its
        /// drop-table cell.
        /// </summary>
        public string ShapeId;

        /// <summary>An <c>ArenaSize</c> name.</summary>
        public string Arena;

        /// <summary>Enemy groups, placed front to back in this order.</summary>
        public EncounterGroupData[] Groups = new EncounterGroupData[0];

        /// <summary>
        /// A difficulty multiplier that replaces the shape's calibrated one (and
        /// <see cref="EncounterLibraryData.DifficultyScale"/>). 0, the default, means none.
        /// </summary>
        public double DifficultyOverride;

        /// <summary>
        /// True while the template's text and design are placeholders pending producer review.
        /// Never shown to the player: draft status lives here, not in <see cref="Description"/>
        /// (the validator rejects a "[DRAFT]" marker in the player-facing text).
        /// </summary>
        public bool Draft;
    }

    /// <summary>One group of a template: an enemy type, how many, and their elements.</summary>
    [Serializable]
    public class EncounterGroupData
    {
        /// <summary>An <see cref="EnemyData.EnemyId"/>.</summary>
        public string EnemyId;

        /// <summary>How many, at least 1.</summary>
        public int Count = 1;

        /// <summary><c>Element</c> names, cycled over the group (unit i gets Elements[i % length]); empty = None.</summary>
        public string[] Elements = new string[0];
    }
}
