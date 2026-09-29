using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Hearthglen's screens, headless: the pickers (New Game, the skip's three picks, a trial's pick),
    /// the Keeper's story and the camp, and the tutorial hints (content, conditions, the once-only rule
    /// persisted in the save, and the settings switch).
    /// </summary>
    public class TutorialScreensTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(MemorySaveStorage storage = null)
        {
            return new GameSession(Content, storage ?? new MemorySaveStorage(), () => 17);
        }

        [Test]
        public void TheHintAndDialogueContent_ValidateClean_AndCoverTheTutorialBeats()
        {
            HintLibraryData hints = FieldJsonHelper.Read<HintLibraryData>(HintLibraryData.ProjectRelativePath);
            DialogueLibraryData dialogue = FieldJsonHelper.Read<DialogueLibraryData>(DialogueLibraryData.ProjectRelativePath);
            CollectionAssert.IsEmpty(HintValidator.Validate(hints));
            CollectionAssert.IsEmpty(DialogueValidator.Validate(dialogue));
            CollectionAssert.IsEmpty(DialogueValidator.ValidateScenes(dialogue, CampaignMapTests.LoadRegions()));
            Assert.That(hints.Hints.Length, Is.GreaterThanOrEqualTo(16), "the design's sixteen beats");
            Assert.IsNotNull(Content.Dialogue.Npc("grove_keeper"));
            Assert.IsNotEmpty(Content.Dialogue.SceneLines("hg_shrine"));
            Assert.AreEqual("keeper_default", Content.Dialogue.Resolve("grove_keeper", new List<string>()).LineId, "the Grove's resolution falls back to the default line");

            hints.Hints[0].Text = "The scamps were killed.";
            hints.Hints[1].Conditions = new[] { "seen:nope" };
            string all = string.Join("\n", HintValidator.Validate(hints));
            StringAssert.Contains("no one dies", all);
            StringAssert.Contains("names no hint", all);
        }

        [Test]
        public void Hints_ShowOnce_InPriorityOrder_PersistInTheSave_AndCanBeTurnedOff()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = NewSession(storage);
            session.NewGame("griffin");

            HintData first = HintService.Next(session, HintTriggers.EncounterOpen, 1);
            Assert.AreEqual("h_preview", first.HintId);
            Assert.IsNull(HintService.Next(session, HintTriggers.EncounterOpen, 2 + 10), "a location with no hint shows none");
            HintService.Dismiss(session, first.HintId);
            Assert.AreEqual("h_party", HintService.Next(session, HintTriggers.EncounterOpen, 1).HintId, "the next one due follows");
            HintService.Dismiss(session, "h_party");
            Assert.IsNull(HintService.Next(session, HintTriggers.EncounterOpen, 1), "each shows once");

            GameSession again = new GameSession(Content, storage, () => 1);
            Assert.IsTrue(again.Continue().Success);
            CollectionAssert.AreEqual(new[] { "h_preview", "h_party" }, again.Save.Tutorial.SeenHintIds, "seen hints are saved");
            Assert.IsNull(HintService.Next(again, HintTriggers.EncounterOpen, 1));

            Assert.IsNotNull(HintService.Next(again, HintTriggers.EncounterOpen, 2));
            HintService.TurnOff(again);
            Assert.IsNull(HintService.Next(again, HintTriggers.EncounterOpen, 2), "hints off: none");
            Assert.IsFalse(new GameSession(Content, storage, () => 1).Settings.TutorialHints, "the switch is saved in the settings");
            SettingsViewModel settings = new SettingsViewModel(again.Settings, again.SaveSettings);
            Assert.AreEqual("Off", settings.Rows()[SettingsViewModel.TutorialHints].Value);
            settings.Change(SettingsViewModel.TutorialHints);
            Assert.IsNotNull(HintService.Next(again, HintTriggers.EncounterOpen, 2));

            again.StartWith(TestSaves.SixStarters(Content));
            Assert.IsNull(HintService.Next(again, HintTriggers.MapOpen), "past Hearthglen: no tutorial hints");
        }

        [Test]
        public void StarterPick_NewGame_OffersAllTen_AndStartsHearthglen()
        {
            GameSession session = NewSession();
            StarterPickViewModel pick = new StarterPickViewModel(session, PickMode.NewGame);
            Assert.AreEqual(1, pick.Step);
            Assert.AreEqual(10, pick.Options.Count);
            Assert.IsTrue(pick.Options.TrueForAll(o => !string.IsNullOrEmpty(o.Blurb) && o.ArtKey.EndsWith("illustrated")), "blurbs and illustrated art");
            Assert.IsNull(pick.RequiredStance);
            Assert.IsTrue(pick.Choose("kirin", out string error), error);
            Assert.AreEqual("r00", session.Save.Campaign.ActiveRun.RegionId);
        }

        [Test]
        public void StarterPick_Skip_AsksOneStanceAtATime_AndLandsInVerdantHollow()
        {
            GameSession session = NewSession();
            StarterPickViewModel pick = new StarterPickViewModel(session, PickMode.Skip);
            Assert.IsFalse(pick.Choose("treant", out string _), "one down, two to go");
            Assert.AreEqual(2, pick.Step);
            Assert.AreEqual(CombatStance.Ranged, pick.RequiredStance);
            Assert.IsTrue(pick.Options.TrueForAll(o => o.Stance == CombatStance.Ranged));
            Assert.IsFalse(pick.Choose("griffin", out string wrong));
            Assert.IsNotNull(wrong, "the stance cycle is enforced");
            Assert.IsTrue(pick.Undo());
            Assert.AreEqual(1, pick.Step);
            pick.Choose("treant", out string _);
            pick.Choose("basilisk", out string _);
            Assert.AreEqual(CombatStance.Skirmisher, pick.RequiredStance);
            Assert.IsTrue(pick.Choose("thunderbird", out string error), error);
            Assert.AreEqual("r01", session.Save.Campaign.ActiveRun.RegionId);
            Assert.IsTrue(session.Save.Tutorial.Skipped);
        }

        [Test]
        public void StoryCampAndTrialPick_PlayThroughTheSession()
        {
            GameSession session = NewSession();
            session.NewGame("golem");
            MapViewModel map = new MapViewModel(session);
            Assert.AreEqual(MapTapKind.Story, map.Tap(0).Kind);
            Assert.IsTrue(map.Header.IsTutorial);
            Assert.AreEqual("Keeper's Shrine", map.Find(0).Name);

            StoryViewModel story = new StoryViewModel(session, 0, "hg_shrine");
            Assert.AreEqual("The Grove Keeper", story.Current.Speaker);
            int lines = 1;
            while (story.Next())
            {
                lines++;
            }

            Assert.AreEqual(4, lines);
            CampaignResult visited = story.Complete();
            Assert.IsTrue(visited.Success);
            StringAssert.Contains("Fury Draught", StoryViewModel.GiftText(session, visited));

            // Win the fights up to the first trial through the rules; its pick then waits.
            for (int nodeId = 1; nodeId <= 4; nodeId++)
            {
                Assert.IsTrue(CampaignRules.ResolveBattle(session.Save, Content.Campaign, nodeId, BattleOutcome.PlayerVictory).Success);
            }

            map.Refresh();
            Assert.AreEqual(2, session.PendingPick);
            Assert.AreEqual(MapTapKind.Pick, map.Tap(5).Kind, "no location while a beast waits");
            Assert.IsNull(NodeBattle.For(session, 5, out string blocked));
            Assert.IsNotNull(blocked);
            StarterPickViewModel trial = new StarterPickViewModel(session, PickMode.Trial);
            Assert.AreEqual(2, trial.Step);
            Assert.IsTrue(trial.Options.TrueForAll(o => o.Stance == CombatStance.Ranged));
            Assert.IsTrue(trial.Choose("phoenix", out string error), error);
            Assert.AreEqual(0, session.PendingPick);
            Assert.IsNotNull(NodeBattle.For(session, 5, out string _));

            for (int nodeId = 5; nodeId <= 7; nodeId++)
            {
                Assert.IsTrue(CampaignRules.ResolveBattle(session.Save, Content.Campaign, nodeId, BattleOutcome.PlayerVictory).Success);
            }

            new StarterPickViewModel(session, PickMode.Trial).Choose("griffin", out string _);
            session.Save.Beasts[0].Progress.Level = 3;
            CampViewModel camp = new CampViewModel(session, 8);
            Assert.IsTrue(camp.IsTutorial);
            Assert.AreEqual("hg_camp", camp.SceneId);
            Assert.AreEqual(session.Save.Beasts[2].BeastId, camp.Suggested, "the newest, lowest beast");
            Assert.IsTrue(camp.Train(camp.Suggested, out string trainError), trainError);
            Assert.IsTrue(camp.Done);
            Assert.IsTrue(camp.Beasts.TrueForAll(b => b.Level == 3), "Hearthglen's camp catches everyone up");
            StringAssert.Contains("caught up", string.Join(" ", camp.Summary));
        }

        [Test]
        public void TheFirstFights_AreOnTheOpenBoard_DrawnWithVerdantHollowsArt()
        {
            GameSession session = NewSession();
            session.NewGame("golem");
            CampaignRules.Visit(session.Save, Content.Campaign, 0, null);
            NodeBattle battle = NodeBattle.For(session, 1, out string error);
            Assert.IsNotNull(battle, error);
            Assert.IsNull(battle.RegionId, "open board");
            Assert.IsNull(battle.Layout);
            Assert.AreEqual("r01", battle.ArtRegionId);
            Assert.AreEqual("r00", battle.Plan.Level > 0 ? session.Save.Campaign.ActiveRun.RegionId : null);
            Assert.AreEqual("r00", CampaignRules.RewardModifiersFor(session.Save.Campaign.ActiveRun, battle.Node, Content.Campaign).FirstClearScope);
        }
    }

    /// <summary>Reads a content file by its project-relative path (the tests find the repo by walking up).</summary>
    internal static class FieldJsonHelper
    {
        public static T Read<T>(string projectRelativePath) where T : class
        {
            string root = GameContent.FindRoot();
            string path = GameContent.PathOf(root, projectRelativePath);
            return BeastCraft.FieldJson.FromJson<T>(BeastCraft.Localization.ContentText.ReadFile(path));
        }
    }
}
