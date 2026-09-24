using BeastCraft.Creatures;

namespace BeastCraft.Battle
{
    /// <summary>
    /// One timed stat buff or debuff currently folded into a unit's <see cref="BattleUnit.Stats"/>,
    /// waiting to be taken back out again.
    /// <para>
    /// Stores the signed delta that was <em>actually</em> applied rather than the authored
    /// <see cref="SkillEffect.Magnitude"/> it came from. Those differ whenever the application was
    /// clamped — a -8 Attack debuff on a unit with 5 Attack only ever moved the stat by 5 — and
    /// reverting the authored number in that case would hand the unit free stats every time a big
    /// debuff wore off. Holding the real delta makes expiry exactly "subtract this back out".
    /// </para>
    /// <para>
    /// A reference type on purpose, like <c>SkillLoadout.Slot</c>:
    /// <see cref="SkillEffectApplier.TickModifiers"/> writes <see cref="RemainingTurns"/> in place,
    /// and a struct in a <see cref="System.Collections.Generic.List{T}"/> would hand out copies
    /// that silently swallowed the write.
    /// </para>
    /// <para>
    /// Only ever created for a <see cref="SkillEffect.DurationTurns"/> greater than 0. An instant
    /// effect is a permanent change with nothing to revert, so it is never tracked here.
    /// </para>
    /// </summary>
    public class ActiveStatModifier
    {
        public ActiveStatModifier(StatType stat, int delta, int remainingTurns, SkillEffect origin = null)
        {
            Stat = stat;
            Delta = delta;
            RemainingTurns = remainingTurns;
            Origin = origin;
        }

        /// <summary>
        /// The authored effect that applied this modifier, or <c>null</c> when it was built by hand.
        /// Stacks are counted per origin against <see cref="SkillEffect.MaxStacks"/>: two different
        /// effects on the same stat never share a cap.
        /// </summary>
        public SkillEffect Origin { get; }

        /// <summary>The stat axis this modifier moved.</summary>
        public StatType Stat { get; }

        /// <summary>
        /// The signed amount already added to <see cref="Stat"/> — positive for a buff, negative
        /// for a debuff. Expiry subtracts exactly this. Fixed at construction: the applied amount
        /// cannot change once it has been folded in, and a second buff on the same stat is tracked
        /// as its own entry rather than merged into this one, so each reverts on its own clock.
        /// </summary>
        public int Delta { get; }

        /// <summary>
        /// Turns left before this modifier reverts, counted in the <em>affected</em> unit's own
        /// turns — not the caster's, which need not coincide. Ticked down by
        /// <see cref="SkillEffectApplier.TickModifiers"/>; the modifier reverts and is dropped on
        /// the tick that brings this to 0.
        /// </summary>
        public int RemainingTurns { get; set; }
    }
}
