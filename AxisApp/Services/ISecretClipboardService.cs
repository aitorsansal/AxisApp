namespace AxisApp.Services;

/// <summary>Copies text that is a long-lived secret (the calendar feed URL — anyone holding it can read
/// the feed) so the OS treats it as sensitive instead of a plain clipboard entry. Android 13+ hides the
/// clipboard preview toast for it; Windows keeps it out of clipboard history (Win+V) and out of the
/// cloud clipboard. Per-platform implementations live next to the other same-named services in
/// Platforms/Android and Platforms/Windows (SECURITY_AUDIT.md #8).</summary>
public interface ISecretClipboardService
{
    Task CopyAsync(string text);
}
