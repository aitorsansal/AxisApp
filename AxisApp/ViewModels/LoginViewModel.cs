using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AxisApp.Services;

namespace AxisApp.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService authService;

    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = string.Empty;

    public LoginViewModel(IAuthService authService)
    {
        this.authService = authService;
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await authService.SignInAsync(Email, Password);
            if (!result.Success)
                ErrorMessage = result.ErrorMessage ?? "Sign in failed.";
            else
                await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SignUp()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await authService.SignUpAsync(Email, Password);
            if (!result.Success)
                ErrorMessage = result.ErrorMessage ?? "Sign up failed.";
            else
                // No separate "create your profile" step: a Member row only exists once this
                // account creates or joins a group, both reachable from the (empty) Groups list.
                await Shell.Current.GoToAsync(AppConstants.Routes.Groups);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
