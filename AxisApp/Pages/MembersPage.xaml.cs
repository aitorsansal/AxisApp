using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class MembersPage : ContentPage
{
    public MembersPage(MembersViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
