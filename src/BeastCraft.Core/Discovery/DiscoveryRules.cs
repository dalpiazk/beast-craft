using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Discovery
{
    /// <summary>What the discovery rules read: the discovery library and the content its points refer to.</summary>
    public sealed class DiscoveryContent
    {
        public DiscoveryLibrary Library;

        public RegionLibrary Regions;

        /// <summary>The looks (a cache's or the 100% reward's); null = no look is granted.</summary>
        public CosmeticLibrary Cosmetics;

        /// <summary>The roster, in roster order (Kinship offers fall back to it; a recruit's stance and element).</summary>
        public IReadOnlyList<CreatureSpeciesSO> Roster;

        /// <summary>The skill library (a recruit's default loadout).</summary>
        public SkillLibraryData Skills;

        /// <summary>The encounters and enemies a Kinship trial is planned from.</summary>
        public EncounterLibrary Encounters;

        public EnemyCatalog Enemies;

        private readonly Dictionary<string, List<PointOfInterest>> _layouts = new Dictionary<string, List<PointOfInterest>>(StringComparer.Ordinal);

        /// <summary>
        /// <see cref="PoiLayout.ForRegion"/> of <see cref="Library"/> and <see cref="Regions"/>, memoized per
        /// region and seed (a layout never changes for its inputs; callers must not change the points).
        /// </summary>
        public List<PointOfInterest> Layout(string regionId, int seed)
        {
            string key = (regionId ?? string.Empty) + "/" + seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            lock (_layouts)
            {
                if (!_layouts.TryGetValue(key, out List<PointOfInterest> points))
                {
                    points = PoiLayout.ForRegion(Library, Regions, regionId, seed);
                    _layouts[key] = points;
                }

                return new List<PointOfInterest>(points);
            }
        }
    }

    /// <summary>How a point of interest shows on the map.</summary>
    public enum PoiState
    {
        /// <summary>Still under the fog.</summary>
        Hidden,

        /// <summary>Seen and waiting to be visited.</summary>
        Revealed,

        /// <summary>Visited (a Kinship site: its beast joined, or it had none left).</summary>
        Found
    }

    /// <summary>A region's exploration: locations (map rows walked, per stage) and points of interest found.</summary>
    public sealed class RegionCompletion
    {
        public string RegionId;

        /// <summary>Map rows walked over every stage (a cleared stage counts all its rows).</summary>
        public int LocationsExplored;

        public int LocationsTotal;

        public int PoisFound;

        public int PoisTotal;

        /// <summary>0-100, rounded down: 100 only when everything is explored and found.</summary>
        public int Percent;

        /// <summary>Whether the 100% reward was granted.</summary>
        public bool Rewarded;

        public bool IsComplete
        {
            get { return LocationsExplored >= LocationsTotal && PoisFound >= PoisTotal; }
        }
    }

    /// <summary>What a discovery call did.</summary>
    public sealed class DiscoveryResult
    {
        public bool Success;

        /// <summary>Why it was refused; null on success.</summary>
        public string Error;

        public PointOfInterest Poi;

        /// <summary>A shrine's Grove unlock, newly granted (or null).</summary>
        public string GroveUnlockId;

        /// <summary>A lore entry newly recorded (a lore stone's, or a claimed Kinship site's), or null.</summary>
        public string LoreId;

        /// <summary>A cache opened (its id), or null.</summary>
        public string CacheId;

        public int Gold;

        public List<MaterialGrant> Materials = new List<MaterialGrant>();

        /// <summary>Looks unlocked (cosmetic keys).</summary>
        public List<string> Looks = new List<string>();

        /// <summary>A Vista's fog cells lifted.</summary>
        public int CellsRevealed;

        /// <summary>A Kinship site with no beast left to offer was visited as a lore and cache stop instead.</summary>
        public bool KinshipFallback;

        internal static DiscoveryResult Refused(string error)
        {
            return new DiscoveryResult { Error = error };
        }
    }

    /// <summary>A material stack granted.</summary>
    public sealed class MaterialGrant
    {
        public string MaterialId;
        public int Quantity;
    }

    /// <summary>The 100% exploration reward of a region (<see cref="DiscoveryRules.TryComplete"/>).</summary>
    public sealed class CompletionReward
    {
        public string RegionId;

        /// <summary>The look unlocked (a cosmetic key), or null when it was already owned.</summary>
        public string Look;

        public int Gold;
    }

    /// <summary>
    /// The discovery layer's rules over a <see cref="PlayerSave"/>: which points of interest are seen
    /// (the fog, <see cref="MapFog"/>), visiting them (a Shrine records a discovery and grants its Grove
    /// unlock id; a LoreStone records its lore entry; a Cache grants its fixed gold, materials and look;
    /// a Vista lifts a chunk of the fog; a Kinship site's trial is <see cref="KinshipRules"/>'), and a
    /// region's completion (locations plus points of interest) and its 100% reward. Every reward is
    /// non-combat or from the existing economy (gold, materials, looks): no new combat power, and no
    /// random draw anywhere. Non-throwing; a refused call changes nothing.
    /// <para>
    /// A point can be visited while an expedition of its stage is on the map, once it is seen: its fog
    /// cell lifted, or — for a Kinship site, which calls to a passing Beastbinder — once a location on
    /// its row or above has been cleared on the stage (<see cref="StageFog.DeepestLayer"/>).
    /// </para>
    /// </summary>
    public static class DiscoveryRules
    {
        /// <summary>The region's points of interest for the save's discovery seed (empty before the region's first expedition).</summary>
        public static List<PointOfInterest> PointsOf(PlayerSave save, DiscoveryContent content, string regionId)
        {
            RegionProgress progress = save == null || save.Campaign == null ? null : save.Campaign.FindRegion(regionId);
            if (progress == null || content == null)
            {
                return new List<PointOfInterest>();
            }

            return content.Layout(regionId, progress.DiscoverySeed);
        }

        /// <summary>The points of interest on the map of the expedition in progress (none outside a discovery region).</summary>
        public static List<PointOfInterest> PointsOnMap(PlayerSave save, DiscoveryContent content)
        {
            if (save == null || save.Campaign == null || !save.Campaign.HasActiveRun)
            {
                return new List<PointOfInterest>();
            }

            MapRun run = save.Campaign.ActiveRun;
            return PointsOf(save, content, run.RegionId).FindAll(point => point.Stage == run.Stage);
        }

        /// <summary>Whether the region has a discovery layer (fog and points of interest).</summary>
        public static bool HasDiscovery(DiscoveryContent content, string regionId)
        {
            return content != null && content.Library != null && content.Library.Region(regionId) != null;
        }

        /// <summary>The fog grid of <paramref name="regionId"/>'s stage maps.</summary>
        public static FogGrid GridOf(DiscoveryContent content, string regionId)
        {
            return MapFog.GridFor(content.Regions, content.Regions.GetRegion(regionId));
        }

        /// <summary>Whether <paramref name="poi"/> is seen (see the class remarks).</summary>
        public static bool IsRevealed(RegionProgress progress, FogGrid grid, PointOfInterest poi)
        {
            StageFog fog = progress == null || poi == null ? null : progress.FindFog(poi.Stage);
            if (fog == null)
            {
                return false;
            }

            return MapFog.IsRevealed(fog, grid.Index(poi.HalfRow, poi.Col)) || (poi.Kind == PoiKind.KinshipSite && fog.DeepestLayer >= poi.Layer);
        }

        /// <summary>How <paramref name="poi"/> shows.</summary>
        public static PoiState StateOf(PlayerSave save, DiscoveryContent content, PointOfInterest poi)
        {
            RegionProgress progress = save == null || poi == null ? null : save.Campaign.FindRegion(poi.RegionId);
            if (progress == null)
            {
                return PoiState.Hidden;
            }

            if (progress.HasFound(poi.PoiId))
            {
                return PoiState.Found;
            }

            return IsRevealed(progress, GridOf(content, poi.RegionId), poi) ? PoiState.Revealed : PoiState.Hidden;
        }

        /// <summary>
        /// The point <paramref name="poiId"/> of the expedition in progress's map when it can be visited
        /// now (seen, not yet found); null with <paramref name="error"/> otherwise.
        /// </summary>
        public static PointOfInterest Visitable(PlayerSave save, DiscoveryContent content, string poiId, out string error)
        {
            error = null;
            if (save == null || content == null)
            {
                error = "No save or no discovery content.";
                return null;
            }

            save.EnsureInitialized();
            PointOfInterest poi = PointOfInterest.Find(PointsOnMap(save, content), poiId);
            if (poi == null)
            {
                error = "That place is not on this map.";
                return null;
            }

            PoiState state = StateOf(save, content, poi);
            if (state == PoiState.Found)
            {
                error = "You have already been there.";
                return null;
            }

            if (state == PoiState.Hidden)
            {
                error = "The Gloam still hides that place: explore nearby first.";
                return null;
            }

            return poi;
        }

        /// <summary>
        /// Visits point <paramref name="poiId"/> (see the class remarks). A Kinship site is refused here
        /// (its trial is <see cref="KinshipRules"/>') unless it has no beast left to offer: then it is a
        /// lore and cache stop — its own lore entry and its fallback cache — and counts as claimed.
        /// </summary>
        public static DiscoveryResult Visit(PlayerSave save, DiscoveryContent content, string poiId)
        {
            PointOfInterest poi = Visitable(save, content, poiId, out string error);
            if (poi == null)
            {
                return DiscoveryResult.Refused(error);
            }

            RegionProgress progress = save.Campaign.FindRegion(poi.RegionId);
            DiscoveryResult result = new DiscoveryResult { Success = true, Poi = poi };
            switch (poi.Kind)
            {
                case PoiKind.Shrine:
                    ShrineData shrine = content.Library.Shrine(poi.RefId);
                    if (shrine != null && DiscoveryProgress.AddOnce(save.Discovery.GroveUnlockIds, shrine.GroveUnlockId))
                    {
                        result.GroveUnlockId = shrine.GroveUnlockId;
                    }

                    break;
                case PoiKind.LoreStone:
                    if (DiscoveryProgress.AddOnce(save.Discovery.LoreIds, poi.RefId))
                    {
                        result.LoreId = poi.RefId;
                    }

                    break;
                case PoiKind.Cache:
                    GrantCache(save, content, poi.RefId, result);
                    break;
                case PoiKind.Vista:
                    result.CellsRevealed = MapFog.RevealVista(progress, GridOf(content, poi.RegionId), poi.Stage, poi.HalfRow, poi.Col);
                    break;
                case PoiKind.KinshipSite:
                    KinshipSiteData site = content.Library.Site(poi.RefId);
                    if (site == null || KinshipRules.Offer(save, site, content.Roster).Count > 0)
                    {
                        return DiscoveryResult.Refused("A beast waits here: face its trial.");
                    }

                    result.KinshipFallback = true;
                    DiscoveryProgress.AddOnce(save.Discovery.ClaimedKinshipIds, site.SiteId);
                    if (DiscoveryProgress.AddOnce(save.Discovery.LoreIds, site.LoreId))
                    {
                        result.LoreId = site.LoreId;
                    }

                    GrantCache(save, content, site.FallbackCacheId, result);
                    break;
            }

            progress.MarkFound(poi.PoiId);
            return result;
        }

        /// <summary>Grants cache <paramref name="cacheId"/>'s fixed rewards (never rolled) into <paramref name="result"/>.</summary>
        public static void GrantCache(PlayerSave save, DiscoveryContent content, string cacheId, DiscoveryResult result)
        {
            CacheData cache = content.Library.Cache(cacheId);
            if (cache == null)
            {
                return;
            }

            result.CacheId = cache.CacheId;
            if (cache.Gold > 0)
            {
                result.Gold += Wallet.Add(save, cache.Gold);
            }

            foreach (CacheMaterialData material in cache.Materials ?? new CacheMaterialData[0])
            {
                if (material != null && material.Quantity > 0 && save.Materials.Add(material.MaterialId, material.Quantity))
                {
                    result.Materials.Add(new MaterialGrant { MaterialId = material.MaterialId, Quantity = material.Quantity });
                }
            }

            if (!string.IsNullOrEmpty(cache.Look) && CosmeticRules.Unlock(save, content.Cosmetics, cache.Look))
            {
                result.Looks.Add(cache.Look);
            }
        }

        /// <summary>
        /// <paramref name="regionId"/>'s exploration: map rows walked over its stages (a cleared stage all
        /// of them, another as far as <see cref="StageFog.DeepestLayer"/>) plus points of interest found,
        /// over the total. Null for a region without a discovery layer or not unlocked.
        /// </summary>
        public static RegionCompletion Completion(PlayerSave save, DiscoveryContent content, string regionId)
        {
            RegionData region = content == null || content.Regions == null ? null : content.Regions.GetRegion(regionId);
            RegionProgress progress = save == null || save.Campaign == null ? null : save.Campaign.FindRegion(regionId);
            if (region == null || progress == null || !HasDiscovery(content, regionId))
            {
                return null;
            }

            int layers = Math.Max(1, content.Regions.RulesFor(region).Layers);
            int stages = Math.Max(1, region.Stages);
            RegionCompletion completion = new RegionCompletion { RegionId = regionId, LocationsTotal = stages * layers, Rewarded = progress.Completed };
            for (int stage = 0; stage < stages; stage++)
            {
                bool cleared = progress.BossCleared || stage < progress.StagesCleared;
                StageFog fog = progress.FindFog(stage);
                completion.LocationsExplored += cleared ? layers : Math.Min(layers, (fog == null ? -1 : fog.DeepestLayer) + 1);
            }

            List<PointOfInterest> points = content.Layout(regionId, progress.DiscoverySeed);
            completion.PoisTotal = points.Count;
            foreach (PointOfInterest point in points)
            {
                completion.PoisFound += progress.HasFound(point.PoiId) ? 1 : 0;
            }

            int done = completion.LocationsExplored + completion.PoisFound;
            int total = completion.LocationsTotal + completion.PoisTotal;
            completion.Percent = total <= 0 ? 0 : completion.IsComplete ? 100 : Math.Min(99, (int)(100L * done / total));
            return completion;
        }

        /// <summary>
        /// Grants <paramref name="regionId"/>'s 100% reward (its DRAFT look and gold) the first time the
        /// region is fully explored; null otherwise (not complete, already granted, or no discovery layer).
        /// </summary>
        public static CompletionReward TryComplete(PlayerSave save, DiscoveryContent content, string regionId)
        {
            RegionCompletion completion = Completion(save, content, regionId);
            if (completion == null || completion.Rewarded || !completion.IsComplete)
            {
                return null;
            }

            RegionProgress progress = save.Campaign.FindRegion(regionId);
            RegionDiscoveryData data = content.Library.Region(regionId);
            progress.Completed = true;
            CompletionReward reward = new CompletionReward { RegionId = regionId };
            if (!string.IsNullOrEmpty(data.CompletionLook) && CosmeticRules.Unlock(save, content.Cosmetics, data.CompletionLook))
            {
                reward.Look = data.CompletionLook;
            }

            if (data.CompletionGold > 0)
            {
                reward.Gold = Wallet.Add(save, data.CompletionGold);
            }

            return reward;
        }
    }
}
