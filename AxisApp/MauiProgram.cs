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

        // TODO: once a Supabase project exists (see /supabase/README.md), replace this with a
        // real SupabaseAuthService wired to the project URL + anon key, and register the
        // Supabase-backed I*Repository implementations alongside it.
        builder.Services.AddSingleton<IAuthService, NotConfiguredAuthService>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
