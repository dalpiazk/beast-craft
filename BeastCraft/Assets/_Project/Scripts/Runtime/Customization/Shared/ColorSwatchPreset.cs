using System;
using UnityEngine;

namespace BeastCraft.Customization
{
    /// <summary>
    /// A curated quick-pick swatch shown alongside a color picker. The picker itself is a
    /// continuous shader-driven hue/palette control; presets are only suggested starting points.
    /// </summary>
    [Serializable]
    public class ColorSwatchPreset
    {
        /// <summary>Player-facing label for the swatch (e.g. "Ash", "Ember").</summary>
        public string DisplayName;

        /// <summary>The color this swatch applies.</summary>
        public Color Color = Color.white;

        /// <summary>
        /// Purely a UI hint marking the recommended swatch. The authoritative default for a
        /// color category is <see cref="ColorPickerDefinition.DefaultColor"/>.
        /// </summary>
        public bool IsDefault;
    }
}
