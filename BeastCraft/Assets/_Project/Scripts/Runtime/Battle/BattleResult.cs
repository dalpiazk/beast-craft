using System.Collections.Generic;

namespace BeastCraft.Battle
{
    /// <summary>
    /// How a whole battle came out, plus the turn-by-turn record of how it got there.
    /// <para>
    /// The return shape of <see cref="BattleTurnExecutor.RunBattle"/>. A named type rather than a
    /// bare <see cref="BattleOutcome"/> because the outcome alone cannot be inspected: a caller
    /// checking that a fight ended for the right reason, or a test proving a skill that failed to
    /// reach on one turn fired on a later one, needs the turns.
    /// </para>
    /// </summary>
    public class BattleResult
    {
        public BattleResult(BattleOutcome outcome, long elapsedTicks, IReadOnlyList<BattleTurnResult> turns)
        {
            Outcome = outcome;
            ElapsedTicks = elapsedTicks < 0 ? 0 : elapsedTicks;
            Turns = turns ?? new List<BattleTurnResult>();
        }

        /// <summary>Which side won, or that nobody did.</summary>
        public BattleOutcome Outcome { get; }

        /// <summary>
        /// The battle's length in <see cref="TurnManager"/> ticks: the moment the last executed turn
        /// was taken (for a won battle, the turn that decided it), or 0 when no turn was taken. Never
        /// past the time cap — under <see cref="BattleOutcome.Stalemate"/> from the cap it is the time
        /// of the last turn that still fitted under it.
        /// </summary>
        public long ElapsedTicks { get; }

        /// <summary>
        /// <see cref="ElapsedTicks"/> in normalized time: 1.0 is one turn of a
        /// <see cref="TurnManager.ReferenceSpeed"/> unit (<see cref="TurnManager.TicksPerTimeUnit"/>
        /// ticks). For reporting; the battle itself only ever reasons in ticks.
        /// </summary>
        public double Time
        {
            get { return (double)ElapsedTicks / TurnManager.TicksPerTimeUnit; }
        }

        /// <summary>How many turns were executed: <see cref="Turns"/>' count.</summary>
        public int ActionCount
        {
            get { return Turns.Count; }
        }

        /// <summary>
        /// Every turn that was executed, in the order they were taken. Never <c>null</c>. Bounded by
        /// the time cap, so this cannot grow without limit even on a battle that never resolves.
        /// </summary>
        public IReadOnlyList<BattleTurnResult> Turns { get; }

        /// <summary>
        /// The winning team, when there is one. <c>false</c> under
        /// <see cref="BattleOutcome.MutualDefeat"/> and <see cref="BattleOutcome.Stalemate"/>, where
        /// <paramref name="winner"/> is left at <see cref="BattleTeam.Player"/> and means nothing.
        /// <para>
        /// A method with a <c>bool</c> rather than a <c>BattleTeam?</c> property so that the "there
        /// is no winner" case cannot be read past with a <c>.Value</c>, and so the two outcomes that
        /// have no winner are handled rather than defaulted through.
        /// </para>
        /// </summary>
        public bool TryGetWinner(out BattleTeam winner)
        {
            switch (Outcome)
            {
                case BattleOutcome.PlayerVictory:
                    winner = BattleTeam.Player;
                    return true;

                case BattleOutcome.EnemyVictory:
                    winner = BattleTeam.Enemy;
                    return true;

                default:
                    winner = BattleTeam.Player;
                    return false;
            }
        }
    }
}
