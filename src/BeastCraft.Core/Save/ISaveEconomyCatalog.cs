namespace BeastCraft.Save
{
    /// <summary>
    /// The economy content a save's schema-4 data may refer to, for <see cref="SaveValidator"/>:
    /// consumables (and their max stack) and cosmetic categories and options (whose owner each
    /// category belongs to, and which looks are free). The game's <c>EconomyContent</c> implements it.
    /// </summary>
    public interface ISaveEconomyCatalog
    {
        /// <summary>Whether <paramref name="consumableId"/> is a known consumable, and if so its max stack.</summary>
        bool TryGetConsumable(string consumableId, out int maxStack);

        /// <summary>
        /// Whether <paramref name="categoryId"/> is a known cosmetic category, and if so its owner:
        /// <paramref name="speciesId"/> is "" for an avatar category, else the species whose beasts
        /// wear it; <paramref name="isColor"/> whether it holds a colour rather than an option.
        /// </summary>
        bool TryGetCosmeticCategory(string categoryId, out string speciesId, out bool isColor);

        /// <summary>Whether <paramref name="optionId"/> is an option of discrete category <paramref name="categoryId"/>.</summary>
        bool IsKnownCosmeticOption(string categoryId, string optionId);

        /// <summary>Whether the look is free to wear without an unlock (its category's default, or a starter look).</summary>
        bool IsFreeCosmetic(string categoryId, string optionId);
    }
}
