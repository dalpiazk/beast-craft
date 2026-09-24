using System.Collections.Generic;
using BeastCraft.Progression;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="LootRoller"/>: seeded reproducibility, independent per-entry chances and
    /// quantities, a fixed number of rng draws per clear, pity, the first-clear bonus, and
    /// <see cref="LootRoller.DeriveSeed"/>. Tables are small hand-built ones
    /// (<see cref="DropTableTests.Minimal"/> and variants), so tuning the authored file never breaks these.
    /// </summary>
    public class LootRollerTests
    {
        [Test]
        public void SameSeed_RollsTheSameLoot()
        {
            DropTable table = Table(DropTableTests.Minimal());

            Assert.AreEqual(Run(table, 42, 300), Run(table, 42, 300));
            Assert.AreNotEqual(Run(table, 42, 300), Run(table, 43, 300));
        }

        [Test]
        public void DrawCount_DoesNotDependOnLuckOrChance()
        {
            DropTableData always = DropTableTests.Minimal();
            always.Bands[0].Cells[0].Drops[0] = new DropEntryData { MaterialId = "shard", Chance = 100, MinQty = 1, MaxQty = 3 };
            DropTableData never = DropTableTests.Minimal();
            never.Bands[0].Cells[0].Drops[0] = new DropEntryData { MaterialId = "shard", Chance = 1, MinQty = 1, MaxQty = 3 };
            never.Pity = new PityData[0];

            System.Random a = new System.Random(7);
            System.Random b = new System.Random(7);
            LootRoller.RollClear(Table(always), "solo", 1, new MaterialInventory(), a);
            LootRoller.RollClear(Table(never), "solo", 1, new MaterialInventory(), b);

            Assert.AreEqual(a.Next(), b.Next(), "two draws per entry, hit or miss");
        }

        [Test]
        public void Chance_IsHonouredOverManyClears_AndQuantityCoversItsRange()
        {
            DropTableData data = DropTableTests.Minimal();
            data.Pity = new PityData[0];
            data.Bands[0].Cells[0].Drops[0] = new DropEntryData { MaterialId = "shard", Chance = 40, MinQty = 2, MaxQty = 4 };
            DropTable table = Table(data);
            System.Random rng = new System.Random(11);

            int hits = 0;
            HashSet<int> quantities = new HashSet<int>();
            const int clears = 20000;
            for (int i = 0; i < clears; i++)
            {
                MaterialInventory inventory = new MaterialInventory();
                inventory.MarkCleared("solo", 1);
                int got = LootRoller.RollClear(table, "solo", 10, inventory, rng).QuantityOf("shard");
                if (got > 0)
                {
                    hits++;
                    quantities.Add(got);
                }
            }

            Assert.That(100.0 * hits / clears, Is.InRange(38.5, 41.5));
            CollectionAssert.AreEquivalent(new[] { 2, 3, 4 }, quantities);
        }

        [Test]
        public void Pity_ForcesTheTierAfterThresholdDryClears_ThenResets()
        {
            DropTableData data = DropTableTests.Minimal();
            data.Pity = new[] { new PityData { Tier = 1, Threshold = 3 } };
            data.Bands[0].Cells[0].Drops[0] = new DropEntryData { MaterialId = "shard", Chance = 1, MinQty = 2, MaxQty = 5 };
            DropTable table = Table(data);
            MaterialInventory inventory = new MaterialInventory();
            inventory.MarkCleared("solo", 1);
            System.Random rng = new System.Random(1);

            // Find a run of natural misses: with a 1% chance the first clears all miss for this seed.
            for (int clear = 1; clear <= 3; clear++)
            {
                LootResult dry = LootRoller.RollClear(table, "solo", 1, inventory, rng);
                Assert.AreEqual(0, dry.QuantityOf("shard"), "clear " + clear);
                Assert.AreEqual(clear, inventory.GetPity("solo", 1));
            }

            LootResult forced = LootRoller.RollClear(table, "solo", 1, inventory, rng);
            Assert.AreEqual(2, forced.QuantityOf("shard"), "forced at MinQty");
            CollectionAssert.AreEqual(new[] { 1 }, forced.PityForcedTiers);
            Assert.AreEqual(0, inventory.GetPity("solo", 1));
            Assert.AreEqual(2, inventory.GetCount("shard"));
        }

        [Test]
        public void Pity_IsPerShape_AndTiersTheCellCannotDropAreLeftAlone()
        {
            DropTableData data = DropTableTests.Minimal();
            data.Pity = new[] { new PityData { Tier = 1, Threshold = 50 }, new PityData { Tier = 2, Threshold = 50 } };
            data.Bands[0].Cells[0].Drops[0] = DropTableTests.Entry("shard", 1);
            DropTable table = Table(data);
            MaterialInventory inventory = new MaterialInventory();
            inventory.SetPity("solo", 2, 7);
            System.Random rng = new System.Random(1);

            LootRoller.RollClear(table, "solo", 1, inventory, rng);

            Assert.AreEqual(1, inventory.GetPity("solo", 1));
            Assert.AreEqual(0, inventory.GetPity("horde", 1), "another shape's counter");
            Assert.AreEqual(7, inventory.GetPity("solo", 2), "the level-1 solo cell cannot drop tier 2");
        }

        [Test]
        public void FirstClear_GrantsTheBandBonusOncePerShapeAndBand()
        {
            DropTableData data = DropTableTests.Minimal();
            data.Bands[0].Cells[0].Drops = new DropEntryData[0];
            data.Bands[0].Cells[1].Drops = new DropEntryData[0];
            DropTable table = Table(data);
            MaterialInventory inventory = new MaterialInventory();
            System.Random rng = new System.Random(5);

            LootResult first = LootRoller.RollClear(table, "solo", 3, inventory, rng);
            Assert.IsTrue(first.FirstClear);
            Assert.AreEqual(1, first.QuantityOf("shard"));

            LootResult again = LootRoller.RollClear(table, "solo", 40, inventory, rng);
            Assert.IsFalse(again.FirstClear, "same band, another level");
            Assert.AreEqual(0, again.Drops.Count);

            Assert.IsTrue(LootRoller.RollClear(table, "horde", 3, inventory, rng).FirstClear, "another shape");
            LootResult nextBand = LootRoller.RollClear(table, "horde", 60, inventory, rng);
            Assert.IsTrue(nextBand.FirstClear, "another band");
            Assert.AreEqual(1, nextBand.QuantityOf("crystal"));

            Assert.AreEqual(2, inventory.GetCount("shard"));
            Assert.AreEqual(1, inventory.GetCount("crystal"));
            Assert.AreEqual(3, inventory.ClearedCells.Count);
        }

        [Test]
        public void BadInput_RollsNothing()
        {
            DropTable table = Table(DropTableTests.Minimal());
            MaterialInventory inventory = new MaterialInventory();

            Assert.AreEqual(0, LootRoller.RollClear(table, "raid", 1, inventory, new System.Random(1)).Drops.Count);
            Assert.AreEqual(0, LootRoller.RollClear(null, "solo", 1, inventory, new System.Random(1)).Drops.Count);
            Assert.AreEqual(0, LootRoller.RollClear(table, "solo", 1, null, new System.Random(1)).Drops.Count);
            Assert.AreEqual(0, LootRoller.RollClear(table, "solo", 1, inventory, null).Drops.Count);
            Assert.AreEqual(0, inventory.Materials.Count);
            Assert.AreEqual(0, inventory.ClearedCells.Count);
        }

        [Test]
        public void DeriveSeed_IsStableAndSpreadsConsecutiveBattles()
        {
            Assert.AreEqual(LootRoller.DeriveSeed(12345, 7), LootRoller.DeriveSeed(12345, 7));
            Assert.GreaterOrEqual(LootRoller.DeriveSeed(-1, int.MaxValue), 0);

            HashSet<int> seeds = new HashSet<int>();
            for (int i = 0; i < 1000; i++)
            {
                seeds.Add(LootRoller.DeriveSeed(12345, i));
            }

            Assert.AreEqual(1000, seeds.Count);
            Assert.AreNotEqual(LootRoller.DeriveSeed(1, 2), LootRoller.DeriveSeed(2, 1));
        }

        private static DropTable Table(DropTableData data)
        {
            return DropTableBuilder.Build(data, DropTableBuilder.TierLookup(DropTableTests.Materials()));
        }

        /// <summary>A fingerprint of <paramref name="clears"/> clears on a mixed schedule with one inventory.</summary>
        private static string Run(DropTable table, int seed, int clears)
        {
            MaterialInventory inventory = new MaterialInventory();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < clears; i++)
            {
                LootResult loot = LootRoller.RollClear(table, i % 3 == 0 ? "horde" : "solo", 1 + (i % 100), inventory,
                                                       new System.Random(LootRoller.DeriveSeed(seed, i)));
                foreach (MaterialStack stack in loot.Drops)
                {
                    sb.Append(i).Append(':').Append(stack.MaterialId).Append('x').Append(stack.Quantity).Append(';');
                }
            }

            return sb.ToString();
        }
    }
}
