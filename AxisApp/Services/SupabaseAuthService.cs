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
public class SupabaseAuthService : IAuthService
{
    private readonly Client client;

    public bool IsAuthenticated => client.Auth.CurrentSession is not null;

    public Guid? CurrentAccountId =>
        Guid.TryParse(client.Auth.CurrentUser?.Id, out var id) ? id : null;

    public string? CurrentEmail => client.Auth.CurrentUser?.Email;

    public event EventHandler? AuthStateChanged;

    /// <summary>Takes the shared Client instance (registered once in MauiProgram) rather than
    /// constructing its own, so every Supabase*Repository talks to the same authenticated
    /// session instead of each service holding an independent, unauthenticated client.</summary>
    public SupabaseAuthService(Client client)
    {
        this.client = client;
        client.Auth.AddStateChangedListener((_, _) => AuthStateChanged?.Invoke(this, EventArgs.Empty));
    }

    public async Task<AuthResult> SignUpAsync(string email, string password)
    {
        try
        {
            await client.Auth.SignUp(email, password);
            // Re-running InitializeAsync() after a successful sign-up rewires the client's
            // internal state (including whatever propagates the session to Postgrest request
            // headers) to the freshly-established session — see PokeCards'
            // EnsureInitializedAsync-before-every-auth-call pattern. Without this, every
            // Postgrest request after sign-up/sign-in went out unauthenticated (verified via a
            // raw curl replay with a valid bearer token still getting 42501 RLS violations).
            await client.InitializeAsync();
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
            await client.InitializeAsync();
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task SignOutAsync() => await client.Auth.SignOut();

    /// <summary>client.Auth.Update(UserAttributes) — confirmed against a real reflection probe of
    /// the installed Supabase.Gotrue 6.3.0 package (Task&lt;User&gt; Update(UserAttributes), not
    /// docs, same caution this file already gives every other Gotrue call shape.</summary>
    public async Task<AuthResult> UpdateEmailAsync(string newEmail)
    {
        try
        {
            await client.Auth.Update(new Supabase.Gotrue.UserAttributes { Email = newEmail });
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> UpdatePasswordAsync(string newPassword)
    {
        try
        {
            await client.Auth.Update(new Supabase.Gotrue.UserAttributes { Password = newPassword });
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    /// <summary>Must run once before any other call — wires up the child clients and restores
    /// a persisted session if one exists. Called from App's constructor. LoadSession() has to run
    /// first: InitializeAsync() alone never calls the configured SessionHandler on its own (found
    /// 2026-08-25 — SaveSession fired correctly on sign-in, but nothing ever called LoadSession
    /// on the next launch, so every restart fell through to Login despite a valid persisted
    /// session sitting right there in SecureStorage). Same two-call order PokeCards uses.</summary>
    public async Task RestoreSessionAsync()
    {
        client.Auth.LoadSession();
        await client.InitializeAsync();
    }
}
