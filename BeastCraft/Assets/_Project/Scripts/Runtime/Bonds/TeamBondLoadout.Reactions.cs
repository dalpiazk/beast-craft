using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// The in-battle half of <see cref="TeamBondLoadout"/>: the behaviour bonds' reactions (see
    /// <see cref="BondReaction"/> and the battle-system design doc, "Team bonds"). The executor
    /// calls these hooks on every turn once the loadout has been applied at battle start; a loadout
    /// with no reacting tier (<see cref="HasReactions"/> false) is never called, so a battle-start
    /// bond changes nothing about a battle's turns or its random draws.
    /// <para>
    /// <strong>When each trigger is checked.</strong>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="BondTrigger.EnemyTargetsAlly"/>: an enemy <c>SingleTarget</c> skill has resolved
    /// to exactly one of the team's beasts, before it lands (<see cref="RedirectTargets"/>).
    /// </description></item>
    /// <item><description>
    /// After every skill that fires (the avatar's passives have had their turn first): per damage
    /// hit in hit order, <see cref="BondTrigger.MemberHit"/>, <see cref="BondTrigger.MemberCrit"/>
    /// and <see cref="BondTrigger.AllyCrit"/> for a skill of the team's beast on an enemy, or
    /// <see cref="BondTrigger.AllyHitByEnemy"/> once per beast an enemy <c>SingleTarget</c> skill
    /// hit; then <see cref="BondTrigger.AllyDefeated"/> for every beast newly fallen (team order);
    /// then <see cref="BondTrigger.AllyBelowHpPercent"/> for every living beast (team order). The
    /// last two also run after damage-over-time opens a turn and after each avatar activation.
    /// </description></item>
    /// <item><description>
    /// As a beast of the team's turn opens, before its statuses tick
    /// (<see cref="BeforeAllyTurn"/>): its reaction cooldowns count down one, then
    /// <see cref="BondTrigger.AllyTurnStartAfflicted"/> if it is stunned or burning.
    /// </description></item>
    /// </list>
    /// Within one event the bonds are tried in the loadout's (the library's) order.
    /// </para>
    /// <para>
    /// <strong>Casting.</strong> A reaction's effects are cast by the reactor through
    /// <see cref="SkillEffectApplier"/> with a private carrier <see cref="SkillSO"/> at level 1,
    /// cached per (reaction, element, category): its element is the reactor's first element (or
    /// <see cref="ReactionElementOverride"/>), its category physical when the reactor's
    /// <c>Attack</c> is at least its <c>SpecialAttack</c>. The defeated are lifted off the grid
    /// after each reaction.
    /// </para>
    /// <para>
    /// <strong>No chaining.</strong> Nothing a reaction does is fed back into any hook: its hits
    /// and crits trigger no bond and no passive, and a unit it defeats is noted silently by the
    /// avatar's passives (<see cref="PassiveLoadout"/> never fires a defeat passive for it).
    /// </para>
    /// <para>
    /// <strong>Randomness.</strong> At most one draw per reaction for its <see cref="BondReaction.Chance"/>
    /// (none at 100), taken after every deterministic check, plus whatever its effects draw as any
    /// skill's would.
    /// </para>
    /// </summary>
    public sealed partial class TeamBondLoadout
    {
        private static readonly object ReactionCarrierLock = new object();
        private static readonly ConditionalWeakTable<BondReaction, Dictionary<int, SkillSO>> ReactionCarriers =
            new ConditionalWeakTable<BondReaction, Dictionary<int, SkillSO>>();

        // Per-battle reaction state, per bond index (and team index where noted).
        private int[][] _reactionCooldowns;
        private int[][] _reactionsByMember;
        private Dictionary<BattleUnit, int>[] _reactionsByTrigger;
        private int[] _reactionsTotal;
        private HashSet<BattleUnit>[] _belowThreshold;
        private HashSet<BattleUnit> _seenDefeated;
        private bool[][] _isMember;

        /// <summary>Whether any active bond's reached tier reacts in battle (<see cref="TeamBondTier.HasReaction"/>).</summary>
        public bool HasReactions
        {
            get
            {
                foreach (ActiveTeamBond bond in _bonds)
                {
                    if (bond.TierDefinition != null && bond.TierDefinition.HasReaction)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// When set, every reaction's carrier takes this element instead of the reactor's first
        /// element (the balance simulator's element-neutral mode). Null, the default, in the game.
        /// </summary>
        public Element? ReactionElementOverride { get; set; }

        private void InitReactionState()
        {
            int count = _bonds.Count;
            _reactionCooldowns = new int[count][];
            _reactionsByMember = new int[count][];
            _reactionsByTrigger = new Dictionary<BattleUnit, int>[count];
            _reactionsTotal = new int[count];
            _belowThreshold = new HashSet<BattleUnit>[count];
            _isMember = new bool[count][];
            _seenDefeated = new HashSet<BattleUnit>();

            for (int b = 0; b < count; b++)
            {
                _reactionCooldowns[b] = new int[_team.Count];
                _reactionsByMember[b] = new int[_team.Count];
                _reactionsByTrigger[b] = new Dictionary<BattleUnit, int>();
                _belowThreshold[b] = new HashSet<BattleUnit>();
                _isMember[b] = new bool[_team.Count];

                foreach (int index in _bonds[b].Members)
                {
                    if (index >= 0 && index < _team.Count)
                    {
                        _isMember[b][index] = true;
                    }
                }
            }
        }

        /// <summary>Notes the team's already-defeated beasts at battle start: they never trigger.</summary>
        private void SyncTeamDefeated()
        {
            foreach (BattleUnit unit in _team)
            {
                if (unit != null && unit.IsDefeated)
                {
                    _seenDefeated.Add(unit);
                }
            }
        }

        /// <summary>
        /// <see cref="BondTrigger.EnemyTargetsAlly"/>: an enemy <paramref name="caster"/>'s
        /// <paramref name="skill"/> resolved to <paramref name="targets"/>. When it is a
        /// <c>SingleTarget</c> skill aimed at exactly one of the team's beasts, the first bond (in
        /// order) whose intercepting member qualifies — alive, not stunned, not the target, within
        /// <see cref="BondReaction.Range"/> of it, off cooldown, under its caps, and past its chance
        /// roll — applies its effects to that member and takes the hit: the returned list is the
        /// member alone. Otherwise <paramref name="targets"/> unchanged. No range recheck: an
        /// adjacent guardian is always a legal target of the swing that was already coming.
        /// </summary>
        internal IReadOnlyList<BattleUnit> RedirectTargets(SkillSO skill, BattleUnit caster, IReadOnlyList<BattleUnit> targets, BondReactionContext context)
        {
            if (!HasApplied || skill == null || caster == null || targets == null || targets.Count != 1 || skill.TargetShape != SkillTargetShape.SingleTarget)
            {
                return targets;
            }

            BattleUnit aimed = targets[0];
            int aimedIndex = TeamIndex(aimed);

            if (aimedIndex < 0 || caster.Team == aimed.Team)
            {
                return targets;
            }

            for (int b = 0; b < _bonds.Count; b++)
            {
                BondReaction reaction = ReactionOf(b);

                if (reaction == null || reaction.Trigger != BondTrigger.EnemyTargetsAlly || reaction.Action != BondAction.Intercept ||
                    !PassesFilter(b, reaction, aimedIndex) || !UnderBattleCaps(b, reaction, aimed))
                {
                    continue;
                }

                foreach (int index in ReactorOrder(b, reaction))
                {
                    BattleUnit guardian = _team[index];

                    if (guardian == null || guardian.IsDefeated || StatusEffects.IsStunned(guardian) || guardian == aimed ||
                        (reaction.Range > 0 && FootprintMath.UnitDistance(guardian, aimed) > reaction.Range) ||
                        !OffCooldownAndUnderCap(b, reaction, index))
                    {
                        continue;
                    }

                    if (!SkillEffectApplier.RollChance(ClampChance(reaction.Chance), context.Rng))
                    {
                        // One reactor per bond per event: a failed roll is this bond's answer.
                        break;
                    }

                    Fire(b, reaction, BondTrigger.EnemyTargetsAlly, index, aimed, new[] { guardian }, aimed, context);
                    return new[] { guardian };
                }
            }

            return targets;
        }

        /// <summary>
        /// The after-damage hook (see the class remarks): the hit and crit triggers for
        /// <paramref name="activation"/>, fired by <paramref name="turnUnit"/> on its turn (null for
        /// damage-over-time, the avatar's own casts and anything else that is no beast's skill), then
        /// the defeat and threshold triggers.
        /// </summary>
        internal void AfterApplication(BattleUnit turnUnit, SkillActivation activation, BondReactionContext context)
        {
            if (!HasApplied)
            {
                return;
            }

            if (activation != null && turnUnit != null && !turnUnit.IsDefeated)
            {
                if (TeamIndex(turnUnit) >= 0)
                {
                    OnTeamHits(turnUnit, activation, context);
                }
                else if (activation.Skill != null && activation.Skill.TargetShape == SkillTargetShape.SingleTarget)
                {
                    OnEnemyHits(turnUnit, activation, context);
                }
            }

            for (int i = 0; i < _team.Count; i++)
            {
                BattleUnit fallen = _team[i];

                if (fallen == null || !fallen.IsDefeated || !_seenDefeated.Add(fallen))
                {
                    continue;
                }

                for (int b = 0; b < _bonds.Count; b++)
                {
                    TryReact(b, BondTrigger.AllyDefeated, fallen, null, null, context);
                }
            }

            CheckThresholds(context);
        }

        /// <summary>
        /// A beast of the team is about to open its turn, before its statuses tick: its reaction
        /// cooldowns count down by one, then <see cref="BondTrigger.AllyTurnStartAfflicted"/> when it
        /// is stunned or carrying damage-over-time. Any other unit is ignored.
        /// </summary>
        internal void BeforeAllyTurn(BattleUnit unit, BondReactionContext context)
        {
            int index = TeamIndex(unit);

            if (!HasApplied || index < 0 || unit.IsDefeated)
            {
                return;
            }

            for (int b = 0; b < _bonds.Count; b++)
            {
                if (_reactionCooldowns[b][index] > 0)
                {
                    _reactionCooldowns[b][index]--;
                }
            }

            if (!StatusEffects.IsAfflicted(unit))
            {
                return;
            }

            for (int b = 0; b < _bonds.Count; b++)
            {
                TryReact(b, BondTrigger.AllyTurnStartAfflicted, unit, null, null, context);
            }
        }

        private void OnTeamHits(BattleUnit hitter, SkillActivation activation, BondReactionContext context)
        {
            IReadOnlyList<DamageHit> hits = activation.Hits;

            for (int h = 0; h < hits.Count; h++)
            {
                BattleUnit target = hits[h].Target;

                if (target == null || target.Team == hitter.Team)
                {
                    continue;
                }

                bool crit = hits[h].Roll.IsCrit;

                for (int b = 0; b < _bonds.Count; b++)
                {
                    BondReaction reaction = ReactionOf(b);

                    if (reaction == null)
                    {
                        continue;
                    }

                    if (reaction.Trigger == BondTrigger.MemberHit || (crit && (reaction.Trigger == BondTrigger.MemberCrit || reaction.Trigger == BondTrigger.AllyCrit)))
                    {
                        TryReact(b, reaction.Trigger, hitter, target, null, context);
                    }
                }
            }
        }

        private void OnEnemyHits(BattleUnit attacker, SkillActivation activation, BondReactionContext context)
        {
            IReadOnlyList<DamageHit> hits = activation.Hits;
            List<BattleUnit> seen = null;

            for (int h = 0; h < hits.Count; h++)
            {
                BattleUnit ally = hits[h].Target;

                if (ally == null || TeamIndex(ally) < 0)
                {
                    continue;
                }

                seen = seen ?? new List<BattleUnit>();

                if (seen.Contains(ally))
                {
                    continue;
                }

                seen.Add(ally);

                for (int b = 0; b < _bonds.Count; b++)
                {
                    TryReact(b, BondTrigger.AllyHitByEnemy, ally, null, attacker, context);
                }
            }
        }

        private void CheckThresholds(BondReactionContext context)
        {
            for (int i = 0; i < _team.Count; i++)
            {
                BattleUnit ally = _team[i];

                if (ally == null || ally.IsDefeated)
                {
                    continue;
                }

                for (int b = 0; b < _bonds.Count; b++)
                {
                    BondReaction reaction = ReactionOf(b);

                    if (reaction == null || reaction.Trigger != BondTrigger.AllyBelowHpPercent)
                    {
                        continue;
                    }

                    if ((long)ally.CurrentHp * 100 >= (long)reaction.HpThresholdPercent * ally.Stats.Hp)
                    {
                        _belowThreshold[b].Remove(ally);
                    }
                    else if (_belowThreshold[b].Add(ally))
                    {
                        TryReact(b, BondTrigger.AllyBelowHpPercent, ally, null, null, context);
                    }
                }
            }
        }

        /// <summary>
        /// One bond's answer to one event: the first member (in the reaction's order) that is
        /// alive, not stunned, allowed to react to this trigger, has somebody to act on within range,
        /// is off cooldown and under its caps; then the chance roll; then the effects. Returns
        /// whether it fired.
        /// </summary>
        private bool TryReact(int b, BondTrigger trigger, BattleUnit triggeringUnit, BattleUnit triggerTarget, BattleUnit attacker, BondReactionContext context)
        {
            BondReaction reaction = ReactionOf(b);

            if (reaction == null || reaction.Trigger != trigger || reaction.Action != BondAction.Apply)
            {
                return false;
            }

            int triggeringIndex = TeamIndex(triggeringUnit);

            if (IsAllyTrigger(trigger) && !PassesFilter(b, reaction, triggeringIndex))
            {
                return false;
            }

            if (!UnderBattleCaps(b, reaction, triggeringUnit))
            {
                return false;
            }

            bool selfOnly = trigger == BondTrigger.MemberHit || trigger == BondTrigger.MemberCrit;
            bool othersOnly = trigger == BondTrigger.AllyCrit || trigger == BondTrigger.AllyHitByEnemy;

            foreach (int index in ReactorOrder(b, reaction))
            {
                BattleUnit reactor = _team[index];

                if (reactor == null || reactor.IsDefeated || StatusEffects.IsStunned(reactor) || (selfOnly && reactor != triggeringUnit) ||
                    (othersOnly && reactor == triggeringUnit))
                {
                    continue;
                }

                List<BattleUnit> targets = ReactionTargets(reaction, reactor, triggeringUnit, triggerTarget, attacker, context);

                if (targets.Count == 0 || !OffCooldownAndUnderCap(b, reaction, index))
                {
                    continue;
                }

                if (!SkillEffectApplier.RollChance(ClampChance(reaction.Chance), context.Rng))
                {
                    return false;
                }

                Fire(b, reaction, trigger, index, triggeringUnit, targets, null, context);
                return true;
            }

            return false;
        }

        /// <summary>Applies a reaction cast by the member at <paramref name="reactorIndex"/>, counts it, and records it.</summary>
        private void Fire(int b, BondReaction reaction, BondTrigger trigger, int reactorIndex, BattleUnit triggeringUnit, IReadOnlyList<BattleUnit> targets,
                          BattleUnit interceptedFrom, BondReactionContext context)
        {
            BattleUnit reactor = _team[reactorIndex];
            SkillActivation activation = new SkillActivation(new SkillInstance(ReactionCarrierFor(reaction, _bonds[b].Bond, reactor)), targets);
            SkillEffectApplier.Apply(activation, reactor, context.Rng, context.Grid);
            BattleTurnExecutor.LiftDefeated(context.AllUnits, context.Grid);

            _reactionCooldowns[b][reactorIndex] = reaction.Cooldown < 0 ? 0 : reaction.Cooldown;
            _reactionsByMember[b][reactorIndex]++;
            _reactionsTotal[b]++;

            if (triggeringUnit != null)
            {
                _reactionsByTrigger[b].TryGetValue(triggeringUnit, out int fired);
                _reactionsByTrigger[b][triggeringUnit] = fired + 1;
            }

            // No chaining: whatever this defeated never fires an avatar passive.
            context.Passives?.SyncDefeatedSilently(context.AllUnits);
            context.Sink?.Add(new BondReactionRecord(_bonds[b], trigger, reactor, triggeringUnit, activation, interceptedFrom));
        }

        /// <summary>
        /// Who the effects land on (see <see cref="BondReactionTarget"/>), living units only, with the
        /// reaction's range applied. Empty when there is nobody: the reactor does not qualify.
        /// </summary>
        private List<BattleUnit> ReactionTargets(BondReaction reaction, BattleUnit reactor, BattleUnit triggeringUnit, BattleUnit triggerTarget, BattleUnit attacker,
                                                 BondReactionContext context)
        {
            List<BattleUnit> targets = new List<BattleUnit>();

            switch (reaction.Target)
            {
                case BondReactionTarget.Self:
                    targets.Add(reactor);
                    break;

                case BondReactionTarget.TriggeringAlly:
                    AddInRange(targets, reaction, reactor, triggeringUnit);
                    break;

                case BondReactionTarget.TriggerTarget:
                    AddInRange(targets, reaction, reactor, triggerTarget);
                    break;

                case BondReactionTarget.Attacker:
                    AddInRange(targets, reaction, reactor, attacker);
                    break;

                case BondReactionTarget.EnemiesNearReactor:
                    if (context.AllUnits != null && reaction.Range > 0)
                    {
                        foreach (BattleUnit unit in context.AllUnits)
                        {
                            if (unit != null && !unit.IsDefeated && unit.Team != reactor.Team && FootprintMath.UnitDistance(reactor, unit) <= reaction.Range)
                            {
                                targets.Add(unit);
                            }
                        }
                    }

                    break;

                case BondReactionTarget.Team:
                    foreach (BattleUnit unit in _team)
                    {
                        if (unit != null && !unit.IsDefeated)
                        {
                            targets.Add(unit);
                        }
                    }

                    break;
            }

            return targets;
        }

        private static void AddInRange(List<BattleUnit> targets, BondReaction reaction, BattleUnit reactor, BattleUnit unit)
        {
            if (unit != null && !unit.IsDefeated && (reaction.Range <= 0 || FootprintMath.UnitDistance(reactor, unit) <= reaction.Range))
            {
                targets.Add(unit);
            }
        }

        private BondReaction ReactionOf(int b)
        {
            TeamBondTier tier = _bonds[b].TierDefinition;
            return tier != null && tier.HasReaction ? tier.Reaction : null;
        }

        private static bool IsAllyTrigger(BondTrigger trigger)
        {
            return trigger != BondTrigger.MemberHit && trigger != BondTrigger.MemberCrit && trigger != BondTrigger.None;
        }

        private bool PassesFilter(int b, BondReaction reaction, int triggeringIndex)
        {
            switch (reaction.TriggerFilter)
            {
                case BondTriggerFilter.Members:
                    return triggeringIndex >= 0 && _isMember[b][triggeringIndex];
                case BondTriggerFilter.NonMembers:
                    return triggeringIndex >= 0 && !_isMember[b][triggeringIndex];
                default:
                    return true;
            }
        }

        private bool UnderBattleCaps(int b, BondReaction reaction, BattleUnit triggeringUnit)
        {
            if (reaction.MaxPerBattle > 0 && _reactionsTotal[b] >= reaction.MaxPerBattle)
            {
                return false;
            }

            if (reaction.MaxPerTriggerUnit > 0 && triggeringUnit != null && _reactionsByTrigger[b].TryGetValue(triggeringUnit, out int fired) &&
                fired >= reaction.MaxPerTriggerUnit)
            {
                return false;
            }

            return true;
        }

        private bool OffCooldownAndUnderCap(int b, BondReaction reaction, int index)
        {
            return _reactionCooldowns[b][index] <= 0 && (reaction.MaxPerMember <= 0 || _reactionsByMember[b][index] < reaction.MaxPerMember);
        }

        /// <summary>The bond's members in the order the reaction tries them (see <see cref="BondReactorOrder"/>).</summary>
        private List<int> ReactorOrder(int b, BondReaction reaction)
        {
            List<int> order = new List<int>();

            for (int i = 0; i < _team.Count; i++)
            {
                if (_isMember[b][i])
                {
                    order.Add(i);
                }
            }

            if (reaction.ReactorOrder == BondReactorOrder.HealthiestFirst)
            {
                // Insertion sort: stable, so equal shares keep team order. Exact integer compare.
                for (int i = 1; i < order.Count; i++)
                {
                    int current = order[i];
                    int j = i - 1;

                    while (j >= 0 && Healthier(_team[current], _team[order[j]]))
                    {
                        order[j + 1] = order[j];
                        j--;
                    }

                    order[j + 1] = current;
                }
            }

            return order;
        }

        /// <summary>Whether <paramref name="a"/>'s HP share is strictly above <paramref name="b"/>'s (null or defeated counts as none).</summary>
        private static bool Healthier(BattleUnit a, BattleUnit b)
        {
            long aHp = a == null || a.IsDefeated ? 0 : a.CurrentHp;
            long bHp = b == null || b.IsDefeated ? 0 : b.CurrentHp;
            long aMax = a == null || a.Stats.Hp <= 0 ? 1 : a.Stats.Hp;
            long bMax = b == null || b.Stats.Hp <= 0 ? 1 : b.Stats.Hp;
            return aHp * bMax > bHp * aMax;
        }

        private int TeamIndex(BattleUnit unit)
        {
            if (unit == null)
            {
                return -1;
            }

            for (int i = 0; i < _team.Count; i++)
            {
                if (_team[i] == unit)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int ClampChance(int chance)
        {
            return chance <= 0 || chance > SkillEffect.AlwaysChance ? SkillEffect.AlwaysChance : chance;
        }

        /// <summary>
        /// The cached carrier for <paramref name="reaction"/> cast by <paramref name="reactor"/>: the
        /// reaction's effect list, the reactor's first element (or <see cref="ReactionElementOverride"/>)
        /// and its stronger attacking category (physical on a tie).
        /// </summary>
        private SkillSO ReactionCarrierFor(BondReaction reaction, TeamBondSO bond, BattleUnit reactor)
        {
            Element element = ReactionElementOverride ?? (reactor.Elements.Count > 0 ? reactor.Elements[0] : Element.None);
            DamageCategory category = reactor.Stats.Attack >= reactor.Stats.SpecialAttack ? DamageCategory.Physical : DamageCategory.Special;
            int key = ((int)element * 2) + (category == DamageCategory.Physical ? 0 : 1);

            lock (ReactionCarrierLock)
            {
                Dictionary<int, SkillSO> byKey;
                if (!ReactionCarriers.TryGetValue(reaction, out byKey))
                {
                    byKey = new Dictionary<int, SkillSO>();
                    ReactionCarriers.Add(reaction, byKey);
                }

                SkillSO carrier;
                if (byKey.TryGetValue(key, out carrier))
                {
                    // Unity's overloaded == reports a destroyed native object as null.
                    if (carrier != null)
                    {
                        return carrier;
                    }

                    byKey.Remove(key);
                }

                carrier = ScriptableObject.CreateInstance<SkillSO>();
                carrier.name = bond == null ? "bond_reaction" : bond.name;
                carrier.SkillId = bond == null ? null : bond.BondId;
                carrier.DisplayName = bond == null ? null : bond.DisplayName;
                carrier.Effects = reaction.Effects ?? new List<SkillEffect>();
                carrier.TargetShape = SkillTargetShape.SingleTarget;
                carrier.TargetSide = SkillTargetSide.Enemy;
                carrier.Range = 0;
                carrier.Element = element;
                carrier.Category = category;
                byKey.Add(key, carrier);
                return carrier;
            }
        }
    }

    /// <summary>
    /// The live context a bond reaction runs in: the roster, the board, the battle's rng, where
    /// firings are recorded, and the avatar's passives (so a unit a reaction defeats is noted
    /// silently). Built by the executor per turn.
    /// </summary>
    internal sealed class BondReactionContext
    {
        public BondReactionContext(IEnumerable<BattleUnit> allUnits, HexGrid grid, System.Random rng, List<BondReactionRecord> sink, PassiveLoadout passives)
        {
            AllUnits = allUnits;
            Grid = grid;
            Rng = rng;
            Sink = sink;
            Passives = passives;
        }

        public IEnumerable<BattleUnit> AllUnits { get; }

        public HexGrid Grid { get; }

        public System.Random Rng { get; }

        public List<BondReactionRecord> Sink { get; }

        public PassiveLoadout Passives { get; }
    }
}
