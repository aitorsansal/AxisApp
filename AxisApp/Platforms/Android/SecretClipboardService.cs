using Android.Content;
using Android.OS;
using AxisApp.Services;

namespace AxisApp;

/// <summary>Sets ClipDescription.ExtraIsSensitive on the clip so Android 13+ doesn't show the copied
/// text in its clipboard-preview overlay. Older versions ignore the extra, so this is a plain copy
/// there.</summary>
public class SecretClipboardService : ISecretClipboardService
{
    public Task CopyAsync(string text)
    {
        var context = Android.App.Application.Context;
        var manager = (ClipboardManager?)context.GetSystemService(Context.ClipboardService);
        if (manager is null) return Task.CompletedTask;

        var clip = ClipData.NewPlainText("Axis", text);
        if (OperatingSystem.IsAndroidVersionAtLeast(33) && clip?.Description is { } description)
        {
            var extras = new PersistableBundle();
            extras.PutBoolean(ClipDescription.ExtraIsSensitive, true);
            description.Extras = extras;
        }

        manager.PrimaryClip = clip;
        return Task.CompletedTask;
    }
}
