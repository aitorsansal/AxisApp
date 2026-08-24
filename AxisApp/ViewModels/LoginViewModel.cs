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
            // TODO: on success, navigate to the Groups tab once it exists.
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
            // TODO: on success, prompt for display name and navigate onward.
        }
        finally
        {
            IsBusy = false;
        }
    }
}
