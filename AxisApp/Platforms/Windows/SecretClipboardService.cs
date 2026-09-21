using AxisApp.Services;
using WinDataTransfer = Windows.ApplicationModel.DataTransfer;

namespace AxisApp;

/// <summary>SetContentWithOptions with history and roaming both off, so the text never lands in the
/// Win+V clipboard history or syncs to the user's other devices. Has to run on the UI thread, which
/// is where the command that calls it already is. Aliased rather than a plain using: MAUI's own
/// Clipboard and DataPackage types are in scope via global usings and make the names ambiguous.</summary>
public class SecretClipboardService : ISecretClipboardService
{
    public Task CopyAsync(string text)
    {
        var package = new WinDataTransfer.DataPackage();
        package.SetText(text);
        WinDataTransfer.Clipboard.SetContentWithOptions(package, new WinDataTransfer.ClipboardContentOptions
        {
            IsAllowedInHistory = false,
            IsRoamable = false
        });
        return Task.CompletedTask;
    }
}
