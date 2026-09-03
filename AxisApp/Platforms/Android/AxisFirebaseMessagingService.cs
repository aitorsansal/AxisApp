using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;

namespace AxisApp;

/// <summary>Receives every push send-push sends (now a data-only FCM message, deliberately — see
/// that function's remarks: a "notification" block would make Android auto-display it via
/// Firebase's own default channel, with no way to control the tap action). Builds the shown
/// notification by hand instead, on a real "Axis" channel, with a PendingIntent carrying
/// group_id/group_name so tapping it lands on that specific group's detail page — funneled through
/// MainActivity's existing HandleIntent → App.HandleNotificationTap, the same cold-start-safe
/// queuing mechanism already proven for invite deep links.
///
/// [Service]/[IntentFilter] here are enough to register this with Android — .NET Android generates
/// the manifest entry from these attributes at build time, same as MainActivity's own
/// [Activity]/[IntentFilter], no manual AndroidManifest.xml edit needed.
///
/// Confirmed working end to end against a real device, including the one real bug this surfaced —
/// see App.xaml.cs's isReadyToNavigate remarks: this service can start the app's process with no
/// Activity ever appearing, which broke the deep-link queue's old "is Shell.Current null" readiness
/// check.</summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public class AxisFirebaseMessagingService : FirebaseMessagingService
{
    private const string ChannelId = "axis_default";

    // Fixed id: a second push while one is still showing replaces it rather than stacking a
    // second system-tray entry — acceptable for v1 (see CLAUDE.md), revisit if a genuinely busy
    // group makes losing an earlier unread notification actually matter.
    private const int NotificationId = 1001;

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var data = message.Data;
        if (data is null || !data.TryGetValue("title", out var title)) return;
        data.TryGetValue("body", out var body);
        data.TryGetValue("group_id", out var groupId);
        data.TryGetValue("group_name", out var groupName);

        EnsureChannel();

        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask);
        if (!string.IsNullOrEmpty(groupId))
        {
            intent.PutExtra("group_id", groupId);
            intent.PutExtra("group_name", groupName ?? "");
        }

        var pendingIntent = PendingIntent.GetActivity(
            this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(body ?? "")
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .SetContentIntent(pendingIntent)
            .Build();

        NotificationManagerCompat.From(this).Notify(NotificationId, notification);
    }

    private void EnsureChannel()
    {
        // CreateNotificationChannel is idempotent — Android dedupes by channel id, so calling this
        // on every message received (rather than once at app startup) is deliberately simple
        // rather than wrong.
        var channel = new NotificationChannel(ChannelId, "Axis notifications", NotificationImportance.Default)
        {
            Description = "New expenses and payments in your groups",
        };

        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }
}
