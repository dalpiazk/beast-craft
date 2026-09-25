using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Post-game regions (<see cref="RegionData.IsPostGame"/>) and post-game shapes
    /// (<see cref="EncounterShapeData.PostGame"/>, <see cref="EncounterShapeData.DropShapeId"/>): the
    /// validators' separate post-game pass, which must leave every mainline message unchanged; the
    /// Hard difficulty's effective region and rules (<see cref="RegionLibrary.RegionFor"/>); and the
    /// drop shape a post-game encounter pays out as. Built on the authored mainline data plus a
    /// synthetic post-game region, so it holds whatever the authored post-game content is.
    /// </summary>
    public class PostGameRegionTests
    {
        private static readonly string[] Mainline = { "squad", "horde", "elite" };

        /// <summary>The authored encounter library with synthetic post-game shapes and boss templates added (unless the file already has them).</summary>
        internal static EncounterLibraryData EncountersWithPostGame()
        {
            EncounterLibraryData data = EncounterContentTests.LoadEncounterLibrary();
            List<EncounterShapeData> shapes = new List<EncounterShapeData>(data.Shapes);
            foreach (string suffix in new[] { "_pg_test", "_pg_test_hard" })
            {
                foreach (string mainline in Mainline)
                {
                    EncounterShapeData source = Array.Find(data.Shapes, s => s.ShapeId == mainline);
                    shapes.Add(new EncounterShapeData
                    {
                        ShapeId = mainline + suffix,
                        DisplayName = source.DisplayName,
                        Description = source.Description,
                        Arena = source.Arena,
                        ThreatMin = source.ThreatMin,
                        ThreatMax = source.ThreatMax,
                        MinDistinctTypes = source.MinDistinctTypes,
                        TargetClear = source.TargetClear / 2.0,
                        Variants = source.Variants,
                        PostGame = true,
                        DropShapeId = mainline
                    });
                }
            }

            data.Shapes = shapes.ToArray();
            List<EncounterTemplateData> templates = new List<EncounterTemplateData>(data.Templates);
            foreach (string id in new[] { "boss_pg_test", "boss_pg_test_hard" })
            {
                templates.Add(new EncounterTemplateData
                {
                    EncounterId = id,
                    DisplayName = "Test Twins",
                    Description = "Two test giants.",
                    ShapeId = "elite",
                    Arena = "Large",
                    Groups = new[] { new EncounterGroupData { EnemyId = "giant", Count = 2, Elements = new[] { "Light", "Dark" } } },
                    DifficultyOverride = 1.0,
                    Draft = true
                });
            }

            data.Templates = templates.ToArray();
            return data;
        }

        /// <summary>A valid post-game region over <see cref="EncountersWithPostGame"/>'s shapes, requiring r10.</summary>
        internal static RegionData PostGameRegion(RegionLibraryData library)
        {
            MapRulesData rules = library.MapRules.Copy();
            rules.NodeWeights = new[]
            {
                new NodeWeightData { Type = "Battle", Weight = 55 }, new NodeWeightData { Type = "Elite", Weight = 30 },
                new NodeWeightData { Type = "Shop", Weight = 5 }, new NodeWeightData { Type = "Rest", Weight = 10 }
            };
            rules.EliteLevelOffset = 0;
            rules.EliteShapeId = "elite_pg_test";
            return new RegionData
            {
                RegionId = "pg_test",
                DisplayName = "Test Meridian",
                Description = "A test region.",
                MinLevel = 100,
                MaxLevel = 100,
                Stages = 4,
                RequiresRegionId = "r10",
                ShapeWeights = new[] { new ShapeWeightData { ShapeId = "squad_pg_test", Weight = 45 }, new ShapeWeightData { ShapeId = "horde_pg_test", Weight = 40 } },
                BossTemplateId = "boss_pg_test",
                BossRewardSealId = string.Empty,
                MapRules = rules,
                IsPostGame = true,
                HardMode = new RegionHardModeData
                {
                    ShapeWeights = new[]
                    {
                        new ShapeWeightData { ShapeId = "squad_pg_test_hard", Weight = 45 }, new ShapeWeightData { ShapeId = "horde_pg_test_hard", Weight = 40 }
                    },
                    EliteShapeId = "elite_pg_test_hard",
                    BossTemplateId = "boss_pg_test_hard"
                }
            };
        }

        private static RegionLibraryData WithPostGame(RegionLibraryData data, RegionData region)
        {
            List<RegionData> regions = new List<RegionData>(data.Regions) { region };
            data.Regions = regions.ToArray();
            return data;
        }

        [Test]
        public void MainlineMessages_AreUnchangedByAValidPostGameRegion()
        {
            EncounterLibraryData encounters = EncountersWithPostGame();
            foreach (KeyValuePair<string, Action<RegionLibraryData>> entry in GoldenRegionValidatorTests.Cases())
            {
                RegionLibraryData mainline = GoldenRegionValidatorTests.LoadMainline();
                entry.Value(mainline);
                RegionLibraryData both = GoldenRegionValidatorTests.LoadMainline();
                entry.Value(both);
                if (both.Regions.Length == 0 || both.Regions[both.Regions.Length - 1] == null || both.Regions[both.Regions.Length - 1].RegionId != "r10")
                {
                    continue;
                }

                WithPostGame(both, PostGameRegion(GoldenRegionValidatorTests.LoadMainline()));

                CollectionAssert.AreEqual(RegionLibraryValidator.Validate(mainline, encounters), RegionLibraryValidator.Validate(both, encounters), entry.Key);
                CollectionAssert.AreEqual(RegionLibraryValidator.Validate(mainline), RegionLibraryValidator.Validate(both), entry.Key + " (no encounter library)");
            }
        }

        [Test]
        public void ValidPostGameRegion_PassesAndIsSkippedByTheBand()
        {
            RegionLibraryData data = WithPostGame(GoldenRegionValidatorTests.LoadMainline(), PostGameRegion(GoldenRegionValidatorTests.LoadMainline()));

            List<string> errors = RegionLibraryValidator.Validate(data, EncountersWithPostGame());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void BrokenPostGameRegion_IsReportedByThePostGamePass()
        {
            RegionLibraryData data = GoldenRegionValidatorTests.LoadMainline();
            RegionData region = PostGameRegion(data);
            region.MinLevel = 95;
            region.RequiresRegionId = "pg_test";
            region.BossRewardSealId = "seal_r10";
            region.Stages = 0;
            region.ShapeWeights = new[] { new ShapeWeightData { ShapeId = "squad", Weight = 10 } };
            region.HardMode.EliteShapeId = string.Empty;
            region.HardMode.BossTemplateId = "no_boss";
            WithPostGame(data, region);

            string all = string.Join("\n", RegionLibraryValidator.Validate(data, EncountersWithPostGame()));

            StringAssert.Contains("Post-game region 'pg_test': RequiresRegionId 'pg_test' must name an earlier region.", all);
            StringAssert.Contains("Post-game region 'pg_test': MinLevel and MaxLevel must both be 100", all);
            StringAssert.Contains("Post-game region 'pg_test': BossRewardSealId must be empty", all);
            StringAssert.Contains("Post-game region 'pg_test': Stages must be at least 1.", all);
            StringAssert.Contains("Post-game region 'pg_test': shape 'squad' is a mainline shape", all);
            StringAssert.Contains("Post-game region 'pg_test' HardMode: no EliteShapeId.", all);
            StringAssert.Contains("Post-game region 'pg_test' HardMode: boss template 'no_boss' is not in the encounter library.", all);
            StringAssert.DoesNotContain("The last region must end", all, "the post-game region is not part of the band");
        }

        [Test]
        public void PostGamePass_RejectsMisplacedAndDuplicateRegions()
        {
            RegionLibraryData data = GoldenRegionValidatorTests.LoadMainline();
            RegionData duplicate = PostGameRegion(data);
            duplicate.RegionId = "r05";
            RegionData missingHard = PostGameRegion(data);
            missingHard.RegionId = "pg_two";
            missingHard.HardMode = new RegionHardModeData();
            WithPostGame(data, duplicate);
            WithPostGame(data, missingHard);
            data.Regions[3].HardMode = new RegionHardModeData { EliteShapeId = "elite" };
            data.Regions[4].ShapeWeights = new[] { new ShapeWeightData { ShapeId = "squad_pg_test", Weight = 1 } };

            string all = string.Join("\n", RegionLibraryValidator.Validate(data, EncountersWithPostGame()));

            StringAssert.Contains("Post-game region 'r05': duplicate RegionId.", all);
            StringAssert.Contains("Post-game region 'pg_two' HardMode: no ShapeWeights", all);
            StringAssert.Contains("Post-game region 'pg_two' HardMode: no BossTemplateId.", all);
            StringAssert.Contains("Region 'r04': only a post-game region (IsPostGame) has a HardMode.", all);
            StringAssert.Contains("Region 'r05': shape 'squad_pg_test' is a post-game shape; only post-game regions draw it.", all);

            RegionLibraryData first = GoldenRegionValidatorTests.LoadMainline();
            first.Regions[0].IsPostGame = true;
            StringAssert.Contains("the first region cannot be a post-game region", string.Join("\n", RegionLibraryValidator.Validate(first)));
        }

        [Test]
        public void EncounterValidator_HoldsPostGameShapesToAMainlineDropShape()
        {
            EncounterLibraryData data = EncountersWithPostGame();
            Assert.IsEmpty(EncounterLibraryValidator.Validate(data, EncounterContentTests.LoadEnemyLibrary(), DropTableTests.LoadTables()),
                           "post-game shapes pay as mainline shapes, so the drop tables stay mainline");

            data.Shapes[data.Shapes.Length - 1].DropShapeId = string.Empty;
            data.Shapes[data.Shapes.Length - 2].DropShapeId = "squad_pg_test";
            data.Shapes[0].DropShapeId = "elite";
            string all = string.Join("\n", EncounterLibraryValidator.Validate(data, null, null));

            StringAssert.Contains("Shape 'elite_pg_test_hard': a post-game shape names the mainline shape it pays out as (DropShapeId).", all);
            StringAssert.Contains("Shape 'horde_pg_test_hard': DropShapeId 'squad_pg_test' must name a mainline shape of this library.", all);
            StringAssert.Contains("Shape '" + data.Shapes[0].ShapeId + "': only a post-game shape (PostGame) pays out as another shape (DropShapeId).", all);
        }

        [Test]
        public void HardDifficulty_OnlyInAPostGameRegion_SwapsShapesAndBoss()
        {
            RegionLibraryData data = WithPostGame(GoldenRegionValidatorTests.LoadMainline(), PostGameRegion(GoldenRegionValidatorTests.LoadMainline()));
            RegionLibrary library = RegionLibrary.Build(data);
            RegionData r10 = library.GetRegion("r10");
            RegionData postGame = library.GetRegion("pg_test");

            Assert.IsTrue(RegionLibrary.Allows(r10, RunDifficulty.Normal));
            Assert.IsFalse(RegionLibrary.Allows(r10, RunDifficulty.Hard));
            Assert.IsNull(library.RegionFor(r10, RunDifficulty.Hard));
            Assert.IsNull(library.RulesFor(r10, RunDifficulty.Hard));
            Assert.AreSame(postGame, library.RegionFor(postGame, RunDifficulty.Normal));
            Assert.AreSame(library.RulesFor(postGame), library.RulesFor(postGame, RunDifficulty.Normal));

            RegionData hard = library.RegionFor(postGame, RunDifficulty.Hard);
            MapRulesData hardRules = library.RulesFor(postGame, RunDifficulty.Hard);
            Assert.AreEqual("boss_pg_test_hard", hard.BossTemplateId);
            Assert.AreEqual("squad_pg_test_hard", hard.ShapeWeights[0].ShapeId);
            Assert.AreEqual("elite_pg_test_hard", hardRules.EliteShapeId);
            Assert.AreEqual("boss_pg_test", postGame.BossTemplateId, "the authored region is untouched");
            Assert.AreEqual("elite_pg_test", library.RulesFor(postGame).EliteShapeId, "the authored rules are untouched");
            CollectionAssert.AreEqual(new[] { "r01", "r02", "r03", "r04", "r05", "r06", "r07", "r08", "r09", "r10" }, library.MainlineRegions().ConvertAll(r => r.RegionId));
            CollectionAssert.AreEqual(new[] { "pg_test" }, library.PostGameRegions().ConvertAll(r => r.RegionId));

            // Same seed, same weights in the same order: the same map, only harder ids; every node at level 100.
            for (int stage = 0; stage < postGame.Stages; stage++)
            {
                List<MapNode> normal = NodeMapGenerator.Generate(postGame, library.RulesFor(postGame), stage, 4242);
                List<MapNode> harder = NodeMapGenerator.Generate(hard, hardRules, stage, 4242);
                Assert.AreEqual(normal.Count, harder.Count);
                for (int i = 0; i < normal.Count; i++)
                {
                    Assert.AreEqual(normal[i].Type, harder[i].Type);
                    Assert.AreEqual(100, normal[i].Level);
                    Assert.AreEqual(100, harder[i].Level);
                    Assert.AreEqual(normal[i].EncounterSeed, harder[i].EncounterSeed);
                    Assert.AreEqual(string.IsNullOrEmpty(normal[i].ShapeId) ? string.Empty : normal[i].ShapeId + "_hard", harder[i].ShapeId);
                    if (normal[i].Type == MapNodeType.Boss)
                    {
                        Assert.AreEqual("boss_pg_test", normal[i].TemplateId);
                        Assert.AreEqual("boss_pg_test_hard", harder[i].TemplateId);
                    }
                }
            }
        }

        [Test]
        public void PostGameEncounter_PaysOutAsItsMainlineShape()
        {
            EncounterLibrary encounters = EncounterLibrary.Build(EncountersWithPostGame());
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);

            Assert.AreEqual("squad", encounters.DropShapeOf("squad_pg_test_hard"));
            Assert.AreEqual("squad", encounters.DropShapeOf("squad"));
            Assert.AreEqual("nope", encounters.DropShapeOf("nope"));

            EncounterPlan plan = EncounterPlan.Generate(encounters, enemies, "horde_pg_test", 100, 7);
            Assert.IsNotNull(plan);
            Assert.AreEqual("horde_pg_test", plan.ShapeId, "the difficulty row is the post-game shape's own");
            Assert.AreEqual("horde", plan.DropShapeId);
            Assert.AreEqual("horde", plan.ToSetup().ShapeId, "the rewards pay the mainline cell");

            EncounterPlan boss = EncounterPlan.FromTemplate(encounters, enemies, "boss_pg_test_hard", 100);
            Assert.AreEqual("elite", boss.DropShapeId);
            Assert.AreEqual(1.0, boss.Multiplier);
        }
    }
}
