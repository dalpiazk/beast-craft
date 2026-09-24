using BeastCraft.Save;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The shop a map's Shop ("Trader") node opens. <strong>An interface only, for now:</strong> the
    /// gold currency and what the shop sells (skill materials, gear, new skills, consumables) are
    /// being designed separately (the economy lane); it will implement this. Until then the game
    /// passes <see cref="ShopServiceStub"/> (or null), and a Shop node is simply visited.
    /// </summary>
    public interface IShopService
    {
        /// <summary>
        /// Opens the shop for <paramref name="save"/> at <paramref name="context"/>. Returns whether a
        /// shop was actually offered (the stub never offers one). The shop owns every change it makes
        /// to the save; the campaign only marks the node visited.
        /// </summary>
        bool Open(PlayerSave save, ShopContext context);
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

        /// <summary>The node's level (its row's), for level-banded stock.</summary>
        public int Level { get; }

        /// <summary>The node's <see cref="MapNode.EncounterSeed"/>: a stable per-node seed for the stock.</summary>
        public int Seed { get; }
    }

    /// <summary>The placeholder shop until the economy lands: offers nothing, changes nothing.</summary>
    public sealed class ShopServiceStub : IShopService
    {
        public bool Open(PlayerSave save, ShopContext context)
        {
            return false;
        }
    }
}
