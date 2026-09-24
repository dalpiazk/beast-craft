namespace BeastCraft.Economy
{
    /// <summary>
    /// The economy's content in one place, for the rules that need more than one library: the gear
    /// library (drops, the Trader, pass and lair rewards) and the consumables. Built by the game from its imported
    /// assets, by tests and the balance simulator from the authored JSON. Any part may be null
    /// (that part of the economy is then off).
    /// </summary>
    public sealed class EconomyContent
    {
        /// <summary>The gear library, or null.</summary>
        public GearLibrary Gear { get; set; }

        /// <summary>The consumable library, or null.</summary>
        public ConsumableLibrary Consumables { get; set; }
    }
}
