using BeastCraft.Presentation.Content;

namespace BeastCraft.Game
{
    /// <summary>
    /// What differs between the hosts of <see cref="BattleViewerGame"/>: the window (desktop) or
    /// full screen (phone), keyboard or touch controls and their help line, and where the content
    /// comes from. Everything else -- the battle, the drawing, the 640x360 frame and its integer
    /// scaling -- is shared.
    /// </summary>
    public sealed class ViewerHost
    {
        /// <summary>The title drawn at the top-left of the frame.</summary>
        public string HudTitle;

        /// <summary>The window title (desktop only).</summary>
        public string WindowTitle;

        /// <summary>
        /// A phone or tablet: full screen in landscape, touch controls (tap: step; two-finger tap or
        /// the on-screen AUTO button: auto-play) and the Back button quits. Otherwise a resizable
        /// desktop window with keyboard controls (Space, A, Esc).
        /// </summary>
        public bool Touch;

        /// <summary>
        /// The content. Null on desktop: <see cref="ViewerOptions.ContentRoot"/>, else
        /// <see cref="GameContent.FindRoot"/>, as plain files.
        /// </summary>
        public IContentSource Content;

        /// <summary>The desktop spike: a window, the keyboard, and the content as files.</summary>
        public static ViewerHost Desktop()
        {
            return new ViewerHost
            {
                HudTitle = "BEAST CRAFT  DESKTOP SPIKE",
                WindowTitle = "Beast Craft - desktop spike (Space: step  A: auto  Esc: quit)"
            };
        }

        /// <summary>A touch device (Android) whose content is <paramref name="content"/>.</summary>
        public static ViewerHost Mobile(string hudTitle, IContentSource content)
        {
            return new ViewerHost { HudTitle = hudTitle, Touch = true, Content = content };
        }
    }
}
