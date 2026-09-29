using System;

namespace BeastCraft.Grove
{
    /// <summary>
    /// The Grove's content (<c>content/data/Grove/grove-library.json</c>): habitats and decor, each
    /// species' affinity tiers and the gift tables, and the Grove's own small lore codex. Plain data
    /// with public fields and arrays (no dictionaries), like every content file; validated by
    /// <see cref="GroveLibraryValidator"/>, looked up through <see cref="GroveLibrary"/>.
    /// <para>
    /// No combat power anywhere in this file: nothing here may be read by battle, stats or campaign
    /// difficulty. See <c>docs/design/grove.md</c>.
    /// </para>
    /// </summary>
    [Serializable]
    public class GroveLibraryData
    {
        public const string ProjectRelativePath = "content/data/Grove/grove-library.json";

        public int SchemaVersion = 1;

        /// <summary>Flat affinity XP a Feed action grants, any species, no cooldown scaling.</summary>
        public int FeedXp = 10;

        /// <summary>Flat affinity XP a Play action grants.</summary>
        public int PlayXp = 15;

        /// <summary>Hours between one daily Feed (or Play) and the next.</summary>
        public int DailyCooldownHours = 20;

        /// <summary>
        /// The gift interval in hours by tier (index 0 = tier 1 .. index 4 = tier 5): narrows as
        /// affinity grows. Exactly 5 entries.
        /// </summary>
        public int[] GiftHoursByTier = { 6, 5, 4, 3, 2 };

        /// <summary>Habitats: where beasts live and decor is placed.</summary>
        public HabitatData[] Habitats = new HabitatData[0];

        /// <summary>Decor: placeable pieces, from various sources.</summary>
        public DecorData[] Decor = new DecorData[0];

        /// <summary>Every species' 5 affinity tiers (flat rows, one per (species, tier)).</summary>
        public AffinityTierData[] AffinityTiers = new AffinityTierData[0];

        /// <summary>Gift tables a beast's species draws its gifts from (see <see cref="GroveLibrary.GiftTableFor"/>).</summary>
        public GiftTableData[] GiftTables = new GiftTableData[0];

        /// <summary>The Grove's own lore entries (an affinity or gift reward), separate from the discovery layer's.</summary>
        public GroveLoreEntryData[] Lore = new GroveLoreEntryData[0];
    }

    /// <summary>A habitat: a home for beasts, with its own decor slots.</summary>
    [Serializable]
    public class HabitatData
    {
        public string HabitatId;

        public string DisplayName;

        /// <summary><c>"shrine"</c> (see <see cref="UnlockId"/>) or <c>"start"</c> (unlocked from the first save).</summary>
        public string UnlockSource;

        /// <summary>For <c>"shrine"</c>: the <c>ShrineData.GroveUnlockId</c> that grants it. Otherwise "".</summary>
        public string UnlockId = string.Empty;

        /// <summary>How many decor pieces this habitat can hold at once.</summary>
        public int SlotCount = 12;

        public string ArtKey;
    }

    /// <summary>A placeable decor piece.</summary>
    [Serializable]
    public class DecorData
    {
        public string DecorId;

        public string DisplayName;

        /// <summary>A specific <c>HabitatId</c> it may be placed in, or "" for any habitat.</summary>
        public string HabitatScope = string.Empty;

        /// <summary><c>"shrine"</c>, <c>"affinity"</c>, <c>"garden_craft"</c>, <c>"expedition"</c> or <c>"starter"</c>.</summary>
        public string Source;

        /// <summary>
        /// What grants it: a shrine's <c>GroveUnlockId</c> (<c>"shrine"</c>), an
        /// <c>"affinity_{speciesId}_{tier}"</c> reward id (<c>"affinity"</c>), a <c>RecipeData.RecipeId</c>
        /// (<c>"garden_craft"</c>), or "" (<c>"starter"</c>, <c>"expedition"</c> — not yet authored in v1).
        /// </summary>
        public string UnlockId = string.Empty;

        public string ArtKey;

        public int SortOrder;
    }

    /// <summary>One (species, tier) row of the affinity ladder.</summary>
    [Serializable]
    public class AffinityTierData
    {
        public string SpeciesId;

        /// <summary>1 to 5.</summary>
        public int Tier;

        /// <summary>Cumulative affinity XP needed to reach this tier.</summary>
        public int XpThreshold;

        /// <summary><c>"lore"</c>, <c>"decor"</c>, <c>"look"</c>, <c>"idle_anim"</c> or <c>"none"</c>. <c>"look"</c> is not authored in v1 (see the Grove design doc).</summary>
        public string RewardKind = "none";

        /// <summary>
        /// The reward's id: a <see cref="GroveLoreEntryData.LoreId"/> (<c>"lore"</c>), a
        /// <see cref="DecorData.DecorId"/> (<c>"decor"</c>), a cosmetic key (<c>"look"</c>), or "" (<c>"idle_anim"</c>,
        /// <c>"none"</c> — an idle-anim reward is derived from reaching tier 5, nothing to grant here).
        /// </summary>
        public string RewardId = string.Empty;
    }

    /// <summary>
    /// The gift table a beast's species draws from: a seeded roll among <see cref="Entries"/> plus a
    /// pity counter (<see cref="PityAt"/> consecutive common entries forces a non-common one next).
    /// </summary>
    [Serializable]
    public class GiftTableData
    {
        /// <summary>A <c>SpeciesId</c>, or <c>"default"</c> (every species without its own table falls back to it).</summary>
        public string SpeciesId;

        public GiftEntryData[] Entries = new GiftEntryData[0];

        /// <summary>Consecutive common gifts before the next is forced non-common.</summary>
        public int PityAt = 5;
    }

    /// <summary>One gift table entry.</summary>
    [Serializable]
    public class GiftEntryData
    {
        /// <summary><c>"decor"</c>, <c>"lore"</c> or <c>"cosmetic"</c>. <c>"cosmetic"</c> is not authored in v1.</summary>
        public string ItemKind;

        /// <summary>A <see cref="DecorData.DecorId"/>, <see cref="GroveLoreEntryData.LoreId"/> or cosmetic key, matching <see cref="ItemKind"/>.</summary>
        public string ItemId;

        public int Weight;

        /// <summary>Whether this entry counts as "common" for pity purposes.</summary>
        public bool IsCommon = true;
    }

    /// <summary>The Grove's own lore entry (DRAFT text, the content bible's tone): affinity and gift flavour, not a discovery find.</summary>
    [Serializable]
    public class GroveLoreEntryData
    {
        public string LoreId;

        public string Title;

        public string Text;
    }
}
