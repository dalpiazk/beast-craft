using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Discovery;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Validates <c>achievements.json</c> (<see cref="AchievementLibraryData"/>): unique snake_case
    /// achievement and title ids, a known <see cref="AchievementKinds"/> kind, a region-scoped kind
    /// naming a region that kind can check (a mainline region for <c>BossCleared</c>, a discovery
    /// region with lore for <c>RegionLoreComplete</c>, a discovery region for <c>RegionExplored</c>)
    /// and no <c>RegionId</c> otherwise, a threshold kind with a positive, in-range
    /// <see cref="AchievementData.Threshold"/> and 0 otherwise, and a non-empty <c>DisplayName</c> and
    /// <c>TitleText</c> within their length caps. No stats anywhere: an achievement only ever awards a
    /// text title.
    /// </summary>
    public static class AchievementLibraryValidator
    {
        public const int MaxNameLength = 40;

        public const int MaxTitleLength = 32;

        public static List<string> Validate(AchievementLibraryData data, RegionLibraryData regions, DiscoveryLibraryData discovery, ICollection<string> speciesIds)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("achievements.json is missing.");
                return errors;
            }

            if (data.SchemaVersion != AchievementLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion must be " + AchievementLibraryData.CurrentSchemaVersion + ".");
            }

            RegionLibrary library = regions == null ? null : RegionLibrary.Build(regions);
            HashSet<string> discoveryRegions = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionDiscoveryData region in discovery == null ? new RegionDiscoveryData[0] : discovery.Regions ?? new RegionDiscoveryData[0])
            {
                if (region != null && !string.IsNullOrEmpty(region.RegionId))
                {
                    discoveryRegions.Add(region.RegionId);
                }
            }

            HashSet<string> regionsWithLore = new HashSet<string>(StringComparer.Ordinal);
            foreach (LoreEntryData lore in discovery == null ? new LoreEntryData[0] : discovery.Lore ?? new LoreEntryData[0])
            {
                if (lore != null && !string.IsNullOrEmpty(lore.RegionId))
                {
                    regionsWithLore.Add(lore.RegionId);
                }
            }

            int kinshipSites = discovery == null ? 0 : (discovery.KinshipSites ?? new KinshipSiteData[0]).Length;
            int rosterSize = speciesIds == null ? int.MaxValue : Math.Max(1, speciesIds.Count);

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> titleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AchievementData achievement in data.Achievements ?? new AchievementData[0])
            {
                string at = "Achievements[" + (achievement == null ? "?" : achievement.AchievementId) + "]";
                if (achievement == null || !CheckId(errors, "Achievements", achievement.AchievementId, ids))
                {
                    continue;
                }

                CheckName(errors, at + ": DisplayName", achievement.DisplayName, MaxNameLength);

                if (Array.IndexOf(AchievementKinds.All, achievement.Kind) < 0)
                {
                    errors.Add(at + ": Kind '" + achievement.Kind + "' must be one of " + string.Join(", ", AchievementKinds.All) + ".");
                    continue;
                }

                ValidateRegion(errors, at, achievement, library, discoveryRegions, regionsWithLore);
                ValidateThreshold(errors, at, achievement, kinshipSites, rosterSize);

                if (!IsSnakeCase(achievement.TitleId) || !titleIds.Add(achievement.TitleId))
                {
                    errors.Add(at + ": TitleId must be a snake_case id no other achievement uses.");
                }

                CheckName(errors, at + ": TitleText", achievement.TitleText, MaxTitleLength);
            }

            return errors;
        }

        private static void ValidateRegion(List<string> errors, string at, AchievementData achievement, RegionLibrary library, HashSet<string> discoveryRegions,
                                           HashSet<string> regionsWithLore)
        {
            bool regionScoped = AchievementKinds.IsRegionScoped(achievement.Kind);
            if (!regionScoped)
            {
                if (!string.IsNullOrEmpty(achievement.RegionId))
                {
                    errors.Add(at + ": Kind " + achievement.Kind + " does not use RegionId.");
                }

                return;
            }

            if (string.IsNullOrEmpty(achievement.RegionId))
            {
                errors.Add(at + ": Kind " + achievement.Kind + " needs a RegionId.");
                return;
            }

            RegionData region = library == null ? null : library.GetRegion(achievement.RegionId);
            if (achievement.Kind == AchievementKinds.BossCleared)
            {
                if (library != null && (region == null || region.IsTutorial))
                {
                    errors.Add(at + ": RegionId '" + achievement.RegionId + "' is not a mainline region with a boss.");
                }

                return;
            }

            if (achievement.Kind == AchievementKinds.RegionExplored && library != null && !discoveryRegions.Contains(achievement.RegionId))
            {
                errors.Add(at + ": RegionId '" + achievement.RegionId + "' is not a discovery region (discovery.json Regions).");
            }

            if (achievement.Kind == AchievementKinds.RegionLoreComplete && library != null && !regionsWithLore.Contains(achievement.RegionId))
            {
                errors.Add(at + ": RegionId '" + achievement.RegionId + "' has no lore entries (discovery.json Lore).");
            }
        }

        private static void ValidateThreshold(List<string> errors, string at, AchievementData achievement, int kinshipSites, int rosterSize)
        {
            bool threshold = AchievementKinds.IsThreshold(achievement.Kind);
            if (!threshold)
            {
                if (achievement.Threshold != 0)
                {
                    errors.Add(at + ": Kind " + achievement.Kind + " does not use Threshold.");
                }

                return;
            }

            int max = achievement.Kind == AchievementKinds.KinshipSitesClaimed && kinshipSites > 0 ? kinshipSites
                      : achievement.Kind == AchievementKinds.BeastsOwned ? rosterSize
                      : achievement.Kind == AchievementKinds.CompendiumPercent ? 100
                      : BeastProgression.MaxLevel;

            if (achievement.Threshold < 1 || achievement.Threshold > max)
            {
                errors.Add(at + ": Threshold must be 1 to " + max + " for " + achievement.Kind + ".");
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
                errors.Add(list + ": id '" + id + "' is used twice in achievements.json.");
                return false;
            }

            return true;
        }

        private static void CheckName(List<string> errors, string at, string name, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > maxLength || name.IndexOf('\'') >= 0)
            {
                errors.Add(at + " must be 1-" + maxLength + " characters with no apostrophe.");
            }
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
