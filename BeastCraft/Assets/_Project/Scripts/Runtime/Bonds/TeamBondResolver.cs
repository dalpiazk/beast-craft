using System.Collections.Generic;
using BeastCraft.Creatures;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// Decides which team bonds a team activates, and at which tier. Pure: a function of the bonds
    /// and the team only, with no battle state and no randomness, so the team-building screen can
    /// show the same answer the battle will use.
    /// <para>
    /// <strong>Counting.</strong> Each bond's condition gives a count and a member set (see
    /// <see cref="TeamBondCondition"/>): <c>Stance</c> counts members of the stance;
    /// <c>Elements</c> counts the set's distinct elements the team covers (never more than the
    /// number of members, so one dual-element beast is not a bond on its own), and its members are
    /// the beasts carrying any of them; <c>Species</c> counts the listed species present (each once),
    /// and its members are the beasts of those species. The bond's tier is the highest one whose
    /// <see cref="TeamBondTier.MinCount"/> the count reaches (the validator keeps the tiers strictly
    /// rising). A count below the first tier leaves the bond inactive. A scaling bond
    /// (<see cref="TeamBondSO.PerCount"/>) has one tier, reached at its <c>MinCount</c> (1 or
    /// more), and applies <see cref="ActiveTeamBond.Stacks"/> = the count capped at
    /// <see cref="TeamBondSO.MaxCount"/> stacks of it.
    /// </para>
    /// <para>
    /// Non-throwing: null bonds, null tiers and null members are skipped. The result keeps the
    /// order the bonds were given in, which is also the order they are applied in.
    /// </para>
    /// </summary>
    public static class TeamBondResolver
    {
        /// <summary>Every bond in <paramref name="bonds"/> that <paramref name="team"/> activates, in the given order.</summary>
        public static List<ActiveTeamBond> Resolve(IEnumerable<TeamBondSO> bonds, IReadOnlyList<TeamBondMember> team)
        {
            List<ActiveTeamBond> active = new List<ActiveTeamBond>();

            if (bonds == null || team == null)
            {
                return active;
            }

            foreach (TeamBondSO bond in bonds)
            {
                if (bond == null)
                {
                    continue;
                }

                List<int> members = new List<int>();
                int count = Count(bond, team, members);
                int tier = TierFor(bond, count);

                if (tier > 0)
                {
                    active.Add(new ActiveTeamBond(bond, tier, count, members, bond.StacksFor(count)));
                }
            }

            return active;
        }

        /// <summary>The members for a team of species, in team order (the team-building view).</summary>
        public static List<TeamBondMember> MembersOf(IEnumerable<CreatureSpeciesSO> team)
        {
            List<TeamBondMember> members = new List<TeamBondMember>();

            if (team != null)
            {
                foreach (CreatureSpeciesSO species in team)
                {
                    members.Add(TeamBondMember.FromSpecies(species));
                }
            }

            return members;
        }

        /// <summary>
        /// The bond's condition count for <paramref name="team"/>, filling <paramref name="members"/>
        /// (when not null) with the matching members' indices, ascending.
        /// </summary>
        public static int Count(TeamBondSO bond, IReadOnlyList<TeamBondMember> team, List<int> members)
        {
            if (bond == null || team == null)
            {
                return 0;
            }

            HashSet<Element> elementsSeen = new HashSet<Element>();
            HashSet<string> speciesSeen = new HashSet<string>();
            int count = 0;
            int matched = 0;

            for (int i = 0; i < team.Count; i++)
            {
                TeamBondMember member = team[i];

                if (member == null)
                {
                    continue;
                }

                bool matches = false;

                switch (bond.Condition)
                {
                    case TeamBondCondition.Stance:
                        matches = member.Stance == bond.Stance;
                        count += matches ? 1 : 0;
                        break;

                    case TeamBondCondition.Elements:
                        foreach (Element element in member.Elements)
                        {
                            if (element != Element.None && bond.Elements != null && bond.Elements.Contains(element))
                            {
                                matches = true;
                                count += elementsSeen.Add(element) ? 1 : 0;
                            }
                        }

                        break;

                    case TeamBondCondition.Species:
                        matches = !string.IsNullOrEmpty(member.SpeciesId) && bond.SpeciesIds != null && bond.SpeciesIds.Contains(member.SpeciesId);
                        count += matches && speciesSeen.Add(member.SpeciesId) ? 1 : 0;
                        break;
                }

                if (matches)
                {
                    matched++;
                    members?.Add(i);
                }
            }

            // An element bond is between beasts: one dual-element beast covering two of the set's
            // elements is still one member, so the count never exceeds the members.
            if (bond.Condition == TeamBondCondition.Elements && count > matched)
            {
                count = matched;
            }

            return count;
        }

        /// <summary>The 1-based tier <paramref name="count"/> reaches, or 0 when it reaches none.</summary>
        public static int TierFor(TeamBondSO bond, int count)
        {
            if (bond == null || bond.Tiers == null)
            {
                return 0;
            }

            int tier = 0;

            for (int i = 0; i < bond.Tiers.Count; i++)
            {
                if (bond.Tiers[i] != null && count >= bond.Tiers[i].MinCount)
                {
                    tier = i + 1;
                }
            }

            return tier;
        }
    }
}
