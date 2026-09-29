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
    /// The Android side of <see cref="ILocalNotifier"/>: the "idle rewards are full" and "something is ready in the
    /// Grove" notifications, each only when the player turned it on in the settings (both off by default). Each kind is
    /// scheduled as its own inexact alarm (no exact-alarm permission needed; a few minutes late is fine), with its own
    /// PendingIntent (request code = the kind) and channel, so one never replaces the other. The alarm wakes
    /// <see cref="LocalNotificationReceiver"/>, which posts the notification with the words the game handed over (the
    /// game's text table; the receiver has no content of its own). The app cancels both when it comes back. Android 13+
    /// asks for the notification permission when a setting is turned on.
    /// </summary>
    /// <remarks>
    /// UNVERIFIED: the Grove notification and this generalisation were written without the Android workload and have
    /// never been compiled or run. Check both notifications (and that the idle one still behaves as before) on a device.
    /// </remarks>
    public sealed class AndroidNotifier : ILocalNotifier
    {
        public const int PermissionRequestCode = 4101;

        private readonly Activity _activity;

        public AndroidNotifier(Activity activity)
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

        public void Schedule(LocalNotification kind, DateTime utc, LocalNotificationText text)
        {
            AlarmManager alarms = (AlarmManager)_activity.GetSystemService(Context.AlarmService);
            long at = (long)(utc.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
            alarms?.Set(AlarmType.Rtc, at, Pending(kind, text));
        }

        public void Cancel(LocalNotification kind)
        {
            AlarmManager alarms = (AlarmManager)_activity.GetSystemService(Context.AlarmService);
            alarms?.Cancel(Pending(kind, null));
            NotificationManager notifications = (NotificationManager)_activity.GetSystemService(Context.NotificationService);
            notifications?.Cancel(LocalNotificationReceiver.NotificationIdOf(kind));
        }

        private PendingIntent Pending(LocalNotification kind, LocalNotificationText text)
        {
            Intent intent = new Intent(_activity, typeof(LocalNotificationReceiver));
            intent.SetAction(LocalNotificationReceiver.ActionOf(kind));
            intent.PutExtra(LocalNotificationReceiver.ExtraKind, (int)kind);
            if (text != null)
            {
                intent.PutExtra(LocalNotificationReceiver.ExtraTitle, text.Title);
                intent.PutExtra(LocalNotificationReceiver.ExtraBody, text.Body);
                intent.PutExtra(LocalNotificationReceiver.ExtraChannel, text.Channel);
                intent.PutExtra(LocalNotificationReceiver.ExtraChannelDescription, text.ChannelDescription);
            }

            return PendingIntent.GetBroadcast(_activity, (int)kind, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }
    }

    /// <summary>Posts a local notification when its alarm fires, on the kind's own channel; tapping it opens the game.</summary>
    [BroadcastReceiver(Enabled = true, Exported = false)]
    public sealed class LocalNotificationReceiver : BroadcastReceiver
    {
        public const string ExtraKind = "beastcraft.kind";
        public const string ExtraTitle = "beastcraft.title";
        public const string ExtraBody = "beastcraft.body";
        public const string ExtraChannel = "beastcraft.channel";
        public const string ExtraChannelDescription = "beastcraft.channel_description";

        /// <summary>The idle notification keeps its original id and channel id ("idle"), so an update changes nothing for it.</summary>
        public static int NotificationIdOf(LocalNotification kind)
        {
            return kind == LocalNotification.GroveReady ? 4103 : 4102;
        }

        public static string ChannelIdOf(LocalNotification kind)
        {
            return kind == LocalNotification.GroveReady ? "grove" : "idle";
        }

        public static string ActionOf(LocalNotification kind)
        {
            return "beastcraft.notify." + ChannelIdOf(kind);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            NotificationManager notifications = (NotificationManager)context.GetSystemService(Context.NotificationService);
            if (notifications == null || intent == null)
            {
                return;
            }

            LocalNotification kind = (LocalNotification)intent.GetIntExtra(ExtraKind, (int)LocalNotification.IdleFull);
            string channelId = ChannelIdOf(kind);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                notifications.CreateNotificationChannel(new NotificationChannel(channelId, intent.GetStringExtra(ExtraChannel) ?? channelId, NotificationImportance.Default)
                {
                    Description = intent.GetStringExtra(ExtraChannelDescription) ?? string.Empty
                });
            }

            Intent open = new Intent(context, typeof(MainActivity));
            open.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            PendingIntent tap = PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
#pragma warning disable CA1422 // Notification.Builder(Context) is the pre-26 constructor, used only there.
            Notification.Builder builder = Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(context, channelId) : new Notification.Builder(context);
#pragma warning restore CA1422
            builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
                   .SetContentTitle(intent.GetStringExtra(ExtraTitle) ?? string.Empty)
                   .SetContentText(intent.GetStringExtra(ExtraBody) ?? string.Empty)
                   .SetContentIntent(tap)
                   .SetAutoCancel(true);
            notifications.Notify(NotificationIdOf(kind), builder.Build());
        }
    }
}
