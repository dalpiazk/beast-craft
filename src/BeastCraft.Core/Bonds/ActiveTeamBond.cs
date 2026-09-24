using System.Collections.Generic;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// One bond a team has activated: which bond, the tier it reached, the count that reached it,
    /// how many stacks it applies, and which team members are its members. The output of
    /// <see cref="TeamBondResolver"/>.
    /// </summary>
    public sealed class ActiveTeamBond
    {
        /// <summary>The stacks are the bond's own (<see cref="TeamBondSO.StacksFor"/> of <paramref name="count"/>).</summary>
        public ActiveTeamBond(TeamBondSO bond, int tier, int count, IReadOnlyList<int> members)
            : this(bond, tier, count, members, bond == null ? 1 : bond.StacksFor(count))
        {
        }

        public ActiveTeamBond(TeamBondSO bond, int tier, int count, IReadOnlyList<int> members, int stacks)
        {
            Bond = bond;
            Tier = tier;
            Count = count;
            Members = members ?? new int[0];
            Stacks = stacks < 1 ? 1 : stacks;
        }

        public TeamBondSO Bond { get; }

        /// <summary>The tier reached, 1-based (1 = the bond's first tier).</summary>
        public int Tier { get; }

        /// <summary>The condition count that reached it (see <see cref="TeamBondCondition"/>).</summary>
        public int Count { get; }

        /// <summary>
        /// How many times the tier's effect magnitudes apply: the count capped at
        /// <see cref="TeamBondSO.MaxCount"/> for a scaling (<see cref="TeamBondSO.PerCount"/>) bond,
        /// 1 for a tiered one. At least 1.
        /// </summary>
        public int Stacks { get; }

        /// <summary>The bond's members, as ascending indices into the team the resolver was given.</summary>
        public IReadOnlyList<int> Members { get; }

        /// <summary>The reached tier's definition (<see cref="TeamBondSO.Tiers"/> at <see cref="Tier"/> - 1).</summary>
        public TeamBondTier TierDefinition
        {
            get { return Bond.Tiers[Tier - 1]; }
        }
    }
}
