using System;

namespace BeastCraft.Idle
{
    /// <summary>
    /// The idle (AFK) reward clock, as save data (<c>PlayerSave.Idle</c>, schema 5): when the rewards
    /// were last claimed, by the wall clock and by a monotonic clock, the seed every claim's rolls
    /// derive from and how many claims have been made. The rules are
    /// <see cref="IdleRewardCalculator"/>'s; this type only holds the state.
    /// <para>
    /// Plain serializable data with public fields, like the rest of the save. Times are integers
    /// (<c>JsonUtility</c> has no <c>DateTime</c>, and a double would lose precision): the wall clock
    /// as <see cref="DateTime.Ticks"/> of a UTC time, the monotonic clock in milliseconds. A
    /// <see cref="LastClaimUtcTicks"/> of 0 means the clock has not started (a new or migrated save);
    /// the first claim starts it and pays nothing.
    /// </para>
    /// </summary>
    [Serializable]
    public class IdleState
    {
        /// <summary>The UTC wall-clock time of the last claim, in ticks; 0 = the clock has not started.</summary>
        public long LastClaimUtcTicks;

        /// <summary>
        /// The monotonic clock (time since the device booted; it resets on a reboot) at the last claim,
        /// in milliseconds. Only compared with a later reading of the same clock.
        /// </summary>
        public long LastClaimMonotonicMs;

        /// <summary>The base seed of every claim's rolls (<c>LootRoller.DeriveSeed(IdleSeed, ClaimIndex)</c>); 0 = not yet drawn.</summary>
        public int IdleSeed;

        /// <summary>How many claims have been paid (the first, which only starts the clock, is not counted).</summary>
        public int ClaimIndex;

        /// <summary>
        /// How many claims had their wall-clock time replaced by the monotonic clock's (a clock set
        /// back, or forward past what really elapsed). A silent diagnostic: nothing is shown to the
        /// player and nothing is taken away (lead / user decision).
        /// </summary>
        public int ClockClamps;

        /// <summary>Whether the clock has started (a first claim has been made).</summary>
        public bool HasStarted
        {
            get { return LastClaimUtcTicks > 0; }
        }
    }
}
