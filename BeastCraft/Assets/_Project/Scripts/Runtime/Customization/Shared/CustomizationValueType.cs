namespace BeastCraft.Customization
{
    /// <summary>
    /// How a customization category is authored and presented to the player.
    /// </summary>
    public enum CustomizationValueType
    {
        /// <summary>A fixed list of authored options (hair styles, ear shapes, clothing pieces...).</summary>
        DiscreteOption = 0,

        /// <summary>A continuous, shader-driven color/hue control (skin tone, fur color...).</summary>
        ColorPicker = 1
    }
}
