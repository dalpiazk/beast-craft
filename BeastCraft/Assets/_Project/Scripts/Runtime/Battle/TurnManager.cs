using System.Collections.Generic;

namespace BeastCraft.Battle
{
    /// <summary>
    /// The speed-stat initiative queue: every combatant is sorted into one order and acts
    /// individually on its own turn, one unit at a time.
    /// <para>
    /// Scaffolding only. This owns turn sequencing and nothing else — no action economy, no AI, no
    /// damage or skill resolution, and no buff logic. It reads <c>Stats.Speed</c> at sort time and
    /// exposes <see cref="RefreshOrder"/> as the hook a later buff/debuff system calls when a
    /// unit's speed changes mid-battle.
    /// </para>
    /// </summary>
    public class TurnManager
    {
        private readonly List<BattleUnit> _roster;
        private readonly List<BattleUnit> _order;
        private int _index;

        /// <summary>
        /// Builds the initiative order from a roster and opens round 1 on the fastest living unit.
        /// Null entries are dropped; the roster itself is copied, so the caller's collection is not
        /// retained.
        /// </summary>
        public TurnManager(IEnumerable<BattleUnit> units)
        {
            _roster = new List<BattleUnit>();

            if (units != null)
            {
                foreach (BattleUnit unit in units)
                {
                    if (unit != null)
                    {
                        _roster.Add(unit);
                    }
                }
            }

            _order = new List<BattleUnit>();
            _index = -1;
            Round = 0;

            StartNextRound();
        }

        /// <summary>The round currently being played, counting from 1.</summary>
        public int Round { get; private set; }

        /// <summary>
        /// The initiative order for the current round, fastest first. Read-only: mutate the roster
        /// through the units themselves and call <see cref="RefreshOrder"/>.
        /// </summary>
        public IReadOnlyList<BattleUnit> Order
        {
            get { return _order; }
        }

        /// <summary>The unit whose turn it is, or <c>null</c> when no living unit remains.</summary>
        public BattleUnit CurrentUnit
        {
            get
            {
                return _index >= 0 && _index < _order.Count ? _order[_index] : null;
            }
        }

        /// <summary>True once every unit in the roster is defeated, so there is no turn to take.</summary>
        public bool IsComplete
        {
            get { return !HasLivingUnit(); }
        }

        /// <summary>
        /// Hands the turn to the next living unit in the order. When the order is exhausted this
        /// opens a new round (which re-sorts, see <see cref="StartNextRound"/>) and returns its
        /// first unit. Defeated units are skipped. Returns <c>null</c> when nothing is left alive.
        /// </summary>
        public BattleUnit AdvanceTurn()
        {
            if (!HasLivingUnit())
            {
                _index = -1;
                return null;
            }

            for (int candidate = _index + 1; candidate < _order.Count; candidate++)
            {
                if (!_order[candidate].IsDefeated)
                {
                    _index = candidate;
                    return _order[candidate];
                }
            }

            StartNextRound();
            return CurrentUnit;
        }

        /// <summary>
        /// Rebuilds and re-sorts the initiative order from the current roster, keeping the turn on
        /// whichever unit is acting right now. Call this after anything changes a unit's speed —
        /// that logic does not exist yet, so for now this is purely the hook it will use.
        /// </summary>
        public void RefreshOrder()
        {
            BattleUnit acting = CurrentUnit;

            _order.Clear();
            _order.AddRange(_roster);
            _order.Sort(CompareInitiative);

            _index = acting == null ? -1 : _order.IndexOf(acting);
        }

        /// <summary>
        /// Initiative comparison: fastest first, ties broken by ordinal comparison of
        /// <see cref="BattleUnit.Id"/>.
        /// <para>
        /// The id tie-break is chosen over "stable by input order" deliberately.
        /// <see cref="List{T}.Sort(System.Comparison{T})"/> is an unstable sort, so input order is
        /// not actually preserved for equal keys without extra bookkeeping — and more importantly,
        /// input order is a property of however the roster happened to be assembled. Keying on the
        /// id instead makes the order a pure function of the roster's contents, so re-sorting
        /// mid-battle cannot silently reshuffle equal-speed units, and a client and a
        /// server-authoritative re-simulation of the same battle agree.
        /// </para>
        /// </summary>
        private static int CompareInitiative(BattleUnit a, BattleUnit b)
        {
            int bySpeed = b.Stats.Speed.CompareTo(a.Stats.Speed);
            if (bySpeed != 0)
            {
                return bySpeed;
            }

            return string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// Opens the next round: re-sorts (speed may have changed during the round that just
        /// ended) and parks the turn on the first living unit in the new order.
        /// </summary>
        private void StartNextRound()
        {
            Round++;
            RefreshOrder();

            _index = -1;
            for (int candidate = 0; candidate < _order.Count; candidate++)
            {
                if (!_order[candidate].IsDefeated)
                {
                    _index = candidate;
                    return;
                }
            }
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
