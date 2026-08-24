using System.Collections.ObjectModel;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>A phantom group member with an outstanding invite pointed at them. There's no
/// dedicated "list invites for a group" repository method, so this treats "phantom member in the
/// group" as equivalent to "has a pending invite" for display purposes, which holds in practice —
/// a phantom is, by definition, not yet claimed.</summary>
public partial class PendingInviteItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";
}

/// <summary>
/// Handles both directions the handout's screen 4 covers: sharing/creating an invite for a
/// specific group (when navigated here with a groupId, e.g. from Group detail's overflow menu),
/// and redeeming someone else's invite code to join a group. Note: IInvitesRepository.CreateAsync
/// always mints a fresh invite - there's no "get the existing active one" lookup yet, so revisiting
/// this screen for the same group issues a new code each time rather than reusing one.
/// </summary>
public partial class JoinGroupViewModel : ObservableObject, IQueryAttributable
{
    private readonly IInvitesRepository invitesRepository;
    private readonly IMembersRepository membersRepository;

    private Guid? groupId;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private bool hasActiveGroup;
    [ObservableProperty] private string joinCodeInput = "";
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private ObservableCollection<PendingInviteItem> pendingInvites = [];
    [ObservableProperty] private bool isBusy;

    public JoinGroupViewModel(IInvitesRepository invitesRepository, IMembersRepository membersRepository)
    {
        this.invitesRepository = invitesRepository;
        this.membersRepository = membersRepository;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupName", out var nameValue))
            GroupName = Uri.UnescapeDataString(nameValue?.ToString() ?? "");

        if (query.TryGetValue("groupId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
        {
            groupId = id;
            HasActiveGroup = true;
            _ = LoadAsync();
        }
    }

    public async Task LoadAsync()
    {
        if (groupId is not { } id) return;
        IsBusy = true;
        try
        {
            var invite = await invitesRepository.CreateAsync(id);
            InviteCode = invite.Token;

            var members = await membersRepository.GetForGroupAsync(id);
            PendingInvites = new ObservableCollection<PendingInviteItem>(
                members.Where(m => m.IsPhantom)
                       .Select(m => new PendingInviteItem { Member = m, Initials = Initials(m.DisplayName) }));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyLink()
    {
        if (string.IsNullOrEmpty(InviteCode)) return;
        await Clipboard.Default.SetTextAsync(InviteCode);
        await Toast.Make("Invite code copied").Show(CancellationToken.None);
    }

    [RelayCommand]
    private async Task Share()
    {
        if (string.IsNullOrEmpty(InviteCode)) return;
        await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = $"Join my Axis group \"{GroupName}\" with code {InviteCode}",
            Title = "Invite to Axis"
        });
    }

    [RelayCommand]
    private async Task Resend(PendingInviteItem? item)
    {
        if (item is null || groupId is not { } id) return;
        var invite = await invitesRepository.CreateAsync(id, item.Member.Id);
        await Clipboard.Default.SetTextAsync(invite.Token);
        await Toast.Make($"New invite code for {item.Member.DisplayName} copied").Show(CancellationToken.None);
    }

    [RelayCommand]
    private async Task JoinByCode()
    {
        if (string.IsNullOrWhiteSpace(JoinCodeInput)) return;
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var joinedGroupId = await invitesRepository.RedeemAsync(JoinCodeInput.Trim());
            await Toast.Make("Joined group").Show(CancellationToken.None);
            await Shell.Current.GoToAsync($"{AppConstants.Routes.GroupDetails}?groupId={joinedGroupId}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
