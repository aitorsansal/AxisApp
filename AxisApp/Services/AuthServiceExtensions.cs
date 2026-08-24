namespace AxisApp.Services;

public static class AuthServiceExtensions
{
    /// <summary>The current account id, or throws if nothing repository-level should ever call
    /// this without being signed in already (every I*Repository call happens post-login).</summary>
    public static Guid RequireAccountId(this IAuthService authService) =>
        authService.CurrentAccountId ?? throw new InvalidOperationException("No signed-in account.");
}
