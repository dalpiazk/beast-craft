using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Encounters;

namespace BeastCraft.Discovery
{
    /// <summary>
    /// Validates <c>discovery.json</c> (<see cref="DiscoveryLibraryData"/>) against the content it names:
    /// unique snake_case ids; every region a known campaign region (never a tutorial or post-game one)
    /// with 1 to <see cref="MaxPoisPerStage"/> points per stage and positive weights over the drawn kinds
    /// (Shrine, LoreStone, Cache, Vista); every Kinship site on a discovery region's stage, its trial a
    /// Draft template with its own <c>DifficultyOverride</c>, its preferred species known and distinct, a
    /// known bond condition, its own lore entry and fallback cache in its region, and no stage holding
    /// more sites than its fewest points; lore, caches and shrines in discovery regions, caches paying
    /// something (known materials, positive quantities, a <c>discovery</c> look), shrines naming distinct
    /// Grove unlocks; every completion look a <c>discovery</c> look of its region; and enough lore, caches
    /// and shrines that no stage's layout runs short, even with no Vista drawn (<see cref="PoiLayout"/>). Display names follow the
    /// content bible (at most <see cref="MaxNameLength"/> characters).
    /// </summary>
    public static class DiscoveryLibraryValidator
    {
        public const int MaxPoisPerStage = 8;

        public const int MaxNameLength = 24;

        public const int MaxTextLength = 280;

        public static List<string> Validate(DiscoveryLibraryData data, RegionLibraryData regions, EncounterLibraryData encounters, ICollection<string> speciesIds,
                                            ICollection<string> materialIds, CosmeticLibraryData cosmetics)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("discovery.json is missing.");
                return errors;
            }

            if (data.SchemaVersion != 1)
            {
                errors.Add("SchemaVersion must be 1.");
            }

