using AxisApp.ViewModels;
using AxisApp.Localization;

namespace AxisApp.Pages;

public partial class AddExpensePage : ContentPage
{
    public AddExpensePage(AddExpenseViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private Shell? navigationShell;
    private bool confirmingLeave;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (navigationShell is not null) navigationShell.Navigating -= OnShellNavigating;
        navigationShell = Shell.Current;
        if (navigationShell is not null) navigationShell.Navigating += OnShellNavigating;
    }

    protected override void OnDisappearing()
    {
        if (navigationShell is not null) navigationShell.Navigating -= OnShellNavigating;
        navigationShell = null;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() => confirmingLeave || base.OnBackButtonPressed();

    private async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (!e.CanCancel || navigationShell?.CurrentPage != this
            || BindingContext is not AddExpenseViewModel vm || !vm.ShouldConfirmLeaving) return;
        if (confirmingLeave) { e.Cancel(); return; }
        var loc = LocalizationResourceManager.Instance;
        if (vm.HasPendingEdits)
        {
            e.Cancel();
            confirmingLeave = true;
            try
            {
                await DisplayAlertAsync(loc["UnsavedChanges_Title"], loc["UnsavedChanges_Pending"], loc["UnsavedChanges_KeepEditing"]);
            }
            catch { vm.ErrorMessage = loc["Common_Error"]; }
            finally { confirmingLeave = false; }
            return;
        }

        var deferral = e.GetDeferral();
        confirmingLeave = true;
        try
        {
            var discard = await DisplayAlertAsync(loc["UnsavedChanges_Title"], loc["UnsavedChanges_Message"],
                loc["UnsavedChanges_Discard"], loc["UnsavedChanges_KeepEditing"]);
            if (!discard) e.Cancel();
        }
        catch
        {
            e.Cancel();
            vm.ErrorMessage = loc["Common_Error"];
        }
        finally
        {
            confirmingLeave = false;
            deferral.Complete();
        }
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
