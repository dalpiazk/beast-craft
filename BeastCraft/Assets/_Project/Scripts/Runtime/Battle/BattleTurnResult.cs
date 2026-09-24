using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Everything one unit's turn did: where it started and finished, what its movement budget was
    /// and how much of it went, what each ready skill slot made of its chance, and — on the
    /// avatar's own turn (<see cref="BattleTurnExecutor.ExecuteAvatarTurn"/>) — what its rotation
    /// fired.
    /// <para>
    /// The return shape of <see cref="BattleTurnExecutor.ExecuteTurn"/>. A turn is no longer a
    /// single list of activations, because a skill can now come up ready and decline to go off, and
    /// because movement is spent inside the turn rather than before it — so the interesting facts
    /// are per slot and per turn, not per activation.
    /// </para>
    /// <para>
    /// Purely a record for callers, logs and tests. Reading it changes nothing; the state changes
    /// all landed on the units themselves while the turn ran.
    /// </para>
    /// </summary>
    public class BattleTurnResult
    {
        public BattleTurnResult(
            BattleUnit unit,
            HexCoordinate startPosition,
            HexCoordinate endPosition,
            int movementBudget,
            int movementSpent,
            IReadOnlyList<BattleSkillOutcome> skillOutcomes,
            IReadOnlyList<SkillActivation> avatarActivations,
            int retreatSteps = 0,
            bool stunned = false,
            int statusDamage = 0,
            IReadOnlyList<PassiveActivation> passiveActivations = null,
            IReadOnlyList<BondReactionRecord> bondReactions = null)
        {
            Unit = unit;
            StartPosition = startPosition;
            EndPosition = endPosition;
            MovementBudget = movementBudget;
            MovementSpent = movementSpent;
            SkillOutcomes = skillOutcomes ?? new List<BattleSkillOutcome>();
            AvatarActivations = avatarActivations ?? new List<SkillActivation>();
            RetreatSteps = retreatSteps;
            Stunned = stunned;
            StatusDamage = statusDamage;
            PassiveActivations = passiveActivations ?? new List<PassiveActivation>();
            BondReactions = bondReactions ?? new List<BondReactionRecord>();
        }

        /// <summary>The unit whose turn this was.</summary>
        public BattleUnit Unit { get; }

        /// <summary>The tile it stood on when the turn opened.</summary>
        public HexCoordinate StartPosition { get; }

        /// <summary>
        /// The tile it stands on now. Equal to <see cref="StartPosition"/> whenever the turn spent
        /// no movement, which is the common case.
        /// </summary>
        public HexCoordinate EndPosition { get; }

        /// <summary>
        /// The movement the turn started with: the unit's <see cref="BattleUnit.MoveRange"/>, with a
        /// negative value read as 0.
        /// </summary>
        public int MovementBudget { get; }

        /// <summary>
        /// Hex steps actually walked, summed across every skill that had to close the distance plus
        /// any <see cref="RetreatSteps"/>. Never more than <see cref="MovementBudget"/>, because the
        /// budget is shared by the whole turn rather than refreshed per skill.
        /// </summary>
        public int MovementSpent { get; }

        /// <summary>
        /// Hex steps walked after every ready slot had been attempted, spending leftover budget to
        /// back away from the enemy — only a <see cref="CombatStance.Ranged"/> or
        /// <see cref="CombatStance.Skirmisher"/> unit does this (see
        /// <see cref="BattleTurnExecutor"/>). Included in <see cref="MovementSpent"/> and not in
        /// any <see cref="BattleSkillOutcome.MovementSpent"/>. 0 for a
        /// <see cref="CombatStance.Vanguard"/>, and whenever the unit stayed where it was.
        /// </summary>
        public int RetreatSteps { get; }

        /// <summary>
        /// True when the unit began this turn under a <see cref="StatusType.Stun"/>: it did not move,
        /// fired nothing and its cooldowns did not tick (see <see cref="BattleTurnExecutor.ExecuteTurn"/>).
        /// </summary>
        public bool Stunned { get; }

        /// <summary>
        /// HP the unit lost to its own <see cref="StatusType.DamageOverTime"/> stacks as this turn
        /// began (after any shield). 0 with none. A unit they defeat takes no further part in the turn.
        /// </summary>
        public int StatusDamage { get; }

        /// <summary>
        /// Movement left over. Of interest mainly because it is what a later skill in the same turn
        /// had to work with.
        /// </summary>
        public int MovementRemaining
        {
            get { return MovementBudget - MovementSpent; }
        }

        /// <summary>
        /// One entry per slot that <see cref="SkillLoadout.Tick"/> offered this turn, in stack
        /// order — including the ones that did not fire, each carrying why. Never <c>null</c>;
        /// empty when nothing came off cooldown.
        /// </summary>
        public IReadOnlyList<BattleSkillOutcome> SkillOutcomes { get; }

        /// <summary>
        /// What the player's avatar cast on this turn, which is then the avatar's own turn (its
        /// loadout ticks once per avatar turn; see <see cref="BattleTurnExecutor.ExecuteAvatarTurn"/>).
        /// Always empty on a beast's turn, and when no avatar was supplied. Never <c>null</c>.
        /// <para>
        /// Plain <see cref="SkillActivation"/>s rather than <see cref="BattleSkillOutcome"/>s
        /// because the avatar's kit cannot fail to reach: every ready slot of its fires, so there is
        /// no status to report and no movement to account for.
        /// </para>
        /// </summary>
        public IReadOnlyList<SkillActivation> AvatarActivations { get; }

        /// <summary>
        /// Every avatar passive that fired during this turn, in the order it fired — on either
        /// side's turn, since passives react to events rather than ticking (see
        /// <see cref="PassiveLoadout"/>). Empty when no passives were supplied. Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<PassiveActivation> PassiveActivations { get; }

        /// <summary>
        /// Every team-bond reaction that fired during this turn, in the order it fired, on either
        /// side's turn (see <see cref="TeamBondLoadout"/> and <see cref="BondReaction"/>). Empty when
        /// no bonds were supplied or none react. Never <c>null</c>.
        /// </summary>
        public IReadOnlyList<BondReactionRecord> BondReactions { get; }
    }
}
