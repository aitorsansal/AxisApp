using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace AxisApp
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    // App Link for AppConstants.Links.InviteHost — AutoVerify makes Android confirm ownership via
    // https://axisapp.aitorsansal.com/.well-known/assetlinks.json before it'll open the link
    // directly in-app instead of a browser; see AppConstants.Links' remarks for what else that needs.
    [IntentFilter(
        new[] { Intent.ActionView },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataScheme = "https",
        DataHost = "axisapp.aitorsansal.com",
        DataPathPrefix = "/invite",
        AutoVerify = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            HandleIntent(Intent);
        }

        // LaunchMode.SingleTop means a link tapped while the app is already running arrives here
        // instead of a fresh OnCreate.
        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            HandleIntent(intent);
        }

        private static void HandleIntent(Intent? intent)
        {
            var uri = intent?.Data?.ToString();
            if (!string.IsNullOrEmpty(uri))
                App.HandleDeepLink(uri);
        }
    }
}
