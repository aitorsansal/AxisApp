using System.Globalization;
using System.Net.Http.Headers;
using Supabase;
using Supabase.Gotrue.Exceptions;

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
    private readonly IGoogleAuthService googleAuthService;

    public bool IsAuthenticated => client.Auth.CurrentSession is not null;

    public Guid? CurrentAccountId =>
        Guid.TryParse(client.Auth.CurrentUser?.Id, out var id) ? id : null;

    public string? CurrentEmail => client.Auth.CurrentUser?.Email;

    /// <summary>UserMetadata is a Dictionary&lt;string, object&gt; (confirmed via reflection
    /// against the installed Supabase.Gotrue 6.3.0 package, same caution as every other Gotrue
    /// call in this file) — Google's claims land here as "avatar_url" and "picture" (both seen
    /// populated with the same value in a real token from this project), never a plain string
    /// property, so this has to look the value up rather than reading a typed field.</summary>
    public string? ProviderAvatarUrl
    {
        get
        {
            var metadata = client.Auth.CurrentUser?.UserMetadata;
            if (metadata is null) return null;

            if (metadata.TryGetValue("avatar_url", out var avatarUrl) && avatarUrl is not null)
                return avatarUrl.ToString();
            if (metadata.TryGetValue("picture", out var picture) && picture is not null)
                return picture.ToString();

            return null;
        }
    }

    public event EventHandler? AuthStateChanged;

    /// <summary>Takes the shared Client instance (registered once in MauiProgram) rather than
    /// constructing its own, so every Supabase*Repository talks to the same authenticated
    /// session instead of each service holding an independent, unauthenticated client.</summary>
    public SupabaseAuthService(Client client, IGoogleAuthService googleAuthService)
    {
        this.client = client;
        this.googleAuthService = googleAuthService;
        client.Auth.AddStateChangedListener((_, _) => AuthStateChanged?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>With "Confirm email" on in Supabase Auth, SignUp creates the account but doesn't
    /// sign it in — the package's own doc says CurrentSession stays unchanged in that case, which
    /// is what's checked here rather than the returned Session's shape. The confirmation link
    /// lands on AppConstants.Links.EmailConfirmedUrl (web/confirm/index.html), a plain browser
    /// page for the same reason PasswordResetUrl is one: no deep-link support on Windows.</summary>
    public async Task<AuthResult> SignUpAsync(string email, string password, string? displayName = null, DateTime? birthDate = null)
    {
        try
        {
            var metadata = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(displayName))
                metadata["display_name"] = displayName.Trim();
            if (birthDate is { } date)
                metadata["birth_date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            await client.Auth.SignUp(email, password, new Supabase.Gotrue.SignUpOptions
            {
                RedirectTo = AppConstants.Links.EmailConfirmedUrl,
                Data = metadata
            });

            if (client.Auth.CurrentSession is null)
                return new AuthResult(true, NeedsEmailConfirmation: true);

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
        catch (GotrueException ex) when (ex.Reason == FailureHint.Reason.UserEmailNotConfirmed)
        {
            return new AuthResult(false, ex.Message, NeedsEmailConfirmation: true);
        }
        // Which Reason the installed Gotrue 6.3.0 maps a wrong password to isn't documented (its
        // DetectReason works off status code + message text), so the raw error_code in the
        // response body is checked too.
        catch (GotrueException ex) when (ex.Reason is FailureHint.Reason.UserBadLogin
                                             or FailureHint.Reason.UserBadPassword
                                             or FailureHint.Reason.UserBadMultiple
                                         || ex.Content?.Contains("invalid_credentials") == true)
        {
            return new AuthResult(false, ex.Message, InvalidCredentials: true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    public async Task<AuthResult> SignInWithGoogleAsync()
    {
        var result = await googleAuthService.SignInAsync(client);
        if (result.Success)
            await client.InitializeAsync();

        return result;
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

    /// <summary>ResetPasswordForEmailOptions(email) — confirmed against a reflection probe of the
    /// installed Supabase.Gotrue 6.3.0 package, same caution as every other Gotrue call in this
    /// file. Deliberately leaves FlowType at its default (Implicit, confirmed by the same probe)
    /// rather than PKCE: PKCE's code_verifier is generated and stored on *this* client, but the
    /// link gets clicked in a browser — a different client entirely — which would never have that
    /// verifier. Implicit's token travels whole in the URL, so it works across that client
    /// boundary.</summary>
    public async Task<AuthResult> ForgotPasswordAsync(string email)
    {
        try
        {
            await client.Auth.ResetPasswordForEmail(new Supabase.Gotrue.ResetPasswordForEmailOptions(email)
            {
                RedirectTo = AppConstants.Links.PasswordResetUrl
            });
            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    private readonly object restoreSessionLock = new();
    private Task? restoreSessionTask;

    /// <summary>Must run once before any other call — wires up the child clients and restores
    /// a persisted session if one exists. Called from App's constructor. LoadSession() has to run
    /// first: InitializeAsync() alone never calls the configured SessionHandler on its own (found
    /// 2026-08-25 — SaveSession fired correctly on sign-in, but nothing ever called LoadSession
    /// on the next launch, so every restart fell through to Login despite a valid persisted
    /// session sitting right there in SecureStorage). Same two-call order PokeCards uses.
    ///
    /// Genuinely no-ops once a session is already live in memory — found 2026-09-15 via the home
    /// screen widgets (WidgetDataAccess calls this on every 30-minute widget tick, same-process,
    /// same Client singleton as the app). InitializeAsync() calls Auth.RetrieveSessionAsync(),
    /// which unconditionally calls RefreshToken() whenever a refresh token exists, with no check
    /// on whether the access token is actually near expiry. The SDK already runs its own
    /// independent refresh timer (Gotrue's TokenRefresh, ~4/5 through the access token's
    /// lifetime). Two refresh calls landing close together on the same client race on the same
    /// refresh token; Supabase rotates it on use, so the losing call gets InvalidRefreshToken back
    /// and that response destroys the *entire* session (Gotrue's RefreshToken() special-cases
    /// InvalidRefreshToken into DestroySession()+SignedOut) — even though the winning call had
    /// just succeeded. That's a real, persisted logout, and WidgetDataAccess's own try/catch
    /// swallows it, so it surfaced only as "why am I logged out" with no error anywhere. Skipping
    /// this entirely once CurrentSession is already set removes the second, racing refresh path;
    /// the SDK's own timer is enough to keep an already-live session current.
    ///
    /// The `CurrentSession is not null` guard alone still has a gap, hit for real 2026-09-16 on a
    /// physical device with both home-screen widgets (Balances + Events) placed at once: on a cold
    /// boot, Android fires OnUpdate for every placed widget instance close together, all in the
    /// same process, all before any of them has restored a session — so both see CurrentSession as
    /// null and both start their own LoadSession()+InitializeAsync(), racing each other on the same
    /// refresh token exactly like the 30-minute-tick case above, just triggered by two widgets at
    /// once instead of two ticks. Caching the in-flight Task and handing every concurrent caller
    /// the same one (instead of each starting its own restore) closes that gap: only the first
    /// caller actually calls LoadSession()/InitializeAsync(), everyone else just awaits its
    /// result.</summary>
    public Task RestoreSessionAsync()
    {
        if (client.Auth.CurrentSession is not null) return Task.CompletedTask;

        lock (restoreSessionLock)
        {
            restoreSessionTask ??= RestoreSessionCoreAsync();
            return restoreSessionTask;
        }
    }

    private async Task RestoreSessionCoreAsync()
    {
        try
        {
            client.Auth.LoadSession();
            await client.InitializeAsync();
        }
        finally
        {
            lock (restoreSessionLock)
            {
                restoreSessionTask = null;
            }
        }
    }

    /// <summary>First direct Edge Function call from the app (every other Edge Function so far —
    /// send-push, cleanup-receipts — is only ever invoked server-side, from a SQL trigger/cron via
    /// pg_net). Session.AccessToken is the standard Gotrue property name but, like every other
    /// Gotrue call shape in this file, hasn't been independently reflection-probed against the
    /// installed 6.3.0 package — flag it the same way if this doesn't compile.</summary>
    private static readonly HttpClient httpClient = new();

    public async Task<AuthResult> DeleteAccountAsync()
    {
        try
        {
            var accessToken = client.Auth.CurrentSession?.AccessToken;
            if (accessToken is null) return new AuthResult(false, "Not signed in");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/functions/v1/delete-account");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("apikey", SupabaseConfig.PublishableKey);

            using var response = await httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new AuthResult(false, ExtractError(body) ?? "Failed to delete account");

            // The account is already gone server-side at this point (the Edge Function's success
            // response means auth.admin.deleteUser already completed) — SignOut()'s own remote
            // call can legitimately fail here with the exact same "sub claim in JWT does not
            // exist" rejection, since it's trying to revoke a session for a user that no longer
            // exists. That must not turn an already-successful deletion into a reported failure —
            // confirmed live: a real deletion succeeded, then this exact SignOut() call threw and
            // its exception was caught by the outer catch below, reporting AuthResult(false, ...)
            // and leaving the caller stuck on ProfilePage instead of navigating to Login. Any
            // lingering local session is harmless — SplashPage.OnAppearing's own try/catch around
            // RestoreSessionAsync already falls back to Login on any failure there.
            try
            {
                await client.Auth.SignOut();
            }
            catch
            {
            }

            return new AuthResult(true);
        }
        catch (Exception ex)
        {
            return new AuthResult(false, ex.Message);
        }
    }

    private static string? ExtractError(string json)
    {
        try { return Newtonsoft.Json.Linq.JObject.Parse(json)["error"]?.ToString(); }
        catch { return null; }
    }
}
