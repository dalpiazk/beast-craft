namespace BeastCraft.Avatar
{
    /// <summary>
    /// Who an avatar <see cref="PassiveSkillSO"/>'s effects land on when it fires. Every scope is
    /// position-free and draw-free, since the avatar is not on the grid.
    /// <para>
    /// Values are explicit and serialized into assets: append new scopes at the end with a new
    /// value, never renumber.
    /// </para>
    /// </summary>
    public enum PassiveTarget
    {
        /// <summary>Every living player beast, in id order.</summary>
        AllAllies = 0,

        /// <summary>
        /// The unit the trigger is about (see the design doc for each trigger's triggering unit),
        /// when it is alive. A passive with no living triggering unit does not fire.
        /// </summary>
        TriggeringUnit = 1,

        /// <summary>Every living enemy beast, in id order.</summary>
        AllEnemies = 2,

        /// <summary>
        /// The living player beast with the lowest <c>CurrentHp / Stats.Hp</c>, compared exactly
        /// in integers; ties go to the lower id.
        /// </summary>
        LowestHpFractionAlly = 3,
    }
}
