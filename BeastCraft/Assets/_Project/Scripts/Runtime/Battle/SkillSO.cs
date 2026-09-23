using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using UnityEngine;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Authored definition of a creature skill used in tactical grid battles.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Battle/Skill", fileName = "NewSkill")]
    public class SkillSO : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string SkillId;

        /// <summary>Player-facing skill name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Icon shown in the battle action bar and skill lists.</summary>
        public Sprite Icon;

        /// <summary>
        /// Cost in the per-creature battle resource. The resource's name ("mana" / "focus" /
        /// "stamina") is a design decision still open, so this stays an unqualified int.
        /// </summary>
        public int ResourceCost;

        /// <summary>Turns that must pass before this skill can be used again. 0 means no cooldown.</summary>
        public int Cooldown;

        /// <summary>The <see cref="InitialCooldown"/> value that means "start at the ordinary cooldown".</summary>
        public const int UseCooldownAsInitial = -1;

        /// <summary>
        /// What this skill's counter starts the battle at, when it should differ from
        /// <see cref="Cooldown"/>. The default, <see cref="UseCooldownAsInitial"/> (any negative
        /// value), starts it at the ordinary cooldown — the instance's
        /// <see cref="SkillInstance.EffectiveCooldown"/> — exactly as before this field existed.
        /// <c>0</c> fires on the owner's first turn; any other value is taken as authored (a tier's
        /// cooldown reduction does not touch it). Counters tick before they are read, so 0 and 1
        /// both fire on the first turn. See <see cref="SkillLoadout"/>.
        /// </summary>
        public int InitialCooldown = UseCooldownAsInitial;

        /// <summary>
        /// How many times this skill may fire in one battle; 0 (the default) or below is unlimited.
        /// Counted per equipped slot by <see cref="SkillLoadout"/>: once a slot has fired this many
        /// times it is spent and never offered again that battle.
        /// </summary>
        public int MaxUsesPerBattle;

        /// <summary>The footprint this skill covers, always anchored on the caster's own tile.</summary>
        public SkillTargetShape TargetShape = SkillTargetShape.SingleTarget;

        /// <summary>
        /// Reach in hex steps, measured with <see cref="Grid.HexCoordinate.Distance"/> from the
        /// caster's live position.
        /// <para>
        /// Range gates the whole footprint, not just an origin tile: the origin <em>is</em> the
        /// caster's tile (a skill is never aimed at a picked point), so there is nothing else for
        /// it to gate. A <see cref="SkillTargetShape.SingleTarget"/> skill can only reach units
        /// within this many steps; a <see cref="SkillTargetShape.Line"/> or
        /// <see cref="SkillTargetShape.Cross"/> extends this many steps outward along its
        /// direction(s); an <see cref="SkillTargetShape.AreaBurst"/> covers the disc of this
        /// radius. <see cref="SkillTargetShape.Self"/>,
        /// <see cref="SkillTargetShape.AllEnemies"/> and <see cref="SkillTargetShape.AllAllies"/>
        /// ignore it entirely — they are not distance-limited.
        /// </para>
        /// </summary>
        public int Range = 1;

        /// <summary>
        /// Which side of the fight this skill may land on, relative to the caster's own
        /// <see cref="BattleTeam"/>.
        /// <para>
        /// Consulted by <see cref="SkillTargetShape.SingleTarget"/>,
        /// <see cref="SkillTargetShape.Line"/>, <see cref="SkillTargetShape.Cross"/> and
        /// <see cref="SkillTargetShape.AreaBurst"/>. Ignored by
        /// <see cref="SkillTargetShape.Self"/> (which always hits the caster) and by
        /// <see cref="SkillTargetShape.AllEnemies"/> / <see cref="SkillTargetShape.AllAllies"/>
        /// (whose names already fix the side).
        /// </para>
        /// </summary>
        public SkillTargetSide TargetSide = SkillTargetSide.Enemy;

        /// <summary>
        /// What this skill compares candidates by when the shape makes it pick just one of them.
        /// <para>
        /// Consulted only by <see cref="SkillTargetShape.SingleTarget"/> and by
        /// <see cref="SkillTargetShape.Line"/> (which uses it to choose the focus target that sets
        /// the beam's direction). Every other shape hits its entire footprint, so it never picks
        /// and never reads this.
        /// </para>
        /// </summary>
        public SkillTargetingCriterion TargetingCriterion = SkillTargetingCriterion.Random;

        /// <summary>
        /// Which extreme of <see cref="TargetingCriterion"/>'s compared value wins.
        /// <para>
        /// Meaningful only where <see cref="TargetingCriterion"/> is, and additionally ignored when
        /// that is <see cref="SkillTargetingCriterion.Random"/>, which does not compare anything.
        /// </para>
        /// </summary>
        public SkillTargetingOrder TargetingOrder = SkillTargetingOrder.Lowest;

        /// <summary>
        /// The stat axis <see cref="SkillTargetingCriterion.Stat"/> compares candidates on, read
        /// through <c>StatBlock.GetStat</c>.
        /// <para>
        /// Consulted <em>only</em> when <see cref="TargetingCriterion"/> is
        /// <see cref="SkillTargetingCriterion.Stat"/>. It is ignored under
        /// <see cref="SkillTargetingCriterion.Random"/>,
        /// <see cref="SkillTargetingCriterion.Distance"/> and
        /// <see cref="SkillTargetingCriterion.CurrentHp"/>, and — like the rest of the targeting
        /// fields — by every shape that does not pick a single candidate.
        /// </para>
        /// </summary>
        public StatType TargetingStat = StatType.HP;

        /// <summary>
        /// The element this skill's damage is dealt in. The <em>skill</em>, not the caster,
        /// decides the attacking element, so a Fire beast can carry a neutral or off-element
        /// skill.
        /// <para>
        /// Read only for <see cref="SkillEffectType.Damage"/> effects, where
        /// <see cref="DamageFormula"/> scales the stat-based damage by
        /// <see cref="ElementChart.GetMultiplier(Element, IReadOnlyList{Element})"/> against the
        /// target's elements. Heals and stat changes ignore it. The default,
        /// <see cref="Element.None"/>, is neutral: a 1x multiplier against everything.
        /// </para>
        /// </summary>
        public Element Element = Element.None;

        /// <summary>
        /// Which stat pair this skill's damage is resolved with: <see cref="DamageCategory.Physical"/>
        /// (the caster's <c>Attack</c> against the target's <c>Defense</c>) or
        /// <see cref="DamageCategory.Special"/> (<c>SpecialAttack</c> against
        /// <c>SpecialDefense</c>). See <see cref="DamageFormula"/>.
        /// <para>
        /// Read only for <see cref="SkillEffectType.Damage"/> effects; heals and stat changes ignore
        /// it. It belongs to the skill rather than to each effect, as <see cref="Element"/> does: a
        /// skill is a physical or a special attack as a whole. Defaults to
        /// <see cref="DamageCategory.Physical"/>.
        /// </para>
        /// </summary>
        public DamageCategory Category = DamageCategory.Physical;

        /// <summary>
        /// Everything this skill applies to each affected unit, at level 1 and tier 0. A leveled
        /// skill scales every magnitude and may append tier bonus effects; see
        /// <see cref="SkillInstance.Effects"/>.
        /// </summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();

        /// <summary>
        /// How this skill grows as its owner uses it: max level, magnitude growth per level and
        /// the breakthrough gates with their bonuses. See <see cref="SkillProgressionDefinition"/>
        /// and the battle-system design doc, "Skill progression". A battle reads it only through a
        /// <see cref="SkillInstance"/>; a skill built without a level (the plain
        /// <see cref="SkillLoadout(IEnumerable{SkillSO})"/> path) is level 1, tier 0, which is
        /// exactly this asset's authored numbers.
        /// </summary>
        public SkillProgressionDefinition Progression = new SkillProgressionDefinition();
    }
}
