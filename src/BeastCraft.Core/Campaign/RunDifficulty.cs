namespace BeastCraft.Campaign
{
    /// <summary>
    /// How hard an expedition is, chosen when it starts (<see cref="CampaignRules.StartRun(BeastCraft.Save.PlayerSave, RegionLibrary, string, int, int, RunDifficulty)"/>)
    /// and stored on it (<see cref="MapRun.Difficulty"/>, save schema 6; written as its number).
    /// Only a post-game region (<see cref="RegionData.IsPostGame"/>) may be played on
    /// <see cref="Hard"/>; every other expedition is <see cref="Normal"/>.
    /// </summary>
    public enum RunDifficulty
    {
        /// <summary>The default: the region's own shapes and boss (every save before schema 6 reads this).</summary>
        Normal = 0,

        /// <summary>
        /// A post-game region's <see cref="RegionData.HardMode"/>: harder shapes and a harder boss
        /// template. The same loot as Normal; a Hard boss clear also unlocks the region's Hard-only
        /// looks (<c>CosmeticLibrary.SourceBossHard</c>).
        /// </summary>
        Hard = 1
    }
}
