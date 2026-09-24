using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using UnityEngine;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// A team's active bonds for one battle, bound to the battle units they apply to. Built fresh
    /// per battle (<see cref="For"/>) and handed to
    /// <see cref="BattleTurnExecutor.RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, System.Random, BattleUnit, PassiveLoadout, TeamBondLoadout, int)"/>
    /// or <see cref="BattleTurnExecutor.BeginBattle(IEnumerable{BattleUnit}, HexGrid, System.Random, BattleUnit, PassiveLoadout, TeamBondLoadout, out IReadOnlyList{TeamBondActivation})"/>,
    /// which apply its battle-start effects once, as the battle begins, before any avatar passive,
    /// and then run its behaviour bonds' reactions on every turn (see the other half of this class,
    /// <c>TeamBondLoadout.Reactions.cs</c>, and <see cref="BondReaction"/>). See the battle-system
    /// design doc, "Team bonds".
    /// <para>
    /// <strong>Application.</strong> Bonds apply in the resolver's order (the library's order). For
    /// each, every living recipient (the bond's members; the whole team for
    /// <see cref="TeamBondScope.Team"/>; the teammates that are not members for
    /// <see cref="TeamBondScope.Others"/>), in team order, applies the reached tier's effects to
    /// itself: <see cref="SkillEffectApplier"/> with the recipient as both caster and only target,
    /// through a private <c>Self</c>-shaped carrier <see cref="SkillSO"/> at level 1 (bonds do not
    /// level), so a shield scales with the recipient's own <c>Defense</c>, a percent buff with its
    /// own stat, and a <c>DurationTurns</c> 0 stat change lasts the battle. A bond's effects draw
    /// from the battle's rng only as any effect would (a <c>Chance</c> below 100); the authored
    /// bonds draw nothing.
    /// </para>
    /// <para>
    /// <strong>Stacks.</strong> A scaling bond (<see cref="TeamBondSO.PerCount"/>) applies its tier
    /// at <see cref="ActiveTeamBond.Stacks"/>: every effect's <c>Magnitude</c> multiplied by the
    /// stacks, as one application (a +4% buff at three stacks is one +12% buff, not three
    /// compounding ones).
    /// </para>
    /// <para>
    /// <strong>One carrier per tier and stack count.</strong> As with <see cref="PassiveInstance"/>,
    /// a carrier is created with <see cref="ScriptableObject.CreateInstance{T}"/> the first time a
    /// tier is applied at a stack count and cached, weakly keyed by that <see cref="TeamBondTier"/>
    /// and then by the stacks, so rebuilding the loadout every battle does not pile up native
    /// objects. At one stack the carrier shares the tier's effect list; above one it carries
    /// <em>clones</em> of the tier's effects with the magnitude scaled, so the authored effects are
    /// never modified.
    /// </para>
    /// </summary>
    public sealed partial class TeamBondLoadout
    {
        private static readonly object CarrierLock = new object();
        private static readonly ConditionalWeakTable<TeamBondTier, Dictionary<int, SkillSO>> Carriers = new ConditionalWeakTable<TeamBondTier, Dictionary<int, SkillSO>>();

        private readonly List<ActiveTeamBond> _bonds = new List<ActiveTeamBond>();
        private readonly List<BattleUnit> _team = new List<BattleUnit>();

        /// <summary>
        /// <paramref name="bonds"/> (resolved against a team, in order; nulls skipped) bound to
        /// <paramref name="team"/>, the battle units of that team in the same order, so a bond's
        /// member index <c>i</c> is <c>team[i]</c>.
        /// </summary>
        public TeamBondLoadout(IEnumerable<ActiveTeamBond> bonds, IEnumerable<BattleUnit> team)
        {
            if (bonds != null)
            {
                foreach (ActiveTeamBond bond in bonds)
                {
                    if (bond != null && bond.Bond != null && bond.Tier > 0 && bond.Bond.Tiers != null && bond.Tier <= bond.Bond.Tiers.Count)
                    {
                        _bonds.Add(bond);
                    }
                }
            }

            if (team != null)
            {
                _team.AddRange(team);
            }

            InitReactionState();
        }

        /// <summary>
        /// Resolves <paramref name="bonds"/> for a team (<paramref name="members"/>, parallel to
        /// <paramref name="team"/>) and binds the result to <paramref name="team"/>. Never null.
        /// </summary>
        public static TeamBondLoadout For(IEnumerable<TeamBondSO> bonds, IReadOnlyList<TeamBondMember> members, IEnumerable<BattleUnit> team)
        {
            return new TeamBondLoadout(TeamBondResolver.Resolve(bonds, members), team);
        }

        /// <summary>The active bonds, in application order. Read-only.</summary>
        public IReadOnlyList<ActiveTeamBond> Bonds
        {
            get { return _bonds; }
        }

        /// <summary>The team the bonds apply to, in team order. Read-only.</summary>
        public IReadOnlyList<BattleUnit> Team
        {
            get { return _team; }
        }

        /// <summary>Whether it has been applied. A loadout serves one battle; later applications do nothing.</summary>
        public bool HasApplied { get; private set; }

        /// <summary>Applies every bond once (see the class remarks) and returns what was applied, in order.</summary>
        internal List<TeamBondActivation> Apply(HexGrid grid, System.Random rng)
        {
            List<TeamBondActivation> applied = new List<TeamBondActivation>();

            if (HasApplied)
            {
                return applied;
            }

            HasApplied = true;
            SyncTeamDefeated();

            foreach (ActiveTeamBond bond in _bonds)
            {
                List<BattleUnit> recipients = Recipients(bond);

                if (recipients.Count == 0)
                {
                    continue;
                }

                SkillSO carrier = CarrierFor(bond.TierDefinition, bond.Bond, bond.Stacks);

                foreach (BattleUnit recipient in recipients)
                {
                    SkillActivation activation = new SkillActivation(new SkillInstance(carrier), new[] { recipient });
                    SkillEffectApplier.Apply(activation, recipient, rng, grid);
                }

                applied.Add(new TeamBondActivation(bond, recipients));
            }

            return applied;
        }

        private List<BattleUnit> Recipients(ActiveTeamBond bond)
        {
            List<BattleUnit> recipients = new List<BattleUnit>();

            if (bond.Bond.Scope == TeamBondScope.Team || bond.Bond.Scope == TeamBondScope.Others)
            {
                bool othersOnly = bond.Bond.Scope == TeamBondScope.Others;
                for (int i = 0; i < _team.Count; i++)
                {
                    BattleUnit unit = _team[i];
                    if (unit != null && !unit.IsDefeated && !(othersOnly && IsMember(bond, i)))
                    {
                        recipients.Add(unit);
                    }
                }

                return recipients;
            }

            foreach (int index in bond.Members)
            {
                if (index >= 0 && index < _team.Count && _team[index] != null && !_team[index].IsDefeated)
                {
                    recipients.Add(_team[index]);
                }
            }

            return recipients;
        }

        private static bool IsMember(ActiveTeamBond bond, int index)
        {
            foreach (int member in bond.Members)
            {
                if (member == index)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The cached carrier for <paramref name="tier"/> at <paramref name="stacks"/> (see the
        /// class remarks): the tier's own effect list at one stack, scaled clones above.
        /// </summary>
        public static SkillSO CarrierFor(TeamBondTier tier, TeamBondSO bond, int stacks)
        {
            if (stacks < 1)
            {
                stacks = 1;
            }

            lock (CarrierLock)
            {
                Dictionary<int, SkillSO> byStacks;
                if (!Carriers.TryGetValue(tier, out byStacks))
                {
                    byStacks = new Dictionary<int, SkillSO>();
                    Carriers.Add(tier, byStacks);
                }

                SkillSO carrier;
                if (byStacks.TryGetValue(stacks, out carrier))
                {
                    // Unity's overloaded == reports a destroyed native object as null.
                    if (carrier != null)
                    {
                        return carrier;
                    }

                    byStacks.Remove(stacks);
                }

                carrier = ScriptableObject.CreateInstance<SkillSO>();
                carrier.name = bond.name;
                carrier.SkillId = bond.BondId;
                carrier.DisplayName = bond.DisplayName;
                carrier.Effects = stacks == 1 ? tier.Effects ?? new List<SkillEffect>() : ScaledEffects(tier.Effects, stacks);
                carrier.TargetShape = SkillTargetShape.Self;
                carrier.TargetSide = SkillTargetSide.Ally;
                carrier.Range = 0;
                byStacks.Add(stacks, carrier);
                return carrier;
            }
        }

        /// <summary>Clones of <paramref name="effects"/> with each <c>Magnitude</c> x <paramref name="stacks"/>; the originals are untouched.</summary>
        private static List<SkillEffect> ScaledEffects(List<SkillEffect> effects, int stacks)
        {
            List<SkillEffect> scaled = new List<SkillEffect>();
            if (effects == null)
            {
                return scaled;
            }

            foreach (SkillEffect effect in effects)
            {
                if (effect == null)
                {
                    continue;
                }

                scaled.Add(new SkillEffect
                {
                    EffectType = effect.EffectType,
                    AffectedStat = effect.AffectedStat,
                    Magnitude = effect.Magnitude * stacks,
                    DurationTurns = effect.DurationTurns,
                    Status = effect.Status,
                    Chance = effect.Chance,
                    MaxStacks = effect.MaxStacks,
                    IsPercent = effect.IsPercent,
                    HitCount = effect.HitCount,
                    ExecuteBonusPercent = effect.ExecuteBonusPercent,
                });
            }

            return scaled;
        }
    }
}
