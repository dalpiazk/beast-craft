using System;
using BeastCraft.Progression;

namespace BeastCraft.Save
{
    /// <summary>
    /// One beast in the player's collection, as save data: a stable per-beast id (two beasts of the
    /// same species are different beasts), its <see cref="BeastProgress"/> (species, level, XP) and
    /// its <see cref="BeastSkillBook"/>. Plain serializable data with public fields; see
    /// <see cref="PlayerSave"/>.
    /// </summary>
    [Serializable]
    public class OwnedBeast
    {
        /// <summary>
        /// Unique within one <see cref="PlayerSave"/>; how teams and other save data refer to this
        /// beast. Assigned once when the beast is obtained and never changed.
        /// </summary>
        public string BeastId;

        /// <summary>Species, level and XP.</summary>
        public BeastProgress Progress = new BeastProgress();

        /// <summary>Learned skills, their progress, and the equipped slots.</summary>
        public BeastSkillBook Skills = new BeastSkillBook();

        /// <summary>A new beast: <paramref name="beastId"/> of <paramref name="speciesId"/> at <paramref name="level"/>, knowing nothing yet.</summary>
        public static OwnedBeast Create(string beastId, string speciesId, int level)
        {
            return new OwnedBeast
            {
                BeastId = beastId,
                Progress = new BeastProgress(speciesId, level)
            };
        }
    }
}
