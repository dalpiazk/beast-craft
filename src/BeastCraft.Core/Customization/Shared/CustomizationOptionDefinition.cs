using System;
using System.Collections.Generic;

namespace BeastCraft.Customization
{
    /// <summary>
    /// A single selectable entry inside a <see cref="CustomizationCategoryDefinition"/> whose
    /// <see cref="CustomizationValueType"/> is <see cref="CustomizationValueType.DiscreteOption"/>.
    /// Authored inline as a list entry on the category asset, not as its own asset.
    /// </summary>
    [Serializable]
    public class CustomizationOptionDefinition
    {
        /// <summary>Stable string key persisted in save data. Never rename after ship.</summary>
        public string OptionId;

        /// <summary>Player-facing label.</summary>
        public string DisplayName;

        /// <summary>Thumbnail shown in the customization UI picker grid. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string PreviewIcon;

        // Single-layer 2D composited art only for now; multi-angle / multi-sprite options
        // (e.g. a shirt needing separate front/side/back sprites) are a later extension.
        /// <summary>The sprite layer this option contributes to the composited character. Engine-neutral art key the host renderer resolves (null = none).</summary>
        public string ArtLayer;

        /// <summary>Draw order within the composited layer stack. Lower draws first (further back).</summary>
        public int SortOrder;

        /// <summary>
        /// Exactly one option per category must be flagged default. Enforced by
        /// <see cref="CustomizationCategoryDefinition.OnValidate"/> at author time and
        /// defensively at runtime by <see cref="CustomizationCategoryDefinition.GetDefaultOption"/>.
        /// </summary>
        public bool IsDefault;

        /// <summary>
        /// True when <see cref="ArtLayer"/> is a grayscale/mask texture intended for the
        /// hue-shift palette shader rather than fixed-color art.
        /// </summary>
        public bool SupportsPaletteShader;

        /// <summary>
        /// Tags used later for cosmetic IAP / unlock gating. Empty means freely available.
        /// Data only at this stage; no unlock logic is implemented yet.
        /// </summary>
        public List<string> UnlockTags = new List<string>();
    }
}
