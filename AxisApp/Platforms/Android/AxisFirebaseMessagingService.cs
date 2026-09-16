using Android.App;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.App;
using AxisApp.Localization;
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
/// Upgraded 2026-09-16 to BigTextStyle + a payer large icon + tray actions (SETTLE on an expense
/// you owe a share of, GOING/MAYBE/CAN'T GO on an event) — see NotificationActionReceiver for what
/// tapping those does. Plain BigTextStyle (no actions) still applies to every other pushType
/// (settlement, event_cancelled/birthday/reminder) since there's nothing actionable to offer there.
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
    // A slow/failed avatar fetch just means the notification renders without a large icon — never
    // worth delaying delivery for. Generous on purpose: the user confirmed a notification landing
    // a little late is fine (this isn't a chat app), so there's no reason to race a tight timeout.
    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var data = message.Data;
        if (data is null || !data.TryGetValue("title", out var title)) return;
        data.TryGetValue("body", out var body);
        data.TryGetValue("type", out var type);
        data.TryGetValue("group_id", out var groupId);
        data.TryGetValue("group_name", out var groupName);
        data.TryGetValue("expense_id", out var expenseId);
        data.TryGetValue("event_id", out var eventId);
        data.TryGetValue("payer_member_id", out var payerMemberId);
        data.TryGetValue("payer_avatar_url", out var payerAvatarUrl);
        data.TryGetValue("my_share_amount", out var myShareAmount);
        data.TryGetValue("currency", out var currency);

        // Fire-and-forget from OnMessageReceived's own thread — FirebaseMessagingService already
        // runs OnMessageReceived off the main thread, and this method's own execution window
        // (Firebase gives it a background-execution allowance similar to a JobService) is what
        // covers the avatar fetch below, so no separate WakeLock/GoAsync-style bookkeeping is
        // needed the way NotificationActionReceiver's plain BroadcastReceiver needs it.
        BuildAndShowAsync(title, body, type, groupId, groupName, expenseId, eventId,
            payerMemberId, payerAvatarUrl, myShareAmount, currency).GetAwaiter().GetResult();
    }

    private async Task BuildAndShowAsync(
        string title, string? body, string? type, string? groupId, string? groupName,
        string? expenseId, string? eventId, string? payerMemberId, string? payerAvatarUrl,
        string? myShareAmount, string? currency)
    {
        EnsureChannel();

        var pendingIntent = BuildOpenAppPendingIntent(groupId, groupName, eventId);

        var builder = new NotificationCompat.Builder(this, AxisNotifications.ChannelId)
            .SetContentTitle(title)
            .SetContentText(body ?? "")
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(body ?? ""))
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .SetContentIntent(pendingIntent);

        if (type == "expense" && !string.IsNullOrEmpty(payerAvatarUrl))
        {
            var avatar = await TryDecodeBitmapAsync(payerAvatarUrl);
            if (avatar is not null) builder.SetLargeIcon(avatar);
        }

        if (type == "expense" && !string.IsNullOrEmpty(expenseId))
        {
            // Only offer SETTLE when this recipient actually owes a share — the payer themselves
            // (my_share_amount left empty by send-push) has nothing to settle from this push.
            if (!string.IsNullOrEmpty(myShareAmount) && !string.IsNullOrEmpty(payerMemberId))
            {
                var settleIntent = BuildActionIntent(NotificationActionReceiver.ActionSettle,
                    groupId, groupName, eventId: null);
                settleIntent.PutExtra("expense_id", expenseId);
                settleIntent.PutExtra("payer_member_id", payerMemberId);
                settleIntent.PutExtra("share_amount", myShareAmount);
                settleIntent.PutExtra("currency", currency ?? "EUR");
                builder.AddAction(0, LocalizationResourceManager.Instance["Notification_ActionSettle"],
                    BuildActionPendingIntent(1, settleIntent));
            }

            builder.AddAction(0, LocalizationResourceManager.Instance["Notification_ActionView"], pendingIntent);
        }
        else if ((type == "event_created" || type == "event_changed") && !string.IsNullOrEmpty(eventId))
        {
            builder.AddAction(0, LocalizationResourceManager.Instance["Notification_ActionGoing"],
                BuildActionPendingIntent(2, BuildActionIntent(NotificationActionReceiver.ActionRsvpGoing, groupId, groupName, eventId)));
            builder.AddAction(0, LocalizationResourceManager.Instance["Notification_ActionMaybe"],
                BuildActionPendingIntent(3, BuildActionIntent(NotificationActionReceiver.ActionRsvpMaybe, groupId, groupName, eventId)));
            builder.AddAction(0, LocalizationResourceManager.Instance["Notification_ActionNotGoing"],
                BuildActionPendingIntent(4, BuildActionIntent(NotificationActionReceiver.ActionRsvpNotGoing, groupId, groupName, eventId)));
        }

        NotificationManagerCompat.From(this).Notify(AxisNotifications.NotificationId, builder.Build());
    }

    private Intent BuildActionIntent(string action, string? groupId, string? groupName, string? eventId)
    {
        var intent = new Intent(action, null, this, typeof(NotificationActionReceiver));
        if (!string.IsNullOrEmpty(groupId)) intent.PutExtra("group_id", groupId);
        if (!string.IsNullOrEmpty(groupName)) intent.PutExtra("group_name", groupName);
        if (!string.IsNullOrEmpty(eventId)) intent.PutExtra("event_id", eventId);
        return intent;
    }

    private PendingIntent BuildActionPendingIntent(int requestCode, Intent intent) =>
        PendingIntent.GetBroadcast(this, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private PendingIntent BuildOpenAppPendingIntent(string? groupId, string? groupName, string? eventId)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask);
        if (!string.IsNullOrEmpty(groupId))
        {
            intent.PutExtra("group_id", groupId);
            intent.PutExtra("group_name", groupName ?? "");
        }
        if (!string.IsNullOrEmpty(eventId))
        {
            intent.PutExtra("event_id", eventId);
        }

        return PendingIntent.GetActivity(
            this, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static async Task<Bitmap?> TryDecodeBitmapAsync(string url)
    {
        try
        {
            var bytes = await httpClient.GetByteArrayAsync(url);
            // BitmapFactory.DecodeByteArray is synchronous — there's no Async-suffixed variant in
            // the Android SDK for this call, unlike genuinely async Java APIs the bindings
            // auto-generate a Task-returning overload for. Task.Run just keeps the decode off
            // whatever thread called this.
            return await Task.Run(() => BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length));
        }
        catch
        {
            // Network failure, 404 (avatar deleted since the push was queued), decode failure —
            // all fall back to no large icon, never worth failing the whole notification over.
            return null;
        }
    }

    private void EnsureChannel()
    {
        // CreateNotificationChannel is idempotent — Android dedupes by channel id, so calling this
        // on every message received (rather than once at app startup) is deliberately simple
        // rather than wrong.
        var channel = new NotificationChannel(AxisNotifications.ChannelId, "Axis notifications", NotificationImportance.Default)
        {
            Description = "New expenses and events in your groups",
        };

        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(channel);
    }
}
