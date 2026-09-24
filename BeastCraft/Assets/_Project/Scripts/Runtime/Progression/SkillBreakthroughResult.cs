namespace BeastCraft.Progression
{
    /// <summary>
    /// How a <see cref="SkillProgression.TryBreakthrough"/> attempt ended. Only
    /// <see cref="Success"/> changes the progress, and only then should the caller consume the
    /// material.
    /// <para>
    /// Values are explicit and may be persisted or logged by number: append new results at the
    /// end with a new value, never renumber.
    /// </para>
    /// </summary>
    public enum SkillBreakthroughResult
    {
        /// <summary>The gate was passed: <see cref="SkillProgress.Tier"/> went up by one.</summary>
        Success = 0,

        /// <summary>Every authored gate has already been passed; there is nothing to break through.</summary>
        NoTierRemaining = 1,

        /// <summary>The skill has not yet reached the next gate's threshold level.</summary>
        BelowThreshold = 2,

        /// <summary>The material's tier is below the gate's <see cref="SkillTierDefinition.RequiredMaterialTier"/>.</summary>
        MaterialTierTooLow = 3,

        /// <summary>The progress or the material was null.</summary>
        MissingInput = 4,
    }
}
