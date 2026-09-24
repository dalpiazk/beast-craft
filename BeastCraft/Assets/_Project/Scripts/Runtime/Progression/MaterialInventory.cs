using System;
using System.Collections.Generic;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The player's skill-training materials and the loot state around them: how many of each
    /// material is held, which (shape, level band) cells have been cleared (for the first-clear
    /// bonus), and the pity counters per (shape, material tier). See the battle-system design doc,
    /// "Material economy".
    /// <para>
    /// Plain serializable save data with public fields, like <see cref="BeastSkillBook"/>, written
    /// as-is by <c>JsonUtility</c> in Unity or <c>System.Text.Json</c> (<c>IncludeFields = true</c>)
    /// outside it. <c>JsonUtility</c> cannot serialize a dictionary, so every map is a list of
    /// [Serializable] entries, searched linearly (the lists are a handful of entries long). Materials
    /// are named by their stable <see cref="SkillMaterialSO.MaterialId"/>. Non-throwing: a null or
    /// empty id and a non-positive count change nothing.
    /// </para>
    /// </summary>
    [Serializable]
    public class MaterialInventory
    {
        /// <summary>Held materials, one entry per material id, in the order first gained. Counts are positive; a spent-out entry is removed.</summary>
        public List<MaterialStack> Materials = new List<MaterialStack>();

        /// <summary>Every (shape, band) cell cleared at least once — the first-clear bonus has been paid for these.</summary>
        public List<ClearedCell> ClearedCells = new List<ClearedCell>();

        /// <summary>Consecutive dry clears per (shape, tier) (see <see cref="PityData"/>). A missing entry is 0.</summary>
        public List<PityCounter> Pity = new List<PityCounter>();

        /// <summary>How many of <paramref name="materialId"/> are held (0 when none or the id is empty).</summary>
        public int GetCount(string materialId)
        {
            MaterialStack stack = Find(materialId);
            return stack == null ? 0 : stack.Quantity;
        }

        /// <summary>Adds <paramref name="quantity"/> of <paramref name="materialId"/>. False (no change) for an empty id or a non-positive quantity.</summary>
        public bool Add(string materialId, int quantity)
        {
            if (string.IsNullOrEmpty(materialId) || quantity <= 0)
            {
                return false;
            }

            if (Materials == null)
            {
                Materials = new List<MaterialStack>();
            }

            MaterialStack stack = Find(materialId);
            if (stack == null)
            {
                Materials.Add(new MaterialStack(materialId, quantity));
            }
            else
            {
                long total = (long)stack.Quantity + quantity;
                stack.Quantity = total > int.MaxValue ? int.MaxValue : (int)total;
            }

            return true;
        }

        /// <summary>
        /// Removes <paramref name="quantity"/> of <paramref name="materialId"/> when that many are
        /// held; otherwise changes nothing and returns false. A stack spent to 0 is removed.
        /// </summary>
        public bool TryConsume(string materialId, int quantity)
        {
            MaterialStack stack = Find(materialId);
            if (stack == null || quantity <= 0 || stack.Quantity < quantity)
            {
                return false;
            }

            stack.Quantity -= quantity;
            if (stack.Quantity == 0)
            {
                Materials.Remove(stack);
            }

            return true;
        }

        /// <summary>Whether the cell (<paramref name="shape"/>, band starting at <paramref name="bandMinLevel"/>) has been cleared before.</summary>
        public bool HasCleared(string shape, int bandMinLevel)
        {
            return FindCleared(shape, bandMinLevel) != null;
        }

        /// <summary>Records a clear of the cell. True when this was the first one (the first-clear bonus is due).</summary>
        public bool MarkCleared(string shape, int bandMinLevel)
        {
            if (string.IsNullOrEmpty(shape) || HasCleared(shape, bandMinLevel))
            {
                return false;
            }

            if (ClearedCells == null)
            {
                ClearedCells = new List<ClearedCell>();
            }

            ClearedCells.Add(new ClearedCell { Shape = shape, BandMinLevel = bandMinLevel });
            return true;
        }

        /// <summary>Consecutive dry clears of <paramref name="shape"/> for <paramref name="tier"/> (0 when none recorded).</summary>
        public int GetPity(string shape, int tier)
        {
            PityCounter counter = FindPity(shape, tier);
            return counter == null ? 0 : counter.Misses;
        }

        /// <summary>Sets the counter (a negative value is read as 0). An empty shape is ignored.</summary>
        public void SetPity(string shape, int tier, int misses)
        {
            if (string.IsNullOrEmpty(shape))
            {
                return;
            }

            PityCounter counter = FindPity(shape, tier);
            if (counter == null)
            {
                if (Pity == null)
                {
                    Pity = new List<PityCounter>();
                }

                counter = new PityCounter { Shape = shape, Tier = tier };
                Pity.Add(counter);
            }

            counter.Misses = misses < 0 ? 0 : misses;
        }

        private MaterialStack Find(string materialId)
        {
            if (string.IsNullOrEmpty(materialId) || Materials == null)
            {
                return null;
            }

            for (int i = 0; i < Materials.Count; i++)
            {
                if (Materials[i] != null && string.Equals(Materials[i].MaterialId, materialId, StringComparison.Ordinal))
                {
                    return Materials[i];
                }
            }

            return null;
        }

        private ClearedCell FindCleared(string shape, int bandMinLevel)
        {
            if (string.IsNullOrEmpty(shape) || ClearedCells == null)
            {
                return null;
            }

            for (int i = 0; i < ClearedCells.Count; i++)
            {
                ClearedCell cell = ClearedCells[i];
                if (cell != null && cell.BandMinLevel == bandMinLevel && string.Equals(cell.Shape, shape, StringComparison.Ordinal))
                {
                    return cell;
                }
            }

            return null;
        }

        private PityCounter FindPity(string shape, int tier)
        {
            if (string.IsNullOrEmpty(shape) || Pity == null)
            {
                return null;
            }

            for (int i = 0; i < Pity.Count; i++)
            {
                PityCounter counter = Pity[i];
                if (counter != null && counter.Tier == tier && string.Equals(counter.Shape, shape, StringComparison.Ordinal))
                {
                    return counter;
                }
            }

            return null;
        }
    }

    /// <summary>A quantity of one material: an inventory entry, or one line of a <see cref="LootResult"/>.</summary>
    [Serializable]
    public class MaterialStack
    {
        /// <summary>Parameterless, for serializers.</summary>
        public MaterialStack()
        {
        }

        public MaterialStack(string materialId, int quantity)
        {
            MaterialId = materialId;
            Quantity = quantity;
        }

        /// <summary>A <see cref="SkillMaterialSO.MaterialId"/>.</summary>
        public string MaterialId;

        public int Quantity;
    }

    /// <summary>A (shape, level band) cell that has been cleared; the band is keyed by its <see cref="LevelBandData.MinLevel"/>.</summary>
    [Serializable]
    public class ClearedCell
    {
        public string Shape;

        public int BandMinLevel;
    }

    /// <summary>One pity counter: consecutive clears of <see cref="Shape"/> that could drop <see cref="Tier"/> and did not.</summary>
    [Serializable]
    public class PityCounter
    {
        public string Shape;

        public int Tier;

        public int Misses;
    }
}
