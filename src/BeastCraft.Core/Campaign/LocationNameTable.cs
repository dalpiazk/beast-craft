using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// <see cref="LocationNameTableData"/> built for the runtime: resolves a map node's
    /// <see cref="MapNode.LabelKey"/> (<c>"{regionId}/{kind}/{variant}"</c>) to the location's display
    /// name. A key the table does not cover (unknown region, a kind without names, a variant past the
    /// list, an empty name) falls back to the kind's generic name (<see cref="FallbackName"/>), so a
    /// map always has something to print; a key that does not parse resolves to "". Expects data
    /// that passed <see cref="LocationNameTableValidator"/>; null entries and repeated regions are
    /// skipped (the first wins). Build it once with <see cref="Build"/> (the
    /// <see cref="RegionLibrarySO"/> does) and share it.
    /// </summary>
    public sealed class LocationNameTable
    {
        private readonly Dictionary<string, LocationNameSetData> _sets = new Dictionary<string, LocationNameSetData>(StringComparer.Ordinal);

        private LocationNameTable(LocationNameTableData data)
        {
            Data = data;
        }

        /// <summary>The authored data.</summary>
        public LocationNameTableData Data { get; }

        /// <summary>The table over <paramref name="data"/> (null reads as empty: every key falls back).</summary>
        public static LocationNameTable Build(LocationNameTableData data)
        {
            LocationNameTable table = new LocationNameTable(data ?? new LocationNameTableData());
            foreach (LocationNameSetData set in table.Data.Regions ?? new LocationNameSetData[0])
            {
                if (set != null && !string.IsNullOrEmpty(set.RegionId) && !table._sets.ContainsKey(set.RegionId))
                {
                    table._sets.Add(set.RegionId, set);
                }
            }

            return table;
        }

        /// <summary>
        /// The display name <paramref name="labelKey"/> stands for: the authored name, else the
        /// kind's <see cref="FallbackName"/>; "" for a null, empty or malformed key.
        /// </summary>
        public string Resolve(string labelKey)
        {
            if (!TryParseLabelKey(labelKey, out string regionId, out LocationKind kind, out int variant))
            {
                return string.Empty;
            }

            if (_sets.TryGetValue(regionId, out LocationNameSetData set))
            {
                string[] names = set.NamesFor(kind);
                if (names != null && variant < names.Length && !string.IsNullOrEmpty(names[variant]))
                {
                    return names[variant];
                }
            }

            return FallbackName(kind);
        }

        /// <summary>
        /// The display name of <paramref name="node"/>: its <see cref="MapNode.LabelKey"/> resolved, or,
        /// when the node is not placed yet or its key does not parse, its <see cref="MapNode.Kind"/>'s
        /// <see cref="FallbackName"/>. "" for a null node.
        /// </summary>
        public string Resolve(MapNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            string name = Resolve(node.LabelKey);
            return name.Length > 0 ? name : FallbackName(node.Kind);
        }

        /// <summary>The generic name of a kind, used when the table has no name for a key: Wilds, Den, Camp, Trading Post, Pass or Lair.</summary>
        public static string FallbackName(LocationKind kind)
        {
            switch (kind)
            {
                case LocationKind.Den:
                    return "Den";
                case LocationKind.Camp:
                    return "Camp";
                case LocationKind.TradingPost:
                    return "Trading Post";
                case LocationKind.Pass:
                    return "Pass";
                case LocationKind.Lair:
                    return "Lair";
                default:
                    return "Wilds";
            }
        }

        /// <summary>
        /// Splits a <see cref="MapNode.LabelKey"/>: exactly three '/'-separated parts, a non-empty
        /// region id, a <see cref="LocationKinds.Key"/> and a non-negative decimal variant.
        /// </summary>
        public static bool TryParseLabelKey(string labelKey, out string regionId, out LocationKind kind, out int variant)
        {
            regionId = string.Empty;
            kind = LocationKind.Wilds;
            variant = 0;
            if (string.IsNullOrEmpty(labelKey))
            {
                return false;
            }

            string[] parts = labelKey.Split('/');
            if (parts.Length != 3 || parts[0].Length == 0 || !LocationKinds.TryParseKey(parts[1], out kind) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out variant))
            {
                kind = LocationKind.Wilds;
                variant = 0;
                return false;
            }

            regionId = parts[0];
            return true;
        }
    }
}
