using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class AddEventPage : ContentPage
{
    public AddEventPage(AddEventViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
