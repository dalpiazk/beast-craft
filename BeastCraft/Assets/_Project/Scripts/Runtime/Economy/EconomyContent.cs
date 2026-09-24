using BeastCraft.Save;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The economy's content in one place, for the rules that need more than one library: the gear
    /// library (drops, the Trader, pass and lair rewards), the consumables, the cosmetics and the
    /// Trader's tables. Built by the game from its imported assets, by tests and the balance
    /// simulator from the authored JSON. Any part may be null (that part of the economy is then off).
    /// Also the save validator's <see cref="ISaveEconomyCatalog"/>: consumables and cosmetics a
    /// missing library knows nothing about read as unknown.
    /// </summary>
    public sealed class EconomyContent : ISaveEconomyCatalog
    {
        /// <summary>The gear library, or null.</summary>
        public GearLibrary Gear { get; set; }

        /// <summary>The consumable library, or null.</summary>
        public ConsumableLibrary Consumables { get; set; }

        /// <summary>The cosmetic library, or null.</summary>
        public CosmeticLibrary Cosmetics { get; set; }

        public bool TryGetConsumable(string consumableId, out int maxStack)
        {
            ConsumableSO consumable = Consumables == null ? null : Consumables.Get(consumableId);
            maxStack = consumable == null ? 0 : consumable.MaxStack;
            return consumable != null;
        }

        public bool TryGetCosmeticCategory(string categoryId, out string speciesId, out bool isColor)
        {
            CosmeticCategory category = Cosmetics == null ? null : Cosmetics.GetCategory(categoryId);
            speciesId = category == null || category.IsAvatar ? string.Empty : category.Scope;
            isColor = category != null && category.IsColor;
            return category != null;
        }

        public bool IsKnownCosmeticOption(string categoryId, string optionId)
        {
            return Cosmetics != null && Cosmetics.GetOption(categoryId, optionId) != null;
        }

        public bool IsFreeCosmetic(string categoryId, string optionId)
        {
            CosmeticOption option = Cosmetics == null ? null : Cosmetics.GetOption(categoryId, optionId);
            return option != null && option.IsFree;
        }
    }
}
