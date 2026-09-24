using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The plain-data shape of <c>Data/Skills/drop-tables.json</c>: which skill-training materials a
    /// cleared battle drops, keyed by encounter shape and level band, plus the pity thresholds and
    /// the first-clear bonus. See the battle-system design doc, "Material economy".
    /// <para>
    /// Like <see cref="Skills.SkillLibraryData"/>, these are plain serializable classes with public
    /// fields and no Unity-object references, so the same types read the file inside Unity
    /// (<c>JsonUtility</c>, via the Editor importer into a <see cref="DropTableSO"/>) and outside it
    /// (<c>System.Text.Json</c> with <c>IncludeFields = true</c>, e.g. the balance simulator's
    /// pacing mode). JSON keys are the field names exactly. Material ids name
    /// <see cref="Skills.SkillMaterialData.MaterialId"/>s in <c>skill-library.json</c>.
    /// </para>
    /// <para>
    /// <see cref="DropTableValidator"/> checks it and <see cref="DropTableBuilder"/> turns it into
    /// the runtime <see cref="DropTable"/> that <see cref="LootRoller"/> rolls against.
    /// </para>
    /// </summary>
    [Serializable]
    public class DropTableData
    {
        /// <summary>Path of the drop-table file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Skills/drop-tables.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>
        /// Every encounter shape id (lowercase snake_case, e.g. <c>solo</c>, <c>elite</c>,
        /// <c>squad</c>, <c>horde</c>). Every band has exactly one cell per shape.
        /// </summary>
        public string[] Shapes = new string[0];

        /// <summary>Pity thresholds per material tier. A tier with no entry has no pity.</summary>
        public PityData[] Pity = new PityData[0];

        /// <summary>
        /// Level bands in ascending order, contiguous and covering levels 1 to
        /// <see cref="DropTableValidator.MaxEncounterLevel"/>.
        /// </summary>
        public LevelBandData[] Bands = new LevelBandData[0];
    }

    /// <summary>
    /// Bad-luck protection for one material tier: after <see cref="Threshold"/> consecutive clears
    /// of a shape whose cell can drop this tier without dropping it, the next clear of that shape
    /// forces it (see <see cref="LootRoller"/>).
    /// </summary>
    [Serializable]
    public class PityData
    {
        /// <summary>The material tier this counter tracks, 1 upward.</summary>
        public int Tier = 1;

        /// <summary>Consecutive dry clears tolerated; the clear after that many forces the tier. At least 1.</summary>
        public int Threshold = 10;
    }

    /// <summary>One level band: the cells for every shape, and the band's first-clear bonus.</summary>
    [Serializable]
    public class LevelBandData
    {
        /// <summary>Lowest encounter level in the band (inclusive).</summary>
        public int MinLevel = 1;

        /// <summary>Highest encounter level in the band (inclusive).</summary>
        public int MaxLevel = 20;

        /// <summary>
        /// The material granted, once per (shape, band), on the first clear of that cell: the
        /// band's headline tier. Recorded in <see cref="MaterialInventory.ClearedCells"/>.
        /// </summary>
        public string FirstClearMaterialId;

        /// <summary>How many of <see cref="FirstClearMaterialId"/> the first clear grants. At least 1.</summary>
        public int FirstClearQuantity = 1;

        /// <summary>One cell per <see cref="DropTableData.Shapes"/> entry.</summary>
        public DropCellData[] Cells = new DropCellData[0];
    }

    /// <summary>What one shape drops within one band.</summary>
    [Serializable]
    public class DropCellData
    {
        /// <summary>A <see cref="DropTableData.Shapes"/> id.</summary>
        public string Shape;

        /// <summary>
        /// Independent rolls, in order: each entry drops or not on its own <see cref="DropEntryData.Chance"/>.
        /// A material appears at most once per cell. May be empty (the cell drops nothing but
        /// first-clear bonuses).
        /// </summary>
        public DropEntryData[] Drops = new DropEntryData[0];
    }

    /// <summary>One independent drop roll.</summary>
    [Serializable]
    public class DropEntryData
    {
        /// <summary>A material id from <c>skill-library.json</c>.</summary>
        public string MaterialId;

        /// <summary>Percent chance, 1 to 100, that this entry drops on a clear.</summary>
        public int Chance = 100;

        /// <summary>Fewest dropped when it drops. At least 1.</summary>
        public int MinQty = 1;

        /// <summary>Most dropped when it drops (uniform between the two). At least <see cref="MinQty"/>.</summary>
        public int MaxQty = 1;
    }
}
