using AxisApp.Services;

namespace AxisApp
{
    public partial class App : Application
    {
        private readonly IAuthService authService;

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
            };

            return window;
        }
    }
}
