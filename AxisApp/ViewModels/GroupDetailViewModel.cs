using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>Group Detail's shell: header, the Expenses/Events tab selector, and the ⋮ overflow
/// menu (leave/transfer/dissolve/rename/view members/view recurring expenses) — everything that
/// applies to the group itself regardless of which tab is active. Split out of a single, growing
/// monolith (see /EVENTS_PLAN.md Milestone 2) once Events needed a genuinely separate vertical:
/// balances/recent-activity/Settle/Add-Expense now live in <see cref="GroupExpensesViewModel"/>
/// (<see cref="ExpensesVm"/>), events/RSVP/carpooling will live in <see cref="GroupEventsViewModel"/>
/// (<see cref="EventsVm"/>, still a placeholder as of Milestone 2).
///
/// Deliberately still does its own group/members fetch here (not just delegating to the child
/// view models) — IsGroupCreator/HasOtherMembers/TransferCandidates need it for the ⋮ menu
/// regardless of which tab is showing, so this can't be pushed down into ExpensesVm alone.</summary>
public partial class GroupDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IGroupsRepository groupsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IBalancesRepository balancesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Dictionary<Guid, Member> membersById = new();
    private Guid? myMemberId;
    private Group? currentGroup;

    public GroupExpensesViewModel ExpensesVm { get; }
    public GroupEventsViewModel EventsVm { get; }

    [ObservableProperty] private string groupName = "";

    /// <summary>Which of the two GroupDetailPage tabs is showing — false = Expenses (default),
    /// true = Events. A plain in-page flip (SelectExpensesTab/SelectEventsTab below), never a
    /// Shell navigation, so the back button always leaves the group rather than un-flipping the
    /// tab first — see /EVENTS_PLAN.md's "Decisions locked".</summary>
    [ObservableProperty] private bool isEventsTabSelected;

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

    public GroupDetailViewModel(
        IGroupsRepository groupsRepository,
        IMembersRepository membersRepository,
        IBalancesRepository balancesRepository,
        IAuthService authService,
        GroupExpensesViewModel expensesVm,
        GroupEventsViewModel eventsVm)
    {
        this.groupsRepository = groupsRepository;
        this.membersRepository = membersRepository;
        this.balancesRepository = balancesRepository;
        this.authService = authService;
        ExpensesVm = expensesVm;
        EventsVm = eventsVm;
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

    /// <summary>Loads the shared header/menu data (group + members, for IsGroupCreator/
    /// HasOtherMembers/TransferCandidates) alongside both tabs' own loads — in parallel, since
    /// they're independent fetches. Eagerly loading EventsVm even when the Expenses tab is the one
    /// showing (rather than lazily loading on first tab switch) matches Milestone 2's Expenses-tab
    /// treatment and keeps this simple; revisit if event data ever gets heavy enough to make eager
    /// loading wasteful.</summary>
    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        var loadGroup = groupsRepository.GetByIdAsync(groupId);
        var loadMembers = membersRepository.GetForGroupAsync(groupId);
        var loadExpensesTab = ExpensesVm.LoadAsync(groupId);
        var loadEventsTab = EventsVm.LoadAsync(groupId);
        await Task.WhenAll(loadGroup, loadMembers, loadExpensesTab, loadEventsTab);

        var members = loadMembers.Result;
        membersById = members.ToDictionary(m => m.Id);
        myMemberId = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)?.Id;
        currentGroup = loadGroup.Result;
        IsGroupCreator = currentGroup.CreatedBy == authService.CurrentAccountId;
        HasOtherMembers = members.Count > 1;
    });

    [RelayCommand]
    private void SelectExpensesTab() => IsEventsTabSelected = false;

    [RelayCommand]
    private void SelectEventsTab() => IsEventsTabSelected = true;

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
