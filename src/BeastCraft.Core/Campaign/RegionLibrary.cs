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

        /// <summary>Whether an expedition into <paramref name="region"/> may be played on <paramref name="difficulty"/>: Normal anywhere, Hard only in a post-game region.</summary>
        public static bool Allows(RegionData region, RunDifficulty difficulty)
        {
            return region != null && (difficulty == RunDifficulty.Normal || (difficulty == RunDifficulty.Hard && region.IsPostGame));
        }

        /// <summary>
        /// <paramref name="region"/> as an expedition on <paramref name="difficulty"/> fields it: itself
        /// on Normal; on Hard (a post-game region only) a copy whose <see cref="RegionData.ShapeWeights"/>
        /// and <see cref="RegionData.BossTemplateId"/> are its <see cref="RegionData.HardMode"/>'s. Null
        /// when the difficulty is not allowed there (<see cref="Allows"/>).
        /// </summary>
        public RegionData RegionFor(RegionData region, RunDifficulty difficulty)
        {
            if (!Allows(region, difficulty))
            {
                return null;
            }

            if (difficulty == RunDifficulty.Normal)
            {
                return region;
            }

            RegionHardModeData hard = region.HardMode ?? new RegionHardModeData();
            RegionData copy = region.Copy();
            copy.ShapeWeights = hard.ShapeWeights ?? new ShapeWeightData[0];
            copy.BossTemplateId = hard.BossTemplateId ?? string.Empty;
            return copy;
        }

        /// <summary>
        /// The map rules an expedition into <paramref name="region"/> on <paramref name="difficulty"/>
        /// uses: <see cref="RulesFor(RegionData)"/>, on Hard with the <see cref="RegionData.HardMode"/>'s
        /// <see cref="RegionHardModeData.EliteShapeId"/>. Null when the difficulty is not allowed there.
        /// </summary>
        public MapRulesData RulesFor(RegionData region, RunDifficulty difficulty)
        {
            if (!Allows(region, difficulty))
            {
                return null;
            }

            MapRulesData rules = RulesFor(region);
            if (difficulty == RunDifficulty.Normal)
            {
                return rules;
            }

            MapRulesData copy = rules.Copy();
            copy.EliteShapeId = region.HardMode == null ? string.Empty : region.HardMode.EliteShapeId ?? string.Empty;
            return copy;
        }

        /// <summary>The mainline regions (not <see cref="RegionData.IsPostGame"/>), in campaign order.</summary>
        public List<RegionData> MainlineRegions()
        {
            return _order.FindAll(region => !region.IsPostGame);
        }

        /// <summary>The post-game regions (<see cref="RegionData.IsPostGame"/>), in campaign order.</summary>
        public List<RegionData> PostGameRegions()
        {
            return _order.FindAll(region => region.IsPostGame);
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
