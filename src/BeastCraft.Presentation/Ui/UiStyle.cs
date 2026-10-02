using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeastCraft.Presentation.Ui
{
    /// <summary>An engine-neutral 8-bit RGBA colour, parsed from <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public readonly struct UiColor : IEquatable<UiColor>
    {
        public UiColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R { get; }

        public byte G { get; }

        public byte B { get; }

        public byte A { get; }

        /// <summary>Whether <paramref name="hex"/> is <c>#RRGGBB</c> or <c>#RRGGBBAA</c>; <paramref name="color"/> is it, else transparent black.</summary>
        public static bool TryParse(string hex, out UiColor color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9))
            {
                return false;
            }

            if (!uint.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                return false;
            }

            if (hex.Length == 7)
            {
                value = (value << 8) | 0xFF;
            }

            color = new UiColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return true;
        }

        /// <summary>This colour with its alpha multiplied by <paramref name="k"/> (0-1).</summary>
        public UiColor Fade(float k)
        {
            float clamped = k < 0f ? 0f : k > 1f ? 1f : k;
            return new UiColor(R, G, B, (byte)Math.Round(A * clamped));
        }

        /// <summary>The colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static UiColor Lerp(UiColor a, UiColor b, float t)
        {
            float k = t < 0f ? 0f : t > 1f ? 1f : t;
            return new UiColor(Mix(a.R, b.R, k), Mix(a.G, b.G, k), Mix(a.B, b.B, k), Mix(a.A, b.A, k));
        }

        public bool Equals(UiColor other)
        {
            return R == other.R && G == other.G && B == other.B && A == other.A;
        }

        public override bool Equals(object obj)
        {
            return obj is UiColor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (R << 24) | (G << 16) | (B << 8) | A;
        }

        public override string ToString()
        {
            return "#" + R.ToString("X2", CultureInfo.InvariantCulture) + G.ToString("X2", CultureInfo.InvariantCulture) + B.ToString("X2", CultureInfo.InvariantCulture) +
                   A.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static byte Mix(byte a, byte b, float k)
        {
            return (byte)Math.Round(a + (b - a) * k);
        }
    }

    /// <summary>
    /// The UI toolkit's house style as data (<c>content/data/Ui/ui-style.json</c>): named colours,
    /// panel and button looks (fill, outline, corner radius) and the text sizes. Rounded, friendly
    /// shapes: warm plum outlines on cream panels. JsonUtility-shaped (public fields, arrays).
    /// </summary>
    [Serializable]
    public class UiStyleData
    {
        public const string ProjectRelativePath = "content/data/Ui/ui-style.json";

        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>Named colours; panels and buttons name these (or give a <c>#hex</c> directly).</summary>
        public UiColorData[] Colors = new UiColorData[0];

        public UiPanelStyleData[] Panels = new UiPanelStyleData[0];

        public UiButtonStyleData[] Buttons = new UiButtonStyleData[0];

        /// <summary>Text cap heights, canvas pixels.</summary>
        public UiTextSizesData TextSizes = new UiTextSizesData();
    }

    [Serializable]
    public class UiColorData
    {
        public string Key;

        public string Hex;
    }

    [Serializable]
    public class UiPanelStyleData
    {
        public string Key;

        public string Fill;

        public string Outline;

        public float OutlineWidth;

        public float Radius;

        /// <summary>A soft drop shadow under the panel (colour), or empty for none.</summary>
        public string Shadow = string.Empty;

        /// <summary>
        /// A nine-sliced art key (<c>content/art/pixel/pixel-art-manifest.json</c>) to draw the
        /// panel's face with instead of the flat <see cref="Fill"/>/<see cref="Outline"/> shape, when
        /// <see cref="UiKit.Enabled"/> (the painted journal UI kit, issue #52 direction D). Empty
        /// (every look before the kit) always draws the vector shape; <see cref="Fill"/>/
        /// <see cref="Outline"/>/<see cref="Radius"/> still apply when the kit is off, so clearing
        /// <see cref="UiKit.Enabled"/> compares the two looks without touching content.
        /// </summary>
        public string Texture = string.Empty;
    }

    [Serializable]
    public class UiButtonStyleData
    {
        public string Key;

        public string Fill;

        public string PressedFill;

        public string DisabledFill;

        public string SelectedFill;

        public string Outline;

        public string Text;

        public string DisabledText;

        /// <summary>
        /// <see cref="Text"/>, but for when this look draws its <see cref="Texture"/> (kit-textured
        /// faces are light parchment regardless of the look's vector <see cref="Fill"/>, so the
        /// classic look's own <see cref="Text"/> — white, for <c>primary</c>/<c>danger</c>, legible
        /// on their vector fills' leaf-green/berry — reads at well under 4.5:1 on parchment; empty
        /// falls back to <see cref="Text"/>). See <see cref="UiPanelStyleData.Texture"/>.
        /// </summary>
        public string TextureText = string.Empty;

        /// <summary>See <see cref="TextureText"/>; the disabled-state equivalent of <see cref="DisabledText"/>.</summary>
        public string TextureDisabledText = string.Empty;

        public float OutlineWidth;

        public float Radius;

        public float TextSize;

        /// <summary>
        /// A nine-sliced art key to draw the button's face with (the look's selected/pressed/enabled
        /// face), when <see cref="UiKit.Enabled"/>; see <see cref="UiPanelStyleData.Texture"/>. A
        /// <see cref="Tabs"/> look also uses <see cref="UnselectedTexture"/> for its unselected items
        /// (every other button ignores it: it has only the one face).
        /// </summary>
        public string Texture = string.Empty;

        /// <summary>An unselected <see cref="Tabs"/> item's face (see <see cref="Texture"/>); unused outside Tabs.</summary>
        public string UnselectedTexture = string.Empty;
    }

    [Serializable]
    public class UiTextSizesData
    {
        public float Small = 16f;

        public float Body = 22f;

        public float Heading = 30f;

        public float Title = 56f;
    }

    /// <summary>A panel look, resolved.</summary>
    public sealed class UiPanelStyle
    {
        public UiColor Fill;
        public UiColor Outline;
        public float OutlineWidth;
        public float Radius;
        public UiColor? Shadow;

        /// <summary>See <see cref="UiPanelStyleData.Texture"/>; empty when this look has none.</summary>
        public string Texture = string.Empty;
    }

    /// <summary>A button look, resolved.</summary>
    public sealed class UiButtonStyle
    {
        public UiColor Fill;
        public UiColor PressedFill;
        public UiColor DisabledFill;
        public UiColor SelectedFill;
        public UiColor Outline;
        public UiColor Text;
        public UiColor DisabledText;

        /// <summary>See <see cref="UiButtonStyleData.TextureText"/>; always resolved (falls back to <see cref="Text"/> when unset).</summary>
        public UiColor TextureText;

        /// <summary>See <see cref="UiButtonStyleData.TextureDisabledText"/>; always resolved (falls back to <see cref="DisabledText"/> when unset).</summary>
        public UiColor TextureDisabledText;

        public float OutlineWidth;
        public float Radius;
        public float TextSize;

        /// <summary>See <see cref="UiButtonStyleData.Texture"/>; empty when this look has none.</summary>
        public string Texture = string.Empty;

        /// <summary>See <see cref="UiButtonStyleData.UnselectedTexture"/>.</summary>
        public string UnselectedTexture = string.Empty;
    }

    /// <summary>
    /// <see cref="UiStyleData"/> resolved for drawing: colours by key, panel and button looks by key
    /// (an unknown key gets the first defined, so a typo never crashes a screen), text sizes.
    /// </summary>
    public sealed class UiStyle
    {
        private static readonly UiColor Magenta = new UiColor(255, 0, 255);

        private readonly Dictionary<string, UiColor> _colors = new Dictionary<string, UiColor>(StringComparer.Ordinal);
        private readonly Dictionary<string, UiPanelStyle> _panels = new Dictionary<string, UiPanelStyle>(StringComparer.Ordinal);
        private readonly Dictionary<string, UiButtonStyle> _buttons = new Dictionary<string, UiButtonStyle>(StringComparer.Ordinal);
        private UiPanelStyle _firstPanel;
        private UiButtonStyle _firstButton;

        private UiStyle()
        {
        }

        public UiTextSizesData TextSizes { get; private set; } = new UiTextSizesData();

        /// <summary>Resolves <paramref name="data"/> (null gives an empty style: every lookup falls back).</summary>
        public static UiStyle Build(UiStyleData data)
        {
            UiStyle style = new UiStyle();
            if (data == null)
            {
                return style;
            }

            style.TextSizes = data.TextSizes ?? new UiTextSizesData();
            foreach (UiColorData color in data.Colors ?? new UiColorData[0])
            {
                if (color != null && !string.IsNullOrEmpty(color.Key) && UiColor.TryParse(color.Hex, out UiColor parsed))
                {
                    style._colors[color.Key] = parsed;
                }
            }

            foreach (UiPanelStyleData panel in data.Panels ?? new UiPanelStyleData[0])
            {
                if (panel == null || string.IsNullOrEmpty(panel.Key))
                {
                    continue;
                }

                UiPanelStyle resolved = new UiPanelStyle
                {
                    Fill = style.Color(panel.Fill),
                    Outline = style.Color(panel.Outline),
                    OutlineWidth = panel.OutlineWidth,
                    Radius = panel.Radius,
                    Shadow = string.IsNullOrEmpty(panel.Shadow) ? (UiColor?)null : style.Color(panel.Shadow),
                    Texture = panel.Texture ?? string.Empty
                };
                style._panels[panel.Key] = resolved;
                style._firstPanel ??= resolved;
            }

            foreach (UiButtonStyleData button in data.Buttons ?? new UiButtonStyleData[0])
            {
                if (button == null || string.IsNullOrEmpty(button.Key))
                {
                    continue;
                }

                UiButtonStyle resolved = new UiButtonStyle
                {
                    Fill = style.Color(button.Fill),
                    PressedFill = style.Color(button.PressedFill),
                    DisabledFill = style.Color(button.DisabledFill),
                    SelectedFill = style.Color(string.IsNullOrEmpty(button.SelectedFill) ? button.PressedFill : button.SelectedFill),
                    Outline = style.Color(button.Outline),
                    Text = style.Color(button.Text),
                    DisabledText = style.Color(button.DisabledText),
                    TextureText = style.Color(string.IsNullOrEmpty(button.TextureText) ? button.Text : button.TextureText),
                    TextureDisabledText = style.Color(string.IsNullOrEmpty(button.TextureDisabledText) ? button.DisabledText : button.TextureDisabledText),
                    OutlineWidth = button.OutlineWidth,
                    Radius = button.Radius,
                    TextSize = button.TextSize > 0f ? button.TextSize : style.TextSizes.Body,
                    Texture = button.Texture ?? string.Empty,
                    UnselectedTexture = button.UnselectedTexture ?? string.Empty
                };
                style._buttons[button.Key] = resolved;
                style._firstButton ??= resolved;
            }

            return style;
        }

        /// <summary>The colour named <paramref name="keyOrHex"/>, or the <c>#hex</c> itself; magenta when neither (easy to spot).</summary>
        public UiColor Color(string keyOrHex)
        {
            if (!string.IsNullOrEmpty(keyOrHex))
            {
                if (_colors.TryGetValue(keyOrHex, out UiColor color))
                {
                    return color;
                }

                if (UiColor.TryParse(keyOrHex, out UiColor parsed))
                {
                    return parsed;
                }
            }

            return Magenta;
        }

        public bool HasColor(string key)
        {
            return key != null && _colors.ContainsKey(key);
        }

        public bool HasPanel(string key)
        {
            return key != null && _panels.ContainsKey(key);
        }

        public bool HasButton(string key)
        {
            return key != null && _buttons.ContainsKey(key);
        }

        public UiPanelStyle Panel(string key)
        {
            return key != null && _panels.TryGetValue(key, out UiPanelStyle panel) ? panel : _firstPanel ?? new UiPanelStyle { Fill = Magenta, Outline = Magenta };
        }

        public UiButtonStyle Button(string key)
        {
            return key != null && _buttons.TryGetValue(key, out UiButtonStyle button)
                       ? button
                       : _firstButton ?? new UiButtonStyle { Fill = Magenta, Outline = Magenta, Text = Magenta, TextSize = TextSizes.Body };
        }
    }

    /// <summary>Checks a <see cref="UiStyleData"/>: the schema, unique keys, every colour reference resolving, and the looks the toolkit draws by default present.</summary>
    public static class UiStyleValidator
    {
        /// <summary>The panel looks every screen may name.</summary>
        public static readonly string[] RequiredPanels = { "panel", "card", "modal", "toast", "header", "nav" };

        /// <summary>The button looks every screen may name.</summary>
        public static readonly string[] RequiredButtons = { "primary", "secondary", "ghost", "nav", "chip" };

        /// <summary>The colours the painter and the screens name directly.</summary>
        public static readonly string[] RequiredColors = { "plum", "cream", "ink", "inkSoft", "gold", "leaf", "berry", "sky", "scrim", "track" };

        public static List<string> Validate(UiStyleData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No UI style.");
                return errors;
            }

            if (data.SchemaVersion != UiStyleData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion " + data.SchemaVersion + " is not " + UiStyleData.CurrentSchemaVersion + ".");
            }

            HashSet<string> colors = new HashSet<string>(StringComparer.Ordinal);
            foreach (UiColorData color in data.Colors ?? new UiColorData[0])
            {
                if (color == null || string.IsNullOrEmpty(color.Key))
                {
                    errors.Add("A colour has no key.");
                    continue;
                }

                if (!colors.Add(color.Key))
                {
                    errors.Add("Colour '" + color.Key + "' is defined twice.");
                }

                if (!UiColor.TryParse(color.Hex, out _))
                {
                    errors.Add("Colour '" + color.Key + "' is not #RRGGBB or #RRGGBBAA: '" + color.Hex + "'.");
                }
            }

            foreach (string key in RequiredColors)
            {
                if (!colors.Contains(key))
                {
                    errors.Add("Colour '" + key + "' is missing.");
                }
            }

            HashSet<string> panels = new HashSet<string>(StringComparer.Ordinal);
            foreach (UiPanelStyleData panel in data.Panels ?? new UiPanelStyleData[0])
            {
                if (panel == null || string.IsNullOrEmpty(panel.Key) || !panels.Add(panel.Key))
                {
                    errors.Add("A panel look has no key or a duplicate key.");
                    continue;
                }

                CheckRef(errors, colors, "Panel '" + panel.Key + "' fill", panel.Fill);
                CheckRef(errors, colors, "Panel '" + panel.Key + "' outline", panel.Outline);
                if (!string.IsNullOrEmpty(panel.Shadow))
                {
                    CheckRef(errors, colors, "Panel '" + panel.Key + "' shadow", panel.Shadow);
                }

                if (panel.Radius < 0f || panel.OutlineWidth < 0f)
                {
                    errors.Add("Panel '" + panel.Key + "' has a negative radius or outline.");
                }
            }

            foreach (string key in RequiredPanels)
            {
                if (!panels.Contains(key))
                {
                    errors.Add("Panel look '" + key + "' is missing.");
                }
            }

            HashSet<string> buttons = new HashSet<string>(StringComparer.Ordinal);
            foreach (UiButtonStyleData button in data.Buttons ?? new UiButtonStyleData[0])
            {
                if (button == null || string.IsNullOrEmpty(button.Key) || !buttons.Add(button.Key))
                {
                    errors.Add("A button look has no key or a duplicate key.");
                    continue;
                }

                string name = "Button '" + button.Key + "' ";
                CheckRef(errors, colors, name + "fill", button.Fill);
                CheckRef(errors, colors, name + "pressed fill", button.PressedFill);
                CheckRef(errors, colors, name + "disabled fill", button.DisabledFill);
                CheckRef(errors, colors, name + "outline", button.Outline);
                CheckRef(errors, colors, name + "text", button.Text);
                CheckRef(errors, colors, name + "disabled text", button.DisabledText);
                if (!string.IsNullOrEmpty(button.SelectedFill))
                {
                    CheckRef(errors, colors, name + "selected fill", button.SelectedFill);
                }

                if (!string.IsNullOrEmpty(button.TextureText))
                {
                    CheckRef(errors, colors, name + "texture text", button.TextureText);
                }

                if (!string.IsNullOrEmpty(button.TextureDisabledText))
                {
                    CheckRef(errors, colors, name + "texture disabled text", button.TextureDisabledText);
                }
            }

            foreach (string key in RequiredButtons)
            {
                if (!buttons.Contains(key))
                {
                    errors.Add("Button look '" + key + "' is missing.");
                }
            }

            UiTextSizesData sizes = data.TextSizes;
            if (sizes == null || sizes.Small <= 0f || sizes.Body <= 0f || sizes.Heading <= 0f || sizes.Title <= 0f)
            {
                errors.Add("Every text size must be above 0.");
            }

            return errors;
        }

        private static void CheckRef(List<string> errors, HashSet<string> colors, string what, string value)
        {
            if (string.IsNullOrEmpty(value) || (!colors.Contains(value) && !UiColor.TryParse(value, out _)))
            {
                errors.Add(what + " '" + value + "' is neither a colour key nor #hex.");
            }
        }
    }
}
