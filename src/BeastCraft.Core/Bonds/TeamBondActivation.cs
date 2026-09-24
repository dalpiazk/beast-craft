using System.Collections.Generic;
using BeastCraft.Battle;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// One team bond applied at battle start: which bond at which tier, and who it landed on (each
    /// recipient applied the tier's effects to itself). Recorded on
    /// <see cref="BattleResult.BondActivations"/>, in application order. Purely a record.
    /// </summary>
    public sealed class TeamBondActivation
    {
        public TeamBondActivation(ActiveTeamBond activeBond, IReadOnlyList<BattleUnit> recipients)
        {
            ActiveBond = activeBond;
            Recipients = recipients ?? new List<BattleUnit>();
        }

        /// <summary>The resolved bond: the bond, its tier, its count and its members.</summary>
        public ActiveTeamBond ActiveBond { get; }

        public TeamBondSO Bond
        {
            get { return ActiveBond == null ? null : ActiveBond.Bond; }
        }

        /// <summary>The tier applied, 1-based.</summary>
        public int Tier
        {
            get { return ActiveBond == null ? 0 : ActiveBond.Tier; }
        }

        /// <summary>How many stacks of the tier's magnitudes were applied (1 for a tiered bond).</summary>
        public int Stacks
        {
            get { return ActiveBond == null ? 0 : ActiveBond.Stacks; }
        }

        /// <summary>The units the tier's effects were applied to (and by), in team order. Never null.</summary>
        public IReadOnlyList<BattleUnit> Recipients { get; }
    }
}
