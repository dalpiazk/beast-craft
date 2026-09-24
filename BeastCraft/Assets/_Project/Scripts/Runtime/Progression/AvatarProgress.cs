using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Where the player avatar's own level has got to: its level and the XP banked toward the next.
    /// The avatar's skills progress separately (<see cref="AvatarSkillBook"/>); this is the avatar
    /// itself, whose level scales its stats (<c>AvatarStatsSO.GetStatsAtLevel</c>).
    /// <para>
    /// Plain serializable save data with public fields, like <see cref="SkillProgress"/>. The rules
    /// are <see cref="AvatarProgression"/>'s; this type only holds the numbers.
    /// </para>
    /// </summary>
    [Serializable]
    public class AvatarProgress
    {
        /// <summary>Current level, 1 to <see cref="AvatarProgression.MaxLevel"/>.</summary>
        public int Level = 1;

        /// <summary>XP banked toward the next level; always below its cost, and 0 at the max level.</summary>
        public int Xp;
    }
}
