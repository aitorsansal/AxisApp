using System.Collections.ObjectModel;
using AxisApp.Localization;
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

/// <summary>A name-match suggestion shown before committing to a brand-new phantom row — see
/// JoinGroupViewModel.OnNewPhantomNameChanged. Real-account matches are display-only: this app
/// never adds a claimed member to a group on someone else's say-so, only phantoms get a "Link"
/// action, since the only legitimate way a real account joins a group is redeeming an invite
/// themselves.</summary>
public partial class MemberMatchItem : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";
    public bool IsPhantom => Member.IsPhantom;
    public bool IsRealAccount => !Member.IsPhantom;
}

/// <summary>
/// Handles both directions the handout's screen 4 covers: sharing/creating an invite for a
/// specific group (when navigated here with a groupId, e.g. from Group detail's overflow menu),
/// and redeeming someone else's invite code to join a group. Note: IInvitesRepository.CreateAsync
/// always mints a fresh invite - there's no "get the existing active one" lookup yet, so revisiting
/// this screen for the same group issues a new code each time rather than reusing one.
/// </summary>
public partial class JoinGroupViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IInvitesRepository invitesRepository;
    private readonly IMembersRepository membersRepository;

    private Guid? groupId;
    private List<Guid> currentGroupMemberIds = [];

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private bool hasActiveGroup;
    [ObservableProperty] private string joinCodeInput = "";
    [ObservableProperty] private ObservableCollection<PendingInviteItem> pendingInvites = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string newPhantomName = "";
    [ObservableProperty] private ObservableCollection<MemberMatchItem> nameMatches = [];
    [ObservableProperty] private bool hasNameMatches;

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

        // Arrives from App.NavigateToDeepLinkAsync when the invite link itself was tapped (not
        // typed) — prefills the redeem box but still requires the explicit Join tap below, rather
        // than auto-redeeming on navigation.
        if (query.TryGetValue("code", out var codeValue))
            JoinCodeInput = Uri.UnescapeDataString(codeValue?.ToString() ?? "");
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        if (groupId is not { } id) return;
        IsBusy = true;
        try
        {
            var invite = await invitesRepository.CreateAsync(id);
            InviteCode = invite.Token;

            var members = await membersRepository.GetForGroupAsync(id);
            currentGroupMemberIds = members.Select(m => m.Id).ToList();
            PendingInvites = new ObservableCollection<PendingInviteItem>(
                members.Where(m => m.IsPhantom)
                       .Select(m => new PendingInviteItem { Member = m, Initials = Initials(m.DisplayName) }));
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task CopyLink() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrEmpty(InviteCode)) return;
        await Clipboard.Default.SetTextAsync(AppConstants.Links.BuildInviteUrl(InviteCode));
        await TryShowToast(LocalizationResourceManager.Instance["JoinGroup_LinkCopied"]);
    });

    /// <summary>AX-07: on this unpackaged Win32 build, Toast.Show throws COMException 0x80070490
    /// (AppNotificationManager isn't registered) — confirmed live, crashing the whole app when
    /// nothing catches it (see Resend's history before this fix). Swallowed here rather than
    /// left to bubble, since a copy/resend/join that already succeeded shouldn't be reported as
    /// failed, or crash the app outright, just because the confirmation toast couldn't show.</summary>
    private static async Task TryShowToast(string message)
    {
        try
        {
            await Toast.Make(message).Show(CancellationToken.None);
        }
        catch
        {
            // best-effort confirmation only; see remarks above.
        }
    }

    [RelayCommand]
    private Task Share() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrEmpty(InviteCode)) return;
        await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = LocalizationResourceManager.Instance.Format(
                "JoinGroup_ShareText", GroupName, AppConstants.Links.BuildInviteUrl(InviteCode)),
            Title = LocalizationResourceManager.Instance["JoinGroup_ShareTitle"]
        });
    });

    private int searchGeneration;

    /// <summary>Surfaces "this might already exist" suggestions as the name is typed, scoped by
    /// RLS to members the current account can already see (shared groups, or created by them).
    /// A stale response is dropped via the generation counter if the text changes again before
    /// the search returns.</summary>
    partial void OnNewPhantomNameChanged(string value)
    {
        var generation = ++searchGeneration;
        _ = RunSafeAsync(() => SearchAsync(value, generation));
    }

    private async Task SearchAsync(string query, int generation)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            NameMatches = [];
            HasNameMatches = false;
            return;
        }

        var matches = await membersRepository.SearchVisibleByNameAsync(query.Trim());
        if (generation != searchGeneration) return; // a newer keystroke already superseded this

        NameMatches = new ObservableCollection<MemberMatchItem>(
            matches.Where(m => !currentGroupMemberIds.Contains(m.Id))
                   .Select(m => new MemberMatchItem { Member = m, Initials = Initials(m.DisplayName) }));
        HasNameMatches = NameMatches.Count > 0;
    }

    /// <summary>Adds a phantom (name-only) member directly to the active group, then mints an
    /// invite targeting them so they show up in Pending invites for a real person to redeem later.
    /// Only reached once the user has confirmed this isn't one of the NameMatches suggestions.</summary>
    [RelayCommand]
    private Task AddPhantomMember() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewPhantomName) || groupId is not { } id) return;
        IsBusy = true;
        try
        {
            var member = await membersRepository.AddPhantomAsync(NewPhantomName.Trim());
            await membersRepository.AddToGroupAsync(id, member.Id);
            await invitesRepository.CreateAsync(id, member.Id);
            NewPhantomName = "";
            NameMatches = [];
            HasNameMatches = false;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Links an existing phantom member (found via NameMatches) into this group instead
    /// of creating a duplicate phantom row for the same person. A claimed (real-account) match is
    /// never passed here — the UI only offers this action for phantom suggestions, since a real
    /// account must join by redeeming an invite itself, never be added on someone else's behalf.</summary>
    [RelayCommand]
    private Task LinkExistingMember(MemberMatchItem? item) => RunSafeAsync(async () =>
    {
        if (item is null || !item.Member.IsPhantom || groupId is not { } id) return;
        IsBusy = true;
        try
        {
            await membersRepository.AddToGroupAsync(id, item.Member.Id);
            NewPhantomName = "";
            NameMatches = [];
            HasNameMatches = false;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task Resend(PendingInviteItem? item) => RunSafeAsync(async () =>
    {
        if (item is null || groupId is not { } id) return;
        var invite = await invitesRepository.CreateAsync(id, item.Member.Id);
        await Clipboard.Default.SetTextAsync(AppConstants.Links.BuildInviteUrl(invite.Token));
        await TryShowToast(LocalizationResourceManager.Instance.Format("JoinGroup_NewLinkCopied", item.Member.DisplayName));
    });

    [RelayCommand]
    private Task JoinByCode() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(JoinCodeInput)) return;
        IsBusy = true;
        try
        {
            var trimmed = JoinCodeInput.Trim();
            var code = AppConstants.Links.TryExtractCode(trimmed) ?? trimmed;
            var joinedGroupId = await invitesRepository.RedeemAsync(code);
            await TryShowToast(LocalizationResourceManager.Instance["JoinGroup_Joined"]);
            await Shell.Current.GoToAsync($"{AppConstants.Routes.GroupDetails}?groupId={joinedGroupId}");
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
}
