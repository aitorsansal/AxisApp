namespace AxisApp;

public static class AppConstants
{
    public static class Preferences
    {
        public const string SupabaseSession = "supabase_session";

        /// <summary>Per-device, per-account, per-group choice of Group Detail's balance display
        /// mode (simplified settle-up vs. real pairwise) — see GroupDetailViewModel. Deliberately
        /// local only, never synced: it's a personal viewing preference, not group state, so
        /// nothing requires every member of a group to see it the same way.</summary>
        public const string BalanceDisplayModePrefix = "balance_display_pairwise_";
    }

    public static class Routes
    {
        public const string Login = "//Login";
        public const string Groups = "//Groups";
        public const string GroupDetails = "GroupDetails";
        public const string JoinGroup = "JoinGroup";
        public const string AddExpense = "AddExpense";
        public const string NewGroup = "NewGroup";
    }

    /// <summary>Web-facing invite links (Android App Links today; iOS Universal Links whenever that
    /// target is back in the active TargetFrameworks). InviteHost is a Cloudflare-hosted subdomain
    /// (axisapp.aitorsansal.com) separate from the personal site at the apex domain — it needs its
    /// own /.well-known/assetlinks.json there plus the matching Android intent-filter in
    /// MainActivity.cs before real devices will treat the link as app-openable instead of just a
    /// web page.</summary>
    public static class Links
    {
        public const string InviteHost = "axisapp.aitorsansal.com";

        public static string BuildInviteUrl(string code) =>
            $"https://{InviteHost}/invite?code={Uri.EscapeDataString(code)}";

        /// <summary>Pulls the "code" query param out of an invite link — either one built by
        /// BuildInviteUrl or the raw URI handed over by the platform's app-link Intent. Returns
        /// null for anything that isn't shaped like one (e.g. a bare code with no URL at all),
        /// so callers can fall back to treating the input as a plain code.</summary>
        public static string? TryExtractCode(string uriString)
        {
            if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri)) return null;

            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] == "code")
                    return Uri.UnescapeDataString(parts[1]);
            }

            return null;
        }
    }
}
