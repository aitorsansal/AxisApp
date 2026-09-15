using Android.Content;

namespace AxisApp.Widgets;

/// <summary>Builds the same group_id/group_name/event_id-carrying Intent a tapped push
/// notification already produces (AxisFirebaseMessagingService), routed through the identical
/// MainActivity.HandleIntent -> App.HandleNotificationTap cold-start-safe queue — see
/// App.xaml.cs's isReadyToNavigate remarks. No separate polling loop needed here, unlike a
/// from-scratch widget implementation would need, because that queue already exists.</summary>
internal static class WidgetNavigation
{
    public static Intent BuildOpenAppIntent(Context context)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask);
        return intent;
    }

    public static Intent BuildGroupIntent(Context context, Guid groupId, string groupName)
    {
        var intent = BuildOpenAppIntent(context);
        intent.PutExtra("group_id", groupId.ToString());
        intent.PutExtra("group_name", groupName);
        return intent;
    }

    public static Intent BuildEventIntent(Context context, Guid groupId, Guid eventId)
    {
        var intent = BuildOpenAppIntent(context);
        intent.PutExtra("group_id", groupId.ToString());
        intent.PutExtra("event_id", eventId.ToString());
        return intent;
    }

    /// <summary>Re-opens the group picker for an already-placed widget instance — the only
    /// reliable way to let someone change a widget's group after placement, since Android's own
    /// "Edit widget" reconfigure affordance is launcher/OS-version dependent (reliable only from
    /// Android 12's reconfigurable widgetFeatures onward) and this app's minSdk is 23. Explicitly
    /// carries EXTRA_APPWIDGET_ID since, unlike the system's placement-time configure flow, launching
    /// this way doesn't supply it automatically.</summary>
    public static Intent BuildReconfigureIntent(Context context, int appWidgetId)
    {
        var intent = new Intent(context, typeof(WidgetConfigActivity));
        intent.SetFlags(ActivityFlags.NewTask);
        intent.PutExtra(global::Android.Appwidget.AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        return intent;
    }
}
