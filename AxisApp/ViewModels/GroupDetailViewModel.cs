using System.Collections.ObjectModel;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One other member's balance row in the group detail's Balances section.</summary>
public partial class MemberBalanceItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";

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
}

public partial class GroupDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IPaymentsRepository paymentsRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IAuthService authService;

    private Guid groupId;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<MemberBalanceItem> balances = [];
    [ObservableProperty] private ObservableCollection<ActivityItem> recentActivity = [];
    [ObservableProperty] private bool isBusy;

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
            _ = LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadBalances = balancesRepository.GetForGroupAsync(groupId);
            var loadPayments = paymentsRepository.GetForGroupAsync(groupId);
            var loadExpenses = expensesRepository.GetForGroupAsync(groupId);
            await Task.WhenAll(loadMembers, loadBalances, loadPayments, loadExpenses);

            var members = loadMembers.Result;
            var membersById = members.ToDictionary(m => m.Id);
            var myMemberId = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)?.Id;

            var balanceItems = new List<MemberBalanceItem>();
            foreach (var balance in loadBalances.Result.Where(b => b.MemberId != myMemberId))
            {
                if (!membersById.TryGetValue(balance.MemberId, out var member)) continue;
                var item = new MemberBalanceItem { Member = member, Initials = Initials(member.DisplayName) };
                ApplyBalance(item, balance.Balance);
                balanceItems.Add(item);
            }
            Balances = new ObservableCollection<MemberBalanceItem>(balanceItems);

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
                    RelativeDate = FormatRelative(expense.OccurredAt)
                });
            }

            RecentActivity = new ObservableCollection<ActivityItem>(activity.OrderByDescending(a => a.OccurredAt));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ApplyBalance(MemberBalanceItem item, decimal balance)
    {
        if (balance > 0)
        {
            item.IsOwed = true;
            item.IsSettled = false;
            item.AmountText = $"+${balance:0.00}";
            item.CaptionText = "owes you";
        }
        else if (balance < 0)
        {
            item.IsOwing = true;
            item.IsSettled = false;
            item.AmountText = $"-${Math.Abs(balance):0.00}";
            item.CaptionText = "you owe";
        }
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

    [RelayCommand]
    private async Task InvitePeople() =>
        await Shell.Current.GoToAsync(
            $"{AppConstants.Routes.JoinGroup}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}");

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();
}
