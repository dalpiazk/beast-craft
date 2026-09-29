using System;
using System.Collections.Generic;
using BeastCraft.Economy;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One look of the look-token shop's pool: its price, and whether it can be bought now.</summary>
    public sealed class LookTokenRow
    {
        public string Key;
        public string DisplayName;
        public string CategoryName;
        public int Rarity;
        public int Price;
        public bool Owned;

        /// <summary>Not owned, and the balance covers the price.</summary>
        public bool CanAfford;
    }

    /// <summary>
    /// The look-token shop (docs/design/compendium-achievements.md, "Look tokens"): the token balance
    /// (<see cref="PlayerSave.LookTokens"/>) and every token-purchasable look
    /// (<see cref="CosmeticLibrary.TokenPool"/>), owned or not, bought directly with tokens
    /// (<see cref="CosmeticRules.SpendLookToken"/>) — a deterministic, no-RNG alternative to a
    /// battle-drop roll for cosmetics only. Nothing here ever touches a stat.
    /// </summary>
    public sealed class LookTokenShopViewModel
    {
        private readonly GameSession _session;

        public LookTokenShopViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public int Balance { get; private set; }

        public List<LookTokenRow> Looks { get; } = new List<LookTokenRow>();

        /// <summary>Re-reads the save (a purchase, or a duplicate reward granted tokens elsewhere).</summary>
        public void Refresh()
        {
            Looks.Clear();
            PlayerSave save = _session.Save;
            CosmeticLibrary library = _session.Content.Economy?.Cosmetics;
            Balance = save?.LookTokens ?? 0;
            if (library == null)
            {
                return;
            }

            foreach (CosmeticOption option in library.TokenPool())
            {
                bool owned = CosmeticRules.IsUsable(save, option);
                Looks.Add(new LookTokenRow
                {
                    Key = option.Key,
                    DisplayName = option.DisplayName,
                    CategoryName = option.Category?.DisplayName ?? string.Empty,
                    Rarity = option.Rarity,
                    Price = option.TokenPrice,
                    Owned = owned,
                    CanAfford = !owned && Balance >= option.TokenPrice
                });
            }
        }

        /// <summary>Buys <paramref name="key"/> (<see cref="CosmeticRules.SpendLookToken"/>); autosaves and refreshes on success.</summary>
        public LookTokenResult Buy(string key)
        {
            LookTokenResult result = CosmeticRules.SpendLookToken(_session.Save, _session.Content.Economy?.Cosmetics, key);
            if (result == LookTokenResult.Unlocked)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return result;
        }
    }
}
