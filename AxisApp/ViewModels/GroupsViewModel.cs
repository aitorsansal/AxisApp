using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

public class GroupMemberAvatar 
{
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }
}

/// <summary>One row on the Groups list: a group plus its members (for the avatar stack) and the
/// current account's own net balance in it (for the "you're owed/you owe/Settled up" summary).</summary>
public partial class GroupListItem : ObservableObject
{
    public Group Group { get; init; } = null!;
    public List<GroupMemberAvatar> MemberAvatars { get; init; } = [];
    public string MemberSummary { get; init; } = "";

    /// <summary>Resolved from Group.Color/Icon at load time (AccentPalettes/GroupIcons) so the
    /// row template just renders a GroupIconCircle without knowing either lookup exists.</summary>
    public Color IconBackgroundColor { get; init; } = Colors.Transparent;
    public Color IconForegroundColor { get; init; } = Colors.White;
    public string? IconGlyph { get; init; }
    public string GroupInitials { get; init; } = "";

    [ObservableProperty] private bool isOwed;
    [ObservableProperty] private bool isOwing;
    [ObservableProperty] private bool isSettled = true;
    [ObservableProperty] private string balanceAmountText = LocalizationResourceManager.Instance["Common_SettledUp"];
    [ObservableProperty] private string balanceCaptionText = "";
}

