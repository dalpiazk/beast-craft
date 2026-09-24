using System;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The plain-data shape of <c>Data/Campaign/location-names.json</c>: the display names of the
    /// region map's locations. A map node's <see cref="MapNode.LabelKey"/>
    /// (<c>"{regionId}/{kind}/{variant}"</c>) names one entry: region <c>regionId</c>'s list for the
    /// <see cref="LocationKind"/> <c>kind</c>, index <c>variant</c>. Every region has exactly
    /// <see cref="NodeMapGenerator.LabelVariants"/> names per kind.
    /// <see cref="LocationNameTableValidator"/> holds the rules; <see cref="LocationNameTable"/> is the
    /// built, indexed form that resolves a key (with a fallback).
    /// <para>
    /// JsonUtility-compatible: arrays, no nullable fields, one named array per kind. Presentation
    /// only: names never change a map, its rules or a save (saves keep the key).
    /// </para>
    /// </summary>
    [Serializable]
    public class LocationNameTableData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Campaign/location-names.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>One name set per region, each region once.</summary>
        public LocationNameSetData[] Regions = new LocationNameSetData[0];
    }

    /// <summary>One region's location names, <see cref="NodeMapGenerator.LabelVariants"/> per kind.</summary>
    [Serializable]
    public class LocationNameSetData
    {
        /// <summary>A <c>regions.json</c> region id.</summary>
        public string RegionId;

        /// <summary>Names for <see cref="LocationKind.Wilds"/> (Battle nodes).</summary>
        public string[] Wilds = new string[0];

        /// <summary>Names for <see cref="LocationKind.Den"/> (Elite nodes).</summary>
        public string[] Den = new string[0];

        /// <summary>Names for <see cref="LocationKind.Camp"/> (Rest nodes).</summary>
        public string[] Camp = new string[0];

        /// <summary>Names for <see cref="LocationKind.TradingPost"/> (Shop nodes).</summary>
        public string[] TradingPost = new string[0];

        /// <summary>Names for <see cref="LocationKind.Pass"/> (Gate nodes).</summary>
        public string[] Pass = new string[0];

        /// <summary>Names for <see cref="LocationKind.Lair"/> (the Boss node).</summary>
        public string[] Lair = new string[0];

        /// <summary>The names for <paramref name="kind"/>; null for a kind this shape has no list for (or a null field).</summary>
        public string[] NamesFor(LocationKind kind)
        {
            switch (kind)
            {
                case LocationKind.Wilds:
                    return Wilds;
                case LocationKind.Den:
                    return Den;
                case LocationKind.Camp:
                    return Camp;
                case LocationKind.TradingPost:
                    return TradingPost;
                case LocationKind.Pass:
                    return Pass;
                case LocationKind.Lair:
                    return Lair;
                default:
                    return null;
            }
        }
    }
}
