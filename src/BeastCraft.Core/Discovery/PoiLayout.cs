using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Progression;

namespace BeastCraft.Discovery
{
    /// <summary>
    /// Lays out a region's points of interest, every stage at once, from the region's discovery seed
    /// (<see cref="RegionProgress.DiscoverySeed"/>) — never from an expedition's map seed, so a
    /// replay (a new map) never moves a point or changes what it holds. Deterministic: each stage
    /// draws from its own <see cref="System.Random"/>, <c>Random(DeriveSeed(seed, </c><see cref="StageStream"/><c> + stage))</c>,
    /// in a fixed order.
    /// <list type="number">
    /// <item><b>How many</b>: <see cref="RegionDiscoveryData.MinPois"/> to <see cref="RegionDiscoveryData.MaxPois"/>
    /// per stage (Kinship sites included).</item>
    /// <item><b>Kinship sites</b> (authored per stage, <see cref="KinshipSiteData.Stage"/>) first: on the
    /// centre column, between map rows <see cref="KinshipLayerMin"/> and <see cref="KinshipLayerMax"/> + 1.</item>
    /// <item><b>The rest</b>: each kind drawn by the region's weights (Shrine, LoreStone, Cache, Vista),
    /// at most <see cref="VistasPerStage"/> Vista a stage; a kind whose content has run out (every lore
    /// entry, cache or shrine of the region already placed: they are dealt in file order across the
    /// stages, each once) is not drawn. Each sits on a free slot: an odd half-row from 1 (between the
    /// first two rows) to the one under the top, an even column (between lanes, or at an edge), at
    /// least <see cref="MinSpacing"/> from every other point (in half-rows and columns).</item>
    /// </list>
    /// </summary>
    public static class PoiLayout
    {
        /// <summary>The <see cref="LootRoller.DeriveSeed"/> stream of stage 0 (stage <c>s</c> draws from <c>StageStream + s</c>).</summary>
        public const int StageStream = 0x504F4900;

        /// <summary>The stream a point's own <see cref="PointOfInterest.Seed"/> is derived on (plus stage × 64 + index).</summary>
        public const int PointStream = 0x504F5300;

        /// <summary>The lowest map row a Kinship site sits above.</summary>
        public const int KinshipLayerMin = 2;

        /// <summary>The highest map row a Kinship site sits above.</summary>
        public const int KinshipLayerMax = 6;

        public const int VistasPerStage = 1;

        /// <summary>The least distance between two points (an ellipse of this many half-rows by columns).</summary>
        public const double MinSpacing = 3.0;

        /// <summary>
        /// Every point of interest of <paramref name="regionId"/>, stage by stage (Kinship sites first
        /// in each), for discovery seed <paramref name="seed"/>. Empty when the region has no discovery
        /// layer (<see cref="DiscoveryLibrary.Region"/>), is unknown, or the seed is 0 (not assigned yet).
        /// </summary>
        public static List<PointOfInterest> ForRegion(DiscoveryLibrary library, RegionLibrary regions, string regionId, int seed)
        {
            List<PointOfInterest> points = new List<PointOfInterest>();
            RegionDiscoveryData data = library == null ? null : library.Region(regionId);
            RegionData region = regions == null ? null : regions.GetRegion(regionId);
            if (data == null || region == null || seed == 0)
            {
                return points;
            }

            MapRulesData rules = regions.RulesFor(region);
            FogGrid grid = MapFog.GridFor(rules);
            Queue<string> lore = new Queue<string>();
            foreach (LoreEntryData entry in library.StoneLore(regionId))
            {
                lore.Enqueue(entry.LoreId);
            }

            Queue<string> caches = new Queue<string>();
            foreach (CacheData cache in library.PointCaches(regionId))
            {
                caches.Enqueue(cache.CacheId);
            }

            Queue<string> shrines = new Queue<string>();
            foreach (ShrineData shrine in library.ShrinesOf(regionId))
            {
                shrines.Enqueue(shrine.ShrineId);
            }

            for (int stage = 0; stage < Math.Max(1, region.Stages); stage++)
            {
                Random rng = new Random(LootRoller.DeriveSeed(seed, StageStream + stage));
                int count = data.MinPois + rng.Next(Math.Max(0, data.MaxPois - data.MinPois) + 1);
                List<PointOfInterest> stagePoints = new List<PointOfInterest>();
                foreach (KinshipSiteData site in library.SitesOn(regionId, stage))
                {
                    int layer = Math.Min(grid.MapRows - 2, KinshipLayerMin + rng.Next(KinshipLayerMax - KinshipLayerMin + 1));
                    int halfRow = (2 * Math.Max(0, layer)) + 1;
                    int col = grid.Lanes;
                    while (Taken(stagePoints, halfRow, col) && halfRow + 2 < grid.HalfRows - 1)
                    {
                        halfRow += 2;
                    }

                    stagePoints.Add(Point(regionId, stage, stagePoints.Count, PoiKind.KinshipSite, halfRow, col, site.SiteId, seed, region, rules, site.LevelOffset));
                }

                int vistas = 0;
                while (stagePoints.Count < count)
                {
                    PoiKind? kind = DrawKind(data, rng, lore.Count > 0, caches.Count > 0, shrines.Count > 0, vistas < VistasPerStage);
                    if (!kind.HasValue)
                    {
                        break;
                    }

                    List<int> slots = FreeSlots(grid, stagePoints);
                    if (slots.Count == 0)
                    {
                        break;
                    }

                    int slot = slots[rng.Next(slots.Count)];
                    string refId = string.Empty;
                    switch (kind.Value)
                    {
                        case PoiKind.LoreStone:
                            refId = lore.Dequeue();
                            break;
                        case PoiKind.Cache:
                            refId = caches.Dequeue();
                            break;
                        case PoiKind.Shrine:
                            refId = shrines.Dequeue();
                            break;
                        default:
                            vistas++;
                            break;
                    }

                    stagePoints.Add(Point(regionId, stage, stagePoints.Count, kind.Value, grid.RowOf(slot), grid.ColOf(slot), refId, seed, region, rules, 0));
                }

                points.AddRange(stagePoints);
            }

            return points;
        }

