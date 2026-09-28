using System.Collections.Generic;
using System.Linq;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The early-region easing (<see cref="RegionData.StageDifficultyScale"/>): the authored curve,
    /// the validator's rules, and that <see cref="CampaignRules.PlanFor(MapRun, MapNode, EncounterLibrary, EnemyCatalog, RegionLibrary)"/>
    /// applies it to campaign battles in r01-r02 only. The curve itself is measured by the balance
    /// simulator's <c>--mode newplayer</c> (docs/balance/tuning-log.md, "Early-region easing").
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
        public void AuthoredCurve_EasesR01AndR02_AndFadesToOneByTheEndOfR02()
        {
            RegionData r01 = _regions.GetRegion("r01");
            RegionData r02 = _regions.GetRegion("r02");
            Assert.AreEqual(r01.Stages, r01.StageDifficultyScale.Length);
            Assert.AreEqual(r02.Stages, r02.StageDifficultyScale.Length);
            Assert.Less(r01.StageDifficultyScale[0], 1.0, "r01's first stage is eased");
            Assert.AreEqual(1.0, r02.StageDifficultyScale[r02.Stages - 1], "the easing is gone by the end of r02");

            double previous = 0.0;
            foreach (double scale in new List<double>(r01.StageDifficultyScale).Concat(r02.StageDifficultyScale))
            {
                Assert.GreaterOrEqual(scale, previous, "the easing only fades out");
                previous = scale;
            }

            foreach (RegionData region in _regions.Regions)
            {
                if (region.RegionId != "r01" && region.RegionId != "r02")
                {
                    Assert.IsEmpty(region.StageDifficultyScale, region.RegionId + ": no easing after r02");
                    for (int stage = 0; stage < region.Stages; stage++)
                    {
                        Assert.AreEqual(1.0, _regions.DifficultyScaleFor(region.RegionId, stage), region.RegionId);
                    }
                }
            }

            Assert.AreEqual(1.0, _regions.DifficultyScaleFor("nowhere", 0));
            Assert.AreEqual(1.0, _regions.DifficultyScaleFor("r01", 9), "a stage past the entries");
        }

        [Test]
        public void Validator_RefusesABadCurve()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            data.Regions[0].StageDifficultyScale = new[] { 0.8, 0.9 };
            data.Regions[1].StageDifficultyScale = new[] { 0.9, 0.85, 1.2, 0.0 };
            data.Regions[10].StageDifficultyScale = new[] { 0.9, 0.9, 0.9, 0.9 };

            List<string> errors = RegionLibraryValidator.Validate(data);

            Assert.That(errors, Has.Some.Contains("Region 'r01': StageDifficultyScale has 2 entries"));
            Assert.That(errors, Has.Some.Contains("StageDifficultyScale[1] 0.85 is below the stage before it"));
            Assert.That(errors, Has.Some.Contains("StageDifficultyScale[2] 1.2 must be above 0 and at most 1"));
            Assert.That(errors, Has.Some.Contains("StageDifficultyScale[3] 0 must be above 0 and at most 1"));
            Assert.That(errors, Has.Some.Contains("Post-game region 'r11': StageDifficultyScale must be empty"));

            RegionLibraryData unfinished = CampaignMapTests.LoadRegions();
            unfinished.Regions[1].StageDifficultyScale = new[] { 0.9, 0.9, 0.95, 0.95 };
            Assert.That(RegionLibraryValidator.Validate(unfinished), Has.Some.Contains("Region 'r03': no StageDifficultyScale after an easing that ended at 0.95"));
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
                    double scale = _regions.DifficultyScaleFor(regionId, stage);
                    foreach (MapNode node in run.Nodes)
                    {
                        if (!node.IsBattle)
                        {
                            continue;
                        }

                        EncounterPlan plain = CampaignRules.PlanFor(node, _encounters, _enemies);
                        EncounterPlan eased = CampaignRules.PlanFor(run, node, _encounters, _enemies, _regions);
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

            Assert.Less(_regions.DifficultyScaleFor("r01", 0), 1.0, "r01 stage 1 is eased (the loop above tested a real scale)");
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
