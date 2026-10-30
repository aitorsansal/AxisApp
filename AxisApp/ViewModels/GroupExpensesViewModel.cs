using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the group detail's Balances section: a real pairwise debt between the
/// viewer and <see cref="Member"/>.</summary>
public partial class MemberBalanceItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }

    /// <summary>The unsigned amount this row represents — kept separately from AmountText
    /// (which carries a +/-/$ display prefix) so Settle doesn't have to re-parse formatted text
    /// to know what payment to create.</summary>
    public decimal Amount { get; init; }

    [ObservableProperty] private bool isOwed;
    [ObservableProperty] private bool isOwing;
    [ObservableProperty] private bool isSettled = true;
    [ObservableProperty] private string amountText = "";
    [ObservableProperty] private string captionText = LocalizationResourceManager.Instance["Common_SettledUp"];
}

/// <summary>One row in the group's recent-activity feed — an Expense, either a real split
/// (IsSettlement false) or a settle-up (true, exactly one share — see Models/Expense.cs).</summary>
public partial class ActivityItem : ObservableObject
{
    public string Description { get; init; } = "";
    public string SubCaption { get; init; } = "";
    public string AmountText { get; init; } = "";

    /// <summary>The expense's own original amount+currency (e.g. "¥180.00"), shown smaller under
    /// AmountText only when it differs from the group's settlement currency — empty otherwise, so
    /// the common same-currency case shows nothing extra. See MULTI_CURRENCY_PLAN.md's Milestone 5
    /// "On" default (the only mode built so far — no ProfilePage toggle to turn it off yet).</summary>
    public string SecondaryAmountText { get; init; } = "";
    public bool IsSettlement { get; init; }
    public DateTime OccurredAt { get; init; }

    /// <summary>Tiebreaker for same-day ordering — OccurredAt only carries a date (no time of
    /// day) for expenses, since it's picked from a plain date picker, so several same-day
    /// entries would otherwise sort in whatever arbitrary order the DB happens to return them.
    /// CreatedAt always has full timestamp precision (server-set via now() on insert), so it
    /// reflects actual creation order even when OccurredAt can't.</summary>
    public DateTime CreatedAt { get; init; }

    public string RelativeDate { get; init; } = "";

    /// <summary>Always set now that every activity row is an Expense — tapping any row, settlement
    /// or not, opens the same AddExpensePage edit flow (see OpenActivity below).</summary>
    public Guid? ExpenseId { get; init; }
}

