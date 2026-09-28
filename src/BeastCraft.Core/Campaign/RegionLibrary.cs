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
        private readonly List<RegionData> _tutorials = new List<RegionData>();

        private RegionLibrary(RegionLibraryData data)
        {
            Data = data;
        }

        /// <summary>The authored data.</summary>
        public RegionLibraryData Data { get; }

        /// <summary>Every campaign region, in campaign order (the tutorial regions are not among them: <see cref="TutorialRegions"/>).</summary>
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

            // The onboarding regions: found by id like any region, never in the campaign order.
            foreach (RegionData region in library.Data.TutorialRegions ?? new RegionData[0])
            {
                if (region != null && !string.IsNullOrEmpty(region.RegionId) && !library._regions.ContainsKey(region.RegionId))
                {
                    library._regions.Add(region.RegionId, region);
                    library._tutorials.Add(region);
                }
            }

            return library;
        }

        /// <summary>The onboarding regions (<see cref="RegionLibraryData.TutorialRegions"/>), in file order.</summary>
        public IReadOnlyList<RegionData> TutorialRegions
        {
            get { return _tutorials; }
        }

        /// <summary>Hearthglen (<see cref="CampaignProgress.TutorialRegionId"/>), or null when the library has none.</summary>
        public RegionData Tutorial
        {
            get { return GetRegion(CampaignProgress.TutorialRegionId) is RegionData region && region.IsTutorial ? region : null; }
        }

        /// <summary>Whether <paramref name="regionId"/> is an onboarding region (<see cref="RegionData.IsTutorial"/>).</summary>
        public bool IsTutorial(string regionId)
        {
            RegionData region = GetRegion(regionId);
            return region != null && region.IsTutorial;
        }

        /// <summary>
        /// The region whose battlefields (painted backdrops, obstacle layouts) a battle in
        /// <paramref name="regionId"/> is fought on: its <see cref="RegionData.BattlefieldRegionId"/>
        /// when set, else itself.
        /// </summary>
        public string BattlefieldRegionOf(string regionId)
        {
            RegionData region = GetRegion(regionId);
            return region != null && !string.IsNullOrEmpty(region.BattlefieldRegionId) ? region.BattlefieldRegionId : regionId;
        }

        /// <summary>
        /// The battlefield region a fight at <paramref name="nodeId"/> of <paramref name="regionId"/>'s
        /// map stands on: null for an authored open-board location (<see cref="FixedNodeData.OpenBoard"/>),
        /// else <see cref="BattlefieldRegionOf"/>.
        /// </summary>
        public string BattlefieldFor(string regionId, int nodeId)
        {
            FixedNodeData node = FixedNode(regionId, nodeId);
            return node != null && node.OpenBoard ? null : BattlefieldRegionOf(regionId);
        }

        /// <summary>
        /// Tutorial region <paramref name="regionId"/>'s authored location <paramref name="nodeId"/>
        /// (<see cref="RegionData.FixedNodes"/>; the node id is its index), or null.
        /// </summary>
        public FixedNodeData FixedNode(string regionId, int nodeId)
        {
            RegionData region = GetRegion(regionId);
            FixedNodeData[] nodes = region == null ? null : region.FixedNodes;
            return nodes != null && nodeId >= 0 && nodeId < nodes.Length ? nodes[nodeId] : null;
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

        /// <summary>
        /// How much of the full early-region discount stage <paramref name="stage"/> of
        /// <paramref name="regionId"/> gets (<see cref="RegionData.StageEasing"/>), 0-1: 0 for an unknown
        /// region, a region without easing or a stage past its entries.
        /// </summary>
        public double EasingWeight(string regionId, int stage)
        {
            RegionData region = GetRegion(regionId);
            double[] weights = region == null ? null : region.StageEasing;
            if (weights == null || stage < 0 || stage >= weights.Length || double.IsNaN(weights[stage]))
            {
                return 0.0;
            }

            return Math.Max(0.0, Math.Min(1.0, weights[stage]));
        }

        /// <summary>
        /// The full early-region discount of <paramref name="shapeKey"/> (a shape id, or
        /// <see cref="RegionLibraryData.EasingBossId"/>; <see cref="RegionLibraryData.EasingShapeScales"/>):
        /// 1 when not listed (never eased).
        /// </summary>
        public double FullEasingScale(string shapeKey)
        {
            foreach (ShapeScaleData entry in Data.EasingShapeScales ?? new ShapeScaleData[0])
            {
                if (entry != null && string.Equals(entry.ShapeId, shapeKey, StringComparison.Ordinal) && entry.Scale > 0.0 && entry.Scale <= 1.0)
                {
                    return entry.Scale;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// The early-region easing of a campaign battle of <paramref name="shapeKey"/> (a shape id, or
        /// <see cref="RegionLibraryData.EasingBossId"/> for an authored template) in stage
        /// <paramref name="stage"/> of <paramref name="regionId"/>: the scale its enemies' calibrated
        /// multiplier is fielded at, 1 - <see cref="EasingWeight"/> x (1 - <see cref="FullEasingScale"/>).
        /// 1 where there is no easing.
        /// </summary>
        public double DifficultyScaleFor(string regionId, int stage, string shapeKey)
        {
            double weight = EasingWeight(regionId, stage);
            RegionData region = GetRegion(regionId);
            if (weight > 0.0 && region != null && region.BossScale > 0.0 && region.BossScale <= 1.0 && stage == Math.Max(1, region.Stages) - 1 &&
                string.Equals(shapeKey, RegionLibraryData.EasingBossId, StringComparison.Ordinal))
            {
                // The region's own boss scale (RegionData.BossScale) in place of the shared boss discount.
                return region.BossScale;
            }

            return weight <= 0.0 ? 1.0 : 1.0 - (weight * (1.0 - FullEasingScale(shapeKey)));
        }

        /// <summary>
        /// The floor <paramref name="shapeKey"/>'s adaptive assist reaches
        /// (<see cref="RegionLibraryData.AssistFloorScales"/>): 1 when not listed (never assisted).
        /// </summary>
        public double AssistFloorScale(string shapeKey)
        {
            foreach (ShapeScaleData entry in Data.AssistFloorScales ?? new ShapeScaleData[0])
            {
                if (entry != null && string.Equals(entry.ShapeId, shapeKey, StringComparison.Ordinal) && entry.Scale > 0.0 && entry.Scale <= 1.0)
                {
                    return entry.Scale;
                }
            }

            return 1.0;
        }

        /// <summary>
        /// Adaptive assist for <paramref name="shapeKey"/> in <paramref name="regionId"/> after
        /// <paramref name="losses"/> consecutive losses at the same location
        /// (<see cref="CampaignRules.LossesAt"/>): 1 at 0 losses, else <c>max(floor, (1 - AssistStep) ^
        /// losses)</c> (<see cref="RegionLibraryData.AssistStep"/>) — one more step of the same size
        /// each loss, compounding, clamped at the floor. The floor is the region's own
        /// <see cref="RegionData.AssistBossFloorScale"/> for its boss (<see cref="RegionLibraryData.EasingBossId"/>)
        /// when it sets one, else the shared <see cref="AssistFloorScale"/>. 1 when the library has no
        /// step or no floor for the shape (never assisted).
        /// </summary>
        public double AssistScaleFor(string regionId, string shapeKey, int losses)
        {
            if (losses <= 0)
            {
                return 1.0;
            }

            double step = Math.Max(0.0, Math.Min(1.0, Data.AssistStep));
            if (step <= 0.0)
            {
                return 1.0;
            }

            RegionData region = GetRegion(regionId);
            double floor = region != null && string.Equals(shapeKey, RegionLibraryData.EasingBossId, StringComparison.Ordinal) && region.AssistBossFloorScale > 0.0 &&
                           region.AssistBossFloorScale <= 1.0
                ? region.AssistBossFloorScale
                : AssistFloorScale(shapeKey);
            if (floor >= 1.0)
            {
                return 1.0;
            }

            double scale = Math.Pow(1.0 - step, losses);
            return Math.Max(floor, scale);
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

        /// <summary>Every region id, in campaign order, then the tutorial regions' (for a save catalog).</summary>
        public List<string> RegionIds()
        {
            List<string> ids = _order.ConvertAll(region => region.RegionId);
            ids.AddRange(_tutorials.ConvertAll(region => region.RegionId));
            return ids;
        }

        /// <summary>Every seal id, in file order (for a save catalog).</summary>
        public List<string> SealIds()
        {
            return new List<string>(_seals.Keys);
        }
    }
}
