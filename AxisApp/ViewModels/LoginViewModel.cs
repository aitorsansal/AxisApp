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

    public LoginViewModel(IAuthService authService)
    {
        this.authService = authService;
    }

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
    private Task SignUp() => RunSafeAsync(async () =>
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await authService.SignUpAsync(Email, Password);
            if (!result.Success)
                ErrorMessage = result.ErrorMessage ?? LocalizationResourceManager.Instance["Login_SignUpFailed"];
            else
                // No separate "create your profile" step: a Member row only exists once this
                // account creates or joins a group, both reachable from the (empty) Groups list.
                await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
        }
        finally
        {
            IsBusy = false;
        }
    });
}
