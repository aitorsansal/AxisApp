using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace AxisApp.Widgets;

[BroadcastReceiver(Name = "com.aitorsansal.axisapp.EventsWidgetProvider", Label = "Axis events", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/events_widget_info")]
public class EventsWidgetProvider : AppWidgetProvider
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            base.OnReceive(context, intent);
            return;
        }

        if (intent.Action == AppWidgetManager.ActionAppwidgetUpdate)
        {
            var ids = intent.GetIntArrayExtra(AppWidgetManager.ExtraAppwidgetIds)
                      ?? AppWidgetManager.GetInstance(context)?.GetAppWidgetIds(Component(context));

            if (ids is { Length: > 0 })
            {
                var pendingResult = GoAsync();
                _ = RefreshAllAsync(context, ids, pendingResult);
                return;
            }
        }

        base.OnReceive(context, intent);
    }

    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions)
    {
        base.OnAppWidgetOptionsChanged(context, appWidgetManager, appWidgetId, newOptions);
        if (context is null) return;

        var pendingResult = GoAsync();
        _ = RefreshAllAsync(context, [appWidgetId], pendingResult);
    }

    public override void OnDeleted(Context? context, int[]? appWidgetIds)
    {
        base.OnDeleted(context, appWidgetIds);
        if (context is null || appWidgetIds is null) return;

        foreach (var id in appWidgetIds)
            WidgetGroupScope.Clear(context, id);
    }

    private static async Task RefreshAllAsync(Context context, int[] ids, BroadcastReceiver.PendingResult? pendingResult)
    {
        try
        {
            var manager = AppWidgetManager.GetInstance(context);
            if (manager is null) return;

            foreach (var id in ids)
                ApplyWidget(context, manager, id);

            manager.NotifyAppWidgetViewDataChanged(ids, Resource.Id.widget_row_list);
        }
        finally
        {
            pendingResult?.Finish();
        }
    }

    private static void ApplyWidget(Context context, AppWidgetManager manager, int widgetId)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_events);

        var scope = WidgetGroupScope.Get(context, widgetId);
        views.SetTextViewText(Resource.Id.widget_header, scope is null ? "Upcoming" : "Upcoming events");

        var openAppPending = PendingIntent.GetActivity(context, widgetId, WidgetNavigation.BuildOpenAppIntent(context),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        views.SetOnClickPendingIntent(Resource.Id.widget_header, openAppPending);

        var reconfigurePending = PendingIntent.GetActivity(context, widgetId, WidgetNavigation.BuildReconfigureIntent(context, widgetId),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        views.SetOnClickPendingIntent(Resource.Id.widget_configure, reconfigurePending);

        var serviceIntent = new Intent(context, typeof(EventsWidgetService));
        serviceIntent.PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);
        serviceIntent.SetData(global::Android.Net.Uri.Parse($"axiswidget://events/{widgetId}"));
#pragma warning disable CS0618
        views.SetRemoteAdapter(widgetId, Resource.Id.widget_row_list, serviceIntent);
#pragma warning restore CS0618
        views.SetEmptyView(Resource.Id.widget_row_list, Resource.Id.widget_row_empty);

        var templateIntent = WidgetNavigation.BuildOpenAppIntent(context);
        var templatePending = PendingIntent.GetActivity(context, widgetId, templateIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Mutable);
        views.SetPendingIntentTemplate(Resource.Id.widget_row_list, templatePending);

        manager.UpdateAppWidget(widgetId, views);
    }

    internal static ComponentName Component(Context context) =>
        new(context, Java.Lang.Class.FromType(typeof(EventsWidgetProvider)));
}
