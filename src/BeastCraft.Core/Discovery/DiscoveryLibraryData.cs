using System;

namespace BeastCraft.Discovery
{
    /// <summary>
    /// The discovery layer's content (<c>content/data/Campaign/discovery.json</c>): which regions carry
    /// points of interest and how many, the Kinship sites, the lore stones' entries, the caches'
    /// fixed rewards and the shrines' Grove unlocks. Plain data with public fields and arrays (no
    /// dictionaries), like every content file; validated by <see cref="DiscoveryLibraryValidator"/>,
    /// looked up through <see cref="DiscoveryLibrary"/>.
    /// </summary>
    [Serializable]
    public class DiscoveryLibraryData
    {
        public const string ProjectRelativePath = "content/data/Campaign/discovery.json";

        public int SchemaVersion = 1;

        /// <summary>The regions with a discovery layer (fog and points of interest), in campaign order. A region not listed is shown fully revealed.</summary>
        public RegionDiscoveryData[] Regions = new RegionDiscoveryData[0];

        /// <summary>The Kinship sites: a trial whose win lets one of two not-yet-owned beasts join.</summary>
        public KinshipSiteData[] KinshipSites = new KinshipSiteData[0];

        /// <summary>Lore entries (a lore stone's, or a Kinship site's story), for the future compendium.</summary>
        public LoreEntryData[] Lore = new LoreEntryData[0];

        /// <summary>Caches: fixed rewards, never rolled.</summary>
        public CacheData[] Caches = new CacheData[0];

        /// <summary>Shrines: each records a discovery and grants a Grove unlock (consumed by the Grove, later).</summary>
        public ShrineData[] Shrines = new ShrineData[0];
    }

    /// <summary>One region's discovery layer.</summary>
    [Serializable]
    public class RegionDiscoveryData
    {
        public string RegionId;

        /// <summary>Points of interest per stage map, Kinship sites included: from <see cref="MinPois"/> to <see cref="MaxPois"/>, drawn from the region's discovery seed.</summary>
        public int MinPois;

        public int MaxPois;

        /// <summary>How the other points of interest are drawn (Shrine, LoreStone, Cache, Vista), by weight.</summary>
        public PoiWeightData[] Weights = new PoiWeightData[0];

        /// <summary>The 100% reward's look: a cosmetic key (<c>categoryId/optionId</c>) of a <c>discovery</c> look whose UnlockId is this region.</summary>
        public string CompletionLook = string.Empty;

        /// <summary>The 100% reward's gold (the existing economy).</summary>
        public int CompletionGold;
    }

    /// <summary>A point-of-interest kind's draw weight.</summary>
    [Serializable]
    public class PoiWeightData
    {
        /// <summary>A <see cref="PoiKind"/> name: Shrine, LoreStone, Cache or Vista (Kinship sites are authored, never drawn).</summary>
        public string Kind;

        public int Weight;
    }

    /// <summary>
    /// A Kinship site: on stage <see cref="Stage"/> of <see cref="RegionId"/>, a fixed trial
    /// (<see cref="TemplateId"/>) at the site's level; its win offers a choice of two of the
    /// player's not-yet-owned beasts, <see cref="Preferred"/> (the region's theme) first
    /// (<see cref="KinshipRules.Offer"/>). A site with nothing left to offer is a lore and cache stop.
    /// </summary>
    [Serializable]
    public class KinshipSiteData
    {
        public string SiteId;

        public string RegionId;

        /// <summary>The stage (0-based) whose map holds the site.</summary>
        public int Stage;

        /// <summary>The site's display name (DRAFT).</summary>
        public string Name;

        /// <summary>The trial: an <c>encounter-library.json</c> template with its own <c>DifficultyOverride</c>.</summary>
        public string TemplateId;

        /// <summary>Levels above its map row's level the trial is fought at.</summary>
        public int LevelOffset;

        /// <summary>Species ids in preference order (the region's theme first); every other species follows in roster order.</summary>
        public string[] Preferred = new string[0];

        /// <summary>
        /// The optional bond condition, flavour only (no reward): <c>stance:{Stance}</c> (a beast of that
        /// stance fought), <c>element:{Element}</c> (a beast of that element fought), <c>no_knockout</c>
        /// (no beast was knocked out), or "" for none.
        /// </summary>
        public string BondCondition = string.Empty;

        /// <summary>The bond condition in words, shown on the trial's preview (DRAFT).</summary>
        public string BondText = string.Empty;

        /// <summary>What the site says when the trial is offered (DRAFT).</summary>
        public string Intro = string.Empty;

        /// <summary>The site's own lore entry, recorded when it is claimed.</summary>
        public string LoreId = string.Empty;

        /// <summary>The cache granted instead when the site has no beast left to offer.</summary>
        public string FallbackCacheId = string.Empty;
    }

    /// <summary>A lore entry (DRAFT text, the content bible's tone).</summary>
    [Serializable]
    public class LoreEntryData
    {
        public string LoreId;

        public string RegionId;

        public string Title;

        public string Text;
    }

    /// <summary>A cache: fixed rewards from the existing economy (gold, materials, a look), never rolled.</summary>
    [Serializable]
    public class CacheData
    {
        public string CacheId;

        public string RegionId;

        /// <summary>What the cache is (DRAFT), e.g. "Mossy Satchel".</summary>
        public string Name;

        public int Gold;

        public CacheMaterialData[] Materials = new CacheMaterialData[0];

        /// <summary>A look (cosmetic key, <c>categoryId/optionId</c>, a <c>discovery</c> look whose UnlockId is this cache), or "".</summary>
        public string Look = string.Empty;
    }

    /// <summary>A cache's material stack.</summary>
    [Serializable]
    public class CacheMaterialData
    {
        public string MaterialId;

        public int Quantity;
    }

    /// <summary>A shrine: records a discovery and grants a Grove unlock id (held until the Grove consumes it).</summary>
    [Serializable]
    public class ShrineData
    {
        public string ShrineId;

        public string RegionId;

        public string Name;

        /// <summary>The Grove unlock this shrine grants (a habitat or decor the Grove will read; an id only for now).</summary>
        public string GroveUnlockId;

        /// <summary>What the shrine says (DRAFT).</summary>
        public string Text;
    }
}
