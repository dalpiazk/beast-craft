using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Customization.Creature;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// Authored definition of a creature species: its identity, base stats, growth, evolution
    /// branches, learnable skills and which customization schema its player-facing appearance uses.
    /// </summary>
    public class CreatureSpeciesSO : ContentAsset
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string SpeciesId;

        /// <summary>Player-facing species name.</summary>
        public string DisplayName;

        public string Description;

        /// <summary>Roster / codex icon. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string Icon;

        /// <summary>
        /// Unscaled reference stats: the stats at growth-curve scale 1, which for authored curves is
        /// max level (see <see cref="GrowthRateCurve"/>). Scaled by <see cref="GrowthRate"/>, except for
        /// <see cref="StatBlock.MoveRange"/> and <see cref="StatBlock.CritChance"/>, which are
        /// authored here as the species' per-turn movement and critical-hit chance and used as-is at
        /// every level (see <see cref="GetStatAtLevel"/>).
        /// </summary>
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
        /// The skills a fresh beast of this species takes into battle, in slot (fire-priority)
        /// order: up to <c>BeastSkillBook.EquipSlotCount</c>, each also in
        /// <see cref="LearnableSkills"/> at a low level. Generated from the skill library JSON with
        /// <see cref="LearnableSkills"/>; a starting point for the equip screen, not a restriction.
        /// </summary>
        public List<SkillSO> DefaultLoadout = new List<SkillSO>();

        /// <summary>
        /// This species' elemental affinities, read when it is on the receiving end of an
        /// elemental skill (see <c>BeastCraft.Battle.ElementChart</c>). The element list is now
        /// finalized as the <see cref="Element"/> enum. Usually one entry, occasionally two; a
        /// dual-element species takes the product of both multipliers. Empty (or
        /// <see cref="Element.None"/>) means no affinity, which is neutral to everything.
        /// </summary>
        public Element[] Elements = new Element[0];

        /// <summary>
        /// How beasts of this species position themselves in battle (see <see cref="CombatStance"/>).
        /// Copied onto each unit by <c>BattleUnitFactory.CreateBeast</c>. Defaults to
        /// <see cref="CombatStance.Vanguard"/>, the stance whose movement is the plain approach rule,
        /// so a species authored before stances existed behaves exactly as it did.
        /// </summary>
        public CombatStance Stance = CombatStance.Vanguard;

        /// <summary>
        /// How many tiles a unit of this species covers (see <see cref="UnitFootprint"/>). Copied
        /// onto each unit by <c>BattleUnitFactory.CreateBeast</c>. Always
        /// <see cref="UnitFootprint.Single"/> for a beast — the roster has no footprint field and
        /// <c>BeastRosterValidator</c> refuses any other value — and only the balance simulator's large
        /// enemies (built as in-memory species) set it.
        /// </summary>
        public UnitFootprint Footprint = UnitFootprint.Single;

        /// <summary>
        /// The species' stat on the given axis at the given level. Falls back to the unscaled base
        /// stat (with an error) when no growth curve is assigned, so a half-authored species still
        /// produces usable numbers instead of a null reference.
        /// <para>
        /// <see cref="StatType.MoveRange"/> is exempt from the curve and always returns the
        /// authored base. A growth curve runs from a small fraction (0.10-0.20 for the authored
        /// curves, 0 for the default) at level 1 up to 1 at max level, which suits stats in the tens
        /// or hundreds but would round a move range of 3 down to 0 or 1 for much of the early game.
        /// Move range is a small tactical integer, not a quantity that grows with level; gear and
        /// buffs are what change it.
        /// </para>
        /// <para>
        /// <see cref="StatType.CritChance"/> is exempt for the same reason: it is a percent chance
        /// authored in single digits or low tens, which a 0.15 level-1 scale would round to 0 or 1.
        /// A beast's crit chance is part of its identity at every level; gear and buffs raise it.
        /// </para>
        /// </summary>
        public int GetStatAtLevel(StatType type, int level)
        {
            int baseStat = BaseStats.GetStat(type);

            if (type == StatType.MoveRange || type == StatType.CritChance)
            {
                return baseStat;
            }

            if (GrowthRate == null)
            {
                Log.Error("[Creatures] Species '" + name + "' (id '" + SpeciesId +
                               "') has no GrowthRate assigned; returning unscaled base stat for " +
                               type + ".", this);
                return baseStat;
            }

            return MathUtil.RoundToInt(baseStat * GrowthRate.GetScaleAtLevel(level));
        }
    }
}
