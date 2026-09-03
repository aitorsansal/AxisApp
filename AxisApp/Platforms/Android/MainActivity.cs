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
            if (intent is null) return;

            var uri = intent.Data?.ToString();
            if (!string.IsNullOrEmpty(uri))
            {
                App.HandleDeepLink(uri);
                return;
            }

            // A tapped push notification — see AxisFirebaseMessagingService, whose PendingIntent
            // targets this same Activity with these extras. No group_id means either a
            // notification with no group context, or (group_id is nullable on expenses/payments —
            // it's set null if the group was later dissolved) a group that no longer exists by the
            // time it's tapped; either way, falling through to the app's normal Login/Groups
            // landing is the right behavior, not an error.
            var groupId = intent.GetStringExtra("group_id");
            if (string.IsNullOrEmpty(groupId)) return;

            var groupName = intent.GetStringExtra("group_name") ?? "";
            App.HandleNotificationTap(
                $"{AppConstants.Routes.GroupDetails}?groupId={groupId}&groupName={Uri.EscapeDataString(groupName)}");
        }
    }
}
