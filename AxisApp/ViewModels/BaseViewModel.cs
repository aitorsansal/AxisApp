using CommunityToolkit.Mvvm.ComponentModel;

namespace AxisApp.ViewModels;

/// <summary>
/// Every [RelayCommand] body should run through RunSafeAsync instead of executing bare. Without
/// it, an unhandled exception from an async command (CommunityToolkit.Mvvm's AsyncRelayCommand)
/// posts back to the WinUI dispatcher outside any try/catch and fail-fasts the whole process
/// (0xc000027b) instead of just failing that one action — confirmed repeatedly in this app: a
/// transient Supabase "JWT issued at future" clock-skew rejection, a Toast/AppNotificationManager
/// COMException, and others all took the entire app down this way before this existed.
///
/// Surfaces the message via the same ErrorMessage-bound red Label pattern JoinGroupViewModel and
/// NewGroupViewModel already used individually — generalized here rather than introducing a
/// separate popup mechanism, since CommunityToolkit.Maui 13.0.0's Popup API (Close()/
/// ShowPopupAsync) turned out to have moved/renamed from the shape PokeCards' 9.1.1-based
/// ErrorPresenter used, and this app already had a simpler, proven, zero-dependency pattern for
/// the exact same job.
/// </summary>
public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty] private string errorMessage = "";

    protected async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            ErrorMessage = "";
            await action();
        }
        catch (Exception ex) when (IsClockSkewRejection(ex))
        {
            // Transient Supabase clock-skew rejection (PGRST303 "JWT issued at future") — a
            // brief drift between the device clock and the server at the exact moment a token
            // was issued, not something the app or the user can fix. PostgREST rejects this at
            // the auth-check step, before the query itself ever runs, so a bare retry is safe —
            // nothing partially executed. Wait a moment for the skew to pass, then retry once;
            // a second consecutive failure is no longer "just a blip", so that one is surfaced
            // normally instead of retried or swallowed again.
            try
            {
                await Task.Delay(750);
                await action();
            }
            catch (Exception retryEx)
            {
                ErrorMessage = retryEx.Message;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static bool IsClockSkewRejection(Exception ex) =>
        ex.Message.Contains("JWT issued at future") || ex.Message.Contains("PGRST303");
}
