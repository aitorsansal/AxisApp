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

        // TODO: register the Supabase-backed I*Repository implementations alongside this
        // once they exist (groups/payments/invites screens need them).
        builder.Services.AddSingleton<IAuthService, SupabaseAuthService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
