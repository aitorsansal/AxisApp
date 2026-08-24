using Supabase;

namespace AxisApp.Services;

/// <summary>
/// Real Supabase-backed IAuthService, replacing NotConfiguredAuthService now that a project
/// exists. NOTE: the exact Gotrue API surface here (SignIn/SignUp/SignOut/CurrentSession/
/// CurrentUser) is written from SDK docs, not verified against a local build — the Postgrest
/// namespace in Models/ was already wrong once this way. If this doesn't compile, the fix is
/// almost certainly just renaming a method/property to whatever IntelliSense actually offers
/// on `client.Auth` — report the exact error back the same way you did for the Models imports.
/// </summary>
public class SupabaseAuthService : IAuthService, IDisposable
{
    private readonly Client client;

    public bool IsAuthenticated => client.Auth.CurrentSession is not null;

    public Guid? CurrentAccountId =>
        Guid.TryParse(client.Auth.CurrentUser?.Id, out var id) ? id : null;

    public string? CurrentEmail => client.Auth.CurrentUser?.Email;

    public event EventHandler? AuthStateChanged;

    public SupabaseAuthService()
    {
        var options = new SupabaseOptions
        {
            AutoRefreshToken = true,
            AutoConnectRealtime = false
        };
        client = new Client(SupabaseConfig.Url, SupabaseConfig.PublishableKey, options);
        client.Auth.AddStateChangedListener((_, _) => AuthStateChanged?.Invoke(this, EventArgs.Empty));
    }

    public async Task<AuthResult> SignUpAsync(string email, string password)
    {
        try
        {
            await client.Auth.SignUp(email, password);
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        try
        {
            await client.Auth.SignIn(email, password);
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task SignOutAsync() => await client.Auth.SignOut();

    /// <summary>Must run once before any other call — wires up the child clients and restores
    /// a persisted session if one exists. Called from App's constructor.</summary>
    public async Task RestoreSessionAsync() => await client.InitializeAsync();

    public void Dispose() => client.Dispose();
}
