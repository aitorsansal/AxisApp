using System.Globalization;

namespace AxisApp.Models;

public static class EventTimes
{
    public static bool IsPast(DateTime startsAt, DateTime now) => startsAt.ToUniversalTime() < now.ToUniversalTime();

    public static string FormatRange(DateTime startsAt, DateTime? endsAt) =>
        FormatRange(startsAt, endsAt, CultureInfo.CurrentCulture);

    public static string FormatRange(DateTime startsAt, DateTime? endsAt, CultureInfo culture)
    {
        var fullPattern = $"{DateFormats.ShortDatePattern(culture, includeWeekday: true)} · HH:mm";
        var start = startsAt.ToLocalTime();
        var caption = start.ToString(fullPattern, culture);
        if (endsAt is not { } endValue) return caption;
        var end = endValue.ToLocalTime();
        return $"{caption} – {end.ToString(start.Date == end.Date ? "HH:mm" : fullPattern, culture)}";
    }
}
