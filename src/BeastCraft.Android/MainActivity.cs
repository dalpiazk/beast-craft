using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using BeastCraft.Game;
using BeastCraft.Presentation.Layout;
using Microsoft.Xna.Framework;

namespace BeastCraft.Android
{
    /// <summary>
    /// The Android host: MonoGame's activity around the shared <see cref="BattleViewerGame"/>, full
    /// screen and locked to portrait, with the content read from the APK's assets
    /// (<see cref="TitleContainerContentSource"/>). The window extends under a display cutout
    /// (notch, punch-hole) and reports the cutout's safe insets to the viewer, which letterboxes its
    /// 1080x1920 canvas inside them. Tap the buttons and skills, tap the board to step, two fingers
    /// toggle auto-play, Back quits.
    /// </summary>
    [Activity(
        Label = "Beast Craft",
        MainLauncher = true,
        AlwaysRetainTaskState = true,
        LaunchMode = LaunchMode.SingleInstance,
        ScreenOrientation = ScreenOrientation.Portrait,
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AndroidGameActivity
    {
        private BattleViewerGame _game;
        private View _view;

        // Written on the UI thread when the layout settles, read by the game loop every frame.
        private volatile SafeInsetsBox _insets = new SafeInsetsBox(SafeInsets.None);

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                // Draw under the cutout on the short edges; the viewer keeps its canvas off it.
                Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            }

            _game = new BattleViewerGame(new ViewerOptions(),
                                         ViewerHost.Mobile("BEAST CRAFT", new TitleContainerContentSource("Content"), () => _insets.Value));
            // MonoGame's Exit() on Android only moves the task to the back; finish the activity so
            // Back really quits.
            _game.Exiting += (sender, args) => Finish();
            _view = (View)_game.Services.GetService(typeof(View));
            _view.ViewTreeObserver.GlobalLayout += (sender, args) => ReadInsets();
            SetContentView(_view);
            _game.Run();
        }

        /// <summary>The display cutout's safe insets (API 28+), in the view's pixels, as the back buffer is.</summary>
        private void ReadInsets()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.P)
            {
                return;
            }

            DisplayCutout cutout = _view?.RootWindowInsets?.DisplayCutout;
            _insets = new SafeInsetsBox(cutout == null
                                            ? SafeInsets.None
                                            : new SafeInsets(cutout.SafeInsetLeft, cutout.SafeInsetTop, cutout.SafeInsetRight, cutout.SafeInsetBottom));
        }

        /// <summary>A reference holder so the struct can be swapped atomically between threads.</summary>
        private sealed class SafeInsetsBox
        {
            public SafeInsetsBox(SafeInsets value)
            {
                Value = value;
            }

            public SafeInsets Value { get; }
        }
    }
}
