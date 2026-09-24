using System;

namespace BeastCraft
{
    /// <summary>
    /// The handful of float/int helpers the game math uses, with the exact semantics of the
    /// Unity-era <c>Mathf</c> calls they replace (stat math and balance numbers depend on them).
    /// </summary>
    public static class MathUtil
    {
        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Floor(float value)
        {
            return (float)Math.Floor(value);
        }

        /// <summary>
        /// Rounds to the nearest integer, .5 to the nearest even integer (banker's rounding, as
        /// Unity's <c>Mathf.RoundToInt</c> does), i.e. <see cref="MidpointRounding.ToEven"/>.
        /// </summary>
        public static int RoundToInt(float value)
        {
            return (int)Math.Round(value, MidpointRounding.ToEven);
        }
    }
}
