using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="TurnManager"/>'s ATB gauge: units fill at <c>round(100 x sqrt(Speed))</c> per tick
    /// and act at <see cref="TurnManager.ActionThreshold"/> (100000), one at a time, overflow carried.
    /// <para>
    /// Every expected sequence here was worked by hand from the rules (fill rate =
    /// <c>round(100 x sqrt(max(1, Speed)))</c>; ticks needed = <c>ceil((100000 - gauge) / rate)</c>,
    /// time jumps by the minimum, full units act highest gauge first, then higher rate, then id).
    /// Rates used below: Speed 25 = 500, 28 = 529, 49 = 700, 100 = 1000, 111 = 1054, 225 = 1500,
    /// 400 = 2000. Units carry no skills unless a test says otherwise, so a turn changes nothing and a
    /// battle between them can only stop at the time cap.
    /// </para>
    /// </summary>
    public class TurnManagerTests
    {
        // ---------------------------------------------------------------------------------------
        // The fill rate.
        // ---------------------------------------------------------------------------------------

        [TestCase(1, 100)]
        [TestCase(2, 141)]
        [TestCase(25, 500)]
        [TestCase(49, 700)]
        [TestCase(50, 707)]
        [TestCase(92, 959)]
        [TestCase(100, 1000)]
        [TestCase(105, 1025)]
        [TestCase(150, 1225)]
        [TestCase(225, 1500)]
        [TestCase(400, 2000)]
        public void FillRate_IsRoundedHundredTimesSqrtSpeed(int speed, int expected)
        {
            Assert.AreEqual(expected, TurnManager.FillRateForSpeed(speed));
            Assert.AreEqual(expected, TurnManager.FillRate(Unit("u", speed)));
        }

        [Test]
        public void FillRate_MatchesTheRoundedSquareRoot_AndOnlyEverRises()
        {
            int previous = 0;
            for (int speed = 1; speed <= 2500; speed++)
            {
                int rate = TurnManager.FillRateForSpeed(speed);
                Assert.AreEqual((int)System.Math.Round(100.0 * System.Math.Sqrt(speed), System.MidpointRounding.AwayFromZero), rate, "speed " + speed);
                Assert.Greater(rate, previous, "speed " + speed);
                previous = rate;
            }
        }

        [Test]
        public void FillRate_HugeSpeed_DoesNotOverflow()
        {
            // sqrt(int.MaxValue) = 46340.95..., so the rate is 4634095.
            Assert.AreEqual(4634095, TurnManager.FillRateForSpeed(int.MaxValue));
        }

        [TestCase(0L, 0L)]
        [TestCase(1L, 1L)]
        [TestCase(3L, 1L)]
        [TestCase(4L, 2L)]
        [TestCase(99L, 9L)]
        [TestCase(100L, 10L)]
        [TestCase(-5L, 0L)]
        [TestCase(long.MaxValue, 3037000499L)]
        public void IntegerSqrt_IsTheFloorOfTheRoot(long value, long expected)
        {
            Assert.AreEqual(expected, TurnManager.IntegerSqrt(value));
        }

        [Test]
        public void TimeUnit_IsOneTurnOfASpeed100Unit()
        {
            Assert.AreEqual(1000, TurnManager.ReferenceFillRate);
            Assert.AreEqual(TurnManager.ReferenceFillRate, TurnManager.FillRateForSpeed(TurnManager.ReferenceSpeed));
            Assert.AreEqual(100, TurnManager.TicksPerTimeUnit);

            TurnManager turns = new TurnManager(new[] { Unit("a", 100) });
            Assert.AreEqual(TurnManager.TicksPerTimeUnit, turns.ElapsedTicks);
            Assert.AreEqual(1.0, turns.Time, 1e-9);
        }

        // ---------------------------------------------------------------------------------------
        // The action sequence.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void HandComputedSequence_Speeds100_225_49()
        {
            // Rates a 1000, b 1500, c 700.
            // t=67:  b fills first (100500) and carries 500. a 67000, c 46900.
            // t=100: a (100000). b 50000, c 70000.
            // t=134: b (101000, carries 1000). a 34000, c 93800.
            // t=143: c (100100, carries 100). a 43000, b 14500.
            // t=200: a and b both at exactly 100000 — equal gauges, so the faster goes first: b, a
            //        at zero elapsed time. c 40000.
            // t=267: b again (100500).
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 225);
            BattleUnit c = Unit("c", 49);
            TurnManager turns = new TurnManager(new[] { a, b, c });

            List<BattleUnit> actors = new List<BattleUnit>();
            List<long> ticks = new List<long>();
            for (int i = 0; i < 7; i++)
            {
                actors.Add(turns.CurrentUnit);
                ticks.Add(turns.ElapsedTicks);
                turns.AdvanceTurn();
            }

            CollectionAssert.AreEqual(new[] { b, a, b, c, b, a, b }, actors);
            CollectionAssert.AreEqual(new long[] { 67, 100, 134, 143, 200, 200, 267 }, ticks);
        }

        [Test]
        public void FirstTurn_IsReadyAsSoonAsTheManagerIsBuilt()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 225);
            TurnManager turns = new TurnManager(new[] { a, b });

            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(67, turns.ElapsedTicks);
            Assert.AreEqual(0.67, turns.Time, 1e-9);
            Assert.AreEqual(1, turns.ActionCount);
            Assert.AreEqual(100500, turns.GetGauge(b));
            Assert.AreEqual(67000, turns.GetGauge(a));
        }

        [Test]
        public void FourTimesTheSpeed_GetsTwiceTheTurns()
        {
            // Rates 1000 and 2000: 100 ticks per turn against 50.
            Dictionary<BattleUnit, int> counts = CountTurnsUntil(10000, Unit("slow", 100), Unit("fast", 400));

            Assert.AreEqual(100, CountFor(counts, "slow"));
            Assert.AreEqual(200, CountFor(counts, "fast"));
        }

        [Test]
        public void NineTimesTheSpeed_GetsThreeTimesTheTurns_EvenWhenTheThresholdDoesNotDivideEvenly()
        {
            // Rates 500 and 1500. 100000 / 1500 is not whole, so each wait rounds up; the carried
            // overflow pays it back (turn 150 lands on t=10000 exactly).
            Dictionary<BattleUnit, int> counts = CountTurnsUntil(10000, Unit("slow", 25), Unit("fast", 225));

            Assert.AreEqual(50, CountFor(counts, "slow"));
            Assert.AreEqual(150, CountFor(counts, "fast"));
        }

        [Test]
        public void DoubleSpeed_GetsSqrtTwoTheTurns_NotTwice()
        {
            // Rates 707 and 1000: the ratio is 1.414, the square root of the speed ratio.
            Dictionary<BattleUnit, int> counts = CountTurnsUntil(100000, Unit("slow", 50), Unit("fast", 100));

            Assert.AreEqual(707, CountFor(counts, "slow"));
            Assert.AreEqual(1000, CountFor(counts, "fast"));
        }

        [Test]
        public void Overflow_CarriesIntoTheNextWait()
        {
            // Speed 225 (rate 1500): 100500 at t=67 (carry 500), 101000 at t=134 (carry 1000),
            // 100000 at t=200. Three turns in 200 ticks is exactly the 1500 rate; without the carry
            // the third would be at t=201.
            BattleUnit b = Unit("b", 225);
            TurnManager turns = new TurnManager(new[] { b });

            Assert.AreEqual(67, turns.ElapsedTicks);
            turns.AdvanceTurn();
            Assert.AreEqual(500 + (1500 * 67), turns.GetGauge(b));
            Assert.AreEqual(134, turns.ElapsedTicks);
            turns.AdvanceTurn();
            Assert.AreEqual(200, turns.ElapsedTicks);
            Assert.AreEqual(100000, turns.GetGauge(b));
            turns.AdvanceTurn();
            Assert.AreEqual(0 + (1500 * 67), turns.GetGauge(b));
        }

        // ---------------------------------------------------------------------------------------
        // Ties.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EqualGaugeAndSpeed_BreaksOnId_WhateverTheInputOrder()
        {
            BattleUnit b = Unit("b", 100);
            BattleUnit a = Unit("a", 100);
            TurnManager turns = new TurnManager(new[] { b, a });

            Assert.AreSame(a, turns.CurrentUnit);
            Assert.AreSame(b, turns.AdvanceTurn());
            Assert.AreEqual(100, turns.ElapsedTicks);
            Assert.AreSame(a, turns.AdvanceTurn());
            Assert.AreEqual(200, turns.ElapsedTicks);
        }

        [Test]
        public void EqualGauge_TheFasterUnitGoesFirst_BeforeTheId()
        {
            // At t=100 both sit at exactly 100000; "b" is faster, so it goes before "a".
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 400);
            TurnManager turns = new TurnManager(new[] { a, b });

            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(50, turns.ElapsedTicks);
            Assert.AreSame(b, turns.AdvanceTurn());
            Assert.AreEqual(100, turns.ElapsedTicks);
            Assert.AreSame(a, turns.AdvanceTurn());
            Assert.AreEqual(100, turns.ElapsedTicks);
        }

        [Test]
        public void HigherGauge_GoesFirst_EvenAgainstAFasterUnitThatWinsTheId()
        {
            // Speeds 28 (rate 529) and 111 (rate 1054): at t=190 both are full, the slow unit at
            // 100510 and the fast one at 100260 (after carrying 130 from its t=95 turn). Most
            // overflow acts first.
            BattleUnit slow = Unit("b", 28);
            BattleUnit fast = Unit("a", 111);
            TurnManager turns = new TurnManager(new[] { slow, fast });

            Assert.AreSame(fast, turns.CurrentUnit);
            Assert.AreEqual(95, turns.ElapsedTicks);

            Assert.AreSame(slow, turns.AdvanceTurn());
            Assert.AreEqual(190, turns.ElapsedTicks);
            Assert.AreEqual(100510, turns.GetGauge(slow));
            Assert.AreEqual(100260, turns.GetGauge(fast));

            Assert.AreSame(fast, turns.AdvanceTurn());
            Assert.AreEqual(190, turns.ElapsedTicks);
        }

        // ---------------------------------------------------------------------------------------
        // Live speed, defeat, and the degenerate cases.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SpeedBuff_MidBattle_ChangesCadenceFromThatMoment()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 100);
            TurnManager turns = new TurnManager(new[] { a, b });

            Assert.AreSame(a, turns.CurrentUnit);
            Assert.AreSame(b, turns.AdvanceTurn());

            // During b's turn at t=100, a is hasted to Speed 400 (rate 2000): it now needs 50 ticks,
            // not 100 — four times the Speed, twice the turns.
            a.Stats = Speed(a.Stats, 400);

            List<BattleUnit> actors = new List<BattleUnit>();
            List<long> ticks = new List<long>();
            for (int i = 0; i < 6; i++)
            {
                actors.Add(turns.AdvanceTurn());
                ticks.Add(turns.ElapsedTicks);
            }

            CollectionAssert.AreEqual(new[] { a, a, b, a, a, b }, actors);
            CollectionAssert.AreEqual(new long[] { 150, 200, 200, 250, 300, 300 }, ticks);
        }

        [Test]
        public void DefeatedUnits_AreSkipped_AndStopFilling()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 225);
            BattleUnit c = Unit("c", 49);
            TurnManager turns = new TurnManager(new[] { a, b, c });

            Assert.AreSame(b, turns.CurrentUnit);
            b.IsDefeated = true;
            long frozen = turns.GetGauge(b) - TurnManager.ActionThreshold;

            for (int i = 0; i < 20; i++)
            {
                BattleUnit next = turns.AdvanceTurn();
                Assert.AreNotSame(b, next);
            }

            Assert.AreEqual(frozen, turns.GetGauge(b));
            Assert.IsFalse(turns.IsComplete);
        }

        [Test]
        public void EveryUnitDefeated_NoTurnIsHandedOut()
        {
            BattleUnit a = Unit("a", 100);
            TurnManager turns = new TurnManager(new[] { a });
            long before = turns.ElapsedTicks;

            a.IsDefeated = true;

            Assert.IsTrue(turns.IsComplete);
            Assert.IsNull(turns.AdvanceTurn());
            Assert.IsNull(turns.CurrentUnit);
            Assert.AreEqual(before, turns.ElapsedTicks);
            Assert.AreEqual(0, turns.PredictNextActors(5).Count);
        }

        [Test]
        public void EmptyOrNullRoster_HasNoTurn()
        {
            Assert.IsNull(new TurnManager(null).CurrentUnit);
            Assert.IsTrue(new TurnManager(new BattleUnit[] { null }).IsComplete);
        }

        [Test]
        public void ZeroOrNegativeSpeed_StillFillsAsSpeedOne()
        {
            // Speed 1 fills at 100 per tick: 1000 ticks per turn.
            TurnManager turns = new TurnManager(new[] { Unit("a", 0) });

            Assert.AreEqual(TurnManager.ActionThreshold / 100, turns.ElapsedTicks);
            Assert.AreEqual(100, TurnManager.FillRate(Unit("b", -5)));
            Assert.AreEqual(100, TurnManager.FillRateForSpeed(int.MinValue));
        }

        [Test]
        public void PredictNextActors_MatchesTheRealSequence_AndChangesNothing()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 225);
            BattleUnit c = Unit("c", 49);
            TurnManager turns = new TurnManager(new[] { a, b, c });

            IReadOnlyList<BattleUnit> forecast = turns.PredictNextActors(7);

            CollectionAssert.AreEqual(new[] { b, a, b, c, b, a, b }, forecast);
            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(67, turns.ElapsedTicks);
            Assert.AreEqual(100500, turns.GetGauge(b));
            Assert.AreEqual(1, turns.ActionCount);
        }

        // ---------------------------------------------------------------------------------------
        // Through RunBattle.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void RunBattle_TimeCap_StopsAnUnendingBattleAsAStalemate()
        {
            // Two skill-less units can never end the fight. Cap 5 = 500 ticks: turns at 100..500 each.
            BattleUnit player = Unit("p", 100, BattleTeam.Player);
            BattleUnit enemy = Unit("e", 100, BattleTeam.Enemy);
            BattleUnit[] roster = { player, enemy };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(1), null, 5);

            Assert.AreEqual(BattleOutcome.Stalemate, result.Outcome);
            Assert.AreEqual(10, result.ActionCount);
            Assert.AreEqual(500, result.ElapsedTicks);
            Assert.AreEqual(5.0, result.Time, 1e-9);
        }

        [Test]
        public void RunBattle_NineTimesTheSpeed_TakesThreeTimesTheTurns()
        {
            BattleUnit slow = Unit("p", 25, BattleTeam.Player);
            BattleUnit fast = Unit("e", 225, BattleTeam.Enemy);
            BattleUnit[] roster = { slow, fast };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(1), null, 100);

            int slowTurns = 0;
            int fastTurns = 0;
            foreach (BattleTurnResult turn in result.Turns)
            {
                slowTurns += turn.Unit == slow ? 1 : 0;
                fastTurns += turn.Unit == fast ? 1 : 0;
            }

            Assert.AreEqual(50, slowTurns);
            Assert.AreEqual(150, fastTurns);
            Assert.AreEqual(10000, result.ElapsedTicks);
        }

        // ---------------------------------------------------------------------------------------

        private static Dictionary<BattleUnit, int> CountTurnsUntil(long ticks, params BattleUnit[] units)
        {
            Dictionary<BattleUnit, int> counts = new Dictionary<BattleUnit, int>();
            TurnManager turns = new TurnManager(units);
            while (turns.ElapsedTicks <= ticks)
            {
                counts.TryGetValue(turns.CurrentUnit, out int count);
                counts[turns.CurrentUnit] = count + 1;
                turns.AdvanceTurn();
            }

            return counts;
        }

        private static int CountFor(Dictionary<BattleUnit, int> counts, string id)
        {
            foreach (KeyValuePair<BattleUnit, int> pair in counts)
            {
                if (pair.Key.Id == id)
                {
                    return pair.Value;
                }
            }

            return 0;
        }

        private static BattleUnit Unit(string id, int speed, BattleTeam team = BattleTeam.Player)
        {
            return new BattleUnit(id, team, new StatBlock(100, 10, 10, 10, 10, speed), HexCoordinate.Zero);
        }

        private static StatBlock Speed(StatBlock stats, int speed)
        {
            stats.Speed = speed;
            return stats;
        }
    }
}
