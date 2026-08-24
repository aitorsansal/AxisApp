using AxisApp.Services;

namespace AxisApp
{
    public partial class App : Application
    {
        public App(IAuthService authService)
        {
            InitializeComponent();
            Application.Current!.UserAppTheme = AppTheme.Dark;
            _ = authService.RestoreSessionAsync();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
