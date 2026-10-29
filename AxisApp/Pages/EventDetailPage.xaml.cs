using AxisApp.ViewModels;
using Microsoft.Maui.Networking;

namespace AxisApp.Pages;

public partial class EventDetailPage : ContentPage
{
    private readonly EventDetailViewModel vm;
    private IDispatcherTimer? captionTimer;
    private Window? subscribedWindow;
    private bool foreground;

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
        foreground = true;
        _ = vm.RefreshIfStaleAsync(force: true);

        subscribedWindow = Window;
        if (subscribedWindow is not null)
        {
            subscribedWindow.Resumed += OnWindowResumed;
            subscribedWindow.Stopped += OnWindowStopped;
        }
        Connectivity.ConnectivityChanged += OnConnectivityChanged;

        captionTimer = Dispatcher.CreateTimer();
        captionTimer.Interval = TimeSpan.FromSeconds(30);
        captionTimer.Tick += OnCaptionTick;
        captionTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        foreground = false;
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;

        if (subscribedWindow is not null)
        {
            subscribedWindow.Resumed -= OnWindowResumed;
            subscribedWindow.Stopped -= OnWindowStopped;
        }
        subscribedWindow = null;

        if (captionTimer is not null)
        {
            captionTimer.Stop();
            captionTimer.Tick -= OnCaptionTick;
            captionTimer = null;
        }
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        foreground = true;
        captionTimer?.Start();
        _ = vm.RefreshIfStaleAsync(force: true);
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        foreground = false;
        captionTimer?.Stop();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
            Dispatcher.Dispatch(() => { if (foreground) _ = vm.RefreshIfStaleAsync(force: true); });
    }

    private void OnCaptionTick(object? sender, EventArgs e)
    {
        if (foreground) _ = vm.RefreshIfStaleAsync();
    }
}
