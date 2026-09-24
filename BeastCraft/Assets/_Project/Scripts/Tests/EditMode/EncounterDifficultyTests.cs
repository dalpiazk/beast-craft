using System.Collections.Generic;
using BeastCraft.Encounters;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="EncounterDifficultyTable"/>: the simulator-written calibration read back by the
    /// game (interpolation, clamping, the elemental-only rule), its validator, and the committed
    /// <c>encounter-difficulty.json</c>.
    /// </summary>
    public class EncounterDifficultyTests
    {
        [Test]
        public void Multiplier_IsExactAtCalibratedLevels_LinearBetween_ClampedOutside()
        {
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(Data(
                Cell("elemental", "solo", 1, 1.0),
                Cell("elemental", "solo", 50, 2.0),
                Cell("elemental", "solo", 100, 1.5)));

            Assert.AreEqual(1.0, table.Multiplier("solo", 1), 1e-12);
            Assert.AreEqual(1.0 + (24.0 / 49.0), table.Multiplier("solo", 25), 1e-12);
            Assert.AreEqual(2.0, table.Multiplier("solo", 50), 1e-12);
            Assert.AreEqual(1.75, table.Multiplier("solo", 75), 1e-12);
            Assert.AreEqual(1.5, table.Multiplier("solo", 100), 1e-12);
            Assert.AreEqual(1.0, table.Multiplier("solo", 0), 1e-12, "below the lowest calibrated level: clamped");
            Assert.AreEqual(1.5, table.Multiplier("solo", 140), 1e-12, "above the highest: clamped");
        }

        [Test]
        public void Multiplier_ReadsOnlyElementalCells_AndIsOneForAnUncalibratedShape()
        {
            EncounterDifficultyTable table = EncounterDifficultyTable.Build(Data(
                Cell("neutral", "solo", 1, 3.0),
                Cell("elemental", "solo", 1, 1.2),
                Cell("neutral", "horde", 1, 3.0)));

            Assert.AreEqual(1.2, table.Multiplier("solo", 60), 1e-12, "a single calibrated level holds everywhere");
            Assert.IsFalse(table.HasShape("horde"), "neutral cells are never read");
            Assert.AreEqual(1.0, table.Multiplier("horde", 10), 1e-12);
            Assert.AreEqual(1.0, table.Multiplier("nowhere", 10), 1e-12);
            Assert.AreEqual(1.0, EncounterDifficultyTable.Build(null).Multiplier("solo", 1), 1e-12);
        }

        [Test]
        public void Validate_RefusesBadCellsAndUncalibratedShapes()
        {
            EncounterLibraryData library = EncounterLibraryTests.Library();
            EncounterDifficultyData data = Data(
                Cell("elemental", "duel", 1, 1.1),
                Cell("elemental", "duel", 1, 1.2),
                Cell("elemental", "ghost", 1, 1.0),
                Cell("mixed", "duel", 50, 1.0),
                Cell("elemental", "duel", 101, 1.0),
                Cell("elemental", "duel", 20, 0.0));

            List<string> errors = EncounterDifficultyTable.Validate(data, library);

            AssertHas(errors, "a second cell for elemental / duel / level 1");
            AssertHas(errors, "Shape 'ghost'");
            AssertHas(errors, "KitMode 'mixed'");
            AssertHas(errors, "Level 101");
            AssertHas(errors, "positive number");
            AssertHas(errors, "Shape 'boss' has no elemental cell");

            data.SchemaVersion = 99;
            AssertHas(EncounterDifficultyTable.Validate(data, library), "SchemaVersion is 99");
        }

        [Test]
        public void CommittedTable_ValidatesAgainstTheLibrary_AndCalibratesEveryShape()
        {
            EncounterDifficultyData data = EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath);
            EncounterLibraryData library = EncounterContentTests.LoadEncounterLibrary();

            List<string> errors = EncounterDifficultyTable.Validate(data, library);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            EncounterDifficultyTable table = EncounterDifficultyTable.Build(data);
            foreach (DifficultyCellData cell in data.Cells)
            {
                if (cell.KitMode == EncounterDifficultyData.ElementalMode)
                {
                    Assert.AreEqual(cell.Multiplier, table.Multiplier(cell.Shape, cell.Level), 1e-12, cell.Shape + " L" + cell.Level);
                }
            }

            foreach (EncounterShapeData shape in library.Shapes)
            {
                Assert.IsTrue(table.HasShape(shape.ShapeId), shape.ShapeId);
            }
        }

        [Test]
        public void SchemaOne_IsReadAsOneUniformTarget()
        {
            EncounterLibraryData library = EncounterLibraryTests.Library();
            EncounterDifficultyData data = Data(Cell("elemental", "duel", 1, 1.1), Cell("elemental", "boss", 1, 1.3));

            Assert.AreEqual(EncounterDifficultyData.UniformTargetSchemaVersion, data.SchemaVersion);
            Assert.IsEmpty(EncounterDifficultyTable.Validate(data, library), "a schema 1 file still loads");
            Assert.AreEqual(50.0, data.TargetFor("duel"));
            Assert.AreEqual(50.0, data.TargetFor("boss"));
            Assert.AreEqual(1.3, EncounterDifficultyTable.Build(data).Multiplier("boss", 1), 1e-12);

            List<string> warnings = EncounterDifficultyTable.Warnings(data, library);
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
            Assert.IsTrue(warnings[0].Contains("Shape 'duel' was calibrated to a 50% clear rate but the encounter library now targets 80%"), warnings[0]);
        }

        [Test]
        public void SchemaTwo_CarriesPerShapeTargets_AndChecksThem()
        {
            EncounterLibraryData library = EncounterLibraryTests.Library();
            EncounterDifficultyData data = TieredData(Target("duel", 80.0), Target("boss", 50.0));
            data.Cells = new[] { TieredCell("elemental", "duel", 1, 1.1, 80.0), TieredCell("elemental", "boss", 1, 1.3, 50.0) };

            Assert.IsEmpty(EncounterDifficultyTable.Validate(data, library));
            Assert.IsEmpty(EncounterDifficultyTable.Warnings(data, library), "the targets match the library");
            Assert.AreEqual(80.0, data.TargetFor("duel"));
            Assert.AreEqual(50.0, data.TargetFor("boss"));
            Assert.AreEqual(1.1, EncounterDifficultyTable.Build(data).Multiplier("duel", 7), 1e-12, "interpolation is unchanged");

            data.Cells[0].TargetClear = 70.0;
            data.Targets = new[] { Target("duel", 80.0), Target("boss", 50.0), Target("boss", 55.0), Target("ghost", 50.0), Target("duel", 100.0) };
            List<string> errors = EncounterDifficultyTable.Validate(data, library);
            AssertHas(errors, "TargetClear 70 is not its shape's target (80)");
            AssertHas(errors, "a second target for 'boss'");
            AssertHas(errors, "Shape 'ghost' is not a shape in the encounter library");
            AssertHas(errors, "TargetClear 100 must be strictly between 0 and 100");

            library.Shapes[1].TargetClear = 60.0;
            data = TieredData(Target("duel", 80.0), Target("boss", 50.0));
            data.Cells = new[] { TieredCell("elemental", "duel", 1, 1.1, 80.0), TieredCell("elemental", "boss", 1, 1.3, 50.0) };
            Assert.IsEmpty(EncounterDifficultyTable.Validate(data, library), "a stale target is legal...");
            Assert.AreEqual(1, EncounterDifficultyTable.Warnings(data, library).Count, "...but warned about");
        }

        [Test]
        public void CommittedTable_IsSchemaTwo_AtTheLibrarysTargets()
        {
            EncounterDifficultyData data = EncounterContentTests.Load<EncounterDifficultyData>(EncounterDifficultyData.ProjectRelativePath);
            EncounterLibraryData library = EncounterContentTests.LoadEncounterLibrary();

            Assert.AreEqual(EncounterDifficultyData.CurrentSchemaVersion, data.SchemaVersion);
            Assert.IsEmpty(EncounterDifficultyTable.Warnings(data, library), string.Join("\n", EncounterDifficultyTable.Warnings(data, library)));
            foreach (EncounterShapeData shape in library.Shapes)
            {
                Assert.AreEqual(shape.TargetClear, data.TargetFor(shape.ShapeId), shape.ShapeId);
            }
        }

        [TestCase(0.0)]
        [TestCase(100.0)]
        [TestCase(-5.0)]
        public void LibraryValidator_NeedsATargetStrictlyBetweenZeroAndOneHundred(double target)
        {
            EncounterLibraryData library = EncounterLibraryTests.Library();
            library.Shapes[0].TargetClear = target;

            List<string> errors = EncounterLibraryValidator.Validate(library, EncounterLibraryTests.Enemies(), null);

            AssertHas(errors, "Shape 'duel': TargetClear");
        }

        /// <summary>A schema 1 file (one uniform 50% target), the shape the older tests were written against.</summary>
        internal static EncounterDifficultyData Data(params DifficultyCellData[] cells)
        {
            return new EncounterDifficultyData { SchemaVersion = EncounterDifficultyData.UniformTargetSchemaVersion, TargetClear = 50.0, CalibratedOn = "bonds", Cells = cells };
        }

        private static EncounterDifficultyData TieredData(params DifficultyTargetData[] targets)
        {
            return new EncounterDifficultyData { SchemaVersion = EncounterDifficultyData.CurrentSchemaVersion, CalibratedOn = "bonds", Targets = targets };
        }

        private static DifficultyTargetData Target(string shape, double target)
        {
            return new DifficultyTargetData { Shape = shape, TargetClear = target };
        }

        private static DifficultyCellData TieredCell(string mode, string shape, int level, double multiplier, double target)
        {
            return new DifficultyCellData { KitMode = mode, Shape = shape, Level = level, Multiplier = multiplier, TargetClear = target };
        }

        internal static DifficultyCellData Cell(string mode, string shape, int level, double multiplier)
        {
            return new DifficultyCellData { KitMode = mode, Shape = shape, Level = level, Multiplier = multiplier };
        }

        private static void AssertHas(List<string> errors, string expected)
        {
            Assert.IsTrue(errors.Exists(e => e.Contains(expected)), "expected an error containing '" + expected + "', got:\n" + string.Join("\n", errors));
        }
    }
}
