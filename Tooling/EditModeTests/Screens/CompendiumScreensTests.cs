using System.Collections.Generic;
using System.Linq;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Progression;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// PR B's screens (docs/design/compendium-achievements.md): the compendium screen's view-model
    /// (beast entries with the Kinship-site hint, lore entries locked or found, completion), the
    /// achievements screen's view-model (condition text, the title picker, the avatar's titled
    /// display name), the look-token shop's view-model (the pool, affordability, spending), the
    /// toast text titles and tokens add to a reward's own message, and achievements retroactively
    /// earned once at session start.
    /// </summary>
    public class CompendiumScreensTests
    {
        private const int MapSeed = 424242;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession Trio(int level = 6)
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => MapSeed);
            session.StartWith(KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, level));
            return session;
        }

        // ------------------------------------------------------------------ compendium

        [Test]
        public void CompendiumViewModel_ListsEveryBeastAndLoreEntry_MatchesCoreCompletion()
        {
            GameSession session = Trio();
            CompendiumViewModel model = new CompendiumViewModel(session);

            Assert.AreEqual(Content.Species.Count, model.Beasts.Count);
            Assert.AreEqual(3, model.Beasts.Count(b => b.State == CompendiumBeastState.Owned));
            Assert.IsTrue(model.Beasts.Where(b => b.State == CompendiumBeastState.Unknown).All(b => b.Name == "???" && b.Hint == Content.Text.Get(RosterViewModel.SilhouetteHintKey)));
            Assert.AreEqual(Content.Discovery.Library.Data.Lore.Length, model.Lore.Count);
            Assert.IsTrue(model.Lore.All(l => !l.Found && l.Title == CompendiumViewModel.LockedTitle && l.Text == Content.Text.Get(CompendiumViewModel.LockedTextKey)));

            CompendiumCompletion expected = CompendiumRules.Completion(session.Save, session.Content.Discovery);
            Assert.AreEqual(expected.Percent, model.Completion.Percent);
            Assert.AreEqual(expected.BeastsOwned, model.Completion.BeastsOwned);
            Assert.AreEqual(expected.LoreTotal, model.Completion.LoreTotal);
        }

        [Test]
        public void CompendiumViewModel_OwnedThroughKinship_NamesTheSite_AndOfferedShowsALivePreview()
        {
            GameSession session = Trio(8);
            PointOfInterest site = DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
            KinshipTests.WalkTo(session.Save, site.Layer);
            KinshipResult won = KinshipRules.ResolveTrial(session.Save, session.Content.Discovery, site.PoiId, BeastCraft.Battle.BattleOutcome.PlayerVictory,
                                                           session.Save.Beasts.ConvertAll(b => b.BeastId), false);
            Assert.AreEqual(KinshipOutcome.Won, won.Outcome);

            CompendiumViewModel offered = new CompendiumViewModel(session);
            string offeredSpecies = won.Offer[0];
            Assert.AreEqual(CompendiumBeastState.Offered, offered.Beasts.Single(b => b.SpeciesId == offeredSpecies).State);
            Assert.AreNotEqual("???", offered.Beasts.Single(b => b.SpeciesId == offeredSpecies).Name, "an offered beast is a live preview, not a silhouette");

            Assert.IsTrue(session.ChooseKinship(offeredSpecies).Success);
            CompendiumViewModel after = new CompendiumViewModel(session);
            CompendiumBeastRow joined = after.Beasts.Single(b => b.SpeciesId == offeredSpecies);
            Assert.AreEqual(CompendiumBeastState.Owned, joined.State);
            string siteName = session.Content.Discovery.Library.Site(site.RefId)?.Name ?? "a kinship stone";
            Assert.AreEqual("Found through Kinship at " + siteName + ".", joined.Hint);

            // The Hearthglen trio joined through no site: no hint.
            Assert.AreEqual(string.Empty, after.Beasts.Single(b => b.SpeciesId == "golem").Hint);
        }

        // ------------------------------------------------------------------ achievements and titles

        [Test]
        public void AchievementsViewModel_ListsEveryAchievement_WithConditionTextAndNoTitleFirst()
        {
            GameSession session = Trio();
            AchievementsViewModel model = new AchievementsViewModel(session);

            Assert.AreEqual(session.Content.Achievements.Library.All.Count, model.Achievements.Count);
            Assert.IsTrue(model.Achievements.All(a => !string.IsNullOrEmpty(a.ConditionText)), "every kind has words");
            Assert.IsTrue(model.Achievements.All(a => !a.Earned), "a fresh save has earned nothing yet");
            Assert.AreEqual(0, model.EarnedCount);
            Assert.AreEqual(1, model.Titles.Count, "no title owned yet: only \"No title\"");
            Assert.IsTrue(model.Titles[0].Equipped);
            Assert.AreEqual(string.Empty, model.Titles[0].TitleId);
            Assert.AreEqual("Beastbinder", model.AvatarDisplayName, "no title equipped: the plain name");
        }

        [Test]
        public void AchievementsViewModel_Equip_SetsTheEquippedTitle_UpdatesTheAvatarName_AndAutosaves()
        {
            GameSession session = Trio();
            AchievementData first = session.Content.Achievements.Library.All[0];
            session.Save.Achievements.EarnedIds.Add(first.AchievementId);
            session.Save.Achievements.OwnedTitleIds.Add(first.TitleId);
            int saves = session.AutosaveCount;

            AchievementsViewModel model = new AchievementsViewModel(session);
            Assert.AreEqual(2, model.Titles.Count);
            Assert.IsTrue(model.Achievements.Single(a => a.AchievementId == first.AchievementId).Earned);

            Assert.IsTrue(model.Equip(first.TitleId));
            Assert.AreEqual(first.TitleId, session.Save.Achievements.EquippedTitleId);
            Assert.Greater(session.AutosaveCount, saves);
            Assert.AreEqual(first.TitleText + " Beastbinder", model.AvatarDisplayName);
            Assert.AreEqual(first.TitleText, AchievementsViewModel.EquippedTitleTextOf(session.Save, session.Content.Achievements.Library));

            Assert.IsTrue(model.Equip(string.Empty), "\"\" clears it");
            Assert.AreEqual(string.Empty, session.Save.Achievements.EquippedTitleId);
            Assert.AreEqual("Beastbinder", model.AvatarDisplayName);
        }

        [Test]
        public void AchievementsViewModel_Equip_RefusesATitleNotOwned()
        {
            GameSession session = Trio();
            AchievementsViewModel model = new AchievementsViewModel(session);

            Assert.IsFalse(model.Equip("not_owned"));
            Assert.AreEqual(string.Empty, session.Save.Achievements.EquippedTitleId);
        }

        // ------------------------------------------------------------------ look-token shop

        [Test]
        public void LookTokenShopViewModel_ListsThePool_OwnedAndAffordableFlags()
        {
            GameSession session = Trio();
            session.Save.LookTokens = 0;
            LookTokenShopViewModel model = new LookTokenShopViewModel(session);

            List<CosmeticOption> pool = session.Content.Economy.Cosmetics.TokenPool();
            Assert.IsNotEmpty(pool, "the shipped pool is not empty");
            Assert.AreEqual(pool.Count, model.Looks.Count);
            Assert.IsTrue(model.Looks.All(l => !l.Owned));
            Assert.IsTrue(model.Looks.All(l => !l.CanAfford), "no tokens yet");

            session.Save.LookTokens = model.Looks[0].Price;
            model.Refresh();
            Assert.AreEqual(model.Looks[0].Price, model.Balance);
            Assert.IsTrue(model.Looks[0].CanAfford);
        }

        [Test]
        public void LookTokenShopViewModel_Buy_SpendsTokens_RefusesInsufficient_AndAutosavesOnSuccess()
        {
            GameSession session = Trio();
            LookTokenShopViewModel model = new LookTokenShopViewModel(session);
            LookTokenRow look = model.Looks[0];
            session.Save.LookTokens = look.Price - 1;
            model.Refresh();

            Assert.AreEqual(LookTokenResult.InsufficientTokens, model.Buy(look.Key));
            Assert.AreEqual(look.Price - 1, session.Save.LookTokens);
            Assert.IsFalse(model.Looks.Single(l => l.Key == look.Key).Owned);

            session.Save.LookTokens = look.Price;
            model.Refresh();
            int saves = session.AutosaveCount;
            Assert.AreEqual(LookTokenResult.Unlocked, model.Buy(look.Key));
            Assert.AreEqual(0, session.Save.LookTokens);
            Assert.Greater(session.AutosaveCount, saves);
            Assert.IsTrue(model.Looks.Single(l => l.Key == look.Key).Owned);
        }

        // ------------------------------------------------------------------ toasts and session-start retroactive earn

        [Test]
        public void ExtraRewardText_FormatsTitlesAndTokens_EmptyWhenNothing()
        {
            Assert.AreEqual(string.Empty, GameSession.ExtraRewardText(Content.Text, null, 0));
            Assert.AreEqual(string.Empty, GameSession.ExtraRewardText(Content.Text, new List<AchievementData>(), 0));

            AchievementData title = new AchievementData { AchievementId = "a", TitleId = "t", TitleText = "Trio" };
            Assert.AreEqual(" You earned the title \"Trio\".", GameSession.ExtraRewardText(Content.Text, new List<AchievementData> { title }, 0));
            Assert.AreEqual(" You earned 3 look tokens.", GameSession.ExtraRewardText(Content.Text, null, 3));
            Assert.AreEqual(" You earned 1 look token.", GameSession.ExtraRewardText(Content.Text, null, 1));
            Assert.AreEqual(" You earned the title \"Trio\" and 3 look tokens.", GameSession.ExtraRewardText(Content.Text, new List<AchievementData> { title }, 3));
        }

        [Test]
        public void Continue_RetroactivelyEarnsAchievements_QueuesOneToast_AndPersists()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession setup = new GameSession(Content, storage, () => MapSeed);
            setup.StartWith(TestSaves.SixStarters(Content, 5));
            Assert.IsTrue(setup.Save.Achievements.HasEarned("beasts_5"), "New Game already evaluates achievements once");

            // Simulate an older save saved before achievements ever evaluated (e.g. migrated from schema 8).
            setup.Save.Achievements.EarnedIds.Clear();
            setup.Save.Achievements.OwnedTitleIds.Clear();
            Assert.IsTrue(setup.Autosave(AutosaveReason.Results));

            GameSession resumed = new GameSession(Content, storage, () => MapSeed);
            LoadOutcome outcome = resumed.Continue();
            Assert.IsTrue(outcome.Success, outcome.Message);

            Assert.IsTrue(resumed.Save.Achievements.HasEarned("beasts_5"), "retroactively earned on Continue");
            Assert.AreEqual(1, resumed.PendingToasts.Count);
            StringAssert.Contains("New title earned", resumed.PendingToasts[0]);
            StringAssert.Contains(Content.Achievements.Library.Get("beasts_5").TitleText, resumed.PendingToasts[0]);

            // Persisted without a further explicit autosave: a fresh load already has it.
            GameSession reloaded = new GameSession(Content, storage, () => MapSeed);
            Assert.IsTrue(reloaded.Continue().Success);
            Assert.IsTrue(reloaded.Save.Achievements.HasEarned("beasts_5"));
            Assert.IsEmpty(reloaded.PendingToasts, "nothing new to earn the second time");
        }

        // ------------------------------------------------------------------ Kinship's "last beast chooses you" framing

        [Test]
        public void KinshipPickViewModel_TwoBeastOffer_UsesTheChooseFraming_NotSolo()
        {
            GameSession session = Trio(8);
            PointOfInterest site = DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
            KinshipTests.WalkTo(session.Save, site.Layer);
            KinshipRules.ResolveTrial(session.Save, session.Content.Discovery, site.PoiId, BeastCraft.Battle.BattleOutcome.PlayerVictory,
                                      session.Save.Beasts.ConvertAll(b => b.BeastId), false);

            KinshipPickViewModel pick = new KinshipPickViewModel(session);
            Assert.AreEqual(2, pick.Options.Count, "the first site of a 3-beast save offers two");
            Assert.IsFalse(pick.SoloOffer);
            Assert.AreEqual("Two beasts answer", pick.Title);
            StringAssert.Contains("choose who joins you", pick.Subtitle);
            StringAssert.DoesNotContain("the last beast chooses you", pick.Subtitle.ToLowerInvariant());
        }
    }
}
