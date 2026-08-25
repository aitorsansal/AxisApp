using System.Collections.ObjectModel;
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
    public string Initials { get; init; } = "";
    public string? ToName { get; init; }
    public bool IsNeutral { get; init; }

    [ObservableProperty] private bool isOwed;
    [ObservableProperty] private bool isOwing;
    [ObservableProperty] private bool isSettled = true;
    [ObservableProperty] private string amountText = "";
    [ObservableProperty] private string captionText = "Settled up";
}

/// <summary>One row in the group's recent-activity feed — either a settle-up payment or a
/// split expense, normalized to the same display shape.</summary>
public partial class ActivityItem : ObservableObject
{
    public string Description { get; init; } = "";
    public string SubCaption { get; init; } = "";
    public string AmountText { get; init; } = "";
    public bool IsSettlement { get; init; }
    public DateTime OccurredAt { get; init; }
    public string RelativeDate { get; init; } = "";

    /// <summary>Set for expense-sourced rows, null for settlement/payment rows — editing a
    /// payment isn't part of this screen's scope, only expenses.</summary>
    public Guid? ExpenseId { get; init; }
}

public partial class GroupDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IPaymentsRepository paymentsRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Dictionary<Guid, Member> membersById = new();
    private Guid? myMemberId;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<MemberBalanceItem> balances = [];
    [ObservableProperty] private ObservableCollection<ActivityItem> recentActivity = [];
    [ObservableProperty] private bool isBusy;

    /// <summary>Per-device display preference, not group state — see
    /// AppConstants.Preferences.BalanceDisplayModePrefix. Set directly from the stored value in
    /// ApplyQueryAttributes (bypassing OnIsPairwiseModeChanged) since that initial set shouldn't
    /// trigger a reload before LoadAsync has even run once.</summary>
    [ObservableProperty] private bool isPairwiseMode;

    partial void OnIsPairwiseModeChanged(bool value)
    {
        Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, value);
        if (membersById.Count > 0) _ = RefreshBalancesAsync();
    }

    private string PreferenceKey => $"{AppConstants.Preferences.BalanceDisplayModePrefix}{groupId}";

    public GroupDetailViewModel(
        IMembersRepository membersRepository,
        IBalancesRepository balancesRepository,
        IPaymentsRepository paymentsRepository,
        IExpensesRepository expensesRepository,
        IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.balancesRepository = balancesRepository;
        this.paymentsRepository = paymentsRepository;
        this.expensesRepository = expensesRepository;
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

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadPayments = paymentsRepository.GetForGroupAsync(groupId);
            var loadExpenses = expensesRepository.GetForGroupAsync(groupId);
            await Task.WhenAll(loadMembers, loadPayments, loadExpenses);

            var members = loadMembers.Result;
            membersById = members.ToDictionary(m => m.Id);
            myMemberId = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)?.Id;

            await RefreshBalancesAsync();

            var activity = new List<ActivityItem>();
            foreach (var payment in loadPayments.Result)
            {
                var payer = membersById.GetValueOrDefault(payment.PayerMemberId)?.DisplayName ?? "Someone";
                var payee = membersById.GetValueOrDefault(payment.PayeeMemberId)?.DisplayName ?? "someone";
                activity.Add(new ActivityItem
                {
                    Description = string.IsNullOrWhiteSpace(payment.Description) ? "Settle up" : payment.Description,
                    SubCaption = $"{payer} paid {payee}",
                    AmountText = $"+${payment.Amount:0.00}",
                    IsSettlement = true,
                    OccurredAt = payment.OccurredAt,
                    RelativeDate = FormatRelative(payment.OccurredAt)
                });
            }
            foreach (var expense in loadExpenses.Result)
            {
                var payer = membersById.GetValueOrDefault(expense.PaidByMemberId)?.DisplayName ?? "Someone";
                var shares = await expensesRepository.GetSharesAsync(expense.Id);
                activity.Add(new ActivityItem
                {
                    Description = string.IsNullOrWhiteSpace(expense.Description) ? expense.Category : expense.Description,
                    SubCaption = $"{payer} paid · split {shares.Count} ways",
                    AmountText = $"${expense.Amount:0.00}",
                    IsSettlement = false,
                    OccurredAt = expense.OccurredAt,
                    RelativeDate = FormatRelative(expense.OccurredAt),
                    ExpenseId = expense.Id
                });
            }

            RecentActivity = new ObservableCollection<ActivityItem>(activity.OrderByDescending(a => a.OccurredAt));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Rebuilds the Balances section for whichever mode is currently selected, without
    /// refetching members/activity — used both by LoadAsync and by the mode toggle.</summary>
    private async Task RefreshBalancesAsync()
    {
        var items = IsPairwiseMode
            ? await BuildPairwiseItemsAsync()
            : BuildSimplifiedItems(await balancesRepository.GetForGroupAsync(groupId), membersById, myMemberId);

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
            var item = new MemberBalanceItem { Member = member, Initials = Initials(member.DisplayName) };
            if (row.Balance > 0)
            {
                item.IsOwed = true;
                item.IsSettled = false;
                item.AmountText = $"+${row.Balance:0.00}";
                item.CaptionText = "owes you";
            }
            else if (row.Balance < 0)
            {
                item.IsOwing = true;
                item.IsSettled = false;
                item.AmountText = $"-${Math.Abs(row.Balance):0.00}";
                item.CaptionText = "you owe";
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
        IEnumerable<GroupBalance> balances, IReadOnlyDictionary<Guid, Member> membersById, Guid? myMemberId)
    {
        var items = new List<MemberBalanceItem>();
        foreach (var transfer in DebtSimplifier.Simplify(balances.Select(b => (b.MemberId, b.Balance))))
        {
            if (!membersById.TryGetValue(transfer.FromMemberId, out var from)) continue;
            if (!membersById.TryGetValue(transfer.ToMemberId, out var to)) continue;

            if (transfer.FromMemberId == myMemberId)
            {
                items.Add(new MemberBalanceItem
                {
                    Member = to,
                    Initials = Initials(to.DisplayName),
                    IsOwing = true,
                    IsSettled = false,
                    AmountText = $"-${transfer.Amount:0.00}",
                    CaptionText = "you owe"
                });
            }
            else if (transfer.ToMemberId == myMemberId)
            {
                items.Add(new MemberBalanceItem
                {
                    Member = from,
                    Initials = Initials(from.DisplayName),
                    IsOwed = true,
                    IsSettled = false,
                    AmountText = $"+${transfer.Amount:0.00}",
                    CaptionText = "owes you"
                });
            }
            else
            {
                items.Add(new MemberBalanceItem
                {
                    Member = from,
                    Initials = Initials(from.DisplayName),
                    ToName = to.DisplayName,
                    IsNeutral = true,
                    AmountText = $"${transfer.Amount:0.00}",
                    CaptionText = $"pays {to.DisplayName}"
                });
            }
        }
        return items;
    }

    private static string FormatRelative(DateTime occurredAtUtc)
    {
        var elapsed = DateTime.UtcNow - occurredAtUtc;
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalDays < 1) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return occurredAtUtc.ToLocalTime().ToString("MMM d");
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    [RelayCommand]
    private async Task AddExpense() =>
        await Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}");

    /// <summary>Tapping an activity row opens it for editing — but only expenses; editing a
    /// settle-up payment isn't in scope here.</summary>
    [RelayCommand]
    private async Task OpenActivity(ActivityItem? item)
    {
        if (item?.ExpenseId is not { } expenseId) return;
        await Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&expenseId={expenseId}");
    }

    [RelayCommand]
    private async Task InvitePeople() =>
        await Shell.Current.GoToAsync(
            $"{AppConstants.Routes.JoinGroup}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}");

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();
}
