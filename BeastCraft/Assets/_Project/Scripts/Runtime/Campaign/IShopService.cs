using System.Globalization;
using BeastCraft.Economy;
using BeastCraft.Save;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The shop a map's trading post (a Shop node, the "Trader") opens. The economy's
    /// <see cref="ShopService"/> implements it (gold, frozen stock, buying and selling);
    /// <see cref="ShopServiceStub"/> (or null) offers nothing, and the trading post is simply visited.
    /// </summary>
    public interface IShopService
    {
        /// <summary>
        /// Opens the shop for <paramref name="save"/> at <paramref name="context"/>: its stock is rolled
        /// (and frozen into the save) on the first visit. Returns whether anything is offered. The shop
        /// owns every change it makes to the save; the campaign only marks the node visited.
        /// </summary>
        bool Open(PlayerSave save, ShopContext context);

        /// <summary>The trading post's stock (frozen into the save on first sight); null when the shop offers nothing.</summary>
        ShopVisit GetStock(PlayerSave save, ShopContext context);

        /// <summary>Buys one of listing <paramref name="listingIndex"/> (a skill tome for <paramref name="targetBeastId"/>). Changes the save only when bought.</summary>
        ShopPurchaseResult TryBuy(PlayerSave save, ShopContext context, int listingIndex, string targetBeastId = null);

        /// <summary>Sells an unworn gear instance back. Changes the save only when sold.</summary>
        ShopSaleResult TrySellGear(PlayerSave save, string gearInstanceId);
    }

    /// <summary>Where a shop is being opened: which expedition node, at what level, with which seed (for its stock).</summary>
    public sealed class ShopContext
    {
        public ShopContext(string regionId, int stage, int nodeId, int level, int seed)
        {
            RegionId = regionId;
            Stage = stage;
            NodeId = nodeId;
            Level = level;
            Seed = seed;
        }

        public string RegionId { get; }

        public int Stage { get; }

        public int NodeId { get; }

        /// <summary>The node's level (its row's), for level-banded stock: the Trader's level.</summary>
        public int Level { get; }

        /// <summary>The node's <see cref="MapNode.EncounterSeed"/>: a stable per-node seed for the stock.</summary>
        public int Seed { get; }

        /// <summary>
        /// The trading post's stable key, <c>"{RegionId}/{Stage}/{NodeId}/{Seed}"</c>: an expedition's
        /// map seed makes every node's seed unique, so this names one trading post of one expedition.
        /// </summary>
        public string NodeKey
        {
            get
            {
                return (RegionId ?? string.Empty) + "/" + Stage.ToString(CultureInfo.InvariantCulture) + "/" + NodeId.ToString(CultureInfo.InvariantCulture) + "/" +
                       Seed.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>A shop that offers nothing and changes nothing (for tests and a game without the economy).</summary>
    public sealed class ShopServiceStub : IShopService
    {
        public bool Open(PlayerSave save, ShopContext context)
        {
            return false;
        }

        public ShopVisit GetStock(PlayerSave save, ShopContext context)
        {
            return null;
        }

        public ShopPurchaseResult TryBuy(PlayerSave save, ShopContext context, int listingIndex, string targetBeastId = null)
        {
            return new ShopPurchaseResult(ShopOutcome.UnknownListing, null, 0, null);
        }

        public ShopSaleResult TrySellGear(PlayerSave save, string gearInstanceId)
        {
            return new ShopSaleResult(ShopOutcome.UnknownItem, null, 0);
        }
    }
}
