using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxisApp.Localization;
using AxisApp.Services;

namespace AxisApp.ViewModels;

/// <summary>Dedicated email/password sign-up form — collects DisplayName/Birthday up front
/// instead of leaving them for a later Profile visit. Google sign-up stays on LoginPage
/// unchanged; there's nothing to collect there since Google already supplies name/photo.</summary>
public partial class RegisterViewModel : BaseViewModel
{
    private readonly IAuthService authService;
    private readonly IMembersRepository membersRepository;

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPasswordVisible;
    [ObservableProperty] private bool isConfirmPasswordVisible;
    [ObservableProperty] private bool hasBirthday;
    [ObservableProperty] private DateTime birthday = DateTime.Today.AddYears(-25);
    [ObservableProperty] private string birthdayDisplay = string.Empty;

    public RegisterViewModel(IAuthService authService, IMembersRepository membersRepository)
    {
        this.authService = authService;
        this.membersRepository = membersRepository;
        RefreshBirthdayDisplay();
    }

    private void RefreshBirthdayDisplay() =>
        BirthdayDisplay = HasBirthday
            ? Birthday.ToString("d", CultureInfo.CurrentUICulture)
            : LocalizationResourceManager.Instance["Profile_BirthdayNotSet"];

    partial void OnBirthdayChanged(DateTime value) => RefreshBirthdayDisplay();

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private void ToggleConfirmPasswordVisibility() => IsConfirmPasswordVisible = !IsConfirmPasswordVisible;

    [RelayCommand]
    private void SetBirthday()
    {
        HasBirthday = true;
        RefreshBirthdayDisplay();
    }

    [RelayCommand]
    private void ClearBirthday()
    {
        HasBirthday = false;
        RefreshBirthdayDisplay();
    }

    [RelayCommand]
    private Task Register() => RunSafeAsync(async () =>
    {
        if (IsBusy) return;

        var trimmedEmail = Email.Trim();
        if (string.IsNullOrEmpty(trimmedEmail) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = LocalizationResourceManager.Instance["Register_MissingRequiredFields"];
            return;
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = LocalizationResourceManager.Instance["Register_PasswordMismatch"];
            return;
        }

        IsBusy = true;
        try
        {
            var result = await authService.SignUpAsync(trimmedEmail, Password);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? LocalizationResourceManager.Instance["Login_SignUpFailed"];
                return;
            }

            await TryApplyProfileDetailsAsync();

            // No separate "create your profile" step needed beyond this: a group-scoped Member
            // row only exists once this account creates or joins a group, both reachable from
            // the (empty) Groups list — same as LoginViewModel's plain-signup path used to do.
            await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Best-effort: the member row is provisioned server-side by a DB trigger on
    /// account creation (handle_new_user_member(), defaulted to the account's email), so it
    /// should already exist by the time SignUpAsync returns. If DisplayName/Birthday were left
    /// blank, or this lookup/update fails for any reason, don't block account creation on it —
    /// the fields stay editable later from ProfilePage regardless.</summary>
    private async Task TryApplyProfileDetailsAsync()
    {
        var trimmedName = DisplayName.Trim();
        if (string.IsNullOrEmpty(trimmedName) && !HasBirthday) return;

        try
        {
            var myMember = await membersRepository.GetMyMemberAsync();
            if (myMember is null) return;

            if (!string.IsNullOrEmpty(trimmedName)) myMember.DisplayName = trimmedName;
            myMember.BirthDate = HasBirthday ? Birthday.Date : null;
            await membersRepository.UpdateAsync(myMember);
        }
        catch
        {
            // Swallow — see remarks above, this is a nice-to-have, not required for signup.
        }
    }
}
