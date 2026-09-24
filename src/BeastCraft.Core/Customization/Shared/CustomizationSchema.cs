using System.Collections.Generic;

namespace BeastCraft.Customization
{
    /// <summary>
    /// Base class for an ordered set of customization categories. Not directly instantiable as an
    /// asset — create an <c>AvatarCustomizationSchema</c> or a <c>CreatureCustomizationSchema</c>
    /// instead, so the asset type carries intent.
    /// </summary>
    public abstract class CustomizationSchema : ContentAsset
    {
        /// <summary>
        /// The categories this schema includes, in UI presentation order. A schema only lists
        /// categories that are meaningful for it; the "defaults rule" is about never leaving an
        /// INCLUDED category blank, not about forcing every schema to include every category.
        /// </summary>
        public List<CustomizationCategoryDefinition> Categories = new List<CustomizationCategoryDefinition>();

        /// <summary>
        /// Returns the category with the given id, or null when this schema does not include it.
        /// A null result from code that expects a specific category is a code/data mismatch and
        /// should be treated as an error by the caller, not as a customization gap.
        /// </summary>
        public CustomizationCategoryDefinition GetCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId) || Categories == null)
            {
                return null;
            }

            for (int i = 0; i < Categories.Count; i++)
            {
                CustomizationCategoryDefinition category = Categories[i];
                if (category != null && category.CategoryId == categoryId)
                {
                    return category;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds a fully-populated selection with every category resolved to its default option id
        /// or default color. Used for brand-new avatars/creatures.
        /// </summary>
        public CustomizationSelection BuildDefaultSelection()
        {
            CustomizationSelection selection = new CustomizationSelection();
            selection.ResolveMissingWithDefaults(this);
            return selection;
        }
    }
}
