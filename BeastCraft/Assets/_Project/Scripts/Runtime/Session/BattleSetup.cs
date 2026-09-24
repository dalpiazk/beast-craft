using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Save;

namespace BeastCraft.Session
{
    /// <summary>
    /// Everything <see cref="BattleSession.Run"/> needs for one battle: the player's
    /// <see cref="Save"/> (the chosen beasts, the avatar's level and skill books), the chosen team,
    /// the avatar's stat profile and gear, the <see cref="Content"/> to resolve ids with, the
    /// <see cref="Encounter"/>, and the <see cref="Seed"/>. Plain input; the session never changes it
    /// (it reads the save, it does not write it — rewards are <see cref="BattleSession.ApplyRewards"/>).
    /// </summary>
    public class BattleSetup
    {
        /// <summary>The player's save. Required.</summary>
        public PlayerSave Save;

        /// <summary>
        /// <see cref="OwnedBeast.BeastId"/>s of the team, in deployment order (the first takes the
        /// front-most tile). 1 to <see cref="BattleFormat.LargeGroup"/>'s party size, no repeats.
        /// </summary>
        public List<string> TeamBeastIds = new List<string>();

        /// <summary>Whether the avatar takes part (built from the save's avatar progress and skill books).</summary>
        public bool IncludeAvatar = true;

        /// <summary>The avatar's stat profile. Null gives the no-stats avatar (Speed only).</summary>
        public AvatarStatsSO AvatarProfile;

        /// <summary>The avatar's equipped stat gear. May be null.</summary>
        public List<AvatarGearSO> AvatarGear;

        /// <summary>The content ids resolve against. Required.</summary>
        public BattleContent Content;

        /// <summary>The opposition and the board. Required.</summary>
        public EncounterSetup Encounter;

        /// <summary>Seeds the battle's only <see cref="System.Random"/>: same setup and seed, same battle.</summary>
        public int Seed;

        /// <summary>The battle's time cap (see <see cref="BattleTurnExecutor.DefaultMaxTime"/>).</summary>
        public int MaxTime = BattleTurnExecutor.DefaultMaxTime;
    }
}
