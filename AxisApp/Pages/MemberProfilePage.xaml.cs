using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class MemberProfilePage : ContentPage
{
    public MemberProfilePage(MemberProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
