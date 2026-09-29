using System;
using BeastCraft.Common;
using BeastCraft.Idle;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="OfflineClock"/>: extracted from <see cref="IdleRewardCalculator.Elapsed"/> so every
    /// offline timer (idle, the Grove's gifts, the Wildgarden's growth, the Board's expeditions) shares
    /// one anti-tamper clock. <see cref="IdleRewardTests"/> already proves idle's own behaviour is
    /// byte-for-byte unchanged after the extraction (it exercises <c>IdleRewardCalculator.Claim</c>,
    /// which now calls this class); these tests exercise <see cref="OfflineClock.ElapsedMs"/> directly
    /// and prove <see cref="IdleRewardCalculator.Elapsed"/> delegates to it with identical results.
    /// </summary>
    public class OfflineClockTests
    {
        private const long HourMs = 3600000;

        [Test]
        public void NeverStarted_ReadsAsZero_Unclamped()
        {
            long elapsed = OfflineClock.ElapsedMs(0, 0, 100, 100, out bool clamped);

            Assert.AreEqual(0, elapsed);
            Assert.IsFalse(clamped);
        }

        [Test]
        public void BothClocksAgree_UsesWallTime_Unclamped()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + 5 * HourMs * TimeSpan.TicksPerMillisecond;

            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 1000 + 5 * HourMs, out bool clamped);

            Assert.AreEqual(5 * HourMs, elapsed);
            Assert.IsFalse(clamped);
        }

        [Test]
        public void WallClockSetForward_ClampsToMonotonicTime()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + 30 * HourMs * TimeSpan.TicksPerMillisecond; // wall clock jumped 30h forward

            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 1000 + 2 * HourMs, out bool clamped); // only 2h really passed

            Assert.AreEqual(2 * HourMs, elapsed);
            Assert.IsTrue(clamped);
        }

        [Test]
        public void WallClockSetBack_ClampsToMonotonicTime()
        {
            long last = new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last - HourMs * TimeSpan.TicksPerMillisecond; // wall clock went back an hour

            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 1000 + 3 * HourMs, out bool clamped); // 3h really passed (monotonic)

            Assert.AreEqual(3 * HourMs, elapsed);
            Assert.IsTrue(clamped);
        }

        [Test]
        public void SmallWallAheadOfMonotonic_WithinSlack_NotClamped()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + (HourMs + 60000) * TimeSpan.TicksPerMillisecond; // 1h + 60s wall

            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 1000 + HourMs, out bool clamped); // 1h monotonic

            Assert.AreEqual(HourMs, elapsed); // the smaller of the two
            Assert.IsFalse(clamped); // within ClockSlackMs (120000ms)
        }

        [Test]
        public void DeviceRebooted_MonotonicWentBack_AndWallDeltaIsSmallerThanTimeSinceBoot_UsesTimeSinceBoot()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + 100 * TimeSpan.TicksPerMillisecond; // barely any wall time passed

            // monotonic reading went back (reboot): now-mono (500) < last-mono (1000), but the device
            // has been up 500ms since booting — more than the 100ms the wall clock shows.
            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 500, out bool clamped);

            Assert.AreEqual(500, elapsed);
            Assert.IsTrue(clamped);
        }

        [Test]
        public void DeviceRebooted_MonotonicWentBack_ButWallDeltaIsAlreadyLarger_TrustsWallTime()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + 4 * HourMs * TimeSpan.TicksPerMillisecond;

            // monotonic reading went back (reboot), but 4h of wall time is already more than the 500ms
            // since boot proves, so the wall reading (which cannot be disproven) is trusted as-is.
            long elapsed = OfflineClock.ElapsedMs(last, 1000, now, 500, out bool clamped);

            Assert.AreEqual(4 * HourMs, elapsed);
            Assert.IsFalse(clamped);
        }

        [Test]
        public void NoMonotonicReading_UsesWallTimeAlone()
        {
            long last = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            long now = last + 2 * HourMs * TimeSpan.TicksPerMillisecond;

            long elapsed = OfflineClock.ElapsedMs(last, -1, now, -1, out bool clamped);

            Assert.AreEqual(2 * HourMs, elapsed);
            Assert.IsFalse(clamped);
        }

        [Test]
        public void IdleRewardCalculator_Elapsed_MatchesOfflineClock_ForTheSameReadings()
        {
            IdleState state = new IdleState { LastClaimUtcTicks = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc).Ticks, LastClaimMonotonicMs = 5000 };
            long nowTicks = state.LastClaimUtcTicks + 7 * HourMs * TimeSpan.TicksPerMillisecond;
            long nowMono = 5000 + 7 * HourMs;

            long viaIdle = IdleRewardCalculator.Elapsed(state, nowTicks, nowMono, out bool idleClamped);
            long viaOffline = OfflineClock.ElapsedMs(state.LastClaimUtcTicks, state.LastClaimMonotonicMs, nowTicks, nowMono, out bool offlineClamped);

            Assert.AreEqual(viaOffline, viaIdle);
            Assert.AreEqual(offlineClamped, idleClamped);
        }

        [Test]
        public void UtcTicks_ConvertsLocalToUtc_AndIsNeverBelowOne()
        {
            Assert.AreEqual(1, OfflineClock.UtcTicks(default));
            Assert.Greater(OfflineClock.UtcTicks(DateTime.UtcNow), 0);
        }

        [Test]
        public void MonotonicMs_NegativeSpanBecomesMinusOne()
        {
            Assert.AreEqual(-1, OfflineClock.MonotonicMs(TimeSpan.FromSeconds(-1)));
            Assert.AreEqual(0, OfflineClock.MonotonicMs(TimeSpan.Zero));
        }
    }
}
