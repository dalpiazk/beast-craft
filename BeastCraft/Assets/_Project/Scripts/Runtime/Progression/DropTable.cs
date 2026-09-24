using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The runtime drop table <see cref="LootRoller"/> rolls against: level bands, each holding one
    /// <see cref="DropCell"/> per encounter shape, the pity thresholds per material tier, and every
    /// entry's material tier resolved. Built from <see cref="DropTableData"/> by
    /// <see cref="DropTableBuilder"/>; immutable once built. See the battle-system design doc,
    /// "Material economy".
    /// </summary>
    public sealed class DropTable
    {
        private readonly List<DropBand> _bands;
        private readonly List<string> _shapes;
        private readonly Dictionary<int, int> _pity;

        internal DropTable(List<string> shapes, List<DropBand> bands, Dictionary<int, int> pity)
        {
            _shapes = shapes;
            _bands = bands;
            _pity = pity;
        }

        /// <summary>Every shape id, in authored order.</summary>
        public IReadOnlyList<string> Shapes
        {
            get { return _shapes; }
        }

        /// <summary>The level bands, ascending.</summary>
        public IReadOnlyList<DropBand> Bands
        {
            get { return _bands; }
        }

        /// <summary>
        /// The index of the band holding <paramref name="level"/>. A level below the first band reads
        /// as the first band and one above the last as the last. -1 only for a table with no bands.
        /// </summary>
        public int BandIndexForLevel(int level)
        {
            if (_bands.Count == 0)
            {
                return -1;
            }

            for (int i = 0; i < _bands.Count; i++)
            {
                if (level <= _bands[i].MaxLevel)
                {
                    return i;
                }
            }

            return _bands.Count - 1;
        }

        /// <summary>The band holding <paramref name="level"/> (clamped, see <see cref="BandIndexForLevel"/>), or null with no bands.</summary>
        public DropBand BandForLevel(int level)
        {
            int index = BandIndexForLevel(level);
            return index < 0 ? null : _bands[index];
        }

        /// <summary>The cell for <paramref name="shape"/> at <paramref name="level"/>, or null for an unknown shape.</summary>
        public DropCell GetCell(string shape, int level)
        {
            DropBand band = BandForLevel(level);
            return band == null ? null : band.GetCell(shape);
        }

        /// <summary>
        /// Consecutive dry clears tolerated before <paramref name="tier"/> is forced (see
        /// <see cref="PityData"/>), or 0 when the tier has no pity.
        /// </summary>
        public int PityThreshold(int tier)
        {
            return _pity.TryGetValue(tier, out int threshold) ? threshold : 0;
        }
    }

    /// <summary>One level band of a <see cref="DropTable"/>.</summary>
    public sealed class DropBand
    {
        private readonly Dictionary<string, DropCell> _cells;

        internal DropBand(int minLevel, int maxLevel, string firstClearMaterialId, int firstClearTier, int firstClearQuantity, Dictionary<string, DropCell> cells)
        {
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            FirstClearMaterialId = firstClearMaterialId;
            FirstClearTier = firstClearTier;
            FirstClearQuantity = firstClearQuantity;
            _cells = cells;
        }

        /// <summary>Lowest level in the band (inclusive). Also the band's key in <see cref="MaterialInventory.ClearedCells"/>.</summary>
        public int MinLevel { get; }

        /// <summary>Highest level in the band (inclusive).</summary>
        public int MaxLevel { get; }

        /// <summary>The material the first clear of each of this band's cells grants.</summary>
        public string FirstClearMaterialId { get; }

        /// <summary>That material's tier (0 when unresolved).</summary>
        public int FirstClearTier { get; }

        /// <summary>How many the first clear grants.</summary>
        public int FirstClearQuantity { get; }

        /// <summary>The cell for <paramref name="shape"/>, or null for a shape this band does not have.</summary>
        public DropCell GetCell(string shape)
        {
            return shape != null && _cells.TryGetValue(shape, out DropCell cell) ? cell : null;
        }
    }

    /// <summary>What one shape drops within one band: independent entries, in authored order.</summary>
    public sealed class DropCell
    {
        private readonly List<DropEntry> _entries;

        internal DropCell(string shape, List<DropEntry> entries)
        {
            Shape = shape;
            _entries = entries;
        }

        /// <summary>The shape id.</summary>
        public string Shape { get; }

        /// <summary>The independent rolls, in the order <see cref="LootRoller"/> draws them.</summary>
        public IReadOnlyList<DropEntry> Entries
        {
            get { return _entries; }
        }
    }

    /// <summary>One independent drop roll, with its material's tier resolved.</summary>
    public sealed class DropEntry
    {
        internal DropEntry(string materialId, int tier, int chance, int minQty, int maxQty)
        {
            MaterialId = materialId;
            Tier = tier;
            Chance = chance < 0 ? 0 : chance > 100 ? 100 : chance;
            MinQty = minQty < 1 ? 1 : minQty;
            MaxQty = maxQty < MinQty ? MinQty : maxQty;
        }

        public string MaterialId { get; }

        /// <summary>The material's tier (0 when the builder could not resolve it; such an entry has no pity).</summary>
        public int Tier { get; }

        /// <summary>Percent chance, clamped to [0, 100].</summary>
        public int Chance { get; }

        public int MinQty { get; }

        public int MaxQty { get; }

        /// <summary>The mean quantity this entry adds per clear, ignoring pity: <c>Chance / 100 * (MinQty + MaxQty) / 2</c>.</summary>
        public double ExpectedQuantity
        {
            get { return Chance / 100.0 * (MinQty + MaxQty) / 2.0; }
        }
    }

    /// <summary>
    /// Builds a <see cref="DropTable"/> from <see cref="DropTableData"/>. The Editor-imported
    /// <see cref="DropTableSO"/> and the balance simulator both go through it, so they cannot read
    /// a field differently. Expects data that passed <see cref="DropTableValidator"/>; it does not
    /// re-check, but never throws on bad data either (null entries are skipped).
    /// </summary>
    public static class DropTableBuilder
    {
        /// <summary>
        /// Builds the table. <paramref name="tierOf"/> resolves a material id to its tier (typically
        /// from the skill library's materials, see <see cref="TierLookup"/>); a null lookup or an
        /// unresolved id gives tier 0 (no pity). Returns null for null data.
        /// </summary>
        public static DropTable Build(DropTableData data, Func<string, int> tierOf)
        {
            if (data == null)
            {
                return null;
            }

            Func<string, int> tier = tierOf ?? (_ => 0);

            List<string> shapes = new List<string>();
            if (data.Shapes != null)
            {
                foreach (string shape in data.Shapes)
                {
                    if (!string.IsNullOrEmpty(shape) && !shapes.Contains(shape))
                    {
                        shapes.Add(shape);
                    }
                }
            }

            Dictionary<int, int> pity = new Dictionary<int, int>();
            if (data.Pity != null)
            {
                foreach (PityData p in data.Pity)
                {
                    if (p != null && p.Threshold > 0)
                    {
                        pity[p.Tier] = p.Threshold;
                    }
                }
            }

            List<DropBand> bands = new List<DropBand>();
            if (data.Bands != null)
            {
                foreach (LevelBandData band in data.Bands)
                {
                    if (band == null)
                    {
                        continue;
                    }

                    Dictionary<string, DropCell> cells = new Dictionary<string, DropCell>(StringComparer.Ordinal);
                    if (band.Cells != null)
                    {
                        foreach (DropCellData cell in band.Cells)
                        {
                            if (cell == null || string.IsNullOrEmpty(cell.Shape) || cells.ContainsKey(cell.Shape))
                            {
                                continue;
                            }

                            List<DropEntry> entries = new List<DropEntry>();
                            if (cell.Drops != null)
                            {
                                foreach (DropEntryData d in cell.Drops)
                                {
                                    if (d != null && !string.IsNullOrEmpty(d.MaterialId))
                                    {
                                        entries.Add(new DropEntry(d.MaterialId, tier(d.MaterialId), d.Chance, d.MinQty, d.MaxQty));
                                    }
                                }
                            }

                            cells.Add(cell.Shape, new DropCell(cell.Shape, entries));
                        }
                    }

                    string bonus = band.FirstClearMaterialId;
                    bands.Add(new DropBand(band.MinLevel, band.MaxLevel, bonus, string.IsNullOrEmpty(bonus) ? 0 : tier(bonus),
                                           band.FirstClearQuantity < 1 ? 1 : band.FirstClearQuantity, cells));
                }
            }

            return new DropTable(shapes, bands, pity);
        }

        /// <summary>
        /// A material-id-to-tier lookup over the skill library's materials: unknown ids and a null
        /// array give tier 0.
        /// </summary>
        public static Func<string, int> TierLookup(Skills.SkillMaterialData[] materials)
        {
            Dictionary<string, int> tiers = new Dictionary<string, int>(StringComparer.Ordinal);
            if (materials != null)
            {
                foreach (Skills.SkillMaterialData m in materials)
                {
                    if (m != null && !string.IsNullOrEmpty(m.MaterialId))
                    {
                        tiers[m.MaterialId] = m.Tier;
                    }
                }
            }

            return id => id != null && tiers.TryGetValue(id, out int t) ? t : 0;
        }
    }
}
