
namespace BeastCraft.Customization.Creature
{
    /// <summary>
    /// A creature customization schema, referenced per-species (a species may point at a shared
    /// schema or its own). Expected category ids: body_size, body_weight, body_shape,
    /// covering_type (fur/scales/feathers/smooth), pattern (solid/stripes/spots/patches/gradient),
    /// color (ColorPicker), head_horns, head_ears, head_eyes, head_fangs, head_snout_or_beak,
    /// appendage_wings, appendage_tail, appendage_extra_limbs, appendage_claws. This list is not
    /// exhaustive — categories are open-ended per schema.
    /// </summary>
    /// <remarks>
    /// A species that lacks a feature simply omits that category (a wingless species has no
    /// appendage_wings category at all, rather than a "no wings" option). The defaults rule applies
    /// to included categories only.
    /// </remarks>
    public class CreatureCustomizationSchema : CustomizationSchema
    {
    }
}
