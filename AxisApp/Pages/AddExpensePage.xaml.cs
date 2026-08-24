using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class AddExpensePage : ContentPage
{
    public AddExpensePage(AddExpenseViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
