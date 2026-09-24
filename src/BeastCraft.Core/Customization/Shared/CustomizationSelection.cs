using System;
using System.Collections.Generic;

namespace BeastCraft.Customization
{
    /// <summary>
    /// Per-player / per-creature-instance customization state: which option is chosen for each
    /// discrete category and which color for each color category. Plain serializable C# (not a
    /// ScriptableObject) because this is mutable runtime state destined for Cloud Save.
    /// </summary>
    /// <remarks>
    /// Storage is two parallel lists rather than dictionaries because Unity's serializer cannot
    /// serialize <see cref="Dictionary{TKey,TValue}"/>. Category counts are small (tens), so linear
    /// lookup is fine; revisit only if profiling says otherwise.
    /// </remarks>
    [Serializable]
    public class CustomizationSelection
    {
        /// <summary>A chosen option for a discrete category.</summary>
        [Serializable]
        public struct CategoryOptionEntry
        {
            public string CategoryId;
            public string OptionId;

            public CategoryOptionEntry(string categoryId, string optionId)
            {
                CategoryId = categoryId;
                OptionId = optionId;
            }
        }

        /// <summary>A chosen color for a color-picker category.</summary>
        [Serializable]
        public struct CategoryColorEntry
        {
            public string CategoryId;
            public Color Color;

            public CategoryColorEntry(string categoryId, Color color)
            {
                CategoryId = categoryId;
                Color = color;
            }
        }

        public List<CategoryOptionEntry> OptionEntries = new List<CategoryOptionEntry>();
        public List<CategoryColorEntry> ColorEntries = new List<CategoryColorEntry>();

        /// <summary>Returns the chosen option id for a category, or null if none is recorded.</summary>
        public string GetOption(string categoryId)
        {
            int index = IndexOfOption(categoryId);
            return index >= 0 ? OptionEntries[index].OptionId : null;
        }

        /// <summary>Records the chosen option for a category, replacing any existing entry.</summary>
        public void SetOption(string categoryId, string optionId)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                Log.Error("[Customization] SetOption called with an empty categoryId; ignoring.");
                return;
            }

            if (OptionEntries == null)
            {
                OptionEntries = new List<CategoryOptionEntry>();
            }

            int index = IndexOfOption(categoryId);
            if (index >= 0)
            {
                OptionEntries[index] = new CategoryOptionEntry(categoryId, optionId);
            }
            else
            {
                OptionEntries.Add(new CategoryOptionEntry(categoryId, optionId));
            }
        }

        /// <summary>
        /// Returns the chosen color for a category. Falls back to <see cref="Color.white"/> when no
        /// entry exists — white is the neutral identity for a multiply-style palette tint, so an
        /// unresolved category still renders its art rather than disappearing. Callers that need to
        /// distinguish "unset" from "white" should use <see cref="TryGetColor"/>.
        /// </summary>
        public Color GetColor(string categoryId)
        {
            Color color;
            return TryGetColor(categoryId, out color) ? color : Color.white;
        }

        /// <summary>Returns true and outputs the chosen color when this selection has one recorded.</summary>
        public bool TryGetColor(string categoryId, out Color color)
        {
            int index = IndexOfColor(categoryId);
            if (index >= 0)
            {
                color = ColorEntries[index].Color;
                return true;
            }

            color = Color.white;
            return false;
        }

        /// <summary>Records the chosen color for a category, replacing any existing entry.</summary>
        public void SetColor(string categoryId, Color color)
        {
            if (string.IsNullOrEmpty(categoryId))
            {
                Log.Error("[Customization] SetColor called with an empty categoryId; ignoring.");
                return;
            }

            if (ColorEntries == null)
            {
                ColorEntries = new List<CategoryColorEntry>();
            }

            int index = IndexOfColor(categoryId);
            if (index >= 0)
            {
                ColorEntries[index] = new CategoryColorEntry(categoryId, color);
            }
            else
            {
                ColorEntries.Add(new CategoryColorEntry(categoryId, color));
            }
        }

        /// <summary>
        /// Guarantees the "defaults rule" against real save data: for every category in
        /// <paramref name="schema"/>, if this selection has no entry — or a discrete entry pointing
        /// at an option id that no longer exists (stale save after a content update) — the
        /// category's default is written in. Entries for categories not present in the schema are
        /// left alone so that temporarily removed content does not permanently destroy a player's
        /// choice.
        /// </summary>
        public void ResolveMissingWithDefaults(CustomizationSchema schema)
        {
            if (schema == null)
            {
                Log.Error("[Customization] ResolveMissingWithDefaults called with a null schema; nothing to resolve.");
                return;
            }

            if (schema.Categories == null)
            {
                return;
            }

            for (int i = 0; i < schema.Categories.Count; i++)
            {
                CustomizationCategoryDefinition category = schema.Categories[i];
                if (category == null)
                {
                    Log.Error("[Customization] Schema '" + schema.name + "' has a null category at index " + i + ".", schema);
                    continue;
                }

                if (string.IsNullOrEmpty(category.CategoryId))
                {
                    Log.Error("[Customization] Category asset '" + category.name + "' has an empty CategoryId; skipping.", category);
                    continue;
                }

                if (category.ValueType == CustomizationValueType.ColorPicker)
                {
                    Color existingColor;
                    if (!TryGetColor(category.CategoryId, out existingColor))
                    {
                        SetColor(category.CategoryId, category.GetDefaultColor());
                    }

                    continue;
                }

                string chosenOptionId = GetOption(category.CategoryId);
                bool stillValid = !string.IsNullOrEmpty(chosenOptionId) && category.FindOption(chosenOptionId) != null;
                if (stillValid)
                {
                    continue;
                }

                // GetDefaultOption throws on a genuinely empty category; that is a build-blocking
                // authoring mistake and deliberately not swallowed here.
                CustomizationOptionDefinition defaultOption = category.GetDefaultOption();
                SetOption(category.CategoryId, defaultOption.OptionId);
            }
        }

        private int IndexOfOption(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId) || OptionEntries == null)
            {
                return -1;
            }

            for (int i = 0; i < OptionEntries.Count; i++)
            {
                if (OptionEntries[i].CategoryId == categoryId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfColor(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId) || ColorEntries == null)
            {
                return -1;
            }

            for (int i = 0; i < ColorEntries.Count; i++)
            {
                if (ColorEntries[i].CategoryId == categoryId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
