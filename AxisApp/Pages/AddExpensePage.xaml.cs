using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class AddExpensePage : ContentPage
{
    public AddExpensePage(AddExpenseViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnAmountUnfocused(object? sender, FocusEventArgs e)
    {
        if (BindingContext is AddExpenseViewModel vm) vm.ValidateAmount();
    }

    private void OnShareUnfocused(object? sender, FocusEventArgs e)
    {
        if (sender is Entry { BindingContext: ExpenseParticipant participant }) participant.ValidateAmount();
    }
}
