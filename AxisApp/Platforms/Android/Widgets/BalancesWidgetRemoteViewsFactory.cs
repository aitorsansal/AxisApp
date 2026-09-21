using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Widget;
using Color = Android.Graphics.Color;

namespace AxisApp.Widgets;

public class BalancesWidgetRemoteViewsFactory : Java.Lang.Object, RemoteViewsService.IRemoteViewsFactory
{
    // Matches Resources/Styles/Colors.xaml's Success/Danger/TextSecondary — native RemoteViews
    // can't reference the MAUI ResourceDictionary, so these are duplicated by value (see also
    // widget_background.xml's own note on the same limitation for SurfaceElevated).
    private static readonly Color OwedToMeColor = Color.ParseColor("#38D998");
    private static readonly Color IOweColor = Color.ParseColor("#FF5C6C");
    private static readonly Color SettledColor = Color.ParseColor("#C3CCDA");

    private readonly Context context;
    private readonly int appWidgetId;
    private List<BalanceRow> rows = [];

    public BalancesWidgetRemoteViewsFactory(Context context, Intent intent)
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

    /// <summary>Runs on a system thread pool thread, so an exception escaping here isn't caught by
    /// anything and takes the whole app process down (seen live: a transient PGRST303 "JWT issued at
    /// future" from Supabase crashed the app from a background widget refresh — as would simply being
    /// offline). Any failure keeps the last good rows on screen instead.</summary>
    public void OnDataSetChanged()
    {
        try
        {
            var scope = WidgetGroupScope.Get(context, appWidgetId);
            rows = BalancesWidgetDataProvider.GetSnapshotAsync(scope).GetAwaiter().GetResult();
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Warn("AxisWidget", $"Balances widget refresh failed, keeping last data: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void OnDestroy() => rows = [];

    public long GetItemId(int position) => rows[position].GroupId.GetHashCode();

    public RemoteViews GetViewAt(int position)
    {
        var row = rows[position];
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_balances_row);

        views.SetTextViewText(Resource.Id.widget_row_name, row.GroupName);
        if (row.Balance == 0)
        {
            views.SetTextViewText(Resource.Id.widget_row_amount, "Settled up");
            views.SetTextColor(Resource.Id.widget_row_amount, SettledColor);
        }
        else
        {
            var verb = row.Balance > 0 ? "owes you" : "you owe";
            views.SetTextViewText(Resource.Id.widget_row_amount, $"{verb} {Math.Abs(row.Balance):0.00}{row.Currency}");
            views.SetTextColor(Resource.Id.widget_row_amount, row.Balance > 0 ? OwedToMeColor : IOweColor);
        }

        var fillInIntent = WidgetNavigation.BuildGroupIntent(context, row.GroupId, row.GroupName);
        views.SetOnClickFillInIntent(Resource.Id.widget_row_root, fillInIntent);

        return views;
    }
}
