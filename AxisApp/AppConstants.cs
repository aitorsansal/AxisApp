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

        /// <summary>Per-device language override ("en"/"es"), empty string means "follow the
        /// device's OS locale" — see LocalizationResourceManager.</summary>
        public const string LanguageOverride = "language_override";

        /// <summary>Per-device accent color choice (an AccentPreset name) — see ThemeService.
        /// Local only, same "personal viewing preference, not group state" reasoning as
        /// BalanceDisplayModePrefix.</summary>
        public const string AccentPreset = "accent_preset";

        /// <summary>Per-device (not per-group — see MULTI_CURRENCY_PLAN.md's Milestone 5) toggle for
        /// whether an expense line item's group-currency-converted amount is shown as primary (true,
        /// the default) or its own original-currency amount is (false). Only affects individual
        /// expense rows (Group Detail's Recent Activity) — balance/Settle totals are always shown in
        /// the group's currency regardless, since a pairwise/group net can span several original
        /// currencies with no single coherent "native" amount to fall back to.</summary>
        public const string AmountDisplayConverted = "amount_display_converted";
    }

    /// <summary>Fixed, developer-maintained expense categories — not a database table. Each key
    /// is a stable, language-independent identifier stored in Expense.Category; the display label
    /// is resolved per-viewer via AppStrings.Get($"Category_{key}", ...), never stored as text.
    /// Storing the localized label itself would mean whichever language the expense's creator
    /// happened to be using becomes baked into the data for every other viewer, forever — the
    /// exact failure mode localizing a shared ledger has to avoid.</summary>
    public static class Categories
    {
        public static readonly IReadOnlyList<string> Keys =
            ["food", "transport", "rent", "utilities", "entertainment", "other"];
    }

    /// <summary>The fixed 30-currency list this app supports — tied 1:1 to the DB check
    /// constraints on groups.currency/expenses.currency/recurring_expenses.currency (see
    /// /MULTI_CURRENCY_PLAN.md), which in turn mirror what Frankfurter (frankfurter.dev, ECB
    /// reference rates) actually returns from its live /v1/currencies endpoint — never hand-roll a
    /// separate "curated" subset. Symbol is a plain display convenience (not stored anywhere),
    /// deliberately not localized per-language the way Categories' labels are — a currency code is
    /// already an international standard, and "USD ($)" reads the same regardless of UI
    /// language.</summary>
    public static class Currencies
    {
        public static readonly IReadOnlyList<(string Code, string Symbol)> All =
        [
            ("AUD", "A$"), ("BRL", "R$"), ("CAD", "C$"), ("CHF", "CHF"), ("CNY", "¥"),
            ("CZK", "Kč"), ("DKK", "kr"), ("EUR", "€"), ("GBP", "£"), ("HKD", "HK$"),
            ("HUF", "Ft"), ("IDR", "Rp"), ("ILS", "₪"), ("INR", "₹"), ("ISK", "kr"),
            ("JPY", "¥"), ("KRW", "₩"), ("MXN", "$"), ("MYR", "RM"), ("NOK", "kr"),
            ("NZD", "NZ$"), ("PHP", "₱"), ("PLN", "zł"), ("RON", "lei"), ("SEK", "kr"),
            ("SGD", "S$"), ("THB", "฿"), ("TRY", "₺"), ("USD", "$"), ("ZAR", "R"),
        ];

        /// <summary>Looks up a currency's display symbol by its 3-letter code, falling back to the
        /// code itself if it's somehow not in the fixed list (should never happen given the DB check
        /// constraint, but a stray/legacy value shouldn't crash a render).</summary>
        public static string SymbolFor(string code) =>
            All.FirstOrDefault(c => c.Code == code) is { Symbol: not null } match ? match.Symbol : code;
    }

    /// <summary>Glyphs from Resources/Fonts/lucide.ttf (lucide-static npm package, ISC license) —
    /// use with FontFamily="Lucide". Codepoints come from that package's font/codepoints.json,
    /// not a standard PUA range, so look up any new icon there rather than guessing.</summary>
    public static class Icons
    {
        public const string ArrowLeft = "";
        public const string ArrowRight = "";
        public const string MoreVertical = "";
        public const string Plus = "";
        public const string LogOut = "";
        public const string User = "";
        public const string Users = "";
        public const string ShoppingCart = "";
        public const string Ticket = "";
        public const string Car = "";
        public const string Plane = "";
    }

    public static class Routes
    {
        public const string Splash = "//Splash";
        public const string Login = "//Login";
        public const string Groups = "//Groups";
        public const string GroupDetails = "GroupDetails";
        public const string Members = "Members";
        public const string JoinGroup = "JoinGroup";
        public const string AddExpense = "AddExpense";
        public const string RecurringExpenses = "RecurringExpenses";
        public const string NewGroup = "NewGroup";
        public const string Profile = "Profile";
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

        /// <summary>Where Supabase redirects after a password-recovery link is clicked — a
        /// standalone page (web/reset/index.html) that reads the recovery session straight out of
        /// the URL via the Supabase JS SDK and lets the user set a new password there. Not an App
        /// Link/deep-link target like BuildInviteUrl: Windows has no deep-link support at all, so
        /// this has to work as a plain browser page regardless of platform.</summary>
        public const string PasswordResetUrl = $"https://{InviteHost}/reset";

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
