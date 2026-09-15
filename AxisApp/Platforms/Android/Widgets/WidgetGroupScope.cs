using Android.Content;

namespace AxisApp.Widgets;

/// <summary>Per-placed-widget "which group" choice, set once by WidgetConfigActivity when the
/// widget is dropped on the home screen and read on every render. Keyed by the widget's own
/// appWidgetId (not by provider/group) so the same Balances or Events widget can be placed
/// multiple times, each scoped independently — one per group, or "All groups" (Guid stored as
/// null). A single SharedPreferences file covers both widget types since appWidgetId is globally
/// unique across all of an app's widgets.</summary>
internal static class WidgetGroupScope
{
    private const string PrefsName = "axis_widget_scope";

    public static Guid? Get(Context context, int appWidgetId)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        var raw = prefs.GetString(Key(appWidgetId), null);
        return Guid.TryParse(raw, out var groupId) ? groupId : null;
    }

    public static void Set(Context context, int appWidgetId, Guid? groupId)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        using var editor = prefs.Edit()!;
        if (groupId is null)
            editor.PutString(Key(appWidgetId), null);
        else
            editor.PutString(Key(appWidgetId), groupId.Value.ToString());
        editor.Apply();
    }

    public static void Clear(Context context, int appWidgetId)
    {
        var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private)!;
        using var editor = prefs.Edit()!;
        editor.Remove(Key(appWidgetId));
        editor.Apply();
    }

    private static string Key(int appWidgetId) => $"scope_{appWidgetId}";
}
