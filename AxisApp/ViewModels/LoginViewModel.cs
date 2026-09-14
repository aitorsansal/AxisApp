using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxisApp.Localization;
using AxisApp.Services;

namespace AxisApp.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private readonly IAuthService authService;

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isPasswordVisible;

    public LoginViewModel(IAuthService authService)
    {
        this.authService = authService;
    }

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private Task SignIn() => RunSafeAsync(async () =>
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await authService.SignInAsync(Email, Password);
            if (!result.Success)
                ErrorMessage = result.ErrorMessage ?? LocalizationResourceManager.Instance["Login_SignInFailed"];
            else
                await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task LoginWithGoogle() => RunSafeAsync(async () =>
    {
        if (IsBusy) return;

        StatusMessage = "";
        IsBusy = true;
        try
        {
            var result = await authService.SignInWithGoogleAsync();
            if (result.Success)
                await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
            else if (result.ErrorMessage is not null)
                // A null ErrorMessage means the user cancelled (dismissed the account picker /
                // closed the browser tab) — see IAuthService.SignInWithGoogleAsync's remarks.
                // Stay silent rather than show a "sign-in failed" message for that.
                ErrorMessage = result.ErrorMessage;
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Reuses the Email field already on this page rather than a separate prompt —
    /// avoids Shell.Current.DisplayPromptAsync, which is a known WinUI fail-fast crash on Windows
    /// (see MembersPage's rename overlay for the same avoidance).</summary>
    [RelayCommand]
    private Task ForgotPassword() => RunSafeAsync(async () =>
    {
        if (IsBusy) return;

        var trimmed = Email.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ErrorMessage = LocalizationResourceManager.Instance["Login_ForgotPasswordNeedsEmail"];
            return;
        }

        StatusMessage = "";
        IsBusy = true;
        try
        {
            var result = await authService.ForgotPasswordAsync(trimmed);
            if (!result.Success)
                ErrorMessage = result.ErrorMessage ?? LocalizationResourceManager.Instance["Login_ForgotPasswordFailed"];
            else
                StatusMessage = LocalizationResourceManager.Instance["Login_ForgotPasswordSent"];
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task GoToRegister() => Shell.Current.GoToAsync(AppConstants.Routes.Register);
}
