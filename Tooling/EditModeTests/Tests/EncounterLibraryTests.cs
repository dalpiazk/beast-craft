using System.Collections.Generic;
using BeastCraft.Battle.Grid;
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

        /// <summary>
        /// A seven-tile Ranged enemy behind one to five Vanguard singles on a Medium arena. Largest
        /// first, five singles and the boss seat (it takes the middle row and the singles fill in
        /// around it), which is all the old descending-size check tried. But the generator places
        /// Vanguards first: five singles take the middle of the eight-tile front row and the boss
        /// then fits nowhere, so every five-brute draw fails its fit safety net. The validator must
        /// check the generator's order, at every count, and refuse the variant.
        /// </summary>
        [Test]
        public void Validate_RefusesALargeRangedEnemyTheGeneratorCannotSeatBehindItsScreen()
        {
            EnemyLibraryData enemies = ScreenEnemies("Ranged");
            EncounterLibraryData library = ScreenLibrary();

            List<UnitFootprint> largestFirst = new List<UnitFootprint> { UnitFootprint.Hex7 };
            List<UnitFootprint> generatorOrder = new List<UnitFootprint>();
            for (int i = 0; i < 5; i++)
            {
                largestFirst.Add(UnitFootprint.Single);
                generatorOrder.Add(UnitFootprint.Single);
            }

            generatorOrder.Add(UnitFootprint.Hex7);
            Assert.IsTrue(EncounterFit.Fits(ArenaSize.Medium, largestFirst), "largest first, the lineup seats (the old check passed it)");
            Assert.IsFalse(EncounterFit.Fits(ArenaSize.Medium, generatorOrder), "in the generator's order it does not");

            List<string> errors = EncounterLibraryValidator.Validate(library, enemies, Drops("duel", "boss"));
            Assert.IsTrue(errors.Exists(e => e.Contains("variant 'screen': 6 enemies it can draw (5 x Single, 1 x Hex7") && e.Contains("do not fit the Medium")),
                          string.Join("\n", errors));

            library.Shapes[1].Variants[0].Slots[0].Min = 5;
            EncounterGenerator generator = new EncounterGenerator(EncounterLibrary.Build(library), EnemyCatalog.Build(enemies, null), 3);
            Assert.IsNull(generator.Draw("boss"), "with exactly five brutes the generator can never seat it");
        }

        /// <summary>The same lineup with the boss a Vanguard: the generator places it first, so it seats and the validator accepts it.</summary>
        [Test]
        public void Validate_AcceptsALargeVanguardTheGeneratorSeatsFirst()
        {
            EnemyLibraryData enemies = ScreenEnemies("Vanguard");
            EncounterLibraryData library = ScreenLibrary();

            List<string> errors = EncounterLibraryValidator.Validate(library, enemies, Drops("duel", "boss"));
            Assert.IsEmpty(errors, string.Join("\n", errors));

            EncounterLineup lineup = new EncounterGenerator(EncounterLibrary.Build(library), EnemyCatalog.Build(enemies, null), 3).Draw("boss");
            Assert.IsNotNull(lineup);
            Assert.AreEqual("wyvern", lineup.Enemies[0].EnemyId, "the large Vanguard is placed first");
        }

        /// <summary>
        /// At most three singles: the Ranged boss seats behind them at every count the slot can draw,
        /// so the check is not simply "large non-Vanguards refused".
        /// </summary>
        [Test]
        public void Validate_AcceptsALargeRangedEnemyWhenEveryDrawSeats()
        {
            EnemyLibraryData enemies = ScreenEnemies("Ranged");
            EncounterLibraryData library = ScreenLibrary();
            library.Shapes[1].Variants[0].Slots[0].Max = 3;

            List<string> errors = EncounterLibraryValidator.Validate(library, enemies, Drops("duel", "boss"));
            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsNotNull(new EncounterGenerator(EncounterLibrary.Build(library), EnemyCatalog.Build(enemies, null), 3).Draw("boss"));
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
        public void Validate_RefusesADraftMarkerInPlayerFacingTemplateText()
        {
            EncounterLibraryData library = Library();
            library.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "marked_boss",
                    DisplayName = "Marked Boss",
                    Description = "A placeholder boss. [DRAFT]",
                    ShapeId = "boss",
                    Arena = "Large",
                    Groups = new[] { new EncounterGroupData { EnemyId = "giant", Count = 1 } }
                }
            };

            AssertRejected(library, "Template 'marked_boss': DisplayName and Description are player-facing and must not carry a '[DRAFT]' marker");

            library.Templates[0].Description = "A placeholder boss.";
            library.Templates[0].Draft = true;
            List<string> errors = EncounterLibraryValidator.Validate(library, Enemies(), Drops("duel", "boss"));
            Assert.IsFalse(errors.Exists(e => e.Contains("marked_boss")), string.Join("\n", errors));
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
                        TargetClear = 80.0,
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
                        TargetClear = 50.0,
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

        /// <summary>A seven-tile <c>wyvern</c> of the given stance (first in library order), then brute, archer and giant.</summary>
        private static EnemyLibraryData ScreenEnemies(string wyvernStance)
        {
            EnemyData wyvern = EnemyLibraryTests.Giant();
            wyvern.EnemyId = "wyvern";
            wyvern.DisplayName = "Wyvern";
            wyvern.Stance = wyvernStance;
            return EnemyLibraryTests.Library(wyvern, EnemyLibraryTests.Brute(), EnemyLibraryTests.Archer(), EnemyLibraryTests.Giant());
        }

        /// <summary><see cref="Library"/> with <c>boss</c> replaced by one variant, <c>screen</c>: one to five brutes and one wyvern.</summary>
        private static EncounterLibraryData ScreenLibrary()
        {
            EncounterLibraryData library = Library();
            EncounterShapeData boss = library.Shapes[1];
            boss.ThreatMin = 0.0;
            boss.ThreatMax = 100.0;
            boss.Variants = new[]
            {
                new EncounterVariantData
                {
                    Label = "screen",
                    Weight = 1,
                    Slots = new[]
                    {
                        new EncounterSlotData { Types = new[] { "brute" }, Min = 1, Max = 5 },
                        new EncounterSlotData { Types = new[] { "wyvern" }, Min = 1, Max = 1 }
                    }
                }
            };

            return library;
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
