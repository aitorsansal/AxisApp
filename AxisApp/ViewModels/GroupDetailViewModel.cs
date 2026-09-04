using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the group detail's Balances section. In pairwise mode this is always a
/// real debt between the viewer and <see cref="Member"/>. In simplified mode it's one suggested
/// settle-up transfer — <see cref="Member"/> is the payer; if the transfer doesn't involve the
/// viewer at all (<see cref="IsNeutral"/>), <see cref="Member"/> pays <see cref="ToName"/>
/// instead of "you", and no owed/owing color is shown.</summary>
public partial class MemberBalanceItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public string? ToName { get; init; }
    public Guid? ToMemberId { get; init; }
    public bool IsNeutral { get; init; }

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

public partial class GroupDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Dictionary<Guid, Member> membersById = new();
    private Dictionary<Guid, string> aliases = new();
    private Guid? myMemberId;
    private Group? currentGroup;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<MemberBalanceItem> balances = [];
    [ObservableProperty] private ObservableCollection<ActivityItem> recentActivity = [];
    [ObservableProperty] private bool isBusy;

    /// <summary>Whether the current account created this group — drives which of Rename/Leave/
    /// Transfer/Dissolve show up in the group options menu (GroupDetailPage.xaml).</summary>
    [ObservableProperty] private bool isGroupCreator;
    [ObservableProperty] private bool hasOtherMembers;
    [ObservableProperty] private bool isGroupOptionsMenuOpen;
    [ObservableProperty] private bool isTransferPickerOpen;
    [ObservableProperty] private ObservableCollection<Member> transferCandidates = [];
    [ObservableProperty] private bool hasTransferCandidates;
    [ObservableProperty] private bool isRenameGroupOverlayOpen;
    [ObservableProperty] private string renameGroupInput = "";

    /// <summary>Per-device display preference, not group state — see
    /// AppConstants.Preferences.BalanceDisplayModePrefix. Set directly from the stored value in
    /// ApplyQueryAttributes (bypassing OnIsPairwiseModeChanged) since that initial set shouldn't
    /// trigger a reload before LoadAsync has even run once.</summary>
    [ObservableProperty] private bool isPairwiseMode;

    partial void OnIsPairwiseModeChanged(bool value)
    {
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, value);
        if (membersById.Count > 0) _ = RunSafeAsync(RefreshBalancesAsync);
    }

    private string PreferenceKey => $"{AppConstants.Preferences.BalanceDisplayModePrefix}{groupId}";

    public GroupDetailViewModel(
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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupName", out var nameValue))
            GroupName = Uri.UnescapeDataString(nameValue?.ToString() ?? "");

        if (query.TryGetValue("groupId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
        {
            groupId = id;
            isPairwiseMode = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, false);
            _ = LoadAsync();
        }
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        try
        {
            var loadGroup = groupsRepository.GetByIdAsync(groupId);
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadExpenses = expensesRepository.GetForGroupAsync(groupId);
            var loadAliases = aliasesRepository.GetMyAliasesAsync();
            await Task.WhenAll(loadGroup, loadMembers, loadExpenses, loadAliases);

            var members = loadMembers.Result;
            membersById = members.ToDictionary(m => m.Id);
            aliases = loadAliases.Result;
            myMemberId = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)?.Id;
            currentGroup = loadGroup.Result;
            IsGroupCreator = currentGroup.CreatedBy == authService.CurrentAccountId;
            HasOtherMembers = members.Count > 1;

            await RefreshBalancesAsync();

            var loc = LocalizationResourceManager.Instance;
            var activity = new List<ActivityItem>();
            foreach (var expense in loadExpenses.Result)
            {
                var payer = membersById.TryGetValue(expense.PaidByMemberId, out var expensePayer)
                    ? MemberDisplay.Name(expensePayer, aliases) : loc["GroupDetail_SomeoneCapitalized"];
                var shares = await expensesRepository.GetSharesAsync(expense.Id);

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

                activity.Add(new ActivityItem
                {
                    Description = description,
                    SubCaption = subCaption,
                    AmountText = $"€{expense.Amount:0.00}",
                    IsSettlement = expense.IsSettlement,
                    OccurredAt = expense.OccurredAt,
                    CreatedAt = expense.CreatedAt,
                    RelativeDate = FormatRelative(expense.OccurredAt),
                    ExpenseId = expense.Id
                });
            }

            RecentActivity = new ObservableCollection<ActivityItem>(
                activity.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.CreatedAt));
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Rebuilds the Balances section for whichever mode is currently selected, without
    /// refetching members/activity — used both by LoadAsync and by the mode toggle.</summary>
    private async Task RefreshBalancesAsync()
    {
        var items = IsPairwiseMode
            ? await BuildPairwiseItemsAsync()
            : BuildSimplifiedItems(await balancesRepository.GetForGroupAsync(groupId), membersById, myMemberId, aliases);

        Balances = new ObservableCollection<MemberBalanceItem>(items);
    }

    /// <summary>Pairwise mode: my_pairwise_balances already gives real, two-party debts from
    /// the viewer's own perspective (positive = they owe me), so no member exclusion or sign
    /// juggling is needed — unlike the old (buggy) group_balances-based display this replaced,
    /// "owes you"/"you owe" is always a literally true statement here.</summary>
    private async Task<List<MemberBalanceItem>> BuildPairwiseItemsAsync()
    {
        var pairwise = await balancesRepository.GetMyPairwiseForGroupAsync(groupId);
        var items = new List<MemberBalanceItem>();
        foreach (var row in pairwise)
        {
            if (!membersById.TryGetValue(row.OtherMemberId, out var member)) continue;
            var item = new MemberBalanceItem
            {
                Member = member,
                Name = MemberDisplay.Name(member, aliases),
                Initials = MemberDisplay.Initials(member, aliases),
                AvatarUrl = MemberDisplay.AvatarUrl(member),
                Amount = Math.Abs(row.Balance)
            };
            if (row.Balance > 0)
            {
                item.IsOwed = true;
                item.IsSettled = false;
                item.AmountText = $"+€{row.Balance:0.00}";
                item.CaptionText = LocalizationResourceManager.Instance["GroupDetail_OwesYou"];
            }
            else if (row.Balance < 0)
            {
                item.IsOwing = true;
                item.IsSettled = false;
                item.AmountText = $"-€{Math.Abs(row.Balance):0.00}";
                item.CaptionText = LocalizationResourceManager.Instance["GroupDetail_YouOwe"];
            }
            items.Add(item);
        }
        return items;
    }

    /// <summary>Simplified mode: run DebtSimplifier over every member's group-wide net balance
    /// (including the viewer's own — unlike the old display, nobody is excluded) to get the
    /// minimum-transfer settle-up plan. A transfer only gets "you owe"/"owes you" phrasing when
    /// the viewer is actually one of its two parties; otherwise it's shown neutrally as
    /// "X pays Y", since it's not a statement about the viewer at all.</summary>
    private static List<MemberBalanceItem> BuildSimplifiedItems(
        IEnumerable<GroupBalance> balances, IReadOnlyDictionary<Guid, Member> membersById, Guid? myMemberId,
        IReadOnlyDictionary<Guid, string> aliases)
    {
        var items = new List<MemberBalanceItem>();
        foreach (var transfer in DebtSimplifier.Simplify(balances.Select(b => (b.MemberId, b.Balance))))
        {
            if (!membersById.TryGetValue(transfer.FromMemberId, out var from)) continue;
            if (!membersById.TryGetValue(transfer.ToMemberId, out var to)) continue;

            var loc = LocalizationResourceManager.Instance;
            if (transfer.FromMemberId == myMemberId)
            {
                items.Add(new MemberBalanceItem
                {
                    Member = to,
                    Name = MemberDisplay.Name(to, aliases),
                    Initials = MemberDisplay.Initials(to, aliases),
                    AvatarUrl = MemberDisplay.AvatarUrl(to),
                    Amount = transfer.Amount,
                    IsOwing = true,
                    IsSettled = false,
                    AmountText = $"-€{transfer.Amount:0.00}",
                    CaptionText = loc["GroupDetail_YouOwe"]
                });
            }
            else if (transfer.ToMemberId == myMemberId)
            {
                items.Add(new MemberBalanceItem
                {
                    Member = from,
                    Name = MemberDisplay.Name(from, aliases),
                    Initials = MemberDisplay.Initials(from, aliases),
                    AvatarUrl = MemberDisplay.AvatarUrl(from),
                    Amount = transfer.Amount,
                    IsOwed = true,
                    IsSettled = false,
                    AmountText = $"+€{transfer.Amount:0.00}",
                    CaptionText = loc["GroupDetail_OwesYou"]
                });
            }
            else
            {
                var toName = MemberDisplay.Name(to, aliases);
                items.Add(new MemberBalanceItem
                {
                    Member = from,
                    Name = MemberDisplay.Name(from, aliases),
                    Initials = MemberDisplay.Initials(from, aliases),
                    AvatarUrl = MemberDisplay.AvatarUrl(from),
                    ToName = toName,
                    ToMemberId = to.Id,
                    Amount = transfer.Amount,
                    IsNeutral = true,
                    AmountText = $"€{transfer.Amount:0.00}",
                    CaptionText = loc.Format("GroupDetail_Pays", toName)
                });
            }
        }
        return items;
    }

    private static string FormatRelative(DateTime occurredAtUtc)
    {
        var loc = LocalizationResourceManager.Instance;
        var elapsed = DateTime.UtcNow - occurredAtUtc;
        if (elapsed.TotalMinutes < 1) return loc["GroupDetail_JustNow"];
        if (elapsed.TotalHours < 1) return loc.Format("GroupDetail_MinutesAgo", (int)elapsed.TotalMinutes);
        if (elapsed.TotalDays < 1) return loc.Format("GroupDetail_HoursAgo", (int)elapsed.TotalHours);
        if (elapsed.TotalDays < 7) return loc.Format("GroupDetail_DaysAgo", (int)elapsed.TotalDays);
        return occurredAtUtc.ToLocalTime().ToString("MMM d");
    }

    [RelayCommand]
    private Task AddExpense() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}"));

    /// <summary>Tapping any activity row opens it for editing via AddExpensePage — a settlement is
    /// just an Expense with IsSettlement true, so this needs no special-casing; the page itself
    /// hides category/receipt for that case (see AddExpenseViewModel.ShowMoneyExtras).</summary>
    [RelayCommand]
    private Task OpenActivity(ActivityItem? item) => RunSafeAsync(async () =>
    {
        if (item?.ExpenseId is not { } expenseId) return;
        await Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&expenseId={expenseId}");
    });

    [RelayCommand]
    private Task ViewMembers() => RunSafeAsync(() =>
    {
        IsGroupOptionsMenuOpen = false;
        return Shell.Current.GoToAsync(
            $"{AppConstants.Routes.Members}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}");
    });

    [RelayCommand]
    private Task ViewRecurringExpenses() => RunSafeAsync(() =>
    {
        IsGroupOptionsMenuOpen = false;
        return Shell.Current.GoToAsync(
            $"{AppConstants.Routes.RecurringExpenses}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}");
    });

    /// <summary>Records a settlement — an Expense with IsSettlement true, paid_by = the discharging
    /// party, and one ExpenseShare for the party being paid back (see Models/Expense.cs and
    /// CLAUDE.md's "Merge payments into expenses" remarks for why this replaced a dedicated
    /// Payment table). Works the same way regardless of balance display mode, since both modes
    /// ultimately produce a (payer, payee, amount) triple on MemberBalanceItem — Pairwise's is a
    /// real counterparty debt, Simplified's is whatever transfer the settle-up algorithm suggested
    /// for that row (see "Balances: simplified vs. pairwise" in CLAUDE.md). A settled/neutral-
    /// with-no-ToMemberId row (shouldn't normally reach here since the UI only offers Settle where
    /// one of these is true) is a no-op.</summary>
    [RelayCommand]
    private Task Settle(MemberBalanceItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;

        Guid payerId, payeeId;
        if (item.IsNeutral)
        {
            if (item.ToMemberId is not { } toId) return;
            payerId = item.Member.Id;
            payeeId = toId;
        }
        else if (item.IsOwing)
        {
            if (myMemberId is not { } me) return;
            payerId = me;
            payeeId = item.Member.Id;
        }
        else if (item.IsOwed)
        {
            if (myMemberId is not { } me) return;
            payerId = item.Member.Id;
            payeeId = me;
        }
        else
        {
            return;
        }

        var settlement = new Expense
        {
            GroupId = groupId,
            PaidByMemberId = payerId,
            Amount = item.Amount,
            Description = LocalizationResourceManager.Instance["GroupDetail_SettleUp"],
            OccurredAt = DateTime.UtcNow,
            IsSettlement = true
        };
        var shares = new List<ExpenseShare> { new() { MemberId = payeeId, ShareAmount = item.Amount } };
        await expensesRepository.AddAsync(settlement, shares);

        await LoadAsync();
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private void ToggleGroupOptionsMenu() => IsGroupOptionsMenuOpen = !IsGroupOptionsMenuOpen;

    /// <summary>Opens an inline rename overlay rather than Shell.Current.DisplayPromptAsync — same
    /// known WinUI crash avoided by MembersViewModel.OpenRenameOverlay (see its remarks). Only ever
    /// offered to the creator (see GroupDetailPage.xaml), matching the "owner-only" RLS policy
    /// backing IGroupsRepository.RenameAsync.</summary>
    [RelayCommand]
    private void OpenRenameGroupOverlay()
    {
        IsGroupOptionsMenuOpen = false;
        RenameGroupInput = GroupName;
        IsRenameGroupOverlayOpen = true;
    }

    [RelayCommand]
    private void CancelRenameGroupOverlay() => IsRenameGroupOverlayOpen = false;

    [RelayCommand]
    private Task ConfirmRenameGroup() => RunSafeAsync(async () =>
    {
        IsRenameGroupOverlayOpen = false;
        if (currentGroup is not { } group) return;

        var trimmed = RenameGroupInput.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == group.Name) return;

        group.Name = trimmed;
        currentGroup = await groupsRepository.RenameAsync(group);
        GroupName = currentGroup.Name;
    });

    /// <summary>Self-service leave. The confirm dialog only covers the always-true "you'll lose
    /// access" consequence — the creator-only and nonzero-balance guards live server-side in
    /// leave_group() (see schema.sql), so a rejection surfaces as ErrorMessage via RunSafeAsync
    /// rather than being pre-checked here. The group options menu only offers this item to
    /// non-creators (see GroupDetailPage.xaml) so the common creator case never even reaches it.</summary>
    [RelayCommand]
    private Task LeaveGroup() => RunSafeAsync(async () =>
    {
        IsGroupOptionsMenuOpen = false;
        var loc = LocalizationResourceManager.Instance;
        var confirmed = await Shell.Current.DisplayAlert(
            loc["GroupDetail_LeaveGroupTitle"],
            loc["GroupDetail_LeaveGroupConfirm"],
            loc["Common_Yes"],
            loc["Common_Cancel"]);
        if (!confirmed) return;

        await groupsRepository.LeaveAsync(groupId);
        await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
    });

    /// <summary>Opens the transfer-target picker with every current claimed (real-account) member
    /// except the creator themselves — a phantom has no account to own the group, so it's excluded
    /// rather than shown disabled.</summary>
    [RelayCommand]
    private void OpenTransferPicker()
    {
        IsGroupOptionsMenuOpen = false;
        TransferCandidates = new ObservableCollection<Member>(
            membersById.Values.Where(m => !m.IsPhantom && m.Id != myMemberId));
        HasTransferCandidates = TransferCandidates.Count > 0;
        IsTransferPickerOpen = true;
    }

    [RelayCommand]
    private void CancelTransferPicker() => IsTransferPickerOpen = false;

    [RelayCommand]
    private Task TransferOwnership(Member? newOwner) => RunSafeAsync(async () =>
    {
        if (newOwner is null) return;
        IsTransferPickerOpen = false;
        await groupsRepository.TransferOwnershipAsync(groupId, newOwner.Id);
        await LoadAsync();
    });

    /// <summary>Dissolves the group outright — the only path available to a creator, whether
    /// they're the last member (equivalent to leaving) or there are others still in it. Warns
    /// about outstanding balances rather than blocking on them, since forcing an entire group to
    /// fully settle before its creator can walk away is a much bigger ask than the one-person case
    /// LeaveGroup enforces server-side.</summary>
    [RelayCommand]
    private Task DissolveGroup() => RunSafeAsync(async () =>
    {
        IsGroupOptionsMenuOpen = false;
        var loc = LocalizationResourceManager.Instance;
        var groupBalances = await balancesRepository.GetForGroupAsync(groupId);
        var hasOutstanding = groupBalances.Any(b => b.Balance != 0);
        var message = hasOutstanding
            ? loc["GroupDetail_DissolveGroupConfirmWithBalances"]
            : loc["GroupDetail_DissolveGroupConfirm"];

        var confirmed = await Shell.Current.DisplayAlert(
            loc["GroupDetail_DissolveGroupTitle"],
            message,
            loc["Common_Yes"],
            loc["Common_Cancel"]);
        if (!confirmed) return;

        await groupsRepository.DeleteAsync(groupId);
        await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
    });
}
