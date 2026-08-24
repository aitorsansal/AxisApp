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