            RegionLibrary library = regions == null ? null : RegionLibrary.Build(regions);
            Dictionary<string, RegionDiscoveryData> discoveryRegions = new Dictionary<string, RegionDiscoveryData>(StringComparer.Ordinal);
            foreach (RegionDiscoveryData region in data.Regions ?? new RegionDiscoveryData[0])
            {
                string at = "Regions[" + (region == null ? "?" : region.RegionId) + "]";
                if (region == null || string.IsNullOrEmpty(region.RegionId))
                {
                    errors.Add("Regions: an entry has no RegionId.");
                    continue;
                }

                if (discoveryRegions.ContainsKey(region.RegionId))
                {
                    errors.Add(at + ": listed twice.");
                    continue;
                }

                discoveryRegions.Add(region.RegionId, region);
                RegionData known = library == null ? null : library.GetRegion(region.RegionId);
                if (library != null && (known == null || known.IsTutorial || known.IsPostGame))
                {
                    errors.Add(at + ": not a mainline campaign region of regions.json.");
                }

                if (region.MinPois < 1 || region.MaxPois < region.MinPois || region.MaxPois > MaxPoisPerStage)
                {
                    errors.Add(at + ": MinPois and MaxPois must satisfy 1 <= MinPois <= MaxPois <= " + MaxPoisPerStage + ".");
                }

                int total = 0;
                HashSet<PoiKind> kinds = new HashSet<PoiKind>();
                foreach (PoiWeightData weight in region.Weights ?? new PoiWeightData[0])
                {
                    if (weight == null || !PointOfInterest.TryParseKind(weight.Kind, out PoiKind kind) || kind == PoiKind.KinshipSite)
                    {
                        errors.Add(at + ": weight kind '" + (weight == null ? "null" : weight.Kind) + "' must be Shrine, LoreStone, Cache or Vista.");
                        continue;
                    }

                    if (!kinds.Add(kind))
                    {
                        errors.Add(at + ": weight kind " + kind + " listed twice.");
                    }

                    if (weight.Weight < 0)
                    {
                        errors.Add(at + ": weight of " + kind + " is negative.");
                    }

                    total += Math.Max(0, weight.Weight);
                }

                if (total <= 0)
                {
                    errors.Add(at + ": needs at least one positive weight.");
                }

                if (region.CompletionGold < 0)
                {
                    errors.Add(at + ": CompletionGold is negative.");
                }

                CheckLook(errors, at + ": CompletionLook", region.CompletionLook, region.RegionId, cosmetics, false);
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, LoreEntryData> lore = new Dictionary<string, LoreEntryData>(StringComparer.Ordinal);
            foreach (LoreEntryData entry in data.Lore ?? new LoreEntryData[0])
            {
                string at = "Lore[" + (entry == null ? "?" : entry.LoreId) + "]";
                if (entry == null || !CheckId(errors, "Lore", entry.LoreId, ids))
                {
                    continue;
                }

                lore[entry.LoreId] = entry;
                CheckRegion(errors, at, entry.RegionId, discoveryRegions);
                CheckName(errors, at + ": Title", entry.Title);
                CheckText(errors, at + ": Text", entry.Text);
            }

            Dictionary<string, CacheData> caches = new Dictionary<string, CacheData>(StringComparer.Ordinal);
            foreach (CacheData cache in data.Caches ?? new CacheData[0])
            {
                string at = "Caches[" + (cache == null ? "?" : cache.CacheId) + "]";
                if (cache == null || !CheckId(errors, "Caches", cache.CacheId, ids))
                {
                    continue;
                }

                caches[cache.CacheId] = cache;
                CheckRegion(errors, at, cache.RegionId, discoveryRegions);
                CheckName(errors, at + ": Name", cache.Name);
                bool pays = cache.Gold > 0 || !string.IsNullOrEmpty(cache.Look);
                if (cache.Gold < 0)
                {
                    errors.Add(at + ": Gold is negative.");
                }

                foreach (CacheMaterialData material in cache.Materials ?? new CacheMaterialData[0])
                {
                    if (material == null || material.Quantity <= 0 || (materialIds != null && !materialIds.Contains(material.MaterialId ?? string.Empty)))
                    {
                        errors.Add(at + ": a material must be a known skill-library material with a positive Quantity.");
                        continue;
                    }

                    pays = true;
                }

                if (!pays)
                {
                    errors.Add(at + ": pays nothing (gold, materials or a look).");
                }

                if (!string.IsNullOrEmpty(cache.Look))
                {
                    CheckLook(errors, at + ": Look", cache.Look, cache.CacheId, cosmetics, true);
                }
            }

            HashSet<string> groveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShrineData shrine in data.Shrines ?? new ShrineData[0])
            {
                string at = "Shrines[" + (shrine == null ? "?" : shrine.ShrineId) + "]";
                if (shrine == null || !CheckId(errors, "Shrines", shrine.ShrineId, ids))
                {
                    continue;
                }

                CheckRegion(errors, at, shrine.RegionId, discoveryRegions);
                CheckName(errors, at + ": Name", shrine.Name);
                CheckText(errors, at + ": Text", shrine.Text);
                if (!IsSnakeCase(shrine.GroveUnlockId) || !groveIds.Add(shrine.GroveUnlockId))
                {
                    errors.Add(at + ": GroveUnlockId must be a snake_case id no other shrine grants.");
                }
            }

