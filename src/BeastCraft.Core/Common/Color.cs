using System;

namespace BeastCraft
{
    /// <summary>
    /// An RGBA colour (each channel 0-1) with RGB/HSV conversion. Engine-neutral: the game only does
    /// colour math and stores colours in saves; renderers convert at their own boundary.
    /// <para>
    /// A verbatim port of the Unity-era <c>Color</c> surface the game used (the same
    /// public fields <c>r</c>/<c>g</c>/<c>b</c>/<c>a</c>, so saved colours keep their JSON shape,
    /// and Unity's own HSV algorithm, so clamped default colours are unchanged).
    /// </para>
    /// </summary>
    [Serializable]
    public struct Color
    {
        // Lower-cased deliberately: these are the save format's field names.
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b)
            : this(r, g, b, 1f)
        {
        }

        public static readonly Color white = new Color(1f, 1f, 1f, 1f);

        public static readonly Color black = new Color(0f, 0f, 0f, 1f);

        public static readonly Color clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// Converts an RGB colour to hue/saturation/value, all in the 0-1 range, treating a
        /// zero-value (black) colour as hue 0, saturation 0.
        /// </summary>
        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            if (rgbColor.b > rgbColor.g && rgbColor.b > rgbColor.r)
            {
                RGBToHSVHelper(4f, rgbColor.b, rgbColor.r, rgbColor.g, out H, out S, out V);
            }
            else if (rgbColor.g > rgbColor.r)
            {
                RGBToHSVHelper(2f, rgbColor.g, rgbColor.b, rgbColor.r, out H, out S, out V);
            }
            else
            {
                RGBToHSVHelper(0f, rgbColor.r, rgbColor.g, rgbColor.b, out H, out S, out V);
            }
        }

        /// <summary>Converts hue/saturation/value (each 0-1) back to an opaque RGB colour (sector-based).</summary>
        public static Color HSVToRGB(float H, float S, float V)
        {
            Color result = white;

            if (S == 0f)
            {
                result.r = V;
                result.g = V;
                result.b = V;
                return result;
            }

            if (V == 0f)
            {
                result.r = 0f;
                result.g = 0f;
                result.b = 0f;
                return result;
            }

            result.r = 0f;
            result.g = 0f;
            result.b = 0f;

            float hueSixths = H * 6f;
            int sector = (int)MathUtil.Floor(hueSixths);
            float fraction = hueSixths - sector;
            float p = V * (1f - S);
            float q = V * (1f - (S * fraction));
            float t = V * (1f - (S * (1f - fraction)));

            switch (sector)
            {
                case 0:
                    result.r = V;
                    result.g = t;
                    result.b = p;
                    break;
                case 1:
                    result.r = q;
                    result.g = V;
                    result.b = p;
                    break;
                case 2:
                    result.r = p;
                    result.g = V;
                    result.b = t;
                    break;
                case 3:
                    result.r = p;
                    result.g = q;
                    result.b = V;
                    break;
                case 4:
                    result.r = t;
                    result.g = p;
                    result.b = V;
                    break;
                case 5:
                    result.r = V;
                    result.g = p;
                    result.b = q;
                    break;
                case 6:
                    // H == 1 wraps back onto the start of the red sector.
                    result.r = V;
                    result.g = t;
                    result.b = p;
                    break;
                case -1:
                    result.r = V;
                    result.g = p;
                    result.b = q;
                    break;
                default:
                    // Out-of-range hue: black rather than extrapolating (Unity's behaviour).
                    break;
            }

            return result;
        }

        public override string ToString()
        {
            return "RGBA(" + r + ", " + g + ", " + b + ", " + a + ")";
        }

        private static void RGBToHSVHelper(
            float offset,
            float dominantColor,
            float colorOne,
            float colorTwo,
            out float H,
            out float S,
            out float V)
        {
            V = dominantColor;

            if (V == 0f)
            {
                S = 0f;
                H = 0f;
                return;
            }

            float smallest = colorOne > colorTwo ? colorTwo : colorOne;
            float difference = V - smallest;

            if (difference != 0f)
            {
                S = difference / V;
                H = offset + ((colorOne - colorTwo) / difference);
            }
            else
            {
                S = 0f;
                H = offset + (colorOne - colorTwo);
            }

            H /= 6f;

            if (H < 0f)
            {
                H += 1f;
            }
        }
    }
}
