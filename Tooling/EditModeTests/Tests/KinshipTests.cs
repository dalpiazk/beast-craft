using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Kinship (docs/design/kinship-discovery.md): seven sites over r01-r06, each offering two
    /// not-yet-owned beasts (the region's theme first), every beast reachable by r06 whatever the
    /// Hearthglen trio and whichever beast is chosen, the recruit's level, the trial flow and old saves.
    /// </summary>
    public class KinshipTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static DiscoveryContent Discovery
        {
            get { return Content.Discovery; }
        }

        /// <summary>Every one-per-stance trio of the roster (the Hearthglen picks).</summary>
        internal static List<string[]> Trios()
        {
            List<CreatureSpeciesSO> roster = Content.Species;
            List<string[]> trios = new List<string[]>();
            foreach (CreatureSpeciesSO v in roster.Where(s => s.Stance == CombatStance.Vanguard))
            {
                foreach (CreatureSpeciesSO r in roster.Where(s => s.Stance == CombatStance.Ranged))
                {
                    foreach (CreatureSpeciesSO k in roster.Where(s => s.Stance == CombatStance.Skirmisher))
                    {
                        trios.Add(new[] { v.SpeciesId, r.SpeciesId, k.SpeciesId });
                    }
                }
            }

            return trios;
        }

        [Test]
        public void Sites_AreSevenOverTheFirstSixRegions_TwoInTheSixth()
        {
            IReadOnlyList<KinshipSiteData> sites = Discovery.Library.Sites;
            Assert.AreEqual(7, sites.Count);
            CollectionAssert.AreEqual(new[] { "r01", "r02", "r03", "r04", "r05", "r06", "r06" }, sites.Select(s => s.RegionId).ToArray());
            Assert.AreEqual(10, Content.Species.Count, "3 Hearthglen picks + 7 Kinship recruits = the whole roster");
            Assert.AreEqual(30, Trios().Count);
            foreach (KinshipSiteData site in sites)
            {
                Assert.IsNotNull(Content.Encounters.GetTemplate(site.TemplateId), site.SiteId);
                Assert.AreEqual(site.Preferred[0], site.Preferred[0].ToLowerInvariant());
            }
        }

        [Test]
        public void EveryTrio_EveryChoicePath_ReachesAllTenByR06_AlwaysOfferingTwoUnowned()
        {
            List<string> order = KinshipRules.RosterOrder(Content.Species);
            IReadOnlyList<KinshipSiteData> sites = Discovery.Library.Sites;
            int paths = 0;
            foreach (string[] trio in Trios())
            {
                // Both orders of r06's two sites (the player may claim either first), every choice at every site.
                foreach (bool swapLast in new[] { false, true })
                {
                    List<KinshipSiteData> sequence = sites.ToList();
                    if (swapLast)
                    {
                        (sequence[5], sequence[6]) = (sequence[6], sequence[5]);
                    }

                    for (int mask = 0; mask < 1 << sequence.Count; mask++)
                    {
                        HashSet<string> owned = new HashSet<string>(trio, StringComparer.Ordinal);
                        for (int i = 0; i < sequence.Count; i++)
                        {
                            List<string> offer = KinshipRules.Offer(sequence[i], owned, order);
                            int unowned = order.Count(id => !owned.Contains(id));
                            string at = string.Join("/", trio) + " site " + sequence[i].SiteId;
                            Assert.AreEqual(Math.Min(KinshipRules.OfferSize, unowned), offer.Count, at + ": two while two are left");
                            Assert.AreEqual(offer.Count, offer.Distinct().Count(), at);
                            Assert.IsFalse(offer.Any(owned.Contains), at + ": never an owned beast");
                            Assert.IsTrue(owned.Add(offer[Math.Min(offer.Count - 1, (mask >> i) & 1)]), at);
                        }

                        CollectionAssert.AreEquivalent(order, owned, string.Join("/", trio) + ": all ten owned after the seven sites");
                        paths++;
                    }
                }
            }

            Assert.AreEqual(30 * 2 * 128, paths);
        }

        [Test]
        public void Offer_PrefersTheRegionTheme_ThenFallsBackInRosterOrder()
        {
            List<string> order = KinshipRules.RosterOrder(Content.Species);
            KinshipSiteData r01 = Discovery.Library.Site("kin_r01");
            CollectionAssert.AreEqual(new[] { "treant", "kirin" }, KinshipRules.Offer(r01, new HashSet<string> { "golem", "phoenix", "griffin" }, order));
            CollectionAssert.AreEqual(new[] { "golem", "griffin" }, KinshipRules.Offer(r01, new HashSet<string> { "treant", "phoenix", "thunderbird" }, order));

            // Preferred exhausted: the roster's order fills the offer.
            HashSet<string> most = new HashSet<string>(order);
            most.Remove("leviathan");
            most.Remove("basilisk");
            CollectionAssert.AreEquivalent(new[] { "leviathan", "basilisk" }, KinshipRules.Offer(r01, most, order));
            most.Remove("basilisk");
            most.Add("basilisk");
            most.Add("leviathan");
            Assert.IsEmpty(KinshipRules.Offer(r01, most, order), "nothing left: no offer");
        }

        [Test]
        public void JoinLevel_IsTheFieldedMeanMinusThree_AtLeastOne()
        {
            PlayerSave save = PlayerSave.CreateNew();
            StarterPicks.AddBeast(save, Content.SkillLibrary, "golem", 10);
            StarterPicks.AddBeast(save, Content.SkillLibrary, "phoenix", 11);
            StarterPicks.AddBeast(save, Content.SkillLibrary, "griffin", 13);
            StarterPicks.AddBeast(save, Content.SkillLibrary, "treant", 2);
            Assert.AreEqual(8, KinshipRules.JoinLevel(save, new[] { "b1", "b2", "b3" }), "mean 11.33 -> 11, minus 3");
            Assert.AreEqual(3, KinshipRules.JoinLevel(save, new[] { "b1", "b4" }), "mean 6 minus 3");
            Assert.AreEqual(1, KinshipRules.JoinLevel(save, new[] { "b4" }), "never below 1");
        }

        [Test]
        public void TrialFlow_LossRetries_WinOffersTwo_ChoiceJoinsBelowTheTeamWithSkillsAtLevelOne()
        {
            PlayerSave save = TrioSave(new[] { "golem", "phoenix", "griffin" }, 6);
            Assert.IsTrue(CampaignRules.StartRun(save, Content.Campaign, "r01", 0, 101).Success);
            PointOfInterest site = DiscoveryRules.PointsOnMap(save, Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
            Assert.IsNull(KinshipRules.Challengeable(save, Discovery, site.PoiId, out _, out string hidden), "not seen before the player walks up to it");
            StringAssert.Contains("Gloam", hidden);

            WalkTo(save, site.Layer);
            Assert.IsNotNull(KinshipRules.Challengeable(save, Discovery, site.PoiId, out KinshipSiteData data, out string error), error);
            Assert.AreEqual("kin_r01", data.SiteId);
            Assert.IsNotNull(KinshipRules.PlanFor(Discovery, site), "the trial builds from its template");
            Assert.AreEqual(site.Level, KinshipRules.PlanFor(Discovery, site).Level);

            KinshipResult lost = KinshipRules.ResolveTrial(save, Discovery, site.PoiId, BattleOutcome.EnemyVictory, new[] { "b1", "b2", "b3" }, true);
            Assert.AreEqual(KinshipOutcome.Lost, lost.Outcome);
            Assert.AreEqual(1, save.Discovery.KinshipLosses);
            Assert.IsFalse(save.Discovery.HasPendingKinship);
            Assert.AreNotEqual(KinshipRules.BattleSeed(site, 0), KinshipRules.BattleSeed(site, 1), "every retry is a new battle");

            KinshipResult won = KinshipRules.ResolveTrial(save, Discovery, site.PoiId, BattleOutcome.PlayerVictory, new[] { "b1", "b2", "b3" }, false);
            Assert.AreEqual(KinshipOutcome.Won, won.Outcome);
            CollectionAssert.AreEqual(new[] { "treant", "kirin" }, won.Offer);
            Assert.IsFalse(won.SoloOffer, "two offered: not the last beast");
            Assert.AreEqual(3, won.JoinLevel, "team level 6, minus 3");
            Assert.IsTrue(won.BondMet, "a Vanguard (the golem) held the line");
            Assert.IsNull(KinshipRules.Challengeable(save, Discovery, site.PoiId, out _, out _), "a choice is pending");

            Assert.IsFalse(KinshipRules.Choose(save, Discovery, "leviathan").Success, "not offered here");
            KinshipResult joined = KinshipRules.Choose(save, Discovery, "kirin");
            Assert.AreEqual(KinshipOutcome.Joined, joined.Outcome);
            Assert.AreEqual(4, save.Beasts.Count);
            Assert.AreEqual("kirin", joined.Beast.Progress.SpeciesId);
            Assert.AreEqual(3, joined.Beast.Progress.Level);
            Assert.IsTrue(joined.Beast.Skills.Known.Count > 0 && joined.Beast.Skills.Known.TrueForAll(s => s.Level == 1), "skills start at level 1");
            Assert.IsTrue(save.Discovery.HasClaimed("kin_r01"));
            CollectionAssert.Contains(save.Discovery.LoreIds, "lore_kin_r01");
            Assert.IsTrue(save.Campaign.FindRegion("r01").HasFound(site.PoiId));
            Assert.IsFalse(save.Discovery.HasPendingKinship);
            Assert.IsNull(KinshipRules.Challengeable(save, Discovery, site.PoiId, out _, out _), "claimed");

            Assert.AreEqual("kin_r01", save.Discovery.FindKinshipJoin("kirin").SiteId, "the compendium's 'found through Kinship, at this site'");
            Assert.IsNull(save.Discovery.FindKinshipJoin("golem"), "a Hearthglen pick never joined through Kinship");
        }

        [Test]
        public void SoloOffer_IsTrueOnlyForAOneBeastOffer()
        {
            Assert.IsFalse(new KinshipResult { Offer = new List<string> { "golem", "kirin" } }.SoloOffer);
            Assert.IsTrue(new KinshipResult { Offer = new List<string> { "kirin" } }.SoloOffer, "the last beast chooses you (no eighth species)");
            Assert.IsFalse(new KinshipResult { Offer = new List<string>() }.SoloOffer);
        }

        [Test]
        public void BondCondition_IsFlavourReadOffTheTeam()
        {
            KinshipSiteData vanguard = new KinshipSiteData { BondCondition = "stance:Vanguard" };
            KinshipSiteData clean = new KinshipSiteData { BondCondition = "no_knockout" };
            KinshipSiteData nature = new KinshipSiteData { BondCondition = "element:Nature" };
            List<CreatureSpeciesSO> ranged = Content.Species.Where(s => s.Stance == CombatStance.Ranged).ToList();
            Assert.IsFalse(KinshipRules.BondMet(vanguard, ranged, false));
            Assert.IsTrue(KinshipRules.BondMet(vanguard, new List<CreatureSpeciesSO> { Content.Battle.GetSpecies("golem") }, true));
            Assert.IsTrue(KinshipRules.BondMet(clean, ranged, false));
            Assert.IsFalse(KinshipRules.BondMet(clean, ranged, true));
            Assert.IsTrue(KinshipRules.BondMet(nature, new List<CreatureSpeciesSO> { Content.Battle.GetSpecies("treant") }, true));
            Assert.IsTrue(KinshipRules.BondMet(new KinshipSiteData(), ranged, true), "no condition is always met");
        }

        [Test]
        public void SixBeastSave_IsNeverOfferedADuplicate_AndSitesWithNothingLeftBecomeLoreAndCacheStops()
        {
            PlayerSave save = TestSaves.SixStarters(Content, 50);
            List<string> order = KinshipRules.RosterOrder(Content.Species);
            HashSet<string> owned = KinshipRules.OwnedSpecies(save);
            int offering = 0;
            foreach (KinshipSiteData site in Discovery.Library.Sites)
            {
                List<string> offer = KinshipRules.Offer(site, owned, order);
                Assert.IsFalse(offer.Any(owned.Contains), site.SiteId);
                if (offer.Count > 0)
                {
                    offering++;
                    owned.Add(offer[0]);
                }
            }

            Assert.AreEqual(4, offering, "the four species a six-beast save lacks, one per site");
            Assert.AreEqual(10, owned.Count);

            // A site with nothing left: visiting it grants its lore and fallback cache and claims it.
            PlayerSave full = TestSaves.SixStarters(Content, 5);
            foreach (string species in order.Where(id => !KinshipRules.OwnedSpecies(full).Contains(id)).ToList())
            {
                StarterPicks.AddBeast(full, Content.SkillLibrary, species, 5);
            }

            Assert.IsTrue(CampaignRules.StartRun(full, Content.Campaign, "r01", 0, 55).Success);
            PointOfInterest point = DiscoveryRules.PointsOnMap(full, Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
            WalkTo(full, point.Layer);
            Assert.IsNull(KinshipRules.Challengeable(full, Discovery, point.PoiId, out _, out string none));
            StringAssert.Contains("No beast", none);
            int gold = full.Gold;
            DiscoveryResult visit = DiscoveryRules.Visit(full, Discovery, point.PoiId);
            Assert.IsTrue(visit.Success, visit.Error);
            Assert.IsTrue(visit.KinshipFallback);
            Assert.AreEqual("cache_kin_r01", visit.CacheId);
            Assert.AreEqual("lore_kin_r01", visit.LoreId);
            Assert.Greater(full.Gold, gold);
            Assert.IsTrue(full.Discovery.HasClaimed("kin_r01"));
            Assert.AreEqual(10, full.Beasts.Count, "no beast added");
        }

        /// <summary>A save that owns <paramref name="species"/> at <paramref name="level"/>, past Hearthglen, in r01.</summary>
        internal static PlayerSave TrioSave(string[] species, int level)
        {
            PlayerSave save = PlayerSave.CreateNew();
            StarterPicks.GrantAvatarDefaults(save, Content.SkillLibrary);
            foreach (string id in species)
            {
                StarterPicks.AddBeast(save, Content.SkillLibrary, id, level);
            }

            save.Tutorial.HearthglenCleared = true;
            return save;
        }

        /// <summary>Clears the expedition's lowest-id reachable location row by row until row <paramref name="layer"/> is cleared.</summary>
        internal static void WalkTo(PlayerSave save, int layer)
        {
            MapRun run = save.Campaign.ActiveRun;
            while (run.CurrentNodeId < 0 || run.Find(run.CurrentNodeId).Layer < layer)
            {
                MapNode next = CampaignRules.Choices(run)[0];
                CampaignResult result = next.Type == MapNodeType.Rest ? CampaignRules.Camp(save, Content.Campaign, next.NodeId, save.Beasts[0].BeastId)
                                        : next.Type == MapNodeType.Shop ? CampaignRules.Trade(save, Content.Campaign, next.NodeId, null)
                                        : CampaignRules.ResolveBattle(save, Content.Campaign, next.NodeId, BattleOutcome.PlayerVictory);
                Assert.IsTrue(result.Success, result.Error);
            }
        }
    }
}
