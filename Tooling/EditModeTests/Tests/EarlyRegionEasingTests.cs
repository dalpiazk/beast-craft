using System.Collections.Generic;
using System.Linq;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The early-region easing (<see cref="RegionData.StageEasing"/> of <see cref="RegionLibraryData.EasingShapeScales"/>): the authored curve,
    /// the validator's rules, and that <see cref="CampaignRules.PlanFor(MapRun, MapNode, EncounterLibrary, EnemyCatalog, RegionLibrary)"/>
    /// applies it to campaign battles in r01-r03 only. The curve itself is measured by the balance
    /// simulator's <c>--mode newplayer</c> (docs/balance/tuning-log.md, "Early-region easing", and
    /// "Kinship: roster growth and the r02 fall-off", which kept the full discount through r02 and moved
    /// the fade into r03's first stages).
    /// </summary>
    public class EarlyRegionEasingTests
    {
        private RegionLibraryData _data;
        private RegionLibrary _regions;
        private EncounterLibrary _encounters;
        private EnemyCatalog _enemies;

        [SetUp]
        public void SetUp()
        {
            _data = CampaignMapTests.LoadRegions();
            _regions = RegionLibrary.Build(_data);
            _encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(),
                                                 EncounterDifficultyTable.Build(EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath)));
            _enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
        }

        [Test]
        public void AuthoredCurve_EasesEachShapeFullyThroughR02_AndFadesToNothingInR03()
        {
            foreach (string key in new[] { "squad", "horde", "solo", "elite", RegionLibraryData.EasingBossId })
            {
                Assert.Less(_regions.FullEasingScale(key), 1.0, key + " has its own discount");
                Assert.Greater(_regions.FullEasingScale(key), 0.5, key);
            }

            Assert.AreEqual(1.0, _regions.FullEasingScale("squad_postgame"), "a kind not listed is never eased");
            Assert.Less(_regions.FullEasingScale("elite"), _regions.FullEasingScale("squad"), "the shapes need different discounts");

            RegionData r01 = _regions.GetRegion("r01");
            RegionData r02 = _regions.GetRegion("r02");
            RegionData r03 = _regions.GetRegion("r03");
            Assert.AreEqual(r01.Stages, r01.StageEasing.Length);
            Assert.AreEqual(r02.Stages, r02.StageEasing.Length);
            Assert.AreEqual(r03.Stages, r03.StageEasing.Length);
            Assert.AreEqual(1.0, _regions.EasingWeight("r01", 0), "r01's first stage gets the full discount");
            Assert.AreEqual(1.0, _regions.EasingWeight("r02", r02.Stages - 1), "the full discount lasts through r02 (the roster-limited fall-off)");
            Assert.AreEqual(0.0, _regions.EasingWeight("r03", r03.Stages - 1), "the easing is gone by the end of r03");
            Assert.AreEqual(1.0, _regions.DifficultyScaleFor("r03", r03.Stages - 1, "elite"));
            Assert.AreEqual(_regions.FullEasingScale("elite"), _regions.DifficultyScaleFor("r01", 0, "elite"), 1e-12);
            Assert.AreEqual(0.92, _regions.DifficultyScaleFor("r02", r02.Stages - 1, RegionLibraryData.EasingBossId), 1e-12, "r02's boss takes its own scale (BossScale)");
            Assert.AreEqual(_regions.FullEasingScale("elite"), _regions.DifficultyScaleFor("r02", r02.Stages - 1, "elite"), 1e-12, "the rest of r02's last stage keeps the full discount");
            Assert.AreEqual(_regions.FullEasingScale(RegionLibraryData.EasingBossId), _regions.DifficultyScaleFor("r01", r01.Stages - 1, RegionLibraryData.EasingBossId), 1e-12,
                            "r01's boss takes the shared boss discount");
            double part = _regions.EasingWeight("r03", 0);
            Assert.Less(part, 1.0);
            Assert.Greater(part, 0.0);
            Assert.AreEqual(1.0 - (part * (1.0 - _regions.FullEasingScale("horde"))), _regions.DifficultyScaleFor("r03", 0, "horde"), 1e-12, "a partial weight scales the discount");

            double previous = 1.0;
            foreach (double weight in new List<double>(r01.StageEasing).Concat(r02.StageEasing).Concat(r03.StageEasing))
            {
                Assert.LessOrEqual(weight, previous, "the easing only fades out");
                previous = weight;
            }

            foreach (RegionData region in _regions.Regions)
            {
                if (region.RegionId != "r01" && region.RegionId != "r02" && region.RegionId != "r03")
                {
                    Assert.IsEmpty(region.StageEasing, region.RegionId + ": no easing after r03");
                    for (int stage = 0; stage < region.Stages; stage++)
                    {
                        Assert.AreEqual(1.0, _regions.DifficultyScaleFor(region.RegionId, stage, "squad"), region.RegionId);
                    }
                }
            }

            Assert.AreEqual(1.0, _regions.DifficultyScaleFor("nowhere", 0, "squad"));
            Assert.AreEqual(1.0, _regions.DifficultyScaleFor("r01", 9, "squad"), "a stage past the entries");
        }

        [Test]
        public void Validator_RefusesABadCurve()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            data.EasingShapeScales = new[]
            {
                new ShapeScaleData { ShapeId = "squad", Scale = 0.8 },
                new ShapeScaleData { ShapeId = "squad", Scale = 0.9 },
                new ShapeScaleData { ShapeId = "nope", Scale = 0.9 },
                new ShapeScaleData { ShapeId = "horde", Scale = 1.5 }
            };
            data.Regions[0].StageEasing = new[] { 1.0, 1.0 };
            data.Regions[1].StageEasing = new[] { 0.5, 0.75, 1.2, -0.1 };
            data.Regions[10].StageEasing = new[] { 1.0, 1.0, 1.0, 1.0 };
            data.Regions[3].BossScale = 0.9;
            data.Regions[10].BossScale = 0.9;

            List<string> errors = RegionLibraryValidator.Validate(data, EncounterContentTests.LoadEncounterLibrary());

            Assert.That(errors, Has.Some.Contains("EasingShapeScales 'squad': missing or repeated ShapeId"));
            Assert.That(errors, Has.Some.Contains("EasingShapeScales 'nope': not a shape"));
            Assert.That(errors, Has.Some.Contains("EasingShapeScales 'horde': Scale 1.5 must be above 0 and at most 1"));
            Assert.That(errors, Has.Some.Contains("Region 'r01': StageEasing has 2 entries"));
            Assert.That(errors, Has.Some.Contains("StageEasing[1] 0.75 is above the stage before it"));
            Assert.That(errors, Has.Some.Contains("StageEasing[2] 1.2 must be between 0 and 1"));
            Assert.That(errors, Has.Some.Contains("StageEasing[3] -0.1 must be between 0 and 1"));
            Assert.That(errors, Has.Some.Contains("Post-game region 'r11': StageEasing must be empty"));
            Assert.That(errors, Has.Some.Contains("Region 'r04': BossScale 0.9 must be 0 (none), or in (0, 1] on a region whose last stage is eased"));
            Assert.That(errors, Has.Some.Contains("Post-game region 'r11': BossScale must be 0"));

            RegionLibraryData unfinished = CampaignMapTests.LoadRegions();
            unfinished.Regions[2].StageEasing = new[] { 1.0, 0.5, 0.25, 0.25 };
            Assert.That(RegionLibraryValidator.Validate(unfinished), Has.Some.Contains("Region 'r04': no StageEasing after an easing that ended at 0.25"));
        }

        [Test]
        public void PlanFor_WithTheRun_ScalesCampaignBattlesInTheEasedStagesOnly()
        {
            for (int region = 0; region < 3; region++)
            {
                string regionId = "r0" + (region + 1);
                for (int stage = 0; stage < 4; stage++)
                {
                    MapRun run = Run(regionId, stage, 11 + stage);
                    foreach (MapNode node in run.Nodes)
                    {
                        if (!node.IsBattle)
                        {
                            continue;
                        }

                        EncounterPlan plain = CampaignRules.PlanFor(node, _encounters, _enemies);
                        EncounterPlan eased = CampaignRules.PlanFor(run, node, _encounters, _enemies, _regions);
                        double scale = _regions.DifficultyScaleFor(regionId, stage, plain.EncounterId == null ? plain.ShapeId : RegionLibraryData.EasingBossId);
                        string where = regionId + " stage " + stage + " node " + node.NodeId;
                        Assert.AreEqual(1.0, plain.DifficultyScale, where + ": outside a run, never eased");
                        Assert.AreEqual(scale, eased.DifficultyScale, 1e-12, where);
                        Assert.AreEqual(plain.Multiplier * scale, eased.Multiplier, 1e-12, where);
                        Assert.AreEqual(plain.Enemies.Count, eased.Enemies.Count, where + ": the same lineup");
                        Assert.AreEqual(plain.Level, eased.Level, where);
                        Assert.AreEqual(eased.Multiplier, eased.ToSetup("r01").Enemies[0].StatMultiplier, 1e-12, where + ": the battle fields the eased multiplier");
                    }
                }
            }

            Assert.Less(_regions.DifficultyScaleFor("r01", 0, "squad"), 1.0, "r01 stage 1 is eased (the loop above tested a real scale)");
            MapRun bossRun = Run("r01", 3, 5);
            MapNode boss = bossRun.Nodes[bossRun.Nodes.Count - 1];
            EncounterPlan bossPlan = CampaignRules.PlanFor(bossRun, boss, _encounters, _enemies, _regions);
            Assert.IsNotNull(bossPlan.EncounterId, "the lair fields the boss template");
            Assert.AreEqual(_regions.FullEasingScale(RegionLibraryData.EasingBossId), bossPlan.DifficultyScale, 1e-12, "the boss takes the boss discount, not its shape's");
        }

        [Test]
        public void Scaled_IsACopy_AndOneIsItself()
        {
            MapRun run = Run("r01", 0, 3);
            MapNode node = run.Nodes.Find(n => n.IsBattle);
            EncounterPlan plan = CampaignRules.PlanFor(node, _encounters, _enemies);

            Assert.AreSame(plan, plan.Scaled(1.0));
            Assert.AreSame(plan, plan.Scaled(0.0));
            EncounterPlan half = plan.Scaled(0.5);
            Assert.AreNotSame(plan, half);
            Assert.AreEqual(plan.Multiplier * 0.5, half.Multiplier, 1e-12);
            Assert.AreEqual(0.5, half.DifficultyScale);
            Assert.AreEqual(1.0, plan.DifficultyScale, "the original is unchanged");
            Assert.AreEqual(0.25, half.Scaled(0.5).DifficultyScale, 1e-12, "scales compose");
        }

        private MapRun Run(string regionId, int stage, int seed)
        {
            MapRun run = new MapRun { RegionId = regionId, Stage = stage, Seed = seed };
            run.Nodes = NodeMapGenerator.Generate(_regions, regionId, stage, seed);
            return run;
        }
    }
}
