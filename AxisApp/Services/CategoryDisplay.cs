using AxisApp.Localization;

namespace AxisApp.Services;

/// <summary>Single place resolving an Expense.Category key to its localized display label —
/// pulled out of GroupStatsViewModel once MemberProfileViewModel needed the exact same
/// resolution, same "one place, not copy-pasted per screen" reasoning as MemberDisplay.</summary>
public static class CategoryDisplay
{
    public static string Label(string key)
    {
        var resolvedKey = string.IsNullOrEmpty(key) ? "other" : key;
        return LocalizationResourceManager.Instance[$"Category_{resolvedKey}"];
    }
}
