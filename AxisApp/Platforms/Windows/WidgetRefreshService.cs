using AxisApp.Services;

namespace AxisApp;

/// <summary>No Windows widget host exists (or is planned — see POSSIBLE_FEATURES.md, Android-only
/// by design, mirroring where push already landed). Deliberate no-op, same shape as
/// NoopWidgetRefreshService.</summary>
public class WidgetRefreshService : IWidgetRefreshService
{
    public void RequestBalancesRefresh()
    {
    }

    public void RequestEventsRefresh()
    {
    }
}
