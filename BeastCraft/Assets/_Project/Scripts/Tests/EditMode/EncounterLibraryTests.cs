using System.Collections.Generic;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary><see cref="EncounterLibraryValidator"/>'s rules over small hand-built libraries.</summary>
    public class EncounterLibraryTests
    {
        [Test]
        public void Validate_AcceptsAMinimalLibrary()
        {
            List<string> errors = EncounterLibraryValidator.Validate(Library(), Enemies(), Drops("duel", "boss"));

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Validate_RefusesBadSchemeWeights()
        {
            EncounterLibraryData unknown = Library();
            unknown.SchemeWeights[0].Scheme = "Rainbow";
            EncounterLibraryData twice = Library();
            twice.SchemeWeights[1].Scheme = twice.SchemeWeights[0].Scheme;
            EncounterLibraryData zero = Library();
            foreach (SchemeWeightData entry in zero.SchemeWeights)
            {
                entry.Weight = 0;
            }

            AssertRejected(unknown, "Scheme 'Rainbow'");
            AssertRejected(twice, "listed twice");
            AssertRejected(zero, "more than 0");
        }

        [Test]
        public void Validate_RefusesABadDifficultyScale()
        {
            EncounterLibraryData library = Library();
            library.DifficultyScale = 0.0;

            AssertRejected(library, "DifficultyScale");
        }

        [Test]
        public void Validate_RefusesAnUnknownTypeAndABadBudget()
        {
            EncounterLibraryData unknown = Library();
            unknown.Shapes[0].Variants[0].Slots[0].Types = new[] { "brute", "ghost" };
            EncounterLibraryData budget = Library();
            budget.Shapes[0].ThreatMin = 9.0;

            AssertRejected(unknown, "unknown enemy type 'ghost'");
            AssertRejected(budget, "ThreatMin <= ThreatMax");
        }

        [Test]
        public void Validate_RefusesAShapeWhoseWorstCaseDoesNotFitItsArena()
        {
            EncounterLibraryData hex7 = Library();
            hex7.Shapes[1].Arena = "Small";
            EncounterLibraryData crowd = Library();
            crowd.Shapes[0].Variants[0].Slots[0].Max = 60;

            AssertRejected(hex7, "variant 'giant'");
            AssertRejected(crowd, "do not fit the Medium");
        }

        [Test]
        public void Validate_RefusesAHex7TemplateOnASmallArena()
        {
            EncounterLibraryData library = Library();
            library.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "cramped_boss",
                    ShapeId = "boss",
                    Arena = "Small",
                    Groups = new[] { new EncounterGroupData { EnemyId = "giant", Count = 1 } }
                }
            };

            AssertRejected(library, "Template 'cramped_boss': its 1 enemies do not fit the Small");
        }

        [Test]
        public void Validate_RefusesATemplateWithAnUnknownShapeEnemyOrElement()
        {
            EncounterLibraryData library = Library();
            library.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "odd",
                    ShapeId = "nowhere",
                    Arena = "Medium",
                    Groups = new[]
                    {
                        new EncounterGroupData { EnemyId = "ghost", Count = 1 },
                        new EncounterGroupData { EnemyId = "brute", Count = 2, Elements = new[] { "Plasma" } }
                    }
                }
            };

            AssertRejected(library, "ShapeId 'nowhere'");
            AssertRejected(library, "group 'ghost': unknown enemy");
            AssertRejected(library, "unknown element 'Plasma'");
        }

        [Test]
        public void Validate_RefusesShapesThatDoNotMatchTheDropTables()
        {
            List<string> errors = EncounterLibraryValidator.Validate(Library(), Enemies(), Drops("duel", "swarm"));

            Assert.IsTrue(errors.Exists(e => e.Contains("Shape 'boss' is not a shape in drop-tables.json")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("drop-tables.json shape 'swarm'")), string.Join("\n", errors));
        }

        // ----------------------------------------------------------------------------------------
        // Helpers.
        // ----------------------------------------------------------------------------------------

        internal static EnemyLibraryData Enemies()
        {
            return EnemyLibraryTests.Library(EnemyLibraryTests.Brute(), EnemyLibraryTests.Archer(), EnemyLibraryTests.Giant());
        }

        /// <summary>Two shapes: <c>duel</c> (two to four brutes and archers) and <c>boss</c> (one giant with an escort).</summary>
        internal static EncounterLibraryData Library()
        {
            return new EncounterLibraryData
            {
                SchemaVersion = EncounterLibraryData.CurrentSchemaVersion,
                DifficultyScale = 1.0,
                SchemeWeights = new[]
                {
                    new SchemeWeightData { Scheme = "Uniform", Weight = 30 },
                    new SchemeWeightData { Scheme = "PerType", Weight = 30 },
                    new SchemeWeightData { Scheme = "PerUnit", Weight = 25 },
                    new SchemeWeightData { Scheme = "None", Weight = 15 }
                },
                Shapes = new[]
                {
                    new EncounterShapeData
                    {
                        ShapeId = "duel",
                        DisplayName = "Duel",
                        Arena = "Medium",
                        ThreatMin = 4.0,
                        ThreatMax = 8.0,
                        MinDistinctTypes = 2,
                        Variants = new[]
                        {
                            new EncounterVariantData
                            {
                                Label = "mixed",
                                Weight = 1,
                                Slots = new[] { new EncounterSlotData { Types = new[] { "brute", "archer" }, Min = 2, Max = 4 } }
                            }
                        }
                    },
                    new EncounterShapeData
                    {
                        ShapeId = "boss",
                        DisplayName = "Boss",
                        Arena = "Medium",
                        ThreatMin = 14.0,
                        ThreatMax = 17.0,
                        MinDistinctTypes = 2,
                        Variants = new[]
                        {
                            new EncounterVariantData
                            {
                                Label = "giant",
                                Weight = 1,
                                Slots = new[]
                                {
                                    new EncounterSlotData { Types = new[] { "giant" }, Min = 1, Max = 1 },
                                    new EncounterSlotData { Types = new[] { "brute", "archer" }, Min = 1, Max = 2 }
                                }
                            }
                        }
                    }
                }
            };
        }

        internal static DropTableData Drops(params string[] shapes)
        {
            return new DropTableData { SchemaVersion = DropTableData.CurrentSchemaVersion, Shapes = shapes };
        }

        private static void AssertRejected(EncounterLibraryData library, string expected)
        {
            List<string> errors = EncounterLibraryValidator.Validate(library, Enemies(), Drops("duel", "boss"));
            Assert.IsTrue(errors.Exists(e => e.Contains(expected)), "expected an error containing '" + expected + "', got:\n" + string.Join("\n", errors));
        }
    }
}
