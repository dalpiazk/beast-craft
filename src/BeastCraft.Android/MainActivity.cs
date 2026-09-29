using System;
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
    /// The Android host: MonoGame's activity around the shared <see cref="BeastCraftGame"/>, full
    /// screen and locked to portrait, with the content read from the APK's assets
    /// (<see cref="TitleContainerContentSource"/>) and the save in the app's files directory. The
    /// window extends under a display cutout (notch, punch-hole) and reports the cutout's safe insets
    /// to the game, which letterboxes its 1080x1920 canvas inside them. It starts at the title; Back
    /// goes back (the title asks before quitting), and pausing the app autosaves.
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
        private BeastCraftGame _game;
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

            ViewerHost host = ViewerHost.Mobile("BEAST CRAFT", new TitleContainerContentSource("Content"), () => _insets.Value, FilesDir?.AbsolutePath);

            // Idle time counts deep sleep (elapsedRealtime), and the idle-full and Grove-ready notifications are Android's.
            host.MonotonicClock = () => TimeSpan.FromMilliseconds(SystemClock.ElapsedRealtime());
            host.Notifier = new AndroidNotifier(this);
            // Light haptics on hits, knockouts and key confirms (on by default, a settings toggle). UNVERIFIED: see AndroidHaptics.
            host.Haptics = new AndroidHaptics(this);

            // TODO(#59): save export/import on Android. host.SaveTransfer is left null, so the slot list
            // hides Export and Import here (Auto Backup still covers the saves: Resources/xml). The plan is
            // an ISaveTransfer over the Storage Access Framework: Export starts ACTION_CREATE_DOCUMENT
            // (type application/json, the suggested name as EXTRA_TITLE) and Import ACTION_OPEN_DOCUMENT;
            // OnActivityResult writes or reads the returned Uri through ContentResolver streams and calls
            // the pending callback (Cancelled when the result is not OK), marshalled back to the game
            // thread before it touches the session. Not written yet because the Android host could not be
            // built or run where this was done (no Android workload); add it with a device test.
            _game = new BeastCraftGame(new ViewerOptions(), host);
            // MonoGame's Exit() on Android only moves the task to the back; finish the activity so
            // Back really quits.
            _game.Exiting += (sender, args) => Finish();

            // Android 13+ delivers Back as a callback, not a key: without one the system only sends
            // the app to the background. Route it to the game's stack (the title asks before quitting).
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, new BackCallback(_game));
            }
            _view = (View)_game.Services.GetService(typeof(View));
            _view.ViewTreeObserver.GlobalLayout += (sender, args) => ReadInsets();
            SetContentView(_view);
            _game.Run();
        }

        /// <summary>Back on Android 12 and older, when the key did not reach the game first.</summary>
#pragma warning disable CS0672, CS0618, CA1422 // OnBackPressed is the pre-33 path; 33+ uses BackCallback.
        public override void OnBackPressed()
        {
            _game?.RequestBack();
        }
#pragma warning restore CS0672, CS0618, CA1422

        /// <summary>Going to the background (home, another app, the screen off): autosave before Android may stop the process.</summary>
        protected override void OnPause()
        {
            _game?.OnBackgrounded();
            base.OnPause();
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

        /// <summary>Back (Android 13+) handed to the game's screen stack.</summary>
        private sealed class BackCallback : Java.Lang.Object, global::Android.Window.IOnBackInvokedCallback
        {
            private readonly BeastCraftGame _game;

            public BackCallback(BeastCraftGame game)
            {
                _game = game;
            }

            public void OnBackInvoked()
            {
                _game.RequestBack();
            }
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
