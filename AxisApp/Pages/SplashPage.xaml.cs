using AxisApp.Services;

namespace AxisApp.Pages;

/// <summary>First ShellContent the app opens on. Restores the session and redirects to
/// Login/Groups before the user sees anything else — avoids the old behavior where Shell always
/// opened on Login first, then bounced straight to Groups a moment later on an already-signed-in
/// launch.</summary>
public partial class SplashPage : ContentPage
{
    private readonly IAuthService authService;
    private bool started;

    public SplashPage(IAuthService authService)
    {
        InitializeComponent();
        this.authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (started) return;
        started = true;

        var destination = AppConstants.Routes.Login;
        try
        {
            await authService.RestoreSessionAsync();
            if (authService.IsAuthenticated)
                destination = AppConstants.Routes.Groups;
        }
        catch
        {
            // Falls back to Login on any restore failure (e.g. a transient Supabase error) —
            // an unhandled exception here would otherwise fail-fast the whole WinUI process
            // instead of just landing on Login, same crash-safety reasoning as
            // BaseViewModel.RunSafeAsync.
        }

        await Shell.Current.GoToAsync(destination);
        await App.ReplayPendingDeepLinkAsync();
    }
}
