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

        // Force this onto a fresh dispatcher tick before touching Shell.Current at all. Without
        // this, when RestoreSessionAsync completes without ever truly awaiting (e.g. no persisted
        // session, so nothing needs restoring), this whole async void method can run synchronously
        // to completion within the same call stack as Shell's own initial "navigate to //Splash" —
        // and GoToAsync below then reenters Shell navigation while that outer navigation hasn't
        // finished, throwing "Pending Navigations still processing" and fail-fasting the process
        // (0xc000027b) before anything is ever shown. Confirmed via axisapp-crash.log: identical
        // stack every time, rooted entirely inside MauiWinUIApplication.OnLaunched with no frame of
        // this class in it — the crash happens on the very first launch, not from a slow reaction.
        await Task.Yield();

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

        // Must come after the GoToAsync above, not before — this is the actual "safe to navigate"
        // signal App.QueueOrNavigate waits on (see its remarks: Shell.Current's nullness alone
        // isn't reliable, since AxisFirebaseMessagingService can construct the Shell object graph
        // by starting this app's process with no Activity ever appearing, well before this method
        // ever runs).
        App.MarkReadyToNavigate();
        await App.ReplayPendingDeepLinkAsync();
    }
}
