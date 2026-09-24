
namespace BeastCraft.Customization.Avatar
{
    /// <summary>
    /// The player-avatar customization schema. Expected category ids: body_height, body_build,
    /// skin_tone (ColorPicker), face_shape, eyes, eyebrows, nose, mouth, ears, hair_style,
    /// hair_color (ColorPicker), facial_hair_style, facial_hair_color (ColorPicker),
    /// markings_tattoos, markings_scars, markings_freckles, accessory_glasses, accessory_hats,
    /// accessory_jewelry, accessory_piercings, clothing_top, clothing_bottom, clothing_shoes,
    /// clothing_outerwear.
    /// </summary>
    /// <remarks>
    /// Carries no extra fields over <see cref="CustomizationSchema"/>; it exists as a distinct type
    /// so designers get a dedicated asset-creation menu entry and so code can type-check that a
    /// reference is specifically the avatar schema. The category assets themselves are authored in
    /// the Editor.
    /// </remarks>
    public class AvatarCustomizationSchema : CustomizationSchema
    {
    }
}
