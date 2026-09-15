using AxisApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace AxisApp.Widgets;

/// <summary>Shared entry point every widget data provider and the config activity go through to
/// reach the app's real repositories. The widget's BroadcastReceiver/Service/Activity run in the
/// same process as the MAUI app (Android boots MainApplication.OnCreate — and with it
/// MauiProgram's whole DI container — before delivering any component's first callback, even one
/// that never shows an Activity), so IPlatformApplication.Current.Services is already populated.
///
/// What's NOT already done for us: SupabaseAuthService.RestoreSessionAsync() only runs from
/// SplashPage.OnAppearing today — a widget-triggered cold start never reaches SplashPage, so the
/// Supabase.Client's session would otherwise be un-restored and every repository call would run
/// unauthenticated. EnsureSessionAsync below is idempotent (LoadSession()+InitializeAsync() are
/// safe to call again on an already-restored session) and must run before any repository call
/// on this path.</summary>
internal static class WidgetDataAccess
{
    public static async Task<IServiceProvider?> GetReadyServicesAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null) return null;

        try
        {
            var authService = services.GetRequiredService<IAuthService>();
            await authService.RestoreSessionAsync();
        }
        catch
        {
            // Best-effort — a repository call right after this will fail its own way (e.g. RLS
            // rejection) if there's genuinely no session, which the callers already render as an
            // empty/placeholder widget state rather than crashing.
        }

        return services;
    }
}
