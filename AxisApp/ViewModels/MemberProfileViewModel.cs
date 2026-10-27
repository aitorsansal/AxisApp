using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One expense between "you" and this member, with whose part the amount is.</summary>
public sealed class MemberProfileExpenseItem
{
    public Guid GroupId { get; init; }
    public Guid ExpenseId { get; init; }
    public string Description { get; init; } = "";
    public string DateText { get; init; } = "";
    public string PartText { get; init; } = "";
}

/// <summary>One shared group's card: the pairwise balance, the three lines it's made of (what I
/// paid for them, what they paid for me, settle-ups — see Models/PairBreakdown.cs), and the
/// expenses between the two of us.</summary>
public partial class MemberProfileGroupItem : ObservableObject
{
    /// <summary>Collapsed list length; "Show all" reveals the rest.</summary>
    public const int CollapsedExpenseCount = 10;

    public Guid GroupId { get; init; }
    public string GroupName { get; init; } = "";
    public bool IsOwed { get; init; }
    public bool IsOwing { get; init; }
    public string AmountText { get; init; } = "";
    public string CaptionText { get; init; } = "";

    public string PaidForThemLabel { get; init; } = "";
    public string PaidForThemText { get; init; } = "";
    public string PaidForYouLabel { get; init; } = "";
    public string PaidForYouText { get; init; } = "";
    /// <summary>"Settle-ups", plus who paid whom on net when there were any.</summary>
    public string SettlementsLabel { get; init; } = "";
    public string SettlementsText { get; init; } = "";

    public IReadOnlyList<MemberProfileExpenseItem> AllExpenses { get; init; } = [];
    public bool HasExpenses => AllExpenses.Count > 0;
    public string ShowAllText { get; init; } = "";

    [ObservableProperty] private ObservableCollection<MemberProfileExpenseItem> visibleExpenses = [];
    [ObservableProperty] private bool hasHiddenExpenses;

    [RelayCommand]
    private void ShowAll()
    {
        VisibleExpenses = new ObservableCollection<MemberProfileExpenseItem>(AllExpenses);
        HasHiddenExpenses = false;
    }
}

