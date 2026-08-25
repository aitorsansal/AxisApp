using System.Collections.ObjectModel;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row on the Groups list: a group plus its members (for the avatar stack) and the
/// current account's own net balance in it (for the "you're owed/you owe/Settled up" summary).</summary>
public partial class GroupListItem : ObservableObject
{
    public Group Group { get; init; } = null!;
    public List<string> AvatarInitials { get; init; } = [];
    public string MemberSummary { get; init; } = "";

    [ObservableProperty] private bool isOwed;
    [ObservableProperty] private bool isOwing;
    [ObservableProperty] private bool isSettled = true;
    [ObservableProperty] private string balanceAmountText = "Settled up";
    [ObservableProperty] private string balanceCaptionText = "";
}

public partial class GroupsViewModel : ObservableObject
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAuthService authService;

    [ObservableProperty] private ObservableCollection<GroupListItem> groups = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private string userInitials = "";

    public GroupsViewModel(
        IGroupsRepository groupsRepository,
        IMembersRepository membersRepository,
        IBalancesRepository balancesRepository,
        IAuthService authService)
    {
        this.groupsRepository = groupsRepository;
        this.membersRepository = membersRepository;
        this.balancesRepository = balancesRepository;
        this.authService = authService;

        UserInitials = Initials(authService.CurrentEmail ?? "?");
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var loadGroups = groupsRepository.GetMyGroupsAsync();
            var loadBalances = balancesRepository.GetMyBalancesAsync();
            await Task.WhenAll(loadGroups, loadBalances);

            var balancesByGroup = loadBalances.Result.ToDictionary(b => b.GroupId, b => b.Balance);

            var items = new List<GroupListItem>();
            foreach (var group in loadGroups.Result)
            {
                var members = await membersRepository.GetForGroupAsync(group.Id);
                var balance = balancesByGroup.GetValueOrDefault(group.Id, 0m);

                var item = new GroupListItem
                {
                    Group = group,
                    AvatarInitials = members.Take(4).Select(m => Initials(m.DisplayName)).ToList(),
                    MemberSummary = $"{members.Count} member{(members.Count == 1 ? "" : "s")}"
                };
                ApplyBalance(item, balance);
                items.Add(item);
            }

            Groups = new ObservableCollection<GroupListItem>(items);
            IsEmpty = Groups.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ApplyBalance(GroupListItem item, decimal balance)
    {
        if (balance > 0)
        {
            item.IsOwed = true;
            item.IsSettled = false;
            item.BalanceAmountText = $"+${balance:0.00}";
            item.BalanceCaptionText = "you're owed";
        }
        else if (balance < 0)
        {
            item.IsOwing = true;
            item.IsSettled = false;
            item.BalanceAmountText = $"-${Math.Abs(balance):0.00}";
            item.BalanceCaptionText = "you owe";
        }
    }

    [RelayCommand]
    private async Task OpenGroup(GroupListItem? item)
    {
        if (item is null) return;
        await Shell.Current.GoToAsync(
            $"{AppConstants.Routes.GroupDetails}?groupId={item.Group.Id}&groupName={Uri.EscapeDataString(item.Group.Name)}");
    }

    /// <summary>Shell.Current.DisplayPromptAsync crashes on Windows (fail-fast in
    /// Microsoft.UI.Xaml.dll — a known WinUI ContentDialog bug, not something fixable from app
    /// code: microsoft/microsoft-ui-xaml#10897), so this is a dedicated page instead of an
    /// inline prompt.</summary>
    [RelayCommand]
    private async Task NewGroup() => await Shell.Current.GoToAsync(AppConstants.Routes.NewGroup);

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    private static string Initials(string name)
    {
        var parts = name.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
