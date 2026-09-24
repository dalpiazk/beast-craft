using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Session;

namespace BeastCraft.Presentation.Playback
{
    /// <summary>One unit as it stood at one moment: what the viewer draws, copied so later turns cannot change it.</summary>
    public readonly struct UnitSnapshot
    {
        public UnitSnapshot(BattleUnit unit)
        {
            Id = unit.Id;
            Team = unit.Team;
            Position = unit.Position;
            Footprint = unit.Footprint;
            Hp = unit.CurrentHp;
            MaxHp = unit.Stats.Hp;
            Defeated = unit.IsDefeated;
        }

        public string Id { get; }

        public BattleTeam Team { get; }

        public HexCoordinate Position { get; }

        public UnitFootprint Footprint { get; }

        public int Hp { get; }

        public int MaxHp { get; }

        public bool Defeated { get; }
    }

    /// <summary>One played turn: the record, its skill beats, and every unit before and after it.</summary>
    public sealed class PlayedTurn
    {
        public PlayedTurn(int index, BattleTurnResult turn, List<SkillBeat> beats, Dictionary<string, UnitSnapshot> before, Dictionary<string, UnitSnapshot> after)
        {
            Index = index;
            Turn = turn;
            Beats = beats;
            Before = before;
            After = after;
        }

        /// <summary>0-based turn number.</summary>
        public int Index { get; }

        public BattleTurnResult Turn { get; }

        public IReadOnlyList<SkillBeat> Beats { get; }

        public IReadOnlyDictionary<string, UnitSnapshot> Before { get; }

        public IReadOnlyDictionary<string, UnitSnapshot> After { get; }
    }

    /// <summary>
    /// A viewer's handle on a session battle begun with <see cref="BattleSession.Begin"/>: plays it
    /// one real turn at a time (<see cref="BattleSessionRun.Step"/>, the same code and random draws
    /// as <see cref="BattleSession.Run"/>) and records what each turn looked like before and after,
    /// so a renderer can animate between them. It only reads the battle; the simulation stays
    /// exactly what it would be without a viewer.
    /// </summary>
    public sealed class BattlePlayback
    {
        private readonly BattleSessionRun _run;
        private readonly List<PlayedTurn> _played = new List<PlayedTurn>();
        private Dictionary<string, UnitSnapshot> _current;

        public BattlePlayback(BattleSessionRun run)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            if (run.Battle == null)
            {
                throw new ArgumentException("The battle did not begin: " + run.Result.Error, nameof(run));
            }

            _current = Capture(run.Units);
            Initial = _current;
        }

        /// <summary>Every unit before the first turn.</summary>
        public IReadOnlyDictionary<string, UnitSnapshot> Initial { get; }

        /// <summary>Every unit now (after the last played turn).</summary>
        public IReadOnlyDictionary<string, UnitSnapshot> Current
        {
            get { return _current; }
        }

        /// <summary>The roster, in session order (enemies, then the team).</summary>
        public IReadOnlyList<BattleUnit> Units
        {
            get { return _run.Units; }
        }

        public HexGrid Grid
        {
            get { return _run.Grid; }
        }

        /// <summary>The turns played so far.</summary>
        public IReadOnlyList<PlayedTurn> Played
        {
            get { return _played; }
        }

        /// <summary>True once the battle has been decided (no further turn).</summary>
        public bool IsOver
        {
            get { return _run.Battle.IsOver; }
        }

        /// <summary>The outcome, once <see cref="IsOver"/>.</summary>
        public BattleOutcome Outcome
        {
            get { return _run.Battle.Outcome; }
        }

        /// <summary>Plays the next turn and returns it, or null when the battle is over (then <see cref="Result"/> is final).</summary>
        public PlayedTurn Advance()
        {
            Dictionary<string, UnitSnapshot> before = _current;
            BattleTurnResult turn = _run.Step();
            if (turn == null)
            {
                _run.Finish();
                return null;
            }

            _current = Capture(_run.Units);
            PlayedTurn played = new PlayedTurn(_played.Count, turn, SkillBeat.FromTurn(turn), before, _current);
            _played.Add(played);
            return played;
        }

        /// <summary>The session result: complete (as <see cref="BattleSession.Run"/> gives it) once the battle is over.</summary>
        public BattleSessionResult Result
        {
            get { return _run.Result; }
        }

        /// <summary>The next <paramref name="count"/> turns as they would fall now (living units only): the turn-order forecast.</summary>
        public List<BattleUnit> Forecast(int count)
        {
            List<BattleUnit> forecast = new List<BattleUnit>();
            if (IsOver || _run.Battle.TurnManager == null)
            {
                return forecast;
            }

            foreach (BattleUnit unit in _run.Battle.TurnManager.PredictNextActors(count * 3))
            {
                if (unit != null && !unit.IsDefeated)
                {
                    forecast.Add(unit);
                    if (forecast.Count == count)
                    {
                        break;
                    }
                }
            }

            return forecast;
        }

        /// <summary>A snapshot of every unit, by id.</summary>
        public static Dictionary<string, UnitSnapshot> Capture(IEnumerable<BattleUnit> units)
        {
            Dictionary<string, UnitSnapshot> snapshot = new Dictionary<string, UnitSnapshot>(StringComparer.Ordinal);
            foreach (BattleUnit unit in units)
            {
                if (unit != null)
                {
                    snapshot[unit.Id] = new UnitSnapshot(unit);
                }
            }

            return snapshot;
        }
    }
}
