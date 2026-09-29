using System;

namespace BeastCraft.Presentation.Ui
{
    /// <summary>
    /// Hue, saturation and value, each 0-1 (hue 0 and 1 are both red): the colour picker's three sliders. Alpha is
    /// not part of it (a picked colour is opaque). Converts to and from 0-1 RGB and <c>#RRGGBB</c>.
    /// </summary>
    public readonly struct Hsv
    {
        public Hsv(float h, float s, float v)
        {
            H = Clamp(h);
            S = Clamp(s);
            V = Clamp(v);
        }

        public float H { get; }

        public float S { get; }

        public float V { get; }

        /// <summary>The colour as 0-1 red, green and blue.</summary>
        public void ToRgb(out float r, out float g, out float b)
        {
            float h = (H >= 1f ? 0f : H) * 6f;
            int sector = (int)Math.Floor(h);
            float f = h - sector;
            float p = V * (1f - S);
            float q = V * (1f - S * f);
            float t = V * (1f - S * (1f - f));
            switch (sector)
            {
                case 0:
                    r = V;
                    g = t;
                    b = p;
                    break;
                case 1:
                    r = q;
                    g = V;
                    b = p;
                    break;
                case 2:
                    r = p;
                    g = V;
                    b = t;
                    break;
                case 3:
                    r = p;
                    g = q;
                    b = V;
                    break;
                case 4:
                    r = t;
                    g = p;
                    b = V;
                    break;
                default:
                    r = V;
                    g = p;
                    b = q;
                    break;
            }
        }

        /// <summary>The colour as <c>#RRGGBB</c>.</summary>
        public string ToHex()
        {
            ToRgb(out float r, out float g, out float b);
            return "#" + Byte(r).ToString("X2") + Byte(g).ToString("X2") + Byte(b).ToString("X2");
        }

        /// <summary>The HSV of 0-1 <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/> (a grey keeps hue 0).</summary>
        public static Hsv FromRgb(float r, float g, float b)
        {
            r = Clamp(r);
            g = Clamp(g);
            b = Clamp(b);
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float h = 0f;
            if (delta > 0f)
            {
                if (max == r)
                {
                    h = (g - b) / delta;
                }
                else if (max == g)
                {
                    h = 2f + (b - r) / delta;
                }
                else
                {
                    h = 4f + (r - g) / delta;
                }

                h /= 6f;
                if (h < 0f)
                {
                    h += 1f;
                }
            }

            return new Hsv(h, max <= 0f ? 0f : delta / max, max);
        }

        private static int Byte(float value)
        {
            return (int)Math.Round(Clamp(value) * 255f);
        }

        private static float Clamp(float value)
        {
            return float.IsNaN(value) ? 0f : value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
