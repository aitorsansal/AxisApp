using Android.App;
using Android.Content;
using Android.Widget;

namespace AxisApp.Widgets;

[Service(Name = "com.aitorsansal.axisapp.EventsWidgetService", Permission = "android.permission.BIND_REMOTEVIEWS", Exported = false)]
public class EventsWidgetService : RemoteViewsService
{
    public override IRemoteViewsFactory OnGetViewFactory(Intent? intent) =>
        new EventsWidgetRemoteViewsFactory(this, intent!);
}
