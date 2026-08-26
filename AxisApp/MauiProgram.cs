using AxisApp.Pages;
using AxisApp.Services;
using AxisApp.ViewModels;
using CommunityToolkit.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AxisApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("lucide.ttf", "Lucide");
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

        builder.Services.AddSingleton<IAuthService, SupabaseAuthService>();
        builder.Services.AddSingleton<IMembersRepository, SupabaseMembersRepository>();
        builder.Services.AddSingleton<IGroupsRepository, SupabaseGroupsRepository>();
        builder.Services.AddSingleton<IPaymentsRepository, SupabasePaymentsRepository>();
        builder.Services.AddSingleton<IExpensesRepository, SupabaseExpensesRepository>();
        builder.Services.AddSingleton<IBalancesRepository, SupabaseBalancesRepository>();
        builder.Services.AddSingleton<ICategoriesRepository, SupabaseCategoriesRepository>();
        builder.Services.AddSingleton<IRecurringPaymentsRepository, SupabaseRecurringPaymentsRepository>();
        builder.Services.AddSingleton<IInvitesRepository, SupabaseInvitesRepository>();
        builder.Services.AddSingleton<IDeviceTokensRepository, SupabaseDeviceTokensRepository>();

        builder.Services.AddTransient<SplashPage>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

        builder.Services.AddTransient<GroupsViewModel>();
        builder.Services.AddTransient<GroupsPage>();
        builder.Services.AddTransient<GroupDetailViewModel>();
        builder.Services.AddTransient<GroupDetailPage>();
        builder.Services.AddTransient<AddExpenseViewModel>();
        builder.Services.AddTransient<AddExpensePage>();
        builder.Services.AddTransient<JoinGroupViewModel>();
        builder.Services.AddTransient<JoinGroupPage>();
        builder.Services.AddTransient<NewGroupViewModel>();
        builder.Services.AddTransient<NewGroupPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
