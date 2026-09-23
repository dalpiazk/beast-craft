using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Progression;
using UnityEngine;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One avatar passive as a battle uses it: the authored <see cref="PassiveSkillSO"/> at the
    /// level and breakthrough tier the avatar has brought it to, plus its per-battle state — how
    /// often it has fired, its internal cooldown, and which beasts it has already reacted to
    /// dropping below its HP threshold. Built fresh for every battle (see
    /// <see cref="PassiveLoadout"/>), the way a <see cref="SkillLoadout"/> is.
    /// <para>
    /// <strong>How it reuses the skill engine.</strong> A passive is applied exactly like a fired
    /// skill: the instance builds a private carrier <see cref="SkillSO"/> that shares the passive's
    /// <see cref="PassiveSkillSO.Effects"/>, <see cref="PassiveSkillSO.Progression"/>,
    /// <see cref="PassiveSkillSO.Element"/> and <see cref="PassiveSkillSO.Category"/>, wraps it in a
    /// <see cref="SkillInstance"/> at the passive's level and tier (<see cref="Skill"/>), and hands
    /// <see cref="SkillEffectApplier"/> an ordinary <see cref="SkillActivation"/> of it with the
    /// avatar as the caster. So level scaling, tier bonus effects, damage, crits, chance and
    /// statuses all behave for a passive exactly as they do for a skill, with no second copy of
    /// any rule. The carrier's targeting fields are set so <see cref="SkillTargetResolver"/> resolves
    /// the passive's <see cref="PassiveSkillSO.TargetScope"/>. The carrier is created with
    /// <see cref="ScriptableObject.CreateInstance{T}"/> and never saved; in Unity it is reclaimed by
    /// the next <c>Resources.UnloadUnusedAssets</c> once the battle is dropped.
    /// </para>
    /// <para>
    /// The authored gating values are captured at construction (clamped: proc chance into
    /// (0, 100], negative limits to 0) so a battle cannot change pace if the asset is edited
    /// mid-fight, the same rule <see cref="SkillLoadout"/> follows for cooldowns. A null passive is
    /// tolerated and simply never fires.
    /// </para>
    /// </summary>
    public sealed class PassiveInstance
    {
        private readonly HashSet<BattleUnit> _belowThreshold = new HashSet<BattleUnit>();

        /// <summary>
        /// The passive at <paramref name="level"/> and <paramref name="tier"/>, each clamped into
        /// the passive's <see cref="PassiveSkillSO.Progression"/> exactly as
        /// <see cref="SkillInstance"/> clamps a skill's.
        /// </summary>
        public PassiveInstance(PassiveSkillSO passive, int level = 1, int tier = 0)
        {
            Passive = passive;

            if (passive == null)
            {
                return;
            }

            Skill = new SkillInstance(BuildCarrier(passive), level, tier);
            Trigger = passive.Trigger;
            TargetScope = passive.TargetScope;
            HpThresholdPercent = passive.HpThresholdPercent;
            ProcChance = passive.ProcChance <= 0 || passive.ProcChance > SkillEffect.AlwaysChance ? SkillEffect.AlwaysChance : passive.ProcChance;
            MaxTriggersPerBattle = passive.MaxTriggersPerBattle < 0 ? 0 : passive.MaxTriggersPerBattle;
            InternalCooldown = passive.InternalCooldown < 0 ? 0 : passive.InternalCooldown;
        }

        /// <summary>
        /// The passive at the level and tier recorded in <paramref name="progress"/>; level 1, tier
        /// 0 for a null progress. The progress's XP plays no part in battle.
        /// </summary>
        public static PassiveInstance FromProgress(PassiveSkillSO passive, SkillProgress progress)
        {
            return progress == null ? new PassiveInstance(passive) : new PassiveInstance(passive, progress.Level, progress.Tier);
        }

        /// <summary>The authored passive. May be <c>null</c>, in which case nothing else is set and it never fires.</summary>
        public PassiveSkillSO Passive { get; }

        /// <summary>
        /// The passive as a leveled skill: its effects (authored, then each passed gate's bonus
        /// effects) and its magnitude multiplier. <c>null</c> only when <see cref="Passive"/> is.
        /// </summary>
        public SkillInstance Skill { get; }

        /// <summary>The passive's level, 1-based, already clamped. 1 for a null passive.</summary>
        public int Level
        {
            get { return Skill == null ? 1 : Skill.Level; }
        }

        /// <summary>How many breakthrough gates the passive has passed, already clamped.</summary>
        public int Tier
        {
            get { return Skill == null ? 0 : Skill.Tier; }
        }

        /// <summary>The captured <see cref="PassiveSkillSO.Trigger"/>.</summary>
        public PassiveTrigger Trigger { get; }

        /// <summary>The captured <see cref="PassiveSkillSO.TargetScope"/>.</summary>
        public PassiveTarget TargetScope { get; }

        /// <summary>The captured <see cref="PassiveSkillSO.HpThresholdPercent"/>.</summary>
        public int HpThresholdPercent { get; }

        /// <summary>The captured <see cref="PassiveSkillSO.ProcChance"/>, in (0, 100].</summary>
        public int ProcChance { get; }

        /// <summary>The captured <see cref="PassiveSkillSO.MaxTriggersPerBattle"/>; 0 is unlimited.</summary>
        public int MaxTriggersPerBattle { get; }

        /// <summary>The captured <see cref="PassiveSkillSO.InternalCooldown"/>, in avatar ticks.</summary>
        public int InternalCooldown { get; }

        /// <summary>How many times it has fired this battle.</summary>
        public int TriggerCount { get; private set; }

        /// <summary>Avatar ticks left before it may fire again; 0 when it is off cooldown.</summary>
        public int CooldownRemaining { get; private set; }

        /// <summary>Whether it has fired <see cref="MaxTriggersPerBattle"/> times and will not fire again this battle.</summary>
        public bool IsSpent
        {
            get { return MaxTriggersPerBattle > 0 && TriggerCount >= MaxTriggersPerBattle; }
        }

        /// <summary>Whether it may fire now: it has a passive, is not spent, and is off cooldown.</summary>
        internal bool IsReady
        {
            get { return Skill != null && !IsSpent && CooldownRemaining == 0; }
        }

        /// <summary>
        /// The beasts this passive has already reacted to crossing below its threshold; one is
        /// removed when it is next seen at or above it, which re-arms the passive for that beast.
        /// </summary>
        internal HashSet<BattleUnit> BelowThreshold
        {
            get { return _belowThreshold; }
        }

        /// <summary>Counts one firing and starts the internal cooldown.</summary>
        internal void MarkTriggered()
        {
            TriggerCount += 1;
            CooldownRemaining = InternalCooldown;
        }

        /// <summary>One avatar tick off the internal cooldown, floored at 0.</summary>
        internal void TickCooldown()
        {
            if (CooldownRemaining > 0)
            {
                CooldownRemaining -= 1;
            }
        }

        /// <summary>
        /// Who the passive would land on right now: <see cref="PassiveTarget.AllAllies"/> and
        /// <see cref="PassiveTarget.AllEnemies"/> through <see cref="SkillTargetResolver.ResolveTargets"/>
        /// (living units of that side, in id order), <see cref="PassiveTarget.LowestHpFractionAlly"/>
        /// through <see cref="SkillTargetResolver.PickFocusIgnoringRange"/> with the
        /// <see cref="SkillTargetingCriterion.HpFraction"/> criterion, and
        /// <see cref="PassiveTarget.TriggeringUnit"/> as <paramref name="triggeringUnit"/> when it is
        /// alive. None of them draws from <paramref name="rng"/>. Never <c>null</c>.
        /// </summary>
        internal IReadOnlyList<BattleUnit> ResolveTargets(BattleUnit avatar, IEnumerable<BattleUnit> allUnits, BattleUnit triggeringUnit, System.Random rng)
        {
            List<BattleUnit> none = new List<BattleUnit>();

            if (Skill == null || avatar == null)
            {
                return none;
            }

            switch (TargetScope)
            {
                case PassiveTarget.AllAllies:
                case PassiveTarget.AllEnemies:
                    return SkillTargetResolver.ResolveTargets(Skill.Skill, avatar, allUnits, null, rng);

                case PassiveTarget.LowestHpFractionAlly:
                    BattleUnit lowest = SkillTargetResolver.PickFocusIgnoringRange(Skill.Skill, avatar, allUnits, rng);
                    if (lowest != null)
                    {
                        none.Add(lowest);
                    }

                    return none;

                case PassiveTarget.TriggeringUnit:
                    if (triggeringUnit != null && !triggeringUnit.IsDefeated)
                    {
                        none.Add(triggeringUnit);
                    }

                    return none;

                default:
                    return none;
            }
        }

        /// <summary>
        /// The carrier skill: the passive's id, name, element, category, effects and progression
        /// (shared references, not copies), with targeting fields that make the resolver produce
        /// the passive's scope. Its cooldown and range are never read.
        /// </summary>
        private static SkillSO BuildCarrier(PassiveSkillSO passive)
        {
            SkillSO carrier = ScriptableObject.CreateInstance<SkillSO>();
            carrier.name = passive.name;
            carrier.SkillId = passive.PassiveId;
            carrier.DisplayName = passive.DisplayName;
            carrier.Element = passive.Element;
            carrier.Category = passive.Category;
            carrier.Effects = passive.Effects;
            carrier.Progression = passive.Progression;
            carrier.Range = 0;

            switch (passive.TargetScope)
            {
                case PassiveTarget.AllAllies:
                    carrier.TargetShape = SkillTargetShape.AllAllies;
                    carrier.TargetSide = SkillTargetSide.Ally;
                    break;

                case PassiveTarget.AllEnemies:
                    carrier.TargetShape = SkillTargetShape.AllEnemies;
                    carrier.TargetSide = SkillTargetSide.Enemy;
                    break;

                case PassiveTarget.LowestHpFractionAlly:
                    carrier.TargetShape = SkillTargetShape.SingleTarget;
                    carrier.TargetSide = SkillTargetSide.Ally;
                    carrier.TargetingCriterion = SkillTargetingCriterion.HpFraction;
                    carrier.TargetingOrder = SkillTargetingOrder.Lowest;
                    break;

                default:
                    carrier.TargetShape = SkillTargetShape.Self;
                    carrier.TargetSide = SkillTargetSide.Ally;
                    break;
            }

            return carrier;
        }
    }
}
