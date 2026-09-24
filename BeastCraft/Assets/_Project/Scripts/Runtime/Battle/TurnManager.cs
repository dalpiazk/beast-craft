using System.Collections.Generic;

namespace BeastCraft.Battle
{
    /// <summary>
    /// The speed-stat initiative gauge (ATB): every combatant fills its own gauge at a rate that
    /// grows with the <em>square root</em> of its current <c>Stats.Speed</c> and takes a turn each
    /// time the gauge reaches <see cref="ActionThreshold"/>, so a unit four times as fast as another
    /// acts twice as often.
    /// <para>
    /// This replaced the original round-based queue (everyone acts once per round, fastest first),
    /// under which Speed only decided <em>order</em> within a round and a Speed-120 beast got exactly
    /// as many turns as a Speed-40 one. The first ATB gauge filled linearly in Speed; the square
    /// root (adopted from Sword x Staff, whose turn interval is <c>100000 / sqrt(SPD x scale)</c>)
    /// gives stacked Speed diminishing returns. See the design doc, §3 "Turn order model", and
    /// <c>docs/balance/research-sword-x-staff.md</c>.
    /// </para>
    /// <para>
    /// <strong>The model.</strong> Pure integer arithmetic, so a client and a server-authoritative
    /// re-simulation always agree:
    /// <list type="bullet">
    /// <item><description>
    /// Every unit starts at gauge 0. Its fill rate is <see cref="FillRateForSpeed"/> of its live
    /// <c>Stats.Speed</c>: <c>round(FillScale x sqrt(max(1, Speed)))</c>, computed with an exact
    /// integer square root (no floating point), so nothing can stall forever. It is read at every
    /// step, so a speed buff or debuff changes the unit's cadence from the moment it lands.
    /// </description></item>
    /// <item><description>
    /// Time advances by events, not by stepping: the next actor is found by computing, for every
    /// living unit, the ticks it still needs, <c>ceil((ActionThreshold - gauge) / fillRate)</c> (0
    /// when already full); time jumps by the smallest of those and every living unit's gauge gains
    /// <c>fillRate x elapsed</c>.
    /// </description></item>
    /// <item><description>
    /// Of the units now at or above the threshold, exactly one acts: the highest gauge (most
    /// overflow) first, then the higher fill rate (the higher Speed), then
    /// <see cref="BattleUnitOrder.CompareById"/>. The others act on the following steps with zero
    /// elapsed time.
    /// </description></item>
    /// <item><description>
    /// When the actor's turn is over (<see cref="AdvanceTurn"/>), <see cref="ActionThreshold"/> is
    /// subtracted from its gauge and the overflow carries into its next wait.
    /// </description></item>
    /// <item><description>
    /// Defeated units never fill and are never handed a turn; their gauge freezes where it was.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Still turn sequencing and nothing else — no action economy, no AI, no damage or skill
    /// resolution, and no buff logic. Everything counted "per turn" elsewhere (cooldowns, buff
    /// durations, the movement budget) is counted in the unit's <em>own</em> turns, so it needs no
    /// change here: a faster unit simply has more of them.
    /// </para>
    /// </summary>
    public class TurnManager
    {
        /// <summary>
        /// Multiplier on <c>sqrt(Speed)</c> in <see cref="FillRateForSpeed"/>: the resolution of the
        /// fill rate. At 100 a Speed-100 unit fills 1000 per tick and a Speed-1 unit 100; rounding
        /// to a whole rate moves a roster-band unit's turn rate by at most about 0.05%.
        /// </summary>
        public const int FillScale = 100;

        /// <summary>
        /// The gauge value at which a unit acts. A tunable default, not a balance number in itself:
        /// only the ratio between fill rates decides who acts how often, and this only sets the
        /// resolution of the integer time line. Chosen with <see cref="FillScale"/> so that
        /// <see cref="TicksPerTimeUnit"/> is a whole 100 (a Speed-1 unit waits 1000 ticks per turn).
        /// </summary>
        public const int ActionThreshold = 100000;

        /// <summary>
        /// The Speed whose turn defines one unit of <see cref="Time"/>: a unit at this Speed acts
        /// exactly once per 1.0 of time. A reporting convention only — it does not affect who acts
        /// when.
        /// </summary>
        public const int ReferenceSpeed = 100;

        /// <summary>
        /// The fill rate of a <see cref="ReferenceSpeed"/> unit, <c>FillScale x sqrt(100)</c> = 1000
        /// (exact: 100 is a perfect square).
        /// </summary>
        public const int ReferenceFillRate = FillScale * 10;

        /// <summary>
        /// Ticks per 1.0 of <see cref="Time"/>: <see cref="ActionThreshold"/> /
        /// <see cref="ReferenceFillRate"/> = 100, the ticks a Speed-100 unit waits for each turn.
        /// </summary>
        public const int TicksPerTimeUnit = ActionThreshold / ReferenceFillRate;

