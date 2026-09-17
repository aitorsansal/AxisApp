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

    public RegisterViewModel(IAuthService authService)
    {
        this.authService = authService;
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
            // DisplayName/Birthday go with the sign-up request itself and are applied by
            // handle_new_user_member() when the member row is provisioned — with email
            // confirmation on there's no session yet to update that row from here.
            var result = await authService.SignUpAsync(
                trimmedEmail,
                Password,
                DisplayName,
                HasBirthday ? Birthday.Date : null);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? LocalizationResourceManager.Instance["Login_SignUpFailed"];
                return;
            }

            if (result.NeedsEmailConfirmation)
            {
                var loc = LocalizationResourceManager.Instance;
                await Shell.Current.DisplayAlertAsync(
                    loc["Register_CheckInboxTitle"],
                    loc.Format("Register_CheckInboxMessage", trimmedEmail),
                    loc["Common_OK"]);
                await Shell.Current.GoToAsync("..");
                return;
            }

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
}
