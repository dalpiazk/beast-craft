using System;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Audio
{
    /// <summary>
    /// The engine-neutral arithmetic of the mix, shared by the MonoGame players and the tests: the
    /// volumes the settings give, each stem's weight at an intensity, a fade step, and the float to
    /// 16-bit conversion the music stream submits.
    /// </summary>
    public static class MusicMix
    {
        /// <summary>How long a layer takes to fade fully in or out when the intensity changes.</summary>
        public const float LayerFadeMs = 1500f;

        /// <summary>The crossfade between screens' tracks.</summary>
        public const int CrossfadeMs = 1200;

        /// <summary>The music volume the settings give, 0-1: master times music, 0 when muted.</summary>
        public static float MusicVolume(PlayerSettings settings)
        {
            return settings == null ? 1f : settings.Muted ? 0f : Percent(settings.MasterVolume) * Percent(settings.MusicVolume);
        }

        /// <summary>The sound effects' volume the settings give, 0-1: master times effects, 0 when muted.</summary>
        public static float SfxVolume(PlayerSettings settings)
        {
            return settings == null ? 1f : settings.Muted ? 0f : Percent(settings.MasterVolume) * Percent(settings.SfxVolume);
        }

        /// <summary>A 0-100 setting as 0-1 (outside the range reads as the nearest end).</summary>
        public static float Percent(int value)
        {
            return Math.Max(0, Math.Min(100, value)) / 100f;
        }

        /// <summary>
        /// Each stem's weight at <paramref name="intensity"/> (0-1) for a track of
        /// <paramref name="stems"/> stems: the base stem always plays; the others come in one after
        /// another as the intensity rises, each over an equal share of it (with 4 stems: the second
        /// from 0 to 1/3, the third from 1/3 to 2/3, the fourth from 2/3 to 1).
        /// </summary>
        public static float[] LayerWeights(float intensity, int stems)
        {
            float[] weights = new float[Math.Max(0, stems)];
            if (weights.Length == 0)
            {
                return weights;
            }

            weights[0] = 1f;
            float scaled = Clamp01(intensity) * (weights.Length - 1);
            for (int i = 1; i < weights.Length; i++)
            {
                weights[i] = Clamp01(scaled - (i - 1));
            }

            return weights;
        }

        /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by <paramref name="elapsedMs"/> of a <paramref name="fullMs"/> fade.</summary>
        public static float Step(float current, float target, float elapsedMs, float fullMs)
        {
            float step = fullMs <= 0f ? 1f : Math.Max(0f, elapsedMs) / fullMs;
            return current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);
        }

        /// <summary>A mixed sample as 16-bit PCM (clipped to the range).</summary>
        public static short ToPcm16(float sample)
        {
            float clipped = Math.Max(-1f, Math.Min(1f, sample));
            return (short)Math.Round(clipped * short.MaxValue);
        }

        public static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
