using System;
using System.Collections.Generic;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// Structural checks for <see cref="LocationNameTableData"/>: the rules
    /// <c>location-names.json</c> must satisfy to import at all. Non-throwing; every problem is one
    /// message.
    /// <list type="bullet">
    /// <item>The schema version; every entry has a region id, no region appears twice.</item>
    /// <item>Every region has exactly <see cref="NodeMapGenerator.LabelVariants"/> names for each
    /// <see cref="LocationKind"/>, every <see cref="MapNode.LabelKey"/> the generator can draw.</item>
    /// <item>Names are non-empty, without leading or trailing whitespace, at most
    /// <see cref="MaxNameLength"/> characters, <see cref="MinNameWords"/>-<see cref="MaxNameWords"/>
    /// words, and unique within their region (ignoring case).</item>
    /// <item>With the region library: every region id is a region, and every region has names.</item>
    /// </list>
    /// Tone and naming style (docs/design/content-bible.md) are review, not this.
    /// </summary>
    public static class LocationNameTableValidator
    {
        /// <summary>The longest name a map label must fit.</summary>
        public const int MaxNameLength = 24;

        /// <summary>The fewest words a name may have.</summary>
        public const int MinNameWords = 1;

        /// <summary>The most words a name may have (the content bible's 1-3 word names).</summary>
        public const int MaxNameWords = 3;

        private static readonly LocationKind[] Kinds =
        {
            LocationKind.Wilds, LocationKind.Den, LocationKind.Camp, LocationKind.TradingPost, LocationKind.Pass, LocationKind.Lair
        };

        /// <summary>Every problem, without the region cross-checks.</summary>
        public static List<string> Validate(LocationNameTableData data)
        {
            return Validate(data, null);
        }

        /// <summary>Every problem found; empty means importable. With <paramref name="regions"/>, region ids are checked both ways.</summary>
        public static List<string> Validate(LocationNameTableData data, RegionLibraryData regions)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Location name table is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != LocationNameTableData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + LocationNameTableData.CurrentSchemaVersion + ".");
            }

            HashSet<string> known = null;
            if (regions != null)
            {
                known = new HashSet<string>(StringComparer.Ordinal);
                foreach (RegionData region in regions.Regions ?? new RegionData[0])
                {
                    if (region != null && !string.IsNullOrEmpty(region.RegionId))
                    {
                        known.Add(region.RegionId);
                    }
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            LocationNameSetData[] sets = data.Regions ?? new LocationNameSetData[0];
            for (int i = 0; i < sets.Length; i++)
            {
                LocationNameSetData set = sets[i];
                string where = "Regions[" + i + "]";
                if (set == null)
                {
                    errors.Add(where + " is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(set.RegionId))
                {
                    errors.Add(where + ": RegionId is empty.");
                }
                else
                {
                    where = "Region '" + set.RegionId + "'";
                    if (!seen.Add(set.RegionId))
                    {
                        errors.Add(where + " appears more than once.");
                    }

                    if (known != null && !known.Contains(set.RegionId))
                    {
                        errors.Add(where + " is not a region in regions.json.");
                    }
                }

                ValidateNames(set, where, errors);
            }

            if (known != null)
            {
                foreach (RegionData region in regions.Regions ?? new RegionData[0])
                {
                    if (region != null && !string.IsNullOrEmpty(region.RegionId) && !seen.Contains(region.RegionId))
                    {
                        errors.Add("Region '" + region.RegionId + "' has no location names.");
                    }
                }
            }

            return errors;
        }

        private static void ValidateNames(LocationNameSetData set, string where, List<string> errors)
        {
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocationKind kind in Kinds)
            {
                string[] names = set.NamesFor(kind);
                string field = where + " " + kind;
                int count = names == null ? 0 : names.Length;
                if (count != NodeMapGenerator.LabelVariants)
                {
                    errors.Add(field + " has " + count + " names; it needs exactly " + NodeMapGenerator.LabelVariants + " (one per label variant).");
                }

                for (int n = 0; n < count; n++)
                {
                    string name = names[n];
                    string at = field + "[" + n + "]";
                    if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                    {
                        errors.Add(at + " is empty.");
                        continue;
                    }

                    if (name.Trim().Length != name.Length)
                    {
                        errors.Add(at + " '" + name + "' has leading or trailing whitespace.");
                    }

                    if (name.Length > MaxNameLength)
                    {
                        errors.Add(at + " '" + name + "' is " + name.Length + " characters; the most is " + MaxNameLength + ".");
                    }

                    int words = CountWords(name);
                    if (words < MinNameWords || words > MaxNameWords)
                    {
                        errors.Add(at + " '" + name + "' is " + words + " words; names are " + MinNameWords + "-" + MaxNameWords + " words.");
                    }

                    if (!unique.Add(name))
                    {
                        errors.Add(at + " '" + name + "' repeats another name in the region.");
                    }
                }
            }
        }

        /// <summary>Whitespace-separated words in <paramref name="name"/>.</summary>
        public static int CountWords(string name)
        {
            return string.IsNullOrEmpty(name) ? 0 : name.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}
