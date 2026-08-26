using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class NewGroupPage : ContentPage
{
    public NewGroupPage(NewGroupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        GroupNameEntry.Focus();
    }
}
