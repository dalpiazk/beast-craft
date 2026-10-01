using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The casual player's shortcuts (producer persona review): idle rewards claimed on Continue
    /// and from the map's idle chip (a claim of nothing is silent), the chip's text, when the idle
    /// cap fills (for the notification), "Next battle" (the recommended location, the last team
    /// already picked), auto-advance after a win, and the saved battle-speed preference.
    /// </summary>
    public class CasualLoopTests
    {
        private const int MapSeed = 424242;
        private static readonly DateTime T0 = new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(ManualGameClock clock, ISaveStorage storage = null)
        {
            return TestSaves.Started(new GameSession(Content, storage ?? new MemorySaveStorage(), () => MapSeed, clock));
        }

        /// <summary>Marks the first reachable location cleared: the progress level idle pays at.</summary>
        private static MapNode ClearFirst(GameSession session)
        {
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode node = CampaignRules.Choices(run)[0];
            run.Cleared.Add(node.NodeId);
            run.CurrentNodeId = node.NodeId;
            return node;
        }

        [Test]
        public void Idle_StartsOnNewGame_PaysNothingBeforeTheFirstClear_Silently()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(100));
            GameSession session = NewSession(clock);
            Assert.IsTrue(session.Save.Idle.HasStarted, "a new game starts the idle clock");
            Assert.AreEqual(T0.AddHours(8), session.IdleCapUtc(), "full after the cap's 8 hours");

            clock.Advance(TimeSpan.FromHours(3));
            IdleStatusView status = session.IdleStatus();
            StringAssert.Contains("win a battle", status.Text);
            Assert.IsFalse(status.Claimable);
            int saves = session.AutosaveCount;
            IdleClaimView claim = session.ClaimIdle();
            Assert.IsNull(claim.Message, "a claim of nothing is silent");
            Assert.AreEqual(saves, session.AutosaveCount, "and saves nothing");
        }

        [Test]
        public void Idle_Chip_ShowsTheTime_ThenFull_AndClaimingPaysWithAToast()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(100));
            GameSession session = NewSession(clock);
            ClearFirst(session);

            clock.Advance(TimeSpan.FromMinutes(135));
            IdleStatusView status = session.IdleStatus();
            Assert.AreEqual("Idle 2h 15m", status.Text);
            Assert.IsTrue(status.Claimable);
            Assert.IsFalse(status.Capped);

            session.ResumeClaimPending = true;
            IdleClaimView claim = session.ClaimIdle();
            Assert.IsFalse(session.ResumeClaimPending, "a claim clears the resume flag");
            Assert.Greater(claim.Gold, 0);
            Assert.Greater(claim.Xp, 0);
            StringAssert.StartsWith("While you were away: +" + claim.Gold + " gold", claim.Message);
            Assert.AreEqual(AutosaveReason.IdleClaim, session.LastAutosaveReason);
            Assert.AreEqual(claim.Gold, session.Save.Gold);
            Assert.IsNull(session.ClaimIdle().Message, "claiming again at once pays nothing, silently");

            clock.Advance(TimeSpan.FromHours(20));
            status = session.IdleStatus();
            Assert.IsTrue(status.Capped);
            Assert.AreEqual("Idle full (8h)", status.Text);
            StringAssert.Contains("idle was full", session.ClaimIdle().Message);
        }

        [Test]
        public void Continue_ClaimsTheIdleRewards()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(100));
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession first = NewSession(clock, storage);
            ClearFirst(first);
            first.Autosave(AutosaveReason.Background);

            clock.Advance(TimeSpan.FromHours(4));
            GameSession second = new GameSession(Content, storage, () => 1, clock);
            LoadOutcome outcome = second.Continue();

            Assert.IsTrue(outcome.Success);
            Assert.IsNotNull(second.LastContinueClaim?.Message, "Continue claims straight away");
            Assert.Greater(second.Save.Gold, 0);
        }

        [Test]
        public void Continue_IdleClaimThatCrossesAnAvatarLevelThreshold_EarnsTheTitle_InTheSameContinue_WithOneToast_AndPersists()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(100));
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession first = NewSession(clock, storage);
            ClearFirst(first);

            // Push the progress level well past the avatar's target level, so idle's avatar XP pays at
            // the full rate no matter the avatar's own level (LevelGapXp: a gap this far below pays 0%,
            // so without this the claim below would earn nothing at all).
            first.Save.Campaign.FindRegion("r01").BossCleared = true;
            first.Save.Campaign.AddSeal("seal_r01");
            first.Save.Campaign.Unlock("r02");
            first.Save.Campaign.FindRegion("r02").BossCleared = true;
            first.Save.Campaign.Unlock("r03");
            first.Save.Campaign.FindRegion("r03").BossCleared = true;

            // One XP short of the avatar's 25th level (the authored "avatar_level_25" / "Seasoned
            // Handler"): any idle XP at all crosses it.
            first.Save.Avatar.Level = 24;
            first.Save.Avatar.Xp = AvatarProgression.XpToNextLevel(24) - 1;
            first.Autosave(AutosaveReason.Background);

            clock.Advance(TimeSpan.FromMinutes(10));
            GameSession second = new GameSession(Content, storage, () => 1, clock);
            LoadOutcome outcome = second.Continue();

            Assert.IsTrue(outcome.Success, outcome.Message);
            Assert.GreaterOrEqual(second.Save.Avatar.Level, 25, "the idle claim leveled the avatar past the threshold");
            Assert.IsTrue(second.Save.Achievements.HasEarned("avatar_level_25"));
            StringAssert.Contains("Seasoned Handler", second.LastContinueClaim?.Message, "the idle claim's own toast carries the new title");
            Assert.IsEmpty(second.PendingToasts, "one toast total: folded into the idle claim's message, not a second one from the session-start check");

            // Persisted: reloading the slot keeps the title without earning (or toasting) it again.
            GameSession third = new GameSession(Content, storage, () => 1, clock);
            LoadOutcome reloaded = third.Continue();
            Assert.IsTrue(reloaded.Success, reloaded.Message);
            Assert.IsTrue(third.Save.Achievements.HasEarned("avatar_level_25"));
            Assert.IsFalse((third.LastContinueClaim?.Message ?? string.Empty).Contains("Seasoned Handler"), "not earned again on a later Continue");
            Assert.IsTrue(third.PendingToasts.TrueForAll(t => !t.Contains("Seasoned Handler")));
        }

        [Test]
        public void NextBattle_IsTheLowestReachableBattle_AndOpensWithTheLastTeam()
        {
            GameSession session = NewSession(new ManualGameClock(T0, TimeSpan.Zero));
            MapViewModel map = new MapViewModel(session);
            MapNodeView next = map.Recommended();

            Assert.IsNotNull(next);
            Assert.AreEqual(MapNodeState.Reachable, next.State);
            foreach (MapNodeView other in map.Reachable())
            {
                Assert.LessOrEqual(next.Level, other.Level);
            }

            session.LastTeam.AddRange(new[] { "b6", "b2" });
            EncounterViewModel encounter = new EncounterViewModel(session, next.NodeId);
            CollectionAssert.AreEqual(new[] { "b6", "b2" }, encounter.Team, "the last team is already picked: Start is one more tap");
            Assert.IsTrue(encounter.CanStart);
        }

        [Test]
        public void NextBattle_IsNone_WhenOnlyACampIsAhead()
        {
            GameSession session = NewSession(new ManualGameClock(T0, TimeSpan.Zero));
            MapRun run = session.Save.Campaign.ActiveRun;
            MapNode camp = run.Nodes.Find(n => n.Type == MapNodeType.Rest);
            MapNode parent = run.Nodes.Find(n => Array.IndexOf(n.Next, camp.NodeId) >= 0 && Array.TrueForAll(n.Next, id => run.Find(id).Type == MapNodeType.Rest));
            Assume.That(parent, Is.Not.Null, "this map has a location that leads only to camps");
            run.CurrentNodeId = parent.NodeId;
            run.Cleared.Add(parent.NodeId);

            Assert.IsNull(new MapViewModel(session).Recommended());
        }

        [Test]
        public void AutoAdvance_GoesOnToTheNextBattle_OnlyAfterAWin_AndOnlyWhenSwitchedOn()
        {
            GameSession session = NewSession(new ManualGameClock(T0, TimeSpan.Zero));
            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(-1, map.AutoAdvanceTarget(session.Settings, true), "off by default");

            SettingsViewModel settings = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings);
            settings.SetAutoAdvance(true);
            Assert.AreEqual(map.Recommended().NodeId, map.AutoAdvanceTarget(session.Settings, true));
            Assert.AreEqual(-1, map.AutoAdvanceTarget(session.Settings, false), "a defeat stops at the map");
        }

        [Test]
        public void Settings_BattleSpeedIsSetDirectly_AndSaved_AndTheAlertSettingIsOnlyAvailableWhereNotificationsExist()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = new GameSession(Content, storage, () => 1);
            SettingsViewModel desktop = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings);

            Assert.AreEqual(1, session.Settings.BattleSpeed);
            Assert.AreEqual(1, desktop.BattleSpeed);
            desktop.SetBattleSpeed(3);
            Assert.AreEqual(3, new GameSession(Content, storage, () => 1).Settings.BattleSpeed, "saved at once");
            desktop.SetBattleSpeed(1);
            Assert.AreEqual(1, session.Settings.BattleSpeed);
            Assert.AreEqual(1, SettingsViewModel.Speed(new PlayerSettings { BattleSpeed = 9 }), "out of range reads as x1");

            Assert.IsFalse(desktop.NotificationsAvailable, "no notification (idle, Grove) or vibration control on desktop");
            desktop.SetIdleNotifications(true);
            Assert.IsFalse(session.Settings.IdleNotifications, "unavailable: the setter does nothing");

            List<bool> asked = new List<bool>();
            SettingsViewModel android = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings, true, true);
            android.IdleNotificationsChanged += asked.Add;
            Assert.IsTrue(android.NotificationsAvailable);
            Assert.IsFalse(android.IdleNotifications, "off by default");
            android.SetIdleNotifications(true);
            Assert.IsTrue(session.Settings.IdleNotifications);
            CollectionAssert.AreEqual(new[] { true }, asked, "turning it on asks the host for the permission");
        }
    }
}
