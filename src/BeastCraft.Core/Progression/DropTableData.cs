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

        /// <summary>
        /// The newest <see cref="SchemaVersion"/> this code reads: 2 adds the economy sections
        /// (<see cref="Gold"/>, <see cref="GearDrops"/>, <see cref="CosmeticDrops"/>). A version-1 file
        /// (no economy) still reads; see <see cref="MinSchemaVersion"/>.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>The oldest <see cref="SchemaVersion"/> this code reads (version 1: materials only).</summary>
        public const int MinSchemaVersion = 1;

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

        /// <summary>
        /// Schema 2: the gold a clear pays (<see cref="GoldData"/>). All zero (the default, and every
        /// version-1 file) pays no gold. See the economy design doc, <c>docs/design/economy-and-shop.md</c>.
        /// </summary>
        public GoldData Gold = new GoldData();

        /// <summary>
        /// Schema 2: gear drop chances by shape and rarity. Each entry rolls on its own; a hit grants
        /// one gear piece of that rarity from the encounter level's gear band (the gear library's
        /// drop pool), on its own seed stream. Empty = no gear drops.
        /// </summary>
        public GearDropData[] GearDrops = new GearDropData[0];

        /// <summary>
        /// Schema 2: the low chance per clear, by shape, of a random cosmetic look (the cosmetic
        /// library's drop pool, one not yet unlocked), on its own seed stream. Empty = none.
        /// </summary>
        public CosmeticDropData[] CosmeticDrops = new CosmeticDropData[0];
    }

    /// <summary>
    /// The gold a clear pays: <c>round((Base + PerLevel x level) x shape multiplier x
    /// U[1 - VariancePct%, 1 + VariancePct%])</c>, plus <see cref="FirstClearBonus"/> on the cell's
    /// first clear, then the caller's reward modifiers (campaign elites, gates and bosses).
    /// </summary>
    [Serializable]
    public class GoldData
    {
        /// <summary>Gold at level 0 (the curve's intercept). 0 with <see cref="PerLevel"/> 0 = no gold.</summary>
        public int Base;

        /// <summary>Gold added per encounter level.</summary>
        public int PerLevel;

        /// <summary>Uniform variance, percent either side (0-50).</summary>
        public int VariancePct;

        /// <summary>Per-shape multipliers; a shape not listed pays x1.</summary>
        public ShapeMultiplierData[] ShapeMultipliers = new ShapeMultiplierData[0];

        /// <summary>Gold added on the first clear of a (shape, band) cell (the material first-clear bonus's gold twin).</summary>
        public int FirstClearBonus;
    }

    /// <summary>A shape's gold multiplier.</summary>
    [Serializable]
    public class ShapeMultiplierData
    {
        /// <summary>A <see cref="DropTableData.Shapes"/> id.</summary>
        public string Shape;

        /// <summary>The multiplier, above 0 and at most 10.</summary>
        public float Multiplier = 1f;
    }

    /// <summary>One gear drop roll: a <see cref="Shape"/> clear drops a piece of <see cref="Rarity"/> with <see cref="ChancePerMille"/>.</summary>
    [Serializable]
    public class GearDropData
    {
        /// <summary>A <see cref="DropTableData.Shapes"/> id.</summary>
        public string Shape;

        /// <summary>Gear rarity, 0 (common) to 2 (epic).</summary>
        public int Rarity;

        /// <summary>Chance per clear in thousandths, 1 to 1000.</summary>
        public int ChancePerMille;
    }

    /// <summary>The chance per clear of a <see cref="Shape"/> of a random cosmetic look.</summary>
    [Serializable]
    public class CosmeticDropData
    {
        /// <summary>A <see cref="DropTableData.Shapes"/> id.</summary>
        public string Shape;

        /// <summary>Chance per clear in thousandths, 1 to 1000.</summary>
        public int ChancePerMille;
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