/// <summary>"You and X", opened by tapping a member row on MembersPage. members are a global table
/// (see CLAUDE.md's "members vs accounts"), so the same member_id recurs across every group that
/// person is in — this walks the viewer's own groups, keeps the ones the target member is also in,
/// and for each one explains the pairwise balance from its parts rather than the group's whole
/// aggregate. Rewritten 2026-10-27: the old card listed per-category totals with no label, mixing
/// both directions and leaving out settle-ups, so the numbers never connected to the balance.</summary>
public partial class MemberProfileViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAliasesRepository aliasesRepository;

    private Guid targetMemberId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private string memberName = "";

    [ObservableProperty] private string initials = "";
    [ObservableProperty] private string? avatarUrl;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private ObservableCollection<MemberProfileGroupItem> sharedGroups = [];
    [ObservableProperty] private bool hasSharedGroups;
    private bool hasLoadedOnce;

    public string PageTitle => LocalizationResourceManager.Instance.Format("MemberProfile_Title", MemberName);

    public MemberProfileViewModel(
        IGroupsRepository groupsRepository,
        IMembersRepository membersRepository,
        IExpensesRepository expensesRepository,
        IBalancesRepository balancesRepository,
        IAliasesRepository aliasesRepository)
    {
        this.groupsRepository = groupsRepository;
        this.membersRepository = membersRepository;
        this.expensesRepository = expensesRepository;
        this.balancesRepository = balancesRepository;
        this.aliasesRepository = aliasesRepository;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("memberName", out var nameValue))
            MemberName = Uri.UnescapeDataString(nameValue?.ToString() ?? "");

        if (query.TryGetValue("memberId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
        {
            targetMemberId = id;
            _ = LoadAsync();
        }
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        var isFirstLoad = !hasLoadedOnce;
        IsInitialLoading = isFirstLoad;
        try
        {
            async Task DoLoad()
            {
                var loadMyGroups = groupsRepository.GetMyGroupsAsync();
                var loadAliases = aliasesRepository.GetMyAliasesAsync();
                var loadMyMember = membersRepository.GetMyMemberAsync();
                var loadTargetMember = membersRepository.GetByIdAsync(targetMemberId);
                await Task.WhenAll(loadMyGroups, loadAliases, loadMyMember, loadTargetMember);

                var aliases = loadAliases.Result;
                var myMemberId = loadMyMember.Result?.Id;
                if (loadTargetMember.Result is { } targetMember)
                {
                    MemberName = MemberDisplay.Name(targetMember, aliases);
                    Initials = MemberDisplay.Initials(targetMember, aliases);
                    AvatarUrl = MemberDisplay.AvatarUrl(targetMember);
                }

                var items = new List<MemberProfileGroupItem>();
                if (myMemberId is { } me)
                {
                    foreach (var group in loadMyGroups.Result)
                    {
                        var item = await BuildGroupItemAsync(group, me);
                        if (item is not null) items.Add(item);
                    }
                }

                SharedGroups = new ObservableCollection<MemberProfileGroupItem>(items);
                HasSharedGroups = items.Count > 0;
            }

            if (isFirstLoad)
                await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), DoLoad);
            else
                await DoLoad();
        }
        finally
        {
            IsBusy = false;
            IsInitialLoading = false;
            hasLoadedOnce = true;
        }
    });

    private static string Money(string symbol, decimal value) => $"{symbol}{value:0.00}";

    /// <summary>"+€19,50" / "−€5,00", or a plain "€0,00" for zero.</summary>
    private static string SignedMoney(string symbol, decimal value) =>
        value > 0 ? $"+{Money(symbol, value)}" : value < 0 ? $"−{Money(symbol, -value)}" : Money(symbol, 0);

    /// <summary>Null if the target member isn't actually in this group — not every one of "my"
    /// groups is shared with them.</summary>
    private async Task<MemberProfileGroupItem?> BuildGroupItemAsync(Group group, Guid myMemberId)
    {
        var members = await membersRepository.GetForGroupAsync(group.Id);
        if (!members.Any(m => m.Id == targetMemberId)) return null;

        var loc = LocalizationResourceManager.Instance;
        var symbol = AppConstants.Currencies.SymbolFor(group.Currency);

        // The header keeps reading the server's balance (my_pairwise_balances, same as Group
        // Detail); PairBreakdown sums the same edges, so its lines add up to it.
        var pairwise = await balancesRepository.GetMyPairwiseForGroupAsync(group.Id);
        var balance = pairwise.FirstOrDefault(p => p.OtherMemberId == targetMemberId)?.Balance ?? 0;

        var expenses = await expensesRepository.GetAllForGroupAsync(group.Id);
        var shares = await expensesRepository.GetSharesForExpensesAsync(expenses.Select(e => e.Id).ToList());
        var breakdown = PairBreakdown.Compute(
            expenses.Select(e => new PairExpense(e.Id, e.PaidByMemberId, e.IsSettlement, e.Description, e.Category, e.OccurredAt)),
            shares.Select(s => new PairShare(s.ExpenseId, s.MemberId, s.ShareAmountInGroupCurrency)),
            myMemberId, targetMemberId);

        var all = breakdown.Expenses.Select(line => new MemberProfileExpenseItem
        {
            GroupId = group.Id,
            ExpenseId = line.ExpenseId,
            Description = string.IsNullOrWhiteSpace(line.Description) ? CategoryDisplay.Label(line.Category) : line.Description,
            DateText = DateFormats.ShortDate(line.OccurredAt.ToLocalTime(), includeYear: line.OccurredAt.Year != DateTime.Now.Year),
            PartText = loc.Format(line.IPaid ? "MemberProfile_TheirPart" : "MemberProfile_YourPart", Money(symbol, line.Amount))
        }).ToList();

        return new MemberProfileGroupItem
        {
            GroupId = group.Id,
            GroupName = group.Name,
            IsOwed = balance > 0,
            IsOwing = balance < 0,
            AmountText = balance > 0 ? $"+{Money(symbol, balance)}" : balance < 0 ? $"-{Money(symbol, -balance)}" : Money(symbol, 0),
            CaptionText = balance > 0 ? loc["GroupDetail_OwesYou"] : balance < 0 ? loc["GroupDetail_YouOwe"] : loc["Common_SettledUp"],
            PaidForThemLabel = loc.Format("MemberProfile_PaidForThem", MemberName),
            PaidForThemText = SignedMoney(symbol, breakdown.PaidForThem),
            PaidForYouLabel = loc.Format("MemberProfile_PaidForYou", MemberName),
            PaidForYouText = SignedMoney(symbol, -breakdown.PaidForMe),
            SettlementsText = SignedMoney(symbol, breakdown.SettlementsNet),
            SettlementsLabel = breakdown.SettlementsNet == 0
                ? loc["MemberProfile_Settlements"]
                : $"{loc["MemberProfile_Settlements"]} ({loc.Format(breakdown.SettlementsNet < 0 ? "MemberProfile_TheyPaidYou" : "MemberProfile_YouPaidThem", MemberName)})",
            AllExpenses = all,
            VisibleExpenses = new ObservableCollection<MemberProfileExpenseItem>(all.Take(MemberProfileGroupItem.CollapsedExpenseCount)),
            HasHiddenExpenses = all.Count > MemberProfileGroupItem.CollapsedExpenseCount,
            ShowAllText = loc.Format("MemberProfile_ShowAll", all.Count)
        };
    }

    [RelayCommand]
    private Task OpenExpense(MemberProfileExpenseItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        await Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={item.GroupId}&expenseId={item.ExpenseId}");
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync();
}