/// <summary>Expenses tab of GroupDetailPage — balances (Simplified/Pairwise), recent activity,
/// Settle, and Add Expense. Split out of GroupDetailViewModel (see /EVENTS_PLAN.md Milestone 2)
/// once Events needed its own sibling tab; this ViewModel now owns everything that used to be
/// GroupDetailViewModel's entire body, unchanged in behavior. Deliberately fetches its own
/// group/members/aliases rather than receiving them from GroupDetailPage's parent ViewModel — a
/// little redundant with the parent's own group/members fetch (needed there for the ⋮ menu's
/// IsGroupCreator/HasOtherMembers/TransferCandidates), but keeps this tab genuinely independent
/// rather than coupling its freshness to exactly when/how the parent happens to refresh.</summary>
public partial class GroupExpensesViewModel : BaseViewModel
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IAuthService authService;

    private const int PageSize = 30;
    private const int SearchResultLimit = 50;

    private Guid groupId;
    private Dictionary<Guid, Member> membersById = new();
    private Dictionary<Guid, string> aliases = new();
    private Guid? myMemberId;
    private Group? currentGroup;

    /// <summary>Every page loaded so far via LoadAsync/LoadMore, newest-first — RecentActivity
    /// mirrors this when SearchQuery is empty and gets swapped for search results otherwise, so
    /// clearing the search box can restore the paged view without refetching anything.</summary>
    private readonly List<ActivityItem> loadedActivity = [];
    private int loadedOffset;
    private int searchGeneration;

    /// <summary>Guards loadedActivity against overlapping LoadAsync calls — GroupDetailViewModel.
    /// ApplyQueryAttributes re-runs LoadAsync on every navigation to GroupDetailPage, including
    /// navigating back to it from AddExpensePage, and Shell can fire that twice in quick
    /// succession. Two overlapping calls each clearing then appending their own fetched page used
    /// to double every row (4 became 8) — bumped at the start of each LoadAsync/LoadMore and
    /// checked in AppendActivityPageAsync before it touches loadedActivity, so a stale call's
    /// results are discarded instead of appended on top of a newer call's.</summary>
    private int loadGeneration;

    [ObservableProperty] private ObservableCollection<MemberBalanceItem> balances = [];
    [ObservableProperty] private ObservableCollection<ActivityItem> recentActivity = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private bool hasMorePages;
    [ObservableProperty] private string searchQuery = "";
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private bool hasNoSearchResults;
    /// <summary>True only while the very first LoadAsync (for this tab instance) is in flight —
    /// kept separate from IsBusy, which also gets set on every later Refresh (after a save/settle),
    /// so the skeleton and its minimum-visible-duration padding only apply to the cold load.</summary>
    [ObservableProperty] private bool isInitialLoading;
    private bool hasLoadedOnce;
    private bool refreshing;
    private DateTime loadedAtUtc;

    public GroupExpensesViewModel(
        IGroupsRepository groupsRepository,
        IMembersRepository membersRepository,
        IBalancesRepository balancesRepository,
        IExpensesRepository expensesRepository,
        IAliasesRepository aliasesRepository,
        IAuthService authService)
    {
        this.groupsRepository = groupsRepository;
        this.membersRepository = membersRepository;
        this.balancesRepository = balancesRepository;
        this.expensesRepository = expensesRepository;
        this.aliasesRepository = aliasesRepository;
        this.authService = authService;
    }

    public Task LoadAsync(Guid groupId) => RunSafeAsync(async () =>
    {
        this.groupId = groupId;
        IsBusy = true;
        var isFirstLoad = !hasLoadedOnce;
        IsInitialLoading = isFirstLoad;
        try
        {
            var generation = ++loadGeneration;

            if (isFirstLoad)
                await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), () => LoadDataAsync(generation));
            else
                await LoadDataAsync(generation);
        }
        finally
        {
            IsBusy = false;
            IsInitialLoading = false;
        }
    });

    private async Task LoadDataAsync(int generation, bool quiet = false)
    {
        var targetGroup = groupId;
        var loadGroup = groupsRepository.GetByIdAsync(targetGroup);
        var loadMembers = membersRepository.GetForGroupAsync(targetGroup);
        var loadExpenses = expensesRepository.GetForGroupAsync(targetGroup, PageSize, 0);
        var loadAliases = aliasesRepository.GetMyAliasesAsync();
        await Task.WhenAll(loadGroup, loadMembers, loadExpenses, loadAliases);
        if (generation != loadGeneration) return;

        var nextGroup = loadGroup.Result;
        var nextMembers = loadMembers.Result.ToDictionary(m => m.Id);
        var nextAliases = loadAliases.Result;
        var page = loadExpenses.Result;
        var groupSymbol = AppConstants.Currencies.SymbolFor(nextGroup.Currency);
        var loadShares = expensesRepository.GetSharesForExpensesAsync(page.Select(e => e.Id).ToList());
        var loadBalances = BuildPairwiseItemsAsync(groupSymbol, nextGroup.Currency, targetGroup, nextMembers, nextAliases);
        await Task.WhenAll(loadShares, loadBalances);
        if (generation != loadGeneration) return;
        // Search or paging may have started while a quiet reload was in flight.
        if (quiet && (IsSearching || !string.IsNullOrEmpty(SearchQuery) || IsLoadingMore || loadedOffset > PageSize)) return;

        var showConverted = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.AmountDisplayConverted, true);
        var items = BuildActivityItems(page, loadShares.Result.ToLookup(s => s.ExpenseId),
            nextMembers, nextAliases, groupSymbol, nextGroup.Currency, showConverted);

        membersById = nextMembers;
        aliases = nextAliases;
        myMemberId = nextMembers.Values.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)?.Id;
        currentGroup = nextGroup;
        if (!quiet)
        {
            SearchQuery = "";
            IsSearching = false;
            HasNoSearchResults = false;
        }
        loadedActivity.Clear();
        loadedActivity.AddRange(items);
        loadedOffset = page.Count;
        HasMorePages = page.Count == PageSize;
        Balances = new ObservableCollection<MemberBalanceItem>(loadBalances.Result);
        RecentActivity = new ObservableCollection<ActivityItem>(loadedActivity);
        hasLoadedOnce = true;
        loadedAtUtc = DateTime.UtcNow;
    }

    public async Task RefreshIfStaleAsync(bool force = false)
    {
        if (!hasLoadedOnce || currentGroup is null || IsBusy || refreshing || IsLoadingMore ||
            IsSearching || !string.IsNullOrEmpty(SearchQuery)) return;
        if (!force && DateTime.UtcNow - loadedAtUtc < TimeSpan.FromSeconds(60)) return;

        refreshing = true;
        var generation = ++loadGeneration;
        try
        {
            await authService.EnsureFreshSessionAsync();
            if (generation != loadGeneration || IsSearching || !string.IsNullOrEmpty(SearchQuery) || IsLoadingMore) return;
            if (loadedOffset > PageSize)
                await RefreshBalancesAsync(generation);
            else
                await LoadDataAsync(generation, quiet: true);
        }
        catch { }
        finally { refreshing = false; }
    }

    /// <summary>Rebuilds the Balances section without refetching members/activity.</summary>
    private async Task RefreshBalancesAsync(int generation)
    {
        var groupSymbol = AppConstants.Currencies.SymbolFor(currentGroup!.Currency);
        var items = await BuildPairwiseItemsAsync(groupSymbol, currentGroup!.Currency, groupId, membersById, aliases);
        if (generation != loadGeneration) return;
        Balances = new ObservableCollection<MemberBalanceItem>(items);
        loadedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Builds ActivityItem rows for a batch of expenses, given their shares already
    /// fetched in one round trip (see IExpensesRepository.GetSharesForExpensesAsync) instead of
    /// one GetSharesAsync call per expense. Internal so EventDetailViewModel can share it too.</summary>
    internal static List<ActivityItem> BuildActivityItems(
        IReadOnlyList<Expense> expenses, ILookup<Guid, ExpenseShare> sharesByExpenseId,
        IReadOnlyDictionary<Guid, Member> membersById, IReadOnlyDictionary<Guid, string> aliases,
        string groupSymbol, string groupCurrency, bool showConverted)
    {
        var loc = LocalizationResourceManager.Instance;
        var activity = new List<ActivityItem>();
        foreach (var expense in expenses)
        {
            var payer = membersById.TryGetValue(expense.PaidByMemberId, out var expensePayer)
                ? MemberDisplay.Name(expensePayer, aliases) : loc["GroupDetail_SomeoneCapitalized"];
            var shares = sharesByExpenseId[expense.Id].ToList();

            string description, subCaption;
            if (expense.IsSettlement)
            {
                var payee = shares.Count > 0 && membersById.TryGetValue(shares[0].MemberId, out var payeeMember)
                    ? MemberDisplay.Name(payeeMember, aliases) : loc["GroupDetail_SomeoneLower"];
                description = string.IsNullOrWhiteSpace(expense.Description) ? loc["GroupDetail_SettleUp"] : expense.Description;
                subCaption = loc.Format("GroupDetail_Paid", payer, payee);
            }
            else
            {
                var categoryLabel = string.IsNullOrEmpty(expense.Category) ? "" : loc[$"Category_{expense.Category}"];
                description = string.IsNullOrWhiteSpace(expense.Description) ? categoryLabel : expense.Description;
                subCaption = loc.Format("GroupDetail_PaidSplit", payer, shares.Count);
            }

            var (amountText, secondaryAmountText) = FormatExpenseAmount(expense, groupSymbol, groupCurrency, showConverted);

            activity.Add(new ActivityItem
            {
                Description = description,
                SubCaption = subCaption,
                AmountText = amountText,
                SecondaryAmountText = secondaryAmountText,
                IsSettlement = expense.IsSettlement,
                OccurredAt = expense.OccurredAt,
                CreatedAt = expense.CreatedAt,
                RelativeDate = FormatRelative(expense.OccurredAt),
                ExpenseId = expense.Id
            });
        }
        return activity;
    }

    /// <summary>Appends one page of expenses to loadedActivity (batch-fetching their shares in a
    /// single round trip), advances the paging cursor, and — only while not searching — mirrors
    /// the accumulated list into the bound RecentActivity. Bails out without touching any state if
    /// a newer LoadAsync/LoadMore has started since this page was requested (see loadGeneration's
    /// remarks) — otherwise two overlapping calls would each append their own page on top of the
    /// other's, duplicating every row.</summary>
    private async Task AppendActivityPageAsync(IReadOnlyList<Expense> page, int generation)
    {
        var shares = await expensesRepository.GetSharesForExpensesAsync(page.Select(e => e.Id).ToList());
        if (generation != loadGeneration) return;

        var sharesByExpenseId = shares.ToLookup(s => s.ExpenseId);
        var groupSymbol = AppConstants.Currencies.SymbolFor(currentGroup!.Currency);
        var showConverted = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.AmountDisplayConverted, true);

        var items = BuildActivityItems(page, sharesByExpenseId, membersById, aliases, groupSymbol, currentGroup!.Currency, showConverted);
        loadedActivity.AddRange(items);
        loadedOffset += page.Count;
        HasMorePages = page.Count == PageSize;

        if (!IsSearching)
            RecentActivity = new ObservableCollection<ActivityItem>(loadedActivity);
    }

    [RelayCommand]
    private Task LoadMore() => RunSafeAsync(async () =>
    {
        if (IsLoadingMore || !HasMorePages || IsSearching) return;
        var generation = loadGeneration;
        IsLoadingMore = true;
        try
        {
            var page = await expensesRepository.GetForGroupAsync(groupId, PageSize, loadedOffset);
            await AppendActivityPageAsync(page, generation);
        }
        finally
        {
            IsLoadingMore = false;
        }
    });

    /// <summary>Search-as-you-type against the backend (not the loaded page) — same
    /// generation-counter pattern as InviteToGroupViewModel.OnNewPhantomNameChanged, so a stale
    /// response can't clobber a newer keystroke's result.</summary>
    partial void OnSearchQueryChanged(string value)
    {
        var generation = ++searchGeneration;
        _ = RunSafeAsync(() => RunSearchAsync(value, generation));
    }

    private async Task RunSearchAsync(string query, int generation)
    {
        if (currentGroup is null) return; // not loaded yet — LoadAsync will reset SearchQuery anyway

        var trimmed = query.Trim();
        if (trimmed.Length < 2)
        {
            IsSearching = false;
            HasNoSearchResults = false;
            RecentActivity = new ObservableCollection<ActivityItem>(loadedActivity);
            return;
        }

        IsSearching = true;
        var matches = await expensesRepository.SearchForGroupAsync(groupId, trimmed, SearchResultLimit);
        if (generation != searchGeneration) return; // a newer keystroke already superseded this

        var shares = await expensesRepository.GetSharesForExpensesAsync(matches.Select(e => e.Id).ToList());
        if (generation != searchGeneration) return;

        var sharesByExpenseId = shares.ToLookup(s => s.ExpenseId);
        var groupSymbol = AppConstants.Currencies.SymbolFor(currentGroup!.Currency);
        var showConverted = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.AmountDisplayConverted, true);
        var items = BuildActivityItems(matches, sharesByExpenseId, membersById, aliases, groupSymbol, currentGroup!.Currency, showConverted);

        RecentActivity = new ObservableCollection<ActivityItem>(items);
        HasNoSearchResults = items.Count == 0;
    }

    /// <summary>my_pairwise_balances already gives real, two-party debts from the viewer's own
    /// perspective (positive = they owe me), so "owes you"/"you owe" is always literally true.
    /// The old Simplified (DebtSimplifier) mode was removed 2026-10-27: settling one of its
    /// suggested transfers between two people who never shared an expense left made-up debts in
    /// this pairwise view.</summary>
    private async Task<List<MemberBalanceItem>> BuildPairwiseItemsAsync(string groupSymbol, string groupCurrency, Guid targetGroup,
        IReadOnlyDictionary<Guid, Member> members, IReadOnlyDictionary<Guid, string> memberAliases)
    {
        var pairwise = await balancesRepository.GetMyPairwiseForGroupAsync(targetGroup);
        var items = new List<MemberBalanceItem>();
        foreach (var row in pairwise)
        {
            if (!members.TryGetValue(row.OtherMemberId, out var member)) continue;
            var item = new MemberBalanceItem
            {
                Member = member,
                Name = MemberDisplay.Name(member, memberAliases),
                Initials = MemberDisplay.Initials(member, memberAliases),
                AvatarUrl = MemberDisplay.AvatarUrl(member),
                Amount = Math.Abs(row.Balance)
            };
            if (row.Balance > 0)
            {
                item.IsOwed = true;
                item.IsSettled = false;
                item.AmountText = $"+{groupSymbol}{CurrencyUnits.Text(row.Balance, groupCurrency)}";
                item.CaptionText = LocalizationResourceManager.Instance["GroupDetail_OwesYou"];
            }
            else if (row.Balance < 0)
            {
                item.IsOwing = true;
                item.IsSettled = false;
                item.AmountText = $"-{groupSymbol}{CurrencyUnits.Text(Math.Abs(row.Balance), groupCurrency)}";
                item.CaptionText = LocalizationResourceManager.Instance["GroupDetail_YouOwe"];
            }
            items.Add(item);
        }
        return items;
    }

    /// <summary>Per-expense primary/secondary amount text, per
    /// AppConstants.Preferences.AmountDisplayConverted — see MULTI_CURRENCY_PLAN.md's Milestone 5
    /// "On"/"Off" behavior. Both fields are empty-secondary when the expense's own currency matches
    /// the group's (the common case), since there's nothing extra worth showing. Internal rather
    /// than private so EventDetailViewModel can reuse it for the same row shape instead of
    /// duplicating the currency-formatting logic.</summary>
    internal static (string AmountText, string SecondaryAmountText) FormatExpenseAmount(
        Expense expense, string groupSymbol, string groupCurrency, bool showConverted)
    {
        if (expense.Currency == groupCurrency)
            return ($"{groupSymbol}{CurrencyUnits.Text(expense.AmountInGroupCurrency, groupCurrency)}", "");

        var originalSymbol = AppConstants.Currencies.SymbolFor(expense.Currency);
        var converted = CurrencyUnits.Text(expense.AmountInGroupCurrency, groupCurrency);
        var original = CurrencyUnits.Text(expense.Amount, expense.Currency);
        return showConverted
            ? ($"{groupSymbol}{converted}", $"({originalSymbol}{original})")
            : ($"{originalSymbol}{original}", $"(≈{groupSymbol}{converted})");
    }

    /// <summary>Internal rather than private so EventDetailViewModel can reuse it — same reasoning
    /// as FormatExpenseAmount above.</summary>
    // occurred_at is a calendar day stored as a midnight timestamp, so elapsed-time wording
    // ("21h ago") is meaningless for it; compare local calendar days instead.
    internal static string FormatRelative(DateTime occurredAtUtc)
    {
        var loc = LocalizationResourceManager.Instance;
        var day = occurredAtUtc.ToLocalTime().Date;
        var today = DateTime.Today;
        if (day == today) return loc["Common_Today"];
        if (day == today.AddDays(-1)) return loc["Common_Yesterday"];
        return DateFormats.ShortDate(day, includeYear: day.Year != today.Year);
    }

    [RelayCommand]
    private Task AddExpense() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}"));

    [RelayCommand]
    private Task OpenActivity(ActivityItem? item) => RunSafeAsync(async () =>
    {
        if (item?.ExpenseId is not { } expenseId) return;
        await Task.Delay(Controls.Juice.PressReleaseSettleMs);
        var route = item.IsSettlement ? AppConstants.Routes.Settlement : AppConstants.Routes.AddExpense;
        await Shell.Current.GoToAsync($"{route}?groupId={groupId}&expenseId={expenseId}");
    });

    [RelayCommand]
    private Task Settle(MemberBalanceItem? item) => RunSafeAsync(async () =>
    {
        if (item is null || myMemberId is null || (!item.IsOwed && !item.IsOwing)) return;
        await Shell.Current.GoToAsync($"{AppConstants.Routes.Settlement}?groupId={groupId}&otherMemberId={item.Member.Id}");
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync(groupId);
}
