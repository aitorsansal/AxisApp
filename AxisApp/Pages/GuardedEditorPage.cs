using AxisApp.Localization;
using AxisApp.ViewModels;

namespace AxisApp.Pages;

public class GuardedEditorPage : ContentPage
{
    private Shell? navigationShell;
    private bool confirmingLeave;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (navigationShell is not null) navigationShell.Navigating -= OnNavigating;
        navigationShell = Shell.Current;
        if (navigationShell is not null) navigationShell.Navigating += OnNavigating;
    }

    protected override void OnDisappearing()
    {
        if (navigationShell is not null) navigationShell.Navigating -= OnNavigating;
        navigationShell = null;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() => confirmingLeave || base.OnBackButtonPressed();

    private async void OnNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (!e.CanCancel || navigationShell?.CurrentPage != this
            || BindingContext is not IUnsavedChangesEditor editor || !editor.ShouldConfirmLeaving) return;
        if (confirmingLeave) { e.Cancel(); return; }
        var loc = LocalizationResourceManager.Instance;
        if (editor.HasPendingEdits)
        {
            e.Cancel();
            confirmingLeave = true;
            try { await DisplayAlertAsync(loc["UnsavedChanges_Title"], loc["UnsavedChanges_Pending"], loc["UnsavedChanges_KeepEditing"]); }
            catch { editor.ErrorMessage = loc["Common_Error"]; }
            finally { confirmingLeave = false; }
            return;
        }
        var deferral = e.GetDeferral();
        confirmingLeave = true;
        try
        {
            if (!await DisplayAlertAsync(loc["UnsavedChanges_Title"], loc["UnsavedChanges_Message"],
                loc["UnsavedChanges_Discard"], loc["UnsavedChanges_KeepEditing"])) e.Cancel();
        }
        catch { e.Cancel(); editor.ErrorMessage = loc["Common_Error"]; }
        finally { confirmingLeave = false; deferral.Complete(); }
    }
}
