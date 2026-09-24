using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Session;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Playback
{
    /// <summary>One status on a unit at one moment (copied from its <see cref="ActiveStatus"/>).</summary>
    public readonly struct StatusSnapshot
    {
        public StatusSnapshot(StatusType type, int remainingTurns, int amount, Element sourceElement)
        {
            Type = type;
            RemainingTurns = remainingTurns;
            Amount = amount;
            SourceElement = sourceElement;
        }

        public StatusType Type { get; }

        /// <summary>The affected unit's own turns it has left (see <see cref="ActiveStatus.RemainingTurns"/>).</summary>
        public int RemainingTurns { get; }

        public int Amount { get; }

        /// <summary>The applying unit's first element (None without one): tells a burn from a poison.</summary>
        public Element SourceElement { get; }

        /// <summary>Its VFX key (<see cref="VfxEffectKey"/>), or null.</summary>
        public string Key
        {
            get { return VfxLibrary.KeyOf(Type, SourceElement); }
        }
    }

    /// <summary>One timed stat change on a unit at one moment (copied from its <see cref="ActiveStatModifier"/>).</summary>
    public readonly struct ModifierSnapshot
    {
        public ModifierSnapshot(StatType stat, int delta, int remainingTurns)
        {
            Stat = stat;
            Delta = delta;
            RemainingTurns = remainingTurns;
        }

        public StatType Stat { get; }

        /// <summary>Positive for a buff, negative for a debuff.</summary>
        public int Delta { get; }

        public int RemainingTurns { get; }
    }

    /// <summary>One unit as it stood at one moment: what the viewer draws, copied so later turns cannot change it.</summary>
    public readonly struct UnitSnapshot
    {
        private static readonly StatusSnapshot[] NoStatuses = new StatusSnapshot[0];
        private static readonly ModifierSnapshot[] NoModifiers = new ModifierSnapshot[0];

        public UnitSnapshot(BattleUnit unit)
        {
            Id = unit.Id;
            Team = unit.Team;
            Position = unit.Position;
            Footprint = unit.Footprint;
            Hp = unit.CurrentHp;
            MaxHp = unit.Stats.Hp;
            Defeated = unit.IsDefeated;

            StatusSnapshot[] statuses = unit.Statuses.Count == 0 ? NoStatuses : new StatusSnapshot[unit.Statuses.Count];
            for (int i = 0; i < statuses.Length; i++)
            {
                ActiveStatus status = unit.Statuses[i];
                Element source = status.Source != null && status.Source.Elements.Count > 0 ? status.Source.Elements[0] : Element.None;
                statuses[i] = new StatusSnapshot(status.Type, status.RemainingTurns, status.Amount, source);
            }

            ModifierSnapshot[] modifiers = unit.ActiveStatModifiers.Count == 0 ? NoModifiers : new ModifierSnapshot[unit.ActiveStatModifiers.Count];
            for (int i = 0; i < modifiers.Length; i++)
            {
                ActiveStatModifier modifier = unit.ActiveStatModifiers[i];
                modifiers[i] = new ModifierSnapshot(modifier.Stat, modifier.Delta, modifier.RemainingTurns);
            }

            Statuses = statuses;
            Modifiers = modifiers;
        }

        /// <summary>A snapshot from explicit values (tests, tools).</summary>
        public UnitSnapshot(string id, BattleTeam team, HexCoordinate position, UnitFootprint footprint, int hp, int maxHp, bool defeated,
                            IReadOnlyList<StatusSnapshot> statuses = null, IReadOnlyList<ModifierSnapshot> modifiers = null)
        {
            Id = id;
            Team = team;
            Position = position;
            Footprint = footprint;
            Hp = hp;
            MaxHp = maxHp;
            Defeated = defeated;
            Statuses = statuses ?? NoStatuses;
            Modifiers = modifiers ?? NoModifiers;
        }

        public string Id { get; }

        public BattleTeam Team { get; }

        public HexCoordinate Position { get; }

        public UnitFootprint Footprint { get; }

        public int Hp { get; }

        public int MaxHp { get; }

        public bool Defeated { get; }

        /// <summary>The statuses on the unit, in the order they were applied.</summary>
        public IReadOnlyList<StatusSnapshot> Statuses { get; }

        /// <summary>The timed stat buffs and debuffs on the unit.</summary>
        public IReadOnlyList<ModifierSnapshot> Modifiers { get; }

        /// <summary>
        /// The lasting VFX keys the unit shows (auras and icons), each once, in
        /// <see cref="VfxEffectKey.Lasting"/> order: its statuses', plus BuffStat / DebuffStat for
        /// any timed stat change up / down. None for a defeated unit.
        /// </summary>
        public List<string> StatusKeys()
        {
            List<string> keys = new List<string>();
            if (Defeated)
            {
                return keys;
            }

            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
            foreach (StatusSnapshot status in Statuses ?? NoStatuses)
            {
                string key = status.Key;
                if (key != null)
                {
                    present.Add(key);
                }
            }

            foreach (ModifierSnapshot modifier in Modifiers ?? NoModifiers)
            {
                if (modifier.Delta != 0)
                {
                    present.Add(modifier.Delta > 0 ? VfxEffectKey.BuffStat : VfxEffectKey.DebuffStat);
                }
            }

            foreach (string key in VfxEffectKey.Lasting)
            {
                if (present.Contains(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }
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