            Dictionary<string, int> sitesPerStage = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> siteLore = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> fallbackCaches = new HashSet<string>(StringComparer.Ordinal);
            foreach (KinshipSiteData site in data.KinshipSites ?? new KinshipSiteData[0])
            {
                string at = "KinshipSites[" + (site == null ? "?" : site.SiteId) + "]";
                if (site == null || !CheckId(errors, "KinshipSites", site.SiteId, ids))
                {
                    continue;
                }

                CheckRegion(errors, at, site.RegionId, discoveryRegions);
                CheckName(errors, at + ": Name", site.Name);
                CheckText(errors, at + ": Intro", site.Intro);
                RegionData region = library == null ? null : library.GetRegion(site.RegionId);
                if (region != null && (site.Stage < 0 || site.Stage >= Math.Max(1, region.Stages)))
                {
                    errors.Add(at + ": Stage " + site.Stage + " is not a stage of " + site.RegionId + ".");
                }

                string key = site.RegionId + "/" + site.Stage;
                sitesPerStage[key] = (sitesPerStage.TryGetValue(key, out int count) ? count : 0) + 1;
                if (discoveryRegions.TryGetValue(site.RegionId ?? string.Empty, out RegionDiscoveryData home) && sitesPerStage[key] > home.MinPois)
                {
                    errors.Add(at + ": stage " + site.Stage + " of " + site.RegionId + " holds more Kinship sites than its fewest points (" + home.MinPois + ").");
                }

                if (site.LevelOffset < 0 || site.LevelOffset > 5)
                {
                    errors.Add(at + ": LevelOffset must be 0 to 5.");
                }

                EncounterTemplateData template = encounters == null ? null : Array.Find(encounters.Templates ?? new EncounterTemplateData[0], t => t != null && t.EncounterId == site.TemplateId);
                if (encounters != null && template == null)
                {
                    errors.Add(at + ": unknown trial template '" + site.TemplateId + "'.");
                }
                else if (template != null && (!(template.DifficultyOverride > 0.0) || !template.Draft))
                {
                    errors.Add(at + ": trial template '" + site.TemplateId + "' needs its own DifficultyOverride and Draft: true.");
                }

                HashSet<string> preferred = new HashSet<string>(StringComparer.Ordinal);
                foreach (string species in site.Preferred ?? new string[0])
                {
                    if (string.IsNullOrEmpty(species) || (speciesIds != null && !speciesIds.Contains(species)) || !preferred.Add(species))
                    {
                        errors.Add(at + ": Preferred names '" + species + "', which is unknown or repeated.");
                    }
                }

                if (preferred.Count == 0)
                {
                    errors.Add(at + ": Preferred must name the region's theme beasts.");
                }

                if (!IsBondCondition(site.BondCondition))
                {
                    errors.Add(at + ": BondCondition '" + site.BondCondition + "' must be '', no_knockout, stance:{Vanguard|Ranged|Skirmisher} or element:{Element}.");
                }
                else if (!string.IsNullOrEmpty(site.BondCondition) && string.IsNullOrEmpty(site.BondText))
                {
                    errors.Add(at + ": a bond condition needs its BondText.");
                }

                if (!lore.TryGetValue(site.LoreId ?? string.Empty, out LoreEntryData entry) || entry.RegionId != site.RegionId || !siteLore.Add(site.LoreId))
                {
                    errors.Add(at + ": LoreId must be a lore entry of its region no other site uses.");
                }

                if (!caches.TryGetValue(site.FallbackCacheId ?? string.Empty, out CacheData fallback) || fallback.RegionId != site.RegionId || !fallbackCaches.Add(site.FallbackCacheId))
                {
                    errors.Add(at + ": FallbackCacheId must be a cache of its region no other site uses.");
                }
            }

            // Enough content that no stage's layout runs short.
            foreach (RegionDiscoveryData region in discoveryRegions.Values)
            {
                RegionData known = library == null ? null : library.GetRegion(region.RegionId);
                int stages = known == null ? 1 : Math.Max(1, known.Stages);
                int sites = 0;
                foreach (KinshipSiteData site in data.KinshipSites ?? new KinshipSiteData[0])
                {
                    sites += site != null && site.RegionId == region.RegionId ? 1 : 0;
                }

                int stoneLore = 0;
                foreach (LoreEntryData entry in lore.Values)
                {
                    stoneLore += entry.RegionId == region.RegionId && !siteLore.Contains(entry.LoreId) ? 1 : 0;
                }

                int pointCaches = 0;
                foreach (CacheData cache in caches.Values)
                {
                    pointCaches += cache.RegionId == region.RegionId && !fallbackCaches.Contains(cache.CacheId) ? 1 : 0;
                }

                int shrines = 0;
                foreach (ShrineData shrine in data.Shrines ?? new ShrineData[0])
                {
                    shrines += shrine != null && shrine.RegionId == region.RegionId ? 1 : 0;
                }

                // Vistas never carry over (at most one a stage), so the dealt entries alone must cover every stage at its most.
                int available = Weighted(region, PoiKind.LoreStone) * stoneLore + Weighted(region, PoiKind.Cache) * pointCaches + Weighted(region, PoiKind.Shrine) * shrines;
                int needed = (stages * region.MaxPois) - sites;
                if (available < needed)
                {
                    errors.Add("Regions[" + region.RegionId + "]: " + available + " drawable points (lore, caches and shrines dealt once each) for up to " + needed +
                               " places: add content or lower MaxPois.");
                }
            }

