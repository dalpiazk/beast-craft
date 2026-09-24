using System;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;

namespace BeastCraft.Game
{
    /// <summary>
    /// What differs between the hosts of <see cref="BattleViewerGame"/>: the window (desktop) or
    /// full screen (phone), keyboard and mouse or touch, where the content comes from and the
    /// screen's safe area. Everything else -- the battle, the drawing, the 1080x1920 portrait
    /// canvas (<see cref="PortraitLayout"/>) and its letterboxing -- is shared.
    /// </summary>
    public sealed class ViewerHost
    {
        /// <summary>The desktop window's default size: the portrait canvas at half scale.</summary>
        public const int DesktopWidth = PortraitLayout.CanvasWidth / 2;

        public const int DesktopHeight = PortraitLayout.CanvasHeight / 2;

        /// <summary>The title drawn in the header.</summary>
        public string HudTitle;

        /// <summary>The window title (desktop only).</summary>
        public string WindowTitle;

        /// <summary>
        /// A phone or tablet: full screen, locked to portrait, touch controls (tap the on-screen
        /// buttons and skills; tap the board to step; two fingers toggle auto) and Back quits.
        /// Otherwise a resizable desktop window (tall by default) with keyboard and mouse.
        /// </summary>
        public bool Touch;

        /// <summary>
        /// The content. Null on desktop: <see cref="ViewerOptions.ContentRoot"/>, else
        /// <see cref="GameContent.FindRoot"/>, as plain files.
        /// </summary>
        public IContentSource Content;

        /// <summary>
        /// The screen's safe-area insets in back-buffer pixels, asked every frame (a phone reports
        /// its display cutout once the view is attached, and again on rotation). Null: none. The
        /// canvas is letterboxed inside what is left.
        /// </summary>
        public Func<SafeInsets> SafeArea;

        /// <summary>The desktop spike: a tall window, the keyboard and mouse, and the content as files.</summary>
        public static ViewerHost Desktop()
        {
            return new ViewerHost
            {
                HudTitle = "BEAST CRAFT",
                WindowTitle = "Beast Craft - desktop spike (Space: step  A: auto  1-3: speed  S: skip  Tab: skill  Esc: quit)"
            };
        }

        /// <summary>A touch device (Android) whose content is <paramref name="content"/> and safe area <paramref name="safeArea"/>.</summary>
        public static ViewerHost Mobile(string hudTitle, IContentSource content, Func<SafeInsets> safeArea = null)
        {
            return new ViewerHost { HudTitle = hudTitle, Touch = true, Content = content, SafeArea = safeArea };
        }
    }
}
