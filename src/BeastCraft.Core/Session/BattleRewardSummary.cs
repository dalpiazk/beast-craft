using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Progression;

namespace BeastCraft.Session
{
    /// <summary>What <see cref="BattleSession.ApplyRewards"/> paid out (or why it paid nothing).</summary>
    public class BattleRewardSummary
    {
        internal BattleRewardSummary()
        {
        }

        /// <summary>Whether the rewards were applied to the save.</summary>
        public bool Applied { get; internal set; }

        /// <summary>Why nothing was applied; null when applied.</summary>
        public string Error { get; internal set; }

        /// <summary>The battle's outcome.</summary>
        public BattleOutcome Outcome { get; internal set; }

        /// <summary>Skill levels gained across the team's beasts and the avatar's actives and passives.</summary>
        public int SkillLevelsGained { get; internal set; }

        /// <summary>
        /// Beast XP credited after the level-gap falloff, by team beast id (every team beast still in
        /// the save has an entry). Includes any part of it that went to the bank at the level cap.
        /// </summary>
        public Dictionary<string, int> BeastXpGained { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Beast levels gained across the team.</summary>
        public int BeastLevelsGained { get; internal set; }

        /// <summary>
        /// Bench XP credited after the level-gap falloff (<c>BeastProgression.BenchXp</c>), by beast id:
        /// every beast in the save that was not on the team has an entry.
        /// </summary>
        public Dictionary<string, int> BenchXpGained { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Levels gained across the benched beasts.</summary>
        public int BenchLevelsGained { get; internal set; }

        /// <summary>
        /// By beast id (team and bench), how much this battle added to the beast's level-cap bank
        /// (<c>BeastProgress.BankedXp</c>); only beasts whose bank grew have an entry.
        /// </summary>
        public Dictionary<string, int> XpBanked { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// By beast id (team and bench), the percent of its battle XP the level-gap falloff let
        /// through (<c>LevelGapXp.Percent</c> on its level before the award): 100 unless it
        /// out-levelled the encounter.
        /// </summary>
        public Dictionary<string, int> FalloffPercent { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The beast level cap the XP was added under (<c>BeastProgression.MaxLevel</c> = no cap).</summary>
        public int BeastLevelCap { get; internal set; }

        /// <summary>Avatar XP credited after the level-gap falloff (0 when the avatar did not take part).</summary>
        public int AvatarXpGained { get; internal set; }

        /// <summary>The percent of its battle XP the level-gap falloff let through to the avatar (100 when it did not take part).</summary>
        public int AvatarFalloffPercent { get; internal set; } = 100;

        /// <summary>Avatar levels gained.</summary>
        public int AvatarLevelsGained { get; internal set; }

        /// <summary>The materials granted (empty unless the battle was a player victory). Never null.</summary>
        public LootResult Loot { get; internal set; } = new LootResult();

        /// <summary>Gold added to the wallet (0 unless the battle was a player victory and the drop table pays gold).</summary>
        public int GoldGained { get; internal set; }

        /// <summary>The gear ids dropped (each now a new instance in the save), in roll order. Never null.</summary>
        public List<string> GearGained { get; } = new List<string>();

        /// <summary>
        /// The consumables the battle spent, whatever its outcome — taken from the pack by
        /// <see cref="BattleSession.Run"/> as the battle began, not by the reward pass. Never null.
        /// </summary>
        public List<string> ConsumablesSpent { get; } = new List<string>();

        /// <summary>Cosmetic looks newly unlocked (a battle drop, then milestones the battle reached), as <c>"categoryId/optionId"</c>. Never null.</summary>
        public List<string> CosmeticsUnlocked { get; } = new List<string>();
    }
}
