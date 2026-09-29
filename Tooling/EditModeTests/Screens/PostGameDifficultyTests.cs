using System;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// r11's Normal or Hard choice in the game (#60): a replayed stage keeps its difficulty, the map
    /// header offers the choice and badges Hard, the next stage starts on the difficulty chosen, the
    /// encounter preview knows, and Hard stays refused outside a post-game region.
    /// </summary>
    public class PostGameDifficultyTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        /// <summary>A started game with every region's boss down and r11 open, on a Normal r11 expedition.</summary>
        private static GameSession AtR11()
        {
            GameSession session = TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => 424242, new ManualGameClock(T0, TimeSpan.FromHours(1000))));
            PlayerSave save = session.Save;
            save.Tutorial.HearthglenCleared = true;
            foreach (RegionData region in Content.Campaign.Regions)
            {
                if (!region.IsPostGame && !region.IsTutorial)
                {
                    save.Campaign.Unlock(region.RegionId);
                    save.Campaign.FindRegion(region.RegionId).BossCleared = true;
                }
            }

            save.Campaign.Unlock("r11");
            CampaignRules.Retreat(save);
            CampaignResult started = CampaignRules.StartRun(save, Content.Campaign, "r11", 0, 7, RunDifficulty.Normal);
            Assert.IsTrue(started.Success, started.Error);
            return session;
        }

        [Test]
        public void ReplayStage_KeepsTheExpeditionsDifficulty()
        {
            GameSession session = AtR11();
            Assert.IsTrue(session.ReplayStage(0, RunDifficulty.Hard).Success);
            Assert.AreEqual(RunDifficulty.Hard, session.Save.Campaign.ActiveRun.Difficulty);

            CampaignResult replayed = session.ReplayStage(0);

            Assert.IsTrue(replayed.Success, replayed.Error);
            Assert.AreEqual(RunDifficulty.Hard, session.Save.Campaign.ActiveRun.Difficulty, "a replay used to restart on Normal");
            Assert.AreEqual("r11", session.Save.Campaign.ActiveRun.RegionId);
        }

        [Test]
        public void TheNextStage_StartsOnTheDifficultyChosen()
        {
            GameSession session = AtR11();
            session.ReplayStage(0, RunDifficulty.Hard);

            // A stage cleared ends the expedition; the map starts the next one.
            session.Save.Campaign.FindRegion("r11").StagesCleared = 1;
            CampaignRules.Retreat(session.Save);
            CampaignResult next = session.EnsureExpedition();

            Assert.IsTrue(next.Success, next.Error);
            Assert.AreEqual(1, session.Save.Campaign.ActiveRun.Stage);
            Assert.AreEqual(RunDifficulty.Hard, session.Save.Campaign.ActiveRun.Difficulty);
        }

        [Test]
        public void TheMapHeader_OffersTheChoice_AndBadgesHard()
        {
            GameSession session = AtR11();
            MapViewModel map = new MapViewModel(session);
            map.Refresh();
            Assert.IsTrue(map.Header.HardAvailable);
            Assert.IsFalse(map.Header.IsHard);

            Assert.IsTrue(map.SwitchDifficulty(RunDifficulty.Hard));

            Assert.IsTrue(map.Header.IsHard);
            Assert.AreEqual(RunDifficulty.Hard, session.PreferredDifficulty);
            Assert.IsFalse(map.SwitchDifficulty(RunDifficulty.Hard), "already on Hard");
            MapNodeView reachable = map.Nodes.Find(n => n.State == MapNodeState.Reachable);
            Assert.IsNotNull(reachable);
            Assert.IsTrue(new EncounterViewModel(session, reachable.NodeId).IsHard);

            Assert.IsTrue(map.SwitchDifficulty(RunDifficulty.Normal));
            Assert.IsFalse(map.Header.IsHard);
            Assert.IsFalse(new EncounterViewModel(session, reachable.NodeId).IsHard);
        }

        [Test]
        public void Hard_StaysRefused_OutsideAPostGameRegion()
        {
            GameSession session = TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => 424242, new ManualGameClock(T0, TimeSpan.FromHours(1000))));
            MapViewModel map = new MapViewModel(session);
            map.Refresh();
            RunDifficulty before = session.Save.Campaign.ActiveRun.Difficulty;

            Assert.IsFalse(map.Header.HardAvailable);
            Assert.IsNull(session.ReplayStage(session.Save.Campaign.ActiveRun.Stage, RunDifficulty.Hard));
            Assert.IsFalse(map.SwitchDifficulty(RunDifficulty.Hard));
            Assert.AreEqual(RunDifficulty.Normal, before);
            Assert.AreEqual(RunDifficulty.Normal, session.Save.Campaign.ActiveRun.Difficulty);
        }
    }
}
