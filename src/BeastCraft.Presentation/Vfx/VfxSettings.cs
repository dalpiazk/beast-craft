using BeastCraft.Save;

namespace BeastCraft.Presentation.Vfx
{
    /// <summary>
    /// The player's effects settings as the VFX timeline reads them (from
    /// <see cref="PlayerSettings"/>: <see cref="PlayerSettings.EffectsIntensity"/>,
    /// <see cref="PlayerSettings.ScreenShake"/>, <see cref="PlayerSettings.Flashes"/>). An input to
    /// <see cref="VfxTimeline"/> like its seed: the same spec, positions, seed and settings always
    /// give the same frames. Immutable.
    /// <list type="bullet">
    /// <item><b>Full</b>: everything authored.</item>
    /// <item><b>Reduced</b>: no <c>Glyphs</c> layers and no <c>Particles</c> layers (the secondary
    /// bursts), and the effect's own particle burst at half its count.</item>
    /// <item><b>Minimal</b>: only the damage number, the hit flash and one simple burst (the
    /// effect's own particle burst, else its first particle layer's, at half the count). No
    /// projectile, flipbook, layers or shake; the hit still lands when it would have.</item>
    /// <item><b>ScreenShake</b> off: no shake at any intensity.</item>
    /// <item><b>Flashes</b> off (accessibility): no hit flash, and no bright additive bursts —
    /// additive flipbooks (the effect's own and its <c>Flipbook</c> layers) and additive
    /// <c>RadialBurst</c> layers.</item>
    /// </list>
    /// </summary>
    public sealed class VfxSettings
    {
        /// <summary>Full effects, shake on, flashes on: what an absent or default settings file means.</summary>
        public static readonly VfxSettings Default = new VfxSettings(EffectsIntensity.Full, true, true);

        public VfxSettings(EffectsIntensity intensity, bool screenShake, bool flashes)
        {
            Intensity = intensity == EffectsIntensity.Reduced || intensity == EffectsIntensity.Minimal ? intensity : EffectsIntensity.Full;
            ScreenShake = screenShake;
            Flashes = flashes;
        }

        /// <summary>How much plays (an unknown value reads as <see cref="EffectsIntensity.Full"/>).</summary>
        public EffectsIntensity Intensity { get; }

        public bool ScreenShake { get; }

        public bool Flashes { get; }

        /// <summary>The settings from the player's <paramref name="settings"/> (null: <see cref="Default"/>).</summary>
        public static VfxSettings From(PlayerSettings settings)
        {
            return settings == null ? Default : new VfxSettings(settings.EffectsIntensity, settings.ScreenShake, settings.Flashes);
        }

        /// <summary>
        /// How many particles a burst of <paramref name="count"/> plays: all of them at Full, half
        /// (rounded up, so a burst never vanishes) at Reduced and Minimal.
        /// </summary>
        public int ParticleCount(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return Intensity == EffectsIntensity.Full ? count : (count + 1) / 2;
        }

        public override string ToString()
        {
            return Intensity + (ScreenShake ? " shake" : " no-shake") + (Flashes ? " flashes" : " no-flashes");
        }
    }
}
