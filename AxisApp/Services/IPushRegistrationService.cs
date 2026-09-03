namespace AxisApp.Services;

/// <summary>Platform-specific push-token registration, same one-implementation-per-platform shape
/// as IGoogleAuthService (Platforms/Android/PushRegistrationService.cs, Platforms/Windows/
/// PushRegistrationService.cs). Android's implementation is real (Firebase Cloud Messaging);
/// Windows' is a no-op for now — push notifications on this app's unpackaged Win32 build are a
/// separate, unresolved investigation (WNS/notification APIs have already bitten this project once
/// for the same "unpackaged" reason, see CLAUDE.md's crash-safety notes), deliberately not
/// attempted here.
///
/// Both methods swallow their own failures rather than throwing — a push-registration hiccup
/// (permission denied, no network, FCM unavailable) should never block sign-in, Groups loading, or
/// sign-out, so callers can invoke these fire-and-forget with no try/catch of their own.</summary>
public interface IPushRegistrationService
{
    /// <summary>Requests the POST_NOTIFICATIONS permission (Android 13+) if not already
    /// granted/denied, then registers this device's current push token via
    /// IDeviceTokensRepository. Safe to call repeatedly (e.g. every time Groups loads) — a
    /// permission already decided isn't re-prompted, and re-registering the same token is
    /// idempotent server-side.</summary>
    Task RegisterAsync();

    /// <summary>Removes this device's push token, both from IDeviceTokensRepository and (Android)
    /// from Firebase itself. Must be called before IAuthService.SignOutAsync, while the session
    /// used to authorize the delete is still valid — otherwise a device reused by a second account
    /// would keep receiving the first account's group notifications.</summary>
    Task UnregisterAsync();
}
