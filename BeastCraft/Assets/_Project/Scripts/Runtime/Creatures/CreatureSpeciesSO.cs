using System.Collections.Generic;
using BeastCraft.Customization.Creature;
using UnityEngine;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// Authored definition of a creature species: its identity, base stats, growth, evolution
    /// branches, learnable skills and which customization schema its player-facing appearance uses.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Creatures/Species", fileName = "NewCreatureSpecies")]
    public class CreatureSpeciesSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string SpeciesId;

        /// <summary>Player-facing species name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Roster / codex icon.</summary>
        public Sprite Icon;

        /// <summary>Unscaled level-1 reference stats. Scaled by <see cref="GrowthRate"/>.</summary>
        public StatBlock BaseStats;

        /// <summary>Shared leveling curve this species uses.</summary>
        public GrowthRateCurve GrowthRate;

        /// <summary>Available evolution branches. An empty list means this is a final-tier species.</summary>
        public List<EvolutionRequirement> EvolutionOptions = new List<EvolutionRequirement>();

        /// <summary>Which customization schema drives this species' appearance options.</summary>
        public CreatureCustomizationSchema CustomizationSchema;

        /// <summary>Skills this species learns, with the level each becomes available.</summary>
        public List<SkillLearnEntry> LearnableSkills = new List<SkillLearnEntry>();

        /// <summary>
        /// This species' elemental affinities, read when it is on the receiving end of an
        /// elemental skill (see <c>BeastCraft.Battle.ElementChart</c>). The element list is now
        /// finalized as the <see cref="Element"/> enum. Usually one entry, occasionally two; a
        /// dual-element species takes the product of both multipliers. Empty (or
        /// <see cref="Element.None"/>) means no affinity, which is neutral to everything.
        /// </summary>
        public Element[] Elements = new Element[0];

        /// <summary>
        /// The species' stat on the given axis at the given level. Falls back to the unscaled base
        /// stat (with an error) when no growth curve is assigned, so a half-authored species still
        /// produces usable numbers instead of a null reference.
        /// </summary>
        public int GetStatAtLevel(StatType type, int level)
        {
            int baseStat = BaseStats.GetStat(type);

            if (GrowthRate == null)
            {
                Debug.LogError("[Creatures] Species '" + name + "' (id '" + SpeciesId +
                               "') has no GrowthRate assigned; returning unscaled base stat for " +
                               type + ".", this);
                return baseStat;
            }

            return Mathf.RoundToInt(baseStat * GrowthRate.GetScaleAtLevel(level));
        }
    }
}
