using AxisApp.Services;

namespace AxisApp
{
    public partial class App : Application
    {
        private readonly IAuthService authService;

        // A cold-start app-link Intent fires before CreateWindow's Shell exists to navigate on, so
        // MainActivity.HandleIntent hands it here and window.Created (below) replays it once ready —
        // same "defer until the window says it's ready" shape as the session-restore navigation.
        private static string? pendingDeepLink;

        public App(IAuthService authService)
        {
            InitializeComponent();
            Application.Current!.UserAppTheme = AppTheme.Dark;
            this.authService = authService;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                LogCrash(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogCrash(e.Exception);
                e.SetObserved();
            };
        }

        private static void LogCrash(Exception? ex)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "axisapp-crash.log");
                File.AppendAllText(path, $"{DateTime.Now:O}\n{ex}\n\n");
            }
            catch { /* best effort */ }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());

            // Shell always opens on its first ShellContent (Login) by default; if a session
            // restores successfully, skip straight past it once the window's up.
            window.Created += async (_, _) =>
            {
                await authService.RestoreSessionAsync();
                if (authService.IsAuthenticated)
                    await Shell.Current.GoToAsync(AppConstants.Routes.Groups);

                if (pendingDeepLink is { } link)
                {
                    pendingDeepLink = null;
                    await NavigateToDeepLinkAsync(link);
                }
            };

            return window;
        }

        /// <summary>Entry point for platform code (MainActivity's App Link intent-filter) handing
        /// over the raw incoming URI. Queues it if Shell isn't up yet (cold start).</summary>
        public static void HandleDeepLink(string uriString)
        {
            if (Shell.Current is null)
            {
                pendingDeepLink = uriString;
                return;
            }

            _ = NavigateToDeepLinkAsync(uriString);
        }

        private static Task NavigateToDeepLinkAsync(string uriString)
        {
            var code = AppConstants.Links.TryExtractCode(uriString);
            return code is null
                ? Task.CompletedTask
                : Shell.Current.GoToAsync($"{AppConstants.Routes.JoinGroup}?code={Uri.EscapeDataString(code)}");
        }
    }
}
