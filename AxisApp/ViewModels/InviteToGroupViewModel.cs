using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QRCoder;

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
/// InviteToGroupViewModel.OnNewPhantomNameChanged. Real-account matches are display-only: this app
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

/// <summary>One entry in the expiry preset picker — see InviteToGroupViewModel.ExpiryOptions.</summary>
public readonly record struct ExpiryOption(string Label, TimeSpan Duration);

/// <summary>
/// Screen 4's "share/create an invite for a specific group" half — always navigated here with a
/// groupId (from MembersPage's overflow menu). Handles the QR/code card (reusing a still-usable
/// invite instead of minting a new one on every visit — see IInvitesRepository.GetOrCreateAsync),
/// its editable max-uses/expiry, and adding phantom members by name. Redeeming someone else's code
/// lives in the separate JoinGroupPage/JoinGroupViewModel.
/// </summary>
public partial class InviteToGroupViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IInvitesRepository invitesRepository;
    private readonly IMembersRepository membersRepository;

    /// <summary>7 days matches the invite's own creation default (SupabaseInvitesRepository) so
    /// the picker's initial selection reflects what a freshly-created invite already got.</summary>
    public static readonly IReadOnlyList<ExpiryOption> ExpiryOptions =
    [
        new(LocalizationResourceManager.Instance["InviteToGroup_Expires1Hour"], TimeSpan.FromHours(1)),
        new(LocalizationResourceManager.Instance["InviteToGroup_Expires1Day"], TimeSpan.FromDays(1)),
        new(LocalizationResourceManager.Instance["InviteToGroup_Expires7Days"], TimeSpan.FromDays(7)),
        new(LocalizationResourceManager.Instance["InviteToGroup_Expires30Days"], TimeSpan.FromDays(30)),
    ];

    private Guid? groupId;
    private Invite? currentInvite;
    private List<Guid> currentGroupMemberIds = [];
    private bool suppressPickerUpdates;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private string inviteCode = "";
    [ObservableProperty] private ImageSource? qrImageSource;
    [ObservableProperty] private int maxUses = 1;
    [ObservableProperty] private int minMaxUses = 1;
    [ObservableProperty] private string usedCountText = "";
    [ObservableProperty] private int selectedExpiryIndex = 2; // 7 days
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string newPhantomName = "";
    [ObservableProperty] private ObservableCollection<MemberMatchItem> nameMatches = [];
    [ObservableProperty] private bool hasNameMatches;
    [ObservableProperty] private ObservableCollection<PendingInviteItem> pendingInvites = [];

    /// <summary>Bound to the expiry Picker's ItemsSource — Picker needs plain strings, ExpiryOptions
    /// carries the TimeSpan each label maps back to.</summary>
    public IReadOnlyList<string> ExpiryLabels { get; } = ExpiryOptions.Select(o => o.Label).ToList();

    public InviteToGroupViewModel(IInvitesRepository invitesRepository, IMembersRepository membersRepository)
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
            _ = LoadAsync();
        }
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        if (groupId is not { } id) return;
        IsBusy = true;
        try
        {
            var invite = await invitesRepository.GetOrCreateAsync(id);
            ApplyInvite(invite);

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

    /// <summary>Mirrors a loaded/updated Invite row into the bindable properties, including
    /// re-rendering the QR image. suppressPickerUpdates guards against the MaxUses/
    /// SelectedExpiryIndex property-changed handlers below re-firing an UpdateAsync call while
    /// this method is itself the one setting those properties from a server response.</summary>
    private void ApplyInvite(Invite invite)
    {
        suppressPickerUpdates = true;
        try
        {
            currentInvite = invite;
            InviteCode = invite.Token;
            // Can't drop max uses below how many redemptions already happened — see the Stepper's
            // Minimum binding in InviteToGroupPage.xaml.
            MinMaxUses = Math.Max(1, invite.UseCount);
            MaxUses = invite.MaxUses;
            UsedCountText = LocalizationResourceManager.Instance.Format("InviteToGroup_UsedCount", invite.UseCount, invite.MaxUses);
            SelectedExpiryIndex = ClosestExpiryIndex(invite.ExpiresAt - DateTime.UtcNow);
            QrImageSource = BuildQrImageSource(AppConstants.Links.BuildInviteUrl(invite.Token));
        }
        finally
        {
            suppressPickerUpdates = false;
        }
    }

    private static int ClosestExpiryIndex(TimeSpan span)
    {
        var closest = 0;
        var closestDiff = TimeSpan.MaxValue;
        for (var i = 0; i < ExpiryOptions.Count; i++)
        {
            var diff = (ExpiryOptions[i].Duration - span).Duration();
            if (diff >= closestDiff) continue;
            closest = i;
            closestDiff = diff;
        }
        return closest;
    }

    private static ImageSource BuildQrImageSource(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        // Explicit byte[] RGBA colors (not System.Drawing.Color) so this stays usable on Android —
        // see AxisApp.csproj's QRCoder remark.
        var bytes = png.GetGraphic(10, [0x0B, 0x12, 0x20, 0xFF], [0xF5, 0xF5, 0xF7, 0xFF]);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private const int MaxUsesCeiling = 20;

    partial void OnMaxUsesChanged(int value)
    {
        DecrementMaxUsesCommand.NotifyCanExecuteChanged();
        IncrementMaxUsesCommand.NotifyCanExecuteChanged();
        _ = PushInviteUpdateAsync();
    }

    partial void OnMinMaxUsesChanged(int value) => DecrementMaxUsesCommand.NotifyCanExecuteChanged();

    partial void OnSelectedExpiryIndexChanged(int value) => _ = PushInviteUpdateAsync();

    /// <summary>Plain +/- buttons instead of a native Stepper — the platform-default up/down
    /// control doesn't match this app's outline-pill button language (see InviteToGroupPage.xaml).</summary>
    [RelayCommand(CanExecute = nameof(CanDecrementMaxUses))]
    private void DecrementMaxUses() => MaxUses--;
    private bool CanDecrementMaxUses() => MaxUses > MinMaxUses;

    [RelayCommand(CanExecute = nameof(CanIncrementMaxUses))]
    private void IncrementMaxUses() => MaxUses++;
    private bool CanIncrementMaxUses() => MaxUses < MaxUsesCeiling;

    /// <summary>Fired by the max-uses +/- buttons / expiry picker. Guarded by suppressPickerUpdates
    /// (set while ApplyInvite is populating these same properties from a server response) and by
    /// currentInvite being set (ApplyInvite always runs first via LoadAsync). Mutates the last
    /// fetched Invite in place rather than building a new one, so CreatedBy/CreatedAt/Token/
    /// TargetMemberId round-trip unchanged — see IInvitesRepository.UpdateAsync's remarks.</summary>
    private Task PushInviteUpdateAsync() => RunSafeAsync(async () =>
    {
        if (suppressPickerUpdates || currentInvite is not { } invite) return;
        invite.MaxUses = MaxUses;
        invite.ExpiresAt = DateTime.UtcNow.Add(ExpiryOptions[SelectedExpiryIndex].Duration);
        var updated = await invitesRepository.UpdateAsync(invite);
        ApplyInvite(updated);
    });

    [RelayCommand]
    private Task CopyLink() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrEmpty(InviteCode)) return;
        await Clipboard.Default.SetTextAsync(AppConstants.Links.BuildInviteUrl(InviteCode));
        await TryShowToast(LocalizationResourceManager.Instance["InviteToGroup_LinkCopied"]);
    });

    /// <summary>AX-07: on this unpackaged Win32 build, Toast.Show throws COMException 0x80070490
    /// (AppNotificationManager isn't registered) — confirmed live, crashing the whole app when
    /// nothing catches it. Swallowed here rather than left to bubble, since a copy/resend that
    /// already succeeded shouldn't be reported as failed, or crash the app outright, just because
    /// the confirmation toast couldn't show.</summary>
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
                "InviteToGroup_ShareText", GroupName, AppConstants.Links.BuildInviteUrl(InviteCode)),
            Title = LocalizationResourceManager.Instance["InviteToGroup_ShareTitle"]
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
            await invitesRepository.GetOrCreateAsync(id, member.Id);
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
        var invite = await invitesRepository.GetOrCreateAsync(id, item.Member.Id);
        await Clipboard.Default.SetTextAsync(AppConstants.Links.BuildInviteUrl(invite.Token));
        await TryShowToast(LocalizationResourceManager.Instance.Format("InviteToGroup_NewLinkCopied", item.Member.DisplayName));
    });

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
