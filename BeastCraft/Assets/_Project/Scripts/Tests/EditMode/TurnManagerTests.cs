using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="TurnManager"/>'s ATB gauge: units fill at their live Speed and act at
    /// <see cref="TurnManager.ActionThreshold"/>, one at a time, overflow carried.
    /// <para>
    /// Every expected sequence here was worked by hand from the rules (ticks needed =
    /// <c>ceil((1000 - gauge) / speed)</c>, time jumps by the minimum, full units act highest gauge
    /// first, then higher Speed, then id). Units carry no skills unless a test says otherwise, so a
    /// turn changes nothing and a battle between them can only stop at the time cap.
    /// </para>
    /// </summary>
    public class TurnManagerTests
    {
        // ---------------------------------------------------------------------------------------
        // The action sequence.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void HandComputedSequence_Speeds100_150_50()
        {
            // t=7: b fills first (1050) and carries 50. t=10: a (1000). t=14: b (1100, carries 100).
            // t=20: all three are at exactly 1000 — equal gauges, so the faster goes first: b, a, c
            // at zero elapsed time. 20 ticks is the full cycle, so t=27 repeats t=7.
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 150);
            BattleUnit c = Unit("c", 50);
            TurnManager turns = new TurnManager(new[] { a, b, c });

            List<BattleUnit> actors = new List<BattleUnit>();
            List<long> ticks = new List<long>();
            for (int i = 0; i < 7; i++)
            {
                actors.Add(turns.CurrentUnit);
                ticks.Add(turns.ElapsedTicks);
                turns.AdvanceTurn();
            }

            CollectionAssert.AreEqual(new[] { b, a, b, b, a, c, b }, actors);
            CollectionAssert.AreEqual(new long[] { 7, 10, 14, 20, 20, 20, 27 }, ticks);
        }

        [Test]
        public void FirstTurn_IsReadyAsSoonAsTheManagerIsBuilt()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 150);
            TurnManager turns = new TurnManager(new[] { a, b });

            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(7, turns.ElapsedTicks);
            Assert.AreEqual(0.7, turns.Time, 1e-9);
            Assert.AreEqual(1, turns.ActionCount);
            Assert.AreEqual(1050, turns.GetGauge(b));
            Assert.AreEqual(700, turns.GetGauge(a));
        }

        [Test]
        public void DoubleSpeed_GetsTwiceTheTurns()
        {
            Dictionary<BattleUnit, int> counts = CountTurnsUntil(10000, Unit("slow", 50), Unit("fast", 100));

            Assert.AreEqual(500, CountFor(counts, "slow"));
            Assert.AreEqual(1000, CountFor(counts, "fast"));
        }

        [Test]
        public void TripleSpeed_GetsThreeTimesTheTurns_EvenWhenTheThresholdDoesNotDivideEvenly()
        {
            // 1000 / 120 is not whole, so each wait rounds up; the carried overflow pays it back.
            Dictionary<BattleUnit, int> counts = CountTurnsUntil(10000, Unit("slow", 40), Unit("fast", 120));

            Assert.AreEqual(400, CountFor(counts, "slow"));
            Assert.AreEqual(1200, CountFor(counts, "fast"));
        }

        [Test]
        public void Overflow_CarriesIntoTheNextWait()
        {
            // Speed 150: 1050 at t=7 (carry 50), 1100 at t=14 (carry 100), 1000 at t=20. Three turns
            // in 20 ticks is exactly the 150 rate; without the carry the third would be at t=21.
            BattleUnit b = Unit("b", 150);
            TurnManager turns = new TurnManager(new[] { b });

            Assert.AreEqual(7, turns.ElapsedTicks);
            turns.AdvanceTurn();
            Assert.AreEqual(50 + (150 * 7), turns.GetGauge(b));
            Assert.AreEqual(14, turns.ElapsedTicks);
            turns.AdvanceTurn();
            Assert.AreEqual(20, turns.ElapsedTicks);
            Assert.AreEqual(1000, turns.GetGauge(b));
            turns.AdvanceTurn();
            Assert.AreEqual(0 + (150 * 7), turns.GetGauge(b));
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
            Assert.AreEqual(10, turns.ElapsedTicks);
            Assert.AreSame(a, turns.AdvanceTurn());
            Assert.AreEqual(20, turns.ElapsedTicks);
        }

        [Test]
        public void EqualGauge_TheFasterUnitGoesFirst_BeforeTheId()
        {
            // At t=10 both sit at exactly 1000; "b" is faster, so it goes before "a".
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 200);
            TurnManager turns = new TurnManager(new[] { a, b });

            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(5, turns.ElapsedTicks);
            Assert.AreSame(b, turns.AdvanceTurn());
            Assert.AreEqual(10, turns.ElapsedTicks);
            Assert.AreSame(a, turns.AdvanceTurn());
            Assert.AreEqual(10, turns.ElapsedTicks);
        }

        [Test]
        public void HigherGauge_GoesFirst_EvenAgainstAFasterUnitThatWinsTheId()
        {
            // Speeds 30 and 59: at t=34 both are full, the slow unit at 1020 and the fast one at
            // 1006 (after carrying 3 from its t=17 turn). Most overflow acts first.
            BattleUnit slow = Unit("b", 30);
            BattleUnit fast = Unit("a", 59);
            TurnManager turns = new TurnManager(new[] { slow, fast });

            Assert.AreSame(fast, turns.CurrentUnit);
            Assert.AreEqual(17, turns.ElapsedTicks);

            Assert.AreSame(slow, turns.AdvanceTurn());
            Assert.AreEqual(34, turns.ElapsedTicks);
            Assert.AreEqual(1020, turns.GetGauge(slow));
            Assert.AreEqual(1006, turns.GetGauge(fast));

            Assert.AreSame(fast, turns.AdvanceTurn());
            Assert.AreEqual(34, turns.ElapsedTicks);
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

            // During b's turn at t=10, a is hasted to 200: it now needs 5 ticks, not 10.
            a.Stats = Speed(a.Stats, 200);

            List<BattleUnit> actors = new List<BattleUnit>();
            List<long> ticks = new List<long>();
            for (int i = 0; i < 6; i++)
            {
                actors.Add(turns.AdvanceTurn());
                ticks.Add(turns.ElapsedTicks);
            }

            CollectionAssert.AreEqual(new[] { a, a, b, a, a, b }, actors);
            CollectionAssert.AreEqual(new long[] { 15, 20, 20, 25, 30, 30 }, ticks);
        }

        [Test]
        public void DefeatedUnits_AreSkipped_AndStopFilling()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 150);
            BattleUnit c = Unit("c", 50);
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
        public void ZeroOrNegativeSpeed_StillFillsAtOne()
        {
            TurnManager turns = new TurnManager(new[] { Unit("a", 0) });

            Assert.AreEqual(TurnManager.ActionThreshold, turns.ElapsedTicks);
            Assert.AreEqual(1, TurnManager.FillRate(Unit("b", -5)));
        }

        [Test]
        public void PredictNextActors_MatchesTheRealSequence_AndChangesNothing()
        {
            BattleUnit a = Unit("a", 100);
            BattleUnit b = Unit("b", 150);
            BattleUnit c = Unit("c", 50);
            TurnManager turns = new TurnManager(new[] { a, b, c });

            IReadOnlyList<BattleUnit> forecast = turns.PredictNextActors(7);

            CollectionAssert.AreEqual(new[] { b, a, b, b, a, c, b }, forecast);
            Assert.AreSame(b, turns.CurrentUnit);
            Assert.AreEqual(7, turns.ElapsedTicks);
            Assert.AreEqual(1050, turns.GetGauge(b));
            Assert.AreEqual(1, turns.ActionCount);
        }

        // ---------------------------------------------------------------------------------------
        // Through RunBattle.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void RunBattle_TimeCap_StopsAnUnendingBattleAsAStalemate()
        {
            // Two skill-less units can never end the fight. Cap 5 = 50 ticks: turns at 10..50 each.
            BattleUnit player = Unit("p", 100, BattleTeam.Player);
            BattleUnit enemy = Unit("e", 100, BattleTeam.Enemy);
            BattleUnit[] roster = { player, enemy };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(1), null, 5);

            Assert.AreEqual(BattleOutcome.Stalemate, result.Outcome);
            Assert.AreEqual(10, result.ActionCount);
            Assert.AreEqual(50, result.ElapsedTicks);
            Assert.AreEqual(5.0, result.Time, 1e-9);
        }

        [Test]
        public void RunBattle_TripleSpeed_TakesThreeTimesTheTurns()
        {
            BattleUnit slow = Unit("p", 40, BattleTeam.Player);
            BattleUnit fast = Unit("e", 120, BattleTeam.Enemy);
            BattleUnit[] roster = { slow, fast };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(1), null, 100);

            int slowTurns = 0;
            int fastTurns = 0;
            foreach (BattleTurnResult turn in result.Turns)
            {
                slowTurns += turn.Unit == slow ? 1 : 0;
                fastTurns += turn.Unit == fast ? 1 : 0;
            }

            Assert.AreEqual(40, slowTurns);
            Assert.AreEqual(120, fastTurns);
            Assert.AreEqual(1000, result.ElapsedTicks);
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
