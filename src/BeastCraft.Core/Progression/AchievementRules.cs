using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Save;

namespace BeastCraft.Progression
{
    /// <summary>What <see cref="AchievementRules.Evaluate"/> reads: the achievement library, and the discovery content the compendium and Kinship conditions need.</summary>
    public sealed class AchievementContent
    {
        public AchievementLibrary Library;

        /// <summary>The discovery content (its <c>Regions</c>, <c>Library</c> and <c>Roster</c> — the compendium and Kinship conditions read these too).</summary>
        public DiscoveryContent Discovery;
    }

    /// <summary>
    /// Deterministic, save-derived achievements (the Collector persona): no RNG anywhere, and no
    /// combat power — every achievement awards a text title only (<see cref="AchievementProgress"/>).
    /// <see cref="Evaluate"/> is idempotent (an already-earned achievement is skipped) and safe to call
    /// after any state change; <c>DiscoveryRules.Visit</c>, <c>DiscoveryRules.TryComplete</c>,
    /// <c>KinshipRules.Choose</c> and <c>CampaignRules.ResolveBattle</c>'s boss clear call it when their
    /// content is wired up (<see cref="DiscoveryContent.Achievements"/> / the optional
    /// <c>achievements</c> parameter).
    /// </summary>
    public static class AchievementRules
    {
        /// <summary>
        /// Every achievement of <paramref name="content"/> newly met by <paramref name="save"/>: each
        /// is recorded once (<see cref="AchievementProgress.EarnedIds"/>) and its title newly owned
        /// (<see cref="AchievementProgress.OwnedTitleIds"/>). Pure over the save's current state; call
        /// again any time (a repeat earns nothing already earned).
        /// </summary>
        public static List<AchievementData> Evaluate(PlayerSave save, AchievementContent content)
        {
            List<AchievementData> earned = new List<AchievementData>();
            if (save == null || content == null || content.Library == null)
            {
                return earned;
            }

            save.EnsureInitialized();
            foreach (AchievementData achievement in content.Library.All)
            {
                if (achievement == null || string.IsNullOrEmpty(achievement.AchievementId) || save.Achievements.HasEarned(achievement.AchievementId))
                {
                    continue;
                }

                if (!IsMet(save, content, achievement))
                {
                    continue;
                }

                AchievementProgress.AddOnce(save.Achievements.EarnedIds, achievement.AchievementId);
                AchievementProgress.AddOnce(save.Achievements.OwnedTitleIds, achievement.TitleId);
                earned.Add(achievement);
            }

            return earned;
        }

        private static bool IsMet(PlayerSave save, AchievementContent content, AchievementData def)
        {
            switch (def.Kind)
            {
                case AchievementKinds.BossCleared:
                    {
                        RegionProgress region = save.Campaign.FindRegion(def.RegionId);
                        return region != null && region.BossCleared;
                    }

                case AchievementKinds.AllBossesCleared:
                    return AllMainlineBossesCleared(save, content);

                case AchievementKinds.RegionExplored:
                    {
                        RegionCompletion completion = DiscoveryRules.Completion(save, content.Discovery, def.RegionId);
                        return completion != null && completion.IsComplete;
                    }

                case AchievementKinds.AllRegionsExplored:
                    return AllDiscoveryRegionsExplored(save, content);

                case AchievementKinds.KinshipSitesClaimed:
                    return (save.Discovery.ClaimedKinshipIds == null ? 0 : save.Discovery.ClaimedKinshipIds.Count) >= def.Threshold;

                case AchievementKinds.AllKinshipClaimed:
                    {
                        int total = content.Discovery == null || content.Discovery.Library == null ? 0 : content.Discovery.Library.Sites.Count;
                        return total > 0 && (save.Discovery.ClaimedKinshipIds == null ? 0 : save.Discovery.ClaimedKinshipIds.Count) >= total;
                    }

                case AchievementKinds.BeastsOwned:
                    return KinshipRules.OwnedSpecies(save).Count >= def.Threshold;

                case AchievementKinds.RegionLoreComplete:
                    return RegionLoreComplete(save, content, def.RegionId);

                case AchievementKinds.AllLoreFound:
                    return AllLoreFound(save, content);

                case AchievementKinds.CompendiumPercent:
                    {
                        CompendiumCompletion completion = CompendiumRules.Completion(save, content.Discovery);
                        return completion != null && completion.Percent >= def.Threshold;
                    }

                case AchievementKinds.AvatarLevel:
                    return save.Avatar != null && save.Avatar.Level >= def.Threshold;

                case AchievementKinds.BeastLevel:
                    return AnyBeastAtLevel(save, def.Threshold);

                default:
                    return false;
            }
        }

        private static bool AllMainlineBossesCleared(PlayerSave save, AchievementContent content)
        {
            RegionLibrary regions = content.Discovery == null ? null : content.Discovery.Regions;
            if (regions == null || regions.Regions.Count == 0)
            {
                return false;
            }

            bool any = false;
            foreach (RegionData region in regions.Regions)
            {
                if (region == null || region.IsPostGame)
                {
                    continue;
                }

                any = true;
                RegionProgress progress = save.Campaign.FindRegion(region.RegionId);
                if (progress == null || !progress.BossCleared)
                {
                    return false;
                }
            }

            return any;
        }

        private static bool AllDiscoveryRegionsExplored(PlayerSave save, AchievementContent content)
        {
            if (content.Discovery == null || content.Discovery.Library == null)
            {
                return false;
            }

            bool any = false;
            foreach (RegionDiscoveryData region in content.Discovery.Library.Data.Regions ?? new RegionDiscoveryData[0])
            {
                if (region == null || string.IsNullOrEmpty(region.RegionId))
                {
                    continue;
                }

                any = true;
                RegionCompletion completion = DiscoveryRules.Completion(save, content.Discovery, region.RegionId);
                if (completion == null || !completion.IsComplete)
                {
                    return false;
                }
            }

            return any;
        }

        private static bool RegionLoreComplete(PlayerSave save, AchievementContent content, string regionId)
        {
            if (content.Discovery == null || content.Discovery.Library == null)
            {
                return false;
            }

            bool any = false;
            foreach (LoreEntryData lore in content.Discovery.Library.Data.Lore ?? new LoreEntryData[0])
            {
                if (lore == null || lore.RegionId != regionId)
                {
                    continue;
                }

                any = true;
                if (save.Discovery.LoreIds == null || !save.Discovery.LoreIds.Contains(lore.LoreId))
                {
                    return false;
                }
            }

            return any;
        }

        private static bool AllLoreFound(PlayerSave save, AchievementContent content)
        {
            if (content.Discovery == null || content.Discovery.Library == null)
            {
                return false;
            }

            LoreEntryData[] lore = content.Discovery.Library.Data.Lore ?? new LoreEntryData[0];
            if (lore.Length == 0)
            {
                return false;
            }

            foreach (LoreEntryData entry in lore)
            {
                if (entry == null || save.Discovery.LoreIds == null || !save.Discovery.LoreIds.Contains(entry.LoreId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AnyBeastAtLevel(PlayerSave save, int threshold)
        {
            foreach (OwnedBeast beast in save.Beasts ?? new List<OwnedBeast>())
            {
                if (beast != null && beast.Progress != null && beast.Progress.Level >= threshold)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
