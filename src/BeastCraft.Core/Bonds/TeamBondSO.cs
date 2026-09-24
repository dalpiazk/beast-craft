using System.Collections.Generic;
using BeastCraft.Creatures;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// Authored definition of one team bond: a team-composition condition (enough beasts of a
    /// stance, a set of elements covered, or a set of species fielded together) and, per tier, the
    /// effects the bond applies as the battle begins. Bonds reward building a team rather than
    /// picking beasts one at a time. A bond is either tiered (the highest tier reached replaces the
    /// lower ones) or scaling (<see cref="PerCount"/>: one tier whose magnitudes grow with every
    /// member, up to <see cref="MaxCount"/>). See the battle-system design doc, "Team bonds".
    /// <para>
    /// Generated from the <c>TeamBonds</c> array of <c>data/Skills/skill-library.json</c> by the
    /// skill library importer; never hand-edit the imported fields, edit the JSON and re-import.
    /// Which bonds are active for a team is <see cref="TeamBondResolver"/>'s call; applying them is
    /// <see cref="TeamBondLoadout"/>'s, called by <c>BattleTurnExecutor.BeginBattle</c>. Only the
    /// player's team has bonds (for now).
    /// </para>
    /// </summary>
    public class TeamBondSO : ContentAsset
    {
        /// <summary>Stable string key (save data, analytics, UI). Never rename after ship.</summary>
        public string BondId;

        /// <summary>Player-facing bond name.</summary>
        public string DisplayName;

        public string Description;

        /// <summary>Icon shown in the team-building screen. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string Icon;

        /// <summary>What the bond counts on the team.</summary>
        public TeamBondCondition Condition = TeamBondCondition.Stance;

        /// <summary>For <see cref="TeamBondCondition.Stance"/>: the stance counted.</summary>
        public CombatStance Stance = CombatStance.Vanguard;

        /// <summary>For <see cref="TeamBondCondition.Elements"/>: the element set.</summary>
        public List<Element> Elements = new List<Element>();

        /// <summary>For <see cref="TeamBondCondition.Species"/>: the species ids (roster <c>SpeciesId</c>s).</summary>
        public List<string> SpeciesIds = new List<string>();

        /// <summary>Who the tier effects land on.</summary>
        public TeamBondScope Scope = TeamBondScope.Members;

        /// <summary>
        /// The tiers, by strictly rising <see cref="TeamBondTier.MinCount"/>; the highest one reached
        /// applies. A <see cref="PerCount"/> bond has exactly one.
        /// </summary>
        public List<TeamBondTier> Tiers = new List<TeamBondTier>();

        /// <summary>
        /// A scaling bond: instead of tiers that replace one another, its single tier is a
        /// <em>per-stack</em> effect list. Once the count reaches the tier's
        /// <see cref="TeamBondTier.MinCount"/> the bond applies every effect's
        /// <see cref="BeastCraft.Battle.SkillEffect.Magnitude"/> x <see cref="StacksFor"/> (the count, capped at
        /// <see cref="MaxCount"/>), so each further member adds one more stack. False (the default)
        /// is the tiered bond.
        /// </summary>
        public bool PerCount;

        /// <summary>For a <see cref="PerCount"/> bond: the most stacks it applies. Ignored otherwise.</summary>
        public int MaxCount;

        /// <summary>
        /// How many times the reached tier's magnitudes apply at condition count
        /// <paramref name="count"/>: for a <see cref="PerCount"/> bond, <paramref name="count"/>
        /// capped at <see cref="MaxCount"/> (never below 1); for a tiered bond, always 1.
        /// </summary>
        public int StacksFor(int count)
        {
            if (!PerCount)
            {
                return 1;
            }

            int stacks = MaxCount > 0 && count > MaxCount ? MaxCount : count;
            return stacks < 1 ? 1 : stacks;
        }
    }
}
