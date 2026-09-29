using System;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;

namespace BeastCraft.Game
{
    /// <summary>
    /// What differs between the hosts of <see cref="BeastCraftGame"/>: the window (desktop) or
    /// full screen (phone), keyboard and mouse or touch, where the content comes from, where the
    /// saves live and the screen's safe area. Everything else -- the screens, the drawing, the
    /// 1080x1920 portrait canvas (<see cref="PortraitLayout"/>) and its letterboxing -- is shared.
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
        /// buttons; in battle tap the board to step, two fingers toggle auto) and the Back button.
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

        /// <summary>
        /// The folder the saves and settings live under (<see cref="Save.SaveLocations.DefaultDirectory(string)"/>
        /// adds <c>saves/</c>). Null: the per-user default (<see cref="Save.SaveLocations.DefaultRoot"/>);
        /// the Android host passes the app's files directory.
        /// </summary>
        public string SaveRoot;

        /// <summary>
        /// The monotonic clock the idle rewards are measured with (time since boot, counting deep
        /// sleep: Android's <c>elapsedRealtime</c>). Null: the desktop's (<see cref="System.Diagnostics.Stopwatch"/>).
        /// </summary>
        public Func<TimeSpan> MonotonicClock;

        /// <summary>
        /// Posts the "idle rewards are full" local notification (Android). Null: the host has none
        /// (desktop), and the setting is hidden.
        /// </summary>
        public IIdleNotifier IdleNotifier;

        /// <summary>
        /// Exports a save slot to a file and imports one back (the slot list's Export and Import). The desktop
        /// host uses a folder (<see cref="FolderSaveTransfer"/>, <c>Documents/BeastCraft</c>). Null: the host has
        /// none, and the buttons are hidden (Android, for now: see its MainActivity).
        /// </summary>
        public ISaveTransfer SaveTransfer;

        /// <summary>The desktop spike: a tall window, the keyboard and mouse, and the content as files.</summary>
        public static ViewerHost Desktop()
        {
            return new ViewerHost
            {
                HudTitle = "BEAST CRAFT",
                WindowTitle = "Beast Craft (Esc: back; in battle Space: step  A: auto  1-3: speed  S: skip  Tab: skill)",
                SaveTransfer = new FolderSaveTransfer(FolderSaveTransfer.DefaultFolder())
            };
        }

        /// <summary>A touch device (Android) whose content is <paramref name="content"/> and safe area <paramref name="safeArea"/>.</summary>
        public static ViewerHost Mobile(string hudTitle, IContentSource content, Func<SafeInsets> safeArea = null, string saveRoot = null)
        {
            return new ViewerHost { HudTitle = hudTitle, Touch = true, Content = content, SafeArea = safeArea, SaveRoot = saveRoot };
        }
    }

    /// <summary>
    /// The platform seam for the "idle rewards are full" local notification (the player's
    /// <c>PlayerSettings.IdleNotifications</c>, off by default). The game schedules one when it goes
    /// to the background (at the moment the idle cap fills) and cancels it when it comes back.
    /// Android implements it (an inexact alarm that posts the notification); desktop has none.
    /// </summary>
    public interface IIdleNotifier
    {
        /// <summary>Asks for the permission to post notifications, where the platform needs one (Android 13+).</summary>
        void RequestPermission();

        /// <summary>Posts the notification at <paramref name="utc"/> (replacing any scheduled one).</summary>
        void Schedule(DateTime utc);

        /// <summary>Cancels the scheduled notification and removes a posted one.</summary>
        void Cancel();
    }
}
