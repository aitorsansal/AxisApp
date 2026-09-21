using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace AxisApp.Widgets;

public class EventsWidgetRemoteViewsFactory : Java.Lang.Object, RemoteViewsService.IRemoteViewsFactory
{
    private readonly Context context;
    private readonly int appWidgetId;
    private List<EventRow> rows = [];

    public EventsWidgetRemoteViewsFactory(Context context, Intent intent)
    {
        this.context = context;
        appWidgetId = intent.GetIntExtra(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId);
    }

    public int Count => rows.Count;
    public bool HasStableIds => true;
    public RemoteViews? LoadingView => null;
    public int ViewTypeCount => 1;

    public void OnCreate()
    {
    }

    /// <summary>See BalancesWidgetRemoteViewsFactory.OnDataSetChanged: an exception escaping this
    /// system-thread callback crashes the whole app process, so a failed refresh keeps the last good
    /// rows instead.</summary>
    public void OnDataSetChanged()
    {
        try
        {
            var scope = WidgetGroupScope.Get(context, appWidgetId);
            rows = EventsWidgetDataProvider.GetSnapshotAsync(scope).GetAwaiter().GetResult();
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Warn("AxisWidget", $"Events widget refresh failed, keeping last data: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void OnDestroy() => rows = [];

    public long GetItemId(int position) => rows[position].EventId.GetHashCode();

    public RemoteViews GetViewAt(int position)
    {
        var row = rows[position];
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_events_row);

        // Local time — StartsAt is stored/queried as UTC (see EventsWidgetDataProvider), same
        // "convert only at the display edge" convention EventDetailPage/AddEventPage already use.
        var local = row.StartsAt.ToLocalTime();
        var label = local.Date == DateTime.Today ? $"Today {local:HH:mm}" : $"{local:ddd d MMM, HH:mm}";

        views.SetTextViewText(Resource.Id.widget_row_title, row.Title);
        views.SetTextViewText(Resource.Id.widget_row_subtitle, $"{row.GroupName} · {label}");

        var fillInIntent = WidgetNavigation.BuildEventIntent(context, row.GroupId, row.EventId);
        views.SetOnClickFillInIntent(Resource.Id.widget_row_root, fillInIntent);

        return views;
    }
}
