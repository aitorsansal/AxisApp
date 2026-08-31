using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel vm;

    public ProfilePage(ProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = this.vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = vm.LoadAsync();
    }
}
