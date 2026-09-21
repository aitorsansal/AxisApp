using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class EventDetailPage : ContentPage
{
    private readonly EventDetailViewModel vm;
    private IDispatcherTimer? captionTimer;
    private Window? subscribedWindow;

    public EventDetailPage(EventDetailViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        BindingContext = vm;
    }

    /// <summary>The attendee counts are only fetched on navigation, pull-to-refresh and after the
    /// viewer's own taps, so they go stale while this page sits open (SECURITY_AUDIT.md #4). On
    /// appearing (which also covers coming back from Add expense / Edit) and on app resume the view
    /// model refetches them if they're over a minute old, and a slow timer keeps the "updated X ago"
    /// caption honest in between. Everything is unhooked in OnDisappearing so nothing ticks while
    /// the page isn't visible.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.RefreshIfStaleAsync();

        subscribedWindow = Window;
        if (subscribedWindow is not null) subscribedWindow.Resumed += OnWindowResumed;

        captionTimer = Dispatcher.CreateTimer();
        captionTimer.Interval = TimeSpan.FromSeconds(30);
        captionTimer.Tick += OnCaptionTick;
        captionTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        if (subscribedWindow is not null) subscribedWindow.Resumed -= OnWindowResumed;
        subscribedWindow = null;

        if (captionTimer is not null)
        {
            captionTimer.Stop();
            captionTimer.Tick -= OnCaptionTick;
            captionTimer = null;
        }
    }

    private void OnWindowResumed(object? sender, EventArgs e) => _ = vm.RefreshIfStaleAsync();

    private void OnCaptionTick(object? sender, EventArgs e) => vm.UpdateUpdatedCaption();
}
