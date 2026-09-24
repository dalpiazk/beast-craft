namespace BeastCraft.Campaign
{
    /// <summary>
    /// What a <see cref="MapNode"/> is. Saved as its number: never renumber a member, only append.
    /// </summary>
    public enum MapNodeType
    {
        /// <summary>A generated encounter of one of the region's battle shapes, at the node's level.</summary>
        Battle = 0,

        /// <summary>A generated encounter of the <c>elite</c> shape, one level above the path.</summary>
        Elite = 1,

        /// <summary>Camp: no battle; train one chosen beast (<see cref="CampaignRules.Camp"/>).</summary>
        Rest = 2,

        /// <summary>Trader: no battle; opens the shop (<see cref="IShopService"/>; a stub until the economy lands).</summary>
        Shop = 3,

        /// <summary>The top of a stage that is not the last: clearing it clears the stage.</summary>
        Gate = 4,

        /// <summary>The top of the region's last stage: the region boss, whose clear grants its seal.</summary>
        Boss = 5
    }
}