        private readonly List<BattleUnit> _roster;
        private readonly Dictionary<BattleUnit, long> _gauges;
        private BattleUnit _current;
        private long _elapsed;

        /// <summary>
        /// Starts every unit at gauge 0 and advances time to the first turn. Null entries are dropped
        /// (as are repeated references to the same unit); the roster itself is copied, so the
        /// caller's collection is not retained.
        /// </summary>
        public TurnManager(IEnumerable<BattleUnit> units)
        {
            _roster = new List<BattleUnit>();
            _gauges = new Dictionary<BattleUnit, long>();

            if (units != null)
            {
                foreach (BattleUnit unit in units)
                {
                    if (unit != null && !_gauges.ContainsKey(unit))
                    {
                        _roster.Add(unit);
                        _gauges.Add(unit, 0);
                    }
                }
            }

            _current = SelectNextActor();
        }

        /// <summary>
        /// Battle time in integer ticks: the moment <see cref="CurrentUnit"/>'s turn happens. Only
        /// ever grows. 0 before anything has filled.
        /// </summary>
        public long ElapsedTicks
        {
            get { return _elapsed; }
        }

        /// <summary>
        /// <see cref="ElapsedTicks"/> in turns of a <see cref="ReferenceSpeed"/> unit, for reporting.
        /// Every decision is made on the integer ticks; this is never read back into the model.
        /// </summary>
        public double Time
        {
            get { return (double)ElapsedTicks / TicksPerTimeUnit; }
        }

        /// <summary>How many turns have been handed out so far, counting the current one.</summary>
        public int ActionCount { get; private set; }

        /// <summary>The unit whose turn it is, or <c>null</c> when no living unit remains.</summary>
        public BattleUnit CurrentUnit
        {
            get { return _current; }
        }

        /// <summary>True once every unit in the roster is defeated, so there is no turn to take.</summary>
        public bool IsComplete
        {
            get { return !HasLivingUnit(); }
        }

        /// <summary>
        /// A unit's gauge right now (it may exceed <see cref="ActionThreshold"/> while the unit waits
        /// out a zero-time tie, and the current actor still holds its full gauge until
        /// <see cref="AdvanceTurn"/>). 0 for a unit this manager does not track.
        /// </summary>
        public long GetGauge(BattleUnit unit)
        {
            return unit != null && _gauges.TryGetValue(unit, out long gauge) ? gauge : 0;
        }

        /// <summary>
        /// Ends the current unit's turn — subtracting <see cref="ActionThreshold"/> from its gauge
        /// and keeping the overflow — then advances time to the next turn and returns whose it is.
        /// Speeds are read now, after the turn, so anything the turn did to a unit's Speed already
        /// counts. Returns <c>null</c> when nothing is left alive.
        /// </summary>
        public BattleUnit AdvanceTurn()
        {
            if (_current != null)
            {
                _gauges[_current] -= ActionThreshold;
            }

            _current = SelectNextActor();
            return _current;
        }

        /// <summary>
        /// The next <paramref name="count"/> turns, starting with <see cref="CurrentUnit"/>, as they
        /// would fall if nobody's Speed changed and nobody fell — the forecast a turn-order UI
        /// shows. A pure projection: the manager's own state is untouched, and the real order can
        /// differ as soon as a buff, debuff or defeat lands. Empty when nothing is alive.
        /// </summary>
        public IReadOnlyList<BattleUnit> PredictNextActors(int count)
        {
            List<BattleUnit> forecast = new List<BattleUnit>();
            if (_current == null || count <= 0)
            {
                return forecast;
            }

            Dictionary<BattleUnit, long> gauges = new Dictionary<BattleUnit, long>(_gauges);
            BattleUnit actor = _current;
            long ignored = 0;

            while (true)
            {
                forecast.Add(actor);
                if (forecast.Count >= count)
                {
                    return forecast;
                }

                gauges[actor] -= ActionThreshold;
                actor = Step(gauges, ref ignored);
            }
        }

        /// <summary>
        /// The gauge <paramref name="unit"/> gains per tick: <see cref="FillRateForSpeed"/> of its
        /// live Speed.
        /// </summary>
        public static int FillRate(BattleUnit unit)
        {
            return FillRateForSpeed(unit.Stats.Speed);
        }

        /// <summary>
        /// <c>round(FillScale x sqrt(max(1, speed)))</c>, exactly, in integers: the integer square
        /// root of <c>speed x FillScale²</c>, rounded half up. No floating point, so every platform
        /// computes the same rate. A speed below 1 fills as Speed 1, so nothing stalls forever.
        /// Turns per unit of time therefore grow with <c>sqrt(Speed)</c>: four times the Speed is
        /// twice the turns, and +21% Speed is +10% turns. Strictly increasing in Speed across the
        /// whole plausible range (consecutive speeds differ by at least one whole rate up to Speed
        /// 2500), so comparing rates is comparing speeds.
        /// <para>
        /// Speeds up to <see cref="CachedSpeeds"/> are read from a table filled by this same
        /// computation when the type loads, since the turn order asks for every living unit's rate
        /// on every turn; the answer is identical either way.
        /// </para>
        /// </summary>
        public static int FillRateForSpeed(int speed)
        {
            if (speed < CachedSpeeds)
            {
                return CachedFillRates[speed < 1 ? 1 : speed];
            }

            return ComputeFillRate(speed);
        }

