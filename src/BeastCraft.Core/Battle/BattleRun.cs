using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;

namespace BeastCraft.Battle
{
    /// <summary>
    /// A battle played one turn at a time: the loop of
    /// <see cref="BattleTurnExecutor.RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, TeamBondLoadout, int)"/>,
    /// which is now written in terms of this class, so a caller stepping it turn by turn (a battle
    /// viewer) and a caller running it to the end (the session, the balance simulator) execute the
    /// same code and draw the same random numbers.
    /// <para>
    /// The constructor runs the battle-start hook (bonds, then the avatar's passives) exactly as
    /// <c>RunBattle</c> does; each <see cref="Step"/> then plays the next turn — skipping the turn
    /// manager's defeated units the way the loop does — and returns its record, or <c>null</c> once
    /// the battle is decided (or stopped by the time cap). Reading the units between steps is safe
    /// and changes nothing; changing them is not supported.
    /// </para>
    /// </summary>
    public sealed class BattleRun
    {
        private readonly TurnManager _turnManager;
        private readonly List<BattleUnit> _roster;
        private readonly HexGrid _grid;
        private readonly Random _rng;
        private readonly BattleUnit _avatar;
        private readonly PassiveLoadout _passives;
        private readonly TeamBondLoadout _bonds;
        private readonly long _capTicks;
        private readonly List<BattleTurnResult> _turns = new List<BattleTurnResult>();
        private long _lastTurnTicks;

        /// <summary>
        /// Begins the battle: copies the roster and runs the battle-start hook. The arguments are
        /// those of <c>RunBattle</c>, with the same meaning.
        /// </summary>
        public BattleRun(TurnManager turnManager, IEnumerable<BattleUnit> allUnits, HexGrid grid, Random rng, BattleUnit avatar, PassiveLoadout passives,
                         TeamBondLoadout bonds, int maxTime = BattleTurnExecutor.DefaultMaxTime)
        {
            _turnManager = turnManager;
            _roster = BattleTurnExecutor.CopyRoster(allUnits);
            _grid = grid;
            _rng = rng;
            _avatar = avatar;
            _passives = passives;
            _bonds = bonds;
            _capTicks = (long)(maxTime < 1 ? 1 : maxTime) * TurnManager.TicksPerTimeUnit;
            OpeningPassiveActivations = BattleTurnExecutor.BeginBattle(_roster, grid, rng, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> bonded);
            BondActivations = bonded;
        }

        /// <summary>The roster (both sides' beasts, not the avatar), in the order given.</summary>
        public IReadOnlyList<BattleUnit> Units
        {
            get { return _roster; }
        }

        /// <summary>The board.</summary>
        public HexGrid Grid
        {
            get { return _grid; }
        }

        /// <summary>The avatar, or null.</summary>
        public BattleUnit Avatar
        {
            get { return _avatar; }
        }

        /// <summary>The turn order (read it, for example <see cref="TurnManager.PredictNextActors"/>; do not advance it).</summary>
        public TurnManager TurnManager
        {
            get { return _turnManager; }
        }

        /// <summary>The avatar passives the battle-start hook fired.</summary>
        public IReadOnlyList<PassiveActivation> OpeningPassiveActivations { get; }

        /// <summary>The bonds the battle-start hook applied.</summary>
        public IReadOnlyList<TeamBondActivation> BondActivations { get; }

        /// <summary>Every turn played so far, in order.</summary>
        public IReadOnlyList<BattleTurnResult> Turns
        {
            get { return _turns; }
        }

        /// <summary>True once <see cref="Step"/> has found the battle over.</summary>
        public bool IsOver { get; private set; }

        /// <summary>How the battle came out; meaningful once <see cref="IsOver"/>.</summary>
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Stalemate;

        /// <summary>
        /// Plays the next turn and returns its record, or returns <c>null</c> (and sets
        /// <see cref="IsOver"/> and <see cref="Outcome"/>) when the battle is decided, the time cap
        /// is passed or the turn order has run dry.
        /// </summary>
        public BattleTurnResult Step()
        {
            while (!IsOver)
            {
                if (BattleTurnExecutor.TryConclude(_roster, out BattleOutcome outcome))
                {
                    Finish(outcome);
                    break;
                }

                if (_turnManager == null || _turnManager.ElapsedTicks > _capTicks)
                {
                    Finish(BattleOutcome.Stalemate);
                    break;
                }

                BattleUnit current = _turnManager.CurrentUnit;

                if (current == null)
                {
                    Finish(BattleOutcome.Stalemate);
                    break;
                }

                BattleTurnResult turn = null;

                if (!current.IsDefeated)
                {
                    _lastTurnTicks = _turnManager.ElapsedTicks;
                    turn = current == _avatar
                               ? BattleTurnExecutor.ExecuteAvatarTurn(_avatar, _roster, _grid, _rng, _passives, _bonds)
                               : BattleTurnExecutor.ExecuteTurn(current, _roster, _grid, _rng, _avatar, _passives, _bonds);
                    _turns.Add(turn);
                }

                _turnManager.AdvanceTurn();

                if (turn != null)
                {
                    return turn;
                }
            }

            return null;
        }

        /// <summary>Steps until the battle is over and returns the result.</summary>
        public BattleResult RunToEnd()
        {
            while (Step() != null)
            {
            }

            return ToResult();
        }

        /// <summary>The battle so far as a <see cref="BattleResult"/> (its outcome is only final once <see cref="IsOver"/>).</summary>
        public BattleResult ToResult()
        {
            return new BattleResult(Outcome, _lastTurnTicks, _turns, OpeningPassiveActivations, BondActivations);
        }

        private void Finish(BattleOutcome outcome)
        {
            Outcome = outcome;
            IsOver = true;
        }
    }
}
