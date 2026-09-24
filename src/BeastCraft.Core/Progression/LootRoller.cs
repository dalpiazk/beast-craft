using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Rolls a cleared battle's material drops against a <see cref="DropTable"/> and banks them in a
    /// <see cref="MaterialInventory"/>. See the battle-system design doc, "Material economy".
    /// <para>
    /// <strong>The roll, for the cell of (shape, level):</strong>
    /// </para>
    /// <list type="number">
    /// <item>Every entry rolls independently, in authored order: <c>rng.Next(100) &lt; Chance</c>
    /// drops it, and the quantity is <c>MinQty + rng.Next(MaxQty - MinQty + 1)</c>. Both draws are
    /// always taken, hit or miss, so the number of draws per clear depends only on the cell, never
    /// on luck: a seeded run is reproducible and a tuning change to one chance does not reshuffle
    /// every later roll.</item>
    /// <item>Pity, per material tier present in the cell with a <see cref="DropTable.PityThreshold"/>
    /// N: if no entry of that tier dropped and the (shape, tier) counter already stands at N, the
    /// cell's first entry of that tier is forced (at its <c>MinQty</c>). A tier that dropped
    /// (naturally or forced) resets its counter to 0; one that did not adds 1. Tiers the cell cannot
    /// drop leave their counters alone, so the protection is "N dry clears of a shape that could
    /// drop it".</item>
    /// <item>First clear: the first clear of each (shape, band) grants the band's
    /// <see cref="DropBand.FirstClearMaterialId"/> once, recorded in
    /// <see cref="MaterialInventory.ClearedCells"/>. It does not touch pity.</item>
    /// </list>
    /// <para>
    /// Deterministic: the only randomness is the <see cref="System.Random"/> handed in (seed it per
    /// battle with <see cref="DeriveSeed"/>). Drops only on a clear — the caller decides that
    /// (<see cref="PostBattleAward"/> rolls only on <c>PlayerVictory</c>). Non-throwing: a null
    /// table, inventory or rng, or a shape the table does not know, rolls nothing.
    /// </para>
    /// </summary>
    public static class LootRoller
    {
        /// <summary>
        /// Rolls one clear of (<paramref name="shape"/>, <paramref name="level"/>), adds the drops to
        /// <paramref name="inventory"/> and updates its pity counters and cleared cells. Returns what
        /// was granted (empty, never null).
        /// </summary>
        public static LootResult RollClear(DropTable table, string shape, int level, MaterialInventory inventory, Random rng)
        {
            LootResult result = new LootResult();
            if (table == null || inventory == null || rng == null)
            {
                return result;
            }

            DropBand band = table.BandForLevel(level);
            DropCell cell = band == null ? null : band.GetCell(shape);
            if (cell == null)
            {
                return result;
            }

            IReadOnlyList<DropEntry> entries = cell.Entries;
            int[] granted = new int[entries.Count];

            for (int i = 0; i < entries.Count; i++)
            {
                DropEntry entry = entries[i];
                bool hit = rng.Next(100) < entry.Chance;
                int quantity = entry.MinQty + rng.Next(entry.MaxQty - entry.MinQty + 1);
                granted[i] = hit ? quantity : 0;
            }

            ApplyPity(table, shape, entries, granted, inventory, result);

            for (int i = 0; i < entries.Count; i++)
            {
                if (granted[i] > 0)
                {
                    result.Add(entries[i].MaterialId, granted[i]);
                }
            }

            if (inventory.MarkCleared(shape, band.MinLevel) && !string.IsNullOrEmpty(band.FirstClearMaterialId))
            {
                result.FirstClear = true;
                result.Add(band.FirstClearMaterialId, band.FirstClearQuantity);
            }

            foreach (MaterialStack stack in result.Drops)
            {
                inventory.Add(stack.MaterialId, stack.Quantity);
            }

            return result;
        }

        /// <summary>
        /// Rolls the cell of (<paramref name="shape"/>, <paramref name="level"/>) <paramref name="rolls"/>
        /// times with every chance scaled by <paramref name="chanceMultiplier"/> (clamped to 0-1), adds
        /// the drops to <paramref name="inventory"/> and returns them (empty, never null). For rewards
        /// that are not a clear (idle rewards): <strong>no pity and no first-clear credit</strong> — the
        /// inventory's pity counters and cleared cells are never read or written. Per roll and entry,
        /// in authored order: <c>rng.Next(10000) &lt; round(Chance × multiplier × 100)</c> drops it and
        /// the quantity is drawn as in <see cref="RollClear"/>; both draws are always taken.
        /// Non-throwing: a null table, inventory or rng, an unknown shape or no rolls grants nothing.
        /// </summary>
        public static LootResult RollScaled(DropTable table, string shape, int level, double chanceMultiplier, int rolls, MaterialInventory inventory, Random rng)
        {
            LootResult result = new LootResult();
            DropCell cell = table == null || inventory == null || rng == null ? null : table.GetCell(shape, level);
            if (cell == null || rolls <= 0)
            {
                return result;
            }

            double multiplier = double.IsNaN(chanceMultiplier) ? 0.0 : Math.Max(0.0, Math.Min(1.0, chanceMultiplier));
            IReadOnlyList<DropEntry> entries = cell.Entries;
            for (int roll = 0; roll < rolls; roll++)
            {
                foreach (DropEntry entry in entries)
                {
                    bool hit = rng.Next(10000) < (int)Math.Round(entry.Chance * multiplier * 100.0, MidpointRounding.AwayFromZero);
                    int quantity = entry.MinQty + rng.Next(entry.MaxQty - entry.MinQty + 1);
                    if (hit && quantity > 0)
                    {
                        result.Add(entry.MaterialId, quantity);
                    }
                }
            }

            foreach (MaterialStack stack in result.Drops)
            {
                inventory.Add(stack.MaterialId, stack.Quantity);
            }

            return result;
        }

        /// <summary>
        /// A per-battle seed from a base seed and the battle's index: a SplitMix64-style mix, stable
        /// across platforms and runtimes (unlike <c>string.GetHashCode</c>), so battle
        /// <paramref name="battleIndex"/> of a run seeded <paramref name="baseSeed"/> always rolls the
        /// same loot.
        /// </summary>
        public static int DeriveSeed(int baseSeed, int battleIndex)
        {
            unchecked
            {
                ulong z = ((ulong)(uint)baseSeed << 32) ^ (uint)battleIndex;
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z & 0x7FFFFFFF);
            }
        }

        private static void ApplyPity(DropTable table, string shape, IReadOnlyList<DropEntry> entries, int[] granted, MaterialInventory inventory, LootResult result)
        {
            List<int> tiers = new List<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                int tier = entries[i].Tier;
                if (tier > 0 && !tiers.Contains(tier))
                {
                    tiers.Add(tier);
                }
            }

            tiers.Sort();
            foreach (int tier in tiers)
            {
                int threshold = table.PityThreshold(tier);
                if (threshold <= 0)
                {
                    continue;
                }

                int first = -1;
                bool dropped = false;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Tier == tier)
                    {
                        first = first < 0 ? i : first;
                        dropped |= granted[i] > 0;
                    }
                }

                int misses = inventory.GetPity(shape, tier);
                if (!dropped && misses >= threshold)
                {
                    granted[first] = entries[first].MinQty;
                    dropped = true;
                    result.PityForcedTiers.Add(tier);
                }

                inventory.SetPity(shape, tier, dropped ? 0 : misses + 1);
            }
        }
    }

    /// <summary>What one clear granted.</summary>
    public sealed class LootResult
    {
        private readonly List<MaterialStack> _drops = new List<MaterialStack>();

        /// <summary>Every material granted (table rolls, pity and the first-clear bonus together), one line per material id, in roll order.</summary>
        public IReadOnlyList<MaterialStack> Drops
        {
            get { return _drops; }
        }

        /// <summary>Whether this was the cell's first clear (its bonus is included in <see cref="Drops"/>).</summary>
        public bool FirstClear { get; internal set; }

        /// <summary>The tiers pity forced this clear, ascending.</summary>
        public List<int> PityForcedTiers { get; } = new List<int>();

        /// <summary>The granted quantity of <paramref name="materialId"/> (0 when none).</summary>
        public int QuantityOf(string materialId)
        {
            foreach (MaterialStack stack in _drops)
            {
                if (string.Equals(stack.MaterialId, materialId, StringComparison.Ordinal))
                {
                    return stack.Quantity;
                }
            }

            return 0;
        }

        internal void Add(string materialId, int quantity)
        {
            foreach (MaterialStack stack in _drops)
            {
                if (string.Equals(stack.MaterialId, materialId, StringComparison.Ordinal))
                {
                    stack.Quantity += quantity;
                    return;
                }
            }

            _drops.Add(new MaterialStack(materialId, quantity));
        }
    }
}
