using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class RecurringExpensesPage : ContentPage
{
    public RecurringExpensesPage(RecurringExpensesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
