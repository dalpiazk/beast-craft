using System;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Vfx
{
    /// <summary>
    /// A painted frame's curves (<see cref="VfxPaintedData"/>) evaluated at a normalized time t
    /// (0-1): piecewise linear between keys, clamped to the first and last key outside them. Pure
    /// and allocation-free, so a painted layer is as deterministic as a flipbook.
    /// </summary>
    public static class VfxPainted
    {
        /// <summary>
        /// The value of <paramref name="keys"/> at <paramref name="t"/>, or
        /// <paramref name="fallback"/> for an empty (or null) curve.
        /// </summary>
        public static float Evaluate(VfxCurveKey[] keys, float t, float fallback)
        {
            if (keys == null || keys.Length == 0)
            {
                return fallback;
            }

            VfxCurveKey previous = null;
            foreach (VfxCurveKey key in keys)
            {
                if (key == null)
                {
                    continue;
                }

                if (t <= key.T)
                {
                    if (previous == null || key.T <= previous.T)
                    {
                        return key.V;
                    }

                    float k = (t - previous.T) / (key.T - previous.T);
                    return previous.V + (key.V - previous.V) * k;
                }

                previous = key;
            }

            return previous == null ? fallback : previous.V;
        }

        /// <summary>Whether <paramref name="keys"/> has any key (an empty curve leaves the part's own behaviour).</summary>
        public static bool Has(VfxCurveKey[] keys)
        {
            return keys != null && keys.Length > 0;
        }

        /// <summary>
        /// The tint of <paramref name="keys"/> at <paramref name="t"/> as two palette chars and the
        /// mix between them (0 = <paramref name="from"/>, 1 = <paramref name="to"/>): the renderer
        /// blends the two colours. An empty curve gives <paramref name="fallback"/> unmixed.
        /// </summary>
        public static void Tint(VfxTintKey[] keys, float t, string fallback, out string from, out string to, out float mix)
        {
            from = fallback;
            to = fallback;
            mix = 0f;
            if (keys == null || keys.Length == 0)
            {
                return;
            }

            VfxTintKey previous = null;
            foreach (VfxTintKey key in keys)
            {
                if (key == null)
                {
                    continue;
                }

                if (t <= key.T)
                {
                    if (previous == null || key.T <= previous.T)
                    {
                        from = key.Color;
                        to = key.Color;
                        return;
                    }

                    from = previous.Color;
                    to = key.Color;
                    mix = (t - previous.T) / (key.T - previous.T);
                    return;
                }

                previous = key;
            }

            if (previous != null)
            {
                from = previous.Color;
                to = previous.Color;
            }
        }

        /// <summary>
        /// <paramref name="sprite"/> drawn as <paramref name="painted"/>'s frame at time
        /// <paramref name="t"/>: its sheet (frame 0), its size times the Scale curve, turned by the
        /// Rotation curve (turns) plus <paramref name="spin"/> (radians), its opacity
        /// <paramref name="envelope"/> times the Alpha curve when there is one (else
        /// <paramref name="builtInAlpha"/>: the part's own fade), tinted by the Tint curve (else its
        /// own tint), marked painted. Null <paramref name="painted"/> returns the sprite unchanged.
        /// </summary>
        public static VfxSprite Apply(VfxPaintedData painted, VfxSprite sprite, float t, float envelope, float builtInAlpha, float spin = 0f)
        {
            if (painted == null)
            {
                return sprite;
            }

            float scale = Evaluate(painted.Scale, t, 1f);
            float turns = Evaluate(painted.Rotation, t, 0f);
            float alpha = Has(painted.Alpha) ? envelope * Math.Max(0f, Math.Min(1f, Evaluate(painted.Alpha, t, 1f))) : builtInAlpha;
            Tint(painted.Tint, t, sprite.Tint, out string from, out string to, out float mix);
            return new VfxSprite(painted.Sheet, 0, sprite.Position, sprite.Scale * scale, sprite.SizePx * scale, sprite.Rotation + spin + turns * (float)(Math.PI * 2.0),
                                 alpha, from, sprite.Additive, sprite.Ground, to, mix, true);
        }

        /// <summary>A deterministic angle in [0, 2 pi) for sprite <paramref name="index"/> of part <paramref name="part"/> under <paramref name="seed"/>.</summary>
        public static float SpinOf(int seed, int part, int index)
        {
            uint bits = DeterministicRandom.Hash(unchecked(seed * 977 + part), index);
            return (float)((bits >> 8) / 16777216.0 * Math.PI * 2.0);
        }
    }
}
