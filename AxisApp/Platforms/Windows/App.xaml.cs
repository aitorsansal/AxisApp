using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace AxisApp.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();

            UnhandledException += (_, e) =>
            {
                try
                {
                    var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axisapp-crash.log");
                    System.IO.File.AppendAllText(path, $"{DateTime.Now:O}\n{e.Message}\n{e.Exception}\n\n");
                }
                catch { /* best effort */ }
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
