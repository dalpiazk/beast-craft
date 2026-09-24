using System;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The plain-data shape of <c>Data/Items/gear-library.json</c>: every piece of beast gear
    /// (<c>GearSO</c>) and avatar gear (<c>AvatarGearSO</c>) the game ships, with where each can be
    /// had (the Trader, battle drops, boss rewards). Plain serializable classes with public fields,
    /// read by <c>JsonUtility</c> (the Editor importer) and <c>System.Text.Json</c> (the balance
    /// simulator) alike; JSON keys are the field names. <see cref="GearLibraryValidator"/> checks it
    /// (ids, slots, stats and the power budget); <see cref="GearLibrary.Build"/> indexes it.
    /// </summary>
    [Serializable]
    public class GearLibraryData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Items/gear-library.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>Beast gear (<c>Slot</c> is a <c>GearSlot</c> name).</summary>
        public GearItemData[] BeastGear = new GearItemData[0];

        /// <summary>Avatar gear (<c>Slot</c> is an <c>AvatarGearSlot</c> name).</summary>
        public GearItemData[] AvatarGear = new GearItemData[0];
    }

    /// <summary>One piece of gear.</summary>
    [Serializable]
    public class GearItemData
    {
        /// <summary>Stable id (lowercase snake_case, unique across beast and avatar gear); never renamed after ship.</summary>
        public string GearId;

        public string DisplayName;

        public string Description;

        /// <summary>A <c>GearSlot</c> name (beast gear) or an <c>AvatarGearSlot</c> name (avatar gear).</summary>
        public string Slot;

        /// <summary>0 common, 1 rare, 2 epic.</summary>
        public int Rarity;

        /// <summary>The lowest level (the beast's, or the avatar's) that may equip it: its band's floor (1, 21, 41, 61, 81).</summary>
        public int MinimumLevel = 1;

        /// <summary>Its family, for sets and the simulator's gear profiles: <c>Striker</c>, <c>Bulwark</c> or <c>Swift</c>.</summary>
        public string Archetype;

        /// <summary>The stat changes it grants (flat, percent, or both).</summary>
        public GearModifierData[] Modifiers = new GearModifierData[0];

        /// <summary>Where it can be had: any of <c>shop</c>, <c>drop</c>, <c>boss</c>.</summary>
        public string[] Sources = new string[0];
    }

    /// <summary>One stat change: <c>Flat</c> added, then <c>Pct</c> (0.05 = +5%) as <c>StatModifier</c>.</summary>
    [Serializable]
    public class GearModifierData
    {
        /// <summary>A <c>StatType</c> name; <c>MoveRange</c> is not allowed.</summary>
        public string Stat;

        public int Flat;

        public float Pct;
    }
}
