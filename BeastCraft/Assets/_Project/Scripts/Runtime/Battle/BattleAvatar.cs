using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Builds the player's avatar as a battle participant.
    /// <para>
    /// The avatar is a non-combatant commander: it does not fight, it is not a piece on the grid,
    /// and it has no slot in the initiative queue (design decision 2). What it does have is a
    /// <see cref="SkillLoadout"/> of support skills on the same cooldown rotation every beast uses
    /// (decision 6), which is the entire reason it needs to exist at runtime at all.
    /// </para>
    /// <para>
    /// <strong>It is an ordinary <see cref="BattleUnit"/>, not a parallel type.</strong> Everything
    /// a caster is put through — <see cref="SkillLoadout.TickAndResolve"/>,
    /// <see cref="SkillTargetResolver.ResolveTargets"/>, <see cref="SkillEffectApplier.Apply"/> —
    /// already takes a <see cref="BattleUnit"/>, so a dedicated avatar type would have to be
    /// converted into one at every call site or be shadowed by a parallel set of overloads. Neither
    /// is worth it for a participant whose only difference from a beast is which fields of it are
    /// read.
    /// </para>
    /// <para>
    /// <strong>Why the placeholder position is correct rather than a workaround.</strong> The
    /// design doc previously refused to park the avatar on a fake origin tile, and that refusal was
    /// right at the time: the position-dependent shapes (<see cref="SkillTargetShape.SingleTarget"/>,
    /// <see cref="SkillTargetShape.Line"/>, <see cref="SkillTargetShape.Cross"/>,
    /// <see cref="SkillTargetShape.AreaBurst"/>) all anchor on <see cref="BattleUnit.Position"/>, so
    /// a fake tile would have silently produced real, wrong footprints measured from the middle of
    /// the board. That is no longer possible: the avatar's kit is now confirmed to be restricted to
    /// <see cref="SkillTargetShape.Self"/>, <see cref="SkillTargetShape.AllAllies"/> and
    /// <see cref="SkillTargetShape.AllEnemies"/>, and those three shapes never read the caster's
    /// position at all. The placeholder is therefore not a value that happens to be unused today —
    /// it is a value the shape restriction guarantees nothing can reach.
    /// </para>
    /// <para>
    /// <strong>That restriction is an authoring convention, not a runtime check.</strong> Nothing
    /// here rejects an avatar skill authored with a position-dependent shape, deliberately: skills
    /// are authored by this game's own designers, and this codebase trusts internally-authored data
    /// rather than validating it (see <see cref="BattleUnit"/> and <c>SkillSO</c>). Authoring a
    /// <c>Line</c> on an avatar skill would aim it from <see cref="HexCoordinate.Zero"/>, which is
    /// a content bug to be caught in content review.
    /// </para>
    /// <para>
    /// <strong>The avatar is a caster, not a member of the roster.</strong> Pass it as the
    /// <c>caster</c> argument; do <em>not</em> add it to the <c>allUnits</c> roster or to
    /// <see cref="TurnManager"/>. Keeping it out of the roster is what makes it a non-combatant in
    /// practice rather than just in description: it takes no initiative turn, an enemy's
    /// <see cref="SkillTargetShape.AllEnemies"/> sweep cannot reach it, and its own
    /// <see cref="SkillTargetShape.AllAllies"/> buff lands on the player's beasts — which is exactly
    /// the supporting role decision 6 describes. A <see cref="SkillTargetShape.Self"/> skill still
    /// works, because the resolver returns the caster directly without consulting the roster.
    /// </para>
    /// <para>
    /// <strong>Not wired to anything.</strong> The confirmed timing — the avatar's loadout ticks
    /// once every time one of the <em>player's own</em> beasts takes its turn, not on enemy turns
    /// and not once per round — is the turn executor's job, and no turn executor exists yet. This
    /// makes the avatar representable; driving it is a later pass.
    /// </para>
    /// </summary>
    public static class BattleAvatar
    {
        /// <summary>
        /// The id <see cref="Create"/> uses by default. A battle holds exactly one avatar, so a
        /// well-known id is enough to make one without the caller inventing a name, and it keeps
        /// the avatar distinguishable in a log next to the beasts' own ids.
        /// </summary>
        public const string DefaultId = "avatar";

        /// <summary>
        /// The tile the avatar is parked on. Never read by anything the avatar casts, for the
        /// reason this class documents: the three shapes an avatar skill may use ignore the
        /// caster's position entirely.
        /// </summary>
        private static readonly HexCoordinate PlaceholderPosition = HexCoordinate.Zero;

        /// <summary>
        /// Builds the avatar. <paramref name="skills"/> is its authored support stack; passing
        /// <c>null</c> gives it an empty loadout that simply never fires, matching
        /// <see cref="BattleUnit"/>'s own handling.
        /// <para>
        /// <see cref="BattleTeam.Player"/>, because the avatar supports the player's beasts and
        /// side is what <see cref="SkillTargetShape.AllAllies"/> and
        /// <see cref="SkillTargetShape.AllEnemies"/> resolve against — an avatar on any other team
        /// would buff the wrong half of the board.
        /// </para>
        /// <para>
        /// Stats are all zero, and that is the honest value rather than a placeholder: the project
        /// spec puts combat stats entirely on creatures and makes avatar items cosmetic, so the
        /// avatar has no stat block to give. Nothing reads them either — it takes no turn, so its
        /// <c>Speed</c> never sorts anything, and it is not in the roster, so nothing targets its
        /// HP. A zero <c>Hp</c> means <see cref="BattleUnit.CurrentHp"/> also starts at 0, which is
        /// harmless for the same reason.
        /// </para>
        /// <para>
        /// <see cref="BattleUnit.IsDefeated"/> is left <c>false</c> and stays that way: there is no
        /// rule anywhere in the design by which a non-combatant commander could be defeated, and
        /// nothing can write the flag on a unit it cannot target. That is also what keeps the
        /// avatar casting for the whole battle, since both
        /// <see cref="SkillLoadout.TickAndResolve"/> and
        /// <see cref="SkillTargetResolver.ResolveTargets"/> refuse a defeated caster.
        /// </para>
        /// </summary>
        public static BattleUnit Create(SkillLoadout skills, string id = DefaultId)
        {
            return new BattleUnit(id, BattleTeam.Player, default(StatBlock), PlaceholderPosition, skills);
        }
    }
}
