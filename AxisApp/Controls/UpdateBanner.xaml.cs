using AxisApp.Services;

namespace AxisApp.Controls;

/// <summary>Bottom-anchored "a new version is available" banner, added as an always-present
/// last child on every page (same shape as ErrorPopup) so it shows regardless of which page the
/// user is on. Deliberately not wired through Shell — Shell has no built-in slot for content
/// that overlays every route, so a real page-by-page control is the actual supported mechanism
/// here, not a shortcut around it.</summary>
public partial class UpdateBanner : ContentView
{
    public UpdateBanner()
    {
        InitializeComponent();
        BindingContext = AppUpdateService.Instance;
    }

    private async void OnUpdateTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(AppConstants.Links.PlayStoreUrl));
        }
        catch
        {
            // No browser/Play Store handler available — nothing sensible to show the user for
            // this, same "never let a secondary action crash the app" treatment as everywhere
            // else fire-and-forget UI actions swallow exceptions in this codebase.
        }
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e) => AppUpdateService.Instance.Dismiss();
}
