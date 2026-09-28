using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Hearthglen (r00), the onboarding region: its authored data (a fixed map, fixed templates with
    /// their own difficulty, never eased), the beast picks (the stance cycle, no duplicates, the pick
    /// pending after a won trial), a whole playthrough to the r00 to r01 unlock, the skip, and the
    /// schema-7 migration (existing saves count Hearthglen as cleared).
    /// </summary>
    public class HearthglenTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static RegionLibrary Regions
        {
            get { return Content.Campaign; }
        }

        private static List<CreatureSpeciesSO> Roster
        {
            get { return Content.Species; }
        }

        private static CombatStance StanceOf(string speciesId)
        {
            return Content.Battle.GetSpecies(speciesId).Stance;
        }

        // ------------------------------------------------------------------ data

        [Test]
        public void TheAuthoredRegions_ValidateClean_WithHearthglenAsTheTutorial()
        {
            CollectionAssert.IsEmpty(RegionLibraryValidator.Validate(CampaignMapTests.LoadRegions(), EncounterContentTests.LoadEncounterLibrary()));
            RegionData hearthglen = Regions.Tutorial;
            Assert.IsNotNull(hearthglen);
            Assert.AreEqual(CampaignProgress.TutorialRegionId, hearthglen.RegionId);
            Assert.IsTrue(hearthglen.IsTutorial);
            Assert.IsFalse(new List<RegionData>(Regions.Regions).Contains(hearthglen), "never in the campaign order the tools walk");
            Assert.AreEqual("r01", Regions.MainlineRegions()[0].RegionId);
            Assert.AreEqual("r01", Regions.BattlefieldRegionOf("r00"), "Hearthglen fights on Verdant Hollow's battlefields");
            Assert.AreEqual("r02", Regions.BattlefieldRegionOf("r02"));
        }

        [Test]
        public void TheFixedMap_IsLinear_NineFightsAtLevelsOneToThree_EachATemplateWithItsOwnDifficulty()
        {
            List<MapNode> nodes = CampaignRules.FixedMap(Regions.Tutorial, 7);
            Assert.AreEqual(CampaignRules.FixedMap(Regions.Tutorial, 99).ConvertAll(n => n.Type), nodes.ConvertAll(n => n.Type), "the map does not depend on the seed");
            int fights = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode node = nodes[i];
                Assert.AreEqual(i, node.NodeId);
                Assert.AreEqual(i, node.Layer);
                CollectionAssert.AreEqual(i + 1 < nodes.Count ? new[] { i + 1 } : new int[0], node.Next);
                Assert.That(node.Level, Is.InRange(1, 3));
                Assert.IsTrue(node.IsPlaced);
                StringAssert.StartsWith("r00/", node.LabelKey);
                if (!node.IsBattle)
                {
                    continue;
                }

                fights++;
                EncounterPlan plan = CampaignRules.PlanFor(new MapRun { RegionId = "r00" }, node, Content.Encounters, Content.Enemies, Regions);
                EncounterTemplateData template = Content.Encounters.GetTemplate(node.TemplateId);
                Assert.Greater(template.DifficultyOverride, 0.0, node.TemplateId);
                Assert.AreEqual(1.0, plan.DifficultyScale, "never eased: " + node.TemplateId);
                Assert.AreEqual(template.DifficultyOverride, plan.Multiplier, "its own difficulty, never the calibrated table: " + node.TemplateId);
            }

            Assert.That(fights, Is.InRange(8, 10));
            Assert.AreEqual(MapNodeType.Story, nodes[0].Type);
            Assert.AreEqual(MapNodeType.Story, nodes[nodes.Count - 1].Type);
            Assert.AreEqual(2, nodes.FindAll(n => n.Type == MapNodeType.Trial).Count);
            Assert.AreEqual(1, nodes.FindAll(n => n.Type == MapNodeType.Rest).Count);
            Assert.AreEqual(LocationKind.Shrine, nodes[0].Kind);
            Assert.AreEqual(LocationKind.KinshipSite, nodes.Find(n => n.Type == MapNodeType.Trial).Kind);
        }

        [Test]
        public void TheTutorialPass_CatchesABrokenHearthglen()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            RegionData r00 = data.TutorialRegions[0];
            r00.StageEasing = new[] { 1.0 };
            r00.FixedNodes[1].TemplateId = "boss_r01_hollow_warden";
            r00.FixedNodes[2].TemplateId = "no_such_template";
            r00.FixedNodes[4].PickStep = 3;
            r00.FixedNodes[3].Level = 9;
            r00.BattlefieldRegionId = "r77";
            List<string> errors = RegionLibraryValidator.Validate(data, EncounterContentTests.LoadEncounterLibrary());
            string all = string.Join("\n", errors);
            StringAssert.Contains("StageEasing must be empty", all);
            StringAssert.Contains("template 'no_such_template' is not in the encounter library", all);
            StringAssert.Contains("PickStep 2, then 3", all);
            StringAssert.Contains("Level 9 is outside", all);
            StringAssert.Contains("BattlefieldRegionId 'r77'", all);

            EncounterLibraryData encounters = EncounterContentTests.LoadEncounterLibrary();
            Array.Find(encounters.Templates, t => t.EncounterId == "hg_meadow_scamps").DifficultyOverride = 0;
            StringAssert.Contains("needs its own DifficultyOverride", string.Join("\n", RegionLibraryValidator.Validate(CampaignMapTests.LoadRegions(), encounters)));
        }

        // ------------------------------------------------------------------ picks

        [Test]
        public void StanceCycle_IsVanguardRangedSkirmisher_AndWraps()
        {
            Assert.AreEqual(CombatStance.Ranged, StarterPicks.NextStance(CombatStance.Vanguard));
            Assert.AreEqual(CombatStance.Skirmisher, StarterPicks.NextStance(CombatStance.Ranged));
            Assert.AreEqual(CombatStance.Vanguard, StarterPicks.NextStance(CombatStance.Skirmisher));
            Assert.IsNull(StarterPicks.RequiredStance(1, CombatStance.Ranged));
            Assert.AreEqual(CombatStance.Skirmisher, StarterPicks.RequiredStance(2, CombatStance.Ranged));
            Assert.AreEqual(CombatStance.Vanguard, StarterPicks.RequiredStance(3, CombatStance.Ranged));
        }

        [Test]
        public void Options_TheFirstPickIsAnyOfTen_TheSecondTheNextStance_TheThirdTheRemainingOne()
        {
            Assert.AreEqual(10, Roster.Count);
            Assert.AreEqual(10, StarterPicks.Options(new string[0], Roster).Count);
            foreach (CreatureSpeciesSO first in Roster)
            {
                List<CreatureSpeciesSO> second = StarterPicks.Options(new[] { first.SpeciesId }, Roster);
                CombatStance next = StarterPicks.NextStance(first.Stance);
                Assert.IsNotEmpty(second);
                Assert.IsTrue(second.TrueForAll(s => s.Stance == next), first.SpeciesId + ": the next stance only");
                Assert.AreEqual(Roster.FindAll(s => s.Stance == next).Count, second.Count, "any beast of that stance");
                foreach (CreatureSpeciesSO pick in second)
                {
                    List<CreatureSpeciesSO> third = StarterPicks.Options(new[] { first.SpeciesId, pick.SpeciesId }, Roster);
                    CombatStance remaining = StarterPicks.NextStance(next);
                    Assert.AreNotEqual(first.Stance, remaining);
                    Assert.IsTrue(third.TrueForAll(s => s.Stance == remaining));
                    Assert.AreEqual(Roster.FindAll(s => s.Stance == remaining).Count, third.Count);
                    Assert.IsEmpty(StarterPicks.Options(new[] { first.SpeciesId, pick.SpeciesId, third[0].SpeciesId }, Roster), "three picks, no more");
                }
            }
        }

        [Test]
        public void EveryLegalRunOfPicks_IsOnePerStance_WithNoDuplicates_AndCoversEveryTrio()
        {
            HashSet<string> trios = new HashSet<string>();
            int sequences = 0;
            foreach (CreatureSpeciesSO a in Roster)
            {
                foreach (CreatureSpeciesSO b in Roster)
                {
                    foreach (CreatureSpeciesSO c in Roster)
                    {
                        string[] picks = { a.SpeciesId, b.SpeciesId, c.SpeciesId };
                        bool legal = StarterPicks.IsLegalSequence(picks, Roster, out string _);
                        bool expected = b.Stance == StarterPicks.NextStance(a.Stance) && c.Stance == StarterPicks.NextStance(b.Stance);
                        Assert.AreEqual(expected, legal, string.Join("/", picks));
                        if (legal)
                        {
                            sequences++;
                            List<string> sorted = new List<string>(picks);
                            sorted.Sort(StringComparer.Ordinal);
                            trios.Add(string.Join("/", sorted));
                        }
                    }
                }
            }

            Assert.AreEqual(90, sequences, "5 x 3 x 2 trios, each reachable from any of its three stances");
            Assert.AreEqual(30, trios.Count, "every one-per-stance trio");
            Assert.IsFalse(StarterPicks.IsLegal(new[] { "golem" }, "golem", Roster, out string duplicate));
            StringAssert.Contains("already", duplicate);
            Assert.IsFalse(StarterPicks.IsLegal(new[] { "golem" }, "treant", Roster, out string sameStance), "a second Vanguard");
            StringAssert.Contains("Ranged", sameStance);
            Assert.IsFalse(StarterPicks.IsLegal(new[] { "golem" }, "griffin", Roster, out string _), "Skirmisher skips a stance");
            Assert.IsFalse(StarterPicks.IsLegal(new string[0], "nope", Roster, out string _));
        }

        // ------------------------------------------------------------------ the playthrough

        [Test]
        public void APlaythrough_PicksAtEachTrial_AndClearingTheLastLocation_UnlocksVerdantHollow()
        {
            PlayerSave save = StarterPicks.NewGame("phoenix", Roster, Content.SkillLibrary, out string error);
            Assert.IsNotNull(save, error);
            Assert.IsFalse(save.Campaign.IsUnlocked("r01"));
            Assert.IsTrue(save.Campaign.IsUnlocked("r00"));
            Assert.IsTrue(CampaignRules.StartRun(save, Regions, "r00", 5).Success);
            MapRun run = save.Campaign.ActiveRun;
            Assert.AreEqual(Regions.Tutorial.FixedNodes.Length, run.Nodes.Count);

            CampaignResult story = CampaignRules.Visit(save, Regions, 0, Content.Battle.GetConsumable);
            Assert.AreEqual(CampaignOutcome.Visited, story.Outcome);
            Assert.AreEqual(2, ConsumableInventory.Quantity(save, "fury_draught"), "the Keeper's gift");
            Assert.AreEqual(1, story.ItemsGranted.Count);

            int count = run.Nodes.Count;
            for (int nodeId = 1; nodeId < count; nodeId++)
            {
                MapNode node = run.Find(nodeId);
                CampaignResult result;
                if (node.IsBattle)
                {
                    Assert.AreEqual(CampaignOutcome.Lost, CampaignRules.ResolveBattle(save, Regions, nodeId, BattleOutcome.EnemyVictory).Outcome, "a loss just retries");
                    result = CampaignRules.ResolveBattle(save, Regions, nodeId, BattleOutcome.PlayerVictory);
                }
                else if (node.Type == MapNodeType.Rest)
                {
                    result = CampaignRules.Camp(save, Regions, nodeId, save.Beasts[save.Beasts.Count - 1].BeastId);
                }
                else
                {
                    result = CampaignRules.Visit(save, Regions, nodeId, Content.Battle.GetConsumable);
                }

                Assert.IsTrue(result.Success, nodeId + ": " + result.Error);
                if (node.Type == MapNodeType.Trial)
                {
                    int step = result.PickStep;
                    Assert.AreEqual(save.Beasts.Count + 1, step);
                    Assert.AreEqual(step, StarterPicks.PendingStep(save, Regions));
                    Assert.IsFalse(CampaignRules.ResolveBattle(save, Regions, nodeId + 1, BattleOutcome.PlayerVictory).Success || nodeId + 1 >= count,
                                   "no location can be entered while the pick waits");

                    string wrong = step == 2 ? "golem" : "kirin";
                    Assert.IsFalse(StarterPicks.Pick(save, Regions, Roster, Content.SkillLibrary, wrong).Success, "the stance cycle is enforced");
                    Assert.IsFalse(StarterPicks.Pick(save, Regions, Roster, Content.SkillLibrary, "phoenix").Success, "no duplicates");
                    string right = step == 2 ? "thunderbird" : "leviathan";
                    PickResult pick = StarterPicks.Pick(save, Regions, Roster, Content.SkillLibrary, right);
                    Assert.IsTrue(pick.Success, pick.Error);
                    Assert.AreEqual(1, pick.Beast.Progress.Level, "every pick joins at level 1");
                    Assert.IsNotNull(pick.Beast.Skills.GetEquipped(0), "wearing its default loadout");
                    Assert.AreEqual(0, StarterPicks.PendingStep(save, Regions));
                    Assert.IsFalse(StarterPicks.Pick(save, Regions, Roster, Content.SkillLibrary, "griffin").Success, "one pick per trial");
                }

                if (nodeId == count - 1)
                {
                    Assert.AreEqual(CampaignOutcome.TutorialCleared, result.Outcome);
                    CollectionAssert.AreEqual(new[] { "r01" }, result.UnlockedRegionIds);
                }
                else
                {
                    Assert.AreNotEqual(CampaignOutcome.TutorialCleared, result.Outcome, "node " + nodeId);
                }
            }

            CollectionAssert.AreEqual(new[] { "phoenix", "thunderbird", "leviathan" }, save.Beasts.ConvertAll(b => b.Progress.SpeciesId));
            Assert.IsTrue(save.Tutorial.HearthglenCleared);
            Assert.IsFalse(save.Tutorial.Skipped);
            Assert.IsFalse(save.Campaign.HasActiveRun);
            Assert.IsTrue(save.Campaign.IsUnlocked("r01"), "r00 -> r01");
            Assert.IsFalse(save.Campaign.IsUnlocked("r00"), "played once");
            Assert.IsFalse(CampaignRules.StartRun(save, Regions, "r00", 1).Success);
            Assert.AreEqual(0, CampaignRules.ProgressLevel(save, Regions), "Hearthglen leaves no trace the campaign's pacing reads");
            Assert.IsTrue(CampaignRules.StartRun(save, Regions, "r01", 3).Success);
            Assert.AreEqual(0, StarterPicks.PendingStep(save, Regions));
        }

        [Test]
        public void APickLeftUnmade_IsOfferedAgain_AfterASaveAndLoad()
        {
            PlayerSave save = StarterPicks.NewGame("griffin", Roster, Content.SkillLibrary, out string _);
            CampaignRules.StartRun(save, Regions, "r00", 1);
            CampaignRules.Visit(save, Regions, 0, null);
            for (int nodeId = 1; nodeId <= 4; nodeId++)
            {
                Assert.IsTrue(CampaignRules.ResolveBattle(save, Regions, nodeId, BattleOutcome.PlayerVictory).Success);
            }

            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer());
            PlayerSave loaded = serializer.Deserialize(serializer.Serialize(save)).Save;
            Assert.AreEqual(2, StarterPicks.PendingStep(loaded, Regions));
            CollectionAssert.AreEquivalent(new[] { "golem", "treant", "tarasque", "leviathan", "frost_wyrm" },
                                      StarterPicks.Options(StarterPicks.Picked(loaded), Roster).ConvertAll(s => s.SpeciesId), "Skirmisher's next stance: every Vanguard");
        }

        [Test]
        public void SkippingTheTutorial_MakesTheThreePicks_GrantsItsRewards_AndLandsInVerdantHollow()
        {
            Assert.IsNull(StarterPicks.NewGameSkippingTutorial(new[] { "kirin", "golem", "griffin" }, Roster, Content.SkillLibrary, Regions, Content.Battle.GetConsumable,
                                                               out string wrong), "Ranged is followed by Skirmisher");
            Assert.IsNotNull(wrong);
            Assert.IsNull(StarterPicks.NewGameSkippingTutorial(new[] { "kirin", "griffin" }, Roster, Content.SkillLibrary, Regions, Content.Battle.GetConsumable, out string _));

            PlayerSave save = StarterPicks.NewGameSkippingTutorial(new[] { "kirin", "griffin", "golem" }, Roster, Content.SkillLibrary, Regions, Content.Battle.GetConsumable,
                                                                   out string error);
            Assert.IsNotNull(save, error);
            CollectionAssert.AreEqual(new[] { "kirin", "griffin", "golem" }, save.Beasts.ConvertAll(b => b.Progress.SpeciesId));
            Assert.IsTrue(save.Beasts.TrueForAll(b => b.Progress.Level == StarterPicks.JoinLevel));
            Assert.IsTrue(save.Tutorial.HearthglenCleared);
            Assert.IsTrue(save.Tutorial.Skipped);
            Assert.IsTrue(save.Campaign.IsUnlocked("r01"));
            Assert.IsFalse(save.Campaign.IsUnlocked("r00"));
            foreach (ItemGrantData grant in CampaignRules.TutorialRewards(Regions))
            {
                Assert.AreEqual(grant.Quantity, ConsumableInventory.Quantity(save, grant.ConsumableId), "Hearthglen's completion rewards: " + grant.ConsumableId);
            }

            Assert.IsNotEmpty(CampaignRules.TutorialRewards(Regions));
            Assert.AreEqual(0, StarterPicks.PendingStep(save, Regions));
            Assert.IsFalse(CampaignRules.SkipTutorial(save, Regions, Content.Battle.GetConsumable).Success, "once only");
            Assert.IsEmpty(SaveValidator.Validate(save, SaveContentCatalog.FromData(Content.Roster, Content.SkillLibrary, Content.Regions)));
        }

        // ------------------------------------------------------------------ migration (schema 6 -> 7)

        [Test]
        public void Migration_ASaveWithBeasts_CountsHearthglenCleared_AndIsNeverOfferedAPick()
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => 3);
            PlayerSave v6 = TestSaves.SixStarters(Content);
            v6.Tutorial = new TutorialProgress();
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), SaveContentCatalog.FromData(Content.Roster, Content.SkillLibrary, Content.Regions));
            string json = serializer.Serialize(v6).Replace("\"SchemaVersion\":7", "\"SchemaVersion\":6");
            json = json.Substring(0, json.IndexOf(",\"Tutorial\":", StringComparison.Ordinal)) + "}";

            SaveLoadResult loaded = serializer.Deserialize(json);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsTrue(loaded.Migrated);
            Assert.AreEqual(6, loaded.SourceVersion);
            PlayerSave save = loaded.Save;
            Assert.IsTrue(save.Tutorial.HearthglenCleared);
            Assert.IsFalse(save.Tutorial.Skipped);
            Assert.AreEqual(6, save.Beasts.Count, "no beast lost");
            Assert.IsTrue(save.Campaign.IsUnlocked("r01"));
            Assert.IsFalse(save.Campaign.IsUnlocked("r00"));
            Assert.AreEqual(0, StarterPicks.PendingStep(save, Regions), "no re-offer");

            session.StartWith(save);
            Assert.AreEqual("r01", session.Save.Campaign.ActiveRun.RegionId, "straight back into the campaign");

            // Round trip at schema 7: nothing moves.
            string again = serializer.Serialize(save);
            Assert.AreEqual(again, serializer.Serialize(serializer.Deserialize(again).Save));
        }

        [Test]
        public void Migration_ABeastlessSave_StartsHearthglen_WithTheNewGamePickPending()
        {
            SaveLoadResult loaded = new SaveSerializer(new JsonSaveSerializer()).Deserialize("{\"SchemaVersion\":6,\"Campaign\":{\"Regions\":[{\"RegionId\":\"r01\"}]}}");
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsFalse(loaded.Save.Tutorial.HearthglenCleared);
            Assert.IsTrue(loaded.Save.Campaign.IsUnlocked("r00"));
            Assert.IsFalse(loaded.Save.Campaign.IsUnlocked("r01"));
            Assert.AreEqual(1, StarterPicks.PendingStep(loaded.Save, Regions));
        }

        // ------------------------------------------------------------------ the session

        [Test]
        public void Session_NewGame_PlaysHearthglen_ThenVerdantHollow()
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => 11);
            Assert.IsTrue(session.NewGame("treant"));
            Assert.AreEqual("r00", session.NextRegionId());
            Assert.AreEqual("Keeper's Shrine", session.LocationName(session.Save.Campaign.ActiveRun.Find(0)), "tutorial locations carry their authored names");

            Assert.IsTrue(CampaignRules.CompleteTutorial(session.Save, Regions));
            Assert.AreEqual("r01", session.NextRegionId());
            session.EnsureExpedition();
            Assert.AreEqual("r01", session.Save.Campaign.ActiveRun.RegionId);
        }
    }
}
