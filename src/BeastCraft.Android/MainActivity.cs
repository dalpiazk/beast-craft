using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using BeastCraft.Game;
using Microsoft.Xna.Framework;

namespace BeastCraft.Android
{
    /// <summary>
    /// The Android host: MonoGame's activity around the shared <see cref="BattleViewerGame"/>, full
    /// screen in landscape, with the content read from the APK's assets
    /// (<see cref="TitleContainerContentSource"/>). Tap steps a turn, a two-finger tap or the AUTO
    /// button toggles auto-play, Back quits.
    /// </summary>
    [Activity(
        Label = "Beast Craft",
        MainLauncher = true,
        AlwaysRetainTaskState = true,
        LaunchMode = LaunchMode.SingleInstance,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenSize |
                               ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AndroidGameActivity
    {
        private BattleViewerGame _game;
        private View _view;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            _game = new BattleViewerGame(new ViewerOptions(), ViewerHost.Mobile("BEAST CRAFT  ANDROID", new TitleContainerContentSource("Content")));
            // MonoGame's Exit() on Android only moves the task to the back; finish the activity so
            // Back really quits.
            _game.Exiting += (sender, args) => Finish();
            _view = (View)_game.Services.GetService(typeof(View));
            SetContentView(_view);
            _game.Run();
        }
    }
}
