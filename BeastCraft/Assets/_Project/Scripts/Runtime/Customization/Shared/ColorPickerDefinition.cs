using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeastCraft.Customization
{
    /// <summary>
    /// Authoring data for a continuous color category (skin tone, hair color, creature color...).
    /// The allowed saturation/value window keeps players inside an art-directed range while still
    /// giving them a free hue choice.
    /// </summary>
    [Serializable]
    public class ColorPickerDefinition
    {
        /// <summary>The color applied when the player has made no choice.</summary>
        public Color DefaultColor = Color.white;

        [Range(0f, 1f)] public float MinSaturation = 0f;
        [Range(0f, 1f)] public float MaxSaturation = 1f;
        [Range(0f, 1f)] public float MinValue = 0f;
        [Range(0f, 1f)] public float MaxValue = 1f;

        /// <summary>Curated quick-pick swatches surfaced in the UI. Optional.</summary>
        public List<ColorSwatchPreset> Presets = new List<ColorSwatchPreset>();

        /// <summary>
        /// Returns <see cref="DefaultColor"/> clamped into the authored saturation/value window,
        /// so a misconfigured default can never resolve to a color outside the allowed range.
        /// Alpha is preserved from <see cref="DefaultColor"/>.
        /// </summary>
        public Color GetDefaultColor()
        {
            // Tolerate inverted min/max authoring rather than returning garbage; OnValidate
            // on the owning category is what tells the designer the asset is wrong.
            float minSaturation = Mathf.Min(MinSaturation, MaxSaturation);
            float maxSaturation = Mathf.Max(MinSaturation, MaxSaturation);
            float minValue = Mathf.Min(MinValue, MaxValue);
            float maxValue = Mathf.Max(MinValue, MaxValue);

            float hue;
            float saturation;
            float value;
            Color.RGBToHSV(DefaultColor, out hue, out saturation, out value);

            saturation = Mathf.Clamp(saturation, minSaturation, maxSaturation);
            value = Mathf.Clamp(value, minValue, maxValue);

            Color clamped = Color.HSVToRGB(hue, saturation, value);
            clamped.a = DefaultColor.a;
            return clamped;
        }
    }
}
