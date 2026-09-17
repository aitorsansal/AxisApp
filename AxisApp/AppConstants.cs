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

        /// <summary>Per-device date (yyyy-MM-dd, invariant) the update-available banner was last
        /// dismissed — see AppUpdateService. Closing the banner hides it until tomorrow, not
        /// permanently, so it comes back as a daily nudge for as long as an update is genuinely
        /// available.</summary>
        public const string UpdateBannerDismissedDate = "update_banner_dismissed_date";

        /// <summary>Per-device, drag-to-reorder custom order for the Groups list — a
        /// comma-separated list of group id GUIDs, most-recently-arranged first. Deliberately
        /// local only (not group state, not synced), same reasoning as AccentPreset above: it's
        /// how this one person likes to scan their own list, not something other members should
        /// see change under them. A group not present in the list (new, or before this feature
        /// existed) sorts after every listed group, in created_at order — see
        /// GroupsViewModel.ApplySavedOrder.</summary>
        public const string GroupOrder = "group_order";
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
            ["food", "drinks", "transport", "travel", "entertainment", "shopping", "subscriptions", "other"];
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
        public const string Eye = "";
        public const string EyeOff = "";
    }

    /// <summary>Added for group appearance (AppConstants.GroupIcons below) - verified present in
    /// the vendored lucide.ttf's cmap against lucide-static's current codepoints.json
    /// (2026-09-14), not guessed. Written as \u escapes rather than raw PUA characters (unlike
    /// the block above) so they stay legible/diffable in source.</summary>
    public static class GroupAppearanceIcons
    {
        public const string Home = "\uE0F5";
        public const string Heart = "\uE0F2";
        public const string Baby = "\uE2CE";
        public const string Dog = "\uE38D";
        public const string Ship = "\uE3BA";
        public const string Bike = "\uE1D2";
        public const string Tent = "\uE227";
        public const string MapPin = "\uE111";
        public const string Mountain = "\uE231";
        public const string UtensilsCrossed = "\uE2F7";
        public const string Coffee = "\uE096";
        public const string Beer = "\uE2CF";
        public const string PartyPopper = "\uE343";
        public const string Gift = "\uE0E1";
        public const string Wallet = "\uE204";
        public const string PiggyBank = "\uE13A";
        public const string Briefcase = "\uE062";
        public const string Dumbbell = "\uE3A1";
        public const string GraduationCap = "\uE234";
        public const string Gamepad2 = "\uE0DF";
        public const string Film = "\uE0D0";
        public const string Music = "\uE122";
        public const string Book = "\uE05E";
        public const string Star = "\uE176";
    }

    /// <summary>Fixed, curated set of Lucide glyphs offered as a group's icon (Groups list /
    /// Group Detail's "Edit appearance"). Each key is a stable, language-independent identifier
    /// stored in Group.Icon \u2014 same reasoning as Categories.Keys above. groups.icon's DB check
    /// constraint (schema.sql) mirrors this exact key list; keep both in sync.</summary>
    public static class GroupIcons
    {
        public static readonly IReadOnlyList<(string Key, string Glyph)> All =
        [
            ("home", GroupAppearanceIcons.Home), ("users", Icons.Users), ("heart", GroupAppearanceIcons.Heart),
            ("baby", GroupAppearanceIcons.Baby), ("dog", GroupAppearanceIcons.Dog), ("plane", Icons.Plane),
            ("car", Icons.Car), ("ship", GroupAppearanceIcons.Ship), ("bike", GroupAppearanceIcons.Bike),
            ("tent", GroupAppearanceIcons.Tent), ("map_pin", GroupAppearanceIcons.MapPin), ("mountain", GroupAppearanceIcons.Mountain),
            ("utensils_crossed", GroupAppearanceIcons.UtensilsCrossed), ("coffee", GroupAppearanceIcons.Coffee), ("beer", GroupAppearanceIcons.Beer),
            ("party_popper", GroupAppearanceIcons.PartyPopper), ("gift", GroupAppearanceIcons.Gift), ("wallet", GroupAppearanceIcons.Wallet),
            ("piggy_bank", GroupAppearanceIcons.PiggyBank), ("briefcase", GroupAppearanceIcons.Briefcase), ("dumbbell", GroupAppearanceIcons.Dumbbell),
            ("graduation_cap", GroupAppearanceIcons.GraduationCap), ("gamepad", GroupAppearanceIcons.Gamepad2), ("film", GroupAppearanceIcons.Film),
            ("music", GroupAppearanceIcons.Music), ("book", GroupAppearanceIcons.Book), ("shopping_cart", Icons.ShoppingCart),
            ("star", GroupAppearanceIcons.Star),
        ];

        /// <summary>Looks up a key's glyph, or null for an unset/unrecognized key \u2014 the caller
        /// (GroupIconCircle) then falls back to an initials circle, same "never crash on a
        /// stray/legacy value" reasoning as Currencies.SymbolFor.</summary>
        public static string? GlyphFor(string? key) =>
            key is null ? null : All.FirstOrDefault(i => i.Key == key).Glyph;
    }

    public static class Routes
    {
        public const string Splash = "//Splash";
        public const string Login = "//Login";
        public const string Register = "Register";
        public const string Groups = "//Groups";
        public const string GroupDetails = "GroupDetails";
        public const string Members = "Members";
        public const string JoinGroup = "JoinGroup";
        public const string InviteToGroup = "InviteToGroup";
        public const string AddExpense = "AddExpense";
        public const string RecurringExpenses = "RecurringExpenses";
        public const string AddEvent = "AddEvent";
        public const string EventDetail = "EventDetail";
        public const string NewGroup = "NewGroup";
        public const string Profile = "Profile";
        public const string MemberProfile = "MemberProfile";
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

        /// <summary>Where Supabase redirects after a sign-up confirmation link is clicked —
        /// web/confirm/index.html, a static "email confirmed, go back and sign in" page. The
        /// account is already confirmed by Supabase's own /auth/v1/verify endpoint before this
        /// redirect happens; the page only reports the outcome. Must be in Supabase Auth's
        /// redirect allow-list, same as PasswordResetUrl.</summary>
        public const string EmailConfirmedUrl = $"https://{InviteHost}/confirm/";

        /// <summary>Served by the same Cloudflare Worker as everything else under web/ (see
        /// AppUpdateService) — a plain static file, not an API endpoint, so it's just another path
        /// under InviteHost.</summary>
        public const string VersionCheckUrl = $"https://{InviteHost}/version.json";

        public const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=com.aitorsansal.axisapp";

        public static string BuildInviteUrl(string code) =>
            $"https://{InviteHost}/invite?code={Uri.EscapeDataString(code)}";

        /// <summary>The public, unauthenticated .ics feed backing CalendarSubscription — see
        /// schema.sql's "Calendar subscription feed" remarks. Built off SupabaseConfig.Url (not a
        /// constant here) since the Edge Function lives on the Supabase project itself, not
        /// InviteHost's Cloudflare Worker.</summary>
        public static string BuildCalendarFeedUrl(string token) =>
            $"{SupabaseConfig.Url}/functions/v1/calendar-feed/{Uri.EscapeDataString(token)}.ics";

        /// <summary>Same feed as BuildCalendarFeedUrl, as a webcal:// link — tapping this on
        /// iOS/macOS opens the native "Subscribe to this calendar?" dialog directly instead of
        /// requiring a manual "Add calendar by URL" menu detour.</summary>
        public static string BuildCalendarFeedWebcalUrl(string token) =>
            BuildCalendarFeedUrl(token).Replace("https://", "webcal://");

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
