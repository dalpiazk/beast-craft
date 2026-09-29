using System.Collections.Generic;
using System.Linq;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Collector persona (docs/design/compendium-achievements.md): the authored
    /// <c>achievements.json</c> and <see cref="AchievementLibraryValidator"/>, and
    /// <see cref="AchievementRules"/> — deterministic (no RNG), no combat power (a title only),
    /// idempotent, and hooked into <see cref="DiscoveryRules.Visit"/>, <see cref="DiscoveryRules.TryComplete"/>,
    /// <see cref="KinshipRules.Choose"/> and a region's first boss clear (<see cref="CampaignRules.ResolveBattle"/>).
    /// </summary>
    public class AchievementTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static DiscoveryContent Discovery
        {
            get { return Content.Discovery; }
        }

        internal static AchievementLibraryData LoadAchievements()
        {
            return EncounterContentTests.Load<AchievementLibraryData>(AchievementLibraryData.ProjectRelativePath);
        }

        private static AchievementContent Synthetic(params AchievementData[] achievements)
        {
            AchievementLibraryData data = new AchievementLibraryData { SchemaVersion = 1, Achievements = achievements };
            return new AchievementContent { Library = AchievementLibrary.Build(data), Discovery = Discovery };
        }

        private static PlayerSave FreshSave()
        {
            return KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
        }

        [Test]
        public void AuthoredLibrary_IsValid_AndHasBetween15And25Achievements()
        {
            AchievementLibraryData data = LoadAchievements();
            List<string> species = new List<string>();
            foreach (Creatures.CreatureSpeciesSO beast in Content.Species)
            {
                species.Add(beast.SpeciesId);
            }

            List<string> errors = AchievementLibraryValidator.Validate(data, CampaignMapTests.LoadRegions(), DiscoveryTests.ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath), species);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.That(data.Achievements.Length, Is.InRange(15, 25));
            foreach (AchievementData achievement in data.Achievements)
            {
                Assert.IsNotEmpty(achievement.TitleText, achievement.AchievementId);
            }
        }

        [Test]
        public void Validator_CatchesAuthoringMistakes()
        {
            AchievementLibraryData data = LoadAchievements();
            data.Achievements[0].Kind = "NotAKind";
            data.Achievements[1].AchievementId = data.Achievements[2].AchievementId;
            data.Achievements[3].TitleId = data.Achievements[4].TitleId;
            data.Achievements[5].RegionId = "r99";
            data.Achievements[5].Kind = AchievementKinds.BossCleared;
            data.Achievements[6].Kind = AchievementKinds.AllKinshipClaimed;
            data.Achievements[6].RegionId = "r01";
            data.Achievements[7].Kind = AchievementKinds.AvatarLevel;
            data.Achievements[7].RegionId = string.Empty;
            data.Achievements[7].Threshold = 0;
            data.Achievements[8].TitleText = "Beast's Friend";
            data.Achievements[9].RegionId = string.Empty;

            List<string> errors = AchievementLibraryValidator.Validate(data, CampaignMapTests.LoadRegions(), DiscoveryTests.ReadJson<DiscoveryLibraryData>(DiscoveryLibraryData.ProjectRelativePath), null);
            StringAssert.Contains("NotAKind", string.Join("\n", errors));
            StringAssert.Contains("used twice in achievements.json", string.Join("\n", errors));
            StringAssert.Contains("TitleId must be", string.Join("\n", errors));
            StringAssert.Contains("mainline region with a boss", string.Join("\n", errors));
            StringAssert.Contains("does not use RegionId", string.Join("\n", errors));
            StringAssert.Contains("needs a RegionId", string.Join("\n", errors));
            StringAssert.Contains("no apostrophe", string.Join("\n", errors));
        }

        [Test]
        public void Evaluate_IsIdempotent_AndAwardsATitleOnlyOnce()
        {
            PlayerSave save = FreshSave();
            AchievementData def = new AchievementData
            {
                AchievementId = "test_beasts_3",
                DisplayName = "Trio",
                Kind = AchievementKinds.BeastsOwned,
                Threshold = 3,
                TitleId = "title_test_trio",
                TitleText = "Trio Keeper"
            };
            AchievementContent content = Synthetic(def);

            List<AchievementData> first = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual("test_beasts_3", first[0].AchievementId);
            Assert.IsTrue(save.Achievements.HasEarned("test_beasts_3"));
            Assert.IsTrue(save.Achievements.HasTitle("title_test_trio"));

            List<AchievementData> second = AchievementRules.Evaluate(save, content);
            Assert.IsEmpty(second, "already earned: nothing new");
            Assert.AreEqual(1, save.Achievements.EarnedIds.Count, "never earned twice");
            Assert.AreEqual(1, save.Achievements.OwnedTitleIds.Count, "the title is owned once");
        }

        [Test]
        public void Evaluate_IsPureAndDeterministic_NoRngNoStats()
        {
            AchievementContent content = Synthetic(LoadAchievements().Achievements);

            List<AchievementData> a = AchievementRules.Evaluate(FreshSave(), content);
            List<AchievementData> b = AchievementRules.Evaluate(FreshSave(), content);
            CollectionAssert.AreEqual(ExtractIdList(a), ExtractIdList(b), "the same save state always earns the same achievements, every time");

            // No stat is ever touched: only Achievements (and its own titles) changed.
            PlayerSave save = FreshSave();
            int levelBefore = save.Beasts[0].Progress.Level;
            AchievementRules.Evaluate(save, content);
            Assert.AreEqual(levelBefore, save.Beasts[0].Progress.Level, "a title never changes a beast's level or stats");
        }

        [Test]
        public void BossCleared_AndAllBossesCleared()
        {
            PlayerSave save = FreshSave();
            AchievementData first = new AchievementData { AchievementId = "a", Kind = AchievementKinds.BossCleared, RegionId = "r01", TitleId = "t1", TitleText = "One" };
            AchievementData all = new AchievementData { AchievementId = "b", Kind = AchievementKinds.AllBossesCleared, TitleId = "t2", TitleText = "All" };
            AchievementContent content = Synthetic(first, all);

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));
            save.Campaign.FindRegion("r01").BossCleared = true;
            List<AchievementData> earned = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, earned.Count, "only r01 cleared so far");
            Assert.AreEqual("a", earned[0].AchievementId);

            foreach (RegionData region in Content.Campaign.Regions)
            {
                if (region.IsPostGame)
                {
                    continue;
                }

                save.Campaign.Unlock(region.RegionId);
                save.Campaign.FindRegion(region.RegionId).BossCleared = true;
            }

            List<AchievementData> earnedAll = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, earnedAll.Count);
            Assert.AreEqual("b", earnedAll[0].AchievementId);
        }

        [Test]
        public void RegionExplored_AndAllRegionsExplored()
        {
            PlayerSave save = FreshSave();
            AchievementData r01 = new AchievementData { AchievementId = "a", Kind = AchievementKinds.RegionExplored, RegionId = "r01", TitleId = "t1", TitleText = "R1" };
            AchievementData all = new AchievementData { AchievementId = "b", Kind = AchievementKinds.AllRegionsExplored, TitleId = "t2", TitleText = "All" };
            AchievementContent content = Synthetic(r01, all);

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));

            // A region reads 100% once its boss is beaten and every stage cleared (no points of
            // interest have been laid out yet at DiscoverySeed 0, so none are outstanding either).
            save.Campaign.FindRegion("r01").BossCleared = true;
            save.Campaign.FindRegion("r01").StagesCleared = 4;
            List<AchievementData> earned = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, earned.Count);
            Assert.AreEqual("a", earned[0].AchievementId);

            foreach (RegionDiscoveryData region in Discovery.Library.Data.Regions)
            {
                save.Campaign.Unlock(region.RegionId);
                RegionProgress progress = save.Campaign.FindRegion(region.RegionId);
                progress.BossCleared = true;
                progress.StagesCleared = 4;
            }

            List<AchievementData> earnedAll = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, earnedAll.Count);
            Assert.AreEqual("b", earnedAll[0].AchievementId);
        }

        [Test]
        public void KinshipSitesClaimed_AndAllClaimed()
        {
            PlayerSave save = FreshSave();
            AchievementData one = new AchievementData { AchievementId = "a", Kind = AchievementKinds.KinshipSitesClaimed, Threshold = 1, TitleId = "t1", TitleText = "One" };
            AchievementData all = new AchievementData { AchievementId = "b", Kind = AchievementKinds.AllKinshipClaimed, TitleId = "t2", TitleText = "All" };
            AchievementContent content = Synthetic(one, all);

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));
            DiscoveryProgress.AddOnce(save.Discovery.ClaimedKinshipIds, Discovery.Library.Sites[0].SiteId);
            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(AchievementRules.Evaluate(save, content)));

            foreach (KinshipSiteData site in Discovery.Library.Sites)
            {
                DiscoveryProgress.AddOnce(save.Discovery.ClaimedKinshipIds, site.SiteId);
            }

            CollectionAssert.AreEquivalent(new[] { "b" }, ExtractIdList(AchievementRules.Evaluate(save, content)));
        }

        [Test]
        public void RegionLoreComplete_AndAllLoreFound()
        {
            PlayerSave save = FreshSave();
            AchievementData r01 = new AchievementData { AchievementId = "a", Kind = AchievementKinds.RegionLoreComplete, RegionId = "r01", TitleId = "t1", TitleText = "R1" };
            AchievementData all = new AchievementData { AchievementId = "b", Kind = AchievementKinds.AllLoreFound, TitleId = "t2", TitleText = "All" };
            AchievementContent content = Synthetic(r01, all);

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));
            foreach (LoreEntryData lore in Discovery.Library.Data.Lore)
            {
                if (lore.RegionId == "r01")
                {
                    DiscoveryProgress.AddOnce(save.Discovery.LoreIds, lore.LoreId);
                }
            }

            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(AchievementRules.Evaluate(save, content)));

            foreach (LoreEntryData lore in Discovery.Library.Data.Lore)
            {
                DiscoveryProgress.AddOnce(save.Discovery.LoreIds, lore.LoreId);
            }

            CollectionAssert.AreEquivalent(new[] { "b" }, ExtractIdList(AchievementRules.Evaluate(save, content)));
        }

        [Test]
        public void AvatarLevel_AndBeastLevel()
        {
            PlayerSave save = FreshSave();
            AchievementData avatar = new AchievementData { AchievementId = "a", Kind = AchievementKinds.AvatarLevel, Threshold = 10, TitleId = "t1", TitleText = "A" };
            AchievementData beast = new AchievementData { AchievementId = "b", Kind = AchievementKinds.BeastLevel, Threshold = 20, TitleId = "t2", TitleText = "B" };
            AchievementContent content = Synthetic(avatar, beast);

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));
            save.Avatar.Level = 10;
            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(AchievementRules.Evaluate(save, content)));

            save.Beasts[0].Progress.Level = 20;
            CollectionAssert.AreEquivalent(new[] { "b" }, ExtractIdList(AchievementRules.Evaluate(save, content)));
        }

        [Test]
        public void CompendiumPercent_ReadsCompendiumRules()
        {
            PlayerSave save = TestSaves.SixStarters(Content, 5);
            foreach (string species in KinshipRules.RosterOrder(Content.Species))
            {
                if (!KinshipRules.OwnedSpecies(save).Contains(species))
                {
                    StarterPicks.AddBeast(save, Content.SkillLibrary, species, 5);
                }
            }

            foreach (LoreEntryData lore in Discovery.Library.Data.Lore)
            {
                DiscoveryProgress.AddOnce(save.Discovery.LoreIds, lore.LoreId);
            }

            foreach (KinshipSiteData site in Discovery.Library.Sites)
            {
                DiscoveryProgress.AddOnce(save.Discovery.ClaimedKinshipIds, site.SiteId);
            }

            AchievementData full = new AchievementData { AchievementId = "a", Kind = AchievementKinds.CompendiumPercent, Threshold = 100, TitleId = "t1", TitleText = "Full" };
            AchievementContent content = Synthetic(full);
            Assert.AreEqual(100, CompendiumRules.Completion(save, Discovery).Percent);
            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(AchievementRules.Evaluate(save, content)));
        }

        [Test]
        public void HookedIntoDiscoveryVisit_KinshipChoose_AndCompletion()
        {
            PlayerSave save = FreshSave();
            AchievementData claim = new AchievementData
            {
                AchievementId = "a",
                Kind = AchievementKinds.KinshipSitesClaimed,
                Threshold = 1,
                TitleId = "t1",
                TitleText = "Claimed"
            };
            AchievementContent achievements = Synthetic(claim);
            DiscoveryContent wired = new DiscoveryContent
            {
                Library = Discovery.Library,
                Regions = Discovery.Regions,
                Cosmetics = Discovery.Cosmetics,
                Roster = Discovery.Roster,
                Skills = Discovery.Skills,
                Encounters = Discovery.Encounters,
                Enemies = Discovery.Enemies,
                Achievements = achievements
            };

            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 101).Success);
            PointOfInterest site = DiscoveryRules.PointsOnMap(save, wired).Single(p => p.Kind == PoiKind.KinshipSite);
            KinshipTests.WalkTo(save, site.Layer);
            KinshipRules.ResolveTrial(save, wired, site.PoiId, Battle.BattleOutcome.PlayerVictory, new[] { "b1", "b2", "b3" }, false);
            KinshipResult joined = KinshipRules.Choose(save, wired, "treant");
            Assert.IsTrue(joined.Success);
            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(joined.TitlesEarned));
            Assert.IsTrue(save.Achievements.HasEarned("a"));
        }

        [Test]
        public void ResolveBattle_EvaluatesAchievements_OnEveryResolvedBattle_NotOnlyABossClear()
        {
            PlayerSave save = FreshSave();
            AchievementData def = new AchievementData { AchievementId = "a", Kind = AchievementKinds.BeastsOwned, Threshold = 3, TitleId = "t1", TitleText = "Trio" };
            AchievementContent achievements = Synthetic(def);
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 3).Success);
            MapRun run = save.Campaign.ActiveRun;
            MapNode first = CampaignRules.Choices(run)[0];
            Assert.IsTrue(first.IsBattle, "a plain battle, not the boss or a gate");

            CampaignResult result = CampaignRules.ResolveBattle(save, Content.Campaign, first.NodeId, Battle.BattleOutcome.PlayerVictory, null, achievements);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(CampaignOutcome.Cleared, result.Outcome, "an ordinary node clear, not a stage or region milestone");
            CollectionAssert.AreEquivalent(new[] { "a" }, ExtractIdList(result.TitlesEarned));
            Assert.IsTrue(save.Achievements.HasEarned("a"));

            // Idempotent: clearing the next node does not earn it again.
            MapNode second = CampaignRules.Choices(run)[0];
            CampaignResult again = CampaignRules.ResolveBattle(save, Content.Campaign, second.NodeId, Battle.BattleOutcome.PlayerVictory, null, achievements);
            Assert.IsEmpty(again.TitlesEarned);
        }

        [Test]
        public void ResolveBattle_WithoutAchievements_LeavesTitlesEarnedEmpty_UnchangedFromBeforeTheParameterExisted()
        {
            PlayerSave save = FreshSave();
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 3).Success);
            MapRun run = save.Campaign.ActiveRun;
            MapNode first = CampaignRules.Choices(run)[0];

            CampaignResult result = CampaignRules.ResolveBattle(save, Content.Campaign, first.NodeId, Battle.BattleOutcome.PlayerVictory);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsEmpty(result.TitlesEarned);
        }

        private static List<string> ExtractIdList(List<AchievementData> list)
        {
            List<string> ids = new List<string>();
            foreach (AchievementData a in list)
            {
                ids.Add(a.AchievementId);
            }

            return ids;
        }
    }
}
