using System.Collections.Generic;
using BeastCraft.Creatures;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// What <see cref="TeamBondResolver"/> needs to know about one team member: its species, its
    /// stance and its elements. A plain record, so a team can be checked for bonds before any battle
    /// unit exists (the team-building screen) as well as at battle start.
    /// </summary>
    public sealed class TeamBondMember
    {
        private static readonly Element[] NoElements = new Element[0];

        public TeamBondMember(string speciesId, CombatStance stance, IReadOnlyList<Element> elements)
        {
            SpeciesId = speciesId;
            Stance = stance;
            Elements = elements ?? NoElements;
        }

        /// <summary>The member for a beast of <paramref name="species"/>; a null species is a Vanguard with no species and no element.</summary>
        public static TeamBondMember FromSpecies(CreatureSpeciesSO species)
        {
            return species == null
                ? new TeamBondMember(null, CombatStance.Vanguard, null)
                : new TeamBondMember(species.SpeciesId, species.Stance, species.Elements);
        }

        /// <summary>The roster <c>SpeciesId</c>; may be null (it then matches no species bond).</summary>
        public string SpeciesId { get; }

        public CombatStance Stance { get; }

        /// <summary>Never null.</summary>
        public IReadOnlyList<Element> Elements { get; }
    }
}
