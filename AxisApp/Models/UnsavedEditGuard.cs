namespace AxisApp.Models;

public sealed class UnsavedEditGuard
{
    private string? baseline;
    private bool committed;

    public void Reset() { baseline = null; committed = false; }
    public void Capture(string snapshot) { baseline = snapshot; committed = false; }
    public bool HasChanges(string snapshot) => !committed && baseline is not null && baseline != snapshot;
    public bool ShouldBlock(string snapshot, bool pending) => !committed && baseline is not null && (pending || HasChanges(snapshot));
    public void MarkCommitted() => committed = true;
}
