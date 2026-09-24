using System;
using BeastCraft.Battle;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// One branch of a species' evolution: the conditions to take it and the species it leads to.
    /// A species with several entries offers the player a branching choice.
    /// </summary>
    [Serializable]
    public class EvolutionRequirement
    {
        /// <summary>Minimum creature level before this branch becomes available.</summary>
        public int RequiredLevel = 1;

        /// <summary>Optional item that must be held/consumed. Null means no item is required.</summary>
        public GearSO RequiredItem;

        /// <summary>The species the creature becomes. A self-reference to another species asset.</summary>
        public CreatureSpeciesSO ResultingSpecies;

        /// <summary>Designer-facing label for the resulting tier, e.g. "Base", "Stage 2", "Promoted".</summary>
        public string StageName;
    }
}
