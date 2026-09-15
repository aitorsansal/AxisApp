namespace AxisApp.Services;

/// <summary>Notifies the platform home-screen widgets (if any) that their underlying data changed,
/// so they refresh immediately instead of waiting on Android's ~30-minute updatePeriodMillis
/// floor. Balances and Events are separate widgets (see Platforms/Android/Widgets) with
/// independent refresh triggers — a new expense shouldn't repaint the Events widget and vice
/// versa. No-op on platforms without a widget implementation.</summary>
public interface IWidgetRefreshService
{
    void RequestBalancesRefresh();
    void RequestEventsRefresh();
}

public class NoopWidgetRefreshService : IWidgetRefreshService
{
    public void RequestBalancesRefresh()
    {
    }

    public void RequestEventsRefresh()
    {
    }
}
