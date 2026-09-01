namespace AxisApp.Services;

/// <summary>Platform-specific Google sign-in, registered per-platform in MauiProgram
/// (Platforms/Android/GoogleAuthService.cs, Platforms/Windows/GoogleAuthService.cs — the file
/// under each Platforms/&lt;X&gt; folder is only compiled for that TargetFramework, so there's no
/// runtime branching needed here, just one implementation per target). The two platforms reach a
/// signed-in Supabase session by genuinely different means (Android: a native ID token via
/// Credential Manager, fed into SignInWithIdToken; Windows: a full browser-based OAuth/PKCE round
/// trip through Supabase's own authorize endpoint, since Windows has no Credential Manager
/// equivalent) — SignInAsync hides that difference and hands back a session-established
/// AuthResult either way, so SupabaseAuthService.SignInWithGoogleAsync doesn't need to know which
/// platform it's running on.
///
/// AuthResult(false, null) (a null, not empty, ErrorMessage) means the user cancelled — e.g.
/// dismissed the Android account picker — and callers should treat that as a silent no-op, not
/// show a "sign-in failed" message. Every genuine failure elsewhere always carries a real
/// ex.Message, so null is safe to use as that one specific signal.</summary>
public interface IGoogleAuthService
{
    Task<AuthResult> SignInAsync(Supabase.Client client);
}
