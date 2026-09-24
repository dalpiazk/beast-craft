using BeastCraft.Save;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The player's gold, <see cref="PlayerSave.Gold"/> (save schema 4): the one currency, earned by
    /// clearing battles (<c>drop-tables.json</c> <c>Gold</c>, see <c>PostBattleAward.AwardGold</c>)
    /// and selling gear, spent at the Trader (<c>ShopService</c>). Held in
    /// [0, <see cref="MaxGold"/>]: income past the ceiling is lost, never wrapped. Non-throwing; a null
    /// save holds nothing and spends nothing.
    /// </summary>
    public static class Wallet
    {
        /// <summary>The most gold a save can hold.</summary>
        public const int MaxGold = 9999999;

        /// <summary>The gold <paramref name="save"/> holds (clamped into range; 0 for a null save).</summary>
        public static int Balance(PlayerSave save)
        {
            return save == null ? 0 : Clamp(save.Gold);
        }

        /// <summary>
        /// Adds <paramref name="amount"/> (a negative amount adds nothing) up to <see cref="MaxGold"/>.
        /// Returns what was actually added.
        /// </summary>
        public static int Add(PlayerSave save, int amount)
        {
            if (save == null || amount <= 0)
            {
                return 0;
            }

            int before = Clamp(save.Gold);
            long after = (long)before + amount;
            save.Gold = after > MaxGold ? MaxGold : (int)after;
            return save.Gold - before;
        }

        /// <summary>Whether <paramref name="save"/> holds at least <paramref name="price"/> (a price below 0 is never affordable).</summary>
        public static bool CanAfford(PlayerSave save, int price)
        {
            return save != null && price >= 0 && Clamp(save.Gold) >= price;
        }

        /// <summary>Spends <paramref name="price"/> when affordable. False (nothing spent) otherwise.</summary>
        public static bool TrySpend(PlayerSave save, int price)
        {
            if (!CanAfford(save, price))
            {
                return false;
            }

            save.Gold = Clamp(save.Gold) - price;
            return true;
        }

        private static int Clamp(int gold)
        {
            return gold < 0 ? 0 : gold > MaxGold ? MaxGold : gold;
        }
    }
}
