using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Npc;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// D3 of the Grove design (docs/design/grove.md, "Peaceful clears and colour evolutions"):
    /// peaceful clears (<see cref="CampaignRules.Soothe"/> — ordinary locations only, full rewards,
    /// RNG-stream parity with a combat win, <see cref="CampaignProgress.LocationsSoothed"/> and its
    /// NPC fact and achievement) and colour evolutions (<see cref="GroveRules.TryUnlockColourForm"/>,
    /// reusing <see cref="Economy.CosmeticRules"/>'s existing per-species ownership/selection shape).
    /// Plus the authored <c>grove-library.json</c> <c>Soothing</c>/<c>ColourForms</c> rows and
    /// <c>cosmetic-library.json</c>'s new colour-form categories against their validators.
    /// </summary>
    public class SoothingAndColourFormTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static PlayerSave SaveWithBeast(string beastId = "b1", string speciesId = "phoenix", int level = 2)
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create(beastId, speciesId, level));
            return save;
        }

        // ---- Authored content ----

        [Test]
        public void AuthoredSoothingAndColourForms_AreValid()
        {
            GroveLibraryData grove = FieldJsonHelper.Read<GroveLibraryData>(GroveLibraryData.ProjectRelativePath);
            RegionLibraryData regions = CampaignMapTests.LoadRegions();
            GardenLibraryData garden = FieldJsonHelper.Read<GardenLibraryData>(GardenLibraryData.ProjectRelativePath);
            ExpeditionLibraryData expedition = FieldJsonHelper.Read<ExpeditionLibraryData>(ExpeditionLibraryData.ProjectRelativePath);
            CosmeticLibraryData cosmetics = FieldJsonHelper.Read<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath);

            List<string> errors = GroveLibraryValidator.ValidateSoothingAndColourForms(grove, regions, garden, expedition, cosmetics);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            Assert.AreEqual(10, grove.ColourForms.Length, "one collectible colour form per roster species");
            HashSet<string> soothedRegions = new HashSet<string>();
            foreach (SoothingRegionData row in grove.Soothing)
            {
                soothedRegions.Add(row.RegionId);
                Assert.That(row.ItemIds.Length, Is.GreaterThanOrEqualTo(2), row.RegionId + ": needs a few options so the player has a choice");
            }

            foreach (RegionData region in regions.Regions)
            {
                Assert.IsTrue(soothedRegions.Contains(region.RegionId), region.RegionId + " has no soothing item set");
            }
        }

        [Test]
        public void Validator_ReportsBrokenSoothingAndColourForms()
        {
            GroveLibraryData grove = FieldJsonHelper.Read<GroveLibraryData>(GroveLibraryData.ProjectRelativePath);
            RegionLibraryData regions = CampaignMapTests.LoadRegions();
            GardenLibraryData garden = FieldJsonHelper.Read<GardenLibraryData>(GardenLibraryData.ProjectRelativePath);
            ExpeditionLibraryData expedition = FieldJsonHelper.Read<ExpeditionLibraryData>(ExpeditionLibraryData.ProjectRelativePath);
            CosmeticLibraryData cosmetics = FieldJsonHelper.Read<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath);

            grove.Soothing[0].ItemIds = new[] { "not_a_grove_item" };
            grove.Soothing[1].RegionId = grove.Soothing[0].RegionId; // now unknown region "r02" has no set, and r01's id is claimed twice
            grove.ColourForms[0].ItemId = "not_a_grove_item";
            grove.ColourForms[0].ItemCount = 0;
            grove.ColourForms[1].SpeciesId = "not_a_species";
            grove.ColourForms[2].CosmeticOptionId = "not_an_option";

            List<string> errors = GroveLibraryValidator.ValidateSoothingAndColourForms(grove, regions, garden, expedition, cosmetics);
            string all = string.Join("\n", errors);

            StringAssert.Contains("not a Grove item", all);
            StringAssert.Contains("is listed twice", all);
            StringAssert.Contains("has no soothing item set", all);
            StringAssert.Contains("ItemCount must be at least 1", all);
            StringAssert.Contains("not a cosmetic category scoped to species", all);
            StringAssert.Contains("must be a grove look whose UnlockId is", all);
        }

        // ---- Soothing: Core rules ----

        /// <summary>The first row-0 location of a fresh r01 expedition whose type matches <paramref name="type"/>, trying seeds until one is found.</summary>
        private static MapNode FindNode(PlayerSave save, MapNodeType type, int maxSeed = 60)
        {
            return FindNode(save, type, out int _, maxSeed);
        }

        /// <summary><see cref="FindNode(PlayerSave, MapNodeType, int)"/>, also reporting the map seed used (so an identical map can be rebuilt).</summary>
        private static MapNode FindNode(PlayerSave save, MapNodeType type, out int usedSeed, int maxSeed = 60)
        {
            for (int seed = 1; seed <= maxSeed; seed++)
            {
                CampaignResult started = CampaignRules.StartRun(save, Content.Campaign, "r01", seed);
                Assert.IsTrue(started.Success, started.Error);
                MapNode node = CampaignRules.Choices(save.Campaign.ActiveRun).Find(n => n.Type == type);
                if (node != null)
                {
                    usedSeed = seed;
                    return node;
                }

                CampaignRules.Retreat(save);
            }

            Assert.Fail("No seed within " + maxSeed + " produced a reachable " + type + " node at row 0.");
            usedSeed = 0;
            return null;
        }

        /// <summary>
        /// Walks the expedition in progress, clearing whatever Battle/Elite locations, Rests and Shops
        /// it meets along the way (the game's own rules: win, camp, trade), until a location matching
        /// <paramref name="stopAt"/> becomes reachable — returned uncleared.
        /// </summary>
        private static MapNode WalkUntil(PlayerSave save, Func<MapNode, bool> stopAt)
        {
            MapRun run = save.Campaign.ActiveRun;
            for (int guard = 0; guard < 60; guard++)
            {
                List<MapNode> choices = CampaignRules.Choices(run);
                MapNode stop = choices.Find(n => stopAt(n));
                if (stop != null)
                {
                    return stop;
                }

                MapNode next = choices[0];
                switch (next.Type)
                {
                    case MapNodeType.Rest:
                        CampaignRules.Camp(save, Content.Campaign, next.NodeId, save.Beasts[0].BeastId);
                        break;
                    case MapNodeType.Shop:
                        CampaignRules.Trade(save, Content.Campaign, next.NodeId, null);
                        break;
                    default:
                        CampaignRules.ResolveBattle(save, Content.Campaign, next.NodeId, BattleOutcome.PlayerVictory);
                        break;
                }
            }

            Assert.Fail("Never found a matching location.");
            return null;
        }

        /// <summary>Clears every node up to (and returns) the stage's Gate, camping/trading through Rest/Shop along the way.</summary>
        private static MapNode WalkToGate(PlayerSave save)
        {
            return WalkUntil(save, n => n.Type == MapNodeType.Gate || n.Type == MapNodeType.Boss);
        }

        [Test]
        public void Soothe_RefusesAnyLocationThatIsNotAnOrdinaryBattle()
        {
            PlayerSave save = SaveWithBeast();
            CampaignRules.StartRun(save, Content.Campaign, "r01", 3);
            MapNode elite = WalkUntil(save, n => n.Type == MapNodeType.Elite);
            save.Grove.Items.Add("ashbloom_pure", 5);

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, elite.NodeId, "ashbloom_pure",
                                                          new[] { "b1" }, Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("only an ordinary battle location can be soothed", result.Error);
            Assert.IsNull(rewards);
            Assert.AreEqual(5, save.Grove.Items.GetCount("ashbloom_pure"), "nothing spent on a refusal");
        }

        [Test]
        public void Soothe_RefusesAGateEvenWithTheItemHeld()
        {
            PlayerSave save = SaveWithBeast();
            CampaignRules.StartRun(save, Content.Campaign, "r01", 3);
            MapNode gate = WalkToGate(save);
            save.Grove.Items.Add("ashbloom_pure", 5);

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, gate.NodeId, "ashbloom_pure",
                                                          new[] { "b1" }, Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success, "gates and bosses must still be won by combat");
            Assert.IsNull(rewards);
        }

        [Test]
        public void Soothe_RefusesHearthglenFights()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Tutorial.HearthglenCleared = false;
            save.Campaign.Lock(CampaignProgress.StartingRegionId);
            save.Campaign.Unlock(CampaignProgress.TutorialRegionId);
            save.Beasts.Add(OwnedBeast.Create("b1", "phoenix", 5));
            CampaignResult started = CampaignRules.StartRun(save, Content.Campaign, CampaignProgress.TutorialRegionId, 1);
            Assert.IsTrue(started.Success, started.Error);

            // Walk Hearthglen's opening path (visiting Story beats, camping at Rest) until the first fight.
            MapNode trial = null;
            for (int guard = 0; guard < 20 && trial == null; guard++)
            {
                MapNode next = CampaignRules.Choices(save.Campaign.ActiveRun).First();
                if (next.IsBattle)
                {
                    trial = next;
                }
                else if (next.Type == MapNodeType.Story)
                {
                    Assert.IsTrue(CampaignRules.Visit(save, Content.Campaign, next.NodeId, _ => null).Success);
                }
                else if (next.Type == MapNodeType.Rest)
                {
                    Assert.IsTrue(CampaignRules.Camp(save, Content.Campaign, next.NodeId, "b1").Success);
                }
                else
                {
                    Assert.Fail("Unexpected Hearthglen node type before the first fight: " + next.Type);
                }
            }

            Assert.IsNotNull(trial, "no fight found in Hearthglen's opening path");

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, trial.NodeId, "ashbloom_pure",
                                                          new[] { "b1" }, Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success, "a tutorial Trial is never an ordinary Battle location: refused either way");
            Assert.IsNull(rewards);
        }

        [Test]
        public void Soothe_RefusesAnItemThatDoesNotSootheThisRegion()
        {
            PlayerSave save = SaveWithBeast();
            MapNode node = FindNode(save, MapNodeType.Battle);
            save.Grove.Items.Add("dye_sunwash", 5);

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, "dye_sunwash",
                                                          new[] { "b1" }, Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("does not soothe", result.Error);
            Assert.IsNull(rewards);
        }

        [Test]
        public void Soothe_RefusesWithoutEnoughOfTheItem_AndSpendsNothing()
        {
            PlayerSave save = SaveWithBeast();
            MapNode node = FindNode(save, MapNodeType.Battle);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, item, new[] { "b1" },
                                                          Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success);
            Assert.IsNull(rewards);
            Assert.AreEqual(0, save.Grove.Items.GetCount(item));
        }

        [Test]
        public void Soothe_RefusesWithNoOwnedBeastInTheTeam()
        {
            PlayerSave save = SaveWithBeast();
            MapNode node = FindNode(save, MapNodeType.Battle);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            save.Grove.Items.Add(item, 5);

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, item,
                                                          new[] { "not_owned" }, Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsFalse(result.Success);
            Assert.IsNull(rewards);
            Assert.AreEqual(5, save.Grove.Items.GetCount(item), "nothing spent: the item is refused before it is consumed");
        }

        [Test]
        public void Soothe_Succeeds_ConsumesOneItem_ClearsTheNode_ResetsTheLossStreak_AndCountsIt()
        {
            PlayerSave save = SaveWithBeast();
            MapNode node = FindNode(save, MapNodeType.Battle);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            save.Grove.Items.Add(item, 2);

            // A prior loss at this node: soothing must reset the streak exactly as a win does.
            CampaignRules.ResolveBattle(save, Content.Campaign, node.NodeId, BattleOutcome.EnemyVictory);
            Assert.AreEqual(1, save.Campaign.ActiveRun.NodeAttempts);
            int avatarLevelBefore = save.Avatar.Level;

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, item, new[] { "b1" },
                                                          Content.Drops, Content.Economy, out BattleRewardSummary rewards);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(CampaignOutcome.Cleared, result.Outcome);
            Assert.AreEqual(1, save.Grove.Items.GetCount(item), "exactly one item spent");
            Assert.IsTrue(save.Campaign.ActiveRun.Cleared.Contains(node.NodeId));
            Assert.AreEqual(node.NodeId, save.Campaign.ActiveRun.CurrentNodeId);
            Assert.AreEqual(0, save.Campaign.ActiveRun.NodeAttempts, "the loss streak resets, exactly as a win does");
            Assert.AreEqual(1, save.Campaign.LocationsSoothed);
            Assert.IsFalse(CampaignRules.CanEnter(save.Campaign.ActiveRun, node.NodeId), "cleared: cannot be entered again");

            Assert.IsNotNull(rewards);
            Assert.IsTrue(rewards.Applied);
            Assert.AreEqual(BattleOutcome.PlayerVictory, rewards.Outcome);
            Assert.IsTrue(rewards.BeastXpGained.ContainsKey("b1"));
            Assert.Greater(rewards.BeastXpGained["b1"], 0, "full beast XP, not participation-only");
            Assert.AreEqual(BeastProgression.BattleXp(BattleOutcome.PlayerVictory, node.Level, false, 2), rewards.BeastXpGained["b1"],
                            "exactly the clear bonus a still-standing fielded beast earns for a real win");

            Assert.Greater(rewards.AvatarXpGained, 0, "full rewards: the avatar is part of the team and earns its battle XP too");
            Assert.AreEqual(AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, node.Level, avatarLevelBefore), rewards.AvatarXpGained,
                            "exactly the avatar XP a real win at this node's level would grant");
        }

        [Test]
        public void Soothe_IsDeterministic_SameNodeSameRewards()
        {
            PlayerSave saveA = SaveWithBeast();
            MapNode nodeA = FindNode(saveA, MapNodeType.Battle, out int seed);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            saveA.Grove.Items.Add(item, 1);
            CampaignRules.Soothe(saveA, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, nodeA.NodeId, item, new[] { "b1" }, Content.Drops,
                                 Content.Economy, out BattleRewardSummary rewardsA);

            // Rebuild the identical map deterministically: same region, same seed as saveA's successful attempt.
            PlayerSave saveB = SaveWithBeast();
            CampaignResult started = CampaignRules.StartRun(saveB, Content.Campaign, "r01", seed);
            Assert.IsTrue(started.Success, started.Error);
            MapNode nodeB = saveB.Campaign.ActiveRun.Find(nodeA.NodeId);
            saveB.Grove.Items.Add(item, 1);

            CampaignRules.Soothe(saveB, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, nodeB.NodeId, item, new[] { "b1" }, Content.Drops,
                                 Content.Economy, out BattleRewardSummary rewardsB);

            Assert.AreEqual(rewardsA.GoldGained, rewardsB.GoldGained);
            Assert.AreEqual(rewardsA.BeastXpGained["b1"], rewardsB.BeastXpGained["b1"]);
            Assert.AreEqual(rewardsA.Loot.Drops.Count, rewardsB.Loot.Drops.Count);
            for (int i = 0; i < rewardsA.Loot.Drops.Count; i++)
            {
                Assert.AreEqual(rewardsA.Loot.Drops[i].MaterialId, rewardsB.Loot.Drops[i].MaterialId);
                Assert.AreEqual(rewardsA.Loot.Drops[i].Quantity, rewardsB.Loot.Drops[i].Quantity);
            }
        }

        [Test]
        public void Soothe_RewardStreamsMatchWhatARealWinRightNowWouldRoll()
        {
            PlayerSave save = SaveWithBeast();
            MapNode node = FindNode(save, MapNodeType.Battle);
            string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
            save.Grove.Items.Add(item, 1);

            int expectedSeed = CampaignRules.BattleSeed(node, CampaignRules.LossesAt(save.Campaign.ActiveRun, node.NodeId));
            EncounterPlan plan = CampaignRules.PlanFor(node, Content.Encounters, Content.Enemies);

            CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, item, new[] { "b1" },
                                                          Content.Drops, Content.Economy, out BattleRewardSummary rewards);
            Assert.IsTrue(result.Success, result.Error);

            // The same gold roll (stream, seed and first-clear flag) a fought win right now would have used.
            Random goldRng = new Random(Progression.LootRoller.DeriveSeed(expectedSeed, Progression.PostBattleAward.GoldStream));
            int expectedGold = Progression.PostBattleAward.AwardGold(new BattleResult(BattleOutcome.PlayerVictory, 0, new List<BattleTurnResult>()), Content.Drops,
                                                                     plan.DropShapeId, plan.Level, rewards.Loot.FirstClear, 1.0, 0, goldRng);

            Assert.AreEqual(expectedGold, rewards.GoldGained, "the same LootRoller.DeriveSeed(seed, GoldStream) roll a real win right now would use");
        }

        [Test]
        public void Soothe_AchievementFires_AtTheThreshold()
        {
            PlayerSave save = SaveWithBeast();
            AchievementData peacekeeper = Content.AchievementLibrary.All.FirstOrDefault(a => a.Kind == AchievementKinds.LocationsSoothed);
            Assert.IsNotNull(peacekeeper, "authored in achievements.json");

            for (int i = 0; i < peacekeeper.Threshold; i++)
            {
                MapNode node = FindNode(save, MapNodeType.Battle);
                string item = Content.GroveLibrary.Soothing("r01").ItemIds[0];
                save.Grove.Items.Add(item, 1);
                CampaignResult result = CampaignRules.Soothe(save, Content.Campaign, Content.GroveLibrary, Content.Encounters, Content.Enemies, node.NodeId, item,
                                                             new[] { "b1" }, Content.Drops, Content.Economy, out BattleRewardSummary _, Content.Achievements);
                if (i + 1 == peacekeeper.Threshold)
                {
                    CollectionAssert.Contains(result.TitlesEarned.ConvertAll(a => a.AchievementId), peacekeeper.AchievementId);
                }

                CampaignRules.Retreat(save);
            }

            Assert.IsTrue(save.Achievements.HasEarned(peacekeeper.AchievementId));
        }

        [Test]
        public void BuildFacts_EmitsLocationSoothed_Cascading()
        {
            PlayerSave save = SaveWithBeast();
            save.Campaign.LocationsSoothed = 2;

            HashSet<string> facts = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);

            CollectionAssert.Contains(facts, "location_soothed:1");
            CollectionAssert.Contains(facts, "location_soothed:2");
            Assert.IsFalse(facts.Contains("location_soothed:3"));
        }

        // ---- Colour forms ----

        [Test]
        public void TryUnlockColourForm_RefusesUnknownFormOrNotEnoughOfTheItem()
        {
            PlayerSave save = SaveWithBeast();
            ColourFormData form = Content.GroveLibrary.Data.ColourForms[0];

            Assert.IsFalse(GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, "not_a_form").Success);
            Assert.IsFalse(GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, form.ColourFormId).Success, "no item held yet");
            Assert.AreEqual(0, save.Grove.Items.GetCount(form.ItemId));
        }

        [Test]
        public void TryUnlockColourForm_Succeeds_ConsumesTheItem_AndAnyBeastOfTheSpeciesCanWearOrSwitchAwayFromIt()
        {
            ColourFormData form = Content.GroveLibrary.Data.ColourForms[0];
            PlayerSave save = SaveWithBeast("b1", form.SpeciesId);
            save.Beasts.Add(OwnedBeast.Create("b2", form.SpeciesId, 10));
            save.Grove.Items.Add(form.ItemId, form.ItemCount);

            ColourFormResult result = GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, form.ColourFormId);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsTrue(result.NewlyUnlocked);
            Assert.AreEqual(0, save.Grove.Items.GetCount(form.ItemId), "the recipe's full cost spent");
            string key = CosmeticCollection.Key(form.CosmeticCategoryId, form.CosmeticOptionId);
            Assert.IsTrue(save.Cosmetics.Has(key), "owned account-wide, not per beast");

            // Both beasts of the species can wear it (owned once, shared)...
            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, "b1", form.CosmeticCategoryId, form.CosmeticOptionId, Content.Economy.Cosmetics));
            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, "b2", form.CosmeticCategoryId, form.CosmeticOptionId, Content.Economy.Cosmetics));
            Assert.AreEqual(form.CosmeticOptionId, CosmeticRules.Worn(save, "b1", form.CosmeticCategoryId, Content.Economy.Cosmetics).OptionId);

            // ...and switch back to the free default independently.
            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, "b1", form.CosmeticCategoryId, "natural", Content.Economy.Cosmetics));
            Assert.AreEqual("natural", CosmeticRules.Worn(save, "b1", form.CosmeticCategoryId, Content.Economy.Cosmetics).OptionId);
            Assert.AreEqual(form.CosmeticOptionId, CosmeticRules.Worn(save, "b2", form.CosmeticCategoryId, Content.Economy.Cosmetics).OptionId, "b2 keeps wearing it");
        }

        [Test]
        public void TryUnlockColourForm_AlreadyOwned_SpendsTheItemButGrantsTokensInstead()
        {
            ColourFormData form = Content.GroveLibrary.Data.ColourForms[0];
            PlayerSave save = SaveWithBeast("b1", form.SpeciesId);
            save.Grove.Items.Add(form.ItemId, form.ItemCount * 2);
            GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, form.ColourFormId);
            int tokensBefore = save.LookTokens;

            ColourFormResult result = GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, form.ColourFormId);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(result.NewlyUnlocked);
            Assert.Greater(result.TokensGranted, 0);
            Assert.AreEqual(tokensBefore + result.TokensGranted, save.LookTokens);
            Assert.AreEqual(0, save.Grove.Items.GetCount(form.ItemId));
        }

        [Test]
        public void BuildFacts_EmitsColourFormOwned_OnlyOnceUnlocked()
        {
            ColourFormData form = Content.GroveLibrary.Data.ColourForms[0];
            PlayerSave save = SaveWithBeast("b1", form.SpeciesId);

            Assert.IsFalse(NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary)
                                   .Contains("colour_form_owned:" + form.ColourFormId));

            save.Grove.Items.Add(form.ItemId, form.ItemCount);
            GroveRules.TryUnlockColourForm(save, Content.GroveLibrary, Content.Economy.Cosmetics, form.ColourFormId);

            CollectionAssert.Contains(NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary),
                                      "colour_form_owned:" + form.ColourFormId);
        }

        [Test]
        public void ColourFormRequest_AsksToSeeAnOwnedColourForm()
        {
            bool found = false;
            foreach (Tutorial.RequestData request in Content.Dialogue.RequestsFor("wandering_scholar"))
            {
                foreach (string condition in request.Conditions)
                {
                    found |= condition.StartsWith("colour_form_owned:", StringComparison.Ordinal);
                }
            }

            Assert.IsTrue(found, "at least one NPC request asks to see a colour form (docs/design/grove.md, D3)");
        }
    }
}
