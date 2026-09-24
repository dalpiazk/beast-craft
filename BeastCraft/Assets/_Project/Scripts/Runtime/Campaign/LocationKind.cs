namespace BeastCraft.Campaign
{
    /// <summary>
    /// What a map location looks like to the player: the place a future spatial map UI draws and
    /// names (a stretch of wilds, a beast den, a camp, a trading post, a pass, a lair). The player
    /// explores a region's <em>map</em>; <see cref="MapNodeType"/> is the internal pacing model
    /// behind it (what the location does), this is how it is presented. Today the two map one to
    /// one (<see cref="LocationKinds.For"/>); they are separate so the open world can add landmark
    /// kinds (ruins, shrines, vistas) without touching the pacing rules.
    /// <para>Saved as its number (<see cref="MapNode.Kind"/>): never renumber a member, only append.</para>
    /// </summary>
    public enum LocationKind
    {
        /// <summary>Open wilds where wild beasts roam: a <see cref="MapNodeType.Battle"/>.</summary>
        Wilds = 0,

        /// <summary>A strong beast's den: an <see cref="MapNodeType.Elite"/>.</summary>
        Den = 1,

        /// <summary>A safe camp: a <see cref="MapNodeType.Rest"/>.</summary>
        Camp = 2,

        /// <summary>A travelling trader's post: a <see cref="MapNodeType.Shop"/>.</summary>
        TradingPost = 3,

        /// <summary>The guarded pass out of this part of the region: a <see cref="MapNodeType.Gate"/>.</summary>
        Pass = 4,

        /// <summary>The region boss's lair: a <see cref="MapNodeType.Boss"/>.</summary>
        Lair = 5
    }

    /// <summary>Helpers over <see cref="LocationKind"/>.</summary>
    public static class LocationKinds
    {
        /// <summary>The location kind a node of <paramref name="type"/> is presented as.</summary>
        public static LocationKind For(MapNodeType type)
        {
            switch (type)
            {
                case MapNodeType.Elite:
                    return LocationKind.Den;
                case MapNodeType.Rest:
                    return LocationKind.Camp;
                case MapNodeType.Shop:
                    return LocationKind.TradingPost;
                case MapNodeType.Gate:
                    return LocationKind.Pass;
                case MapNodeType.Boss:
                    return LocationKind.Lair;
                default:
                    return LocationKind.Wilds;
            }
        }

        /// <summary>The kind's lowercase key, as used in <see cref="MapNode.LabelKey"/> (<c>wilds</c>, <c>den</c>, <c>camp</c>, <c>trading_post</c>, <c>pass</c>, <c>lair</c>).</summary>
        public static string Key(LocationKind kind)
        {
            switch (kind)
            {
                case LocationKind.Den:
                    return "den";
                case LocationKind.Camp:
                    return "camp";
                case LocationKind.TradingPost:
                    return "trading_post";
                case LocationKind.Pass:
                    return "pass";
                case LocationKind.Lair:
                    return "lair";
                default:
                    return "wilds";
            }
        }

        /// <summary>The kind whose <see cref="Key"/> is <paramref name="key"/> (case-sensitive); false for anything else.</summary>
        public static bool TryParseKey(string key, out LocationKind kind)
        {
            switch (key)
            {
                case "wilds":
                    kind = LocationKind.Wilds;
                    return true;
                case "den":
                    kind = LocationKind.Den;
                    return true;
                case "camp":
                    kind = LocationKind.Camp;
                    return true;
                case "trading_post":
                    kind = LocationKind.TradingPost;
                    return true;
                case "pass":
                    kind = LocationKind.Pass;
                    return true;
                case "lair":
                    kind = LocationKind.Lair;
                    return true;
                default:
                    kind = LocationKind.Wilds;
                    return false;
            }
        }
    }
}