            // Every discovery look is some reward's.
            if (cosmetics != null)
            {
                HashSet<string> granted = new HashSet<string>(StringComparer.Ordinal);
                foreach (RegionDiscoveryData region in discoveryRegions.Values)
                {
                    granted.Add(region.CompletionLook ?? string.Empty);
                }

                foreach (CacheData cache in caches.Values)
                {
                    granted.Add(cache.Look ?? string.Empty);
                }

                foreach (CosmeticCategoryData category in cosmetics.Categories ?? new CosmeticCategoryData[0])
                {
                    foreach (CosmeticOptionData option in category == null ? new CosmeticOptionData[0] : category.Options ?? new CosmeticOptionData[0])
                    {
                        if (option != null && option.Source == CosmeticLibrary.SourceDiscovery && !granted.Contains(category.CategoryId + "/" + option.OptionId))
                        {
                            errors.Add("cosmetic-library.json look '" + category.CategoryId + "/" + option.OptionId + "' is a discovery look no region or cache grants.");
                        }
                    }
                }
            }

            return errors;
        }

        private static int Weighted(RegionDiscoveryData region, PoiKind kind)
        {
            foreach (PoiWeightData weight in region.Weights ?? new PoiWeightData[0])
            {
                if (weight != null && weight.Weight > 0 && PointOfInterest.TryParseKind(weight.Kind, out PoiKind parsed) && parsed == kind)
                {
                    return 1;
                }
            }

            return 0;
        }

        private static void CheckLook(List<string> errors, string at, string key, string unlockId, CosmeticLibraryData cosmetics, bool optional)
        {
            if (string.IsNullOrEmpty(key))
            {
                if (!optional)
                {
                    errors.Add(at + " is missing.");
                }

                return;
            }

            if (cosmetics == null)
            {
                return;
            }

            string[] parts = key.Split('/');
            CosmeticCategoryData category = parts.Length != 2 ? null : Array.Find(cosmetics.Categories ?? new CosmeticCategoryData[0], c => c != null && c.CategoryId == parts[0]);
            CosmeticOptionData option = category == null ? null : Array.Find(category.Options ?? new CosmeticOptionData[0], o => o != null && o.OptionId == parts[1]);
            if (option == null || option.Source != CosmeticLibrary.SourceDiscovery || option.UnlockId != unlockId)
            {
                errors.Add(at + " '" + key + "' must be a discovery look whose UnlockId is '" + unlockId + "'.");
            }
        }

        private static bool CheckId(List<string> errors, string list, string id, HashSet<string> ids)
        {
            if (!IsSnakeCase(id))
            {
                errors.Add(list + ": id '" + id + "' must be snake_case.");
                return false;
            }

            if (!ids.Add(id))
            {
                errors.Add(list + ": id '" + id + "' is used twice in discovery.json.");
                return false;
            }

            return true;
        }

        private static void CheckRegion(List<string> errors, string at, string regionId, Dictionary<string, RegionDiscoveryData> regions)
        {
            if (string.IsNullOrEmpty(regionId) || !regions.ContainsKey(regionId))
            {
                errors.Add(at + ": RegionId '" + regionId + "' is not a discovery region (Regions).");
            }
        }

        private static void CheckName(List<string> errors, string at, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength || name.IndexOf('\'') >= 0)
            {
                errors.Add(at + " must be 1-" + MaxNameLength + " characters with no apostrophe.");
            }
        }

        private static void CheckText(List<string> errors, string at, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            {
                errors.Add(at + " must be 1-" + MaxTextLength + " characters.");
            }
        }

        private static bool IsBondCondition(string condition)
        {
            if (string.IsNullOrEmpty(condition) || condition == "no_knockout")
            {
                return true;
            }

            string[] parts = condition.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (parts[0] == "stance")
            {
                return parts[1] == "Vanguard" || parts[1] == "Ranged" || parts[1] == "Skirmisher";
            }

            return parts[0] == "element" && Enum.TryParse(parts[1], false, out Creatures.Element element) && element != Creatures.Element.None;
        }

        private static bool IsSnakeCase(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] < 'a' || id[0] > 'z')
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
