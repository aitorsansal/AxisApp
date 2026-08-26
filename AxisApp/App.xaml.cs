namespace AxisApp
{
    public partial class App : Application
    {
        // A cold-start app-link Intent fires before CreateWindow's Shell exists to navigate on, so
        // MainActivity.HandleIntent hands it here and SplashPage replays it once it's decided
        // where to land (Login or Groups) — same "defer until ready" shape as before, just moved
        // from window.Created to Splash now that Splash (not Login) is the first ShellContent.
        private static string? pendingDeepLink;

        public App()
        {
            InitializeComponent();
            Application.Current!.UserAppTheme = AppTheme.Dark;

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

        protected override Window CreateWindow(IActivationState? activationState) =>
            new(new AppShell());

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

        /// <summary>Called by SplashPage once it's decided where to land, to replay a deep link
        /// that arrived before the window (and Shell) existed.</summary>
        public static Task ReplayPendingDeepLinkAsync()
        {
            if (pendingDeepLink is not { } link) return Task.CompletedTask;
            pendingDeepLink = null;
            return NavigateToDeepLinkAsync(link);
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
