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

    /// <summary>Raised after sign-in, sign-up, or sign-out changes the current session.</summary>
    event EventHandler? AuthStateChanged;

    Task<AuthResult> SignUpAsync(string email, string password);
    Task<AuthResult> SignInAsync(string email, string password);
    Task SignOutAsync();

    /// <summary>Changes the signed-in account's email. Supabase sends a confirmation link to the
    /// new address and the change only takes effect once that's clicked — CurrentEmail keeps
    /// showing the old address until then, this isn't a bug in the caller.</summary>
    Task<AuthResult> UpdateEmailAsync(string newEmail);

    Task<AuthResult> UpdatePasswordAsync(string newPassword);

    /// <summary>Restores a previously persisted session on app start, if one exists.</summary>
    Task RestoreSessionAsync();
}
