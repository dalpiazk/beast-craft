using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The discovery layer's view-models over the real content: the fogged map (hidden locations, fog
    /// cells, the points of interest seen, the Explored percentage), a point of interest's popup and
    /// visit, a Kinship trial from its preview through a headless battle to the pending choice and the
    /// choice popup, leaving a Kinship site behind at the pass, revisiting a stage, and the 100% toast.
    /// </summary>
    public class DiscoveryScreensTests
    {
        private const int MapSeed = 424242;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        /// <summary>A session playing the Hearthglen trio (golem, phoenix, griffin) at <paramref name="level"/> in r01.</summary>
        private static GameSession Trio(int level = 6)
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => MapSeed);
            session.StartWith(KinshipTests.TrioSave(new[] { "golem", "phoenix", "griffin" }, level));
            return session;
        }

        private static PointOfInterest Site(GameSession session)
        {
            return DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).Single(p => p.Kind == PoiKind.KinshipSite);
        }

        [Test]
        public void FoggedMap_HidesUnseenLocations_ListsSeenPoints_AndShowsExplored()
        {
            GameSession session = Trio();
            MapViewModel map = new MapViewModel(session);
            Assert.IsTrue(map.HasFog);
            Assert.IsNotEmpty(map.FogCells);
            Assert.IsTrue(map.Nodes.Where(n => n.Layer == 0).All(n => !n.Hidden), "the trailhead row is in sight");
            Assert.IsTrue(map.Nodes.Where(n => n.Layer >= 3 && n.Type != MapNodeType.Gate && n.Type != MapNodeType.Boss).All(n => n.Hidden), "further up is fogged");
            Assert.IsFalse(map.Nodes.Any(n => n.Hidden && n.State == MapNodeState.Reachable), "a reachable location is never hidden");
            Assert.AreEqual("Explored 0%", map.Header.CompletionText);
            Assert.IsEmpty(map.Pois, "nothing seen yet");

            KinshipTests.WalkTo(session.Save, Site(session).Layer);
            map.Refresh();
            PoiView site = map.Pois.Single(p => p.Kind == PoiKind.KinshipSite);
            Assert.AreEqual(PoiState.Revealed, site.State);
            Assert.AreEqual("Mossbound Stone", site.Name);
            Assert.AreEqual(MapTapKind.KinshipTrial, map.TapPoi(site.PoiId).Kind);
            StringAssert.StartsWith("Explored ", map.Header.CompletionText);
            Assert.Greater(map.Header.CompletionPercent, 0);

            // Hearthglen and the later regions are shown fully revealed.
            GameSession hearthglen = new GameSession(Content, new MemorySaveStorage(), () => MapSeed);
            Assert.IsTrue(hearthglen.NewGame("golem"));
            MapViewModel home = new MapViewModel(hearthglen);
            Assert.IsFalse(home.HasFog);
            Assert.IsTrue(home.Nodes.All(n => !n.Hidden));
            Assert.AreEqual(-1, home.Header.CompletionPercent);
        }

        [Test]
        public void PoiPopup_VisitsAPoint_ThenShowsItFound()
        {
            GameSession session = Trio();
            RegionProgress progress = session.Save.Campaign.FindRegion("r01");
            FogGrid grid = DiscoveryRules.GridOf(session.Content.Discovery, "r01");
            PointOfInterest point = DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery).First(p => p.Kind != PoiKind.KinshipSite);
            Assert.IsFalse(new PoiViewModel(session, point.PoiId).CanVisit, "still under the fog");
            MapFog.Reveal(progress.FogOf(0), grid.Index(point.HalfRow, point.Col));

            PoiViewModel popup = new PoiViewModel(session, point.PoiId);
            Assert.IsTrue(popup.CanVisit);
            Assert.IsNotEmpty(popup.Title);
            Assert.IsNotEmpty(popup.Body);
            int saves = session.AutosaveCount;
            string toast = popup.Visit(out DiscoveryResult result);
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotEmpty(toast);
            Assert.Greater(session.AutosaveCount, saves, "a visit is saved");
            PoiViewModel again = new PoiViewModel(session, point.PoiId);
            Assert.AreEqual(PoiState.Found, again.State);
            Assert.IsFalse(again.CanVisit);
            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(PoiState.Found, map.FindPoi(point.PoiId).State);
            Assert.AreEqual(MapTapKind.Poi, map.TapPoi(point.PoiId).Kind, "a found point still opens (what it held)");
        }

        [Test]
        public void KinshipTrial_PreviewBattleResults_ThenTheChoicePopup()
        {
            GameSession session = Trio(8);
            PointOfInterest site = Site(session);
            KinshipTests.WalkTo(session.Save, site.Layer);

            EncounterViewModel preview = EncounterViewModel.ForKinship(session, site.PoiId);
            Assert.IsNotNull(preview.Battle, preview.Error);
            Assert.IsTrue(preview.Battle.IsKinshipTrial);
            Assert.AreEqual("Mossbound Stone", preview.Title);
            Assert.AreEqual("Kinship trial", preview.KindLabel);
            Assert.IsNotEmpty(preview.Banner);
            StringAssert.Contains("Vanguard", preview.BondText);
            Assert.IsNull(preview.Suggestion);
            Assert.AreEqual(site.Level, preview.Level);

            ResultsViewModel results = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int xp = session.Save.Beasts[0].Progress.Xp;
                int gold = session.Save.Gold;
                NodeBattle battle = EncounterViewModel.ForKinship(session, site.PoiId).Start(out string error);
                Assert.IsNotNull(battle, error);
                Assert.AreEqual(KinshipRules.BattleSeed(site, attempt), battle.Seed, "each retry is a new battle");
                results = battle.Complete();
                Assert.IsTrue(results.IsKinshipTrial);
                Assert.AreEqual(xp, session.Save.Beasts[0].Progress.Xp, "a trial pays no XP");
                Assert.AreEqual(gold, session.Save.Gold, "nor gold");
                if (results.Victory)
                {
                    break;
                }

                Assert.IsNotNull(results.RetryNote);
                Assert.AreEqual(attempt + 1, session.Save.Discovery.KinshipLosses);
            }

            Assert.IsTrue(results.Victory, "the trial was won within 12 tries");
            CollectionAssert.AreEqual(new[] { "Treant", "Kirin" }, results.KinshipOffer);
            Assert.IsTrue(session.PendingKinship);
            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(-1, map.AutoAdvanceTarget(new PlayerSettings { AutoAdvance = true }, true), "the choice comes first");
            Assert.AreEqual(MapTapKind.KinshipChoice, map.Tap(map.Reachable()[0].NodeId).Kind);

            KinshipPickViewModel pick = new KinshipPickViewModel(session);
            CollectionAssert.AreEqual(new[] { "treant", "kirin" }, pick.Options.Select(o => o.SpeciesId).ToArray());
            Assert.AreEqual(5, pick.JoinLevel, "the team's level 8, minus 3");
            StringAssert.Contains("level 5", pick.Subtitle);
            Assert.IsTrue(pick.Choose("kirin", out string chooseError), chooseError);
            StringAssert.Contains("level 5", pick.JoinedMessage(pick.Options[1]));
            Assert.IsFalse(session.PendingKinship);
            Assert.AreEqual(4, session.Save.Beasts.Count);
            Assert.AreEqual(PoiState.Found, new MapViewModel(session).FindPoi(site.PoiId).State);
        }

        [Test]
        public void PassWithAnUnclaimedKinshipSite_AsksFirst()
        {
            GameSession session = Trio();
            PointOfInterest site = Site(session);
            MapRun run = session.Save.Campaign.ActiveRun;
            int top = run.Nodes.Max(n => n.Layer);
            KinshipTests.WalkTo(session.Save, top - 1);
            MapViewModel map = new MapViewModel(session);
            MapNodeView gate = map.Nodes.Single(n => n.Type == MapNodeType.Gate);
            MapTapResult tap = map.Tap(gate.NodeId);
            Assert.AreEqual(MapTapKind.ConfirmLeave, tap.Kind);
            Assert.AreEqual(site.PoiId, tap.PoiId);
            StringAssert.Contains("Mossbound Stone", tap.Message);
            Assert.AreEqual(MapTapKind.Preview, map.Tap(gate.NodeId, true).Kind, "once the player says go on");
        }

        [Test]
        public void RegionProgress_ListsStages_AndRevisitsOne()
        {
            GameSession session = Trio();
            MapRun run = session.Save.Campaign.ActiveRun;
            KinshipTests.WalkTo(session.Save, run.Nodes.Max(n => n.Layer) - 1);
            MapNode gate = run.Nodes.Single(n => n.Type == MapNodeType.Gate);
            Assert.AreEqual(CampaignOutcome.StageCleared, CampaignRules.ResolveBattle(session.Save, Content.Campaign, gate.NodeId, BattleOutcome.PlayerVictory).Outcome);
            session.EnsureExpedition();
            Assert.AreEqual(1, session.Save.Campaign.ActiveRun.Stage);

            RegionProgressViewModel panel = new RegionProgressViewModel(session);
            Assert.AreEqual("Verdant Hollow", panel.Name);
            Assert.AreEqual(4, panel.Stages.Count);
            Assert.IsTrue(panel.Stages[0].Cleared);
            Assert.AreEqual(panel.Stages[0].Rows, panel.Stages[0].RowsWalked);
            Assert.IsTrue(panel.Stages[0].CanRevisit);
            Assert.IsTrue(panel.Stages[1].IsCurrent);
            Assert.IsFalse(panel.Stages[2].CanRevisit, "not reached yet");
            StringAssert.Contains("Mosstrail Cape", panel.Reward);

            string cells = session.Save.Campaign.FindRegion("r01").FindFog(0).Cells;
            Assert.IsTrue(panel.Revisit(0));
            Assert.AreEqual(0, session.Save.Campaign.ActiveRun.Stage);
            Assert.AreEqual(1, session.Save.Campaign.FindRegion("r01").StagesCleared, "stage progress stays");
            StringAssert.StartsWith(cells.TrimEnd('0'), session.Save.Campaign.FindRegion("r01").FindFog(0).Cells, "the fog stays lifted");
        }

        [Test]
        public void FullExploration_QueuesTheRewardToast()
        {
            GameSession session = Trio();
            RegionProgress progress = session.Save.Campaign.FindRegion("r01");
            progress.StagesCleared = 3;
            progress.BossCleared = true;
            foreach (PointOfInterest point in DiscoveryRules.PointsOf(session.Save, session.Content.Discovery, "r01"))
            {
                progress.MarkFound(point.PoiId);
            }

            Assert.IsNotNull(session.CheckCompletion("r01"));
            Assert.AreEqual(1, session.PendingToasts.Count);
            StringAssert.Contains("Verdant Hollow fully explored", session.PendingToasts[0]);
            StringAssert.Contains("Mosstrail Cape", session.PendingToasts[0]);
            Assert.IsNull(session.CheckCompletion("r01"), "once");
            Assert.IsTrue(new MapViewModel(session).Header.CompletionRewarded);
        }
    }
}
