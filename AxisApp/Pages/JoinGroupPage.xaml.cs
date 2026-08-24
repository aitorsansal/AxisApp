using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class JoinGroupPage : ContentPage
{
    public JoinGroupPage(JoinGroupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackTapped(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
