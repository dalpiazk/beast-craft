using System.Collections.Generic;
using BeastCraft.Battle;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// One bond reaction that fired: which bond, who reacted, what it was about, and the
    /// <see cref="SkillActivation"/> its effects were applied through. Recorded on
    /// <see cref="BattleTurnResult.BondReactions"/>, in the order they fired. Only firings are
    /// recorded (a blocked reaction or a failed roll leaves nothing). Purely a record.
    /// </summary>
    public sealed class BondReactionRecord
    {
        private static readonly BattleUnit[] NoTargets = new BattleUnit[0];

        public BondReactionRecord(ActiveTeamBond bond, BondTrigger trigger, BattleUnit reactor, BattleUnit triggeringUnit, SkillActivation activation,
                                  BattleUnit interceptedFrom)
        {
            ActiveBond = bond;
            Trigger = trigger;
            Reactor = reactor;
            TriggeringUnit = triggeringUnit;
            Activation = activation;
            InterceptedFrom = interceptedFrom;
        }

        /// <summary>The resolved bond (bond, tier, members).</summary>
        public ActiveTeamBond ActiveBond { get; }

        /// <summary>The bond (<see cref="ActiveTeamBond.Bond"/>).</summary>
        public TeamBondSO Bond
        {
            get { return ActiveBond == null ? null : ActiveBond.Bond; }
        }

        /// <summary>What fired it.</summary>
        public BondTrigger Trigger { get; }

        /// <summary>The member that reacted (the caster of the effects).</summary>
        public BattleUnit Reactor { get; }

        /// <summary>The unit the trigger was about: the ally hit, targeted, afflicted, critting or fallen, or the member that hit.</summary>
        public BattleUnit TriggeringUnit { get; }

        /// <summary>The reaction's effects as applied: targets and damage hits. Never null.</summary>
        public SkillActivation Activation { get; }

        /// <summary>The effects' targets. Never null.</summary>
        public IReadOnlyList<BattleUnit> Targets
        {
            get { return Activation == null ? NoTargets : Activation.Targets; }
        }

        /// <summary>For an intercept: the beast the enemy skill was aimed at (the reactor took the hit instead). Null otherwise.</summary>
        public BattleUnit InterceptedFrom { get; }
    }
}
