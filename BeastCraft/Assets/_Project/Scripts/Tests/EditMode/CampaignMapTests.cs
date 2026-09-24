using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The region campaign: the authored <c>Data/Campaign/regions.json</c> and its boss templates,
    /// <see cref="RegionLibraryValidator"/>, <see cref="NodeMapGenerator"/>'s maps (structure,
    /// types, levels, determinism), <see cref="LevelCaps"/> and <see cref="CampaignRules"/> over a save.
    /// Pacing (battles per region, where the cap bites) is the balance simulator's <c>--mode campaign</c>.
    /// </summary>
    public class CampaignMapTests
    {
        private RegionLibraryData _data;
        private RegionLibrary _regions;

        [SetUp]
        public void SetUp()
        {
            _data = LoadRegions();
            _regions = RegionLibrary.Build(_data);
        }

        internal static RegionLibraryData LoadRegions()
        {
            return EncounterContentTests.Load<RegionLibraryData>(RegionLibraryData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredRegions_PassValidationAgainstTheEncounterLibrary()
        {
            List<string> errors = RegionLibraryValidator.Validate(_data, EncounterContentTests.LoadEncounterLibrary());

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(10, _regions.Regions.Count);
            Assert.AreEqual(CampaignProgress.StartingRegionId, _regions.Regions[0].RegionId);
            Assert.AreEqual(12, _data.StartingLevelCap, "first region max 10 + margin 2");
            foreach (RegionData region in _regions.Regions)
            {
                Assert.AreEqual(4, region.Stages, region.RegionId + ": four stages per region (lead decision)");
            }
        }

        [Test]
        public void AuthoredBossTemplates_AreDraftsThatBuildAtTheRegionsMaxLevel()
        {
            EncounterLibraryData encounterData = EncounterContentTests.LoadEncounterLibrary();
            EncounterLibrary encounters = EncounterLibrary.Build(encounterData);
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);

            foreach (RegionData region in _regions.Regions)
            {
                EncounterTemplateData template = encounters.GetTemplate(region.BossTemplateId);
                Assert.IsNotNull(template, region.RegionId);
                Assert.IsTrue(template.Draft, region.RegionId + ": boss templates are placeholders pending producer review (Draft flag)");
                StringAssert.DoesNotContain(EncounterLibraryValidator.DraftMarker, template.Description, region.RegionId + ": draft status stays out of player-facing text");
                Assert.Greater(template.DifficultyOverride, 0.0, region.RegionId + ": the boss carries its own calibrated difficulty");

                EncounterPlan plan = EncounterPlan.FromTemplate(encounters, enemies, region.BossTemplateId, region.MaxLevel);
                Assert.IsNotNull(plan, region.RegionId);
                Assert.AreEqual(region.MaxLevel, plan.Level);
                Assert.AreEqual(template.DifficultyOverride, plan.Multiplier);
            }
        }

        [Test]
        public void Validator_ReportsBrokenRegions()
        {
            RegionLibraryData data = LoadRegions();
            data.Regions[0].RegionId = "start";
            data.Regions[2].MinLevel = 25;
            data.Regions[3].RequiresRegionId = "r09";
            data.Regions[4].ShapeWeights = new[] { new ShapeWeightData { ShapeId = "nope", Weight = 1 } };
            data.Regions[5].BossTemplateId = "no_boss";
            data.Seals[6].LevelCap = 50;
            data.Seals[1].DisplayName = string.Empty;
            data.Seals[2].Description = "  ";
            data.MapRules.Layers = 2;
            data.MapRules.NodeWeights = new[] { new NodeWeightData { Type = "Gate", Weight = 5 } };

            List<string> errors = RegionLibraryValidator.Validate(data, EncounterContentTests.LoadEncounterLibrary());

            string all = string.Join("\n", errors);
            StringAssert.Contains("first region must be 'r01'", all);
            StringAssert.Contains("MinLevel 25", all);
            StringAssert.Contains("RequiresRegionId 'r09'", all);
            StringAssert.Contains("shape 'nope'", all);
            StringAssert.Contains("boss template 'no_boss'", all);
            StringAssert.Contains("caps must not fall", all);
            StringAssert.Contains("Seal '" + data.Seals[1].SealId + "': DisplayName is empty", all);
            StringAssert.Contains("Seal '" + data.Seals[2].SealId + "': Description is empty", all);
            StringAssert.Contains("Layers must be at least 3", all);
            StringAssert.Contains("must be Battle, Elite, Shop or Rest", all);
            StringAssert.Contains("must include Battle", all);

            Assert.IsNotEmpty(RegionLibraryValidator.Validate(null));
            RegionLibraryData wrongVersion = LoadRegions();
            wrongVersion.SchemaVersion = 7;
            Assert.AreEqual(1, RegionLibraryValidator.Validate(wrongVersion).Count);
        }

        [Test]
        public void RowLevel_SpreadsTheRegionOverItsStages()
        {
            RegionData region = _regions.GetRegion("r03");
            MapRulesData rules = _regions.RulesFor(region);
            int top = rules.Layers - 1;

            Assert.AreEqual(21, NodeMapGenerator.RowLevel(region, rules, 0, 0));
            Assert.AreEqual(30, NodeMapGenerator.RowLevel(region, rules, region.Stages - 1, top));
            int previous = 0;
            for (int stage = 0; stage < region.Stages; stage++)
            {
                for (int layer = 0; layer <= top; layer++)
                {
                    int level = NodeMapGenerator.RowLevel(region, rules, stage, layer);
                    Assert.GreaterOrEqual(level, previous);
                    previous = level;
                }
            }

            Assert.AreEqual(21 + ((1 * top) * 9 / (4 * top)), NodeMapGenerator.RowLevel(region, rules, 1, 0), "about 2.5 levels per stage");
        }

        [Test]
        public void Generate_IsDeterministicPerSeed()
        {
            string first = FieldJson.ToJson(new MapRun { Nodes = NodeMapGenerator.Generate(_regions, "r02", 1, 99) });
            string second = FieldJson.ToJson(new MapRun { Nodes = NodeMapGenerator.Generate(_regions, "r02", 1, 99) });
            string other = FieldJson.ToJson(new MapRun { Nodes = NodeMapGenerator.Generate(_regions, "r02", 1, 100) });

            Assert.AreEqual(first, second);
            Assert.AreNotEqual(first, other);
            Assert.IsNull(NodeMapGenerator.Generate(_regions, "nowhere", 0, 1));
        }

        [Test]
        public void Generate_PlacesEveryLocationOnTheRegionMap()
        {
            foreach (RegionData region in _regions.Regions)
            {
                MapRulesData rules = _regions.RulesFor(region);
                int top = rules.Layers - 1;
                for (int seed = 0; seed < 20; seed++)
                {
                    List<MapNode> nodes = NodeMapGenerator.Generate(region, rules, seed % region.Stages, seed);
                    foreach (MapNode node in nodes)
                    {
                        string where = region.RegionId + " seed " + seed + " node " + node.NodeId;
                        Assert.That(node.X, Is.InRange(0.05f, 0.95f), where);
                        Assert.That(node.Y, Is.InRange(0.05f, 0.95f), where);
                        Assert.AreEqual(LocationKinds.For(node.Type), node.Kind, where);
                        StringAssert.StartsWith(region.RegionId + "/" + LocationKinds.Key(node.Kind) + "/", node.LabelKey, where);
                        foreach (MapNode other in nodes)
                        {
                            if (other.Layer > node.Layer)
                            {
                                Assert.Less(node.Y, other.Y, where + ": deeper rows lie further into the region");
                            }
                            else if (other.Layer == node.Layer && other.Lane > node.Lane)
                            {
                                Assert.Less(node.X, other.X, where + ": lanes run left to right");
                            }
                        }
                    }

                    MapNode last = nodes[nodes.Count - 1];
                    Assert.AreEqual(top, last.Layer);
                    Assert.AreEqual(0.5f, last.X, 1e-6f, "the Pass or Lair sits centred at the far edge");
                    Assert.IsTrue(last.Kind == LocationKind.Pass || last.Kind == LocationKind.Lair);
                }
            }
        }

        [Test]
        public void Place_UsesItsOwnStream_AndLeavesTheMapItself()
        {
            List<MapNode> nodes = NodeMapGenerator.Generate(_regions, "r04", 2, 31);
            string placed = FieldJson.ToJson(new MapRun { Nodes = nodes });
            foreach (MapNode node in nodes)
            {
                node.X = 0f;
                node.Y = 0f;
                node.LabelKey = string.Empty;
            }

            MapRun unplaced = new MapRun { RegionId = "r04", Stage = 2, Seed = 31, Nodes = nodes };
            Assert.AreEqual(1, unplaced.EnsureInitialized(), "the missing placement is one repair");
            Assert.AreEqual(placed, FieldJson.ToJson(new MapRun { Nodes = unplaced.Nodes }));
            Assert.AreEqual(0, unplaced.EnsureInitialized());

            NodeMapGenerator.Place(nodes, "r04", 32);
            Assert.AreNotEqual(placed, FieldJson.ToJson(new MapRun { Nodes = nodes }), "another seed jitters differently");
            NodeMapGenerator.Place(null, "r04", 1);
        }

        [Test]
        public void Generate_EveryAuthoredRegionAndStage_MakesAWellFormedMap()
        {
            foreach (RegionData region in _regions.Regions)
            {
                MapRulesData rules = _regions.RulesFor(region);
                for (int stage = 0; stage < region.Stages; stage++)
                {
                    for (int seed = 0; seed < 40; seed++)
                    {
                        AssertWellFormed(region, rules, stage, seed, NodeMapGenerator.Generate(region, rules, stage, seed));
                    }
                }
            }
        }

        private static void AssertWellFormed(RegionData region, MapRulesData rules, int stage, int seed, List<MapNode> nodes)
        {
            string where = region.RegionId + " stage " + stage + " seed " + seed;
            int top = rules.Layers - 1;
            bool last = stage == region.Stages - 1;
            List<int>[] parents = new List<int>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                parents[i] = new List<int>();
            }

            int elites = 0;
            int shops = 0;
            HashSet<int> startLanes = new HashSet<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode node = nodes[i];
                Assert.AreEqual(i, node.NodeId, where);
                Assert.AreEqual(LootRoller.DeriveSeed(seed, i), node.EncounterSeed, where);
                Assert.That(node.Level, Is.InRange(region.MinLevel, Math.Min(100, region.MaxLevel + 1)), where);
                foreach (int next in node.Next)
                {
                    Assert.AreEqual(node.Layer + 1, nodes[next].Layer, where + ": links go one row up");
                    parents[next].Add(i);
                }

                if (node.Layer < top)
                {
                    Assert.IsNotEmpty(node.Next, where + ": every node but the top leads on");
                }

                elites += node.Type == MapNodeType.Elite ? 1 : 0;
                shops += node.Type == MapNodeType.Shop ? 1 : 0;
                int rowLevel = NodeMapGenerator.RowLevel(region, rules, stage, node.Layer);
                if (node.Layer == 0)
                {
                    Assert.AreEqual(MapNodeType.Battle, node.Type, where);
                    startLanes.Add(node.Lane);
                }
                else if (node.Layer == top)
                {
                    Assert.AreEqual(nodes.Count - 1, i, where + ": the top is the last node");
                    Assert.AreEqual(last ? MapNodeType.Boss : MapNodeType.Gate, node.Type, where);
                }
                else if (node.Layer == rules.RestLayer)
                {
                    Assert.AreEqual(MapNodeType.Rest, node.Type, where);
                }

                switch (node.Type)
                {
                    case MapNodeType.Battle:
                        Assert.AreEqual(rowLevel, node.Level, where);
                        Assert.IsTrue(Array.Exists(region.ShapeWeights, w => w.ShapeId == node.ShapeId), where + ": shape " + node.ShapeId);
                        break;
                    case MapNodeType.Elite:
                        Assert.GreaterOrEqual(node.Layer, rules.EliteMinLayer, where);
                        Assert.AreEqual(rowLevel + rules.EliteLevelOffset, node.Level, where);
                        Assert.AreEqual(rules.EliteShapeId, node.ShapeId, where);
                        break;
                    case MapNodeType.Gate:
                        Assert.AreEqual(rowLevel + rules.GateLevelOffset, node.Level, where);
                        Assert.AreEqual(rules.EliteShapeId, node.ShapeId, where + ": no authored gates, so a generated elite");
                        break;
                    case MapNodeType.Boss:
                        Assert.AreEqual(region.MaxLevel, node.Level, where);
                        Assert.AreEqual(region.BossTemplateId, node.TemplateId, where);
                        break;
                }
            }

            Assert.GreaterOrEqual(startLanes.Count, 2, where + ": the first two paths start in different lanes");
            Assert.That(elites, Is.InRange(rules.MinElites, rules.MaxElites), where);
            Assert.LessOrEqual(shops, rules.MaxShops, where);

            for (int i = 0; i < nodes.Count; i++)
            {
                MapNode node = nodes[i];
                if (node.Layer > 0)
                {
                    Assert.IsNotEmpty(parents[i], where + ": every node is reachable");
                }

                foreach (int parent in parents[i])
                {
                    if (node.Type == MapNodeType.Elite || node.Type == MapNodeType.Shop || (node.Type == MapNodeType.Rest && node.Layer != rules.RestLayer))
                    {
                        Assert.AreNotEqual(node.Type, nodes[parent].Type, where + ": " + node.Type + " follows its own type");
                    }
                }

                foreach (int a in node.Next)
                {
                    foreach (MapNode other in nodes)
                    {
                        if (other.Layer != node.Layer || other.NodeId == node.NodeId)
                        {
                            continue;
                        }

                        foreach (int b in other.Next)
                        {
                            bool crosses = (node.Lane < other.Lane && nodes[a].Lane > nodes[b].Lane) || (node.Lane > other.Lane && nodes[a].Lane < nodes[b].Lane);
                            Assert.IsFalse(crosses && nodes[a].Layer != top, where + ": edges cross");
                        }
                    }
                }
            }
        }

        [Test]
        public void Generate_TooStrictCounts_AreFixedUpDeterministically()
        {
            RegionData region = _regions.GetRegion("r01");
            MapRulesData rules = FieldJson.FromJson<MapRulesData>(FieldJson.ToJson(_regions.RulesFor(region)));
            rules.NodeWeights = new[] { new NodeWeightData { Type = "Battle", Weight = 1 }, new NodeWeightData { Type = "Shop", Weight = 1000 } };
            rules.MinElites = 2;
            rules.MaxElites = 2;
            rules.MaxShops = 0;

            List<MapNode> nodes = NodeMapGenerator.Generate(region, rules, 0, 5);

            Assert.AreEqual(0, nodes.FindAll(n => n.Type == MapNodeType.Shop).Count, "extra shops become battles");
            Assert.AreEqual(2, nodes.FindAll(n => n.Type == MapNodeType.Elite).Count, "the lowest eligible battles become elites");
            Assert.AreEqual(FieldJson.ToJson(new MapRun { Nodes = nodes }), FieldJson.ToJson(new MapRun { Nodes = NodeMapGenerator.Generate(region, rules, 0, 5) }));
        }

        [Test]
        public void BeastCap_IsTheHighestSealOrTheStartingCap()
        {
            Assert.AreEqual(12, LevelCaps.BeastCap(new string[0], _regions));
            Assert.AreEqual(12, LevelCaps.BeastCap(null, _regions));
            Assert.AreEqual(42, LevelCaps.BeastCap(new[] { "seal_r01", "seal_r03", "unknown" }, _regions));
            Assert.AreEqual(100, LevelCaps.BeastCap(new[] { "seal_r10" }, _regions));
            Assert.AreEqual(BeastProgression.MaxLevel, LevelCaps.BeastCap(new[] { "seal_r01" }, null));
        }

        [Test]
        public void StartRun_RefusesLockedUnknownOrBusy_AndStoresTheMap()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.IsFalse(CampaignRules.StartRun(save, _regions, "r02", 1).Success, "locked");
            Assert.IsFalse(CampaignRules.StartRun(save, _regions, "nowhere", 1).Success);
            Assert.IsFalse(CampaignRules.StartRun(save, _regions, "r01", 2, 1).Success, "stage 2 before stage 0 is cleared");
            Assert.IsFalse(CampaignRules.StartRun(null, _regions, "r01", 1).Success);

            CampaignResult started = CampaignRules.StartRun(save, _regions, "r01", 7);

            Assert.IsTrue(started.Success, started.Error);
            Assert.AreEqual(CampaignOutcome.Started, started.Outcome);
            Assert.IsTrue(save.Campaign.HasActiveRun);
            Assert.AreEqual("r01", save.Campaign.CurrentRegionId);
            Assert.AreEqual(0, save.Campaign.ActiveRun.Stage);
            Assert.AreEqual(FieldJson.ToJson(new MapRun { Nodes = NodeMapGenerator.Generate(_regions, "r01", 0, 7) }),
                            FieldJson.ToJson(new MapRun { Nodes = save.Campaign.ActiveRun.Nodes }));
            Assert.IsFalse(CampaignRules.StartRun(save, _regions, "r01", 8).Success, "one expedition at a time");

            Assert.IsTrue(CampaignRules.Retreat(save).Success);
            Assert.IsFalse(save.Campaign.HasActiveRun);
            Assert.IsFalse(CampaignRules.Retreat(save).Success);
        }

        [Test]
        public void ALoss_StaysAtTheNode_AndARetryUsesANewBattleSeed()
        {
            PlayerSave save = PlayerSave.CreateNew();
            CampaignRules.StartRun(save, _regions, "r01", 3);
            MapRun run = save.Campaign.ActiveRun;
            List<MapNode> start = CampaignRules.Choices(run);
            Assert.IsNotEmpty(start);
            Assert.IsTrue(start.TrueForAll(n => n.Layer == 0));
            MapNode first = start[0];

            CampaignResult lost = CampaignRules.ResolveBattle(save, _regions, first.NodeId, BattleOutcome.EnemyVictory);

            Assert.AreEqual(CampaignOutcome.Lost, lost.Outcome);
            Assert.AreEqual(-1, run.CurrentNodeId, "the player stays where they were");
            Assert.AreEqual(1, run.Attempts);
            Assert.AreEqual(1, run.NodeAttempts);
            Assert.IsTrue(CampaignRules.CanEnter(run, first.NodeId), "the same node can be retried");
            Assert.AreNotEqual(CampaignRules.BattleSeed(first, 0), CampaignRules.BattleSeed(first, run.NodeAttempts));

            CampaignResult won = CampaignRules.ResolveBattle(save, _regions, first.NodeId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(CampaignOutcome.Cleared, won.Outcome);
            Assert.AreEqual(first.NodeId, run.CurrentNodeId);
            Assert.AreEqual(0, run.NodeAttempts);
            Assert.IsFalse(CampaignRules.CanEnter(run, first.NodeId), "cleared");
            foreach (MapNode choice in CampaignRules.Choices(run))
            {
                Assert.Contains(choice.NodeId, first.Next);
            }

            MapNode unreachable = run.Nodes.Find(n => n.Layer == 5);
            Assert.IsFalse(CampaignRules.ResolveBattle(save, _regions, unreachable.NodeId, BattleOutcome.PlayerVictory).Success);
        }

        [Test]
        public void AStage_WalkedToItsGate_ClearsTheStage_AndCampTrainsABeast()
        {
            PlayerSave save = PlayerSave.CreateNew();
            OwnedBeast beast = OwnedBeast.Create("b1", "emberfox", 1);
            save.Beasts.Add(beast);
            CampaignRules.StartRun(save, _regions, "r01", 11);
            bool camped = false;

            for (int guard = 0; guard < 40 && save.Campaign.HasActiveRun; guard++)
            {
                MapNode node = CampaignRules.Choices(save.Campaign.ActiveRun)[0];
                CampaignResult result;
                switch (node.Type)
                {
                    case MapNodeType.Rest:
                        int before = BeastProgression.TotalXpToReach(beast.Progress.Level) + beast.Progress.Xp;
                        result = CampaignRules.Camp(save, _regions, node.NodeId, "b1");
                        Assert.AreEqual(BeastProgression.BattleXp(BattleOutcome.PlayerVictory, node.Level, false), result.XpTrained, "a standing clear at the node's level");
                        Assert.AreEqual(before + result.XpTrained, BeastProgression.TotalXpToReach(beast.Progress.Level) + beast.Progress.Xp);
                        camped = true;
                        break;
                    case MapNodeType.Shop:
                        result = CampaignRules.Trade(save, _regions, node.NodeId, new ShopServiceStub());
                        Assert.IsFalse(result.ShopOpened);
                        break;
                    default:
                        Assert.IsFalse(CampaignRules.Camp(save, _regions, node.NodeId, "b1").Success, "only a Rest node camps");
                        result = CampaignRules.ResolveBattle(save, _regions, node.NodeId, BattleOutcome.PlayerVictory);
                        break;
                }

                Assert.IsTrue(result.Success, result.Error);
            }

            Assert.IsTrue(camped, "every path crosses the rest row");
            Assert.IsFalse(save.Campaign.HasActiveRun, "the gate ended the expedition");
            Assert.AreEqual(1, save.Campaign.FindRegion("r01").StagesCleared);
            Assert.IsFalse(save.Campaign.FindRegion("r01").BossCleared);
            Assert.AreEqual(1, CampaignRules.NextStage(save, _regions, "r01"));
        }

        [Test]
        public void TheBoss_GrantsItsSeal_ReleasesTheBanks_AndUnlocksTheNextRegion()
        {
            PlayerSave save = PlayerSave.CreateNew();
            OwnedBeast capped = OwnedBeast.Create("b1", "emberfox", 12);
            BeastProgression.AddXp(capped.Progress, 1000, 12);
            save.Beasts.Add(capped);
            save.Campaign.FindRegion("r01").StagesCleared = 3;
            CampaignRules.StartRun(save, _regions, "r01", 21);
            MapRun run = save.Campaign.ActiveRun;
            Assert.AreEqual(3, run.Stage);
            MapNode boss = run.Nodes[run.Nodes.Count - 1];
            Assert.AreEqual(MapNodeType.Boss, boss.Type);
            run.CurrentNodeId = boss.NodeId - 1;
            run.Nodes[boss.NodeId - 1].Next = new[] { boss.NodeId };

            CampaignResult result = CampaignRules.ResolveBattle(save, _regions, boss.NodeId, BattleOutcome.PlayerVictory);

            Assert.AreEqual(CampaignOutcome.RegionCleared, result.Outcome, result.Error);
            Assert.AreEqual("seal_r01", result.SealGranted);
            Assert.AreEqual(22, result.BeastLevelCap);
            Assert.AreEqual(22, CampaignRules.BeastCap(save, _regions));
            Assert.Greater(result.LevelsReleased, 0);
            Assert.Greater(capped.Progress.Level, 12, "the bank was spent at the new cap");
            Assert.AreEqual(0, capped.Progress.BankedXp);
            CollectionAssert.AreEqual(new[] { "r02" }, result.UnlockedRegionIds);
            Assert.IsTrue(save.Campaign.IsUnlocked("r02"));
            Assert.IsTrue(save.Campaign.FindRegion("r01").BossCleared);
            Assert.IsFalse(save.Campaign.HasActiveRun);
            Assert.AreEqual(3, CampaignRules.NextStage(save, _regions, "r01"), "a cleared region replays its last stage");
        }

        [Test]
        public void GrantSeal_IsTheHookForStoryEvents()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.IsFalse(CampaignRules.GrantSeal(save, _regions, "seal_nowhere").Success);
            CampaignResult granted = CampaignRules.GrantSeal(save, _regions, "seal_r04");
            CampaignResult again = CampaignRules.GrantSeal(save, _regions, "seal_r04");

            Assert.AreEqual("seal_r04", granted.SealGranted);
            Assert.AreEqual(52, granted.BeastLevelCap);
            Assert.IsTrue(again.Success);
            Assert.IsNull(again.SealGranted);
            Assert.AreEqual(1, save.Campaign.Seals.Count);
        }

        [Test]
        public void Trade_OpensTheShopService_AndMarksTheNodeVisited()
        {
            PlayerSave save = PlayerSave.CreateNew();
            MapRun run = save.Campaign.ActiveRun;
            run.RegionId = "r01";
            run.Seed = 4;
            run.Nodes.Add(new MapNode { NodeId = 0, Layer = 0, Type = MapNodeType.Shop, Level = 3, EncounterSeed = 77, Next = new[] { 1 } });
            run.Nodes.Add(new MapNode { NodeId = 1, Layer = 1, Type = MapNodeType.Battle, Level = 3, ShapeId = "squad" });
            RecordingShop shop = new RecordingShop();

            Assert.IsFalse(CampaignRules.ResolveBattle(save, _regions, 0, BattleOutcome.PlayerVictory).Success, "a shop is not a battle");
            CampaignResult result = CampaignRules.Trade(save, _regions, 0, shop);

            Assert.IsTrue(result.ShopOpened);
            Assert.AreEqual("r01", shop.Context.RegionId);
            Assert.AreEqual(3, shop.Context.Level);
            Assert.AreEqual(77, shop.Context.Seed);
            Assert.AreEqual(0, run.CurrentNodeId);
            Assert.IsFalse(CampaignRules.Trade(save, _regions, 1, shop).Success, "not a shop");
            Assert.IsEmpty(SaveValidator.Validate(save, null));
        }

        [Test]
        public void PlanFor_FieldsTheNodesEncounter()
        {
            EncounterLibrary encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary());
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
            List<MapNode> nodes = NodeMapGenerator.Generate(_regions, "r05", 3, 17);
            MapNode battle = nodes.Find(n => n.Type == MapNodeType.Battle);
            MapNode boss = nodes[nodes.Count - 1];
            MapNode rest = nodes.Find(n => n.Type == MapNodeType.Rest);

            EncounterPlan battlePlan = CampaignRules.PlanFor(battle, encounters, enemies);
            EncounterPlan again = CampaignRules.PlanFor(battle, encounters, enemies);
            EncounterPlan bossPlan = CampaignRules.PlanFor(boss, encounters, enemies);

            Assert.AreEqual(battle.ShapeId, battlePlan.ShapeId);
            Assert.AreEqual(battle.Level, battlePlan.Level);
            Assert.AreEqual(battlePlan.Enemies.Count, again.Enemies.Count, "a retry fields the same lineup");
            Assert.AreEqual(_regions.GetRegion("r05").BossTemplateId, bossPlan.EncounterId);
            Assert.AreEqual(50, bossPlan.Level);
            Assert.IsNull(CampaignRules.PlanFor(rest, encounters, enemies));
        }

        [Test]
        public void ASaveMidExpedition_RoundTripsAndValidatesAgainstTheAuthoredContent()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", BeastRosterTests.LoadRoster().Species[0].SpeciesId, 5));
            CampaignRules.StartRun(save, _regions, "r01", 5);
            CampaignRules.ResolveBattle(save, _regions, CampaignRules.Choices(save.Campaign.ActiveRun)[0].NodeId, BattleOutcome.PlayerVictory);
            CampaignRules.GrantSeal(save, _regions, "seal_r01");
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(),
                                                           SaveContentCatalog.FromData(BeastRosterTests.LoadRoster(), SkillLibraryTests.LoadLibrary(), _data));

            string json = serializer.Serialize(save);
            SaveLoadResult loaded = serializer.Deserialize(json);

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual(json, serializer.Serialize(loaded.Save));
            Assert.AreEqual(save.Campaign.ActiveRun.Nodes.Count, loaded.Save.Campaign.ActiveRun.Nodes.Count);

            loaded.Save.Campaign.Seals.Add("seal_made_up");
            Assert.AreEqual(1, SaveValidator.Validate(loaded.Save, SaveContentCatalog.FromData(null, null, _data)).FindAll(i => i.Kind == SaveIssueKind.UnknownSeal).Count);
        }

        private sealed class RecordingShop : IShopService
        {
            public ShopContext Context;

            public bool Open(PlayerSave save, ShopContext context)
            {
                Context = context;
                return true;
            }

            public BeastCraft.Economy.ShopVisit GetStock(PlayerSave save, ShopContext context)
            {
                return null;
            }

            public BeastCraft.Economy.ShopPurchaseResult TryBuy(PlayerSave save, ShopContext context, int listingIndex, string targetBeastId = null)
            {
                return new ShopServiceStub().TryBuy(save, context, listingIndex, targetBeastId);
            }

            public BeastCraft.Economy.ShopSaleResult TrySellGear(PlayerSave save, string gearInstanceId)
            {
                return new ShopServiceStub().TrySellGear(save, gearInstanceId);
            }
        }
    }
}
