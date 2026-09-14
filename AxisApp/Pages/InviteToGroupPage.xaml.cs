using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class InviteToGroupPage : ContentPage
{
    public InviteToGroupPage(InviteToGroupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
