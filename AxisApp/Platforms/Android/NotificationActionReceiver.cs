using System.Globalization;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using AxisApp.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace AxisApp;

/// <summary>Handles the SETTLE/GOING/MAYBE/CAN'T GO tray actions AxisFirebaseMessagingService adds
/// to an expense/event push — writes directly via the app's real repositories (same
/// WidgetDataAccess.GetReadyServicesAsync() pattern the home-screen widgets use to reach MAUI's DI
/// container and restore the Supabase session from a cold, Activity-less process start), never
/// opening the app on success.
///
/// On ANY failure (not signed in, network error, RLS rejection) this falls back to opening the app
/// to the relevant screen instead of silently dropping the action — a deliberate choice: a tray
/// action that looks like it worked but didn't (e.g. an RSVP that never actually reached the
/// server) is worse than one extra tap, since there's no way for the user to tell the two apart
/// from a bare success/failure toast on a background write they can't see.</summary>
[BroadcastReceiver(Name = "com.aitorsansal.axisapp.NotificationActionReceiver", Exported = false)]
public class NotificationActionReceiver : BroadcastReceiver
{
    public const string ActionSettle = "com.aitorsansal.axisapp.action.SETTLE";
    public const string ActionRsvpGoing = "com.aitorsansal.axisapp.action.RSVP_GOING";
    public const string ActionRsvpMaybe = "com.aitorsansal.axisapp.action.RSVP_MAYBE";
    public const string ActionRsvpNotGoing = "com.aitorsansal.axisapp.action.RSVP_NOT_GOING";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action is null) return;

        var pendingResult = GoAsync();
        _ = HandleAndFinishAsync(context, intent, pendingResult);
    }

    private static async Task HandleAndFinishAsync(Context context, Intent intent, PendingResult? pendingResult)
    {
        try
        {
            var handled = await TryHandleAsync(context, intent);
            if (!handled)
            {
                OpenApp(context, intent);
            }
        }
        catch
        {
            OpenApp(context, intent);
        }
        finally
        {
            pendingResult?.Finish();
        }
    }

    private static async Task<bool> TryHandleAsync(Context context, Intent intent)
    {
        var services = await WidgetDataAccess.GetReadyServicesAsync();
        if (services is null) return false;

        var authService = services.GetRequiredService<IAuthService>();
        if (!authService.IsAuthenticated) return false;

        return intent.Action switch
        {
            ActionSettle => await HandleSettleAsync(context, intent, services),
            ActionRsvpGoing => await HandleRsvpAsync(context, intent, services, "going"),
            ActionRsvpMaybe => await HandleRsvpAsync(context, intent, services, "maybe"),
            ActionRsvpNotGoing => await HandleRsvpAsync(context, intent, services, "not_going"),
            _ => false,
        };
    }

    private static async Task<bool> HandleSettleAsync(Context context, Intent intent, IServiceProvider services)
    {
        if (!Guid.TryParse(intent.GetStringExtra("payer_member_id"), out var payeeId)) return false;
        if (!decimal.TryParse(intent.GetStringExtra("share_amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return false;

        var membersRepository = services.GetRequiredService<IMembersRepository>();
        var me = await membersRepository.GetMyMemberAsync();
        if (me is null) return false;

        Guid? groupId = Guid.TryParse(intent.GetStringExtra("group_id"), out var g) ? g : null;
        var currency = intent.GetStringExtra("currency") ?? "EUR";

        var expensesRepository = services.GetRequiredService<IExpensesRepository>();
        var settlement = new Expense
        {
            GroupId = groupId,
            PaidByMemberId = me.Id,
            Amount = amount,
            Currency = currency,
            Description = LocalizationResourceManager.Instance["GroupDetail_SettleUp"],
            OccurredAt = DateTime.UtcNow,
            IsSettlement = true,
        };
        var shares = new List<ExpenseShare> { new() { MemberId = payeeId, ShareAmount = amount } };
        await expensesRepository.AddAsync(settlement, shares);

        UpdateNotification(context,
            LocalizationResourceManager.Instance["Notification_Settled"],
            LocalizationResourceManager.Instance["Notification_SettledBody"]);
        return true;
    }

    private static async Task<bool> HandleRsvpAsync(Context context, Intent intent, IServiceProvider services, string response)
    {
        if (!Guid.TryParse(intent.GetStringExtra("event_id"), out var eventId)) return false;

        var membersRepository = services.GetRequiredService<IMembersRepository>();
        var me = await membersRepository.GetMyMemberAsync();
        if (me is null) return false;

        var eventsRepository = services.GetRequiredService<IEventsRepository>();
        await eventsRepository.UpsertRsvpAsync(eventId, me.Id, response);

        var label = response switch
        {
            "going" => LocalizationResourceManager.Instance["Notification_RsvpGoing"],
            "maybe" => LocalizationResourceManager.Instance["Notification_RsvpMaybe"],
            _ => LocalizationResourceManager.Instance["Notification_RsvpNotGoing"],
        };
        UpdateNotification(context, label, intent.GetStringExtra("group_name") ?? "");
        return true;
    }

    /// <summary>Replaces the same tray entry the push originally posted (fixed NotificationId, see
    /// AxisNotifications) with a plain, non-actionable confirmation — mirrors how tapping AutoCancel
    /// dismisses it, except here the action itself already completed so there's nothing left to
    /// offer.</summary>
    private static void UpdateNotification(Context context, string title, string body)
    {
        var notification = new NotificationCompat.Builder(context, AxisNotifications.ChannelId)
            .SetContentTitle(title)
            .SetContentText(body)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .Build()!;

        NotificationManagerCompat.From(context)!.Notify(AxisNotifications.NotificationId, notification);
    }

    /// <summary>Same PendingIntent shape AxisFirebaseMessagingService's own tap-to-open uses —
    /// MainActivity.HandleIntent already knows how to route group_id/event_id extras to the right
    /// screen, so this doesn't need its own routing logic.</summary>
    private static void OpenApp(Context context, Intent sourceIntent)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask);

        var groupId = sourceIntent.GetStringExtra("group_id");
        var groupName = sourceIntent.GetStringExtra("group_name");
        var eventId = sourceIntent.GetStringExtra("event_id");
        if (!string.IsNullOrEmpty(groupId))
        {
            intent.PutExtra("group_id", groupId);
            intent.PutExtra("group_name", groupName ?? "");
        }
        if (!string.IsNullOrEmpty(eventId))
        {
            intent.PutExtra("event_id", eventId);
        }

        context.StartActivity(intent);
    }
}
