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
}
