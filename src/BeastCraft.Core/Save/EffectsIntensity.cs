namespace BeastCraft.Save
{
    /// <summary>
    /// How much of a skill's visual effect the battle viewer plays (<see cref="PlayerSettings.EffectsIntensity"/>).
    /// Presentation only: a battle plays out identically at every setting. Stored as its number,
    /// so never renumber a member; a value this build does not know reads as <see cref="Full"/>.
    /// </summary>
    public enum EffectsIntensity
    {
        /// <summary>Everything the VFX library authors. The default.</summary>
        Full = 0,

        /// <summary>No glyph rings and no secondary (layer) particle bursts, and half the particles.</summary>
        Reduced = 1,

        /// <summary>Only the damage number, the hit flash (when flashes are on) and one simple burst.</summary>
        Minimal = 2
    }
}
