using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Everything a finished battle pays out, in one place: practice XP to every skill that fired
    /// (the player's beasts and the avatar) and, on a clear, the material drops. See the
    /// battle-system design doc, "Material economy".
    /// <para>
    /// Practice is credited on any finished battle (a skill that fired was practised, win or lose);
    /// drops are rolled only on <see cref="BattleOutcome.PlayerVictory"/>. Each beast's counts come
    /// from <see cref="BattleSkillUsage.CountFiredSkills"/> under its <see cref="BattleUnit.Id"/>, the
    /// avatar's from <see cref="BattleSkillUsage.CountAvatarActiveUses"/> and
    /// <see cref="BattleSkillUsage.CountPassiveTriggers"/>. Only the books passed in change; a null
    /// argument skips its part. Non-throwing.
    /// </para>
    /// </summary>
    public static class PostBattleAward
    {
        /// <summary>
        /// Credits practice XP: each <paramref name="beastBooksByUnitId"/> book gets its unit's fired
        /// skills (<see cref="BeastSkillBook.AwardPractice"/>), and <paramref name="avatarBook"/> its
        /// actives' fires and passives' triggers (<see cref="AvatarSkillBook.AwardPractice"/>).
        /// Returns the total skill levels gained.
        /// </summary>
        public static int AwardPractice(BattleResult result, IReadOnlyDictionary<string, BeastSkillBook> beastBooksByUnitId, Func<string, SkillSO> skillLookup,
                                        AvatarSkillBook avatarBook = null, Func<string, SkillSO> activeLookup = null,
                                        Func<string, PassiveSkillSO> passiveLookup = null)
        {
            if (result == null)
            {
                return 0;
            }

            int gained = 0;
            if (beastBooksByUnitId != null)
            {
                Dictionary<string, Dictionary<string, int>> fired = BattleSkillUsage.CountFiredSkills(result);
                foreach (KeyValuePair<string, BeastSkillBook> entry in beastBooksByUnitId)
                {
                    if (entry.Value != null && entry.Key != null && fired.TryGetValue(entry.Key, out Dictionary<string, int> uses))
                    {
                        gained += entry.Value.AwardPractice(uses, skillLookup);
                    }
                }
            }

            if (avatarBook != null)
            {
                gained += avatarBook.AwardPractice(BattleSkillUsage.CountAvatarActiveUses(result), activeLookup, BattleSkillUsage.CountPassiveTriggers(result),
                                                   passiveLookup);
            }

            return gained;
        }

        /// <summary>
        /// Rolls the drops for a clear of (<paramref name="shape"/>, <paramref name="level"/>) with
        /// <see cref="LootRoller.RollClear"/> into <paramref name="inventory"/> — only when
        /// <paramref name="result"/> is a <see cref="BattleOutcome.PlayerVictory"/>. Returns the
        /// loot (empty otherwise, never null).
        /// </summary>
        public static LootResult AwardDrops(BattleResult result, DropTable table, string shape, int level, MaterialInventory inventory, Random rng)
        {
            if (result == null || result.Outcome != BattleOutcome.PlayerVictory)
            {
                return new LootResult();
            }

            return LootRoller.RollClear(table, shape, level, inventory, rng);
        }
    }
}
