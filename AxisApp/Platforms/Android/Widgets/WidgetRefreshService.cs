using Android.App;
using Android.Appwidget;
using Android.Content;
using AxisApp.Services;
using AxisApp.Widgets;

namespace AxisApp;

public class WidgetRefreshService : IWidgetRefreshService
{
    public void RequestBalancesRefresh() => Broadcast<BalancesWidgetProvider>(BalancesWidgetProvider.Component);

    public void RequestEventsRefresh() => Broadcast<EventsWidgetProvider>(EventsWidgetProvider.Component);

    private static void Broadcast<TProvider>(Func<Context, ComponentName> component)
    {
        var context = global::Android.App.Application.Context;
        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(component(context));
        if (ids is not { Length: > 0 }) return;

        var intent = new Intent(context, typeof(TProvider));
        intent.SetAction(AppWidgetManager.ActionAppwidgetUpdate);
        intent.PutExtra(AppWidgetManager.ExtraAppwidgetIds, ids);
        context.SendBroadcast(intent);
    }
}
