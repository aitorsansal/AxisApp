using System.Globalization;

namespace AxisApp.Models;

/// <summary>Short date patterns that follow the culture's day/month order. A fixed "MMM d, yyyy"
/// rendered Spanish as "sept 8, 2026" (English order, Spanish names); this gives "8 sept 2026" for
/// Spanish and "Sep 8, 2026" for English.</summary>
public static class DateFormats
{
    public static string ShortDate(DateTime value, bool includeYear = true, bool includeWeekday = false) =>
        value.ToString(ShortDatePattern(CultureInfo.CurrentCulture, includeYear, includeWeekday), CultureInfo.CurrentCulture);

    public static string ShortDatePattern(CultureInfo culture, bool includeYear = true, bool includeWeekday = false)
    {
        var dayFirst = culture.DateTimeFormat.MonthDayPattern.TrimStart('\'').StartsWith('d');
        var pattern = (dayFirst, includeYear) switch
        {
            (true, true) => "d MMM yyyy",
            (true, false) => "d MMM",
            (false, true) => "MMM d, yyyy",
            (false, false) => "MMM d",
        };
        return includeWeekday ? $"ddd, {pattern}" : pattern;
    }
}
