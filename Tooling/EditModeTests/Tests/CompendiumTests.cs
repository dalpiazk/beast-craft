using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The compendium (docs/design/compendium-achievements.md): a pure derived view over save state —
    /// per-beast entries (owned, offered by a pending Kinship choice, or unknown; whether it joined
    /// through Kinship, and which site), the lore entries found, and the completion counts over both
    /// plus the Kinship sites claimed. No storage of its own.
    /// </summary>
    public class CompendiumTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static DiscoveryContent Discovery
        {
            get { return Content.Discovery; }
        }

        [Test]
        public void BeastEntries_CoverTheWholeRoster_OwnedFirst()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            List<CompendiumBeastEntry> entries = CompendiumRules.BeastEntries(save, Discovery);
            Assert.AreEqual(Content.Species.Count, entries.Count, "one entry per roster species");
            Assert.AreEqual(3, entries.Count(e => e.State == CompendiumBeastState.Owned));
            CollectionAssert.AreEquivalent(new[] { "golem", "phoenix", "griffin" }, entries.Where(e => e.State == CompendiumBeastState.Owned).Select(e => e.SpeciesId));
            Assert.IsTrue(entries.Where(e => e.State == CompendiumBeastState.Owned).All(e => !e.JoinedThroughKinship), "the Hearthglen trio did not join through Kinship");
            Assert.IsTrue(entries.Where(e => e.State != CompendiumBeastState.Owned).All(e => e.KinshipSiteId == string.Empty));
        }

        [Test]
        public void BeastEntries_MarkAPendingKinshipOfferAsOffered_AndAJoinAsThroughKinship()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 6);
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 101).Success);
            PointOfInterest site = DiscoveryRules.PointsOnMap(save, Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
            KinshipTests.WalkTo(save, site.Layer);
            KinshipResult won = KinshipRules.ResolveTrial(save, Discovery, site.PoiId, BattleOutcome.PlayerVictory, new[] { "b1", "b2", "b3" }, false);
            Assert.AreEqual(KinshipOutcome.Won, won.Outcome);

            List<CompendiumBeastEntry> offered = CompendiumRules.BeastEntries(save, Discovery);
            foreach (string speciesId in won.Offer)
            {
                Assert.AreEqual(CompendiumBeastState.Offered, offered.Single(e => e.SpeciesId == speciesId).State, speciesId);
            }

            string chosen = won.Offer[0];
            KinshipResult joined = KinshipRules.Choose(save, Discovery, chosen);
            Assert.IsTrue(joined.Success);

            List<CompendiumBeastEntry> after = CompendiumRules.BeastEntries(save, Discovery);
            CompendiumBeastEntry entry = after.Single(e => e.SpeciesId == chosen);
            Assert.AreEqual(CompendiumBeastState.Owned, entry.State);
            Assert.IsTrue(entry.JoinedThroughKinship);
            Assert.AreEqual(site.RefId, entry.KinshipSiteId);

            // The beast not chosen goes back to unknown (a live preview only, never stored).
            string other = won.Offer.Count > 1 ? won.Offer[1] : null;
            if (other != null)
            {
                Assert.AreEqual(CompendiumBeastState.Unknown, after.Single(e => e.SpeciesId == other).State);
            }
        }

        [Test]
        public void LoreEntries_ListEveryEntry_FoundFlagsMatchTheSave()
        {
            PlayerSave save = KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, 3);
            List<CompendiumLoreEntry> before = CompendiumRules.LoreEntries(save, Discovery);
            Assert.AreEqual(Discovery.Library.Data.Lore.Length, before.Count);
            Assert.IsTrue(before.All(e => !e.Found));

            string loreId = Discovery.Library.Data.Lore[0].LoreId;
            DiscoveryProgress.AddOnce(save.Discovery.LoreIds, loreId);
            List<CompendiumLoreEntry> after = CompendiumRules.LoreEntries(save, Discovery);
            Assert.IsTrue(after.Single(e => e.LoreId == loreId).Found);
            Assert.AreEqual(1, after.Count(e => e.Found));
            Assert.AreEqual(Discovery.Library.Data.Lore[0].Title, after.Single(e => e.LoreId == loreId).Title);
        }

        [Test]
        public void Completion_CombinesBeastsLoreAndKinship_100OnlyWhenAllThreeAreComplete()
        {
            PlayerSave save = TestSaves.SixStarters(Content, 5);
            CompendiumCompletion start = CompendiumRules.Completion(save, Discovery);
            Assert.AreEqual(6, start.BeastsOwned);
            Assert.AreEqual(Content.Species.Count, start.BeastsTotal);
            Assert.AreEqual(0, start.LoreFound);
            Assert.AreEqual(Discovery.Library.Data.Lore.Length, start.LoreTotal);
            Assert.AreEqual(0, start.KinshipClaimed);
            Assert.AreEqual(Discovery.Library.Sites.Count, start.KinshipTotal);
            Assert.Less(start.Percent, 100);

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

            CompendiumCompletion done = CompendiumRules.Completion(save, Discovery);
            Assert.AreEqual(100, done.Percent);
            Assert.AreEqual(done.BeastsTotal, done.BeastsOwned);
            Assert.AreEqual(done.LoreTotal, done.LoreFound);
            Assert.AreEqual(done.KinshipTotal, done.KinshipClaimed);
        }
    }
}
