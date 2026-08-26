using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class GroupDetailPage : ContentPage
{
    public GroupDetailPage(GroupDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
