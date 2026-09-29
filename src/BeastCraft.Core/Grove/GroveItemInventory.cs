using System;
using System.Collections.Generic;

namespace BeastCraft.Grove
{
    /// <summary>
    /// A generic, counted pool of Grove items (<c>PlayerSave.Grove.Items</c>): grown varieties
    /// (<c>GardenRules.Harvest</c>), crafted outputs (<c>GardenRules.Craft</c>) and expedition
    /// trinkets (<c>ExpeditionRules.Collect</c>) all land here, keyed by a plain id (a variety id, a
    /// recipe's dye output id, a destination's trinket id) with a count. No dictionaries
    /// (<c>JsonUtility</c> parity), like <c>MaterialInventory</c>: a list, searched linearly. This is
    /// the seam a later PR reads for peaceful clears (spending an item to clear an ordinary battle
    /// location) and colour evolutions (spending a dye to shift a beast's colour) — see
    /// <c>docs/design/grove.md</c>, "Producer additions".
    /// </summary>
    [Serializable]
    public class GroveItemInventory
    {
        public List<GroveItemStack> Items = new List<GroveItemStack>();

        /// <summary>How many of <paramref name="itemId"/> are held (0 if none or unknown id).</summary>
        public int GetCount(string itemId)
        {
            GroveItemStack stack = Find(itemId);
            return stack == null ? 0 : stack.Quantity;
        }

        /// <summary>Adds <paramref name="quantity"/> (must be positive) of <paramref name="itemId"/>. Returns whether anything was added.</summary>
        public bool Add(string itemId, int quantity)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            {
                return false;
            }

            GroveItemStack stack = Find(itemId);
            if (stack == null)
            {
                Items.Add(new GroveItemStack(itemId, quantity));
            }
            else
            {
                stack.Quantity += quantity;
            }

            return true;
        }

        /// <summary>
        /// Consumes <paramref name="quantity"/> (must be positive) of <paramref name="itemId"/> when at
        /// least that many are held; otherwise refuses and changes nothing. Returns whether it consumed.
        /// </summary>
        public bool TryConsume(string itemId, int quantity)
        {
            if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            {
                return false;
            }

            GroveItemStack stack = Find(itemId);
            if (stack == null || stack.Quantity < quantity)
            {
                return false;
            }

            stack.Quantity -= quantity;
            if (stack.Quantity <= 0)
            {
                Items.Remove(stack);
            }

            return true;
        }

        /// <summary>Replaces a null list with an empty one and drops null, empty-id, non-positive and duplicate-id entries (merged into the first). Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (Items == null)
            {
                Items = new List<GroveItemStack>();
                repaired++;
            }

            repaired += Items.RemoveAll(stack => stack == null || string.IsNullOrEmpty(stack.ItemId) || stack.Quantity <= 0);

            Dictionary<string, GroveItemStack> byId = new Dictionary<string, GroveItemStack>(StringComparer.Ordinal);
            List<GroveItemStack> merged = new List<GroveItemStack>();
            foreach (GroveItemStack stack in Items)
            {
                if (byId.TryGetValue(stack.ItemId, out GroveItemStack existing))
                {
                    existing.Quantity += stack.Quantity;
                    repaired++;
                }
                else
                {
                    byId[stack.ItemId] = stack;
                    merged.Add(stack);
                }
            }

            Items = merged;
            return repaired;
        }

        private GroveItemStack Find(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || Items == null)
            {
                return null;
            }

            foreach (GroveItemStack stack in Items)
            {
                if (stack != null && stack.ItemId == itemId)
                {
                    return stack;
                }
            }

            return null;
        }
    }

    /// <summary>One held Grove item stack.</summary>
    [Serializable]
    public class GroveItemStack
    {
        public GroveItemStack()
        {
        }

        public GroveItemStack(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId;

        public int Quantity;
    }
}
