using System;
using System.Collections.Generic;

namespace BeastCraft.Discovery
{
    /// <summary>
    /// What a point of interest is. A separate, off-path discovery layer over a stage's map — never a
    /// <c>MapNodeType</c>, so the pacing model (the node map the balance simulator routes) never sees
    /// one. Never saved as a number (a point of interest is regenerated from its region's seed; the
    /// save names it by <see cref="PointOfInterest.PoiId"/>).
    /// </summary>
    public enum PoiKind
    {
        /// <summary>Records a discovery and grants a Grove unlock (held for the Grove).</summary>
        Shrine,

        /// <summary>A lore entry (DRAFT text), kept for the compendium.</summary>
        LoreStone,

        /// <summary>A cache of fixed rewards (gold, materials, a look), never rolled.</summary>
        Cache,

        /// <summary>A Kinship site: a fixed trial whose win lets one of two not-yet-owned beasts join.</summary>
        KinshipSite,

        /// <summary>A lookout: visiting it lifts a large chunk of the stage's fog.</summary>
        Vista
    }

    /// <summary>
    /// One point of interest on a stage's map, as laid out by <see cref="PoiLayout"/> from the region's
    /// discovery seed: where it sits on the fog grid (<c>MapFog</c>: an odd half-row, between two map
    /// rows, and an even column, between two lanes or at an edge), its level and what it holds.
    /// Derived data, never saved: the same seed always lays out the same points.
    /// </summary>
    public sealed class PointOfInterest
    {
        /// <summary>Stable id, <c>{regionId}/s{stage}/{index}</c> (e.g. <c>r01/s0/2</c>): what the save records.</summary>
        public string PoiId;

        public string RegionId;

        public int Stage;

        /// <summary>Its index among the stage's points, 0-based (Kinship sites first).</summary>
        public int Index;

        public PoiKind Kind;

        /// <summary>The fog-grid half-row (odd: between map rows <c>HalfRow / 2</c> and the next).</summary>
        public int HalfRow;

        /// <summary>The fog-grid column (even: between lanes, or at an edge).</summary>
        public int Col;

        /// <summary>The level of the map row just below it (a Kinship trial adds its site's offset).</summary>
        public int Level;

        /// <summary>What it holds: the Kinship site, lore entry, cache or shrine id ("" for a Vista).</summary>
        public string RefId = string.Empty;

        /// <summary>Its own seed (a Kinship trial's battle seeds derive from it).</summary>
        public int Seed;

        /// <summary>The map row just below it.</summary>
        public int Layer
        {
            get { return HalfRow / 2; }
        }

        /// <summary>The kind's lowercase key (<c>shrine</c>, <c>lore_stone</c>, <c>cache</c>, <c>kinship_site</c>, <c>vista</c>).</summary>
        public static string Key(PoiKind kind)
        {
            switch (kind)
            {
                case PoiKind.Shrine:
                    return "shrine";
                case PoiKind.LoreStone:
                    return "lore_stone";
                case PoiKind.Cache:
                    return "cache";
                case PoiKind.KinshipSite:
                    return "kinship_site";
                default:
                    return "vista";
            }
        }

        /// <summary>The kind named <paramref name="name"/> (its enum name, as <c>discovery.json</c> writes it; case-sensitive).</summary>
        public static bool TryParseKind(string name, out PoiKind kind)
        {
            foreach (PoiKind candidate in (PoiKind[])Enum.GetValues(typeof(PoiKind)))
            {
                if (string.Equals(candidate.ToString(), name, StringComparison.Ordinal))
                {
                    kind = candidate;
                    return true;
                }
            }

            kind = PoiKind.Vista;
            return false;
        }

        /// <summary>The point in <paramref name="points"/> with <paramref name="poiId"/>, or null.</summary>
        public static PointOfInterest Find(IEnumerable<PointOfInterest> points, string poiId)
        {
            foreach (PointOfInterest point in points ?? new PointOfInterest[0])
            {
                if (point != null && string.Equals(point.PoiId, poiId, StringComparison.Ordinal))
                {
                    return point;
                }
            }

            return null;
        }
    }
}
