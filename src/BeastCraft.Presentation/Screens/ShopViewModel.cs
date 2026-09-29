using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>The Shop/Trader screen's two inner tabs.</summary>
    public enum ShopTab
    {
        Stock = 0,
        Sell = 1
    }

    /// <summary>One of the Trader's current listings, with what a Buy button needs to know (afford it, sold out, needs a beast target).</summary>
    public sealed class ShopListingRow
    {
        public int Index;
        public ShopCategory Category;
        public string ItemId;
        public string Name;
        public string Detail;
        public int Price;
        public int Remaining;
        public int Quantity;
        public bool CanAfford;
        public bool NeedsBeastTarget;

        /// <summary>Why Buy is disabled, or null when it is not.</summary>
        public string DisabledReason;
    }

    /// <summary>
    /// The Trader at a Shop map node, or a camp's travelling trader (economy-and-shop.md, "The Trader").
    /// <see cref="EnsureOpened"/> rolls/freezes its stock the first time it is reached (a Shop node
    /// also through <see cref="CampaignRules.Trade"/>, which marks it visited/cleared and reveals fog
    /// — unchanged Core behaviour; a camp node through <see cref="IShopService.Open"/> alone, since
    /// <see cref="CampaignRules.Trade"/> refuses a non-Shop node type by design). Buy/Sell read and
    /// write through <see cref="CampaignRules.ShopContextFor"/>, recomputed each time (it is derived
    /// from the node, not random, so nothing needs to be cached).
    /// </summary>
    public sealed class ShopViewModel
    {
        private readonly GameSession _session;
        private readonly InventoryGearViewModel _gearSource;
        private readonly int _nodeId;
        private readonly bool _isCamp;

        public ShopViewModel(GameSession session, int nodeId, bool isCamp = false)
        {
            _session = session;
            _nodeId = nodeId;
            _isCamp = isCamp;
            _gearSource = new InventoryGearViewModel(session);
            EnsureOpened();
        }

        public ShopTab Tab { get; private set; } = ShopTab.Stock;
        public int Gold { get; private set; }
        public List<ShopListingRow> Listings { get; } = new List<ShopListingRow>();
        public List<InventoryGearRow> SellableGear { get; } = new List<InventoryGearRow>();

        public void Select(ShopTab tab)
        {
            Tab = tab;
        }

        /// <summary>Opens the Trader once (rolls/freezes its stock the first time this node's key is seen), then refreshes.</summary>
        public void EnsureOpened()
        {
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            ShopService shop = _session.Content.Shop;
            if (save == null || shop == null)
            {
                Refresh();
                return;
            }

            ShopContext context = Context();
            bool alreadyStocked = save.Shops != null && save.Shops.Exists(v => v != null && v.NodeKey == context.NodeKey);
            if (!alreadyStocked)
            {
                if (_isCamp)
                {
                    shop.Open(save, context);
                }
                else
                {
                    CampaignRules.Trade(save, _session.Content.Campaign, _nodeId, shop);
                }

                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
        }

        public void Refresh()
        {
            Listings.Clear();
            Gold = _session.Save?.Gold ?? 0;
            ShopService shop = _session.Content.Shop;
            if (shop != null && _session.Save != null)
            {
                ShopVisit visit = shop.GetStock(_session.Save, Context());
                int index = 0;
                foreach (ShopListing listing in visit?.Listings ?? new List<ShopListing>())
                {
                    Listings.Add(BuildRow(index, listing));
                    index++;
                }
            }

            _gearSource.Refresh();
            SellableGear.Clear();
            foreach (InventoryGearRow row in _gearSource.Gear)
            {
                if (row.CanSell)
                {
                    SellableGear.Add(row);
                }
            }
        }

        /// <summary>The owned beasts a beast-skill tome could be bought for (every owned beast; the rule itself reports why not — <see cref="ShopOutcome.IneligibleTarget"/>).</summary>
        public List<BeastOptionRow> BeastsForTome()
        {
            return BeastOptions.For(_session);
        }

        /// <summary>Buys listing <paramref name="listingIndex"/> (<see cref="ShopService.TryBuy"/>). Autosaves on success.</summary>
        public ShopOutcome Buy(int listingIndex, string targetBeastId, out string message)
        {
            ShopService shop = _session.Content.Shop;
            if (shop == null)
            {
                message = "The Trader is unavailable.";
                return ShopOutcome.UnknownItem;
            }

            ShopPurchaseResult result = shop.TryBuy(_session.Save, Context(), listingIndex, targetBeastId);
            message = MessageFor(result.Outcome);
            if (result.Success)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return result.Outcome;
        }

        /// <summary>Sells unworn gear <paramref name="instanceId"/> (the same rule the Inventory Gear tab's Sell uses). Autosaves on success.</summary>
        public bool Sell(string instanceId, out string message, out int gold)
        {
            bool ok = _gearSource.Sell(instanceId, out message, out gold);
            Refresh();
            return ok;
        }

        private ShopContext Context()
        {
            MapRun run = _session.Save?.Campaign?.ActiveRun;
            MapNode node = run?.Find(_nodeId);
            return CampaignRules.ShopContextFor(run, node);
        }

        private ShopListingRow BuildRow(int index, ShopListing listing)
        {
            GameContent content = _session.Content;
            string name = listing.ItemId;
            string detail = null;
            bool needsTarget = false;
            switch (listing.Category)
            {
                case ShopCategory.Material:
                    SkillMaterialSO material = BeastDetailViewModel.MaterialDefinitions(content).Find(m => m.MaterialId == listing.ItemId);
                    name = material?.DisplayName ?? listing.ItemId;
                    detail = material == null ? null : "Tier " + material.Tier;
                    break;
                case ShopCategory.BeastGear:
                    GearSO gear = content.Battle.GetGear(listing.ItemId);
                    name = gear?.DisplayName ?? listing.ItemId;
                    detail = gear == null ? null : string.Join(", ", BonusLines(gear.Modifiers));
                    break;
                case ShopCategory.AvatarGear:
                    AvatarGearSO avatarGear = content.Battle.GetAvatarGear(listing.ItemId);
                    name = avatarGear?.DisplayName ?? listing.ItemId;
                    detail = avatarGear == null ? null : string.Join(", ", BonusLines(avatarGear.Modifiers));
                    break;
                case ShopCategory.BeastSkill:
                    SkillSO tome = content.Battle.GetSkill(listing.ItemId);
                    name = "Tome: " + (tome?.DisplayName ?? listing.ItemId);
                    detail = "Teaches an owned beast of its species.";
                    needsTarget = true;
                    break;
                case ShopCategory.AvatarSkill:
                    SkillSO active = content.Battle.GetSkill(listing.ItemId);
                    name = active?.DisplayName ?? listing.ItemId;
                    detail = "Avatar active skill.";
                    break;
                case ShopCategory.AvatarPassive:
                    PassiveSkillSO passive = content.Battle.GetPassive(listing.ItemId);
                    name = passive?.DisplayName ?? listing.ItemId;
                    detail = "Avatar passive.";
                    break;
                case ShopCategory.Consumable:
                    ConsumableSO consumable = content.Economy?.Consumables?.Get(listing.ItemId);
                    name = consumable?.DisplayName ?? listing.ItemId;
                    detail = consumable?.Description;
                    break;
                case ShopCategory.Cosmetic:
                    CosmeticOption option = content.Economy?.Cosmetics?.GetOption(listing.ItemId);
                    name = option?.DisplayName ?? listing.ItemId;
                    detail = option?.Category?.DisplayName;
                    break;
            }

            bool afford = Wallet.CanAfford(_session.Save, listing.Price);
            string reason = listing.Remaining <= 0 ? "Sold out" : !afford ? "Not enough gold" : null;
            return new ShopListingRow
            {
                Index = index,
                Category = listing.Category,
                ItemId = listing.ItemId,
                Name = name,
                Detail = detail,
                Price = listing.Price,
                Remaining = listing.Remaining,
                Quantity = listing.Quantity,
                CanAfford = afford,
                NeedsBeastTarget = needsTarget,
                DisabledReason = reason
            };
        }

        private static List<string> BonusLines(List<StatModifier> modifiers)
        {
            List<string> lines = new List<string>();
            foreach (StatModifier modifier in modifiers ?? new List<StatModifier>())
            {
                if (modifier == null)
                {
                    continue;
                }

                if (modifier.FlatBonus != 0)
                {
                    lines.Add((modifier.FlatBonus > 0 ? "+" : string.Empty) + modifier.FlatBonus.ToString(CultureInfo.InvariantCulture) + " " + DerivedStats.ShortName(modifier.Stat));
                }

                if (modifier.PercentBonus != 0f)
                {
                    lines.Add((modifier.PercentBonus > 0f ? "+" : string.Empty) + (modifier.PercentBonus * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "% " +
                              DerivedStats.ShortName(modifier.Stat));
                }
            }

            return lines;
        }

        private static string MessageFor(ShopOutcome outcome)
        {
            switch (outcome)
            {
                case ShopOutcome.Bought:
                    return "Bought.";
                case ShopOutcome.SoldOut:
                    return "Sold out.";
                case ShopOutcome.NotEnoughGold:
                    return "Not enough gold.";
                case ShopOutcome.StackFull:
                    return "You already hold the most of that.";
                case ShopOutcome.IneligibleTarget:
                    return "That beast cannot learn it.";
                case ShopOutcome.AlreadyKnown:
                    return "Already known.";
                case ShopOutcome.LevelTooLow:
                    return "The avatar's level is too low.";
                case ShopOutcome.AlreadyUnlocked:
                    return "Already unlocked.";
                default:
                    return "Cannot buy that.";
            }
        }
    }
}
