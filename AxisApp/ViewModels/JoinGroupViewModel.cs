using AxisApp.Localization;
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

    public JoinGroupViewModel(IInvitesRepository invitesRepository)
    {
        this.invitesRepository = invitesRepository;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("code", out var codeValue))
            JoinCodeInput = Uri.UnescapeDataString(codeValue?.ToString() ?? "");
    }

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
