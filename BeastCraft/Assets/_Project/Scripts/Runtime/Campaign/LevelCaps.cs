using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The beast level cap from the seals the player owns: the highest owned seal's
    /// <see cref="SealData.LevelCap"/>, or <see cref="RegionLibraryData.StartingLevelCap"/> when that
    /// is higher (or nothing is owned), clamped to 1-100. What the campaign passes to
    /// <c>BattleSession.ApplyRewards</c> and <see cref="LevelCap.Release"/>. The avatar has no cap.
    /// </summary>
    public static class LevelCaps
    {
        /// <summary>The cap for <paramref name="ownedSeals"/> (unknown ids ignored). No library means no cap (<see cref="BeastProgression.MaxLevel"/>).</summary>
        public static int BeastCap(IEnumerable<string> ownedSeals, RegionLibrary library)
        {
            if (library == null)
            {
                return BeastProgression.MaxLevel;
            }

            int cap = library.StartingLevelCap;
            foreach (string sealId in ownedSeals ?? new string[0])
            {
                SealData seal = library.GetSeal(sealId);
                if (seal != null && seal.LevelCap > cap)
                {
                    cap = seal.LevelCap;
                }
            }

            return cap < 1 ? 1 : cap > BeastProgression.MaxLevel ? BeastProgression.MaxLevel : cap;
        }
    }
}
