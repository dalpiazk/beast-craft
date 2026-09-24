using System;
using System.Collections.Generic;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// <see cref="RegionLibraryData"/> built for the runtime: regions and seals indexed by id, each
    /// region's effective map rules resolved, and which regions each region's boss unlocks. Expects
    /// data that passed <see cref="RegionLibraryValidator"/>; unknown ids give null, null entries and
    /// repeated ids are skipped (the first wins). Build it once with <see cref="Build"/> (the
    /// <see cref="RegionLibrarySO"/> does) and share it.
    /// </summary>
    public sealed class RegionLibrary
    {
        private readonly Dictionary<string, RegionData> _regions = new Dictionary<string, RegionData>(StringComparer.Ordinal);
        private readonly Dictionary<string, SealData> _seals = new Dictionary<string, SealData>(StringComparer.Ordinal);
        private readonly List<RegionData> _order = new List<RegionData>();

        private RegionLibrary(RegionLibraryData data)
        {
            Data = data;
        }

        /// <summary>The authored data.</summary>
        public RegionLibraryData Data { get; }

        /// <summary>Every region, in campaign order.</summary>
        public IReadOnlyList<RegionData> Regions
        {
            get { return _order; }
        }

        /// <summary><see cref="RegionLibraryData.StartingLevelCap"/>.</summary>
        public int StartingLevelCap
        {
            get { return Data.StartingLevelCap; }
        }

        /// <summary>The library over <paramref name="data"/> (null reads as empty).</summary>
        public static RegionLibrary Build(RegionLibraryData data)
        {
            RegionLibrary library = new RegionLibrary(data ?? new RegionLibraryData());

            foreach (SealData seal in library.Data.Seals ?? new SealData[0])
            {
                if (seal != null && !string.IsNullOrEmpty(seal.SealId) && !library._seals.ContainsKey(seal.SealId))
                {
                    library._seals.Add(seal.SealId, seal);
                }
            }

            foreach (RegionData region in library.Data.Regions ?? new RegionData[0])
            {
                if (region != null && !string.IsNullOrEmpty(region.RegionId) && !library._regions.ContainsKey(region.RegionId))
                {
                    library._regions.Add(region.RegionId, region);
                    library._order.Add(region);
                }
            }

            return library;
        }

        /// <summary>The region with <paramref name="regionId"/>, or null.</summary>
        public RegionData GetRegion(string regionId)
        {
            return !string.IsNullOrEmpty(regionId) && _regions.TryGetValue(regionId, out RegionData region) ? region : null;
        }

        /// <summary>The seal with <paramref name="sealId"/>, or null.</summary>
        public SealData GetSeal(string sealId)
        {
            return !string.IsNullOrEmpty(sealId) && _seals.TryGetValue(sealId, out SealData seal) ? seal : null;
        }

        /// <summary>The region's own map rules when it overrides them (<see cref="MapRulesData.Layers"/> above 0), otherwise the library's.</summary>
        public MapRulesData RulesFor(RegionData region)
        {
            return region != null && region.MapRules != null && region.MapRules.Layers > 0 ? region.MapRules : Data.MapRules ?? new MapRulesData();
        }

        /// <summary>The regions whose <see cref="RegionData.RequiresRegionId"/> is <paramref name="regionId"/>, in campaign order.</summary>
        public List<RegionData> UnlockedBy(string regionId)
        {
            List<RegionData> unlocked = new List<RegionData>();
            foreach (RegionData region in _order)
            {
                if (!string.IsNullOrEmpty(regionId) && string.Equals(region.RequiresRegionId, regionId, StringComparison.Ordinal))
                {
                    unlocked.Add(region);
                }
            }

            return unlocked;
        }

        /// <summary>Every region id, in campaign order (for a save catalog).</summary>
        public List<string> RegionIds()
        {
            return _order.ConvertAll(region => region.RegionId);
        }

        /// <summary>Every seal id, in file order (for a save catalog).</summary>
        public List<string> SealIds()
        {
            return new List<string>(_seals.Keys);
        }
    }
}
