using AxisApp.Pages;
using AxisApp.Services;
using AxisApp.ViewModels;
using CommunityToolkit.Maui;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace AxisApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            // LiveCharts' CartesianChart renders via SkiaSharp — UseSkiaSharp() must run before
            // UseLiveCharts() (order matters, per LiveCharts2's install docs), or SkiaSharp's own
            // rendering-mode handler (LiveChartsCore.SkiaSharpView.Maui.Rendering.CPURenderMode)
            // never gets registered and the app crashes with HandlerNotFoundException the moment
            // any CartesianChart is instantiated — found live via logcat, not caught by the debug
            // build pass before this. Note: the installed SkiaSharp.Views.Maui.Controls 3.119.0
            // package's own bundled .xml doc names this method "UseSkiaSharpHandlers" — that's
            // stale/mismatched against the actual compiled DLL (confirmed via the DLL's raw string
            // heap), which only has "UseSkiaSharp". Don't trust that xml doc for this package.
            .UseSkiaSharp()
            .UseLiveCharts()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("lucide.ttf", "Lucide");
                // Required by Google's "Sign in with Google" branding guidelines for the button
                // text — see LoginPage's Google button.
                fonts.AddFont("GoogleSansMedium.ttf", "GoogleSansMedium");
            });

        // Persists the Gotrue session to SecureStorage across app launches — without this,
        // SupabaseOptions.SessionHandler is null and RestoreSessionAsync's InitializeAsync()
        // call (App.xaml.cs) has nothing to load, so every launch fell through to Login even
        // right after signing in. See SupabaseSessionPersistence's remarks.
        builder.Services.AddSingleton<Supabase.Gotrue.Interfaces.IGotrueSessionPersistence<Supabase.Gotrue.Session>, SupabaseSessionPersistence>();

        // Single shared Supabase.Client — SupabaseAuthService and every Supabase*Repository
        // below take this same instance instead of each opening its own, so repository calls
        // ride on the session that SignIn/SignUp actually established.
        builder.Services.AddSingleton(provider => new Supabase.Client(
            SupabaseConfig.Url,
            SupabaseConfig.PublishableKey,
            new Supabase.SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false,
                SessionHandler = provider.GetRequiredService<Supabase.Gotrue.Interfaces.IGotrueSessionPersistence<Supabase.Gotrue.Session>>()
            }));

        // Platform-specific implementation — Platforms/Android/GoogleAuthService.cs or
        // Platforms/Windows/GoogleAuthService.cs, whichever this TargetFramework actually
        // compiles; both share the AxisApp namespace/class name so this registration doesn't
        // need to branch on platform itself.
        builder.Services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
        builder.Services.AddSingleton<IAuthService, SupabaseAuthService>();
        builder.Services.AddSingleton<IMembersRepository, SupabaseMembersRepository>();
        builder.Services.AddSingleton<IGroupsRepository, SupabaseGroupsRepository>();
        builder.Services.AddSingleton<IExpensesRepository, SupabaseExpensesRepository>();
        builder.Services.AddSingleton<IBalancesRepository, SupabaseBalancesRepository>();
        builder.Services.AddSingleton<IRecurringExpensesRepository, SupabaseRecurringExpensesRepository>();
        builder.Services.AddSingleton<IEventsRepository, SupabaseEventsRepository>();
        builder.Services.AddSingleton<IInvitesRepository, SupabaseInvitesRepository>();
        builder.Services.AddSingleton<IAliasesRepository, SupabaseAliasesRepository>();
        builder.Services.AddSingleton<IAvatarsRepository, SupabaseAvatarsRepository>();
        builder.Services.AddSingleton<IReceiptsRepository, SupabaseReceiptsRepository>();
        builder.Services.AddSingleton<IDeviceTokensRepository, SupabaseDeviceTokensRepository>();
        builder.Services.AddSingleton<ICalendarSubscriptionsRepository, SupabaseCalendarSubscriptionsRepository>();

        // Same per-platform-file convention as IGoogleAuthService/IPushRegistrationService above —
        // Android's implementation broadcasts a widget refresh; Windows has no widget host, so its
        // WidgetRefreshService is a deliberate no-op.
        builder.Services.AddSingleton<IWidgetRefreshService, WidgetRefreshService>();

        // Same per-platform-file convention as IGoogleAuthService above — Android's implementation
        // is real (Firebase Cloud Messaging), Windows' is a deliberate no-op for now.
        builder.Services.AddSingleton<IPushRegistrationService, PushRegistrationService>();

        // Same per-platform-file convention again — marks a copied secret (the calendar feed URL) as
        // sensitive so it stays out of clipboard previews/history. See ISecretClipboardService.
        builder.Services.AddSingleton<ISecretClipboardService, SecretClipboardService>();

        builder.Services.AddTransient<SplashPage>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<RegisterPage>();

        builder.Services.AddTransient<GroupsViewModel>();
        builder.Services.AddTransient<GroupsPage>();
        builder.Services.AddTransient<GroupExpensesViewModel>();
        builder.Services.AddTransient<GroupEventsViewModel>();
        builder.Services.AddTransient<GroupStatsViewModel>();
        builder.Services.AddTransient<GroupDetailViewModel>();
        builder.Services.AddTransient<GroupDetailPage>();
        builder.Services.AddTransient<MembersViewModel>();
        builder.Services.AddTransient<MembersPage>();
        builder.Services.AddTransient<MemberProfileViewModel>();
        builder.Services.AddTransient<MemberProfilePage>();
        builder.Services.AddTransient<AddExpenseViewModel>();
        builder.Services.AddTransient<AddExpensePage>();
        builder.Services.AddTransient<RecurringExpensesViewModel>();
        builder.Services.AddTransient<RecurringExpensesPage>();
        builder.Services.AddTransient<AddEventViewModel>();
        builder.Services.AddTransient<AddEventPage>();
        builder.Services.AddTransient<EventDetailViewModel>();
        builder.Services.AddTransient<EventDetailPage>();
        builder.Services.AddTransient<JoinGroupViewModel>();
        builder.Services.AddTransient<JoinGroupPage>();
        builder.Services.AddTransient<InviteToGroupViewModel>();
        builder.Services.AddTransient<InviteToGroupPage>();
        builder.Services.AddTransient<NewGroupViewModel>();
        builder.Services.AddTransient<NewGroupPage>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<ProfilePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
