using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace AxisApp.Services;

/// <summary>Checks web/version.json (deployed alongside every other static file under
/// AppConstants.Links.InviteHost) against the running app's own version, and drives the
/// update-available banner shown on every page. Plain INotifyPropertyChanged singleton, same
/// shape as ThemeService/LocalizationResourceManager — not DI-registered, since it holds
/// app-global UI state every page's XAML binds to directly via the shared instance, not a
/// per-ViewModel concern.</summary>
public sealed class AppUpdateService : INotifyPropertyChanged
{
    public static AppUpdateService Instance { get; } = new();

    private static readonly HttpClient httpClient = new();

    private AppUpdateService() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool isUpdateAvailable;
    private string? latestVersion;
    private bool isDismissed;

    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        private set
        {
            if (value == isUpdateAvailable) return;
            isUpdateAvailable = value;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(ShouldShowBanner));
        }
    }

    public string? LatestVersion
    {
        get => latestVersion;
        private set
        {
            if (value == latestVersion) return;
            latestVersion = value;
            OnPropertyChanged(nameof(LatestVersion));
        }
    }

    /// <summary>What every page's UpdateBanner actually binds IsVisible to — folds in "already
    /// dismissed today" so the banner control itself never needs its own dismissal logic beyond
    /// calling Dismiss().</summary>
    public bool ShouldShowBanner => IsUpdateAvailable && !isDismissed;

    /// <summary>Fire-and-forget from SplashPage after the auth-restore/navigation decision —
    /// never awaited by its caller, and every failure (no network, malformed JSON, unreachable
    /// host) is swallowed here so a version-check hiccup can never affect startup or show an
    /// error anywhere. Reads the dismissed-date preference fresh on every call rather than once
    /// at construction, so a dismissal made earlier in the same session (unlikely — this only
    /// runs once per launch today, but cheap to keep correct) is respected.</summary>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            var dismissedDate = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.UpdateBannerDismissedDate, "");
            isDismissed = dismissedDate == DateTime.UtcNow.ToString("yyyy-MM-dd");

            var json = await httpClient.GetStringAsync(AppConstants.Links.VersionCheckUrl);
            var latest = JObject.Parse(json)["latestVersion"]?.ToString();
            if (string.IsNullOrEmpty(latest)) return;

            var current = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
            if (!Version.TryParse(latest, out var latestParsed) || !Version.TryParse(current, out var currentParsed))
                return;

            LatestVersion = latest;
            IsUpdateAvailable = latestParsed > currentParsed;
        }
        catch
        {
            // No network, unreachable host, malformed JSON, etc. — never surface this to the
            // user, and never block/slow down startup over it.
        }
    }

    /// <summary>Called from UpdateBanner's close button — hides the banner until tomorrow, not
    /// permanently, so it comes back as a daily nudge for as long as an update is genuinely
    /// available (see AppConstants.Preferences.UpdateBannerDismissedDate).</summary>
    public void Dismiss()
    {
        Microsoft.Maui.Storage.Preferences.Default.Set(AppConstants.Preferences.UpdateBannerDismissedDate, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        isDismissed = true;
        OnPropertyChanged(nameof(ShouldShowBanner));
    }

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
