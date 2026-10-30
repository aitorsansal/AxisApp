using System.Text.RegularExpressions;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>
/// Screen 4's "redeem someone else's invite code" half — reached with no groupId, either from
/// Groups' "join a group" action or from tapping an invite link/deep-link (which arrives with the
/// code prefilled but still requires the explicit Join tap, rather than auto-redeeming on
/// navigation). Sharing/creating an invite for a group you already belong to is the separate
/// InviteToGroupPage/InviteToGroupViewModel.
/// </summary>
public partial class JoinGroupViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IInvitesRepository invitesRepository;

    [ObservableProperty] private string joinCodeInput = "";
    [ObservableProperty] private bool isBusy;

    // Preview card (preview_invite). A failed or missing preview leaves all of this empty and
    // Join enabled; redeem_invite then reports its own error.
    [ObservableProperty] private bool hasPreviewCard;
    [ObservableProperty] private string previewTitle = "";
    [ObservableProperty] private string previewDetail = "";
    [ObservableProperty] private string previewNote = "";
    [ObservableProperty] private bool hasPreviewNote;
    [ObservableProperty] private bool hasBlockedMessage;
    [ObservableProperty] private string blockedMessage = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanJoin))] private bool isBlocked;
    [ObservableProperty] private string joinButtonText = LocalizationResourceManager.Instance["Common_Join"];

    public bool CanJoin => !IsBlocked;

    private InvitePreview? preview;
    private int previewGeneration;

    public JoinGroupViewModel(IInvitesRepository invitesRepository)
    {
        this.invitesRepository = invitesRepository;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("code", out var codeValue))
            JoinCodeInput = Uri.UnescapeDataString(codeValue?.ToString() ?? "");
    }

    private static string ExtractCode(string input)
    {
        var trimmed = input.Trim();
        return AppConstants.Links.TryExtractCode(trimmed) ?? trimmed;
    }

    /// <summary>Debounced like the phantom-name search: the generation counter drops a response
    /// the person has already typed past.</summary>
    partial void OnJoinCodeInputChanged(string value)
    {
        var generation = ++previewGeneration;
        ApplyPreview(null);
        _ = LoadPreviewAsync(ExtractCode(value), generation);
    }

    private async Task LoadPreviewAsync(string code, int generation)
    {
        // A real token is 12 base64url characters; don't hit the server for partial input.
        if (!Regex.IsMatch(code, "^[A-Za-z0-9_-]{8,}$")) return;
        await Task.Delay(400);
        if (generation != previewGeneration) return;
        var result = await invitesRepository.PreviewAsync(code);
        if (generation != previewGeneration) return;
        ApplyPreview(result);
    }

    private void ApplyPreview(InvitePreview? result)
    {
        preview = result;
        var loc = LocalizationResourceManager.Instance;
        HasPreviewCard = result is { IsJoinable: true } && result.Status == InvitePreview.Ok;
        IsBlocked = result is { IsJoinable: false };
        HasBlockedMessage = IsBlocked;
        BlockedMessage = result is { IsJoinable: false } ? loc[InvitePreview.ErrorKey(result.Status)] : "";
        JoinButtonText = result?.Status == InvitePreview.AlreadyMember ? loc["JoinGroup_OpenGroup"] : loc["Common_Join"];

        if (result?.Status == InvitePreview.AlreadyMember)
        {
            HasPreviewCard = true;
            PreviewTitle = loc.Format("JoinGroup_PreviewAlready", result.GroupName ?? "");
            PreviewDetail = "";
            PreviewNote = "";
        }
        else if (HasPreviewCard && result is not null)
        {
            PreviewTitle = result.TargetName is { } target
                ? loc.Format("JoinGroup_PreviewClaim", target, result.GroupName ?? "")
                : loc.Format("JoinGroup_PreviewJoin", result.GroupName ?? "");
            PreviewDetail = loc.Format("JoinGroup_PreviewMeta", result.MemberCount, result.GroupCurrency ?? "");
            PreviewNote = result.TargetName is null ? "" : loc["JoinGroup_PreviewClaimNote"];
        }
        else
        {
            PreviewTitle = "";
            PreviewDetail = "";
            PreviewNote = "";
        }
        HasPreviewNote = PreviewNote.Length > 0;
    }

    [RelayCommand]
    private Task JoinByCode() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(JoinCodeInput)) return;
        if (preview is { IsJoinable: false }) return;
        IsBusy = true;
        try
        {
            var code = ExtractCode(JoinCodeInput);
            Guid joinedGroupId;
            try
            {
                joinedGroupId = await invitesRepository.RedeemAsync(code);
            }
            catch (Exception ex) when (InvitePreview.StatusFromRedeemMessage(ex.Message) is not null)
            {
                ErrorMessage = LocalizationResourceManager.Instance[
                    InvitePreview.ErrorKey(InvitePreview.StatusFromRedeemMessage(ex.Message)!)];
                return;
            }
            await TryShowToast(LocalizationResourceManager.Instance["JoinGroup_Joined"]);
            await Shell.Current.GoToAsync($"{AppConstants.Routes.GroupDetails}?groupId={joinedGroupId}");
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>AX-07: on this unpackaged Win32 build, Toast.Show throws COMException 0x80070490
    /// (AppNotificationManager isn't registered) — confirmed live, crashing the whole app when
    /// nothing catches it. Swallowed here rather than left to bubble, since a join that already
    /// succeeded shouldn't be reported as failed, or crash the app outright, just because the
    /// confirmation toast couldn't show.</summary>
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
}
