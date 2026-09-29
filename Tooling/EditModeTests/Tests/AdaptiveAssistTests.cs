using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Adaptive assist (producer decision "assist + guidance", docs/balance/tuning-log.md,
    /// "Never-blocked targets"): <see cref="RegionLibrary.AssistScaleFor"/>'s step-and-floor curve,
    /// the validator's rules for <see cref="RegionLibraryData.AssistFloorScales"/> and
    /// <see cref="RegionLibraryData.AssistStep"/>, that <see cref="EncounterPlan.WithAssist"/> tracks
    /// it apart from the early-region easing, that <see cref="CampaignRules.PlanFor(MapRun, MapNode, EncounterLibrary, EnemyCatalog, RegionLibrary)"/>
    /// applies it from the consecutive losses at a location and turns it off on post-game Hard, and
    /// the preview's ALWAYS-shown matchup warnings (<see cref="MatchupWarnings"/>).
    /// </summary>
    public class AdaptiveAssistTests
    {
        // ---------------------------------------------------------------------------------------
        // RegionLibrary.AssistScaleFor / AssistFloorScale
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AssistScaleFor_StepsDownEachLoss_ThenClampsAtTheFloor()
        {
            RegionLibrary regions = Library(step: 0.10, floors: new[] { ("squad", 0.5) });

            Assert.AreEqual(1.0, regions.AssistScaleFor("r01", "squad", 0), "no losses, no assist");
            Assert.AreEqual(0.9, regions.AssistScaleFor("r01", "squad", 1), 1e-9);
            Assert.AreEqual(0.81, regions.AssistScaleFor("r01", "squad", 2), 1e-9);
            Assert.AreEqual(0.729, regions.AssistScaleFor("r01", "squad", 3), 1e-9);

            // Keeps compounding until it would cross the floor, then holds there.
            double atFloor = regions.AssistScaleFor("r01", "squad", 50);
            Assert.AreEqual(0.5, atFloor, 1e-9);
            Assert.AreEqual(0.5, regions.AssistScaleFor("r01", "squad", 51), 1e-9, "never below the floor");
        }

        [Test]
        public void AssistScaleFor_IsOne_WithNoFloorForTheShape_OrNoStep()
        {
            RegionLibrary assisted = Library(step: 0.10, floors: new[] { ("squad", 0.5) });
            Assert.AreEqual(1.0, assisted.AssistScaleFor("r01", "horde", 5), "horde has no floor: never assisted");

            RegionLibrary noStep = Library(step: 0.0, floors: new[] { ("squad", 0.5) });
            Assert.AreEqual(1.0, noStep.AssistScaleFor("r01", "squad", 5), "step 0 disables assist everywhere");
        }

        [Test]
        public void AssistFloorScale_IsOne_WhenNotListed()
        {
            RegionLibrary regions = Library(step: 0.10, floors: new[] { ("squad", 0.5) });
            Assert.AreEqual(1.0, regions.AssistFloorScale("elite"));
            Assert.AreEqual(0.5, regions.AssistFloorScale("squad"));
        }

        [Test]
        public void AssistScaleFor_UsesTheRegionsOwnBossFloor_WhenItSetsOne()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            data.AssistStep = 0.10;
            data.AssistFloorScales = new[] { new ShapeScaleData { ShapeId = RegionLibraryData.EasingBossId, Scale = 0.8 } };
            RegionData r01 = System.Array.Find(data.Regions, r => r.RegionId == "r01");
            r01.AssistBossFloorScale = 0.5;
            RegionLibrary regions = RegionLibrary.Build(data);

            Assert.AreEqual(0.5, regions.AssistScaleFor("r01", RegionLibraryData.EasingBossId, 99), 1e-9, "r01's own floor overrides the shared one");
            RegionData other = System.Array.Find(data.Regions, r => r.RegionId != "r01");
            Assert.AreEqual(0.8, regions.AssistScaleFor(other.RegionId, RegionLibraryData.EasingBossId, 99), 1e-9, "elsewhere uses the shared floor");
        }

        // ---------------------------------------------------------------------------------------
        // Validator
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Validator_RefusesABadAssistCurve()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            data.AssistStep = 1.0;
            data.AssistFloorScales = new[]
            {
                new ShapeScaleData { ShapeId = "squad", Scale = 0.7 },
                new ShapeScaleData { ShapeId = "squad", Scale = 0.6 },
                new ShapeScaleData { ShapeId = "nope", Scale = 0.6 },
                new ShapeScaleData { ShapeId = "horde", Scale = 1.5 }
            };

            List<string> errors = RegionLibraryValidator.Validate(data, EncounterContentTests.LoadEncounterLibrary());

            Assert.That(errors, Has.Some.Contains("AssistStep 1 must be in [0, 1)"));
            Assert.That(errors, Has.Some.Contains("AssistFloorScales 'squad': missing or repeated ShapeId"));
            Assert.That(errors, Has.Some.Contains("AssistFloorScales 'nope': not a shape"));
            Assert.That(errors, Has.Some.Contains("AssistFloorScales 'horde': Scale 1.5 must be above 0 and at most 1"));
        }

        [Test]
        public void Validator_AcceptsNoAssist_AndAValidCurve()
        {
            RegionLibraryData none = CampaignMapTests.LoadRegions();
            none.AssistStep = 0.0;
            none.AssistFloorScales = new ShapeScaleData[0];
            Assert.That(RegionLibraryValidator.Validate(none, EncounterContentTests.LoadEncounterLibrary()), Has.None.Contains("Assist"));

            RegionLibraryData some = CampaignMapTests.LoadRegions();
            some.AssistStep = 0.1;
            some.AssistFloorScales = new[] { new ShapeScaleData { ShapeId = "squad", Scale = 0.5 } };
            Assert.That(RegionLibraryValidator.Validate(some, EncounterContentTests.LoadEncounterLibrary()), Has.None.Contains("Assist"));
        }

        // ---------------------------------------------------------------------------------------
        // EncounterPlan.WithAssist
        // ---------------------------------------------------------------------------------------

        [Test]
        public void WithAssist_ScalesTheMultiplier_AndIsTrackedApartFromEasing()
        {
            EncounterPlan plan = Plan();
            double before = plan.Multiplier;

            EncounterPlan eased = plan.Scaled(0.8);
            Assert.AreEqual(before * 0.8, eased.Multiplier, 1e-9);
            Assert.AreEqual(0.8, eased.DifficultyScale, 1e-9);
            Assert.AreEqual(1.0, eased.AssistScale, 1e-9, "Scaled (easing) does not touch AssistScale");

            EncounterPlan assisted = eased.WithAssist(0.9);
            Assert.AreEqual(before * 0.8 * 0.9, assisted.Multiplier, 1e-9, "easing and assist compose multiplicatively");
            Assert.AreEqual(0.8 * 0.9, assisted.DifficultyScale, 1e-9, "DifficultyScale carries both");
            Assert.AreEqual(0.9, assisted.AssistScale, 1e-9, "AssistScale is only the assist part, for the preview note");

            Assert.AreSame(plan, plan.WithAssist(1.0), "exactly 1 returns the plan itself");
            Assert.AreSame(plan, plan.WithAssist(0.0), "0 or below returns the plan itself");
        }

        // ---------------------------------------------------------------------------------------
        // CampaignRules.PlanFor: applies assist from consecutive losses, off on post-game Hard.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PlanFor_AppliesAssist_FromConsecutiveLossesAtTheNode()
        {
            RegionLibrary regions = Library(step: 0.10, floors: new[] { ("squad", 0.5) }, regionId: "r01");
            EncounterLibrary encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath)));
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
            MapRun run = new MapRun { RegionId = "r01", Stage = 0, Seed = 3 };
            run.Nodes = NodeMapGenerator.Generate(regions, "r01", 0, 3);
            MapNode node = run.Nodes.Find(n => n.IsBattle && n.ShapeId == "squad");
            Assume.That(node, Is.Not.Null, "the seed draws a squad battle");

            EncounterPlan noLosses = CampaignRules.PlanFor(run, node, encounters, enemies, regions);

            run.NodeAttemptsNodeId = node.NodeId;
            run.NodeAttempts = 2;
            EncounterPlan twoLosses = CampaignRules.PlanFor(run, node, encounters, enemies, regions);

            Assert.Less(twoLosses.Multiplier, noLosses.Multiplier, "two losses ease the fight further");
            Assert.AreEqual(regions.AssistScaleFor("r01", "squad", 2), twoLosses.AssistScale, 1e-9);

            run.NodeAttempts = 999;
            EncounterPlan atFloor = CampaignRules.PlanFor(run, node, encounters, enemies, regions);
            Assert.AreEqual(0.5, atFloor.AssistScale, 1e-9, "clamped at the floor, however many losses");
        }

        [Test]
        public void PlanFor_NeverAssists_OnPostGameHard()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            data.AssistStep = 0.5;
            data.AssistFloorScales = new[]
            {
                new ShapeScaleData { ShapeId = "squad", Scale = 0.1 }, new ShapeScaleData { ShapeId = "horde", Scale = 0.1 },
                new ShapeScaleData { ShapeId = "elite", Scale = 0.1 }, new ShapeScaleData { ShapeId = "solo", Scale = 0.1 },
                new ShapeScaleData { ShapeId = "squad_postgame", Scale = 0.1 }, new ShapeScaleData { ShapeId = "horde_postgame", Scale = 0.1 },
                new ShapeScaleData { ShapeId = "elite_postgame", Scale = 0.1 }, new ShapeScaleData { ShapeId = "squad_postgame_hard", Scale = 0.1 },
                new ShapeScaleData { ShapeId = "horde_postgame_hard", Scale = 0.1 }, new ShapeScaleData { ShapeId = "elite_postgame_hard", Scale = 0.1 },
                new ShapeScaleData { ShapeId = RegionLibraryData.EasingBossId, Scale = 0.1 }
            };
            RegionLibrary regions = RegionLibrary.Build(data);
            EncounterLibrary encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath)));
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);

            RegionData postGame = null;
            foreach (RegionData candidate in regions.Regions)
            {
                if (candidate.IsPostGame)
                {
                    postGame = candidate;
                    break;
                }
            }

            Assume.That(postGame, Is.Not.Null, "a post-game region exists");

            MapRun run = new MapRun { RegionId = postGame.RegionId, Stage = 0, Seed = 5, Difficulty = RunDifficulty.Hard };
            run.Nodes = NodeMapGenerator.Generate(regions, postGame.RegionId, 0, 5);
            MapNode node = run.Nodes.Find(n => n.IsBattle && string.IsNullOrEmpty(n.TemplateId));
            Assume.That(node, Is.Not.Null, "a generated (non-template) battle: a floor is authored for every generated shape in this test");
            run.NodeAttemptsNodeId = node.NodeId;
            run.NodeAttempts = 10;

            EncounterPlan hard = CampaignRules.PlanFor(run, node, encounters, enemies, regions);
            Assert.AreEqual(1.0, hard.AssistScale, 1e-9, "post-game Hard is never assisted, however many losses");

            run.Difficulty = RunDifficulty.Normal;
            EncounterPlan normal = CampaignRules.PlanFor(run, node, encounters, enemies, regions);
            Assert.Less(normal.AssistScale, 1.0, "the same location on Normal is assisted");
        }

        [Test]
        public void PlanFor_NeverAssists_InHearthglen_ButStillAssistsANormalRegion()
        {
            RegionLibraryData data = CampaignMapTests.LoadRegions();
            Assume.That(data.AssistStep, Is.GreaterThan(0.0), "the shipped curve actually assists something");
            RegionLibrary regions = RegionLibrary.Build(data);
            EncounterLibrary encounters = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath)));
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);

            // Hearthglen: a fixed-map battle node, retried 1, 2 and 5 times. Its own hg_* template
            // difficulty and the shared "boss" assist floor (shapeKey resolves to EasingBossId for a
            // templated fight) would otherwise ease it further on top of its own catch-up.
            List<MapNode> hearthglenNodes = CampaignRules.FixedMap(regions.Tutorial, 7);
            MapNode hearthglenNode = hearthglenNodes.Find(n => n.IsBattle);
            Assume.That(hearthglenNode, Is.Not.Null, "Hearthglen has at least one battle node");
            MapRun hearthglenRun = new MapRun { RegionId = CampaignProgress.TutorialRegionId };

            foreach (int losses in new[] { 1, 2, 5 })
            {
                hearthglenRun.NodeAttemptsNodeId = hearthglenNode.NodeId;
                hearthglenRun.NodeAttempts = losses;
                EncounterPlan plan = CampaignRules.PlanFor(hearthglenRun, hearthglenNode, encounters, enemies, regions);
                Assert.AreEqual(1.0, plan.AssistScale, 1e-9, "Hearthglen after " + losses + " loss(es) is never assisted: it has its own catch-up");
            }

            // A normal (non-tutorial) region node: the same repeated losses DO get assisted.
            MapRun normalRun = new MapRun { RegionId = "r01", Stage = 0, Seed = 3 };
            normalRun.Nodes = NodeMapGenerator.Generate(regions, "r01", 0, 3);
            MapNode normalNode = normalRun.Nodes.Find(n => n.IsBattle);
            Assume.That(normalNode, Is.Not.Null, "the seed draws a battle node in r01");
            normalRun.NodeAttemptsNodeId = normalNode.NodeId;
            normalRun.NodeAttempts = 2;
            EncounterPlan normalPlan = CampaignRules.PlanFor(normalRun, normalNode, encounters, enemies, regions);
            Assert.Less(normalPlan.AssistScale, 1.0, "a normal region node is still assisted after repeated losses");
        }

        // ---------------------------------------------------------------------------------------
        // MatchupWarnings
        // ---------------------------------------------------------------------------------------

        [Test]
        public void MatchupWarnings_FlagsNoVanguard_NoSkirmisher_AndUnderLevelled()
        {
            List<CreatureSpeciesSO> noVanguard = new List<CreatureSpeciesSO> { Species("a", CombatStance.Ranged), Species("b", CombatStance.Skirmisher) };
            List<string> warnings = MatchupWarnings.For(null, noVanguard, 10, 10);
            CollectionAssert.Contains(warnings, MatchupWarnings.NoVanguard);
            CollectionAssert.DoesNotContain(warnings, MatchupWarnings.NoSkirmisher);

            List<CreatureSpeciesSO> noSkirmisher = new List<CreatureSpeciesSO> { Species("a", CombatStance.Vanguard), Species("b", CombatStance.Ranged) };
            warnings = MatchupWarnings.For(null, noSkirmisher, 10, 10);
            CollectionAssert.DoesNotContain(warnings, MatchupWarnings.NoVanguard);
            CollectionAssert.Contains(warnings, MatchupWarnings.NoSkirmisher);

            List<CreatureSpeciesSO> balanced = new List<CreatureSpeciesSO> { Species("a", CombatStance.Vanguard), Species("b", CombatStance.Skirmisher) };
            warnings = MatchupWarnings.For(null, balanced, 5, 10);
            CollectionAssert.DoesNotContain(warnings, MatchupWarnings.NoVanguard);
            CollectionAssert.DoesNotContain(warnings, MatchupWarnings.NoSkirmisher);
            CollectionAssert.Contains(warnings, "Under-levelled by 5.");

            warnings = MatchupWarnings.For(null, balanced, 10, 10);
            CollectionAssert.DoesNotContain(warnings, "Under-levelled by 0.", "equal level is not under-levelled");
        }

        [Test]
        public void MatchupWarnings_FlagsAnElementDisadvantage()
        {
            List<CreatureSpeciesSO> team = new List<CreatureSpeciesSO> { Species("metal", CombatStance.Vanguard, Element.Metal), Species("metal2", CombatStance.Skirmisher, Element.Metal) };
            EncounterPreview waterEnemies = EncounterPreview.Build(new[] { Enemy("Giant", Element.Water), Enemy("Giant", Element.Water) }, ArenaSize.Medium);

            List<string> warnings = MatchupWarnings.For(waterEnemies, team, 10, 10);
            CollectionAssert.Contains(warnings, MatchupWarnings.ElementDisadvantage, "metal is weak against water on both sides");

            List<CreatureSpeciesSO> favoured = new List<CreatureSpeciesSO> { Species("water", CombatStance.Vanguard, Element.Water), Species("water2", CombatStance.Skirmisher, Element.Water) };
            EncounterPreview fireEnemies = EncounterPreview.Build(new[] { Enemy("Giant", Element.Fire), Enemy("Giant", Element.Fire) }, ArenaSize.Medium);
            warnings = MatchupWarnings.For(fireEnemies, favoured, 10, 10);
            CollectionAssert.DoesNotContain(warnings, MatchupWarnings.ElementDisadvantage, "water beats fire on both sides");
        }

        [Test]
        public void MatchupWarnings_IsEmpty_ForANullOrEmptyTeam()
        {
            Assert.IsEmpty(MatchupWarnings.For(null, null, 10, 10));
            Assert.IsEmpty(MatchupWarnings.For(null, new List<CreatureSpeciesSO>(), 10, 10));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------

        private static RegionLibrary Library(double step, (string shape, double floor)[] floors, string regionId = null)
        {
            RegionLibraryData data = regionId == null ? new RegionLibraryData { Regions = new RegionData[0] } : CampaignMapTests.LoadRegions();
            data.AssistStep = step;
            List<ShapeScaleData> scales = new List<ShapeScaleData>();
            foreach ((string shape, double floor) entry in floors)
            {
                scales.Add(new ShapeScaleData { ShapeId = entry.shape, Scale = entry.floor });
            }

            data.AssistFloorScales = scales.ToArray();
            return RegionLibrary.Build(data);
        }

        private static EncounterPlan Plan()
        {
            EncounterLibrary library = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath)));
            EnemyCatalog enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
            return EncounterPlan.Generate(library, enemies, "squad", 10, 3);
        }

        private static CreatureSpeciesSO Species(string id, CombatStance stance, Element element = Element.None)
        {
            return new CreatureSpeciesSO { SpeciesId = id, Stance = stance, Elements = element == Element.None ? new Element[0] : new[] { element } };
        }

        private static IEncounterPreviewSource Enemy(string name, Element element)
        {
            return new PreviewEnemy(name, element);
        }

        private sealed class PreviewEnemy : IEncounterPreviewSource
        {
            public PreviewEnemy(string displayName, Element element)
            {
                DisplayName = displayName;
                Element = element;
            }

            public Element Element { get; }

            public CombatStance Stance
            {
                get { return CombatStance.Vanguard; }
            }

            public string DisplayName { get; }
        }
    }
}
