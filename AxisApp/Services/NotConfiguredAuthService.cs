namespace AxisApp.Services;

/// <summary>
/// Placeholder registered until a real Supabase-backed IAuthService is wired up (needs a
/// Supabase project URL + anon key — see /supabase/README.md). Lets the app boot and the
/// login screen render before that's done, instead of DI failing outright.
/// </summary>
public class NotConfiguredAuthService : IAuthService
{
    public bool IsAuthenticated => false;
    public Guid? CurrentAccountId => null;
    public string? CurrentEmail => null;

    public event EventHandler? AuthStateChanged { add { } remove { } }

    public Task<AuthResult> SignUpAsync(string email, string password) =>
        Task.FromResult(new AuthResult(false, "Supabase isn't configured yet — see /supabase/README.md."));

    public Task<AuthResult> SignInAsync(string email, string password) =>
        Task.FromResult(new AuthResult(false, "Supabase isn't configured yet — see /supabase/README.md."));

    public Task SignOutAsync() => Task.CompletedTask;

    public Task RestoreSessionAsync() => Task.CompletedTask;
}
