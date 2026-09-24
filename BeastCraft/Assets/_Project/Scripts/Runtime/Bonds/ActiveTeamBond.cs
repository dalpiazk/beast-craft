using System.Collections.Generic;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// One bond a team has activated: which bond, the tier it reached, the count that reached it,
    /// and which team members are its members. The output of <see cref="TeamBondResolver"/>.
    /// </summary>
    public sealed class ActiveTeamBond
    {
        public ActiveTeamBond(TeamBondSO bond, int tier, int count, IReadOnlyList<int> members)
        {
            Bond = bond;
            Tier = tier;
            Count = count;
            Members = members ?? new int[0];
        }

        public TeamBondSO Bond { get; }

        /// <summary>The tier reached, 1-based (1 = the bond's first tier).</summary>
        public int Tier { get; }

        /// <summary>The condition count that reached it (see <see cref="TeamBondCondition"/>).</summary>
        public int Count { get; }

        /// <summary>The bond's members, as ascending indices into the team the resolver was given.</summary>
        public IReadOnlyList<int> Members { get; }

        /// <summary>The reached tier's definition (<see cref="TeamBondSO.Tiers"/> at <see cref="Tier"/> - 1).</summary>
        public TeamBondTier TierDefinition
        {
            get { return Bond.Tiers[Tier - 1]; }
        }
    }
}
