using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using UnityEngine;

namespace BeastCraft.Avatar
{
    /// <summary>
    /// Authored definition of one of the avatar's passive skills: an effect list that fires on its
    /// own when something happens in battle, rather than on a cooldown rotation. Passives are the
    /// avatar's main role (it does not fight on the grid); it equips up to
    /// <see cref="AvatarSkillBook.PassiveSlotCount"/> of them. See the battle-system design doc,
    /// "Avatar passives".
    /// <para>
    /// <strong>The same engine as skills.</strong> <see cref="Effects"/> is a list of ordinary
    /// <see cref="SkillEffect"/>s, applied by <see cref="SkillEffectApplier"/> with the avatar as
    /// the caster: damage uses the avatar's attacking stat and crit chance, a shield the avatar's
    /// <c>Defense</c>. <see cref="Progression"/> is the same <see cref="SkillProgressionDefinition"/>
    /// block beast skills carry, so a passive levels and breaks through exactly like a skill: every
    /// magnitude scales with its level, and each passed gate's
    /// <see cref="SkillTierDefinition.BonusEffects"/> are appended to <see cref="Effects"/>
    /// (<see cref="SkillTierDefinition.CooldownReduction"/> means nothing to a passive).
    /// </para>
    /// <para>
    /// <strong>Gating.</strong> Each time its <see cref="Trigger"/> happens the passive fires only
    /// if it has not used up <see cref="MaxTriggersPerBattle"/>, its
    /// <see cref="InternalCooldown"/> has run out, its <see cref="TargetScope"/> finds someone to
    /// land on, and its <see cref="ProcChance"/> roll succeeds — checked in that order, so the roll
    /// (the only rng draw a passive adds) is taken only when everything else allows it.
    /// </para>
    /// <para>
    /// Authoring conventions, not runtime checks (this codebase trusts internally authored data):
    /// a <see cref="StatusType.Knockback"/> pushes away from the avatar's placeholder tile and
    /// should not be authored on a passive.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Avatar/Passive Skill", fileName = "NewPassiveSkill")]
    public class PassiveSkillSO : ScriptableObject
    {
        /// <summary>Default <see cref="HpThresholdPercent"/>.</summary>
        public const int DefaultHpThresholdPercent = 50;

        /// <summary>
        /// Stable string key persisted in save data (as a <see cref="SkillProgress.SkillId"/> in the
        /// avatar's passive book). Never rename after ship.
        /// </summary>
        public string PassiveId;

        /// <summary>Player-facing passive name.</summary>
        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Icon shown in the avatar's passive slots.</summary>
        public Sprite Icon;

        /// <summary>
        /// How this passive levels: max level, magnitude growth per level and breakthrough gates.
        /// The shared block, read by <see cref="SkillProgression"/> exactly as for a beast skill.
        /// </summary>
        public SkillProgressionDefinition Progression = new SkillProgressionDefinition();

        /// <summary>What makes this passive fire.</summary>
        public PassiveTrigger Trigger = PassiveTrigger.Aura;

        /// <summary>
        /// For <see cref="PassiveTrigger.AllyBelowHpPercent"/> only: the percent of max HP a beast
        /// must drop <em>below</em> (strictly) to trigger it, compared exactly in integers
        /// (<c>CurrentHp * 100 &lt; HpThresholdPercent * Stats.Hp</c>).
        /// </summary>
        public int HpThresholdPercent = DefaultHpThresholdPercent;

        /// <summary>
        /// Percent chance the passive fires when it is otherwise allowed to. Values of 0 or below,
        /// and above 100, read as 100 (Unity zero-fills a new list entry's ints, and a fresh passive
        /// must fire rather than never), matching <see cref="SkillEffect.Chance"/>. Drawn from the
        /// battle's rng only when below 100.
        /// </summary>
        public int ProcChance = SkillEffect.AlwaysChance;

        /// <summary>How many times it may fire in one battle. 0 or below is unlimited.</summary>
        public int MaxTriggersPerBattle;

        /// <summary>
        /// After firing, how many avatar ticks (one per player-beast turn, the clock the avatar's
        /// active skills also run on) must pass before it may fire again. 0 or below: no cooldown.
        /// </summary>
        public int InternalCooldown;

        /// <summary>Who the effects land on.</summary>
        public PassiveTarget TargetScope = PassiveTarget.AllAllies;

        /// <summary>The element a damage or damage-over-time effect attacks with, as <see cref="SkillSO.Element"/>.</summary>
        public Element Element = Element.None;

        /// <summary>Which attacking and defending stats a damage effect uses, as <see cref="SkillSO.Category"/>.</summary>
        public DamageCategory Category = DamageCategory.Physical;

        /// <summary>The effects applied when it fires, in authored order, to every target.</summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();
    }
}
