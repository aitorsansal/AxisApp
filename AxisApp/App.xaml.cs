namespace AxisApp
{
    public partial class App : Application
    {
        // A cold-start app-link Intent (or a tapped push notification) fires before CreateWindow's
        // Shell exists to navigate on, so MainActivity.HandleIntent hands it here and SplashPage
        // replays it once it's decided where to land (Login or Groups) — same "defer until ready"
        // shape as before, just moved from window.Created to Splash now that Splash (not Login) is
        // the first ShellContent. Holds an already-resolved route string rather than a raw URI —
        // both HandleDeepLink (invite links) and HandleNotificationTap (push taps, see
        // AxisFirebaseMessagingService) resolve to a route before queuing, so this one field/queue
        // serves both instead of duplicating the "queue or navigate now" logic per source.
        private static string? pendingRoute;

        // Real bug, found 2026-09-03: Shell.Current being non-null is NOT a reliable "is the app
        // ready to navigate" signal — AxisFirebaseMessagingService can start this app's process
        // with no Activity ever appearing (Android starts the process purely to deliver a push),
        // and MAUI's Application/Window/Shell object graph gets constructed as soon as the process
        // starts (CreateWindow() runs unconditionally), before SplashPage — the thing that actually
        // restores the session — has ever rendered. A notification tapped shortly after in that
        // state saw Shell.Current already non-null and navigated immediately, straight into an
        // RLS-gated query with no session ever restored ("Group not found" — genuinely
        // unauthenticated, not a real permission gap). Tracked explicitly instead, set true only
        // once SplashPage.OnAppearing has actually finished its RestoreSessionAsync + landed on
        // Login/Groups.
        private static bool isReadyToNavigate;

        /// <summary>Called by SplashPage once RestoreSessionAsync has completed and it's landed on
        /// Login/Groups — the actual "safe to navigate" signal, not Shell.Current's nullness.</summary>
        public static void MarkReadyToNavigate() => isReadyToNavigate = true;

        public App()
        {
            // Must run before InitializeComponent/any page is built, so the very first frame
            // (Splash) already renders in the right language instead of flashing device-default
            // text first. See LocalizationResourceManager.Bootstrap's remarks.
            Localization.LocalizationResourceManager.Instance.Bootstrap();

            InitializeComponent();
            Application.Current!.UserAppTheme = AppTheme.Dark;

            // Must run after InitializeComponent (Resources.MergedDictionaries don't exist until
            // App.xaml is parsed) — unlike Localization.Bootstrap above, which must run before it.
            Services.ThemeService.Instance.Bootstrap();

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
        /// over the raw incoming URI. Queues it if SplashPage hasn't finished restoring the
        /// session yet (see isReadyToNavigate's remarks).</summary>
        public static void HandleDeepLink(string uriString)
        {
            var code = AppConstants.Links.TryExtractCode(uriString);
            if (code is not null)
                QueueOrNavigate($"{AppConstants.Routes.JoinGroup}?code={Uri.EscapeDataString(code)}");
        }

        /// <summary>Entry point for a tapped push notification (MainActivity, reading the
        /// group_id/group_name extras AxisFirebaseMessagingService's PendingIntent carries).
        /// Takes an already-built route rather than raw data, since the caller already knows
        /// exactly which route it wants.</summary>
        public static void HandleNotificationTap(string route) => QueueOrNavigate(route);

        private static void QueueOrNavigate(string route)
        {
            if (!isReadyToNavigate)
            {
                pendingRoute = route;
                return;
            }

            _ = Shell.Current.GoToAsync(route);
        }

        /// <summary>Called by SplashPage once it's decided where to land, to replay a deep link or
        /// notification tap that arrived before the window (and Shell) existed.</summary>
        public static Task ReplayPendingDeepLinkAsync()
        {
            if (pendingRoute is not { } route) return Task.CompletedTask;
            pendingRoute = null;
            return Shell.Current.GoToAsync(route);
        }
    }
}
