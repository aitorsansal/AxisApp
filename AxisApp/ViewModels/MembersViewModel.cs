using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the members roster — a plain display wrapper, not an editable model. Name/
/// Initials/AvatarUrl are already alias-resolved (see Services/MemberDisplay.cs) by the time this
/// is built. Caption is "You" for the viewer's own row, "Phantom member" for an unclaimed one,
/// empty for anyone else's claimed row (nothing useful to say about them).</summary>
public partial class MemberRowItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public bool IsYou { get; init; }
    public bool IsPhantom { get; init; }
}

public partial class MembersViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IMembersRepository membersRepository;
    private readonly IGroupsRepository groupsRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private MemberRowItem? renamingItem;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<MemberRowItem> members = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isRenameOverlayOpen;
    [ObservableProperty] private string renameInput = "";

    public MembersViewModel(
        IMembersRepository membersRepository,
        IGroupsRepository groupsRepository,
        IAliasesRepository aliasesRepository,
        IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.groupsRepository = groupsRepository;
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
            _ = LoadAsync();
        }
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadAliases = aliasesRepository.GetMyAliasesAsync();
            await Task.WhenAll(loadMembers, loadAliases);

            var aliases = loadAliases.Result;
            var myAccountId = authService.CurrentAccountId;

            Members = new ObservableCollection<MemberRowItem>(
                loadMembers.Result
                    .OrderBy(m => MemberDisplay.Name(m, aliases))
                    .Select(m => new MemberRowItem
                    {
                        Member = m,
                        Name = MemberDisplay.Name(m, aliases),
                        Initials = MemberDisplay.Initials(m, aliases),
                        AvatarUrl = MemberDisplay.AvatarUrl(m),
                        IsYou = m.AccountId == myAccountId,
                        IsPhantom = m.IsPhantom
                    }));
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task InvitePeople() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.JoinGroup}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}"));

    /// <summary>Opens an inline rename overlay (MembersPage.xaml) rather than
    /// Shell.Current.DisplayPromptAsync — that API is a known WinUI crash on Windows (fail-fast in
    /// Microsoft.UI.Xaml.dll, microsoft/microsoft-ui-xaml#10897; see GroupsViewModel.NewGroup's
    /// remarks, which is why NewGroupPage exists as a dedicated screen instead of a prompt too).
    /// DisplayAlert (used elsewhere in this ViewModel and throughout GroupDetailViewModel) is a
    /// different ContentDialog configuration and has been confirmed safe repeatedly this session —
    /// only the text-input prompt variant is the problem.</summary>
    [RelayCommand]
    private void OpenRenameOverlay(MemberRowItem? item)
    {
        if (item is null) return;
        renamingItem = item;
        RenameInput = item.Name;
        IsRenameOverlayOpen = true;
    }

    [RelayCommand]
    private void CancelRenameOverlay()
    {
        renamingItem = null;
        IsRenameOverlayOpen = false;
    }

    /// <summary>Submitting the member's real DisplayName (or an empty field) resets to no alias
    /// rather than storing a pointless override — same behavior the old DisplayPromptAsync version
    /// had.</summary>
    [RelayCommand]
    private Task ConfirmRename() => RunSafeAsync(async () =>
    {
        if (renamingItem is not { } item) return;
        IsRenameOverlayOpen = false;

        var trimmed = RenameInput.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == item.Member.DisplayName)
            await aliasesRepository.ClearAliasAsync(item.Member.Id);
        else
            await aliasesRepository.SetAliasAsync(item.Member.Id, trimmed);

        renamingItem = null;
        await LoadAsync();
    });

    /// <summary>Only ever offered for a phantom row (see MembersPage.xaml) — a claimed member can
    /// only remove themselves via Leave on GroupDetailPage, never be removed by someone else's
    /// action. The creator/balance guards live server-side in remove_group_member() (see
    /// schema.sql), so a rejection (e.g. a nonzero balance) surfaces as ErrorMessage.</summary>
    [RelayCommand]
    private Task RemoveMember(MemberRowItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;

        var loc = LocalizationResourceManager.Instance;
        var confirmed = await Shell.Current.DisplayAlert(
            loc.Format("Members_RemoveConfirmTitle", item.Name),
            loc["Members_RemoveConfirmMessage"],
            loc["Common_Yes"],
            loc["Common_Cancel"]);
        if (!confirmed) return;

        await groupsRepository.RemoveMemberAsync(groupId, item.Member.Id);
        await LoadAsync();
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync();
}
