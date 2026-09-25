using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The authored post-game region, r11 Duskmeridian, over a save: unlocked by r10's boss, flat
    /// level 100 with no seal, the player's choice of Normal or Hard when an expedition starts
    /// (<see cref="MapRun.Difficulty"/>, save schema 6), Hard's harder shapes and boss, its
    /// Hard-only looks, and the save rules around the new field. Balance is the simulator's.
    /// </summary>
    public class PostGameCampaignTests
    {
        private RegionLibrary _regions;
        private CosmeticLibrary _cosmetics;

        [SetUp]
        public void SetUp()
        {
            _regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
            _cosmetics = CosmeticLibrary.Build(CosmeticTests.LoadCosmetics());
        }

        private PlayerSave SaveAtR11()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.Unlock("r11");
            return save;
        }

        /// <summary>Starts the boss stage of r11 on <paramref name="difficulty"/> and stands the player next to the boss.</summary>
        private MapNode StartAtBoss(PlayerSave save, RunDifficulty difficulty, int seed)
        {
            save.Campaign.FindRegion("r11").StagesCleared = 3;
            CampaignResult started = CampaignRules.StartRun(save, _regions, "r11", seed, difficulty);
            Assert.IsTrue(started.Success, started.Error);
            MapRun run = save.Campaign.ActiveRun;
            MapNode boss = run.Nodes[run.Nodes.Count - 1];
            Assert.AreEqual(MapNodeType.Boss, boss.Type);
            run.CurrentNodeId = boss.NodeId - 1;
            run.Nodes[boss.NodeId - 1].Next = new[] { boss.NodeId };
            return boss;
        }

        [Test]
        public void R11_IsThePostGameRegionAfterWorldcrown()
        {
            RegionData r11 = _regions.GetRegion("r11");

            Assert.IsTrue(r11.IsPostGame);
            Assert.AreEqual("r10", r11.RequiresRegionId);
            Assert.AreEqual(100, r11.MinLevel);
            Assert.AreEqual(100, r11.MaxLevel);
            Assert.AreEqual(4, r11.Stages);
            Assert.AreEqual(string.Empty, r11.BossRewardSealId, "no seal, no cap raise");
            CollectionAssert.AreEqual(new[] { "r11" }, _regions.UnlockedBy("r10").ConvertAll(r => r.RegionId));

            MapRulesData rules = _regions.RulesFor(r11);
            Dictionary<string, int> weights = new Dictionary<string, int>();
            foreach (NodeWeightData weight in rules.NodeWeights)
            {
                weights.Add(weight.Type, weight.Weight);
            }

            CollectionAssert.AreEquivalent(new Dictionary<string, int> { { "Battle", 55 }, { "Elite", 30 }, { "Shop", 5 }, { "Rest", 10 } }, weights);
            Assert.AreEqual(0, rules.EliteLevelOffset, "nothing above the level cap");
            Assert.AreEqual(0, rules.GateLevelOffset);
        }

        [Test]
        public void R11_BossTemplates_AreDraftTwinGiants_HardHarderThanNormal()
        {
            EncounterLibrary encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary());
            EncounterTemplateData normal = encounters.GetTemplate(_regions.GetRegion("r11").BossTemplateId);
            EncounterTemplateData hard = encounters.GetTemplate(_regions.GetRegion("r11").HardMode.BossTemplateId);
            EncounterTemplateData apex = encounters.GetTemplate("boss_r10_apex_pair");

            foreach (EncounterTemplateData boss in new[] { normal, hard })
            {
                Assert.IsTrue(boss.Draft);
                Assert.AreEqual(apex.ShapeId, boss.ShapeId, "mirrors the r10 apex pair");
                Assert.AreEqual(apex.Arena, boss.Arena);
                Assert.AreEqual(1, boss.Groups.Length);
                Assert.AreEqual("giant", boss.Groups[0].EnemyId);
                Assert.AreEqual(2, boss.Groups[0].Count);
                CollectionAssert.AreEqual(new[] { "Light", "Dark" }, boss.Groups[0].Elements);
            }

            Assert.Greater(hard.DifficultyOverride, normal.DifficultyOverride);
        }

        [Test]
        public void PostGameDifficultyCells_AreHarderOnHard_AndHarderThanTheMainline()
        {
            EncounterLibraryData library = EncounterContentTests.LoadEncounterLibrary();
            EncounterDifficultyData data = EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath);
            Assert.IsEmpty(EncounterDifficultyTable.Validate(data, library));
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(data);

            foreach (string shape in new[] { "squad", "horde", "elite" })
            {
                double mainline = table.Multiplier(shape, 100);
                double normal = table.Multiplier(shape + "_postgame", 100);
                double hard = table.Multiplier(shape + "_postgame_hard", 100);
                Assert.Greater(normal, mainline, shape + ": the post-game shape is calibrated harder than r10's level-100 cell");
                Assert.Greater(hard, normal, shape + ": Hard is calibrated harder than Normal");
            }
        }

        [Test]
        public void StartRun_HardOnlyInAPostGameRegion_StoredOnTheRun()
        {
            PlayerSave save = SaveAtR11();
            save.Campaign.Unlock("r10");

            CampaignResult refused = CampaignRules.StartRun(save, _regions, "r10", 0, 5, RunDifficulty.Hard);
            Assert.IsFalse(refused.Success);
            StringAssert.Contains("post-game regions only", refused.Error);
            Assert.IsFalse(save.Campaign.HasActiveRun, "a refused call changes nothing");
            Assert.IsFalse(CampaignRules.StartRun(save, _regions, "r11", 0, 5, (RunDifficulty)7).Success);

            Assert.IsTrue(CampaignRules.StartRun(save, _regions, "r11", 0, 5).Success);
            Assert.AreEqual(RunDifficulty.Normal, save.Campaign.ActiveRun.Difficulty, "the default is Normal");
            List<MapNode> normal = save.Campaign.ActiveRun.Nodes;
            CampaignRules.Retreat(save);
            Assert.AreEqual(RunDifficulty.Normal, save.Campaign.ActiveRun.Difficulty, "a cleared run is Normal again");

            CampaignResult hard = CampaignRules.StartRun(save, _regions, "r11", 0, 5, RunDifficulty.Hard);
            Assert.IsTrue(hard.Success, hard.Error);
            MapRun run = save.Campaign.ActiveRun;
            Assert.AreEqual(RunDifficulty.Hard, run.Difficulty);
            Assert.AreEqual(normal.Count, run.Nodes.Count, "the same seed draws the same map");
            for (int i = 0; i < run.Nodes.Count; i++)
            {
                MapNode node = run.Nodes[i];
                Assert.AreEqual(normal[i].Type, node.Type);
                Assert.AreEqual(100, node.Level, "flat level 100");
                if (node.IsBattle)
                {
                    StringAssert.EndsWith("_postgame_hard", node.ShapeId, "Hard fields the Hard shapes");
                    StringAssert.EndsWith("_postgame", normal[i].ShapeId);
                }
            }
        }

        [Test]
        public void HardBossClear_AddsTheHardLooks_OnTopOfNormalsRewards()
        {
            EconomyContent economy = new EconomyContent { Cosmetics = _cosmetics };
            PlayerSave save = SaveAtR11();
            MapNode boss = StartAtBoss(save, RunDifficulty.Hard, 11);
            Assert.AreEqual("boss_r11_dusk_and_dawn_hard", boss.TemplateId);

            CampaignResult first = CampaignRules.ResolveBattle(save, _regions, boss.NodeId, BattleOutcome.PlayerVictory, economy);

            Assert.AreEqual(CampaignOutcome.RegionCleared, first.Outcome, first.Error);
            Assert.IsNull(first.SealGranted, "no seal");
            CollectionAssert.IsSubsetOf(new[] { "kirin_horn/dawnshade", "basilisk_crown/dawnshade", "kirin_horn/radiant_dawnshade", "basilisk_crown/eclipse_dawnshade" },
                                        first.CosmeticsUnlocked, "a first clear on Hard grants the lair's looks and the Hard looks");
            Assert.IsTrue(save.Campaign.FindRegion("r11").BossCleared);
            Assert.IsFalse(save.Campaign.HasActiveRun);

            MapNode again = StartAtBoss(save, RunDifficulty.Hard, 12);
            CampaignResult replay = CampaignRules.ResolveBattle(save, _regions, again.NodeId, BattleOutcome.PlayerVictory, economy);
            Assert.IsEmpty(replay.CosmeticsUnlocked, "nothing new on a replay");
        }

        [Test]
        public void NormalBossClear_NeverUnlocksTheHardLooks_ALaterHardClearDoes()
        {
            EconomyContent economy = new EconomyContent { Cosmetics = _cosmetics };
            PlayerSave save = SaveAtR11();
            MapNode boss = StartAtBoss(save, RunDifficulty.Normal, 21);
            Assert.AreEqual("boss_r11_dusk_and_dawn", boss.TemplateId);

            CampaignResult normal = CampaignRules.ResolveBattle(save, _regions, boss.NodeId, BattleOutcome.PlayerVictory, economy);
            CollectionAssert.Contains(normal.CosmeticsUnlocked, "kirin_horn/dawnshade");
            CollectionAssert.Contains(normal.CosmeticsUnlocked, "basilisk_crown/dawnshade");
            CollectionAssert.DoesNotContain(normal.CosmeticsUnlocked, "kirin_horn/radiant_dawnshade");
            Assert.IsFalse(save.Cosmetics.Has("basilisk_crown/eclipse_dawnshade"));

            MapNode hard = StartAtBoss(save, RunDifficulty.Hard, 22);
            CampaignResult hardClear = CampaignRules.ResolveBattle(save, _regions, hard.NodeId, BattleOutcome.PlayerVictory, economy);
            CollectionAssert.AreEquivalent(new[] { "kirin_horn/radiant_dawnshade", "basilisk_crown/eclipse_dawnshade" }, hardClear.CosmeticsUnlocked,
                                           "a Hard clear after a Normal one adds the Hard looks only");
            Assert.IsNull(hardClear.GearGranted, "Hard's extra reward is looks only: the boss's gear is the first clear's");
        }

        [Test]
        public void HardLooks_AreNeverSoldOrDropped_AndNameAPostGameRegion()
        {
            for (int region = 1; region <= 10; region++)
            {
                Assert.IsFalse(_cosmetics.Pool(CosmeticLibrary.SourceShop, region).Exists(o => o.UnlockId == "r11"));
                Assert.IsFalse(_cosmetics.Pool(CosmeticLibrary.SourceDrop, region).Exists(o => o.UnlockId == "r11"));
            }

            CosmeticLibraryData data = CosmeticTests.LoadCosmetics();
            CosmeticOptionData hard = null;
            foreach (CosmeticCategoryData category in data.Categories)
            {
                foreach (CosmeticOptionData option in category.Options ?? new CosmeticOptionData[0])
                {
                    hard = option.Source == CosmeticLibrary.SourceBossHard ? option : hard;
                }
            }

            Assert.IsNotNull(hard);
            hard.UnlockId = "r10";
            string all = string.Join("\n", CosmeticLibraryValidator.Validate(data, null, _regions.RegionIds(), new[] { "r11" }));
            StringAssert.Contains("a Hard boss look names the post-game region whose lair grants it on Hard (UnlockId).", all);
            hard.UnlockId = string.Empty;
            StringAssert.Contains("a Hard boss look names the post-game region", string.Join("\n", CosmeticLibraryValidator.Validate(data)));
        }

        [Test]
        public void SaveSchema6_MigratesARunToNormal_AndTheValidatorHoldsHardToPostGame()
        {
            SaveContentCatalog catalog = SaveContentCatalog.FromData(BeastRosterTests.LoadRoster(), null, CampaignMapTests.LoadRegions());
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), catalog);

            PlayerSave save = SaveAtR11();
            Assert.IsTrue(CampaignRules.StartRun(save, _regions, "r11", 0, 5, RunDifficulty.Hard).Success);
            string v6 = serializer.Serialize(save);
            StringAssert.Contains("\"SchemaVersion\":6", v6);
            StringAssert.Contains(",\"Difficulty\":1}", v6);
            SaveLoadResult loaded = serializer.Deserialize(v6);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.AreEqual(RunDifficulty.Hard, loaded.Save.Campaign.ActiveRun.Difficulty);
            Assert.IsFalse(loaded.Save.Campaign.ActiveRun.Nodes.Count == 0);
            Assert.IsEmpty(SaveValidator.Validate(loaded.Save, catalog), "a Hard run in r11 is valid");

            string v5 = v6.Replace("\"SchemaVersion\":6", "\"SchemaVersion\":5").Replace(",\"Difficulty\":1}", "}");
            SaveLoadResult migrated = serializer.Deserialize(v5);
            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(5, migrated.SourceVersion);
            Assert.AreEqual(RunDifficulty.Normal, migrated.Save.Campaign.ActiveRun.Difficulty, "a v5 expedition migrates to Normal");

            PlayerSave wrong = PlayerSave.CreateNew();
            wrong.Campaign.Unlock("r10");
            Assert.IsTrue(CampaignRules.StartRun(wrong, _regions, "r10", 0, 5).Success);
            wrong.Campaign.ActiveRun.Difficulty = RunDifficulty.Hard;
            List<SaveIssue> issues = SaveValidator.Validate(wrong, catalog);
            Assert.IsTrue(issues.Exists(i => i.Kind == SaveIssueKind.InvalidMapRun && i.Path == "Campaign.ActiveRun.Difficulty"), "Hard is rejected outside a post-game region");

            wrong.Campaign.ActiveRun.Difficulty = (RunDifficulty)9;
            Assert.IsTrue(SaveValidator.Validate(wrong, null).Exists(i => i.Path == "Campaign.ActiveRun.Difficulty"), "an undefined difficulty is rejected");

            PlayerSave idle = PlayerSave.CreateNew();
            idle.Campaign.ActiveRun.Difficulty = RunDifficulty.Hard;
            Assert.IsTrue(SaveValidator.Validate(idle, null).Exists(i => i.Path == "Campaign.ActiveRun.Difficulty"), "no expedition stores Normal");
        }
    }
}
