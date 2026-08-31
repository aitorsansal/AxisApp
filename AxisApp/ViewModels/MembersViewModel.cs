using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the members roster — a plain display wrapper, not an editable model.
/// Caption is "You" for the viewer's own row, "Phantom member" for an unclaimed one, empty for
/// anyone else's claimed row (nothing useful to say about them).</summary>
public partial class MemberRowItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";
    public bool IsYou { get; init; }
    public bool IsPhantom { get; init; }
}

public partial class MembersViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IMembersRepository membersRepository;
    private readonly IGroupsRepository groupsRepository;
    private readonly IAuthService authService;

    private Guid groupId;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<MemberRowItem> members = [];
    [ObservableProperty] private bool isBusy;

    public MembersViewModel(IMembersRepository membersRepository, IGroupsRepository groupsRepository, IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.groupsRepository = groupsRepository;
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
            var members = await membersRepository.GetForGroupAsync(groupId);
            var myAccountId = authService.CurrentAccountId;

            Members = new ObservableCollection<MemberRowItem>(
                members.OrderBy(m => m.DisplayName).Select(m => new MemberRowItem
                {
                    Member = m,
                    Initials = Initials(m.DisplayName),
                    IsYou = m.AccountId == myAccountId,
                    IsPhantom = m.IsPhantom
                }));
        }
        finally
        {
            IsBusy = false;
        }
    });

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    [RelayCommand]
    private Task InvitePeople() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.JoinGroup}?groupId={groupId}&groupName={Uri.EscapeDataString(GroupName)}"));

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
            loc.Format("Members_RemoveConfirmTitle", item.Member.DisplayName),
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
