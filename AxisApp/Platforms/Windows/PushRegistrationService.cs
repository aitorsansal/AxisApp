using AxisApp.Services;

namespace AxisApp;

/// <summary>Deliberate no-op — see IPushRegistrationService's remarks. Windows push is a separate,
/// unresolved investigation (this app is an unpackaged Win32 build, and native Windows notification
/// APIs already required MSIX packaging once before on this exact codebase — see CLAUDE.md's
/// crash-safety notes on AppNotificationManager). Registered here only so MauiProgram's DI
/// registration doesn't need to branch per platform, same as GoogleAuthService.</summary>
public class PushRegistrationService : IPushRegistrationService
{
    public Task RegisterAsync() => Task.CompletedTask;

    public Task UnregisterAsync() => Task.CompletedTask;
}
