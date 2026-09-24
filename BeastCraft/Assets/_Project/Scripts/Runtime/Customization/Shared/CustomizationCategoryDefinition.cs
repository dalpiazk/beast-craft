using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeastCraft.Customization
{
    /// <summary>
    /// One customization slot (e.g. "hair_style", "skin_tone") shared by the avatar and creature
    /// systems. A category always resolves to a concrete value: see <see cref="GetDefaultOption"/>
    /// and <see cref="GetDefaultColor"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Customization/Category", fileName = "NewCustomizationCategory")]
    public class CustomizationCategoryDefinition : ScriptableObject
    {
        /// <summary>Stable string key persisted in save data and referenced from code. Never rename after ship.</summary>
        public string CategoryId;

        /// <summary>Player-facing category label.</summary>
        public string DisplayName;

        /// <summary>Whether this category is a discrete option list or a continuous color picker.</summary>
        public CustomizationValueType ValueType = CustomizationValueType.DiscreteOption;

        /// <summary>Authored options. Used when <see cref="ValueType"/> is DiscreteOption.</summary>
        public List<CustomizationOptionDefinition> Options = new List<CustomizationOptionDefinition>();

        /// <summary>Color picker configuration. Used when <see cref="ValueType"/> is ColorPicker.</summary>
        public ColorPickerDefinition ColorPicker = new ColorPickerDefinition();

        /// <summary>
        /// Resolves the default option for a DiscreteOption category. Never returns null for a
        /// non-empty category: if no option (or more than one) is flagged default, the first
        /// option wins and an error is logged, because a blank category is not an acceptable
        /// player-facing outcome.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the category has no options at all. An empty included category is a
        /// build-blocking authoring mistake, not a runtime edge case.
        /// </exception>
        public CustomizationOptionDefinition GetDefaultOption()
        {
            if (ValueType != CustomizationValueType.DiscreteOption)
            {
                Log.Error(
                    "[Customization] GetDefaultOption() called on category '" + name +
                    "' (id '" + CategoryId + "') which is a " + ValueType +
                    " category. Use GetDefaultColor() instead.", this);
            }

            if (Options == null || Options.Count == 0)
            {
                throw new InvalidOperationException(
                    "Customization category '" + name + "' (id '" + CategoryId +
                    "') has no options. Every included category must contain at least one option " +
                    "so it can always resolve to a concrete value.");
            }

            CustomizationOptionDefinition resolved = null;
            int defaultCount = 0;
            for (int i = 0; i < Options.Count; i++)
            {
                CustomizationOptionDefinition option = Options[i];
                if (option == null || !option.IsDefault)
                {
                    continue;
                }

                defaultCount++;
                if (resolved == null)
                {
                    resolved = option;
                }
            }

            if (defaultCount == 1 && resolved != null)
            {
                return resolved;
            }

            CustomizationOptionDefinition fallback = FirstNonNullOption();
            if (fallback == null)
            {
                throw new InvalidOperationException(
                    "Customization category '" + name + "' (id '" + CategoryId +
                    "') contains only null option entries and cannot resolve a default.");
            }

            if (defaultCount == 0)
            {
                Log.Error(
                    "[Customization] Category '" + name + "' (id '" + CategoryId +
                    "') has no option flagged IsDefault. Falling back to '" + fallback.OptionId +
                    "'. Fix the asset: exactly one option must be flagged default.", this);
            }
            else
            {
                Log.Error(
                    "[Customization] Category '" + name + "' (id '" + CategoryId + "') has " +
                    defaultCount + " options flagged IsDefault. Falling back to '" +
                    fallback.OptionId + "'. Fix the asset: exactly one option must be flagged default.",
                    this);
            }

            return fallback;
        }

        /// <summary>
        /// Resolves the default color for a ColorPicker category, clamped into the authored
        /// saturation/value range.
        /// </summary>
        public Color GetDefaultColor()
        {
            if (ValueType != CustomizationValueType.ColorPicker)
            {
                Log.Error(
                    "[Customization] GetDefaultColor() called on category '" + name +
                    "' (id '" + CategoryId + "') which is a " + ValueType +
                    " category. Use GetDefaultOption() instead.", this);
            }

            if (ColorPicker == null)
            {
                Log.Error(
                    "[Customization] Category '" + name + "' (id '" + CategoryId +
                    "') has no ColorPicker definition. Falling back to white.", this);
                return Color.white;
            }

            return ColorPicker.GetDefaultColor();
        }

        /// <summary>
        /// Returns the option with the given id, or null when this category does not contain it
        /// (e.g. stale save data referencing content that was removed).
        /// </summary>
        public CustomizationOptionDefinition FindOption(string optionId)
        {
            if (string.IsNullOrEmpty(optionId) || Options == null)
            {
                return null;
            }

            for (int i = 0; i < Options.Count; i++)
            {
                CustomizationOptionDefinition option = Options[i];
                if (option != null && option.OptionId == optionId)
                {
                    return option;
                }
            }

            return null;
        }

        private CustomizationOptionDefinition FirstNonNullOption()
        {
            for (int i = 0; i < Options.Count; i++)
            {
                if (Options[i] != null)
                {
                    return Options[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Author-time validation. Logs errors rather than throwing: OnValidate runs inside the
        /// Editor's inspector loop and must not destabilise it.
        /// </summary>
        private void OnValidate()
        {
            if (ValueType == CustomizationValueType.DiscreteOption)
            {
                if (Options == null || Options.Count == 0)
                {
                    Log.Error(
                        "[Customization] Category '" + name + "' (id '" + CategoryId +
                        "') has no options. Every included category must contain at least one option.",
                        this);
                    return;
                }

                int defaultCount = 0;
                for (int i = 0; i < Options.Count; i++)
                {
                    if (Options[i] != null && Options[i].IsDefault)
                    {
                        defaultCount++;
                    }
                }

                if (defaultCount != 1)
                {
                    Log.Error(
                        "[Customization] Category '" + name + "' (id '" + CategoryId + "') has " +
                        defaultCount + " options flagged IsDefault; exactly 1 is required so the " +
                        "category always resolves to a concrete value.", this);
                }
            }
            else if (ValueType == CustomizationValueType.ColorPicker)
            {
                // A ColorPicker category has a single DefaultColor field, so the default can never
                // be ambiguous. Only the allowed range needs checking.
                if (ColorPicker == null)
                {
                    Log.Error(
                        "[Customization] Category '" + name + "' (id '" + CategoryId +
                        "') is a ColorPicker category but has no ColorPicker definition.", this);
                    return;
                }

                if (ColorPicker.MinSaturation > ColorPicker.MaxSaturation)
                {
                    Log.Error(
                        "[Customization] Category '" + name + "' (id '" + CategoryId +
                        "') has MinSaturation (" + ColorPicker.MinSaturation +
                        ") greater than MaxSaturation (" + ColorPicker.MaxSaturation + ").", this);
                }

                if (ColorPicker.MinValue > ColorPicker.MaxValue)
                {
                    Log.Error(
                        "[Customization] Category '" + name + "' (id '" + CategoryId +
                        "') has MinValue (" + ColorPicker.MinValue +
                        ") greater than MaxValue (" + ColorPicker.MaxValue + ").", this);
                }
            }
        }
    }
}
