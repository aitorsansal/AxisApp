using AxisApp.Pages;
using AxisApp.Services;
using AxisApp.ViewModels;
using CommunityToolkit.Maui;
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
                // Uses platform default fonts until custom fonts are added under Resources/Fonts.
            });

        // Single shared Supabase.Client — SupabaseAuthService and every Supabase*Repository
        // below take this same instance instead of each opening its own, so repository calls
        // ride on the session that SignIn/SignUp actually established.
        builder.Services.AddSingleton(_ => new Supabase.Client(
            SupabaseConfig.Url,
            SupabaseConfig.PublishableKey,
            new Supabase.SupabaseOptions { AutoRefreshToken = true, AutoConnectRealtime = false }));

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

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
