namespace AxisApp.Services;

public record AuthResult(bool Success, string? ErrorMessage = null);

/// <summary>
/// Wraps whatever auth provider backs the app (Supabase Auth today). Nothing outside this
/// interface's implementation should reference the Supabase SDK directly, so swapping backends
/// later — self-hosted Supabase, or a custom API — means writing a new implementation of this
/// interface, not touching ViewModels.
/// </summary>
public interface IAuthService
{
    bool IsAuthenticated { get; }
    Guid? CurrentAccountId { get; }
    string? CurrentEmail { get; }

    /// <summary>The signed-in account's profile photo URL, if the provider handed one over (e.g.
    /// Google's "picture"/"avatar_url" claim) — null for a plain email/password account. Used to
    /// backfill a claimed member's own avatar on first Profile visit; see ProfileViewModel.</summary>
    string? ProviderAvatarUrl { get; }

    /// <summary>Raised after sign-in, sign-up, or sign-out changes the current session.</summary>
    event EventHandler? AuthStateChanged;

    Task<AuthResult> SignUpAsync(string email, string password);
    Task<AuthResult> SignInAsync(string email, string password);

    /// <summary>Delegates to the platform-specific IGoogleAuthService — see its remarks for why
    /// Android and Windows reach a session by genuinely different means. A null (not empty)
    /// ErrorMessage on a failed result means the user cancelled; callers should stay silent
    /// rather than show a generic failure message in that case.</summary>
    Task<AuthResult> SignInWithGoogleAsync();

    Task SignOutAsync();

    /// <summary>Changes the signed-in account's email. Supabase sends a confirmation link to the
    /// new address and the change only takes effect once that's clicked — CurrentEmail keeps
    /// showing the old address until then, this isn't a bug in the caller.</summary>
    Task<AuthResult> UpdateEmailAsync(string newEmail);

    Task<AuthResult> UpdatePasswordAsync(string newPassword);

    /// <summary>Sends a password-recovery email via Supabase. The link lands on
    /// AppConstants.Links.PasswordResetUrl (web/reset/index.html), not this app — Windows has no
    /// deep-link support, so the reset form has to work standalone in whatever browser opens the
    /// link, independent of this app's own session/state.</summary>
    Task<AuthResult> ForgotPasswordAsync(string email);

    /// <summary>Restores a previously persisted session on app start, if one exists.</summary>
    Task RestoreSessionAsync();

    /// <summary>Permanently deletes the signed-in account via the delete-account Edge Function —
    /// unlinks the account's member row back to a phantom (ledger history stays intact), deletes
    /// any group the account owns with no other members, and removes the auth user itself. Fails
    /// with the server's raise-exception message (surfaced verbatim as ErrorMessage) if the
    /// account still owns a group that has other members — see schema.sql's delete_account().</summary>
    Task<AuthResult> DeleteAccountAsync();
}
