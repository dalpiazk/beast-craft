using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="EncounterPlan"/>: a generated or templated encounter at a level, its difficulty
    /// multiplier (calibrated table x scale, or a template's override), its scouting preview and the
    /// <see cref="EncounterSetup"/> it hands the battle session. The session end to end is in
    /// <see cref="BattleSessionTests"/>.
    /// </summary>
    public class EncounterPlanTests
    {
        private EncounterLibraryData _data;
        private EnemyCatalog _enemies;

        [SetUp]
        public void SetUp()
        {
            _data = EncounterContentTests.LoadEncounterLibrary();
            _enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), null);
        }

        [Test]
        public void Generate_IsTheGeneratorsDrawAtTheCalibratedMultiplier()
        {
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(LoadDifficulty());
            EncounterLibrary library = EncounterLibrary.Build(_data, table);

            EncounterPlan plan = EncounterPlan.Generate(library, _enemies, "elite", 30, 99);
            EncounterLineup draw = new EncounterGenerator(library, _enemies, 99).Draw("elite");

            Assert.AreEqual("elite", plan.ShapeId);
            Assert.IsNull(plan.EncounterId);
            Assert.AreEqual(30, plan.Level);
            Assert.AreEqual(table.Multiplier("elite", 30), plan.Multiplier, 1e-12);
            Assert.AreEqual(draw.ElementScheme, plan.ElementScheme);
            Assert.AreEqual(draw.Enemies.Count, plan.Enemies.Count);
            for (int i = 0; i < draw.Enemies.Count; i++)
            {
                Assert.AreEqual(draw.Enemies[i].EnemyId, plan.Enemies[i].EnemyId);
                Assert.AreEqual(draw.Enemies[i].Element, plan.Enemies[i].Element);
            }
        }

        [Test]
        public void Generate_MultipliesByTheDifficultyScale_AndClampsTheLevel()
        {
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(LoadDifficulty());
            _data.DifficultyScale = 0.8;
            EncounterLibrary library = EncounterLibrary.Build(_data, table);

            EncounterPlan plan = EncounterPlan.Generate(library, _enemies, "solo", 250, 1);

            Assert.AreEqual(100, plan.Level);
            Assert.AreEqual(table.Multiplier("solo", 100) * 0.8, plan.Multiplier, 1e-12);
            Assert.AreEqual(1.0, EncounterPlan.Generate(EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary()), _enemies, "solo", 10, 1).Multiplier,
                            1e-12, "no table: uncalibrated");
            Assert.IsNull(EncounterPlan.Generate(library, _enemies, "nowhere", 10, 1));
        }

        [Test]
        public void FromTemplate_FieldsTheAuthoredGroups_AndItsOverrideWins()
        {
            // EXAMPLE templates, for this test only: the shipped library authors none.
            _data.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "example_ambush",
                    ShapeId = "squad",
                    Arena = "Small",
                    Groups = new[]
                    {
                        new EncounterGroupData { EnemyId = "brute", Count = 2, Elements = new[] { "Fire", "Ice" } },
                        new EncounterGroupData { EnemyId = "archer", Count = 3, Elements = new[] { "Dark" } }
                    },
                    DifficultyOverride = 1.5
                },
                new EncounterTemplateData
                {
                    EncounterId = "example_patrol",
                    ShapeId = "squad",
                    Arena = "Medium",
                    Groups = new[] { new EncounterGroupData { EnemyId = "stalker", Count = 2 } }
                }
            };
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(LoadDifficulty());
            EncounterLibrary library = EncounterLibrary.Build(_data, table);

            EncounterPlan ambush = EncounterPlan.FromTemplate(library, _enemies, "example_ambush", 40);
            EncounterPlan patrol = EncounterPlan.FromTemplate(library, _enemies, "example_patrol", 40);

            Assert.AreEqual("example_ambush", ambush.EncounterId);
            Assert.AreEqual("squad", ambush.ShapeId);
            Assert.AreEqual(ArenaSize.Small, ambush.Arena);
            Assert.IsNull(ambush.ElementScheme);
            Assert.AreEqual(1.5, ambush.Multiplier, 1e-12, "the override replaces the table and the scale");
            Assert.AreEqual(table.Multiplier("squad", 40), patrol.Multiplier, 1e-12);

            Element[] expected = { Element.Fire, Element.Ice, Element.Dark, Element.Dark, Element.Dark };
            Assert.AreEqual(5, ambush.Enemies.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(i < 2 ? "brute" : "archer", ambush.Enemies[i].EnemyId);
                Assert.AreEqual(expected[i], ambush.Enemies[i].Element);
            }

            Assert.AreEqual(Element.None, patrol.Enemies[0].Element);
            Assert.AreEqual(CombatStance.Skirmisher, patrol.Enemies[0].Stance);
            Assert.IsNull(EncounterPlan.FromTemplate(library, _enemies, "nowhere", 40));
        }

        [Test]
        public void ToSetup_SpecsEveryEnemyForTheSession()
        {
            EncounterLibrary library = EncounterLibrary.Build(_data, EncounterDifficultyTable.Build(LoadDifficulty()));
            EncounterPlan plan = EncounterPlan.Generate(library, _enemies, "elite", 12, 4);

            EncounterSetup setup = plan.ToSetup();

            Assert.AreEqual(plan.Arena, setup.Arena);
            Assert.AreEqual("elite", setup.ShapeId);
            Assert.AreEqual(12, setup.EncounterLevel);
            Assert.AreEqual(plan.Enemies.Count, setup.Enemies.Count);
            for (int i = 0; i < setup.Enemies.Count; i++)
            {
                EnemySpec spec = setup.Enemies[i];
                Assert.AreEqual(plan.Enemies[i].EnemyId, spec.SpeciesId);
                Assert.AreEqual(12, spec.Level);
                Assert.AreEqual(plan.Enemies[i].Element, spec.Element);
                Assert.AreEqual(plan.Multiplier, spec.StatMultiplier, 1e-12);
                Assert.AreEqual(_enemies.Get(spec.SpeciesId).StatusResist, spec.StatusResist);
                Assert.IsNull(spec.SkillIds, "the catalog kit");
                Assert.IsNull(spec.Position, "auto-placed front to back");
            }
        }

        [Test]
        public void Preview_GroupsThePlansEnemies()
        {
            EncounterPlan plan = EncounterPlan.Generate(EncounterLibrary.Build(_data), _enemies, "horde", 20, 8);

            EncounterPreview full = plan.Preview();
            EncounterPreview dominant = plan.Preview(ScoutingDetail.DominantElementOnly);

            Assert.AreEqual(plan.Enemies.Count, full.TotalEnemies);
            Assert.AreEqual(plan.Arena, full.Arena);
            Assert.AreEqual(ScoutingDetail.DominantElementOnly, dominant.Detail);
            int counted = 0;
            foreach (EncounterPreviewGroup group in full.Groups)
            {
                counted += group.Count;
            }

            Assert.AreEqual(plan.Enemies.Count, counted);
        }

        internal static EncounterDifficultyData LoadDifficulty()
        {
            return EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath);
        }
    }
}
