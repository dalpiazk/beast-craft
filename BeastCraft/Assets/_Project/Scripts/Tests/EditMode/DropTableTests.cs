using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Progression;
using BeastCraft.Skills;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The drop tables: the authored <c>Data/Skills/drop-tables.json</c> (straight from the JSON, so
    /// it runs without imported assets), <see cref="DropTableValidator"/>'s structural rules with
    /// negative cases over small hand-built tables, and <see cref="DropTableBuilder"/>. The pacing
    /// targets are the balance simulator's (<c>--mode pacing</c>), not these tests'.
    /// </summary>
    public class DropTableTests
    {
        // ----------------------------------------------------------------------------------------
        // The authored file.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void AuthoredTables_PassValidationAgainstTheLibraryMaterials()
        {
            List<string> errors = DropTableValidator.Validate(LoadTables(), SkillLibraryTests.LoadLibrary().Materials);

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void AuthoredTables_FirstBandPaysATier1FirstClear_AndEveryGateTierCanDrop()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            DropTable table = DropTableBuilder.Build(LoadTables(), DropTableBuilder.TierLookup(library.Materials));

            Assert.AreEqual(1, table.Bands[0].FirstClearTier, "the level-5 gate's material comes from the first clears");

            HashSet<int> droppable = new HashSet<int>();
            foreach (DropBand band in table.Bands)
            {
                droppable.Add(band.FirstClearTier);
                foreach (string shape in table.Shapes)
                {
                    foreach (DropEntry entry in band.GetCell(shape).Entries)
                    {
                        Assert.Greater(entry.Tier, 0, entry.MaterialId + " resolves to a tier");
                        droppable.Add(entry.Tier);
                    }
                }
            }

            foreach (int tier in new[] { 1, 2, 3 })
            {
                Assert.IsTrue(droppable.Contains(tier), "tier " + tier + " (a breakthrough gate's material) can drop");
                Assert.Greater(table.PityThreshold(tier), 0, "tier " + tier + " has pity");
            }
        }

        [Test]
        public void AuthoredTables_ExpectedMaterialXpPerClear_IsLowestInTheFirstBandAndHighestInTheLast()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            Dictionary<string, int> xp = new Dictionary<string, int>();
            foreach (SkillMaterialData m in library.Materials)
            {
                xp[m.MaterialId] = m.XpValue;
            }

            DropTable table = DropTableBuilder.Build(LoadTables(), DropTableBuilder.TierLookup(library.Materials));
            List<double> perBand = new List<double>();
            foreach (DropBand band in table.Bands)
            {
                double total = 0.0;
                foreach (string shape in table.Shapes)
                {
                    foreach (DropEntry entry in band.GetCell(shape).Entries)
                    {
                        total += entry.ExpectedQuantity * xp[entry.MaterialId];
                    }
                }

                perBand.Add(total);
            }

            int last = perBand.Count - 1;
            for (int i = 1; i < last; i++)
            {
                Assert.Greater(perBand[i], perBand[0], "band " + i + " out-earns the first");
                Assert.Greater(perBand[last], perBand[i], "the last band out-earns band " + i);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Validator.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Validator_AcceptsAMinimalTable()
        {
            Assert.IsEmpty(DropTableValidator.Validate(Minimal(), Materials()));
        }

        [Test]
        public void Validator_NullOrWrongSchema_IsReported()
        {
            Assert.IsNotEmpty(DropTableValidator.Validate(null));

            DropTableData table = Minimal();
            table.SchemaVersion = DropTableData.CurrentSchemaVersion + 1;
            AssertError(table, "SchemaVersion");
        }

        [Test]
        public void Validator_BandsMustBeContiguousFromOneToOneHundred()
        {
            DropTableData gap = Minimal();
            gap.Bands[1].MinLevel = 52;
            AssertError(gap, "contiguous");

            DropTableData shortTable = Minimal();
            shortTable.Bands[1].MaxLevel = 90;
            AssertError(shortTable, "cover every level");

            DropTableData inverted = Minimal();
            inverted.Bands[0].MaxLevel = 0;
            AssertError(inverted, "MaxLevel is below MinLevel");
        }

        [Test]
        public void Validator_EveryBandHasExactlyOneCellPerShape()
        {
            DropTableData missing = Minimal();
            missing.Bands[0].Cells = new[] { missing.Bands[0].Cells[0] };
            AssertError(missing, "no cell for shape 'horde'");

            DropTableData doubled = Minimal();
            doubled.Bands[0].Cells[1].Shape = "solo";
            AssertError(doubled, "two cells");

            DropTableData stranger = Minimal();
            stranger.Bands[0].Cells[1].Shape = "raid";
            AssertError(stranger, "not one of the Shapes");
        }

        [Test]
        public void Validator_ShapesAreUniqueSnakeCase()
        {
            DropTableData dup = Minimal();
            dup.Shapes = new[] { "solo", "solo", "horde" };
            AssertError(dup, "listed twice");

            DropTableData caps = Minimal();
            caps.Shapes = new[] { "Solo", "horde" };
            AssertError(caps, "snake_case");

            DropTableData none = Minimal();
            none.Shapes = new string[0];
            AssertError(none, "No shapes");
        }

        [TestCase(0, 1, 1, "Chance")]
        [TestCase(101, 1, 1, "Chance")]
        [TestCase(50, 0, 1, "quantity")]
        [TestCase(50, 3, 2, "quantity")]
        [TestCase(50, 1, 11, "quantity")]
        public void Validator_ChancesArePercentsAndQuantitiesSane(int chance, int min, int max, string expected)
        {
            DropTableData table = Minimal();
            table.Bands[0].Cells[0].Drops[0] = new DropEntryData { MaterialId = "shard", Chance = chance, MinQty = min, MaxQty = max };
            AssertError(table, expected);
        }

        [Test]
        public void Validator_MaterialIdsResolveAgainstTheLibrary_AndAppearOncePerCell()
        {
            DropTableData unknown = Minimal();
            unknown.Bands[0].Cells[0].Drops[0].MaterialId = "mystery";
            AssertError(unknown, "not a material");
            Assert.IsEmpty(DropTableValidator.Validate(unknown), "without the library there is nothing to resolve against");

            DropTableData twice = Minimal();
            twice.Bands[0].Cells[0].Drops = new[] { Entry("shard", 50), Entry("shard", 20) };
            AssertError(twice, "appears twice");

            DropTableData noBonus = Minimal();
            noBonus.Bands[0].FirstClearMaterialId = null;
            AssertError(noBonus, "FirstClearMaterialId");

            DropTableData badQty = Minimal();
            badQty.Bands[0].FirstClearQuantity = 0;
            AssertError(badQty, "FirstClearQuantity");
        }

        [Test]
        public void Validator_PityIsOnePositiveThresholdPerExistingTier()
        {
            DropTableData dup = Minimal();
            dup.Pity = new[] { new PityData { Tier = 1, Threshold = 5 }, new PityData { Tier = 1, Threshold = 6 } };
            AssertError(dup, "listed twice");

            DropTableData zero = Minimal();
            zero.Pity = new[] { new PityData { Tier = 1, Threshold = 0 } };
            AssertError(zero, "Threshold");

            DropTableData ghost = Minimal();
            ghost.Pity = new[] { new PityData { Tier = 7, Threshold = 5 } };
            AssertError(ghost, "no material has tier 7");
        }

        // ----------------------------------------------------------------------------------------
        // Builder.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Builder_ResolvesTiers_PityAndBands()
        {
            DropTable table = DropTableBuilder.Build(Minimal(), DropTableBuilder.TierLookup(Materials()));

            CollectionAssert.AreEqual(new[] { "solo", "horde" }, table.Shapes);
            Assert.AreEqual(2, table.Bands.Count);
            Assert.AreEqual(0, table.BandIndexForLevel(-5), "below the first band reads as the first");
            Assert.AreEqual(0, table.BandIndexForLevel(50));
            Assert.AreEqual(1, table.BandIndexForLevel(51));
            Assert.AreEqual(1, table.BandIndexForLevel(500), "above the last band reads as the last");
            Assert.AreEqual(8, table.PityThreshold(1));
            Assert.AreEqual(0, table.PityThreshold(2), "a tier without pity");

            DropEntry entry = table.GetCell("solo", 60).Entries[0];
            Assert.AreEqual("crystal", entry.MaterialId);
            Assert.AreEqual(2, entry.Tier);
            Assert.AreEqual(0.25 * 1.5, entry.ExpectedQuantity, 1e-9);
            Assert.AreEqual(2, table.Bands[1].FirstClearTier);
            Assert.IsNull(table.GetCell("raid", 10));
            Assert.IsNull(DropTableBuilder.Build(null, null));
        }

        [Test]
        public void Builder_WithoutATierLookup_GivesTierZero()
        {
            DropTable table = DropTableBuilder.Build(Minimal(), null);

            Assert.AreEqual(0, table.GetCell("solo", 1).Entries[0].Tier);
        }

        // ----------------------------------------------------------------------------------------
        // Helpers.
        // ----------------------------------------------------------------------------------------

        /// <summary>Two shapes (solo, horde), two bands (1-50 shards, 51-100 crystals), pity on tier 1 only.</summary>
        internal static DropTableData Minimal()
        {
            return new DropTableData
            {
                SchemaVersion = DropTableData.CurrentSchemaVersion,
                Shapes = new[] { "solo", "horde" },
                Pity = new[] { new PityData { Tier = 1, Threshold = 8 } },
                Bands = new[]
                {
                    new LevelBandData
                    {
                        MinLevel = 1, MaxLevel = 50, FirstClearMaterialId = "shard",
                        Cells = new[]
                        {
                            new DropCellData { Shape = "solo", Drops = new[] { Entry("shard", 50) } },
                            new DropCellData { Shape = "horde", Drops = new[] { Entry("shard", 30) } },
                        }
                    },
                    new LevelBandData
                    {
                        MinLevel = 51, MaxLevel = 100, FirstClearMaterialId = "crystal",
                        Cells = new[]
                        {
                            new DropCellData { Shape = "solo", Drops = new[] { new DropEntryData { MaterialId = "crystal", Chance = 25, MinQty = 1, MaxQty = 2 } } },
                            new DropCellData { Shape = "horde", Drops = new DropEntryData[0] },
                        }
                    },
                }
            };
        }

        internal static SkillMaterialData[] Materials()
        {
            return new[]
            {
                new SkillMaterialData { MaterialId = "shard", DisplayName = "Shard", Description = "d", Tier = 1, XpValue = 250 },
                new SkillMaterialData { MaterialId = "crystal", DisplayName = "Crystal", Description = "d", Tier = 2, XpValue = 1000 },
            };
        }

        internal static DropEntryData Entry(string id, int chance)
        {
            return new DropEntryData { MaterialId = id, Chance = chance };
        }

        internal static DropTableData LoadTables()
        {
            string path = FindFile(DropTableData.ProjectRelativePath);
            Assert.IsNotNull(path, "Could not find " + DropTableData.ProjectRelativePath);

            DropTableData tables = FieldJson.FromJson<DropTableData>(File.ReadAllText(path));
            Assert.IsNotNull(tables);
            return tables;
        }

        private static void AssertError(DropTableData table, string fragment)
        {
            List<string> errors = DropTableValidator.Validate(table, Materials());
            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        internal static string FindFile(string projectRelativePath)
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

            foreach (string start in starts)
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string[] candidates =
                    {
                        Path.Combine(dir.FullName, projectRelativePath),
                        Path.Combine(dir.FullName, "BeastCraft", projectRelativePath),
                    };

                    foreach (string candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }
    }
}
