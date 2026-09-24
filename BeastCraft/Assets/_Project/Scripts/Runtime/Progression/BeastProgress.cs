using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// Where one owned beast's own level has got to: which species it is, its level, and the XP
    /// banked toward the next. Its skills progress separately (<see cref="BeastSkillBook"/>).
    /// <para>
    /// Plain serializable save data with public fields, like <see cref="AvatarProgress"/> and
    /// <see cref="SkillProgress"/>. The species is named by its stable
    /// <c>CreatureSpeciesSO.SpeciesId</c>, never an asset reference. No beast XP curve exists yet,
    /// so this type only holds the numbers; the rules will live beside it when one is designed.
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
    }
}
