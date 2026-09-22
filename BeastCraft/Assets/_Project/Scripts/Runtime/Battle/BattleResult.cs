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
        public BattleResult(BattleOutcome outcome, int rounds, IReadOnlyList<BattleTurnResult> turns)
        {
            Outcome = outcome;
            Rounds = rounds;
            Turns = turns ?? new List<BattleTurnResult>();
        }

        /// <summary>Which side won, or that nobody did.</summary>
        public BattleOutcome Outcome { get; }

        /// <summary>
        /// <see cref="TurnManager.Round"/> as it stood when the loop stopped, counting from 1. Under
        /// <see cref="BattleOutcome.Stalemate"/> from the round cap this is one past the cap, since
        /// the cap is tested at the top of a round that then never runs.
        /// </summary>
        public int Rounds { get; }

        /// <summary>
        /// Every turn that was executed, in the order they were taken. Never <c>null</c>. Bounded by
        /// the round cap, so this cannot grow without limit even on a battle that never resolves.
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
