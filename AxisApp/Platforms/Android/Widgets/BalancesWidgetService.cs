using Android.App;
using Android.Content;
using Android.Widget;

namespace AxisApp.Widgets;

[Service(Name = "com.aitorsansal.axisapp.BalancesWidgetService", Permission = "android.permission.BIND_REMOTEVIEWS", Exported = false)]
public class BalancesWidgetService : RemoteViewsService
{
    public override IRemoteViewsFactory OnGetViewFactory(Intent? intent) =>
        new BalancesWidgetRemoteViewsFactory(this, intent!);
}
