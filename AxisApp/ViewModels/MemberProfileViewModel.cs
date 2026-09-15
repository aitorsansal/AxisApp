using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One category's share of the money that's moved between "you" and this member within
/// one shared group.</summary>
public partial class MemberProfileCategoryItem : ObservableObject
{
    public string Label { get; init; } = "";
    public string AmountText { get; init; } = "";
}

/// <summary>One shared group's card on the member profile screen: current pairwise balance
/// (my_pairwise_balances, same source GroupExpensesView's Detailed toggle uses) plus a category
/// breakdown of every non-settlement expense actually between the two of you in that group.</summary>
public partial class MemberProfileGroupItem : ObservableObject
{
    public Guid GroupId { get; init; }
    public string GroupName { get; init; } = "";
    public bool IsOwed { get; init; }
    public bool IsOwing { get; init; }
    public string AmountText { get; init; } = "";
    public string CaptionText { get; init; } = "";
    public ObservableCollection<MemberProfileCategoryItem> CategoryBreakdown { get; init; } = [];
    public bool HasCategoryBreakdown => CategoryBreakdown.Count > 0;
}

/// <summary>"You and X" — merges the mini-profile and cross-group ideas from the v2 Stats design
/// discussion into one screen. Opened by tapping a member row on MembersPage (not a chart bar —
/// LiveCharts data-point tap-to-navigate was ruled out as extra risk for what a plain list already
/// does simply). members are a global table (see CLAUDE.md's "members vs accounts"), so the same
/// member_id genuinely recurs across every group that person is in — this walks the viewer's own
/// groups, keeps the ones the target member is also in, and for each one pulls the real pairwise
/// edges (same edge definition my_pairwise_balances uses: payer vs. share-holder) rather than the
/// group's whole aggregate, so "you and X" stays about the two of you specifically even in a
/// larger group. No new repository methods or SQL — entirely composed from IGroupsRepository/
/// IMembersRepository/IExpensesRepository/IBalancesRepository calls that already existed.</summary>
public partial class MemberProfileViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAliasesRepository aliasesRepository;

    private Guid targetMemberId;

    [ObservableProperty] private string memberName = "";
    [ObservableProperty] private string initials = "";
    [ObservableProperty] private string? avatarUrl;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private ObservableCollection<MemberProfileGroupItem> sharedGroups = [];
    [ObservableProperty] private bool hasSharedGroups;
    private bool hasLoadedOnce;

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
                        var item = await BuildGroupItemAsync(group, me, aliases);
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

    /// <summary>Null if the target member isn't actually in this group — not every one of "my"
    /// groups is shared with them.</summary>
    private async Task<MemberProfileGroupItem?> BuildGroupItemAsync(Group group, Guid myMemberId, IReadOnlyDictionary<Guid, string> aliases)
    {
        var members = await membersRepository.GetForGroupAsync(group.Id);
        if (!members.Any(m => m.Id == targetMemberId)) return null;

        var loc = LocalizationResourceManager.Instance;
        var groupSymbol = AppConstants.Currencies.SymbolFor(group.Currency);

        var pairwise = await balancesRepository.GetMyPairwiseForGroupAsync(group.Id);
        var balance = pairwise.FirstOrDefault(p => p.OtherMemberId == targetMemberId)?.Balance ?? 0;

        var isOwed = balance > 0;
        var isOwing = balance < 0;
        var amountText = isOwed
            ? $"+{groupSymbol}{balance:0.00}"
            : isOwing
                ? $"-{groupSymbol}{Math.Abs(balance):0.00}"
                : $"{groupSymbol}0.00";
        var captionText = isOwed
            ? loc["GroupDetail_OwesYou"]
            : isOwing
                ? loc["GroupDetail_YouOwe"]
                : loc["Common_SettledUp"];

        var expenses = await expensesRepository.GetAllForGroupAsync(group.Id);
        var expenseIds = expenses.Select(e => e.Id).ToList();
        var shares = await expensesRepository.GetSharesForExpensesAsync(expenseIds);
        var sharesByExpense = shares.ToLookup(s => s.ExpenseId);

        // Same edge definition as my_pairwise_balances (schema.sql): one of us paid, the other
        // holds a share — that's what "between us" means, not just any expense in a group we
        // both happen to belong to.
        (string Category, decimal Amount)? RelevantEdge(Expense e)
        {
            if (e.PaidByMemberId == myMemberId)
            {
                var theirShare = sharesByExpense[e.Id].FirstOrDefault(s => s.MemberId == targetMemberId);
                return theirShare is null ? null : (e.Category, theirShare.ShareAmountInGroupCurrency);
            }
            if (e.PaidByMemberId == targetMemberId)
            {
                var myShare = sharesByExpense[e.Id].FirstOrDefault(s => s.MemberId == myMemberId);
                return myShare is null ? null : (e.Category, myShare.ShareAmountInGroupCurrency);
            }
            return null;
        }

        var categoryBreakdown = expenses
            .Where(e => !e.IsSettlement)
            .Select(RelevantEdge)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Total)
            .Select(x => new MemberProfileCategoryItem
            {
                Label = CategoryDisplay.Label(x.Category),
                AmountText = $"{groupSymbol}{x.Total:0.00}"
            })
            .ToList();

        return new MemberProfileGroupItem
        {
            GroupId = group.Id,
            GroupName = group.Name,
            IsOwed = isOwed,
            IsOwing = isOwing,
            AmountText = amountText,
            CaptionText = captionText,
            CategoryBreakdown = new ObservableCollection<MemberProfileCategoryItem>(categoryBreakdown)
        };
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();
}
