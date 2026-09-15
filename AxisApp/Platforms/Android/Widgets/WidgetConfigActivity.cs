using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AxisApp.Services;
using Microsoft.Extensions.DependencyInjection;
using ListView = Android.Widget.ListView;

namespace AxisApp.Widgets;

/// <summary>Shown once, by Android itself, the moment a Balances or Events widget is dropped on
/// the home screen (android:configure in each widget's *_widget_info.xml points here) — lets the
/// user pick "All groups" or one specific group for that placed instance. Content-agnostic: both
/// widget types show the same group list, since the picker only decides scope, not which data
/// type to render (that's already fixed by which widget the user dragged out).
///
/// Must call SetResult(Ok, ...) before finishing or Android removes the widget it was just
/// configuring — Result.Canceled (the default if the user backs out) does exactly that.</summary>
[Activity(Name = "com.aitorsansal.axisapp.WidgetConfigActivity", Theme = "@style/Maui.SplashTheme", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetConfigure])]
public class WidgetConfigActivity : Activity
{
    private int appWidgetId = AppWidgetManager.InvalidAppwidgetId;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetResult(Result.Canceled);

        appWidgetId = Intent?.Extras?.GetInt(AppWidgetManager.ExtraAppwidgetId, AppWidgetManager.InvalidAppwidgetId)
            ?? AppWidgetManager.InvalidAppwidgetId;
        if (appWidgetId == AppWidgetManager.InvalidAppwidgetId)
        {
            Finish();
            return;
        }

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#0B1220"));
        root.SetPadding(24, 24, 24, 24);

        var title = new TextView(this)
        {
            Text = "Which group?",
            TextSize = 18
        };
        title.SetTextColor(global::Android.Graphics.Color.White);
        root.AddView(title);

        var listView = new ListView(this);
        root.AddView(listView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        SetContentView(root);

        _ = LoadGroupsAsync(listView);
    }

    private async Task LoadGroupsAsync(ListView listView)
    {
        var services = await WidgetDataAccess.GetReadyServicesAsync();
        var authService = services?.GetService<IAuthService>();

        var labels = new List<string> { "All groups" };
        var groupIds = new List<Guid?> { null };

        if (services is not null && authService is { IsAuthenticated: true })
        {
            var groupsRepository = services.GetRequiredService<IGroupsRepository>();
            var groups = await groupsRepository.GetMyGroupsAsync();
            foreach (var group in groups)
            {
                labels.Add(group.Name);
                groupIds.Add(group.Id);
            }
        }

        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, labels);
        listView.Adapter = adapter;
        listView.ItemClick += (_, e) =>
        {
            WidgetGroupScope.Set(this, appWidgetId, groupIds[e.Position]);
            FinishConfiguring();
        };
    }

    private void FinishConfiguring()
    {
        // Push an immediate render for this one instance rather than leaving it blank until
        // Android's own periodic refresh — the provider that owns this appWidgetId resolves
        // itself from AppWidgetManager rather than being passed in, since this activity is shared
        // by both widget types.
        var manager = AppWidgetManager.GetInstance(this);
        var providerClassName = manager?.GetAppWidgetInfo(appWidgetId)?.Provider?.ClassName;

        // Matches the explicit Name= given to each [BroadcastReceiver] attribute below, not the
        // C# type's own FullName — .NET for Android's default generated Java name (a build-time
        // crc64 hash) was deliberately overridden to this fixed string precisely so it could be
        // compared/referenced predictably here and in the widget-info XML's android:configure.
        if (providerClassName == "com.aitorsansal.axisapp.BalancesWidgetProvider")
            SendBroadcast(RefreshIntent<BalancesWidgetProvider>());
        else if (providerClassName == "com.aitorsansal.axisapp.EventsWidgetProvider")
            SendBroadcast(RefreshIntent<EventsWidgetProvider>());

        var result = new Intent();
        result.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        SetResult(Result.Ok, result);
        Finish();
    }

    private Intent RefreshIntent<TProvider>()
    {
        var intent = new Intent(this, typeof(TProvider));
        intent.SetAction(AppWidgetManager.ActionAppwidgetUpdate);
        intent.PutExtra(AppWidgetManager.ExtraAppwidgetIds, new[] { appWidgetId });
        return intent;
    }
}
