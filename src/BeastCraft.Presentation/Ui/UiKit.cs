namespace BeastCraft.Presentation.Ui
{
    /// <summary>
    /// The one switch between the journal UI kit (issue #52, direction D: painted parchment,
    /// wood/brass trim, ink-rail sliders) and the original code-drawn house style (flat rounded
    /// rectangles, <see cref="UiStyle"/>'s plain Fill/Outline/Radius) — so the two can be compared
    /// without touching content: every panel/button look still carries its vector Fill/Outline/Radius
    /// either way, and a look only draws textured when both this is on and that look names a
    /// <see cref="UiPanelStyleData.Texture"/>/<see cref="UiButtonStyleData.Texture"/>. D is the
    /// default (the producer's direction, GitHub #52); a debug build or a host's own CLI flag may
    /// clear it to render the comparison.
    /// </summary>
    public static class UiKit
    {
        public static bool Enabled = true;
    }

    /// <summary>
    /// The journal UI kit's art keys (<c>content/art/ui/kit/</c>, built by
    /// <c>Tooling/UiKit/build_kit.py</c>, indexed in the art manifest under Category
    /// <c>ui_kit</c>): the pieces <see cref="UiStyle"/>'s data does not name directly because the
    /// widget that draws them (a slider, a toggle, a chip's row) is not a <see cref="UiPanelStyle"/>/
    /// <see cref="UiButtonStyle"/> look. Panels, buttons and tabs instead name their own texture in
    /// <c>content/data/Ui/ui-style.json</c> (<see cref="UiPanelStyleData.Texture"/>,
    /// <see cref="UiButtonStyleData.Texture"/>/<see cref="UiButtonStyleData.UnselectedTexture"/>), so
    /// this list only has the pieces nothing else names.
    /// </summary>
    public static class UiKitArt
    {
        /// <summary>A screen header's title plaque (<c>ScreenHeader</c>, <c>BeastCraft.Game</c>): a small parchment pill with brass trim, sized to the title text.</summary>
        public const string TitlePlaque = "ui/kit/title_plaque";

        /// <summary>The small ribbon-cloth accent under a selected tab's pill (a fixed sprite, not nine-sliced).</summary>
        public const string TabRibbon = "ui/kit/tab_ribbon";

        /// <summary>The ink-rail slider's background track (a 3-slice horizontal pill).</summary>
        public const string SliderRail = "ui/kit/slider_rail";

        /// <summary>The slider's painted knob (a fixed sprite).</summary>
        public const string SliderKnob = "ui/kit/slider_knob";

        /// <summary>The toggle's wood track, on or off alike (a 3-slice horizontal pill; only the knob moves).</summary>
        public const string ToggleTrack = "ui/kit/toggle_track";

        /// <summary>The toggle's brass knob (a fixed sprite).</summary>
        public const string ToggleKnob = "ui/kit/toggle_knob";
    }
}
