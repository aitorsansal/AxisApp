using AxisApp.ViewModels;
using Microsoft.Maui.Networking;
using System.ComponentModel;

namespace AxisApp.Pages;

public partial class GroupDetailPage : ContentPage
{
    private readonly GroupDetailViewModel vm;
    private IDispatcherTimer? refreshTimer;
    private Window? subscribedWindow;
    private bool foreground;
    public GroupDetailPage(GroupDetailViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        foreground = true;
        vm.PropertyChanged += OnViewModelChanged;
        subscribedWindow = Window;
        if (subscribedWindow is not null)
        {
            subscribedWindow.Resumed += OnWindowResumed;
            subscribedWindow.Stopped += OnWindowStopped;
        }
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        refreshTimer = Dispatcher.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(30);
        refreshTimer.Tick += OnRefreshTick;
        refreshTimer.Start();
        RefreshSelectedTab(true);
    }

    protected override void OnDisappearing()
    {
        foreground = false;
        vm.PropertyChanged -= OnViewModelChanged;
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        if (subscribedWindow is not null)
        {
            subscribedWindow.Resumed -= OnWindowResumed;
            subscribedWindow.Stopped -= OnWindowStopped;
        }
        subscribedWindow = null;
        if (refreshTimer is not null)
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= OnRefreshTick;
            refreshTimer = null;
        }
        base.OnDisappearing();
    }

    private void RefreshSelectedTab(bool force = false)
    {
        if (!foreground) return;
        if (vm.IsEventsTabSelected) _ = vm.EventsVm.RefreshIfStaleAsync(force);
        else if (vm.IsExpensesTabSelected) _ = vm.ExpensesVm.RefreshIfStaleAsync(force);
    }

    private void OnRefreshTick(object? sender, EventArgs e) => RefreshSelectedTab();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GroupDetailViewModel.SelectedTab)) RefreshSelectedTab(true);
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        foreground = true;
        refreshTimer?.Start();
        RefreshSelectedTab(true);
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        foreground = false;
        refreshTimer?.Stop();
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet) Dispatcher.Dispatch(() => RefreshSelectedTab(true));
    }
}
