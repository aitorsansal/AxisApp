using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class GroupDetailPage : ContentPage
{
    public GroupDetailPage(GroupDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackTapped(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
