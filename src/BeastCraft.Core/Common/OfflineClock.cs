using System;

namespace BeastCraft.Common
{
    /// <summary>
    /// The one wall-clock-plus-monotonic reconciliation every offline timer in the game shares:
    /// idle (AFK) rewards (<c>IdleRewardCalculator.Elapsed</c>, its original home), and the Grove's
    /// gifts, the Garden's plot growth and the Board's expeditions. There is no trusted server clock
    /// (the game is offline-first): a caller compares the wall clock (UTC) and a monotonic clock
    /// (time since boot, which the player cannot set) with their readings at some anchor (a claim, a
    /// feed, a planting, a send) and gets back the real elapsed time, never more than the monotonic
    /// clock says really passed.
    /// <para>
    /// <strong>Anti-tamper stance (one behavior everywhere):</strong> a clock moved back or forward is
    /// silently clamped to the provable elapsed time — no message, no penalty, no bonus. See
    /// <see cref="ElapsedMs"/> for the exact reconciliation.
    /// </para>
    /// </summary>
    public static class OfflineClock
    {
        /// <summary>A wall clock ahead of the monotonic clock by more than this (ms) counts as a clamp (NTP corrections stay under it).</summary>
        public const long ClockSlackMs = 120000;

        /// <summary>
        /// The real elapsed time since an anchor at (<paramref name="lastUtcTicks"/>,
        /// <paramref name="lastMonoMs"/>), now at (<paramref name="nowUtcTicks"/>,
        /// <paramref name="nowMonoMs"/>): both in UTC ticks and milliseconds of a monotonic clock
        /// (−1 = not available). <paramref name="clamped"/> is set when the wall clock could not be
        /// trusted (moved back, or ran far enough ahead that it was replaced by the monotonic reading):
        /// <list type="bullet">
        /// <item>Both clocks from the same boot (the monotonic reading has not gone back): the wall-clock
        /// time, but never more than the monotonic time and, when the wall clock went back, the
        /// monotonic time.</item>
        /// <item>The device rebooted in between (the monotonic reading went back): at least the time
        /// since boot really passed, so the wall-clock time, or the time since boot when that is more.</item>
        /// <item>No monotonic reading (now or at the anchor): the wall-clock time, or 0 when it went
        /// back.</item>
        /// </list>
        /// Never negative. An anchor of 0 (never started) reads as 0 elapsed, unclamped — it is the
        /// caller's job to know whether its own clock has started.
        /// </summary>
        public static long ElapsedMs(long lastUtcTicks, long lastMonoMs, long nowUtcTicks, long nowMonoMs, out bool clamped)
        {
            clamped = false;
            if (lastUtcTicks <= 0)
            {
                return 0;
            }

            long deltaUtcMs = (nowUtcTicks - lastUtcTicks) / TimeSpan.TicksPerMillisecond;
            if (nowMonoMs < 0 || lastMonoMs < 0)
            {
                clamped = deltaUtcMs < 0;
                return Math.Max(0, deltaUtcMs);
            }

            long deltaMonoMs = nowMonoMs - lastMonoMs;
            if (deltaMonoMs >= 0)
            {
                if (deltaUtcMs < 0)
                {
                    clamped = true;
                    return deltaMonoMs;
                }

                clamped = deltaUtcMs > deltaMonoMs + ClockSlackMs;
                return Math.Min(deltaUtcMs, deltaMonoMs);
            }

            if (deltaUtcMs < nowMonoMs)
            {
                clamped = true;
                return nowMonoMs;
            }

            return deltaUtcMs;
        }

        /// <summary>
        /// <paramref name="now"/> as UTC ticks (a local time is converted, an unspecified one read as
        /// UTC), at least 1 (0 and negative are reserved for "never started" by every caller here).
        /// </summary>
        public static long UtcTicks(DateTime now)
        {
            DateTime utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now;
            return Math.Max(1, utc.Ticks);
        }

        /// <summary><paramref name="monotonic"/> as milliseconds, or −1 when it is negative (not available).</summary>
        public static long MonotonicMs(TimeSpan monotonic)
        {
            return monotonic < TimeSpan.Zero ? -1 : (long)monotonic.TotalMilliseconds;
        }
    }
}
