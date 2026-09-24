using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Where one owned beast's own level has got to: which species it is, its level, the XP
    /// toward the next, and any XP banked while at the level cap. Its skills progress separately (<see cref="BeastSkillBook"/>).
    /// <para>
    /// Plain serializable save data with public fields, like <see cref="AvatarProgress"/> and
    /// <see cref="SkillProgress"/>. The species is named by its stable
    /// <c>CreatureSpeciesSO.SpeciesId</c>, never an asset reference. The rules are
    /// <see cref="BeastProgression"/>'s; this type only holds the numbers.
    /// </para>
    /// </summary>
    [Serializable]
    public class BeastProgress
    {
        /// <summary>Parameterless, for serializers. Level 1, no XP, no species.</summary>
        public BeastProgress()
        {
        }

        /// <summary>A freshly obtained beast of <paramref name="speciesId"/> at <paramref name="level"/> (below 1 reads as 1), no XP.</summary>
        public BeastProgress(string speciesId, int level)
        {
            SpeciesId = speciesId;
            Level = level < 1 ? 1 : level;
        }

        /// <summary>The <c>CreatureSpeciesSO.SpeciesId</c> this beast is. Follows the id's never-rename rule.</summary>
        public string SpeciesId;

        /// <summary>Current level, 1-based.</summary>
        public int Level = 1;

        /// <summary>XP banked toward the next level.</summary>
        public int Xp;

        /// <summary>
        /// XP earned while held at the beast level cap, beyond what <see cref="Xp"/> holds: spent as
        /// levels when the cap rises (<see cref="LevelCap.Release"/>), and itself capped at the XP of
        /// <see cref="LevelCap.BankLevelLimit"/> levels. 0 below the cap. Added in save schema 3.
        /// </summary>
        public int BankedXp;
    }
}
