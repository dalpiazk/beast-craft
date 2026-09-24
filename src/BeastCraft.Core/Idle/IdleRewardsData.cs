using System;

namespace BeastCraft.Idle
{
    /// <summary>
    /// The authored idle (AFK) reward rates, <c>Data/Idle/idle-rewards.json</c>: the accumulation cap,
    /// the drop-table shape the materials and the look roll come from, and per band of the player's
    /// progress level (<c>CampaignRules.ProgressLevel</c>: the level of the highest cleared map
    /// location) what an idle hour pays. Validated by <see cref="IdleRewardsValidator"/>, built by
    /// <see cref="IdleRewardsBuilder"/>, imported by the Editor into <see cref="IdleRewardsSO"/>.
    /// Tuned with the balance simulator's <c>--mode campaign</c> idle model; see
    /// <c>docs/design/progression-and-saves.md</c>, "Idle rewards".
    /// </summary>
    [Serializable]
    public class IdleRewardsData
    {
        /// <summary>Where the authored file lives, relative to the Unity project folder.</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Idle/idle-rewards.json";

        /// <summary>The schema this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The largest cap the validator accepts (hours).</summary>
        public const int MaxCapHours = 24;

        /// <summary>The largest look chance per full claim the validator accepts, in 1/10,000 (1%).</summary>
        public const int MaxCosmeticChancePer10k = 100;

        /// <summary>The schema the file is written in.</summary>
        public int SchemaVersion;

        /// <summary>
        /// Idle time accumulates for at most this many hours between claims (lead / user decision: 8);
        /// time beyond it is not banked. One constant, so a future extension (a pass, VIP) is data.
        /// </summary>
        public int CapHours = 8;

        /// <summary>
        /// The <c>drop-tables.json</c> shape whose cell the idle material rolls use (at the progress
        /// level, chances scaled by the band's <see cref="IdleBandData.MaterialChanceMultiplier"/>).
        /// </summary>
        public string Shape = "squad";

        /// <summary>Material rolls per idle hour (fractions are rounded stochastically, so short claims lose nothing on average).</summary>
        public float MaterialRollsPerHour = 1f;

        /// <summary>The rates, one band per range of progress levels, ascending and contiguous over 1-100.</summary>
        public IdleBandData[] Bands = new IdleBandData[0];
    }

    /// <summary>What an idle hour pays at progress levels <see cref="MinProgressLevel"/>-<see cref="MaxProgressLevel"/>.</summary>
    [Serializable]
    public class IdleBandData
    {
        /// <summary>The band's lowest progress level (inclusive).</summary>
        public int MinProgressLevel = 1;

        /// <summary>The band's highest progress level (inclusive).</summary>
        public int MaxProgressLevel = 10;

        /// <summary>
        /// XP per idle hour for each beast of the party, before the level-gap falloff (on the beast's
        /// level against the progress level) and the level cap; the bench gets the bench share of it.
        /// </summary>
        public int XpPerHour;

        /// <summary>Gold per idle hour (no first-clear bonus, no reward modifiers).</summary>
        public int GoldPerHour;

        /// <summary>Scales every chance of the idle material cell (0-1; 0.25 = a quarter of a clear's chance).</summary>
        public float MaterialChanceMultiplier;

        /// <summary>The chance of one look from the battle-drop pool per full-cap claim, in 1/10,000 (scaled by the hours claimed); at most 1%.</summary>
        public int CosmeticChancePer10k;
    }
}
