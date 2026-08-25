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

public partial class GroupsViewModel : BaseViewModel
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAuthService authService;

    [ObservableProperty] private ObservableCollection<GroupListItem> groups = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private string userInitials = "";
    [ObservableProperty] private string userEmail = "";
    [ObservableProperty] private bool isAccountMenuOpen;

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
        UserEmail = authService.CurrentEmail ?? "";
    }

    /// <summary>Wraps its own body in RunSafeAsync rather than relying on callers to — this is
    /// called both as a [RelayCommand] (Refresh) and directly, fire-and-forget, from
    /// GroupsPage.OnAppearing, and either path hitting an unhandled exception (e.g. the
    /// transient Supabase "JWT issued at future" clock-skew rejection seen repeatedly during
    /// testing) needs to degrade to an error message, not take the app down.</summary>
    public Task LoadAsync() => RunSafeAsync(async () =>
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
    });

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
    private Task OpenGroup(GroupListItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        await Shell.Current.GoToAsync(
            $"{AppConstants.Routes.GroupDetails}?groupId={item.Group.Id}&groupName={Uri.EscapeDataString(item.Group.Name)}");
    });

    /// <summary>Shell.Current.DisplayPromptAsync crashes on Windows (fail-fast in
    /// Microsoft.UI.Xaml.dll — a known WinUI ContentDialog bug, not something fixable from app
    /// code: microsoft/microsoft-ui-xaml#10897), so this is a dedicated page instead of an
    /// inline prompt.</summary>
    [RelayCommand]
    private Task NewGroup() => RunSafeAsync(() => Shell.Current.GoToAsync(AppConstants.Routes.NewGroup));

    /// <summary>The only other way onto this screen is GroupDetailViewModel's overflow menu,
    /// which requires already being in a group — so a brand-new account with zero groups had no
    /// way to redeem an invite code at all. JoinGroupPage/ViewModel already handle a missing
    /// groupId query param fine (HasActiveGroup just stays false), so this only needed a route in.</summary>
    [RelayCommand]
    private Task JoinGroup() => RunSafeAsync(() => Shell.Current.GoToAsync(AppConstants.Routes.JoinGroup));

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private void ToggleAccountMenu() => IsAccountMenuOpen = !IsAccountMenuOpen;

    /// <summary>Real profile content (name/photo/whatever) isn't designed yet — the menu still
    /// shows the row so the shape matches the eventual account menu, it just doesn't navigate
    /// anywhere yet. Revisit once there's an actual Profile screen.</summary>
    [RelayCommand]
    private void OpenProfile() => IsAccountMenuOpen = false;

    [RelayCommand]
    private Task Logout() => RunSafeAsync(async () =>
    {
        IsAccountMenuOpen = false;
        await authService.SignOutAsync();
        await Shell.Current.GoToAsync(AppConstants.Routes.Login);
    });

    private static string Initials(string name)
    {
        var parts = name.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
