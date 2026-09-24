using System;
using System.Collections.Generic;

namespace BeastCraft.Economy
{
    /// <summary>
    /// A stack of one consumable in the save (<c>PlayerSave.Consumables</c>, schema 4): its
    /// <c>ConsumableSO.ConsumableId</c> and how many are held, 1 to the consumable's max stack.
    /// </summary>
    [Serializable]
    public class ConsumableStack
    {
        /// <summary>Parameterless, for serializers.</summary>
        public ConsumableStack()
        {
        }

        public ConsumableStack(string consumableId, int quantity)
        {
            ConsumableId = consumableId;
            Quantity = quantity;
        }

        /// <summary>The consumable's stable id (never renamed after ship).</summary>
        public string ConsumableId;

        /// <summary>How many are held; a stack that reaches 0 is removed.</summary>
        public int Quantity;
    }

    /// <summary>
    /// The account-wide cosmetic unlocks (<c>PlayerSave.Cosmetics</c>, schema 4): every look the player
    /// has earned, as <c>"categoryId/optionId"</c> keys (<see cref="Key"/>; category ids are globally
    /// unique). Defaults and starter looks are free and never listed. Unlocks are forever.
    /// </summary>
    [Serializable]
    public class CosmeticCollection
    {
        /// <summary>Unlocked <c>"categoryId/optionId"</c> keys, in the order earned. Each at most once.</summary>
        public List<string> Unlocked = new List<string>();

        /// <summary>The key of <paramref name="optionId"/> in <paramref name="categoryId"/>.</summary>
        public static string Key(string categoryId, string optionId)
        {
            return (categoryId ?? string.Empty) + "/" + (optionId ?? string.Empty);
        }

        /// <summary>Whether <paramref name="key"/> is unlocked.</summary>
        public bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && Unlocked != null && Unlocked.Contains(key);
        }

        /// <summary>Unlocks <paramref name="key"/>. False when it already was, or the key is empty.</summary>
        public bool Unlock(string key)
        {
            if (string.IsNullOrEmpty(key) || Has(key))
            {
                return false;
            }

            if (Unlocked == null)
            {
                Unlocked = new List<string>();
            }

            Unlocked.Add(key);
            return true;
        }
    }

    /// <summary>What a <see cref="ShopListing"/> sells. Saved as its number: append only.</summary>
    public enum ShopCategory
    {
        /// <summary>A skill material (<c>SkillMaterialSO.MaterialId</c>).</summary>
        Material = 0,

        /// <summary>A piece of beast gear (<c>GearSO.GearId</c>); a bought piece is a new instance.</summary>
        BeastGear = 1,

        /// <summary>A piece of avatar gear (<c>AvatarGearSO.AvatarGearId</c>).</summary>
        AvatarGear = 2,

        /// <summary>A beast skill tome, taught to one chosen beast of a species that learns it.</summary>
        BeastSkill = 3,

        /// <summary>An avatar active skill (learned by the avatar).</summary>
        AvatarSkill = 4,

        /// <summary>An avatar passive (learned by the avatar).</summary>
        AvatarPassive = 5,

        /// <summary>A consumable (<c>ConsumableSO.ConsumableId</c>).</summary>
        Consumable = 6,

        /// <summary>A cosmetic look, as its <c>"categoryId/optionId"</c> key.</summary>
        Cosmetic = 7
    }

    /// <summary>One line of a Trader's stock, frozen into the save on the first visit.</summary>
    [Serializable]
    public class ShopListing
    {
        /// <summary>What it is.</summary>
        public ShopCategory Category;

        /// <summary>The item's id in its category (a cosmetic's <c>"categoryId/optionId"</c> key).</summary>
        public string ItemId;

        /// <summary>How many the Trader stocked.</summary>
        public int Quantity = 1;

        /// <summary>The price of one, in gold.</summary>
        public int Price;

        /// <summary>How many are left (0 = sold out).</summary>
        public int Remaining = 1;
    }

    /// <summary>
    /// A Trader's stock as the player first saw it (<c>PlayerSave.Shops</c>, schema 4): rolled once
    /// per trading post (<see cref="NodeKey"/>) and then frozen, so leaving and coming back (or
    /// reloading) never re-rolls it and purchases stick.
    /// </summary>
    [Serializable]
    public class ShopVisit
    {
        /// <summary>The trading post's stable key (<c>ShopContext.NodeKey</c>).</summary>
        public string NodeKey;

        /// <summary>The stock, in display order.</summary>
        public List<ShopListing> Listings = new List<ShopListing>();
    }
}
