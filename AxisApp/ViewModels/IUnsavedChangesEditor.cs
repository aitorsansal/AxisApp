namespace AxisApp.ViewModels;

public interface IUnsavedChangesEditor
{
    bool ShouldConfirmLeaving { get; }
    bool HasPendingEdits { get; }
    string ErrorMessage { get; set; }
}
