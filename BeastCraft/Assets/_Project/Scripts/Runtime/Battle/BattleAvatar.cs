using System.Collections.Generic;
using BeastCraft.Avatar;
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
    /// (decision 6), which is the main reason it needs to exist at runtime at all — plus, since
    /// decision 6 was amended, a stat block of its own (see "Stats" below).
    /// </para>
    /// <para>
    /// <strong>It is an ordinary <see cref="BattleUnit"/>, not a parallel type.</strong> Everything
    /// a caster is put through — <see cref="SkillLoadout.TickAndResolve"/>,
    /// <see cref="SkillTargetResolver.ResolveTargets"/>, <see cref="SkillEffectApplier.Apply(SkillActivation, BattleUnit, System.Random)"/> —
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
    /// <strong>Driven by the turn executor.</strong> The confirmed timing — the avatar's loadout
    /// ticks once every time one of the <em>player's own</em> beasts takes its turn, not on enemy
    /// turns and not once per round — lives in <see cref="BattleTurnExecutor.ExecuteTurn"/>, which
    /// takes the avatar as its own argument. This class only builds it.
    /// </para>
    /// <para>
    /// <strong>Stats (design decision 6, amended).</strong> The avatar has real stats: an authored
    /// base (<see cref="AvatarStatsSO"/>) raised by equipped <see cref="AvatarGearSO"/>, assembled
    /// by <see cref="StatCalculator"/> exactly as beast gear is. That gear is never rendered, and it
    /// is independent of the avatar's appearance, which stays purely cosmetic and statless in the
    /// customization system. <strong>The avatar's stats and level feed
    /// <see cref="DamageFormula"/> exactly as a beast's do</strong>: a damaging avatar skill (say an
    /// <see cref="SkillTargetShape.AllEnemies"/> strike) uses the avatar's <c>Attack</c> or
    /// <c>SpecialAttack</c> and its <see cref="BattleUnit.Level"/>. Its heals and buffs are still
    /// flat, as everyone's are, so for those its stats change nothing yet.
    /// </para>
    /// <para>
    /// <strong>Level.</strong> Avatar progression is still undesigned — there is no avatar XP and
    /// no avatar level anywhere in the data. The damage formula needs a caster level all the same,
    /// so the statful <c>Create</c> takes a <em>battle</em> level that the battle setup is expected
    /// to choose sensibly (for example the level of the player's team), defaulting to 1. It is a
    /// per-battle input, not a stored avatar attribute, until progression is designed.
    /// </para>
    /// </summary>
    public static class BattleAvatar
    {
        /// <summary>
        /// The id both <c>Create</c> overloads use by default. A battle holds exactly one avatar, so
        /// a well-known id is enough to make one without the caller inventing a name, and it keeps
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
        /// This overload gives the avatar an all-zero stat block, unclamped, exactly as it always
        /// has; it is the "no stats authored" path and is kept so existing callers are unaffected.
        /// Use <see cref="Create(SkillLoadout, StatBlock, IEnumerable{AvatarGearSO}, string, int)"/>
        /// to give the avatar its base stats and gear. It takes no turn, so its zero <c>Speed</c>
        /// never sorts anything, and it is not in the roster, so nothing targets its HP. A zero
        /// <c>Hp</c> means <see cref="BattleUnit.CurrentHp"/> also starts at 0, which is harmless
        /// for the same reason.
        /// </para>
        /// <para>
        /// <strong>Its damage is the formula's floor.</strong> This avatar is level 1 with zero
        /// <c>Attack</c> and <c>SpecialAttack</c>, and <see cref="DamageFormula"/> gives a zero
        /// attacking stat exactly its +2 constant: every damage effect it lands deals 2 times the
        /// element multiplier (truncated, at least 1), whatever the authored power. Before the
        /// damage formula it dealt the authored magnitude flat; a caller relying on an avatar
        /// strike landing hard must now give the avatar stats through the other overload. Heals and
        /// buffs it casts are unchanged, since those are still flat.
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

        /// <summary>
        /// Builds the avatar with real stats: <paramref name="baseStats"/> (normally
        /// <see cref="AvatarStatsSO.BaseStats"/>) raised by every modifier on
        /// <paramref name="equipped"/>, through
        /// <see cref="StatCalculator.ComputeStats(StatBlock, IEnumerable{StatModifier})"/> — flat
        /// bonuses, then summed percentages, then rounding and the floors (every stat at least 0,
        /// <c>Hp</c> at least 1). Everything else is identical to
        /// <see cref="Create(SkillLoadout, string)"/>: player team, placeholder position, a caster
        /// and never a member of the roster, never defeated.
        /// <para>
        /// <paramref name="level"/> becomes the avatar's <see cref="BattleUnit.Level"/>, the
        /// caster level <see cref="DamageFormula"/> reads for its damaging skills. It is a battle
        /// level, not avatar progression (which is still open; see the class remarks): the battle
        /// setup should pass something sensible such as the player team's level. It defaults to 1,
        /// and anything below 1 is stored as 1. It is the last parameter so that existing callers,
        /// including any passing <paramref name="id"/> positionally, are unaffected.
        /// </para>
        /// <para>
        /// A null <paramref name="equipped"/> list, null pieces and null modifiers are skipped. One
        /// item per <see cref="AvatarGearSlot"/> is <em>not</em> enforced — two pieces in the same
        /// slot both count. That is the equipment screen's rule to keep, as it is for beast gear.
        /// </para>
        /// <para>
        /// The 1 HP floor applies on this path only, because it is <see cref="StatCalculator"/>'s
        /// floor; it gives the avatar a nonzero <see cref="BattleUnit.CurrentHp"/> but changes
        /// nothing about its role, since nothing targets it.
        /// </para>
        /// </summary>
        public static BattleUnit Create(SkillLoadout skills, StatBlock baseStats, IEnumerable<AvatarGearSO> equipped, string id = DefaultId,
                                        int level = 1)
        {
            StatBlock stats = StatCalculator.ComputeStats(baseStats, StatCalculator.CollectModifiers(equipped));
            return new BattleUnit(id, BattleTeam.Player, stats, PlaceholderPosition, skills, null, level);
        }
    }
}