public partial class GroupsViewModel : BaseViewModel
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAuthService authService;
    private readonly IPushRegistrationService pushRegistrationService;

    [ObservableProperty] private ObservableCollection<GroupListItem> groups = [];
    [ObservableProperty] private bool isBusy;
    /// <summary>True only while the very first LoadAsync is in flight — kept separate from
    /// IsBusy (which also drives the pull-to-refresh spinner on every later call, e.g. returning
    /// from a child page) so the skeleton and its minimum-visible-duration padding only apply to
    /// the cold load, not every refresh.</summary>
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private bool isEmpty;
    [ObservableProperty] private string userInitials = "";
    [ObservableProperty] private string userEmail = "";
    [ObservableProperty] private string? myAvatarUrl;
    [ObservableProperty] private bool isAccountMenuOpen;

    public GroupsViewModel(
        IGroupsRepository groupsRepository,
        IMembersRepository membersRepository,
        IBalancesRepository balancesRepository,
        IAuthService authService,
        IPushRegistrationService pushRegistrationService)
    {
        this.groupsRepository = groupsRepository;
        this.membersRepository = membersRepository;
        this.balancesRepository = balancesRepository;
        this.authService = authService;
        this.pushRegistrationService = pushRegistrationService;

        UserInitials = Initials(authService.CurrentEmail ?? "?");
        UserEmail = authService.CurrentEmail ?? "";
    }

    /// <summary>Wraps its own body in RunSafeAsync rather than relying on callers to — this is
    /// called both as a [RelayCommand] (Refresh) and directly, fire-and-forget, from
    /// GroupsPage.OnAppearing, and either path hitting an unhandled exception (e.g. the
    /// transient Supabase "JWT issued at future" clock-skew rejection seen repeatedly during
    /// testing) needs to degrade to an error message, not take the app down.</summary>
    private bool hasLoadedOnce;

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        var isFirstLoad = !hasLoadedOnce;
        IsInitialLoading = isFirstLoad;
        try
        {
            async Task DoLoad()
            {
                var loadGroups = groupsRepository.GetMyGroupsAsync();
                var loadBalances = balancesRepository.GetMyBalancesAsync();
                var loadMyMember = membersRepository.GetMyMemberAsync();
                await Task.WhenAll(loadGroups, loadBalances, loadMyMember);

                var myMember = loadMyMember.Result;
                MyAvatarUrl = myMember is null ? null : MemberDisplay.AvatarUrl(myMember);

                var balancesByGroup = loadBalances.Result.ToDictionary(b => b.GroupId, b => b.Balance);

                var items = new List<GroupListItem>();
                foreach (var group in loadGroups.Result)
                {
                    var members = await membersRepository.GetForGroupAsync(group.Id);
                    var balance = balancesByGroup.GetValueOrDefault(group.Id, 0m);

                    var loc = LocalizationResourceManager.Instance;
                    var item = new GroupListItem
                    {
                        Group = group,
                        MemberAvatars = members.Take(4).Select(m => new GroupMemberAvatar()
                        {
                          Initials  = Initials(m.DisplayName),
                          AvatarUrl = MemberDisplay.AvatarUrl(m)
                        }).ToList(),
                        MemberSummary = loc.Format(
                            members.Count == 1 ? "Groups_MemberSingular" : "Groups_MemberPlural", members.Count),
                        IconBackgroundColor = AccentPalettes.ColorFor(group.Color),
                        IconForegroundColor = AccentPalettes.TextOnAccentFor(group.Color),
                        IconGlyph = AppConstants.GroupIcons.GlyphFor(group.Icon),
                        GroupInitials = Initials(group.Name),
                    };
                    ApplyBalance(item, balance, AppConstants.Currencies.SymbolFor(group.Currency));
                    items.Add(item);
                }

                Groups = new ObservableCollection<GroupListItem>(ApplySavedOrder(items));
                IsEmpty = Groups.Count == 0;

                // Fire-and-forget, deliberately not awaited: it may show a permission prompt, and
                // this screen's own IsBusy spinner shouldn't sit up waiting on the user answering it.
                // IPushRegistrationService.RegisterAsync never throws (see its remarks), and this runs
                // on every Groups load — sign-in, sign-up, Google sign-in, and a restored session on
                // relaunch all land here, so this is the one choke point that covers every path
                // without duplicating the call across LoginViewModel/SplashPage.
                _ = pushRegistrationService.RegisterAsync();
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

    private static void ApplyBalance(GroupListItem item, decimal balance, string groupSymbol)
    {
        if (balance > 0)
        {
            item.IsOwed = true;
            item.IsSettled = false;
            item.BalanceAmountText = $"+{groupSymbol}{balance:0.00}";
            item.BalanceCaptionText = LocalizationResourceManager.Instance["Groups_YoureOwed"];
        }
        else if (balance < 0)
        {
            item.IsOwing = true;
            item.IsSettled = false;
            item.BalanceAmountText = $"-{groupSymbol}{Math.Abs(balance):0.00}";
            item.BalanceCaptionText = LocalizationResourceManager.Instance["Groups_YouOwe"];
        }
    }

    [RelayCommand]
    private Task OpenGroup(GroupListItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        await Task.Delay(Controls.Juice.PressReleaseSettleMs);
        await Shell.Current.GoToAsync(
            $"{AppConstants.Routes.GroupDetails}?groupId={item.Group.Id}&groupName={Uri.EscapeDataString(item.Group.Name)}");
    });

    /// <summary>Shell.Current.DisplayPromptAsync crashes on Windows (fail-fast in
    /// Microsoft.UI.Xaml.dll — a known WinUI ContentDialog bug, not something fixable from app
    /// code: microsoft/microsoft-ui-xaml#10897), so this is a dedicated page instead of an
    /// inline prompt.</summary>
    [RelayCommand]
    private Task NewGroup() => RunSafeAsync(() => Shell.Current.GoToAsync(AppConstants.Routes.NewGroup));

    /// <summary>The only other way to reach a group is MembersViewModel's "invite people" action,
    /// which requires already being in a group — so a brand-new account with zero groups had no
    /// way to redeem an invite code at all. This is the route in: JoinGroupPage is the dedicated
    /// "redeem someone else's code" screen (InviteToGroupPage is the separate "share my own group's
    /// invite" one).</summary>
    [RelayCommand]
    private Task JoinGroup() => RunSafeAsync(() => Shell.Current.GoToAsync(AppConstants.Routes.JoinGroup));

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    /// <summary>Sorts freshly-loaded items (already in created_at order, see
    /// SupabaseGroupsRepository.GetMyGroupsAsync) by the saved drag order, if any — a group not
    /// in the saved list (new since it was last saved, or this account predates the feature)
    /// falls through to int.MaxValue, so LINQ's stable OrderBy leaves it after every listed group
    /// in its original created_at position rather than jumping to an arbitrary spot.</summary>
    private static List<GroupListItem> ApplySavedOrder(List<GroupListItem> items)
    {
        var saved = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.GroupOrder, "");
        if (string.IsNullOrEmpty(saved)) return items;

        var order = saved.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        return items
            .OrderBy(i => order.IndexOf(i.Group.Id) is var idx && idx >= 0 ? idx : int.MaxValue)
            .ToList();
    }

    private void PersistGroupOrder() =>
        Microsoft.Maui.Storage.Preferences.Default.Set(
            AppConstants.Preferences.GroupOrder, string.Join(',', Groups.Select(g => g.Group.Id)));

    /// <summary>Set by BeginDragGroup (the row the drag started on), consumed and cleared by
    /// DropGroup (the row it was dropped on) — see GroupsPage.xaml's per-row
    /// DragGestureRecognizer/DropGestureRecognizer, both bound to the row's own GroupListItem.</summary>
    private GroupListItem? draggedItem;

    [RelayCommand]
    private void BeginDragGroup(GroupListItem item) => draggedItem = item;

    [RelayCommand]
    private void DropGroup(GroupListItem targetItem)
    {
        if (draggedItem is null) return;
        var dragged = draggedItem;
        draggedItem = null;
        if (dragged == targetItem) return;

        var oldIndex = Groups.IndexOf(dragged);
        var newIndex = Groups.IndexOf(targetItem);
        if (oldIndex < 0 || newIndex < 0) return;

        Groups.Move(oldIndex, newIndex);
        PersistGroupOrder();
    }

    [RelayCommand]
    private void ToggleAccountMenu() => IsAccountMenuOpen = !IsAccountMenuOpen;

    /// <summary>Photo/language/display-name/etc. all moved to ProfilePage — the account menu now
    /// just links there. MyAvatarUrl (the header circle) still refreshes on its own via LoadAsync,
    /// which GroupsPage.OnAppearing re-runs every time this page is navigated back to, e.g. from
    /// Profile after a photo change.</summary>
    [RelayCommand]
    private Task OpenProfile() => RunSafeAsync(() =>
    {
        IsAccountMenuOpen = false;
        return Shell.Current.GoToAsync(AppConstants.Routes.Profile);
    });

    [RelayCommand]
    private Task Logout() => RunSafeAsync(async () =>
    {
        IsAccountMenuOpen = false;
        // Must run before SignOutAsync, while the session that authorizes deleting this device's
        // own device_tokens row is still valid — otherwise a device later reused by a second
        // account would keep receiving the first account's group notifications.
        await pushRegistrationService.UnregisterAsync();
        await authService.SignOutAsync();
        await Shell.Current.GoToAsync(AppConstants.Routes.Login);
    });

    private static string Initials(string name)
    {
        var parts = name.Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
