using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class GroupsPage : ContentPage
{
    private readonly GroupsViewModel vm;

    public GroupsPage(GroupsViewModel vm)
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