        /// <summary><see cref="ForRegion"/>'s points on stage <paramref name="stage"/> only.</summary>
        public static List<PointOfInterest> ForStage(DiscoveryLibrary library, RegionLibrary regions, string regionId, int stage, int seed)
        {
            return ForRegion(library, regions, regionId, seed).FindAll(point => point.Stage == stage);
        }

        private static PointOfInterest Point(string regionId, int stage, int index, PoiKind kind, int halfRow, int col, string refId, int seed, RegionData region,
                                             MapRulesData rules, int levelOffset)
        {
            int level = NodeMapGenerator.RowLevel(region, rules, stage, halfRow / 2) + levelOffset;
            return new PointOfInterest
            {
                PoiId = regionId + "/s" + stage.ToString(CultureInfo.InvariantCulture) + "/" + index.ToString(CultureInfo.InvariantCulture),
                RegionId = regionId,
                Stage = stage,
                Index = index,
                Kind = kind,
                HalfRow = halfRow,
                Col = col,
                Level = Math.Max(1, Math.Min(BeastProgression.MaxLevel, level)),
                RefId = refId ?? string.Empty,
                Seed = LootRoller.DeriveSeed(seed, PointStream + (stage * 64) + index)
            };
        }

        private static PoiKind? DrawKind(RegionDiscoveryData data, Random rng, bool lore, bool caches, bool shrines, bool vista)
        {
            List<PoiKind> kinds = new List<PoiKind>();
            List<int> weights = new List<int>();
            int total = 0;
            foreach (PoiWeightData weight in data.Weights ?? new PoiWeightData[0])
            {
                if (weight == null || weight.Weight <= 0 || !PointOfInterest.TryParseKind(weight.Kind, out PoiKind kind))
                {
                    continue;
                }

                bool available = kind == PoiKind.LoreStone ? lore : kind == PoiKind.Cache ? caches : kind == PoiKind.Shrine ? shrines : kind == PoiKind.Vista && vista;
                if (available)
                {
                    kinds.Add(kind);
                    weights.Add(weight.Weight);
                    total += weight.Weight;
                }
            }

            if (total <= 0)
            {
                return null;
            }

            int roll = rng.Next(total);
            for (int i = 0; i < kinds.Count; i++)
            {
                if (roll < weights[i])
                {
                    return kinds[i];
                }

                roll -= weights[i];
            }

            return kinds[kinds.Count - 1];
        }

        /// <summary>Every free slot (odd half-row 1 to the one under the top, even column), in cell order.</summary>
        private static List<int> FreeSlots(FogGrid grid, List<PointOfInterest> placed)
        {
            List<int> slots = new List<int>();
            for (int halfRow = 1; halfRow < grid.HalfRows - 1; halfRow += 2)
            {
                for (int col = 0; col < grid.Cols; col += 2)
                {
                    if (!Taken(placed, halfRow, col))
                    {
                        slots.Add(grid.Index(halfRow, col));
                    }
                }
            }

            return slots;
        }

        private static bool Taken(List<PointOfInterest> placed, int halfRow, int col)
        {
            foreach (PointOfInterest point in placed)
            {
                double dr = (point.HalfRow - halfRow) / MinSpacing;
                double dc = (point.Col - col) / MinSpacing;
                if ((dr * dr) + (dc * dc) < 1.0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
