using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Progression/achievements.json</c>: deterministic,
    /// collector-facing achievements (no RNG, no combat power). Each one is met from save state alone
    /// (a region's boss beaten, its discovery layer or lore complete, Kinship sites claimed, beasts
    /// owned, the compendium's own completion, an avatar or beast level) and awards a text title — a
    /// display-only name the player can own and equip (<see cref="AchievementProgress"/>), never a
    /// stat. Validated by <see cref="AchievementLibraryValidator"/>; looked up through
    /// <see cref="AchievementLibrary"/>; evaluated by <see cref="AchievementRules"/>.
    /// </summary>
    [Serializable]
    public class AchievementLibraryData
    {
        public const string ProjectRelativePath = "content/data/Progression/achievements.json";

        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = 1;

        public AchievementData[] Achievements = new AchievementData[0];
    }

    /// <summary>
    /// One achievement: a deterministic <see cref="Kind"/> (an <see cref="AchievementKinds"/> name)
    /// checked against save state, awarding <see cref="TitleId"/> / <see cref="TitleText"/> the first
    /// time it is met. <see cref="RegionId"/> and <see cref="Threshold"/> are used by some kinds and
    /// blank/zero for the rest (the validator checks which).
    /// </summary>
    [Serializable]
    public class AchievementData
    {
        public string AchievementId;

        /// <summary>The achievement's own name, shown in an achievement list (DRAFT).</summary>
        public string DisplayName;

        /// <summary>An <see cref="AchievementKinds"/> name.</summary>
        public string Kind;

        /// <summary>For a region-scoped kind (<c>BossCleared</c>, <c>RegionExplored</c>, <c>RegionLoreComplete</c>); "" otherwise.</summary>
        public string RegionId = string.Empty;

        /// <summary>For a threshold kind (<c>KinshipSitesClaimed</c>, <c>BeastsOwned</c>, <c>CompendiumPercent</c>, <c>AvatarLevel</c>, <c>BeastLevel</c>); 0 otherwise.</summary>
        public int Threshold;

        /// <summary>The title's stable id (an <c>OwnedTitleIds</c> entry).</summary>
        public string TitleId;

        /// <summary>The title's text, shown wherever an equipped title is shown (DRAFT).</summary>
        public string TitleText;
    }

    /// <summary>The achievement <see cref="AchievementData.Kind"/> names <see cref="AchievementLibraryValidator"/> and <see cref="AchievementRules"/> know.</summary>
    public static class AchievementKinds
    {
        /// <summary><see cref="RegionId"/>'s boss beaten (<c>RegionProgress.BossCleared</c>).</summary>
        public const string BossCleared = "BossCleared";

        /// <summary>Every mainline region's boss beaten.</summary>
        public const string AllBossesCleared = "AllBossesCleared";

        /// <summary><see cref="RegionId"/> explored to 100% (<c>DiscoveryRules.Completion</c>).</summary>
        public const string RegionExplored = "RegionExplored";

        /// <summary>Every discovery region explored to 100%.</summary>
        public const string AllRegionsExplored = "AllRegionsExplored";

        /// <summary>At least <see cref="Threshold"/> Kinship sites claimed.</summary>
        public const string KinshipSitesClaimed = "KinshipSitesClaimed";

        /// <summary>Every Kinship site claimed.</summary>
        public const string AllKinshipClaimed = "AllKinshipClaimed";

        /// <summary>At least <see cref="Threshold"/> distinct species owned.</summary>
        public const string BeastsOwned = "BeastsOwned";

        /// <summary>Every lore entry of <see cref="RegionId"/> found.</summary>
        public const string RegionLoreComplete = "RegionLoreComplete";

        /// <summary>Every lore entry in the game found.</summary>
        public const string AllLoreFound = "AllLoreFound";

        /// <summary>The compendium's own completion (<c>CompendiumRules.Completion</c>) at least <see cref="Threshold"/> percent.</summary>
        public const string CompendiumPercent = "CompendiumPercent";

        /// <summary>The avatar at least level <see cref="Threshold"/>.</summary>
        public const string AvatarLevel = "AvatarLevel";

        /// <summary>Any owned beast at least level <see cref="Threshold"/>.</summary>
        public const string BeastLevel = "BeastLevel";

        /// <summary>Every kind name, in validation order.</summary>
        public static readonly string[] All =
        {
            BossCleared, AllBossesCleared, RegionExplored, AllRegionsExplored, KinshipSitesClaimed, AllKinshipClaimed, BeastsOwned, RegionLoreComplete, AllLoreFound,
            CompendiumPercent, AvatarLevel, BeastLevel
        };

        /// <summary>Whether <paramref name="kind"/> is scoped to a region (needs <see cref="AchievementData.RegionId"/>).</summary>
        public static bool IsRegionScoped(string kind)
        {
            return kind == BossCleared || kind == RegionExplored || kind == RegionLoreComplete;
        }

        /// <summary>Whether <paramref name="kind"/> is a threshold kind (needs a positive <see cref="AchievementData.Threshold"/>).</summary>
        public static bool IsThreshold(string kind)
        {
            return kind == KinshipSitesClaimed || kind == BeastsOwned || kind == CompendiumPercent || kind == AvatarLevel || kind == BeastLevel;
        }
    }
}
