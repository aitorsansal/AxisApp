using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class AddEventPage : GuardedEditorPage
{
    public AddEventPage(AddEventViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
