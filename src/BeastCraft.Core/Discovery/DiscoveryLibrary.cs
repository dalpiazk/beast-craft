using System;
using System.Collections.Generic;

namespace BeastCraft.Discovery
{
    /// <summary>Lookups over a validated <see cref="DiscoveryLibraryData"/> (<c>discovery.json</c>).</summary>
    public sealed class DiscoveryLibrary
    {
        private readonly Dictionary<string, RegionDiscoveryData> _regions = new Dictionary<string, RegionDiscoveryData>(StringComparer.Ordinal);
        private readonly Dictionary<string, KinshipSiteData> _sites = new Dictionary<string, KinshipSiteData>(StringComparer.Ordinal);
        private readonly Dictionary<string, LoreEntryData> _lore = new Dictionary<string, LoreEntryData>(StringComparer.Ordinal);
        private readonly Dictionary<string, CacheData> _caches = new Dictionary<string, CacheData>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShrineData> _shrines = new Dictionary<string, ShrineData>(StringComparer.Ordinal);

        private DiscoveryLibrary(DiscoveryLibraryData data)
        {
            Data = data;
        }

        public DiscoveryLibraryData Data { get; }

        /// <summary>Every Kinship site, in file (campaign) order.</summary>
        public IReadOnlyList<KinshipSiteData> Sites
        {
            get { return Data.KinshipSites ?? new KinshipSiteData[0]; }
        }

        public static DiscoveryLibrary Build(DiscoveryLibraryData data)
        {
            DiscoveryLibrary library = new DiscoveryLibrary(data ?? new DiscoveryLibraryData());
            foreach (RegionDiscoveryData region in library.Data.Regions ?? new RegionDiscoveryData[0])
            {
                if (region != null && !string.IsNullOrEmpty(region.RegionId))
                {
                    library._regions[region.RegionId] = region;
                }
            }

            foreach (KinshipSiteData site in library.Data.KinshipSites ?? new KinshipSiteData[0])
            {
                if (site != null && !string.IsNullOrEmpty(site.SiteId))
                {
                    library._sites[site.SiteId] = site;
                }
            }

            foreach (LoreEntryData lore in library.Data.Lore ?? new LoreEntryData[0])
            {
                if (lore != null && !string.IsNullOrEmpty(lore.LoreId))
                {
                    library._lore[lore.LoreId] = lore;
                }
            }

            foreach (CacheData cache in library.Data.Caches ?? new CacheData[0])
            {
                if (cache != null && !string.IsNullOrEmpty(cache.CacheId))
                {
                    library._caches[cache.CacheId] = cache;
                }
            }

            foreach (ShrineData shrine in library.Data.Shrines ?? new ShrineData[0])
            {
                if (shrine != null && !string.IsNullOrEmpty(shrine.ShrineId))
                {
                    library._shrines[shrine.ShrineId] = shrine;
                }
            }

            return library;
        }

        /// <summary>The region's discovery layer, or null (no fog, no points of interest there).</summary>
        public RegionDiscoveryData Region(string regionId)
        {
            return regionId != null && _regions.TryGetValue(regionId, out RegionDiscoveryData region) ? region : null;
        }

        public KinshipSiteData Site(string siteId)
        {
            return siteId != null && _sites.TryGetValue(siteId, out KinshipSiteData site) ? site : null;
        }

        public LoreEntryData Lore(string loreId)
        {
            return loreId != null && _lore.TryGetValue(loreId, out LoreEntryData lore) ? lore : null;
        }

        public CacheData Cache(string cacheId)
        {
            return cacheId != null && _caches.TryGetValue(cacheId, out CacheData cache) ? cache : null;
        }

        public ShrineData Shrine(string shrineId)
        {
            return shrineId != null && _shrines.TryGetValue(shrineId, out ShrineData shrine) ? shrine : null;
        }

        /// <summary>The Kinship sites on stage <paramref name="stage"/> of <paramref name="regionId"/>, in file order.</summary>
        public List<KinshipSiteData> SitesOn(string regionId, int stage)
        {
            List<KinshipSiteData> sites = new List<KinshipSiteData>();
            foreach (KinshipSiteData site in Sites)
            {
                if (site != null && site.RegionId == regionId && site.Stage == stage)
                {
                    sites.Add(site);
                }
            }

            return sites;
        }

        /// <summary>
        /// The lore entries a region's lore stones reveal, in file order: the region's entries that are no
        /// Kinship site's own (those are recorded by claiming the site).
        /// </summary>
        public List<LoreEntryData> StoneLore(string regionId)
        {
            HashSet<string> siteLore = new HashSet<string>(StringComparer.Ordinal);
            foreach (KinshipSiteData site in Sites)
            {
                if (site != null && !string.IsNullOrEmpty(site.LoreId))
                {
                    siteLore.Add(site.LoreId);
                }
            }

            List<LoreEntryData> entries = new List<LoreEntryData>();
            foreach (LoreEntryData lore in Data.Lore ?? new LoreEntryData[0])
            {
                if (lore != null && lore.RegionId == regionId && !siteLore.Contains(lore.LoreId))
                {
                    entries.Add(lore);
                }
            }

            return entries;
        }

        /// <summary>The caches a region's cache points hold, in file order: the region's caches that are no Kinship site's fallback.</summary>
        public List<CacheData> PointCaches(string regionId)
        {
            HashSet<string> fallbacks = new HashSet<string>(StringComparer.Ordinal);
            foreach (KinshipSiteData site in Sites)
            {
                if (site != null && !string.IsNullOrEmpty(site.FallbackCacheId))
                {
                    fallbacks.Add(site.FallbackCacheId);
                }
            }

            List<CacheData> caches = new List<CacheData>();
            foreach (CacheData cache in Data.Caches ?? new CacheData[0])
            {
                if (cache != null && cache.RegionId == regionId && !fallbacks.Contains(cache.CacheId))
                {
                    caches.Add(cache);
                }
            }

            return caches;
        }

        /// <summary>The region's shrines, in file order.</summary>
        public List<ShrineData> ShrinesOf(string regionId)
        {
            List<ShrineData> shrines = new List<ShrineData>();
            foreach (ShrineData shrine in Data.Shrines ?? new ShrineData[0])
            {
                if (shrine != null && shrine.RegionId == regionId)
                {
                    shrines.Add(shrine);
                }
            }

            return shrines;
        }
    }
}
