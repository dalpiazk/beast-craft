using System;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using BeastCraft.Game;

namespace BeastCraft.Android
{
    /// <summary>
    /// The Android side of <see cref="IIdleNotifier"/>: the "idle rewards are full" notification,
    /// only when the player turned it on in the settings (off by default). Scheduled as an inexact
    /// alarm (no exact-alarm permission needed; a few minutes late is fine) that wakes
    /// <see cref="IdleAlarmReceiver"/>, which posts the notification; the app cancels both when it
    /// comes back. Android 13+ asks for the notification permission when the setting is turned on.
    /// </summary>
    public sealed class AndroidIdleNotifier : IIdleNotifier
    {
        public const int PermissionRequestCode = 4101;

        private readonly Activity _activity;

        public AndroidIdleNotifier(Activity activity)
        {
            _activity = activity;
        }

        public void RequestPermission()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
                _activity.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            {
                _activity.RequestPermissions(new[] { Manifest.Permission.PostNotifications }, PermissionRequestCode);
            }
        }

        public void Schedule(DateTime utc)
        {
            AlarmManager alarms = (AlarmManager)_activity.GetSystemService(Context.AlarmService);
            long at = (long)(utc.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
            alarms?.Set(AlarmType.Rtc, at, Pending());
        }

        public void Cancel()
        {
            AlarmManager alarms = (AlarmManager)_activity.GetSystemService(Context.AlarmService);
            alarms?.Cancel(Pending());
            NotificationManager notifications = (NotificationManager)_activity.GetSystemService(Context.NotificationService);
            notifications?.Cancel(IdleAlarmReceiver.NotificationId);
        }

        private PendingIntent Pending()
        {
            Intent intent = new Intent(_activity, typeof(IdleAlarmReceiver));
            return PendingIntent.GetBroadcast(_activity, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }
    }

    /// <summary>Posts the "idle rewards are full" notification when the alarm fires; tapping it opens the game.</summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public sealed class IdleAlarmReceiver : BroadcastReceiver
    {
        public const int NotificationId = 4102;
        public const string ChannelId = "idle";

        public override void OnReceive(Context context, Intent intent)
        {
            NotificationManager notifications = (NotificationManager)context.GetSystemService(Context.NotificationService);
            if (notifications == null)
            {
                return;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Idle rewards", NotificationImportance.Default)
                {
                    Description = "When your beasts' idle rewards are full"
                });
            }

            Intent open = new Intent(context, typeof(MainActivity));
            open.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            PendingIntent tap = PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
#pragma warning disable CA1422 // Notification.Builder(Context) is the pre-26 constructor, used only there.
            Notification.Builder builder = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(context, ChannelId) : new Notification.Builder(context);
#pragma warning restore CA1422
            builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                   .SetContentTitle("Beast Craft")
                   .SetContentText("Your beasts' idle rewards are full. Come and collect them!")
                   .SetContentIntent(tap)
                   .SetAutoCancel(true);
            notifications.Notify(NotificationId, builder.Build());
        }
    }
}