        /// <summary>Speeds below this read <see cref="FillRateForSpeed"/> from a precomputed table.</summary>
        private const int CachedSpeeds = 4096;

        private static readonly int[] CachedFillRates = BuildFillRateTable();

        private static int[] BuildFillRateTable()
        {
            int[] rates = new int[CachedSpeeds];
            for (int speed = 0; speed < CachedSpeeds; speed++)
            {
                rates[speed] = ComputeFillRate(speed);
            }

            return rates;
        }

        private static int ComputeFillRate(int speed)
        {
            long radicand = (long)(speed < 1 ? 1 : speed) * FillScale * FillScale;
            long root = IntegerSqrt(radicand);

            // sqrt(x) rounds up iff sqrt(x) >= root + 0.5, i.e. x >= root² + root + 0.25, which for
            // integers is x - root² > root. (An exact half is impossible for an integer x.)
            return (int)(radicand - (root * root) > root ? root + 1 : root);
        }

        /// <summary>
        /// <c>floor(sqrt(value))</c> for a non-negative long, by integer bisection (0 for a negative
        /// value). Integer-only so it is bit-identical on every platform.
        /// </summary>
        public static long IntegerSqrt(long value)
        {
            if (value < 2)
            {
                return value < 0 ? 0 : value;
            }

            // sqrt(long.MaxValue) < 3037000500, so the root lies in [1, 3037000499].
            long low = 1;
            long high = value < MaxLongSqrt ? value : MaxLongSqrt;
            while (low < high)
            {
                long mid = low + ((high - low + 1) / 2);
                if (mid <= value / mid)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low;
        }

        /// <summary>floor(sqrt(long.MaxValue)): the bisection's upper bound.</summary>
        private const long MaxLongSqrt = 3037000499L;

        private BattleUnit SelectNextActor()
        {
            BattleUnit actor = Step(_gauges, ref _elapsed);
            if (actor != null)
            {
                ActionCount++;
            }

            return actor;
        }

        /// <summary>
        /// One event step over <paramref name="gauges"/>: advance <paramref name="elapsed"/> to the
        /// moment the soonest living unit fills, fill every living unit by that much, and return the
        /// unit that acts (see <see cref="CompareReadiness"/>). <c>null</c> when nothing is alive.
        /// Shared by the real advance and <see cref="PredictNextActors"/> so the two cannot drift.
        /// </summary>
        private BattleUnit Step(Dictionary<BattleUnit, long> gauges, ref long elapsed)
        {
            long wait = long.MaxValue;

            for (int i = 0; i < _roster.Count; i++)
            {
                BattleUnit unit = _roster[i];
                if (unit.IsDefeated)
                {
                    continue;
                }

                long missing = ActionThreshold - gauges[unit];
                int rate = FillRate(unit);
                long ticks = missing <= 0 ? 0 : (missing + rate - 1) / rate;
                if (ticks < wait)
                {
                    wait = ticks;
                }
            }

            if (wait == long.MaxValue)
            {
                return null;
            }

            BattleUnit actor = null;
            elapsed += wait;

            for (int i = 0; i < _roster.Count; i++)
            {
                BattleUnit unit = _roster[i];
                if (unit.IsDefeated)
                {
                    continue;
                }

                gauges[unit] += FillRate(unit) * wait;

                if (gauges[unit] >= ActionThreshold && (actor == null || CompareReadiness(unit, actor, gauges) < 0))
                {
                    actor = unit;
                }
            }

            return actor;
        }

        /// <summary>
        /// Readiness order among full units: highest gauge (most overflow) first, then the higher
        /// fill rate (the higher Speed; see <see cref="FillRateForSpeed"/>), ties broken by <see cref="BattleUnitOrder.CompareById"/>.
        /// <para>
        /// The id tie-break is chosen over "stable by input order" deliberately: input order is a
        /// property of however the roster happened to be assembled. See
        /// <see cref="BattleUnitOrder"/> for the full rationale, which
        /// <see cref="SkillTargetResolver"/> shares.
        /// </para>
        /// </summary>
        private static int CompareReadiness(BattleUnit a, BattleUnit b, Dictionary<BattleUnit, long> gauges)
        {
            int byGauge = gauges[b].CompareTo(gauges[a]);
            if (byGauge != 0)
            {
                return byGauge;
            }

            int bySpeed = FillRate(b).CompareTo(FillRate(a));
            if (bySpeed != 0)
            {
                return bySpeed;
            }

            return BattleUnitOrder.CompareById(a, b);
        }

        private bool HasLivingUnit()
        {
            for (int i = 0; i < _roster.Count; i++)
            {
                if (!_roster[i].IsDefeated)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
