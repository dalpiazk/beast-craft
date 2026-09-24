using System.Collections.Generic;
using BeastCraft.Creatures;
using UnityEngine;

namespace BeastCraft.Bonds
{
    /// <summary>
    /// Authored definition of one team bond: a team-composition condition (enough beasts of a
    /// stance, a set of elements covered, or a set of species fielded together) and, per tier, the
    /// effects the bond applies as the battle begins. Bonds reward building a team rather than
    /// picking beasts one at a time. See the battle-system design doc, "Team bonds".
    /// <para>
    /// Generated from the <c>TeamBonds</c> array of <c>Data/Skills/skill-library.json</c> by the
    /// skill library importer; never hand-edit the imported fields, edit the JSON and re-import.
    /// Which bonds are active for a team is <see cref="TeamBondResolver"/>'s call; applying them is
    /// <see cref="TeamBondLoadout"/>'s, called by <c>BattleTurnExecutor.BeginBattle</c>. Only the
    /// player's team has bonds (for now).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Bonds/Team Bond", fileName = "NewTeamBond")]
    public class TeamBondSO : ScriptableObject
    {
        /// <summary>Stable string key (save data, analytics, UI). Never rename after ship.</summary>
        public string BondId;

        /// <summary>Player-facing bond name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Icon shown in the team-building screen.</summary>
        public Sprite Icon;

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

        /// <summary>The tiers, by strictly rising <see cref="TeamBondTier.MinCount"/>; the highest one reached applies.</summary>
        public List<TeamBondTier> Tiers = new List<TeamBondTier>();
    }
}
