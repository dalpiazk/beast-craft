using System;

namespace BeastCraft.Expeditions
{
    /// <summary>
    /// The Board's content (<c>content/data/Grove/expedition-library.json</c>): destinations, their
    /// outcome tables (a seeded roll plus pity, the same idiom as the Grove's gifts) and the lore
    /// stories an outcome can unlock. Plain data with public fields and arrays (no dictionaries);
    /// validated by <see cref="ExpeditionLibraryValidator"/>, looked up through
    /// <see cref="ExpeditionLibrary"/>.
    /// <para>
    /// No combat power anywhere in this file: nothing here may be read by battle, stats or campaign
    /// difficulty. Sending a beast never locks it — see <see cref="ExpeditionRules"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class ExpeditionLibraryData
    {
        public const string ProjectRelativePath = "content/data/Grove/expedition-library.json";

        public int SchemaVersion = 1;

        public DestinationData[] Destinations = new DestinationData[0];

        public ExpeditionOutcomeTableData[] OutcomeTables = new ExpeditionOutcomeTableData[0];

        public LoreStoryData[] Stories = new LoreStoryData[0];
    }

    /// <summary>A destination the Board can send a party to.</summary>
    [Serializable]
    public class DestinationData
    {
        public string DestinationId;

        public string DisplayName;

        /// <summary><c>"start"</c> (unlocked from the first save) or <c>"habitat"</c> (see <see cref="UnlockId"/>).</summary>
        public string UnlockSource;

        /// <summary>For <c>"habitat"</c>: the <c>Grove.HabitatData.HabitatId</c> that must be unlocked. Otherwise "".</summary>
        public string UnlockId = string.Empty;

        /// <summary>1 to 12: hours away, checked through <c>OfflineClock</c>.</summary>
        public int DurationHours;

        /// <summary>1 to 3: how many beasts a send commits (they stay available for battle the whole time).</summary>
        public int PartySize;

        public string ArtKey;
    }

    /// <summary>One destination's outcome table: a seeded roll among <see cref="Entries"/> plus pity.</summary>
    [Serializable]
    public class ExpeditionOutcomeTableData
    {
        public string DestinationId;

        public ExpeditionOutcomeEntryData[] Entries = new ExpeditionOutcomeEntryData[0];

        /// <summary>Consecutive common outcomes before the next is forced non-common.</summary>
        public int PityAt = 5;
    }

    /// <summary>One outcome table entry.</summary>
    [Serializable]
    public class ExpeditionOutcomeEntryData
    {
        /// <summary><c>"story"</c> (a <see cref="LoreStoryData.StoryId"/>), <c>"trinket"</c> (a Grove item id) or <c>"look"</c> (a cosmetic key; not authored in v1).</summary>
        public string Kind;

        public string Id;

        public int Weight;

        /// <summary>Whether this entry counts as "common" for pity purposes.</summary>
        public bool IsCommon = true;
    }

    /// <summary>A lore story an expedition outcome can unlock (DRAFT text, the content bible's tone).</summary>
    [Serializable]
    public class LoreStoryData
    {
        public string StoryId;

        public string Text;

        /// <summary>A grouping label for a future codex screen (DRAFT), e.g. "Wilds" or "Old Roads".</summary>
        public string CodexCategory;
    }
}
