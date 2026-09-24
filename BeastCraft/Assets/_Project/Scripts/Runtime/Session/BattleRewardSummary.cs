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

        /// <summary>Beast XP credited, by team beast id (every team beast still in the save has an entry).</summary>
        public Dictionary<string, int> BeastXpGained { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Beast levels gained across the team.</summary>
        public int BeastLevelsGained { get; internal set; }

        /// <summary>Avatar XP credited (0 when the avatar did not take part).</summary>
        public int AvatarXpGained { get; internal set; }

        /// <summary>Avatar levels gained.</summary>
        public int AvatarLevelsGained { get; internal set; }

        /// <summary>The materials granted (empty unless the battle was a player victory). Never null.</summary>
        public LootResult Loot { get; internal set; } = new LootResult();
    }
}
