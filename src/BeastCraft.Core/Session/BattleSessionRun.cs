using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
using BeastCraft.Save;

namespace BeastCraft.Session
{
    /// <summary>
    /// A session battle begun by <see cref="BattleSession.Begin"/> and not yet finished: the built
    /// board and units, ready to be stepped one turn at a time (<see cref="Step"/>) — what a battle
    /// viewer drives — and then <see cref="Finish"/>ed into the <see cref="BattleSessionResult"/>
    /// that <see cref="BattleSession.Run"/> would have returned for the same setup.
    /// </summary>
    public sealed class BattleSessionRun
    {
        private readonly List<BattleUnit> _units;
        private readonly TeamBondLoadout _bonds;
        private readonly List<OwnedBeast> _team;
        private readonly List<BattleUnit> _members;
        private bool _finished;

        internal BattleSessionRun(BattleSessionResult result)
        {
            Result = result;
        }

        internal BattleSessionRun(BattleSessionResult result, BattleRun battle, HexGrid grid, List<BattleUnit> units, BattleUnit avatar, TeamBondLoadout bonds,
                                  List<OwnedBeast> team, List<BattleUnit> members)
        {
            Result = result;
            Battle = battle;
            Grid = grid;
            Avatar = avatar;
            _units = units;
            _bonds = bonds;
            _team = team;
            _members = members;
        }

        /// <summary>
        /// The session result: failed (with every error) when the setup did not validate, otherwise
        /// filled in by <see cref="Finish"/>. Its <see cref="BattleSessionResult.StartingStats"/> are
        /// already set once the battle has begun.
        /// </summary>
        public BattleSessionResult Result { get; }

        /// <summary>The battle being stepped, or null when the setup failed.</summary>
        public BattleRun Battle { get; }

        /// <summary>The board, or null when the setup failed.</summary>
        public HexGrid Grid { get; }

        /// <summary>The avatar, or null (none, or the setup failed).</summary>
        public BattleUnit Avatar { get; }

        /// <summary>The roster (enemies, then the team), or empty when the setup failed.</summary>
        public IReadOnlyList<BattleUnit> Units
        {
            get { return _units ?? new List<BattleUnit>(); }
        }

        /// <summary>Plays the next turn (see <see cref="BattleRun.Step"/>); null once the battle is over or when the setup failed.</summary>
        public BattleTurnResult Step()
        {
            return Battle == null || _finished ? null : Battle.Step();
        }

        /// <summary>
        /// Plays out whatever is left of the battle and fills <see cref="Result"/>, exactly as
        /// <see cref="BattleSession.Run"/> does. Finishing twice returns the same result.
        /// </summary>
        public BattleSessionResult Finish()
        {
            if (Battle == null || _finished)
            {
                return Result;
            }

            BattleResult battle = Battle.RunToEnd();
            _finished = true;
            BattleSession.Complete(Result, battle, Grid, _units, Avatar, _bonds, _team, _members);
            return Result;
        }
    }
}
