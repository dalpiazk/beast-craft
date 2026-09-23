namespace BeastCraft.Battle
{
    /// <summary>
    /// Which pair of stats a damaging skill is resolved with (see <see cref="DamageFormula"/>):
    /// <see cref="Physical"/> reads the caster's <c>Attack</c> against the target's <c>Defense</c>,
    /// <see cref="Special"/> the caster's <c>SpecialAttack</c> against the target's
    /// <c>SpecialDefense</c>. Authored per skill as <see cref="SkillSO.Category"/>.
    /// <para>
    /// Values are explicit and serialized into authored assets by number. Never rename or
    /// renumber after ship; append new categories at the end with a new value.
    /// </para>
    /// </summary>
    public enum DamageCategory
    {
        Physical = 0,
        Special = 1
    }
}
